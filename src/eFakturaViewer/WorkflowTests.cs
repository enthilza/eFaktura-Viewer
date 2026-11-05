using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace eFakturaViewer;

internal static class WorkflowTests
{
    sealed class ImmediateProgress(Action<int> action) : IProgress<int> { public void Report(int value) => action(value); }
    internal static async Task Run(string output, string examples, List<string> checks, bool canRender)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        void Reject(Action action, string name) { try { action(); } catch { checks.Add(name); return; } throw new Exception("Neodmietnute: " + name); }
        var parent = Preferences.TestDirectory;
        // Presuny testujeme v novom pracovnom priecinku na kopiach syntetickych
        // prikladov. Nikdy neposielame zdrojovy examples ani pouzivatelsky profil do movera.
        var root = Path.Combine(output, "workflow-" + Guid.NewGuid().ToString("N"));
        Preferences.TestDirectory = Path.Combine(root, "profile");
        var windows = new List<Window>();
        string Dir(string name) { var path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
        var bytes = File.ReadAllBytes(Path.Combine(examples, "01_zaklad.xml"));
        var extended = File.ReadAllBytes(Path.Combine(examples, "02_fmcg.xml"));
        var credit = File.ReadAllBytes(Path.Combine(examples, "04_dobropis.xml"));
        string Put(string dir, string name, byte[]? data = null) { var path = Path.Combine(dir, name); File.WriteAllBytes(path, data ?? bytes); return path; }
        MoveCandidate Candidate(string source, string target) => new(source, target, WorkflowStore.Fingerprint(File.ReadAllBytes(source)));
        try
        {
            new Preferences { CheckUpdates = false }.Save();
            string source = Dir("vstup"), target1 = Dir("Účtovníctvo"), target2 = Dir("Schvaľovanie");
            var folders = new[] { new WorkflowFolder(Guid.NewGuid(), "Účtovníctvo", target1), new WorkflowFolder(Guid.NewGuid(), "Schvaľovanie", target2) };
            string one = Put(source, "faktura.xml"), two = Put(source, "druha.XML", extended), three = Put(source, "dobropis.xml", credit);
            var unrelated = Put(source, "nastavenia.xml", Encoding.UTF8.GetBytes("<settings/>"));
            var text = Put(source, "poznamky.txt", Encoding.UTF8.GetBytes("zachovať"));
            var nested = Put(Dir("vstup/podpriecinok"), "dalsia.xml");
            var state = WorkflowStore.SetFolders(folders);
            Check(WorkflowStore.Selected(state, one, bytes)?.Id == folders[0].Id, "Nepriradena faktura ma prvy ciel");
            state = WorkflowStore.Assign(two, extended, folders[1].Id);
            var preferences = Preferences.Load(); preferences.Light = false; preferences.Save();
            Check(WorkflowStore.Load().Assignments.Count == 1 && WorkflowStore.Selected(WorkflowStore.Load(), two, extended)?.Id == folders[1].Id, "Priradenie prezije nacitanie profilu aj ulozenie temy");
            state = WorkflowStore.SetFolders(folders.Reverse());
            Check(WorkflowStore.Selected(state, two, extended)?.Id == folders[1].Id && WorkflowStore.Selected(state, one, bytes)?.Id == folders[1].Id, "Zmena poradia zachova vybrany ciel a zmeni iba predvolbu");
            state = WorkflowStore.SetFolders(folders);
            Reject(() => WorkflowStore.SetFolders([folders[0], folders[1] with { Path = target1 }]), "Duplicitna cesta sa neulozi");
            Reject(() => WorkflowStore.SetFolders([folders[0], folders[1] with { Id = folders[0].Id }]), "Duplicitna identita sa neulozi");
            Reject(() => WorkflowStore.SetFolders([folders[0] with { Path = "relativny" }]), "Relativny ciel sa neulozi");
            var occupied = Put(target1, "faktura.xml", Encoding.UTF8.GetBytes("povodny ciel"));
            Directory.CreateDirectory(Path.Combine(target1, "faktura (2).xml"));
            var report = InvoiceMover.Run(source, state, null, CancellationToken.None);
            Check(report.Problems.Count == 0 && report.Moved.Count == 3 && report.OtherXml == 1, "Davka rozpozna vsetky faktury vratane neprezreteho dobropisu");
            Check(!File.Exists(one) && !File.Exists(two) && !File.Exists(three) && File.ReadAllBytes(Path.Combine(target1, "faktura (3).xml")).SequenceEqual(bytes) && File.ReadAllBytes(Path.Combine(target2, "druha.XML")).SequenceEqual(extended), "Presun ide do spravnych cielov a hlada volny nazov bez prepisu");
            Check(File.ReadAllText(occupied) == "povodny ciel" && File.Exists(unrelated) && File.Exists(text) && File.Exists(nested), "Kolizia ani hromadny presun nemenia ine subory a podpriecinky");
            WorkflowStore.RemoveMoved(report.Moved);
            Check(WorkflowStore.Load().Assignments.Count == 0, "Priradenia uspesne presunutych faktur sa vycistia");

            var guarded = Dir("kontroly");
            var changed = Put(guarded, "zmenena.xml");
            WorkflowStore.Assign(changed, bytes, folders[1].Id); File.AppendAllText(changed, "\n");
            report = InvoiceMover.Run(guarded, WorkflowStore.Load(), null, CancellationToken.None);
            Check(report.Moved.Count == 0 && report.Problems.Count == 1 && File.Exists(changed), "Zmeneny obsah po priradeni sa nepresunie do povodneho ani predvoleneho ciela");
            WorkflowStore.Assign(changed, File.ReadAllBytes(changed), folders[1].Id);
            report = InvoiceMover.Run(guarded, WorkflowStore.Load(), null, CancellationToken.None);
            Check(report.Moved.Count == 1, "Opatovne vedome priradenie povoli zmeneny doklad");
            var race = Put(guarded, "po-kontrole.xml"); var stale = Candidate(race, target1); File.AppendAllText(race, "\n");
            Reject(() => InvoiceMover.MoveOne(stale, CancellationToken.None), "Zmena obsahu medzi planom a presunom je odmietnuta");
            Check(File.Exists(race) && !File.Exists(Path.Combine(target1, "po-kontrole.xml")), "Po odmietnuti zmeny ostal zdroj bez cielovej kopie");
            Reject(() => InvoiceMover.MoveOne(Candidate(race, Path.Combine(guarded, ".")), CancellationToken.None), "Ciel totozny so zdrojom sa odmietne aj cez alias");
            using (var locked = new FileStream(race, FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(() => InvoiceMover.MoveOne(Candidate(race, target1), CancellationToken.None), "Zamknuty zdroj sa nepresunie");
            File.SetAttributes(race, FileAttributes.ReadOnly);
            try { Reject(() => InvoiceMover.MoveOne(Candidate(race, target1), CancellationToken.None), "Subor iba na citanie zostane na mieste"); }
            finally { File.SetAttributes(race, FileAttributes.Normal); }
            var copied = Put(guarded, "medzi-diskami.xml");
            var copyResult = InvoiceMover.MoveOne(Candidate(copied, target2), CancellationToken.None, forceCopy: true);
            Check(!File.Exists(copied) && File.ReadAllBytes(copyResult.Destination).SequenceEqual(bytes) && Directory.GetFiles(target2, ".efaktura-*.tmp").Length == 0, "Kopirovacia vetva overi obsah pred zmazanim zdroja a uprace pracovny subor");
            // Na vyvojovom PC su vystupy a systemovy TEMP na odlisnych diskoch.
            // Tento pokus overi aj prechod z nativneho rename do kopirovacej vetvy.
            var externalTarget = Path.Combine(Path.GetTempPath(), "efaktura-move-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(externalTarget);
            try
            {
                var crossDrive = Put(guarded, "iny-disk.xml");
                var crossResult = InvoiceMover.MoveOne(Candidate(crossDrive, externalTarget), CancellationToken.None);
                Check(!File.Exists(crossDrive) && File.ReadAllBytes(crossResult.Destination).SequenceEqual(bytes), "Skutocny presun medzi vystupnym priecinkom a systemovym TEMP zachova bajty");
            }
            finally
            {
                // Mazeme iba presne pomenovane testovacie vystupy, bez rekurzie.
                File.Delete(Path.Combine(externalTarget, "iny-disk.xml")); Directory.Delete(externalTarget);
            }
            var cancelDir = Dir("zrusenie"); var cancel1 = Put(cancelDir, "a.xml"); var cancel2 = Put(cancelDir, "b.xml");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); report = InvoiceMover.Run(cancelDir, state, null, cancellation.Token);
                Check(report.Cancelled && report.Moved.Count == 0 && File.Exists(cancel1) && File.Exists(cancel2), "Zrusenie pred zaciatkom nic nepresunie");
            }
            using (var cancellation = new CancellationTokenSource())
            {
                report = InvoiceMover.Run(cancelDir, state, new ImmediateProgress(p => { if (p > 40) cancellation.Cancel(); }), cancellation.Token);
                Check(report.Cancelled && report.Moved.Count == 1 && Directory.GetFiles(cancelDir).Length == 1, "Prerusena davka vrati presny pocet hotovych presunov a zvysok necha na mieste");
            }
            var errors = Dir("chyby"); var broken = Put(errors, "poskodena.xml", Encoding.UTF8.GetBytes("<bad>")); var good = Put(errors, "platna.xml");
            report = InvoiceMover.Run(errors, state, null, CancellationToken.None);
            Check(report.Problems.Count == 1 && report.Moved.Count == 1 && File.Exists(broken) && !File.Exists(good), "Chybne XML neblokuje ostatne faktury a zostane na mieste");

            var uiDir = Dir("okno"); var ui1 = Put(uiDir, "prva.xml"); var ui2 = Put(uiDir, "druha.xml", extended);
            WorkflowStore.SetFolders(folders);
            var window = new ViewerWindow(null); windows.Add(window); window.Show(); await window.OpenPath(ui1);
            Check(window.WorkflowTargetForTest == target1, "Okno zvyrazni predvoleny prvy ciel");
            Check(window.WorkflowButtonsForTest.Take(2).Select(b => ((TextBlock)((StackPanel)b.Content).Children[0]).Text).SequenceEqual(new[] { "1 · Účtovníctvo", "2 · Schvaľovanie" }), "Cielove tlacidla zobrazuju cislo aj priatelsky nazov");
            var keyEvent = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.D2) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            window.HeaderToolsForTest.RaiseEvent(keyEvent);
            Check(keyEvent.Handled && window.WorkflowTargetForTest == target2 && File.Exists(ui1), "Klaves 2 zaradi fakturu bez fyzickeho presunu");
            await window.OpenPath(ui2); Check(window.WorkflowTargetForTest == target1, "Dalsia nepriradena faktura ma predvolbu 1");
            await window.OpenPath(ui1); Check(window.WorkflowTargetForTest == target2, "Listovanie zachova priradenie konkretnej faktury");
            var firstButton = window.WorkflowButtonsForTest.First(); firstButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.WorkflowTargetForTest == target1, "Cislovane tlacidlo meni priradenie rovnako ako klaves");
            window.TestAssignWorkflow(1);
            window.Close(); window = new ViewerWindow(null); windows.Add(window); window.Show(); await window.OpenPath(ui1);
            Check(window.WorkflowTargetForTest == target2, "Nove okno obnovi ciel ulozeny pred zatvorenim");
            var originalWidth = window.Width; window.Width = 1200; window.UpdateLayout();
            var switchRight = window.EmbeddedSwitchForTest.TranslatePoint(new Point(window.EmbeddedSwitchForTest.ActualWidth, window.EmbeddedSwitchForTest.ActualHeight / 2), window);
            var targetLeft = window.WorkflowButtonsForTest[0].TranslatePoint(new Point(0, window.WorkflowButtonsForTest[0].ActualHeight / 2), window);
            Check(targetLeft.X > switchRight.X && Math.Abs(targetLeft.Y - switchRight.Y) < 1, "Cielove tlacidla pokracuju v tom istom riadku hned za prepinacom PDF");
            if (canRender)
            {
                foreach (var light in new[] { true, false })
                {
                    await window.Capture(Path.Combine(output, "workflow-" + (light ? "light" : "dark") + ".png"), light);
                    var dialog = WorkflowDialog.Build(window); windows.Add(dialog); dialog.Show();
                    await dialog.Dispatcher.InvokeAsync(dialog.UpdateLayout, DispatcherPriority.ApplicationIdle);
                    SelfTest.SaveWindow(dialog, Path.Combine(output, "workflow-dialog-" + (light ? "light" : "dark") + ".png"));
                    var edit = Descendants<TextBox>(dialog).First(); edit.Text = "neulozene"; dialog.Close();
                    Check(WorkflowStore.Load().Folders[0].Name == folders[0].Name, "Zatvorenie dialogu bez ulozenia nezmeni ciele " + light);
                }
            }
            window.Width = originalWidth;
            await window.TestMoveInvoices();
            Check(!window.HasPdf && !window.NavigationVisible && window.EmptyTitle == "" && Directory.GetFiles(uiDir).Length == 0 && File.Exists(Path.Combine(target2, "prva.xml")) && File.Exists(Path.Combine(target1, "druha.xml")), "Tlacidlo vykona davku cez modalny priebeh a vyprazdni nahlad");
            var manyFolders = Enumerable.Range(1, 12).Select(i => new WorkflowFolder(Guid.NewGuid(), "Cieľ " + i, Path.Combine(root, "ciel-" + i))).ToArray();
            WorkflowStore.SetFolders(manyFolders); Put(uiDir, "prva.xml"); await window.OpenPath(ui1);
            keyEvent = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, Key.NumPad0) { RoutedEvent = Keyboard.PreviewKeyDownEvent }; window.HeaderToolsForTest.RaiseEvent(keyEvent);
            Check(keyEvent.Handled && window.WorkflowTargetForTest == manyFolders[9].Path && window.WorkflowButtonsForTest.Count == 13, "Desiaty ciel ma klaves 0 a dalsie ciele zostavaju pristupne tlacidlami");
            window.Width = window.MinWidth; window.UpdateLayout();
            Check(window.WorkflowButtonsForTest.All(b => b.TranslatePoint(new Point(b.ActualWidth, 0), window).X <= window.ActualWidth - 12), "Ciele sa zalomia aj v uzkom okne");
            WorkflowStore.SetFolders([]); await window.OpenPath(ui1);
            Check(window.WorkflowButtonsForTest.Count == 0 && window.HasPdf, "Bez cielov funguje povodny prezerac bez panela triedenia");
            using (var key = Registry.CurrentUser.CreateSubKey(Preferences.RegistryPath)) key.SetValue("Workflows", "{pokazene}");
            Reject(() => WorkflowStore.Load(), "Poskodeny profil triedenia sa nezameni za prazdny predvoleny profil");
            await window.OpenPath(ui1); Check(window.HasPdf && window.WorkflowButtonsForTest.Count == 0, "Poskodeny profil triedenia neblokuje prezeranie");
        }
        finally
        {
            foreach (var window in windows.Where(w => w.IsVisible).Reverse().ToArray()) window.Close();
            Preferences.CleanupTestProfile(); Preferences.TestDirectory = parent;
        }
    }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}

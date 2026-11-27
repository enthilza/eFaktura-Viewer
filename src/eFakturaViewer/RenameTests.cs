using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;

namespace eFakturaViewer;

internal static class RenameTests
{
    sealed class Progress(Action<int> action) : IProgress<int> { public void Report(int value) => action(value); }
    internal static async Task Run(string output, string examples, List<string> checks, bool canRender)
    {
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
        void Reject(Action action, string name) { try { action(); } catch (IOException) { checks.Add(name); return; } throw new Exception("Neodmietnute: " + name); }
        var parent = Preferences.TestDirectory;
        var root = Path.Combine(output, "rename-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        Preferences.TestDirectory = Path.Combine(root, "profile");
        var template = Invoice.Load(Path.Combine(examples, "01_zaklad.xml"), CancellationToken.None);
        string Dir(string name) { var path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
        byte[] Xml(string supplier, string issue = "2026-11-27", string? tax = "2026-11-26")
        {
            var doc = new XDocument(template.Xml); var r = doc.Root!;
            r.Element(Invoice.A + "AccountingSupplierParty")?.Remove();
            r.Add(new XElement(Invoice.A + "AccountingSupplierParty", new XElement(Invoice.A + "Party",
                new XElement(Invoice.A + "PartyLegalEntity", new XElement(Invoice.C + "RegistrationName", supplier)))));
            r.SetElementValue(Invoice.C + "IssueDate", issue); r.SetElementValue(Invoice.C + "TaxPointDate", tax);
            return Encoding.UTF8.GetBytes(doc.ToString());
        }
        string Put(string directory, string name, byte[] bytes, int minute = 0)
        {
            var path = Path.Combine(directory, name); File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, 27, 12, minute, 0, DateTimeKind.Utc)); return path;
        }
        RenameReport Run(string dir, CancellationToken token = default, IProgress<int>? progress = null) => InvoiceRenamer.Run(dir, progress, token);
        try
        {
            new Preferences { CheckUpdates = false }.Save();
            var source = Dir("davka"); var a = Xml("Dodávateľ A"); var b = Xml("Dodávateľ B", tax: null);
            var first = Put(source, "z-guid.XML", a, 1); var second = Put(source, "a-guid.xml", a, 2); var third = Put(source, "b-guid.xml", b, 3);
            var keep = Put(source, "Dodávateľ A_2025-02-03_2025-02-01_8.xml", a, 9);
            var other = Put(source, "config.xml", Encoding.UTF8.GetBytes("<settings/>"));
            var text = Put(source, "poznamka.txt", Encoding.UTF8.GetBytes("ponechať"));
            var nested = Put(Dir("davka/podpriecinok"), "vnorena.xml", a);
            var targets = new[] { new WorkflowFolder(Guid.NewGuid(), "Prvý", Dir("ciel1")), new WorkflowFolder(Guid.NewGuid(), "Druhý", Dir("ciel2")) };
            WorkflowStore.SetFolders(targets); WorkflowStore.Assign(first, a, targets[1].Id);
            var report = Run(source);
            Check(report.Problems.Count == 0 && report.Renamed.Count == 3 && report.AlreadyNamed == 1 && report.OtherXml == 1, "Premenuj spracuje iba rozpoznane faktury a preskoci uz pomenovane");
            var firstNew = Path.Combine(source, "Dodávateľ A_2026-11-27_2026-11-26_9.xml");
            var secondNew = Path.Combine(source, "Dodávateľ A_2026-11-27_2026-11-26_10.xml");
            Check(report.Renamed.Single(x => x.Source.Equals(first, StringComparison.OrdinalIgnoreCase)).Destination.Equals(firstNew, StringComparison.OrdinalIgnoreCase) && report.Renamed.Single(x => x.Source.Equals(second, StringComparison.OrdinalIgnoreCase)).Destination.Equals(secondNew, StringComparison.OrdinalIgnoreCase), "Cislovanie pokracuje od maxima a riadi sa casom upravy, nie nazvom");
            Check(File.Exists(Path.Combine(source, "Dodávateľ B_2026-11-27_bez-datumu-ZP_1.xml")), "Kazdy dodavatel zacina od 1 a chyba ZP ma dohodnuty text");
            Check(File.ReadAllBytes(firstNew).SequenceEqual(a) && File.ReadAllBytes(secondNew).SequenceEqual(a) && File.ReadAllBytes(keep).SequenceEqual(a), "Premenovanie nemenilo ani bajt XML vratanie uz pomenovanej faktury");
            Check(File.GetLastWriteTimeUtc(firstNew).Minute == 1 && File.Exists(other) && File.Exists(text) && File.Exists(nested), "Cas upravy, ine subory a podpriecinky zostali zachovane");
            Check(WorkflowStore.Selected(WorkflowStore.Load(), firstNew, a)?.Id == targets[1].Id && !WorkflowStore.Load().Assignments.ContainsKey(first), "Premenovana faktura si zachovala vybrany ciel");
            report = Run(source); Check(report.Renamed.Count == 0 && report.AlreadyNamed == 4 && report.Problems.Count == 0, "Opakovane Premenuj nezmeni uz pomenovane faktury");
            Put(source, "novy-guid.xml", a, 0); report = Run(source);
            Check(report.Renamed.Single().Destination.EndsWith("_11.xml", StringComparison.Ordinal), "Neskor dorucena starsia faktura pokracuje za existujucim maximom");

            var clean = InvoiceRenamer.Supplier(Invoice.Parse(Xml("  Žltý / A\\B: C*D?\"<>|  ")));
            Check(clean.StartsWith("Žltý", StringComparison.Ordinal) && clean.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !clean.EndsWith(' '), "Dodavatel zachova diakritiku a nahradi zakazane znaky");
            Check(InvoiceRenamer.Supplier(Invoice.Parse(Xml(new string('x', 500)))).Length == 120, "Dlhy nazov dodavatela neprekroci rezervovanu dlzku");
            var reserved = Dir("rezervovane-meno"); Put(reserved, "guid.xml", Xml("CON.s.r.o."));
            report = Run(reserved);
            Check(report.Problems.Count == 0 && report.Renamed.Count == 1 && File.Exists(report.Renamed[0].Destination), "Rezervovane zariadenie Windows v nazve dodavatela dostane citatelny nazov suboru");
            var duplicateNames = Dir("zhodne-mena"); Put(duplicateNames, "a.xml", Xml("A/B")); Put(duplicateNames, "b.xml", Xml("A\\B"));
            report = Run(duplicateNames);
            Check(report.Renamed.Count == 2 && report.Renamed.Select(x => x.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2, "Po ocisteni zhodne mena zdielaju cislovanie bez kolizie");

            var collision = Dir("kolizia"); var original = Put(collision, "guid.xml", a);
            var blocked = Put(collision, "Dodávateľ A_2026-11-27_2026-11-26_20.xml", Encoding.UTF8.GetBytes("nedotknut"));
            Directory.CreateDirectory(Path.Combine(collision, "Dodávateľ A_2025-01-01_2025-01-01_21.xml"));
            report = Run(collision);
            Check(report.Renamed.Single().Destination.EndsWith("_22.xml", StringComparison.Ordinal) && File.ReadAllText(blocked) == "nedotknut", "Cisla rezervuju aj necitatelne XML a rovnako pomenovane adresare");
            var race = Put(collision, "race.xml", a); int name = 0, guard = 0;
            var raceDestination = Path.Combine(collision, "raced.xml");
            var result = InvoiceMover.RenameInPlace(race, WorkflowStore.Fingerprint(a), () => ++name == 1 ? "raced.xml" : "safe.xml", () =>
            { if (++guard == 2) File.WriteAllText(raceDestination, "subor vytvoreny pocas operacie"); }, CancellationToken.None);
            Check(File.ReadAllText(raceDestination) == "subor vytvoreny pocas operacie" && File.ReadAllBytes(result.File.Destination).SequenceEqual(a) && name == 2, "Ciel vzniknuty po kontrole existencie sa neprepise a vyberie sa dalsie cislo");

            var invalid = Dir("chyby"); var missing = Put(invalid, "bez-mena.xml", Xml(""));
            var badDate = Put(invalid, "zly-datum.xml", Xml("Firma", "2026-02-30"));
            var readOnly = Put(invalid, "readonly.xml", a); File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            try { report = Run(invalid); Check(report.Renamed.Count == 0 && report.Problems.Count == 3 && File.Exists(missing) && File.Exists(badDate), "Chybajuce udaje a read-only subor sa ponechaju s vysvetlenim"); }
            finally { File.SetAttributes(readOnly, FileAttributes.Normal); }
            var changed = Dir("zmena"); var changedPath = Put(changed, "guid.xml", a);
            report = Run(changed, progress: new Progress(p => { if (p == 40) File.AppendAllText(changedPath, "\n"); }));
            Check(report.Renamed.Count == 0 && report.Problems.Count == 1 && File.Exists(changedPath), "Zmena XML po nacitani metadat zastavi premenovanie tohto suboru");
            var cancelled = Dir("zrusenie"); Put(cancelled, "a.xml", a); Put(cancelled, "b.xml", a);
            using (var cancel = new CancellationTokenSource())
            {
                report = Run(cancelled, cancel.Token, new Progress(p => { if (p > 40) cancel.Cancel(); }));
                Check(report.Cancelled && report.Renamed.Count == 1 && Directory.GetFiles(cancelled, "*.xml").Length == 2, "Zrusenie zastavi dalsie premenovania a hotovy subor nestrati");
            }

            var locking = Dir("zamok"); var lockPath = Path.Combine(locking, DirectoryBatchLock.FileName);
            File.WriteAllText(lockPath, "pozostatok po pade");
            using (var held = DirectoryBatchLock.Acquire(locking))
            {
                Reject(() => { using var otherLock = DirectoryBatchLock.Acquire(locking); }, "Aktivny zamok odmietne druhu instanciu");
                Reject(() => Run(locking), "Premenovanie nemoze prejst cez aktivny zamok");
                Reject(() => InvoiceMover.Run(locking, WorkflowStore.Load(), null, CancellationToken.None), "Rovnaky zamok chrani aj presun pred premenovanim");
                held.EnsureHeld();
            }
            Check(!File.Exists(lockPath), "Pozostatok bez drzaneho handle neblokuje a zavretie odstrani zamok");
            var childInfo = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            childInfo.Environment["EFA_TEST_LOCK"] = lockPath;
            childInfo.Environment["EFA_TEST_READY"] = Path.Combine(locking, "ready");
            const string script = "$ErrorActionPreference='Stop'; $f=[IO.FileStream]::new($env:EFA_TEST_LOCK,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None,1,[IO.FileOptions]::DeleteOnClose); try { [IO.File]::WriteAllText($env:EFA_TEST_READY,'ready'); [Threading.Thread]::Sleep(30000) } finally { $f.Dispose() }";
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) childInfo.ArgumentList.Add(arg);
            using (var child = Process.Start(childInfo)!)
            {
                try
                {
                    var wait = Stopwatch.StartNew();
                    while (!File.Exists(childInfo.Environment["EFA_TEST_READY"]) && !child.HasExited && wait.ElapsedMilliseconds < 10000) await Task.Delay(30);
                    Check(File.Exists(childInfo.Environment["EFA_TEST_READY"]), "Nezavisly proces prevzal vyhradny zamok");
                    Reject(() => Run(locking), "Zamok drzany inym procesom zablokuje davku");
                    child.Kill(); await child.WaitForExitAsync();
                    // Zatvaranie I/O moze kratko dobiehat aj po ukonceni procesu.
                    // Ani test nemaze aktivny zamok: o uvolneni rozhoduje filesystem.
                    DirectoryBatchLock? recovered = null; wait.Restart();
                    while (recovered == null)
                    {
                        try { recovered = DirectoryBatchLock.Acquire(locking); }
                        catch (IOException) when (wait.ElapsedMilliseconds < 5000) { await Task.Delay(50); }
                    }
                    using (recovered) recovered.EnsureHeld();
                    Check(true, "Po nasilenom ukonceni vlastneho testovacieho procesu dalsia instancia ziska zamok");
                }
                finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
            }
            var disposed = DirectoryBatchLock.Acquire(locking); disposed.Dispose();
            Reject(disposed.EnsureHeld, "Strata povodneho handle nepokusi tiche opatovne ziskanie zamku");

            var ui = Dir("okno"); var uiPath = Put(ui, "nahodny-nazov.xml", a);
            WorkflowStore.SetFolders(targets); WorkflowStore.Assign(uiPath, a, targets[1].Id);
            var window = new ViewerWindow(null);
            try
            {
                window.Show(); await window.OpenPath(uiPath);
                Check(window.RenameButtonForTest.IsEnabled && !window.NavigationVisible, "Premenuj je dostupne hore aj pri jedinej fakture");
                await window.TestRenameInvoices();
                Check(window.HasPdf && window.Title.StartsWith("Dodávateľ A_2026-11-27_2026-11-26_1.xml", StringComparison.Ordinal) && window.WorkflowTargetForTest == targets[1].Path, "Po premenovani zostane otvoreny ten isty doklad aj jeho ciel");
                if (canRender)
                {
                    await window.Capture(Path.Combine(output, "rename-light.png"), true);
                    await window.Capture(Path.Combine(output, "rename-dark.png"), false);
                }

                var keyboardDirectory = Dir("klavesnica");
                var keyboardA = XDocument.Parse(Encoding.UTF8.GetString(a));
                keyboardA.Root!.SetElementValue(Invoice.C + "ID", "RENAME-A");
                var keyboardB = new XDocument(keyboardA);
                keyboardB.Root!.SetElementValue(Invoice.C + "ID", "RENAME-B");
                var keyboardPath = Put(keyboardDirectory, "prva.xml", Encoding.UTF8.GetBytes(keyboardA.ToString()), 1);
                Put(keyboardDirectory, "druha.xml", Encoding.UTF8.GetBytes(keyboardB.ToString()), 2);
                await window.OpenPath(keyboardPath);
                Check(window.RenameButtonForTest.Focus() && window.RenameButtonForTest.IsKeyboardFocused,
                    "Pred premenovanim ma klavesnicu skutocne tlacidlo Premenuj");
                await window.TestRenameInvoices();
                await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
                Check(window.IsKeyboardFocusWithin, "Po premenovani sa klavesnica vrati do hlavneho okna bez kliknutia");
                async Task PressFromFocus(Key key, string expected)
                {
                    // Udalost zacina na skutocne fokusovanom prvku. Priamo na okne by test
                    // presiel aj pri strate fokusu, ktoru pouzivatel opravil az kliknutim.
                    var focused = Keyboard.FocusedElement as UIElement;
                    Check(focused != null && Window.GetWindow(focused) == window,
                        $"Klaves {key} ma po premenovani ciel v prezeraci");
                    var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    focused!.RaiseEvent(args);
                    for (int i = 0; i < 500 && (window.OpenedNumber != expected || !window.FirstPageRendered); i++) await Task.Delay(10);
                    Check(args.Handled && window.OpenedNumber == expected && window.FirstPageRendered,
                        $"Klaves {key} po premenovani prepne fakturu bez kliknutia mysou");
                }
                await PressFromFocus(Key.Right, "RENAME-B");
                await PressFromFocus(Key.Left, "RENAME-A");
            }
            finally { window.Close(); }
        }
        finally { Preferences.CleanupTestProfile(); Preferences.TestDirectory = parent; }
    }
}

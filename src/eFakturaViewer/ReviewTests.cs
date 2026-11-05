using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;

namespace eFakturaViewer;

internal static class ReviewTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetShortPathName(string path, StringBuilder result, uint size);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    internal static async Task Run(string output, string examples, List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        void Reject(Action action, string name) { try { action(); } catch { checks.Add(name); return; } throw new Exception(name); }
        Check(Invoice.Money("0.0125") == "0,0125" && Invoice.Money("12.3456") == "12,3456" && Invoice.Money("-001234.50") == "-1\u00a0234,50" && Invoice.Money("1.123456789012345678901234567890") == "1,123456789012345678901234567890", "Ceny zachovaju všetky desatinne miesta");
        Check(new[] { "2026-01-01", "2026-01-01Z", "2026-01-01+14:00", "2026-01-01-12:00" }.All(x => Invoice.Date(x) == "01.01.2026") && Invoice.Date("2026-02-30Z") == "2026-02-30Z", "Dátum sa neposunie podľa casoveho pasma");
        Check(Preferences.DirectoryPath != Preferences.UserDirectoryPath && !Preferences.Load().CheckUpdates, "Testovaci profil je oddeleny este pred vytvorenim okna");
        var original = XDocument.Load(Path.Combine(examples, "01_zaklad.xml"));
        var compactItems = XDocument.Load(Path.Combine(examples, "02_fmcg.xml"));
        var itemTemplate = compactItems.Root!.Element(Invoice.A + "InvoiceLine")!;
        compactItems.Root.Elements(Invoice.A + "InvoiceLine").Remove();
        for (int i = 1; i <= 20; i++)
        {
            var copy = new XElement(itemTemplate); copy.Element(Invoice.C + "ID")!.Value = i.ToString();
            copy.Element(Invoice.A + "Item")!.Element(Invoice.C + "Name")!.Value = "Polozka " + i;
            copy.Element(Invoice.A + "Item")!.Add(new XElement(Invoice.A + "AdditionalItemProperty", new XElement(Invoice.C + "Name", "skryty_doplnok"), new XElement(Invoice.C + "Value", "PODROBNOST-LEN-V-XML")));
            compactItems.Root.Add(copy);
        }
        var compactInvoice = Invoice.Parse(Encoding.UTF8.GetBytes(compactItems.ToString()));
        var compactPdf = await Task.Run(() => InvoicePdf.Create(compactInvoice, CancellationToken.None));
        File.WriteAllBytes(Path.Combine(output, "review-kompaktne-polozky.pdf"), compactPdf);
        using (var view = await PdfView.Open(compactPdf)) Check(view.Document.PageCount <= 3 && compactInvoice.Extended && compactInvoice.Root.Value.Contains("PODROBNOST-LEN-V-XML"), "Doplnky nevytvaraju blok pod kazdou polozkou a zostavaju v XML");
        var root = original.Root!; var party = root.Element(Invoice.A + "AccountingSupplierParty")!;
        var data = party.Element(Invoice.A + "Party")!;
        data.Add(new XElement(Invoice.A + "PartyName", new XElement(Invoice.C + "Name", "Obchodný názov pre test")));
        Check(InvoicePdf.PartyLines(party).Any(x => x.Contains("Obchodný názov pre test")), "Obchodný názov sa zachova popri registrovanom mene");
        var legal = data.Element(Invoice.A + "PartyLegalEntity")!;
        Check(InvoicePdf.Leaves(new XElement(Invoice.A + "PartyLegalEntity", new XElement(Invoice.C + "CompanyID", "123"))).Single().Contains("IČO") && InvoicePdf.Leaves(new XElement(Invoice.A + "PartyTaxScheme", new XElement(Invoice.C + "CompanyID", "SK123"), new XElement(Invoice.A + "TaxScheme", new XElement(Invoice.C + "ID", "VAT")))).First().Contains("IČ DPH"), "IČO a IČ DPH maju odlisne popisy");
        var ids = new[] { "SellersItemIdentification", "BuyersItemIdentification", "StandardItemIdentification" }.Select(x => InvoicePdf.Leaves(new XElement(Invoice.A + x, new XElement(Invoice.C + "ID", "123"))).Single()).ToArray();
        Check(ids.Distinct().Count() == 3, "Tri identifikatory tovaru maju rozlisitelne popisy");
        var item = root.Element(Invoice.A + "InvoiceLine")!.Element(Invoice.A + "Item")!;
        item.Element(Invoice.C + "Name")!.Value = new string('W', 160);
        root.Add(new XElement(Invoice.C + "Note", new string('W', 400)));
        var compact = Invoice.Parse(Encoding.UTF8.GetBytes(original.ToString()));
        File.WriteAllBytes(Path.Combine(output, "review-dlhe-slova.pdf"), await Task.Run(() => InvoicePdf.Create(compact, CancellationToken.None)));
        Check(InvoicePdf.Wrap(new string('W', 400)).Replace("\u00ad", "") == new string('W', 400), "Zalomenie dlheho textu nestrati znaky");
        data.Element(Invoice.A + "PostalAddress")!.Add(new XElement(Invoice.A + "AddressLine", new XElement(Invoice.C + "Line", string.Concat(Enumerable.Repeat("Dlha adresa pre kontrolu strankovania. ", 320)) + " KONIEC-ADRESY")));
        legal.Add(new XElement(Invoice.C + "CompanyLegalForm", "KONIEC-PARTNERA"));
        Check(InvoicePdf.PartyNeedsFlow(InvoicePdf.PartyLines(party)), "Vysoky blok partnera sa prepne na strankovatelne odstavce");
        var longPdf = await Task.Run(() => InvoicePdf.Create(Invoice.Parse(Encoding.UTF8.GetBytes(original.ToString())), CancellationToken.None));
        File.WriteAllBytes(Path.Combine(output, "review-dlhy-partner.pdf"), longPdf);
        using (var view = await PdfView.Open(longPdf))
        {
            Check(view.Document.PageCount > 2, "Dlhy partner prejde na ďalšie strany");
            var paginator = new PdfPrintPaginator(view, new Size(600, 800));
            using var last = paginator.GetPage(paginator.PageCount - 1);
            Check(last.Visual != null && paginator.GetPage(paginator.PageCount) == System.Windows.Documents.DocumentPage.Missing, "Tlacovy paginator vykresli poslednu stranu na poziadanie");
        }
        byte[] oversized;
        using (var document = new PdfSharp.Pdf.PdfDocument())
        {
            for (int i = 0; i < 1001; i++) document.AddPage();
            using var stream = new MemoryStream(); document.Save(stream, false); oversized = stream.ToArray();
        }
        var limitRejected = false;
        try { using var unused = await PdfView.Open(oversized); } catch (PdfPageLimitException e) { limitRejected = e.Message.Contains("1001"); }
        Check(limitRejected, "Nadlimitne PDF ma vlastne zrozumitelne hlasenie");
        var testFile = Path.Combine(examples, "01_zaklad.xml");
        // Skutocna WPF udalost z horneho tlacidla musi posunut viacstranovy dokument,
        // nie prejst na dalsiu fakturu. Netestujeme iba samotny pomocny switch.
        var keyboardPath = Path.Combine(output, "review-klavesnica.xml"); compactItems.Save(keyboardPath);
        var keyboardWindow = new ViewerWindow(null, _ => new[] { keyboardPath, testFile }); keyboardWindow.Show();
        try
        {
            await keyboardWindow.OpenPath(keyboardPath); keyboardWindow.TestFit(ViewerWindow.FitMode.Width); keyboardWindow.UpdateLayout();
            var keyboardScroll = keyboardWindow.ScrollForTest;
            var keyboardOrigin = (UIElement)keyboardWindow.HeaderToolsForTest.Children[0]; keyboardOrigin.Focus();
            async Task<KeyEventArgs> Press(Key key)
            {
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(keyboardWindow)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                keyboardOrigin.RaiseEvent(args);
                await keyboardWindow.Dispatcher.InvokeAsync(keyboardWindow.UpdateLayout, DispatcherPriority.ApplicationIdle);
                return args;
            }
            var number = keyboardWindow.OpenedNumber;
            Check(keyboardScroll.ScrollableHeight > keyboardScroll.ViewportHeight, "Viacstranova faktura pre test klavesnice ma posuvny obsah");
            Check((await Press(Key.Down)).Handled && keyboardScroll.VerticalOffset > 0 && keyboardWindow.OpenedNumber == number, "Sipka dole posunie PDF aj s fokusom v zahlavi");
            Check((await Press(Key.Up)).Handled && keyboardScroll.VerticalOffset == 0, "Sipka hore vrati posun PDF");
            Check((await Press(Key.PageDown)).Handled && Math.Abs(keyboardScroll.VerticalOffset - keyboardScroll.ViewportHeight) < 1 && keyboardWindow.OpenedNumber == number, "Page Down posunie o vysku nahladu a nezmeni fakturu");
            Check((await Press(Key.PageUp)).Handled && keyboardScroll.VerticalOffset == 0, "Page Up vrati predchadzajucu cast PDF");
            keyboardScroll.ScrollToEnd(); keyboardWindow.UpdateLayout();
            Check((await Press(Key.PageDown)).Handled && keyboardScroll.VerticalOffset == keyboardScroll.ScrollableHeight && keyboardWindow.OpenedNumber == number, "Page Down na konci neprepne fakturu");
            await Press(Key.Right);
            for (int i = 0; i < 500 && (keyboardWindow.OpenedNumber == number || !keyboardWindow.FirstPageRendered); i++) await Task.Delay(10);
            Check(keyboardWindow.OpenedNumber != number && keyboardWindow.FirstPageRendered, "Sipka doprava nadalej prepina faktury");
            await Press(Key.Left);
            for (int i = 0; i < 500 && (keyboardWindow.OpenedNumber != number || !keyboardWindow.FirstPageRendered); i++) await Task.Delay(10);
            Check(keyboardWindow.OpenedNumber == number && keyboardWindow.FirstPageRendered, "Sipka dolava sa vrati na povodnu fakturu");
        }
        finally { keyboardWindow.Close(); }
        var restricted = new ViewerWindow(null, _ => throw new UnauthorizedAccessException()); restricted.Show();
        await restricted.OpenPath(testFile);
        Check(restricted.HasPdf && !restricted.NavigationVisible && restricted.StatusForTest.Length > 0, "Necitatelny zoznam priečinka neblokuje otvorenie XML"); restricted.Close();
        var window = new ViewerWindow(null); window.Show();
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        window.UpdateLayout();
        var tests = new[] { (new Point(16, 16), 13), (new Point(window.ActualWidth - 16, 16), 14), (new Point(16, window.ActualHeight - 16), 16), (new Point(window.ActualWidth - 16, window.ActualHeight - 16), 17), (new Point(16, window.ActualHeight / 2), 10), (new Point(window.ActualWidth - 16, window.ActualHeight / 2), 11), (new Point(window.ActualWidth / 2, 16), 12), (new Point(window.ActualWidth / 2, window.ActualHeight - 16), 15) };
        foreach (var (point, expectedHit) in tests)
        {
            var pixel = window.PointToScreen(point); var packed = new IntPtr(((int)pixel.Y << 16) | ((int)pixel.X & 0xffff));
            Check(SendMessage(handle, 0x84, IntPtr.Zero, packed).ToInt32() == expectedHit, "Systemovy uchop velkosti okna " + expectedHit);
        }
        var originalWidth = window.Width; window.Width -= 80; window.UpdateLayout();
        Check(window.ActualWidth < originalWidth - 70, "Okno prijme zmensenu velkost"); window.Width = originalWidth;
        var overLimitXml = XDocument.Load(Path.Combine(examples, "03_s_pdf.xml"));
        overLimitXml.Descendants(Invoice.C + "EmbeddedDocumentBinaryObject").First().Value = Convert.ToBase64String(oversized);
        var overLimitPath = Path.Combine(output, "review-1001.xml"); overLimitXml.Save(overLimitPath);
        await window.OpenPath(overLimitPath); await window.TestSelectSource(1);
        Check(window.HasPdf && window.StatusForTest.Contains("1001") && !window.FirstPageRendered && window.EmptyTitle.Length > 0, "Nadlimitne PDF zostane na export a zobrazi sa XML strom");
        await window.OpenPath(testFile);
        await window.RenderGateForTest.WaitAsync();
        Task pending;
        try
        {
            pending = window.OpenPath(Path.Combine(examples, "02_fmcg.xml"));
            for (int i = 0; i < 1000 && !window.HasPdf; i++) await Task.Delay(10);
            Check(window.HasPdf && !pending.IsCompleted, "Zmena dokumentu caka na obsadeny renderer");
            window.TestFit(ViewerWindow.FitMode.Width);
        }
        finally { window.RenderGateForTest.Release(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(20));
        Check(window.FirstPageRendered, "Posledná poziadavka vykreslenia sa nestrati");
        var shortName = new StringBuilder(32768); var shortLength = GetShortPathName(Path.GetFullPath(testFile), shortName, (uint)shortName.Capacity);
        Check(shortLength == 0 || ViewerWindow.CanonicalPath(shortName.ToString()).Equals(ViewerWindow.CanonicalPath(testFile), StringComparison.OrdinalIgnoreCase), "Kratka 8.3 cesta sa zjednoti s nazvom v priečinku");
        await window.OpenPath("invalid\0path"); Check(!window.NavigationVisible && !window.HasPdf, "Chýba novej cesty nezachova staru navigaciu");
        window.Close();
        var offer = new UpdateOffer("99.0.0", "v99.0.0", "eFakturaViewer.exe", "win-x64", 10000, "");
        int offersShown = 0;
        // Sledujeme skutocny potvrdzovaci dialog a zavrieme ho bez prijatia.
        // Test tak overi startovaci tok, ale nikdy nestahuje ani neinstaluje aktualizaciu.
        var offerCloser = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        offerCloser.Tick += (_, _) =>
        {
            foreach (var dialog in Application.Current.Windows.OfType<AppDialog>().ToArray())
                if (dialog.Title == "Nová verzia 99.0.0") { offersShown++; dialog.Close(); }
        };
        async Task WaitForOffer()
        {
            var deadline = Stopwatch.StartNew();
            while (offersShown == 0 && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(30);
        }
        offerCloser.Start();
        try
        {
            var startupPreferences = Preferences.Load(); startupPreferences.CheckUpdates = true; startupPreferences.Save();
            var notification = new ViewerWindow(null, updateChecker: _ => Task.FromResult<UpdateOffer?>(offer));
            // Okno uz ma vlastnu kopiu preferencii; ostatne testovacie okna nemaju kontaktovat siet.
            startupPreferences.CheckUpdates = false; startupPreferences.Save();
            notification.Show();
            try
            {
                await WaitForOffer();
                Check(offersShown == 1 && notification.UpdateAvailable && !notification.AutomaticOfferPending, "Start automaticky ponukne najdenu aktualizaciu");
                await notification.OpenPath(testFile); await Task.Delay(350);
                Check(offersShown == 1 && notification.UpdateAvailable, "Odmietnuta ponuka sa neopakuje pri listovani a indikator zostane");
            }
            finally { notification.Close(); }

            offersShown = 0;
            var deferred = new ViewerWindow(null, updateChecker: _ => Task.FromResult<UpdateOffer?>(offer)); deferred.Show();
            var busy = AppDialog.Preview(deferred); busy.Show();
            try
            {
                await deferred.CheckUpdates(false); await Task.Delay(350);
                Check(offersShown == 0 && deferred.AutomaticOfferPending, "Ponuka aktualizacie pocka kym sa zavrie otvoreny dialog");
                busy.Close(); await WaitForOffer();
                Check(offersShown == 1 && !deferred.AutomaticOfferPending, "Odlozena ponuka sa zobrazi po zavreti dialogu");
            }
            finally { if (busy.IsVisible) busy.Close(); deferred.Close(); }
            offersShown = 0;
            foreach (var checker in new Func<CancellationToken, Task<UpdateOffer?>>[] { _ => Task.FromResult<UpdateOffer?>(null), _ => Task.FromException<UpdateOffer?>(new IOException("Test bez siete")) })
            {
                var silent = new ViewerWindow(null, updateChecker: checker); silent.Show();
                try { await silent.CheckUpdates(false); Check(!silent.AutomaticOfferPending && !silent.UpdateAvailable && offersShown == 0, "Bez novej verzie alebo siete sa pri starte neotvara oznam"); }
                finally { silent.Close(); }
            }
        }
        finally { offerCloser.Stop(); }
        int calls = 0; bool cancelled = false;
        var manual = new ViewerWindow(null, updateChecker: async token =>
        {
            if (++calls == 1) { try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { cancelled = true; throw; } }
            return offer;
        }); manual.Show(); var automatic = manual.CheckUpdates(false);
        // Zatvorime az potvrdzovaci dialog, nie priebeh kontroly; nic sa nestahuje.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => { foreach (var dialog in Application.Current.Windows.OfType<AppDialog>().ToArray()) if (dialog.Title.Contains("99.0.0")) dialog.Close(); };
        timer.Start(); try { await manual.CheckUpdates(true); await automatic; } finally { timer.Stop(); }
        Check(calls == 2 && cancelled && manual.UpdateAvailable, "Rucna kontrola zrusi visiacu automaticku kontrolu a zobrazí vysledok"); manual.Close();
        var transaction = Path.Combine(output, "update-transaction-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(transaction);
        var target = Path.Combine(transaction, "app.exe"); var candidate = Path.Combine(transaction, "candidate.exe");
        var failedJob = Path.Combine(transaction, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(failedJob);
        var reopenPath = Path.Combine(output, "faktura s medzerami.xml");
        File.WriteAllText(Path.Combine(failedJob, "reopen.json"), System.Text.Json.JsonSerializer.Serialize(reopenPath));
        bool restarted = false;
        Check(Updates.Apply(failedJob, target, int.MaxValue.ToString(), (path, reopen, failed) => restarted = path == target && reopen == reopenPath && failed) == 1 && restarted, "Chyba pred vymenou EXE obnovi okno aj cestu dokumentu");
        File.WriteAllText(target, "pôvodné"); File.WriteAllText(candidate, "nove");
        Reject(() => Updates.ReplaceAndStart(candidate, target, "verify", _ => throw new IOException("Overenie"), _ => { }, () => { }), "Chýba overenia pripravenych bajtov sa hlasi");
        Check(File.ReadAllText(target) == "pôvodné", "Chýba pred vymenou ponecha pôvodný súbor");
        Reject(() => Updates.ReplaceAndStart(candidate, target, "probe", _ => { }, _ => throw new IOException("Sonda"), () => { }), "Zlyhanie sondy sa hlasi");
        Check(File.ReadAllText(target) == "pôvodné", "Zlyhanie sondy obnovi pôvodný súbor");
        Reject(() => Updates.ReplaceAndStart(candidate, target, "start", _ => { }, _ => { }, () => throw new IOException("Start")), "Zlyhanie spustenia sa hlasi");
        Check(File.ReadAllText(target) == "pôvodné", "Zlyhanie spustenia obnovi pôvodný súbor");
        Task releaseLock = Task.CompletedTask;
        Reject(() => Updates.ReplaceAndStart(candidate, target, "short-lock", _ => { }, _ => { }, () =>
        {
            var held = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
            releaseLock = Task.Run(async () => { await Task.Delay(200); held.Dispose(); });
            throw new IOException("Zlyhanie startu pri kratkodobom zamku");
        }), "Zlyhanie startu so zdielacim zamkom sa hlasi");
        await releaseLock;
        Check(File.ReadAllText(target) == "pôvodné", "Obnova zalohy pocka na kratkodobe uvolnenie suboru");
        FileStream? locked = null; var id = Guid.NewGuid().ToString("N");
        var backup = Updates.ReplaceAndStart(candidate, target, id, _ => { }, _ => { }, () => locked = new FileStream(target + ".previous-" + id, FileMode.Open, FileAccess.Read, FileShare.Read));
        Check(File.ReadAllText(target) == "nove" && File.Exists(backup), "Zamknutá záloha nevrati uspesnu aktualizáciu"); locked!.Dispose();
        var jobs = Path.Combine(transaction, "Updates"); var job = Path.Combine(jobs, id); Directory.CreateDirectory(job);
        File.WriteAllText(Path.Combine(job, "updater.exe"), "pomocnik"); Updates.CompleteJob(job, target, backup);
        using (var active = new FileStream(Path.Combine(job, "job.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        { Updates.CleanupJobs(jobs, target); Check(Directory.Exists(job), "Upratovanie nezasiahne aktivnu aktualizáciu"); }
        Updates.CleanupJobs(jobs, target); Check(!Directory.Exists(job) && !File.Exists(backup), "Dokončená aktualizácia odstrani pomocnika aj odlozenu zalohu");
        var unrelated = Path.Combine(jobs, "cudzie-súbory"); Directory.CreateDirectory(unrelated); File.WriteAllText(Path.Combine(unrelated, "updater.exe"), "zachovat");
        Updates.CleanupJobs(jobs, target); Check(File.Exists(Path.Combine(unrelated, "updater.exe")), "Upratovanie nemaze cudzie priecinky");
        var pidFile = Path.Combine(transaction, "probe.pid");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command"); start.ArgumentList.Add("[IO.File]::WriteAllText('" + pidFile.Replace("'", "''") + "',$PID.ToString()); Start-Sleep -Seconds 60");
        await Task.Run(() => Reject(() => Updates.RunProbe(start, 2500), "Zamrznuta sonda prekroci časový limit"));
        var probePid = int.Parse(File.ReadAllText(pidFile)); bool exited; try { using var process = Process.GetProcessById(probePid); exited = process.HasExited; } catch (ArgumentException) { exited = true; }
        Check(exited, "Pred obnovou zálohy je zamrznuta sonda ukoncena");
        Updates.VerifyExecutableVersion(Environment.ProcessPath!, typeof(App).Assembly.GetName().Version!.ToString(3));
        Reject(() => Updates.VerifyExecutableVersion(Environment.ProcessPath!, "99.0.0"), "Stary EXE sa nesmie vydavat za novú verziu");
    }
}

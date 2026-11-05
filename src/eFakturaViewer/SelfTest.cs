using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace eFakturaViewer;

internal static class SelfTest
{
    [DllImport("wtsapi32.dll")] static extern bool WTSQuerySessionInformation(IntPtr server, int session, int infoClass, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr memory);
    static bool Disconnected()
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, System.Diagnostics.Process.GetCurrentProcess().SessionId, 8, out var buffer, out var count)) return false;
        try { return count >= 4 && Marshal.ReadInt32(buffer) == 4; } finally { WTSFreeMemory(buffer); }
    }
    internal static async Task Run(string output, string examples)
    {
        output = Path.GetFullPath(output); examples = Path.GetFullPath(examples); Directory.CreateDirectory(output);
        var checks = new List<string>();
        var skipped = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        void Reject(Action a, string name) { try { a(); } catch { checks.Add(name); return; } throw new Exception("Neodmietnute: " + name); }
        // Najprv skusime obraz nezavisly od prezeraca. Iba zlyhanie tejto sondy spolu s odpojenou
        // relaciou opravnuje preskocit snimky; inak by sme mohli skryt skutocnu chybu vykreslovania.
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var probe = new DrawingVisual(); using (var dc = probe.RenderOpen()) dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 2, 2));
        var probeBitmap = new RenderTargetBitmap(2, 2, 96, 96, PixelFormats.Pbgra32); probeBitmap.Render(probe); byte[] pixels = new byte[16]; probeBitmap.CopyPixels(pixels, 8, 0);
        var canRender = pixels[2] == 255 && pixels[3] == 255;
        if (!canRender && Disconnected()) skipped.Add("Vizualna kontrola WPF okna: odpojena relacia Windows vracia prazdny obraz aj pre nezavislu graficku sondu.");
        else Check(canRender, "WPF graficka sonda");
        // Porovnanie pred a po behu kontroluje slub, ze prezeranie nikdy nemenilo vstupne XML.
        var originals = Directory.GetFiles(examples, "*.xml").ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
        foreach (var name in new[] { "01_zaklad", "02_fmcg", "04_dobropis" })
        {
            var invoice = Invoice.Load(Path.Combine(examples, name + ".xml"), CancellationToken.None);
            Check(invoice.IsInvoice, "Rozpoznanie " + name);
            Check(invoice.Extended == (name == "02_fmcg"), "Profil " + name);
            var bytes = await Task.Run(() => InvoicePdf.Create(invoice, CancellationToken.None));
            File.WriteAllBytes(Path.Combine(output, name + ".pdf"), bytes);
            using var view = await PdfView.Open(bytes); var bitmap = await view.Render(0, 800);
            Check(view.Document.PageCount > 0 && bitmap.PixelWidth == 800, "PDF sa vykresli " + name);
        }
        var embedded = Invoice.Load(Path.Combine(examples, "03_s_pdf.xml"), CancellationToken.None);
        Check(embedded.Attachments[0].Read().SequenceEqual(File.ReadAllBytes(Path.Combine(examples, "ukazkove_vlozene.pdf"))), "Vložené PDF bez zmeny bajtov");
        var two = new XDocument(embedded.Xml); two.Root!.Add(new XElement(two.Root.Elements(Invoice.A + "AdditionalDocumentReference").First()));
        Check(Invoice.Parse(Encoding.UTF8.GetBytes(two.ToString())).Attachments.Count == 2, "Viac PDF príloh zostane na vyber");
        var invalid = new XDocument(embedded.Xml); invalid.Descendants(Invoice.C + "EmbeddedDocumentBinaryObject").First().Value = "%%%";
        var invalidAttachment = Invoice.Parse(Encoding.UTF8.GetBytes(invalid.ToString()));
        Reject(() => invalidAttachment.Attachments[0].Read(), "Poškodené base64");
        var stillGenerated = await Task.Run(() => InvoicePdf.Create(invalidAttachment, CancellationToken.None));
        using (var view = await PdfView.Open(stillGenerated)) Check(view.Document.PageCount > 0, "Chybna príloha neblokuje generovany náhľad");
        var rejectedPdf = false; try { using var bad = await PdfView.Open(Encoding.ASCII.GetBytes("%PDF-broken")); } catch { rejectedPdf = true; }
        Check(rejectedPdf, "Poškodené telo PDF odmietne Windows renderer");
        Check(!Invoice.Parse(Encoding.UTF8.GetBytes("<settings><name>test</name></settings>")).IsInvoice, "Bezne XML sa nepovazuje za fakturu");
        Reject(() => Invoice.Parse(Encoding.UTF8.GetBytes("<bad>")), "Poškodené XML");
        Reject(() => Invoice.Parse(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY y SYSTÉM 'file:///C:/secret'>]><x>&y;</x>")), "DTD a XXE");
        Reject(() => Invoice.Parse(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<?xml version='1.0' encoding='UTF-16'?><!DOCTYPE x [<!ENTITY y 'x'>]><x>&y;</x>")).ToArray()), "UTF16 DTD");
        var original = File.ReadAllText(Path.Combine(examples, "01_zaklad.xml"));
        var prefixed = original.Replace("cbc:", "b:").Replace("xmlns:cbc=", "xmlns:b=").Replace("cac:", "a:").Replace("xmlns:cac=", "xmlns:a=");
        Check(Invoice.Parse(Encoding.UTF8.GetBytes(prefixed)).Number == Invoice.Parse(Encoding.UTF8.GetBytes(original)).Number, "Lubovolne namespace prefixy");
        var badProfile = original.Replace(Invoice.Profile, Invoice.Profile + "-invalid");
        Check(!Invoice.Parse(Encoding.UTF8.GetBytes(badProfile)).IsInvoice, "Podobny profil sa neprijme");
        var extension = new XElement(Invoice.A + "AdditionalItemProperty", new XElement(Invoice.C + "Name", "vlastny_udaj"), new XElement(Invoice.C + "Value", "000123"));
        Check(Invoice.Property(extension).Contains("000123"), "Neznama vlastnost a uvodne nuly");
        var many = Invoice.Parse(Encoding.UTF8.GetBytes(original)).Xml;
        var line = many.Root!.Elements(Invoice.A + "InvoiceLine").First();
        many.Root.Elements(Invoice.A + "InvoiceLine").Remove();
        for (int i = 0; i < 2000; i++) { var copy = new XElement(line); copy.Element(Invoice.C + "ID")!.Value = (i + 1).ToString(); many.Root.Add(copy); }
        var big = Invoice.Parse(Encoding.UTF8.GetBytes(many.ToString()));
        var bigPdf = await Task.Run(() => InvoicePdf.Create(big, CancellationToken.None)); File.WriteAllBytes(Path.Combine(output, "2000-poloziek.pdf"), bigPdf);
        using (var view = await PdfView.Open(bigPdf)) { Check(view.Document.PageCount > 2, "2000 položiek sa strankuje"); await view.Render(view.Document.PageCount - 1, 800); checks.Add("Posledná strana velkej faktúry sa vykresli"); }
        line = big.Xml.Root!.Elements(Invoice.A + "InvoiceLine").First(); line.Element(Invoice.A + "Item")!.Element(Invoice.C + "Name")!.Value = string.Concat(Enumerable.Repeat("Dlhy názov so slovenskou diakritikou čšžýáíé ", 160));
        var longDoc = new XDocument(big.Xml); longDoc.Root!.Elements(Invoice.A + "InvoiceLine").Skip(1).Remove();
        var longPdf = await Task.Run(() => InvoicePdf.Create(Invoice.Parse(Encoding.UTF8.GetBytes(longDoc.ToString())), CancellationToken.None));
        using (var view = await PdfView.Open(longPdf)) Check(view.Document.PageCount >= 2, "Jedna velmi dlha položka sa zalomi na viac stran");
        // Testovaci kluc je jednorazovy a nesuvisi s vydavatelom. Test nema potrebovat privatny
        // produkcny kluc; skutocny vydany balik sa overuje osobitne cez --verify-package.
        using var rsa = RSA.Create(2048); var payload = Encoding.UTF8.GetBytes("test podpisu"); var signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Updates.Verify(payload, signature, rsa.ToXmlString(false)); checks.Add("Platný podpis");
        Reject(() => Updates.Verify(Encoding.UTF8.GetBytes("zmenené"), signature, rsa.ToXmlString(false)), "Pozmeneny podpisany obsah");
        var window = new ViewerWindow(null); window.Show();
        foreach (var screen in new[] { new Rect(0, 0, 1920, 1080), new Rect(-2560, -200, 2560, 1440), new Rect(1920, 0, 3840, 2160) })
        {
            var bounds = WindowPlacement.Calculate(screen);
            Check(Math.Abs(bounds.Height - screen.Height * .97) < 1 && Math.Abs(bounds.Width / bounds.Height - 210.0 / 297) < .001 && Math.Abs(bounds.X + bounds.Width / 2 - (screen.X + screen.Width / 2)) <= .5 && bounds.Bottom == screen.Bottom - 2, "A4 a 97 % vysky, dva pixely nad spodnou hranou " + screen);
            foreach (var work in new[] { new Rect(screen.X, screen.Y, screen.Width, screen.Height - 48), new Rect(screen.X, screen.Y + 60, screen.Width, screen.Height - 60), new Rect(screen.X + 80, screen.Y, screen.Width - 80, screen.Height) })
            { var available = WindowPlacement.Calculate(screen, work); Check(available.Bottom == work.Bottom - 2 && available.Top >= work.Top && available.Left >= work.Left && available.Right <= work.Right, "Panel uloh neprekryje okno " + work); }
        }
        var expected = WindowPlacement.LastInitialBounds;
        Check(WindowPlacement.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle, out var actual) && Math.Abs(actual.Left - expected.Left) <= 2 && Math.Abs(actual.Top - expected.Top) <= 2 && Math.Abs((actual.Right - actual.Left) - expected.Width) <= 2 && Math.Abs((actual.Bottom - actual.Top) - expected.Height) <= 2, "Skutocne otvorené okno ma pozadovanu polohu a rozmery");
        Check(window.FontFamily.Source == "Segoe UI Variable, Segoe UI", "Pismo zhodne s Lumou");
        Check(window.HeaderForTest.InputHitTest(new Point(window.HeaderForTest.ActualWidth / 2, 1)) != null && window.CanDragHeader(window.HeaderForTest, new Point(120, 22)) && !window.CanDragHeader(window.HeaderToolsForTest.Children[0], new Point(120, 22)) && !window.CanDragHeader(window.HeaderForTest, new Point(120, 500)), "Prazdne zahlavie je uchopitelne, tlačidlá a dokument nie");
        using (var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream)
        {
            var decoder = new IconBitmapDecoder(iconStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(decoder.Frames.Count == 7 && decoder.Frames.Any(x => x.PixelWidth == 16) && decoder.Frames.Any(x => x.PixelWidth == 256), "Ikona obsahuje sedem velkosti");
            foreach (var frame in decoder.Frames) { var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0); var px = new byte[bgra.PixelWidth * bgra.PixelHeight * 4]; bgra.CopyPixels(px, bgra.PixelWidth * 4, 0); Check(px[3] == 0 && Enumerable.Range(0, px.Length / 4).All(i => px[i * 4] == px[i * 4 + 1] && px[i * 4 + 1] == px[i * 4 + 2]), "Priehladna ciernobiela ikona " + frame.PixelWidth); }
        }
        await window.OpenPath(Path.Combine(examples, "02_fmcg.xml"));
        window.UpdateLayout();
        Check(window.CurrentFit == ViewerWindow.FitMode.Height && window.PageFitsHeight, "Predvolene sa cela strana zmesti na vysku");
        var heightWidth = window.PageWidth; window.TestFit(ViewerWindow.FitMode.Width); Check(window.PageWidth > heightWidth, "Prispôsobenie šírke skutocne zvacsi stranu"); window.TestFit(ViewerWindow.FitMode.Height);
        var slider = window.ZoomSliderForTest; slider.ApplyTemplate();
        Check(Math.Abs(slider.Value - window.PageWidth / 794 * 100) < .01 && window.CurrentFit == ViewerWindow.FitMode.Height, "Percenta lupy odrazaju prisposobenie bez zmeny rezimu");
        var track = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider);
        Check(!window.CanDragHeader(track.Thumb, new Point(320, 110)) && !slider.Focusable, "Uchop lupy nepresuva okno ani neprebera sipky");
        slider.Value = 175;
        Check(window.CurrentFit == ViewerWindow.FitMode.Manual && Math.Abs(window.PageWidth - 794 * 1.75) < .1 && window.ZoomPercentForTest == "175 %", "Posuvnik meni skutocnu velkost strany aj percenta");
        var priorZoom = slider.Value;
        track.Thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(8, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
        Check(slider.Value > priorZoom && window.PageWidth > 794 * 1.75, "Tah uchopu skutocne meni priblizenie PDF");
        window.ZoomResetForTest.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(window.ZoomPercentForTest == "100 %" && Math.Abs(window.PageWidth - 794) < .1, "Kliknutie na percenta obnovi 100 percent");
        window.TestFit(ViewerWindow.FitMode.Height);
        var regularWidth = window.Width; window.Width = window.MinWidth; window.UpdateLayout();
        var zoomCorner = window.ZoomControlForTest.TranslatePoint(new Point(window.ZoomControlForTest.ActualWidth, 0), window);
        Check(zoomCorner.X <= window.ActualWidth - 12, "Lupa sa zmesti aj do minimalnej sirky okna");
        window.Width = regularWidth; window.UpdateLayout();
        Check(window.NavigationVisible && window.ProfileBadge.Contains("ROZŠÍRENÝ FMCG") && window.HasPdf, "Rozšírený profil, PDF a navigacia v GUI");
        if (canRender)
        {
            // Seriove simulovane tahy nemaju prestavku ako skutocna mys. Pred snimkou
            // nechame prebehnut aj rozlozenie po zmene sirky a kompoziciu novej mierky.
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await Task.Delay(250);
            await window.Capture(Path.Combine(output, "viewer-dark.png"), false);
            Check(PageInkVisible(Path.Combine(output, "viewer-dark.png")), "Po zmene lupy je PDF viditelne aj v tmavej scheme");
            var dialog = AppDialog.Preview(window); dialog.Show(); dialog.UpdateLayout(); SaveWindow(dialog, Path.Combine(output, "dialog-dark.png")); dialog.Close();
            await window.Capture(Path.Combine(output, "viewer-light.png"), true);
            Check(PageInkVisible(Path.Combine(output, "viewer-light.png")), "Po prepnuti temy je faktúra viditelna aj vo vykreslenom okne");
            dialog = AppDialog.Preview(window); dialog.Show(); dialog.UpdateLayout(); SaveWindow(dialog, Path.Combine(output, "dialog-light.png")); dialog.Close();
        }
        window.SetThemeForTest(false); Check(window.BackgroundAlpha is >= 241 and <= 243, "Tmave pozadie ma priblizne 95 % krytie");
        window.SetThemeForTest(true); Check(window.BackgroundAlpha is >= 241 and <= 243, "Svetle pozadie ma priblizne 95 % krytie");
        await window.OpenPath(Path.Combine(examples, "03_s_pdf.xml")); Check(window.ProfileBadge.EndsWith("Z XML") && window.HasPdf, "Predvolený náhľad je generovany aj pri prilohe");
        await window.TestSelectSource(1); Check(window.ProfileBadge.EndsWith("VLOŽENÉ PDF") && window.HasPdf, "Prepinanie na vložené PDF");
        Check(window.EmbeddedSwitchChecked && !window.AttachmentPickerVisible, "Jedina príloha pouziva prepinac bez zbytocneho zoznamu");
        var multiplePath = Path.Combine(output, "multiple-pdf.xml"); File.WriteAllText(multiplePath, two.ToString());
        await window.OpenPath(multiplePath); await window.TestSelectSource(2); Check(window.EmbeddedSwitchChecked && window.AttachmentPickerVisible && window.HasPdf, "Pri viacerých prilohach sa ukaze vyber konkrétneho PDF");
        await window.TestSelectSource(0); Check(!window.EmbeddedSwitchChecked && !window.AttachmentPickerVisible && window.HasPdf, "Vypnutie príloh vráti generovany náhľad");
        Check(AppDialog.Work(window, "Kontrola", "Overujem dialog…", (_, _) => Task.CompletedTask), "Modalny priebeh sa po dokonceni zatvori");
        Check(!AppDialog.Work(window, "Kontrola", "Overujem zrusenie…", (_, _) => Task.FromException(new OperationCanceledException())), "Zrusena operácia vráti riadenie vlastnemu oknu");
        var single = Path.Combine(output, "single"); Directory.CreateDirectory(single); File.Copy(Path.Combine(examples, "01_zaklad.xml"), Path.Combine(single, "faktura.xml"), true);
        // Z minuleho behu moze ostat ukazka beznej XML konfiguracie.
        var prior = Path.Combine(single, "settings.xml"); if (File.Exists(prior)) File.Delete(prior);
        await window.OpenPath(Path.Combine(single, "faktura.xml")); Check(!window.NavigationVisible, "Jeden XML skryje spodny panel"); Check(window.HeaderToolsVisible && window.PageFitsHeight, "Pri jednom XML zostavaju horne nástroje a strana na vysku");
        Check(window.ZoomControlForTest.IsVisible && window.ZoomControlForTest.IsEnabled, "Lupa zostava dostupna aj pri jedinom XML");
        if (canRender) await window.Capture(Path.Combine(output, "viewer-single.png"), false);
        var unknown = Path.Combine(single, "settings.xml"); File.WriteAllText(unknown, "<settings><server port='443'>Ukazka</server><enabled>true</enabled></settings>");
        await window.OpenPath(unknown); Check(window.EmptyTitle == "Toto nie je eFaktúra." && window.NavigationVisible, "Bezne XML zobrazí oznam a zachova navigaciu"); if (canRender) await window.Capture(Path.Combine(output, "viewer-xml.png"), false);
        Check(!window.ZoomControlForTest.IsEnabled && window.ZoomPercentForTest == "—", "XML strom nema aktivne ovladanie zoomu PDF");
        // Zamerne necakame medzi poziadavkami. Postupne await by neodhalilo oneskoreny
        // vysledok prveho nacitania, ktory by prepisal druhy dokument.
        var first = window.OpenPath(Path.Combine(examples, "02_fmcg.xml")); var second = window.OpenPath(Path.Combine(examples, "04_dobropis.xml")); await Task.WhenAll(first, second);
        Check(window.Title.StartsWith("04_dobropis.xml") && window.OpenedNumber == Invoice.Load(Path.Combine(examples, "04_dobropis.xml"), CancellationToken.None).Number && window.HasPdf, "Rychle listovanie zachova poslednu poziadavku a jej PDF");
        await ReviewTests.Run(output, examples, checks);
        await UpdateTransportTests.Run(output, checks);
        await PdfLayoutTests.Run(output, examples, checks);
        Check(originals.All(x => x.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(x.Key)))), "Vstupne súbory zostali nezmenene");
        if (!canRender) foreach (var p in Directory.GetFiles(output, "viewer-*.png")) File.Delete(p);
        window.Close();
        await PreferenceTests.Run(output, examples, checks);
        await WorkflowTests.Run(output, examples, checks, canRender);
        File.WriteAllText(Path.Combine(output, "self-test.json"), JsonSerializer.Serialize(new { checks, skipped, runtime = Environment.Version.ToString(), version = typeof(App).Assembly.GetName().Version!.ToString(3) }, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static void SaveWindow(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); png.Save(stream);
    }
    static bool PageInkVisible(string path)
    {
        using var stream = File.OpenRead(path); var png = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = new FormatConvertedBitmap(png.Frames[0], PixelFormats.Bgra32, null, 0); var data = new byte[frame.PixelWidth * frame.PixelHeight * 4]; frame.CopyPixels(data, frame.PixelWidth * 4, 0);
        int ink = 0;
        for (int y = frame.PixelHeight / 4; y < frame.PixelHeight * 3 / 4; y++) for (int x = frame.PixelWidth / 4; x < frame.PixelWidth * 3 / 4; x++)
        { var i = (y * frame.PixelWidth + x) * 4; if (data[i] < 150 && data[i + 1] < 150 && data[i + 2] < 150 && data[i + 3] > 200) ink++; }
        return ink > 200;
    }
}

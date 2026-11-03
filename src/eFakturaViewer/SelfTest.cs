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
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var probe = new DrawingVisual(); using (var dc = probe.RenderOpen()) dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 2, 2));
        var probeBitmap = new RenderTargetBitmap(2, 2, 96, 96, PixelFormats.Pbgra32); probeBitmap.Render(probe); byte[] pixels = new byte[16]; probeBitmap.CopyPixels(pixels, 8, 0);
        var canRender = pixels[2] == 255 && pixels[3] == 255;
        if (!canRender && Disconnected()) skipped.Add("Vizualna kontrola WPF okna: odpojena relacia Windows vracia prazdny obraz aj pre nezavislu graficku sondu.");
        else Check(canRender, "WPF graficka sonda");
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
        Check(embedded.Attachments[0].Read().SequenceEqual(File.ReadAllBytes(Path.Combine(examples, "ukazkove_vlozene.pdf"))), "Vlozene PDF bez zmeny bajtov");
        var two = new XDocument(embedded.Xml); two.Root!.Add(new XElement(two.Root.Elements(Invoice.A + "AdditionalDocumentReference").First()));
        Check(Invoice.Parse(Encoding.UTF8.GetBytes(two.ToString())).Attachments.Count == 2, "Viac PDF priloh zostane na vyber");
        var invalid = new XDocument(embedded.Xml); invalid.Descendants(Invoice.C + "EmbeddedDocumentBinaryObject").First().Value = "%%%";
        var invalidAttachment = Invoice.Parse(Encoding.UTF8.GetBytes(invalid.ToString()));
        Reject(() => invalidAttachment.Attachments[0].Read(), "Poskodene base64");
        var stillGenerated = await Task.Run(() => InvoicePdf.Create(invalidAttachment, CancellationToken.None));
        using (var view = await PdfView.Open(stillGenerated)) Check(view.Document.PageCount > 0, "Chybna priloha neblokuje generovany nahlad");
        var rejectedPdf = false; try { using var bad = await PdfView.Open(Encoding.ASCII.GetBytes("%PDF-broken")); } catch { rejectedPdf = true; }
        Check(rejectedPdf, "Poskodene telo PDF odmietne Windows renderer");
        Check(!Invoice.Parse(Encoding.UTF8.GetBytes("<settings><name>test</name></settings>")).IsInvoice, "Bezne XML sa nepovazuje za fakturu");
        Reject(() => Invoice.Parse(Encoding.UTF8.GetBytes("<bad>")), "Poskodene XML");
        Reject(() => Invoice.Parse(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///C:/secret'>]><x>&y;</x>")), "DTD a XXE");
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
        using (var view = await PdfView.Open(bigPdf)) { Check(view.Document.PageCount > 2, "2000 poloziek sa strankuje"); await view.Render(view.Document.PageCount - 1, 800); checks.Add("Posledna strana velkej faktury sa vykresli"); }
        line = big.Xml.Root!.Elements(Invoice.A + "InvoiceLine").First(); line.Element(Invoice.A + "Item")!.Element(Invoice.C + "Name")!.Value = string.Concat(Enumerable.Repeat("Dlhy nazov so slovenskou diakritikou čšžýáíé ", 160));
        var longDoc = new XDocument(big.Xml); longDoc.Root!.Elements(Invoice.A + "InvoiceLine").Skip(1).Remove();
        var longPdf = await Task.Run(() => InvoicePdf.Create(Invoice.Parse(Encoding.UTF8.GetBytes(longDoc.ToString())), CancellationToken.None));
        using (var view = await PdfView.Open(longPdf)) Check(view.Document.PageCount >= 2, "Jedna velmi dlha polozka sa zalomi na viac stran");
        using var rsa = RSA.Create(2048); var payload = Encoding.UTF8.GetBytes("test podpisu"); var signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Updates.Verify(payload, signature, rsa.ToXmlString(false)); checks.Add("Platny podpis");
        Reject(() => Updates.Verify(Encoding.UTF8.GetBytes("zmenene"), signature, rsa.ToXmlString(false)), "Pozmeneny podpisany obsah");
        var window = new ViewerWindow(null); window.Show();
        await window.OpenPath(Path.Combine(examples, "02_fmcg.xml"));
        Check(window.NavigationVisible && window.ProfileBadge.Contains("ROZSIRENY FMCG") && window.HasPdf, "Rozsireny profil, PDF a navigacia v GUI");
        if (canRender) { await window.Capture(Path.Combine(output, "viewer-dark.png"), false); await window.Capture(Path.Combine(output, "viewer-light.png"), true); }
        await window.OpenPath(Path.Combine(examples, "03_s_pdf.xml")); Check(window.ProfileBadge.EndsWith("Z XML") && window.HasPdf, "Predvoleny nahlad je generovany aj pri prilohe");
        await window.TestSelectSource(1); Check(window.ProfileBadge.EndsWith("VLOZENE PDF") && window.HasPdf, "Prepinanie na vlozene PDF");
        var single = Path.Combine(output, "single"); Directory.CreateDirectory(single); File.Copy(Path.Combine(examples, "01_zaklad.xml"), Path.Combine(single, "faktura.xml"), true);
        // Z minuleho behu moze ostat ukazka beznej XML konfiguracie.
        var prior = Path.Combine(single, "settings.xml"); if (File.Exists(prior)) File.Delete(prior);
        await window.OpenPath(Path.Combine(single, "faktura.xml")); Check(!window.NavigationVisible, "Jeden XML skryje spodny panel"); if (canRender) await window.Capture(Path.Combine(output, "viewer-single.png"), false);
        var unknown = Path.Combine(single, "settings.xml"); File.WriteAllText(unknown, "<settings><server port='443'>Ukazka</server><enabled>true</enabled></settings>");
        await window.OpenPath(unknown); Check(window.EmptyTitle == "Toto nie je eFaktúra." && window.NavigationVisible, "Bezne XML zobrazi oznam a zachova navigaciu"); if (canRender) await window.Capture(Path.Combine(output, "viewer-xml.png"), false);
        var first = window.OpenPath(Path.Combine(examples, "02_fmcg.xml")); var second = window.OpenPath(Path.Combine(examples, "04_dobropis.xml")); await Task.WhenAll(first, second);
        Check(window.Title.StartsWith("04_dobropis.xml") && window.OpenedNumber == Invoice.Load(Path.Combine(examples, "04_dobropis.xml"), CancellationToken.None).Number && window.HasPdf, "Rychle listovanie zachova poslednu poziadavku a jej PDF");
        Check(originals.All(x => x.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(x.Key)))), "Vstupne subory zostali nezmenene");
        if (!canRender) foreach (var p in Directory.GetFiles(output, "viewer-*.png")) File.Delete(p);
        File.WriteAllText(Path.Combine(output, "self-test.json"), JsonSerializer.Serialize(new { checks, skipped, runtime = Environment.Version.ToString(), version = "1.0.0" }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    }
}

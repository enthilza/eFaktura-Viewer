using System.IO;
using System.Text;
using System.Xml.Linq;
using static eFakturaViewer.Invoice;

namespace eFakturaViewer;

internal static class PdfLayoutTests
{
    internal static async Task Run(string output, string examples, List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        var invoice = Invoice.Load(Path.Combine(examples, "05_prehladna.xml"), CancellationToken.None);
        var original = invoice.Root.ToString();
        var bytes = await Task.Run(() => InvoicePdf.Create(invoice, CancellationToken.None));
        File.WriteAllBytes(Path.Combine(output, "prehladna-faktura.pdf"), bytes);
        using (var view = await PdfView.Open(bytes)) Check(view.Document.PageCount == 1, "Bezny dvojpolozkovy doklad sa zmesti na jednu stranu");
        Check(invoice.Root.ToString() == original, "Zjednoduseny nahlad nemeni ani povodny XML strom");
        var party = invoice.Root.Element(A + "AccountingSupplierParty")!;
        var lines = InvoicePdf.PartyLines(party);
        Check(lines.Any(x => x.StartsWith("IČO:")) && lines.Any(x => x.StartsWith("IČ DPH:")) && !lines.Any(x => x.Contains("schemeID") || x.Contains("Elektronická adresa") || x.Contains("VAT")), "Partner ma obchodne udaje bez endpointov a technickych atributov");
        var category = new XElement(A + "TaxCategory", new XElement(C + "ID", "AE"));
        Check(InvoicePdf.TaxRate(category, true).Contains("Prenesenie") && !InvoicePdf.TaxRate(category).Contains('%'), "Chybajuca sadzba sa nezameni za nulovu DPH");
        category.Add(new XElement(C + "Percent", "0"));
        Check(InvoicePdf.TaxRate(category).Contains("AE"), "Nulova DPH zachovava odlisny rezim dane");

        // Tieto PDF zostavaju v test-results aj na nezavisle citanie textu a kontrolu
        // rozlozenia. Hodnoty su zamerne rozdielne: renderer ich nesmie dopocitat z poloziek.
        var modified = new XDocument(invoice.Xml); var root = modified.Root!;
        var totals = root.Element(A + "LegalMonetaryTotal")!;
        totals.Element(C + "PayableAmount")!.Value = "4400.49";
        totals.Add(new XElement(C + "AllowanceTotalAmount", new XAttribute("currencyID", "EUR"), "30.00"),
            new XElement(C + "ChargeTotalAmount", new XAttribute("currencyID", "EUR"), "5.00"),
            new XElement(C + "PrepaidAmount", new XAttribute("currencyID", "EUR"), "335.00"),
            new XElement(C + "PayableRoundingAmount", new XAttribute("currencyID", "EUR"), "-0.01"));
        root.Element(A + "TaxTotal")!.Element(A + "TaxSubtotal")!.Element(A + "TaxCategory")!.Add(new XElement(C + "TaxExemptionReason", "TEST-DOVOD-OSLOBODENIA"));
        root.Add(new XElement(A + "TaxTotal", new XElement(C + "TaxAmount", new XAttribute("currencyID", "CZK"), "22137.50")));
        root.Add(new XElement(A + "PaymentTerms", new XElement(C + "Note", string.Concat(Enumerable.Repeat("Dlhé podmienky úhrady. ", 200)) + "KONIEC-PODMIENOK")));
        var changed = Invoice.Parse(Encoding.UTF8.GetBytes(modified.ToString()));
        var expanded = await Task.Run(() => InvoicePdf.Create(changed, CancellationToken.None));
        File.WriteAllBytes(Path.Combine(output, "review-platby-a-dane.pdf"), expanded);
        using (var view = await PdfView.Open(expanded)) Check(view.Document.PageCount > 1, "Dlhe platobne podmienky pretecu na dalsie strany");
        totals.Element(C + "PayableAmount")!.Remove();
        root.Elements(A + "PaymentTerms").Remove();
        var missing = await Task.Run(() => InvoicePdf.Create(Invoice.Parse(Encoding.UTF8.GetBytes(modified.ToString())), CancellationToken.None));
        File.WriteAllBytes(Path.Combine(output, "review-neuvedena-suma.pdf"), missing);
        using (var view = await PdfView.Open(missing)) Check(view.Document.PageCount > 0, "Neuvedena suma nezablokuje nahlad ani sa nedopocita");
    }
}

using System.IO;
using System.Xml.Linq;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using static eFakturaViewer.Invoice;

namespace eFakturaViewer;

internal static partial class InvoicePdf
{
    static readonly Color Ink = Color.FromRgb(38, 41, 46);
    static readonly Color Muted = Color.FromRgb(103, 108, 115);
    static readonly Color Line = Color.FromRgb(225, 227, 230);
    static readonly Color PaperPanel = Color.FromRgb(246, 247, 248);

    internal static byte[] Create(Invoice invoice, CancellationToken token)
    {
        var d = new Document(); d.Info.Title = (invoice.Credit ? "Dobropis " : "Faktúra ") + invoice.Number;
        d.Info.Author = "eFaktura Viewer";
        var normal = d.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Segoe UI"; normal.Font.Size = 9; normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = 3;
        var h = d.Styles[StyleNames.Heading1]!;
        h.Font.Name = "Segoe UI"; h.Font.Size = 25; h.Font.Bold = true; h.Font.Color = Ink;
        var h2 = d.Styles[StyleNames.Heading2]!;
        h2.Font.Name = "Segoe UI"; h2.Font.Size = 10; h2.Font.Bold = true; h2.Font.Color = Muted;
        h2.ParagraphFormat.SpaceBefore = 14; h2.ParagraphFormat.SpaceAfter = 7;
        var s = d.AddSection(); s.PageSetup.PageFormat = PageFormat.A4;
        s.PageSetup.LeftMargin = Unit.FromMillimeter(16); s.PageSetup.RightMargin = Unit.FromMillimeter(16);
        s.PageSetup.TopMargin = Unit.FromMillimeter(14); s.PageSetup.BottomMargin = Unit.FromMillimeter(17);
        var footer = s.Footers.Primary.AddParagraph("eFaktura Viewer · Náhľad z XML");
        footer.Format.Font.Size = 7; footer.Format.Font.Color = Muted;
        footer.Format.TabStops.AddTabStop(Unit.FromMillimeter(178), TabAlignment.Right);
        footer.AddTab(); footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();
        s.AddParagraph(Wrap(d.Info.Title), StyleNames.Heading1);
        var sub = s.AddParagraph((invoice.Extended ? "BIS3 · ROZŠÍRENÝ FMCG" : "BIS3 · ZÁKLADNÝ") + "   /   " + invoice.Currency);
        sub.Format.Font.Size = 8; sub.Format.Font.Color = Muted; sub.Format.SpaceAfter = 15;

        Overview(s, invoice);
        Gap(s, 12);
        var r = invoice.Root;
        var supplier = PartyLines(r.Element(A + "AccountingSupplierParty"));
        var customer = PartyLines(r.Element(A + "AccountingCustomerParty"));
        // MigraDoc nerozdeli riadok tabulky cez strany. Neobvykle dlhe adresy
        // preto zostanu v plynucich odstavcoch namiesto orezania do karticky.
        if (PartyNeedsFlow(supplier) || PartyNeedsFlow(customer))
        {
            FlowParty(s, supplier, "DODÁVATEĽ", token); FlowParty(s, customer, "ODBERATEĽ", token);
        }
        else
        {
            var parties = Card(s, [3, 79, 3, 8, 3, 79, 3]);
            for (int i = 0; i < parties.Rows.Count; i++) parties.Rows[i].Cells[3].Shading.Color = Colors.White;
            Corners(parties, 0, 2); Corners(parties, 4, 6);
            Party(parties.Rows[1].Cells[1], supplier, "DODÁVATEĽ");
            Party(parties.Rows[1].Cells[5], customer, "ODBERATEĽ");
        }
        Payments(s, invoice, token);
        References(s, r, token);
        s.AddParagraph("POLOŽKY FAKTÚRY  /  " + invoice.Lines.Count(), StyleNames.Heading2);
        Table? items = null;
        foreach (var line in invoice.Lines)
        {
            token.ThrowIfCancellationRequested(); items ??= Items(s, invoice.Currency);
            var item = line.Element(A + "Item"); var price = line.Element(A + "Price");
            var q = line.Element(C + (invoice.Credit ? "CreditedQuantity" : "InvoicedQuantity"));
            var row = DataRow(items); var name = Value(item, "Name");
            if (name == "") name = Value(item, "Description");
            row.Cells[0].AddParagraph(Wrap(name.Length <= 160 ? name : name[..160] + "…", 18));
            row.Cells[1].AddParagraph(Wrap((q?.Value ?? "") + " " + UnitName((string?)q?.Attribute("unitCode")), 10));
            // Cena za balenie sa nesmie tvarit ako cena za kus. Zaklad aj presnost
            // preberame bez prepocitavania, rovnako ako dodavatelovu sumu riadka.
            var basis = Value(price, "BaseQuantity");
            var amount = Money(Value(price, "PriceAmount"));
            row.Cells[2].AddParagraph(Wrap(amount, 13));
            if (basis != "")
            {
                var p = row.Cells[2].AddParagraph(Wrap("za " + basis + " " + UnitName((string?)price?.Element(C + "BaseQuantity")?.Attribute("unitCode") ?? (string?)q?.Attribute("unitCode")), 12));
                p.Format.Font.Size = 7; p.Format.Font.Color = Muted;
            }
            row.Cells[3].AddParagraph(Wrap(TaxRate(item?.Element(A + "ClassifiedTaxCategory")), 10));
            row.Cells[4].AddParagraph(Wrap(Money(Value(line, "LineExtensionAmount")), 13)); row.Cells[4].Format.Font.Bold = true;
            for (int c = 1; c < 5; c++) row.Cells[c].Format.Alignment = ParagraphAlignment.Right;
            // Technicke/FMCG vlastnosti su v XML strome. Cely dlhy nazov vsak patri
            // k polozke a musi byt citatelny aj pri exporte, preto moze pokracovat za tabulkou.
            if (name.Length > 160) { Small(s, "Úplný názov položky " + Value(line, "ID") + ": " + name, token); items = null; }
        }
        Taxes(s, invoice, token);
        Totals(s, invoice);
        var notes = r.Elements(C + "Note").Select(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (notes.Length > 0) s.AddParagraph("POZNÁMKY", StyleNames.Heading2);
        foreach (var note in notes) Small(s, note, token);
        Gap(s, 10);
        var source = s.AddParagraph("Zjednodušený náhľad. Úplné údaje sú v XML; náhľad neoveruje platnosť BIS.");
        source.Format.Font.Size = 7; source.Format.Font.Color = Muted;
        token.ThrowIfCancellationRequested();
        // RenderDocument neprebera token. Po dobehnuti nepouziteho renderu jeho
        // vysledok zahodime, aby rychle listovanie nevratilo predoslu fakturu.
        var renderer = new PdfDocumentRenderer { Document = d }; renderer.RenderDocument();
        token.ThrowIfCancellationRequested();
        using var output = new MemoryStream(); renderer.PdfDocument.Save(output, false); return output.ToArray();
    }

    static void Overview(Section s, Invoice invoice)
    {
        var r = invoice.Root; var total = r.Element(A + "LegalMonetaryTotal");
        var row = Card(s, [3, 92, 40, 40, 3]).Rows[1];
        Caption(row.Cells[1], invoice.Credit ? "SUMA DOBROPISU" : "CELKOVÁ SUMA NA ÚHRADU");
        var p = row.Cells[1].AddParagraph(Wrap(Amount(total?.Element(C + "PayableAmount"), invoice.Currency), 20));
        p.Format.Font.Size = 23; p.Format.Font.Bold = true;
        var issue = Value(r, "IssueDate");
        var due = Value(r, "DueDate");
        if (due == "") due = r.Elements(A + "PaymentMeans").Select(x => Value(x, "PaymentDueDate")).FirstOrDefault(x => x != "") ?? "";
        if (issue != "") { Caption(row.Cells[2], "DÁTUM VYSTAVENIA"); row.Cells[2].AddParagraph(Wrap(Date(issue), 15)); }
        if (due != "") { Caption(row.Cells[3], "DÁTUM SPLATNOSTI"); row.Cells[3].AddParagraph(Wrap(Date(due), 15)); }
    }

    static void Payments(Section s, Invoice invoice, CancellationToken token)
    {
        var lines = new List<(string Label, string Value)>(); var r = invoice.Root;
        foreach (var means in r.Elements(A + "PaymentMeans"))
        {
            var account = means.Element(A + "PayeeFinancialAccount");
            AddField(lines, "IBAN / účet", Value(account, "ID"));
            AddField(lines, "Referencia platby / VS", Value(means, "PaymentID"));
            AddField(lines, "Názov účtu", Value(account, "Name"));
            AddField(lines, "BIC / banka", Value(account?.Element(A + "FinancialInstitutionBranch"), "ID"));
            var code = Value(means, "PaymentMeansCode");
            var method = code switch { "10" => "Hotovosť", "30" or "58" => "Bankový prevod", "48" => "Platobná karta", "49" or "59" => "Inkaso", "97" => "Vzájomný zápočet", _ => code == "" ? "" : "Kód " + code };
            AddField(lines, "Spôsob platby", method);
            var mandate = means.Element(A + "PaymentMandate");
            AddField(lines, "Mandát inkasa", Value(mandate, "ID"));
            AddField(lines, "Účet platiteľa", Value(mandate?.Element(A + "PayerFinancialAccount"), "ID"));
        }
        var payee = r.Element(A + "PayeeParty");
        AddField(lines, "Príjemca platby", Value(payee?.Element(A + "PartyName"), "Name"));
        foreach (var terms in r.Elements(A + "PaymentTerms"))
            foreach (var note in terms.Elements(C + "Note")) AddField(lines, "Platobné podmienky", note.Value);
        if (lines.Count == 0) return;
        s.AddParagraph("PLATOBNÉ ÚDAJE", StyleNames.Heading2);
        // Kazdy par ma vlastny ohraniceny riadok. Dlhy text podmienok tecie mimo
        // tabulky, inak by vysoka nedelitelna bunka mohla prekryt patu strany.
        Table? table = null;
        foreach (var field in lines)
        {
            token.ThrowIfCancellationRequested();
            if (field.Value.Length > 160) { Small(s, field.Label + ": " + field.Value, token); table = null; continue; }
            table ??= PlainTable(s, [48, 130]);
            var row = table.AddRow(); row.TopPadding = 3; row.BottomPadding = 3;
            row.Cells[0].AddParagraph(field.Label).Format.Font.Color = Muted;
            row.Cells[1].AddParagraph(Wrap(field.Value, 30));
        }
    }

    static void References(Section s, XElement r, CancellationToken token)
    {
        var fields = new List<(string Label, string Value)>();
        AddField(fields, "Referencia odberateľa", Value(r, "BuyerReference"));
        AddField(fields, "Objednávka", Value(r.Element(A + "OrderReference"), "ID"));
        AddField(fields, "Dátum daňovej povinnosti", Date(Value(r, "TaxPointDate")));
        foreach (var e in r.Elements(A + "Delivery")) AddField(fields, "Dátum dodania", Date(Value(e, "ActualDeliveryDate")));
        foreach (var e in r.Elements(A + "BillingReference")) AddField(fields, "Pôvodná faktúra", Value(e.Element(A + "InvoiceDocumentReference"), "ID"));
        foreach (var e in r.Elements(A + "DespatchDocumentReference")) AddField(fields, "Dodací list", Value(e, "ID"));
        foreach (var e in r.Elements(A + "InvoicePeriod"))
        {
            AddField(fields, "Obdobie od", Date(Value(e, "StartDate")));
            AddField(fields, "Obdobie do", Date(Value(e, "EndDate")));
        }
        if (fields.Count > 0) Gap(s, 7);
        foreach (var f in fields) Small(s, f.Label + ": " + f.Value, token);
    }

    static void Taxes(Section s, Invoice invoice, CancellationToken token)
    {
        var taxes = invoice.Root.Elements(A + "TaxTotal").ToArray();
        if (taxes.Length == 0) return;
        s.AddParagraph("ROZPIS DPH", StyleNames.Heading2);
        Table? table = null;
        foreach (var tax in taxes)
        {
            foreach (var detail in tax.Elements(A + "TaxSubtotal"))
            {
                token.ThrowIfCancellationRequested();
                table ??= HeaderTable(s, [78, 50, 50], ["Sadzba DPH", "Základ dane", "Suma dane"]);
                var category = detail.Element(A + "TaxCategory"); var row = DataRow(table);
                row.Cells[0].AddParagraph(Wrap(TaxRate(category, true), 25));
                row.Cells[1].AddParagraph(Wrap(Amount(detail.Element(C + "TaxableAmount"), invoice.Currency), 20));
                row.Cells[2].AddParagraph(Wrap(Amount(detail.Element(C + "TaxAmount"), invoice.Currency), 20));
                row.Cells[1].Format.Alignment = row.Cells[2].Format.Alignment = ParagraphAlignment.Right;
                var reasons = category?.Elements(C + "TaxExemptionReason").Select(x => x.Value).ToList() ?? [];
                if (reasons.Count == 0 && Value(category, "TaxExemptionReasonCode") != "") reasons.Add(Value(category, "TaxExemptionReasonCode"));
                foreach (var reason in reasons) Small(s, "Oslobodenie / režim DPH: " + reason, token);
                if (reasons.Count > 0) table = null;
            }
            // Druhy TaxTotal moze byt v uctovnej mene bez rozpisu. Nesmieme ho
            // vydavat za dan v mene faktury ani ho zahodit pri zjednoduseni.
            if (!tax.Elements(A + "TaxSubtotal").Any())
            {
                Small(s, "DPH spolu: " + Amount(tax.Element(C + "TaxAmount"), invoice.Currency), token);
                table = null;
            }
        }
    }

    static void Totals(Section s, Invoice invoice)
    {
        var totals = invoice.Root.Element(A + "LegalMonetaryTotal"); if (totals == null) return;
        Gap(s, 10);
        var table = PlainTable(s, [57, 37]); table.Rows.Alignment = RowAlignment.Right;
        // Ziadny sucet nevypocitavame z riadkov. Aj zalohy, zlavy a zaokruhlenie
        // su hodnoty z XML, preto zostavaju v kratkej obchodnej rekapitulacii.
        foreach (var name in new[] { "LineExtensionAmount", "AllowanceTotalAmount", "ChargeTotalAmount", "TaxExclusiveAmount", "TaxInclusiveAmount", "PrepaidAmount", "PayableRoundingAmount" })
        {
            var e = totals.Element(C + name); if (e == null) continue;
            if (name == "LineExtensionAmount" && Value(totals, "AllowanceTotalAmount") == "" && Value(totals, "ChargeTotalAmount") == "" && Value(totals, "TaxExclusiveAmount") != "") continue;
            var row = table.AddRow(); row.TopPadding = 3; row.BottomPadding = 3;
            row.Cells[0].AddParagraph(Label(name)); row.Cells[1].AddParagraph(Wrap(Amount(e, invoice.Currency), 17));
            row.Cells[1].Format.Alignment = ParagraphAlignment.Right;
            if (name == "TaxInclusiveAmount") { row.Format.Font.Bold = true; row.Shading.Color = PaperPanel; }
        }
    }

    internal static string TaxRate(XElement? category, bool describe = false)
    {
        var percent = Value(category, "Percent"); var id = Value(category, "ID");
        // Chybajuca sadzba nie je nula. Kod odlisuje napr. oslobodenie a prenesenie
        // povinnosti, aj ked maju oba nulovu dan.
        var name = id switch { "AE" => "Prenesenie povinnosti", "E" => "Oslobodené", "O" => "Mimo DPH", "K" => "Dodanie v EÚ", "G" => "Vývoz", "Z" => "Nulová sadzba", _ => id };
        if (percent == "") return describe ? (name == "" ? "Neuvedená" : name) : (id == "" ? "—" : id);
        var rate = percent.Replace('.', ',') + " %";
        return rate + (id is "" or "S" ? "" : describe ? " · " + name : " (" + id + ")");
    }
    static string Amount(XElement? e, string currency)
    {
        if (e == null || string.IsNullOrWhiteSpace(e.Value)) return "Neuvedené";
        var unit = (string?)e.Attribute("currencyID") ?? currency;
        return Money(e.Value) + " " + (unit == "EUR" ? "€" : unit);
    }
    static void AddField(List<(string Label, string Value)> fields, string label, string value) { if (!string.IsNullOrWhiteSpace(value)) fields.Add((label, value)); }
    static void Caption(Cell cell, string value) { var p = cell.AddParagraph(value); p.Format.Font.Size = 8; p.Format.Font.Color = Muted; p.Format.SpaceAfter = 7; }
    static void Gap(Section s, double size) { var p = s.AddParagraph(); p.Format.Font.Size = 1; p.Format.SpaceAfter = size; }
    static Table PlainTable(Section s, double[] widths)
    {
        var t = s.AddTable(); t.Rows.LeftIndent = 0; t.Borders.Visible = false; t.Format.SpaceAfter = 0;
        t.LeftPadding = t.RightPadding = Unit.FromMillimeter(2);
        foreach (var width in widths) t.AddColumn(Unit.FromMillimeter(width));
        return t;
    }
    static Table Card(Section s, double[] widths)
    {
        var t = PlainTable(s, widths); t.Shading.Color = PaperPanel;
        for (int i = 0; i < 3; i++)
        {
            var row = t.AddRow();
            if (i != 1) { row.HeightRule = RowHeightRule.Exactly; row.Height = Unit.FromMillimeter(3); }
        }
        t.Rows[0].KeepWith = 2;
        Corners(t, 0, widths.Length - 1); return t;
    }
    static void Corners(Table t, int first, int last)
    {
        t.Rows[0].Cells[first].RoundedCorner = RoundedCorner.TopLeft; t.Rows[0].Cells[last].RoundedCorner = RoundedCorner.TopRight;
        t.Rows[2].Cells[first].RoundedCorner = RoundedCorner.BottomLeft; t.Rows[2].Cells[last].RoundedCorner = RoundedCorner.BottomRight;
    }
    static Table HeaderTable(Section s, double[] widths, string[] headers)
    {
        var t = PlainTable(s, widths); var r = t.AddRow(); r.HeadingFormat = true;
        r.Shading.Color = PaperPanel; r.Format.Font.Bold = true; r.Format.Font.Size = 8;
        r.TopPadding = 8; r.BottomPadding = 8;
        for (int i = 0; i < headers.Length; i++) { r.Cells[i].AddParagraph(headers[i]); if (i > 0) r.Cells[i].Format.Alignment = ParagraphAlignment.Right; }
        return t;
    }
    static Table Items(Section s, string currency) => HeaderTable(s, [79, 24, 29, 18, 28], ["Položka", "Množstvo", "Cena / " + currency, "DPH", "Bez DPH / " + currency]);
    static Row DataRow(Table table)
    {
        var row = table.AddRow(); row.TopPadding = 7; row.BottomPadding = 7;
        row.Borders.Bottom.Width = 0.4; row.Borders.Bottom.Color = Line; row.Borders.Bottom.Visible = true;
        return row;
    }
}

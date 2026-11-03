using System.IO;
using System.Xml.Linq;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using static eFakturaViewer.Invoice;

namespace eFakturaViewer;

internal static class InvoicePdf
{
    internal static byte[] Create(Invoice invoice, CancellationToken token)
    {
        var d = new Document(); d.Info.Title = (invoice.Credit ? "Dobropis " : "Faktura ") + invoice.Number;
        d.Info.Author = "eFaktura Viewer";
        var normal = d.Styles[StyleNames.Normal]!; normal.Font.Name = "Arial"; normal.Font.Size = 9;
        normal.ParagraphFormat.SpaceAfter = 4;
        var h = d.Styles[StyleNames.Heading1]!; h.Font.Name = "Arial"; h.Font.Size = 24; h.Font.Color = Color.FromRgb(48, 48, 48);
        var h2 = d.Styles[StyleNames.Heading2]!; h2.Font.Name = "Arial"; h2.Font.Size = 12; h2.Font.Color = Color.FromRgb(48, 48, 48); h2.ParagraphFormat.SpaceBefore = 12;
        var s = d.AddSection(); s.PageSetup.PageFormat = PageFormat.A4;
        s.PageSetup.LeftMargin = Unit.FromMillimeter(16); s.PageSetup.RightMargin = Unit.FromMillimeter(16);
        s.PageSetup.TopMargin = Unit.FromMillimeter(15); s.PageSetup.BottomMargin = Unit.FromMillimeter(17);
        var footer = s.Footers.Primary.AddParagraph("eFaktura Viewer  /  Nahlad z XML                                      ");
        footer.Format.Font.Size = 7; footer.Format.Font.Color = Colors.Gray; footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();
        s.AddParagraph(d.Info.Title, StyleNames.Heading1);
        var sub = s.AddParagraph((invoice.Extended ? "BIS3 / ROZSIRENY FMCG" : "BIS3 / ZAKLADNY") + "  •  NAHLAD Z XML  •  " + invoice.Currency); sub.Format.Font.Color = Colors.Gray; sub.Format.SpaceAfter = 15;
        var r = invoice.Root;
        var parties = s.AddTable(); parties.Borders.Visible = false; parties.AddColumn(Unit.FromMillimeter(89)); parties.AddColumn(Unit.FromMillimeter(89));
        var row = parties.AddRow();
        Party(row.Cells[0], r.Element(A + "AccountingSupplierParty"), "DODAVATEL");
        Party(row.Cells[1], r.Element(A + "AccountingCustomerParty"), "ODBERATEL");
        s.AddParagraph();
        string[] fields = ["ID", "IssueDate", "DueDate", "TaxPointDate", "InvoiceTypeCode", "CreditNoteTypeCode", "BuyerReference", "DocumentCurrencyCode", "TaxCurrencyCode", "Note"];
        foreach (var name in fields) foreach (var e in r.Elements(C + name)) Add(s, Label(name), name.EndsWith("Date") ? Date(e.Value) : e.Value);
        foreach (var e in r.Elements().Where(x => x.Name.Namespace == A && x.Name.LocalName is not ("AccountingSupplierParty" or "AccountingCustomerParty" or "InvoiceLine" or "CreditNoteLine" or "TaxTotal" or "LegalMonetaryTotal" or "AdditionalDocumentReference")))
        { token.ThrowIfCancellationRequested(); s.AddParagraph(Label(e.Name.LocalName), StyleNames.Heading2); Details(s, e); }
        s.AddParagraph("Polozky", StyleNames.Heading2);
        Table? t = null;
        foreach (var line in invoice.Lines)
        {
            token.ThrowIfCancellationRequested();
            t ??= Items(s);
            var item = line.Element(A + "Item"); var price = line.Element(A + "Price");
            var q = line.Element(C + (invoice.Credit ? "CreditedQuantity" : "InvoicedQuantity"));
            var rr = t.AddRow(); rr.TopPadding = 5; rr.BottomPadding = 5; rr.Borders.Bottom.Width = 0.3; rr.Borders.Bottom.Color = Colors.LightGray;
            rr.Cells[0].AddParagraph(Value(line, "ID"));
            var name = Value(item, "Name"); rr.Cells[1].AddParagraph(name.Length <= 160 ? name : name[..160] + "…");
            rr.Cells[2].AddParagraph((q?.Value ?? "") + " " + UnitName((string?)q?.Attribute("unitCode")));
            var basis = Value(price, "BaseQuantity");
            rr.Cells[3].AddParagraph(Money(Value(price, "PriceAmount")) + (basis != "" ? " / " + basis + " " + UnitName((string?)price?.Element(C + "BaseQuantity")?.Attribute("unitCode") ?? (string?)q?.Attribute("unitCode")) : ""));
            rr.Cells[4].AddParagraph(Value(item?.Element(A + "ClassifiedTaxCategory"), "Percent") + " %");
            rr.Cells[5].AddParagraph(Money(Value(line, "LineExtensionAmount")));
            for (int c = 2; c < 6; c++) rr.Cells[c].Format.Alignment = ParagraphAlignment.Right;
            // Detailne udaje su odstavce: aj jedna extremne dlha polozka sa moze rozdelit na viac stran.
            var details = new List<string>();
            if (name.Length > 160) details.Add("Nazov: " + name);
            if (item != null) foreach (var child in item.Elements())
            {
                if (child.Name == C + "Name") continue;
                if (child.Name == A + "AdditionalItemProperty") details.Add(Property(child));
                else details.AddRange(Leaves(child));
            }
            foreach (var child in line.Elements().Where(x => x.Name.LocalName is not ("ID" or "Item" or "InvoicedQuantity" or "CreditedQuantity" or "LineExtensionAmount"))) details.AddRange(Leaves(child));
            if (details.Count > 0) { foreach (var detail in details) Small(s, detail, token); t = null; }
        }
        s.AddParagraph("Rekapitulacia DPH", StyleNames.Heading2);
        foreach (var tax in r.Elements(A + "TaxTotal")) Details(s, tax);
        s.AddParagraph("Suhrn a platba", StyleNames.Heading2);
        var totals = r.Element(A + "LegalMonetaryTotal");
        if (totals != null) foreach (var e in totals.Elements())
        {
            var p = s.AddParagraph(Label(e.Name.LocalName) + ":  " + Money(e.Value) + " " + ((string?)e.Attribute("currencyID") ?? invoice.Currency));
            if (e.Name.LocalName == "PayableAmount") { p.Format.Font.Size = 18; p.Format.Font.Bold = true; p.Format.SpaceBefore = 8; p.Format.Font.Color = Color.FromRgb(48, 48, 48); }
        }
        // Referencie a vsetky dalsie hlavickove prvky zostanu viditelne, binarne prilohy sa nevypisuju.
        var extra = r.Elements().Where(x => x.Name.Namespace != A && !fields.Contains(x.Name.LocalName)).ToList();
        if (extra.Count > 0 || invoice.Attachments.Count > 0) s.AddParagraph("Dalsie udaje dokumentu", StyleNames.Heading2);
        foreach (var e in extra) Details(s, e);
        foreach (var e in r.Elements(A + "AdditionalDocumentReference")) Details(s, e);
        var note = s.AddParagraph("Nahlad nie je potvrdenim formalnej platnosti BIS. Vlozene PDF moze mat iny obsah ako XML."); note.Format.Font.Size = 7; note.Format.Font.Color = Colors.Gray;
        token.ThrowIfCancellationRequested();
        var renderer = new PdfDocumentRenderer { Document = d }; renderer.RenderDocument();
        token.ThrowIfCancellationRequested();
        using var output = new MemoryStream(); renderer.PdfDocument.Save(output, false); return output.ToArray();
    }
    static Table Items(Section s)
    {
        var t = s.AddTable(); double[] widths = [10, 67, 24, 28, 18, 31]; foreach (var w in widths) t.AddColumn(Unit.FromMillimeter(w));
        var r = t.AddRow(); r.HeadingFormat = true; r.Shading.Color = Color.FromRgb(237, 237, 237); r.Format.Font.Bold = true; r.Format.Font.Size = 8; r.TopPadding = 6; r.BottomPadding = 6;
        string[] headers = ["C.", "Nazov polozky", "Mnozstvo", "Cena / zaklad", "DPH", "Bez DPH"];
        for (var i = 0; i < 6; i++) r.Cells[i].AddParagraph(headers[i]); return t;
    }
    static void Party(Cell c, XElement? party, string label)
    {
        var p = c.AddParagraph(label); p.Format.Font.Size = 8; p.Format.Font.Color = Colors.Gray;
        if (party == null) return;
        var data = party.Element(A + "Party"); if (data == null) return;
        var legal = data.Element(A + "PartyLegalEntity"); var name = Value(legal, "RegistrationName");
        if (name == "") name = Value(data.Element(A + "PartyName"), "Name");
        var heading = c.AddParagraph(name); heading.Format.Font.Bold = true; heading.Format.Font.Size = 12;
        var address = data.Element(A + "PostalAddress");
        if (address != null)
        {
            c.AddParagraph(Value(address, "StreetName") + " " + Value(address, "AdditionalStreetName"));
            c.AddParagraph(Value(address, "PostalZone") + " " + Value(address, "CityName") + "  " + Value(address.Element(A + "Country"), "IdentificationCode"));
            foreach (var extra in address.Elements().Where(x => x.Name.LocalName is not ("StreetName" or "AdditionalStreetName" or "PostalZone" or "CityName" or "Country"))) foreach (var text in Leaves(extra)) c.AddParagraph(text);
        }
        foreach (var e in data.Elements().Where(x => x.Name.LocalName is not ("PostalAddress" or "PartyName")))
            foreach (var leaf in e.DescendantsAndSelf().Where(x => !x.HasElements && x.Name.LocalName != "RegistrationName"))
            { foreach (var text in Leaves(leaf)) { var n = c.AddParagraph(text); n.Format.Font.Size = 8; } }
    }
    static void Add(Section s, string name, string value) { if (!string.IsNullOrWhiteSpace(value)) s.AddParagraph(name + ": " + value); }
    static void Details(Section s, XElement e) { foreach (var line in Leaves(e)) Small(s, line, CancellationToken.None); }
    static void Small(Section s, string text, CancellationToken token)
    {
        // Ohranicene odstavce zachovaju cely obsah a zabezpecia strankovanie velmi dlheho textu.
        for (var i = 0; i < text.Length; i += 600) { token.ThrowIfCancellationRequested(); var p = s.AddParagraph(text.Substring(i, Math.Min(600, text.Length - i))); p.Format.Font.Size = 8; p.Format.Font.Color = Color.FromRgb(64, 64, 64); p.Format.LeftIndent = 5; }
    }
    static IEnumerable<string> Leaves(XElement e, string prefix = "")
    {
        if (e.Name.LocalName == "EmbeddedDocumentBinaryObject") { yield return "Vlozena priloha: " + (string?)e.Attribute("filename") + " (" + (string?)e.Attribute("mimeCode") + ")"; yield break; }
        var label = prefix + Label(e.Name.LocalName);
        if (e.HasElements) { foreach (var c in e.Elements()) foreach (var v in Leaves(c)) yield return v; }
        else if (!string.IsNullOrWhiteSpace(e.Value))
        {
            var attrs = string.Join(", ", e.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name.LocalName + "=" + a.Value));
            if (e.Name.LocalName == "ID") label = e.Parent?.Name.LocalName switch { "PayeeFinancialAccount" => "IBAN / ucet", "TaxScheme" => "Danovy system", "TaxCategory" or "ClassifiedTaxCategory" => "Kategoria DPH", "OrderReference" => "Objednavka", _ => label };
            yield return label + ": " + (e.Name.LocalName.EndsWith("Date") ? Date(e.Value) : e.Value) + (attrs == "" ? "" : " [" + attrs + "]");
        }
    }
    static string UnitName(string? code) => code switch { "H87" or "C62" or "PCE" => "ks", "KGM" => "kg", "LTR" => "l", "MTR" => "m", "MTK" => "m²", "TNE" => "t", _ => code ?? "" };
    static string Label(string name) => Labels.GetValueOrDefault(name, System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2"));
    static readonly Dictionary<string, string> Labels = new()
    {
        ["ID"]="Cislo",["IssueDate"]="Vystavene",["DueDate"]="Splatnost",["TaxPointDate"]="Danova povinnost",["Note"]="Poznamka",["BuyerReference"]="Referencia odberatela",["DocumentCurrencyCode"]="Mena",["TaxCurrencyCode"]="Mena dane",
        ["Party"]="Partner",["PartyName"]="Nazov",["Name"]="Nazov",["PartyLegalEntity"]="Registracne udaje",["RegistrationName"]="Obchodne meno",["CompanyID"]="Identifikator",["PartyTaxScheme"]="Danove udaje",["PostalAddress"]="Adresa",["StreetName"]="Ulica",["CityName"]="Mesto",["PostalZone"]="PSC",["Country"]="Krajina",["IdentificationCode"]="Kod",["Contact"]="Kontakt",["Telephone"]="Telefon",["ElectronicMail"]="E-mail",["EndpointID"]="Elektronicka adresa",
        ["PaymentMeans"]="Platba",["PaymentMeansCode"]="Sposob platby",["PaymentID"]="Referencia platby / VS",["PaymentDueDate"]="Splatnost",["PayeeFinancialAccount"]="Bankovy ucet",["PaymentTerms"]="Platobne podmienky",["Delivery"]="Dodanie",["ActualDeliveryDate"]="Datum dodania",["OrderReference"]="Objednavka",["BillingReference"]="Referencia faktury",["AllowanceCharge"]="Zlava / prirazka",["ChargeIndicator"]="Prirazka (true) / zlava (false)",["Amount"]="Suma",["InvoicePeriod"]="Obdobie",["StartDate"]="Od",["EndDate"]="Do",["Description"]="Popis",["Price"]="Cena",["PriceAmount"]="Cena",["BaseQuantity"]="Zakladne mnozstvo",
        ["LineExtensionAmount"]="Sucet poloziek bez DPH",["TaxExclusiveAmount"]="Zaklad dane",["TaxInclusiveAmount"]="Celkom s DPH",["AllowanceTotalAmount"]="Zlavy spolu",["ChargeTotalAmount"]="Prirazky spolu",["PrepaidAmount"]="Zaplatene preddavky",["PayableRoundingAmount"]="Zaokruhlenie",["PayableAmount"]="NA UHRADU",["TaxAmount"]="DPH",["TaxableAmount"]="Zaklad dane",["Percent"]="Sadzba %",["TaxCategory"]="Kategoria dane",["TaxExemptionReason"]="Dovod oslobodenia",["TaxTotal"]="DPH",["TaxSubtotal"]="Rozpis",["AdditionalDocumentReference"]="Priloha / referencia"
    };
}

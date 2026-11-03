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
        var d = new Document(); d.Info.Title = (invoice.Credit ? "Dobropis " : "Faktúra ") + invoice.Number;
        d.Info.Author = "eFaktúra Viewer";
        var normal = d.Styles[StyleNames.Normal]!; normal.Font.Name = "Arial"; normal.Font.Size = 9;
        normal.ParagraphFormat.SpaceAfter = 4;
        var h = d.Styles[StyleNames.Heading1]!; h.Font.Name = "Arial"; h.Font.Size = 24; h.Font.Color = Color.FromRgb(48, 48, 48);
        var h2 = d.Styles[StyleNames.Heading2]!; h2.Font.Name = "Arial"; h2.Font.Size = 12; h2.Font.Color = Color.FromRgb(48, 48, 48); h2.ParagraphFormat.SpaceBefore = 12;
        var s = d.AddSection(); s.PageSetup.PageFormat = PageFormat.A4;
        s.PageSetup.LeftMargin = Unit.FromMillimeter(16); s.PageSetup.RightMargin = Unit.FromMillimeter(16);
        s.PageSetup.TopMargin = Unit.FromMillimeter(15); s.PageSetup.BottomMargin = Unit.FromMillimeter(17);
        var footer = s.Footers.Primary.AddParagraph("eFaktúra Viewer  /  Náhľad z XML                                      ");
        footer.Format.Font.Size = 7; footer.Format.Font.Color = Colors.Gray; footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();
        s.AddParagraph(Wrap(d.Info.Title), StyleNames.Heading1);
        var sub = s.AddParagraph((invoice.Extended ? "BIS3 / ROZŠÍRENÝ FMCG" : "BIS3 / ZÁKLADNÝ") + "  •  NÁHĽAD Z XML  •  " + invoice.Currency); sub.Format.Font.Color = Colors.Gray; sub.Format.SpaceAfter = 15;
        var r = invoice.Root;
        var supplier = PartyLines(r.Element(A + "AccountingSupplierParty"));
        var customer = PartyLines(r.Element(A + "AccountingCustomerParty"));
        // Bezny doklad si zachova dva stlpce. Vysoky blok presunieme do odstavcov,
        // lebo jeden riadok tabulky MigraDoc nevie rozdelit na dalsiu stranu.
        if (PartyNeedsFlow(supplier) || PartyNeedsFlow(customer))
        {
            FlowParty(s, supplier, "DODÁVATEĽ", token); FlowParty(s, customer, "ODBERATEĽ", token);
        }
        else
        {
            var parties = s.AddTable(); parties.Borders.Visible = false;
            parties.AddColumn(Unit.FromMillimeter(89)); parties.AddColumn(Unit.FromMillimeter(89));
            var row = parties.AddRow(); Party(row.Cells[0], supplier, "DODÁVATEĽ"); Party(row.Cells[1], customer, "ODBERATEĽ");
        }
        s.AddParagraph();
        string[] fields = ["ID", "IssueDate", "DueDate", "TaxPointDate", "InvoiceTypeCode", "CreditNoteTypeCode", "BuyerReference", "DocumentCurrencyCode", "TaxCurrencyCode", "Note"];
        foreach (var name in fields) foreach (var e in r.Elements(C + name)) Add(s, Label(name), name.EndsWith("Date") ? Date(e.Value) : e.Value);
        foreach (var e in r.Elements().Where(x => x.Name.Namespace == A && x.Name.LocalName is not ("AccountingSupplierParty" or "AccountingCustomerParty" or "InvoiceLine" or "CreditNoteLine" or "TaxTotal" or "LegalMonetaryTotal" or "AdditionalDocumentReference")))
        { token.ThrowIfCancellationRequested(); s.AddParagraph(Label(e.Name.LocalName), StyleNames.Heading2); Details(s, e); }
        s.AddParagraph("Položky", StyleNames.Heading2);
        Table? t = null;
        foreach (var line in invoice.Lines)
        {
            token.ThrowIfCancellationRequested();
            t ??= Items(s);
            var item = line.Element(A + "Item"); var price = line.Element(A + "Price");
            var q = line.Element(C + (invoice.Credit ? "CreditedQuantity" : "InvoicedQuantity"));
            var rr = t.AddRow(); rr.TopPadding = 5; rr.BottomPadding = 5; rr.Borders.Bottom.Width = 0.3; rr.Borders.Bottom.Color = Colors.LightGray;
            rr.Cells[0].AddParagraph(Value(line, "ID"));
            var name = Value(item, "Name"); rr.Cells[1].AddParagraph(Wrap(name.Length <= 160 ? name : name[..160] + "…", 12));
            rr.Cells[2].AddParagraph((q?.Value ?? "") + " " + UnitName((string?)q?.Attribute("unitCode")));
            var basis = Value(price, "BaseQuantity");
            // Cena moze platit napr. za 12 kusov. Zaklad zobrazime pri cene, neprepisujeme ju
            // na cenu za kus ani z nej znovu nepocitame sumu riadka dodanu v XML.
            rr.Cells[3].AddParagraph(Money(Value(price, "PriceAmount")) + (basis != "" ? " / " + basis + " " + UnitName((string?)price?.Element(C + "BaseQuantity")?.Attribute("unitCode") ?? (string?)q?.Attribute("unitCode")) : ""));
            rr.Cells[4].AddParagraph(Value(item?.Element(A + "ClassifiedTaxCategory"), "Percent") + " %");
            rr.Cells[5].AddParagraph(Money(Value(line, "LineExtensionAmount")));
            for (int c = 2; c < 6; c++) rr.Cells[c].Format.Alignment = ParagraphAlignment.Right;
            // Bezny nahlad ma jednu suvislu tabulku. Technicke a FMCG doplnky pouzivatel
            // najde v XML strome; opakovany vypis ceny a dane pod kazdym riadkom prekaza pri citani.
            // Iba mimoriadne dlhy nazov presunieme pod tabulku, aby sa nestratil ani neodrezal.
            if (name.Length > 160) { Small(s, "Úplný názov položky " + Value(line, "ID") + ": " + name, token); t = null; }
        }
        s.AddParagraph("Rekapitulácia DPH", StyleNames.Heading2);
        foreach (var tax in r.Elements(A + "TaxTotal")) Details(s, tax);
        s.AddParagraph("Súhrn a platba", StyleNames.Heading2);
        var totals = r.Element(A + "LegalMonetaryTotal");
        // Zobrazujeme rekapitulaciu dodavatela. Vlastny prepocet by mohol prekryt jeho zaokruhlenie,
        // preddavky alebo rozdiel v doklade; prezerac nema opravovat uctovne hodnoty.
        if (totals != null) foreach (var e in totals.Elements())
        {
            var p = s.AddParagraph(Wrap(Label(e.Name.LocalName) + ":  " + Money(e.Value) + " " + ((string?)e.Attribute("currencyID") ?? invoice.Currency)));
            if (e.Name.LocalName == "PayableAmount") { p.Format.Font.Size = 18; p.Format.Font.Bold = true; p.Format.SpaceBefore = 8; p.Format.Font.Color = Color.FromRgb(48, 48, 48); }
        }
        // Referencie a vsetky dalsie hlavickove prvky zostanu viditelne, binarne prilohy sa nevypisuju.
        var extra = r.Elements().Where(x => x.Name.Namespace != A && !fields.Contains(x.Name.LocalName)).ToList();
        if (extra.Count > 0 || invoice.Attachments.Count > 0) s.AddParagraph("Ďalšie údaje dokumentu", StyleNames.Heading2);
        foreach (var e in extra) Details(s, e);
        foreach (var e in r.Elements(A + "AdditionalDocumentReference")) Details(s, e);
        var note = s.AddParagraph("Náhľad nie je potvrdením formálnej platnosti BIS. Vložené PDF môže mať iný obsah ako XML."); note.Format.Font.Size = 7; note.Format.Font.Color = Colors.Gray;
        token.ThrowIfCancellationRequested();
        // MigraDoc pocas RenderDocument neprebera nas token. Kontrola po navrate zahodi vysledok,
        // ak pouzivatel medzitym presiel na iny subor; samotne vykreslenie vsak musi dobehnut.
        var renderer = new PdfDocumentRenderer { Document = d }; renderer.RenderDocument();
        token.ThrowIfCancellationRequested();
        using var output = new MemoryStream(); renderer.PdfDocument.Save(output, false); return output.ToArray();
    }
    static Table Items(Section s)
    {
        var t = s.AddTable(); double[] widths = [10, 67, 24, 28, 18, 31]; foreach (var w in widths) t.AddColumn(Unit.FromMillimeter(w));
        var r = t.AddRow(); r.HeadingFormat = true; r.Shading.Color = Color.FromRgb(237, 237, 237); r.Format.Font.Bold = true; r.Format.Font.Size = 8; r.TopPadding = 6; r.BottomPadding = 6;
        string[] headers = ["Č.", "Názov položky", "Množstvo", "Cena / základ", "DPH", "Bez DPH"];
        for (var i = 0; i < 6; i++) r.Cells[i].AddParagraph(headers[i]); return t;
    }
    internal static List<string> PartyLines(XElement? party)
    {
        var lines = new List<string>(); var data = party?.Element(A + "Party"); if (data == null) return lines;
        var name = Value(data.Element(A + "PartyLegalEntity"), "RegistrationName");
        if (name == "") name = Value(data.Element(A + "PartyName"), "Name");
        lines.Add(name);
        foreach (var trade in data.Elements(A + "PartyName").Select(x => Value(x, "Name")).Where(x => x.Length > 0 && x != name).Distinct())
            lines.Add("Obchodný názov: " + trade);
        var address = data.Element(A + "PostalAddress");
        if (address != null)
        {
            lines.Add(Value(address, "StreetName") + " " + Value(address, "AdditionalStreetName"));
            lines.Add(Value(address, "PostalZone") + " " + Value(address, "CityName") + "  " + Value(address.Element(A + "Country"), "IdentificationCode"));
            foreach (var extra in address.Elements().Where(x => x.Name.LocalName is not ("StreetName" or "AdditionalStreetName" or "PostalZone" or "CityName" or "Country"))) lines.AddRange(Leaves(extra));
        }
        foreach (var e in data.Elements().Where(x => x.Name.LocalName is not ("PostalAddress" or "PartyName")))
            foreach (var leaf in e.DescendantsAndSelf().Where(x => !x.HasElements && !(x.Name == C + "RegistrationName" && x.Value == name))) lines.AddRange(Leaves(leaf));
        return lines;
    }
    internal static bool PartyNeedsFlow(List<string> lines) => lines.Count > 18 || lines.Sum(x => x.Length) > 1200 || lines.Any(x => x.Length > 240) || (lines.Count > 0 && lines[0].Length > 160);
    static void Party(Cell cell, List<string> lines, string label)
    {
        var p = cell.AddParagraph(label); p.Format.Font.Size = 8; p.Format.Font.Color = Colors.Gray;
        for (int i = 0; i < lines.Count; i++)
        {
            p = cell.AddParagraph(Wrap(lines[i], 16)); p.Format.Font.Size = i == 0 ? 12 : 8; p.Format.Font.Bold = i == 0;
        }
    }
    static void FlowParty(Section s, List<string> lines, string label, CancellationToken token)
    {
        s.AddParagraph(label, StyleNames.Heading2);
        foreach (var line in lines) Small(s, line, token);
    }
    // Podmienene delenie sa vykresli iba pri zalomeni. Zdrojovy XML zostava bez uprav.
    // MigraDoc sam nerozdeli dlhe URL, kody ani slova bez medzier.
    internal static string Wrap(string text, int run = 24) => System.Text.RegularExpressions.Regex.Replace(text, @"[^\s\u00ad]{" + (run + 1) + @",}", m => string.Join("\u00ad", Enumerable.Range(0, (m.Length + run - 1) / run).Select(i => m.Value.Substring(i * run, Math.Min(run, m.Length - i * run)))));
    static void Add(Section s, string name, string value) { if (!string.IsNullOrWhiteSpace(value)) Small(s, name + ": " + value, CancellationToken.None); }
    static void Details(Section s, XElement e) { foreach (var line in Leaves(e)) Small(s, line, CancellationToken.None); }
    static void Small(Section s, string text, CancellationToken token)
    {
        // Ohranicene odstavce zachovaju cely obsah a zabezpecia strankovanie velmi dlheho textu.
        for (var i = 0; i < text.Length; i += 600) { token.ThrowIfCancellationRequested(); var p = s.AddParagraph(Wrap(text.Substring(i, Math.Min(600, text.Length - i)))); p.Format.Font.Size = 8; p.Format.Font.Color = Color.FromRgb(64, 64, 64); p.Format.LeftIndent = 5; }
    }
    internal static IEnumerable<string> Leaves(XElement e, string prefix = "")
    {
        // Base64 by zbytocne zaplnilo strany a nebolo citatelne. Prilohu tu zastupuju iba jej udaje;
        // povodne PDF je dostupne samostatnym prepinacom v prezeraci.
        if (e.Name.LocalName == "EmbeddedDocumentBinaryObject") { yield return "Vložená príloha: " + (string?)e.Attribute("filename") + " (" + (string?)e.Attribute("mimeCode") + ")"; yield break; }
        var label = prefix + Label(e.Name.LocalName);
        if (e.HasElements) { foreach (var c in e.Elements()) foreach (var v in Leaves(c, label + " / ")) yield return v; }
        else if (!string.IsNullOrWhiteSpace(e.Value))
        {
            // Atributy nesu menu, jednotku alebo identifikacnu schemu. Samotna hodnota bez nich moze zmenit vyznam.
            var attrs = string.Join(", ", e.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name.LocalName + "=" + a.Value));
            if (e.Name.LocalName == "ID") label = e.Parent?.Name.LocalName switch { "PayeeFinancialAccount" => "IBAN / účet", "TaxScheme" => "Daňový systém", "TaxCategory" or "ClassifiedTaxCategory" => "Kategória DPH", "OrderReference" => "Objednávka", "SellersItemIdentification" => "Kód tovaru dodávateľa", "BuyersItemIdentification" => "Kód tovaru odberateľa", "StandardItemIdentification" => "Štandardný kód tovaru", _ => label };
            if (e.Name == C + "CompanyID") label = e.Parent?.Name.LocalName switch
            {
                "PartyLegalEntity" => "Registračný identifikátor (IČO)",
                "PartyTaxScheme" => Value(e.Parent.Element(A + "TaxScheme"), "ID") == "VAT" ? "IČ DPH" : "Daňový identifikátor",
                _ => label
            };
            yield return label + ": " + (e.Name.LocalName.EndsWith("Date") ? Date(e.Value) : e.Value) + (attrs == "" ? "" : " [" + attrs + "]");
        }
    }
    static string UnitName(string? code) => code switch { "H87" or "C62" or "PCE" => "ks", "KGM" => "kg", "LTR" => "l", "MTR" => "m", "MTK" => "m²", "TNE" => "t", _ => code ?? "" };
    static string Label(string name) => Labels.GetValueOrDefault(name, System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2"));
    static readonly Dictionary<string, string> Labels = new()
    {
        ["InvoiceTypeCode"]="Typ faktúry",["CreditNoteTypeCode"]="Typ dobropisu",["CustomizationID"]="Špecifikácia dokumentu",["ProfileID"]="Procesný profil",["UBLVersionID"]="Verzia UBL",
        ["PartyIdentification"]="Identifikácia partnera",["TaxScheme"]="Daňový systém",["CompanyLegalForm"]="Právna forma",["AdditionalStreetName"]="Doplnenie ulice",["AddressLine"]="Riadok adresy",["Line"]="Text",["CountrySubentity"]="Kraj",
        ["SellersItemIdentification"]="Označenie tovaru dodávateľom",["BuyersItemIdentification"]="Označenie tovaru odberateľom",["StandardItemIdentification"]="Štandardné označenie tovaru",["ClassifiedTaxCategory"]="Daňová kategória tovaru",["CommodityClassification"]="Klasifikácia tovaru",["ItemClassificationCode"]="Klasifikačný kód tovaru",
        ["DeliveryLocation"]="Miesto dodania",["DeliveryParty"]="Príjemca",["DeliveryAddress"]="Adresa dodania",["Address"]="Adresa",["PayeeParty"]="Príjemca platby",["TaxRepresentativeParty"]="Daňový zástupca",["FinancialInstitutionBranch"]="Pobočka banky",["FinancialInstitution"]="Banka",["BranchID"]="Kód pobočky",
        ["AllowanceChargeReason"]="Dôvod zľavy alebo prirážky",["AllowanceChargeReasonCode"]="Kód dôvodu zľavy alebo prirážky",["MultiplierFactorNumeric"]="Percentuálna sadzba",["BaseAmount"]="Základná suma",["InvoiceDocumentReference"]="Odkaz na faktúru",["DespatchDocumentReference"]="Odkaz na dodací list",["ReceiptDocumentReference"]="Odkaz na príjemku",["ContractDocumentReference"]="Odkaz na zmluvu",["OriginatorDocumentReference"]="Odkaz na pôvodný dokument",["ProjectReference"]="Odkaz na projekt",["AccountingCost"]="Účtovné stredisko",["DocumentDescription"]="Opis dokumentu",["Attachment"]="Príloha",["ExternalReference"]="Externý odkaz",["URI"]="Adresa odkazu",
        ["ID"]="Číslo",["IssueDate"]="Vystavené",["DueDate"]="Splatnosť",["TaxPointDate"]="Daňová povinnosť",["Note"]="Poznámka",["BuyerReference"]="Referencia odberateľa",["DocumentCurrencyCode"]="Mena",["TaxCurrencyCode"]="Mena dane",
        ["Party"]="Partner",["PartyName"]="Názov",["Name"]="Názov",["PartyLegalEntity"]="Registračné údaje",["RegistrationName"]="Obchodné meno",["CompanyID"]="Identifikátor",["PartyTaxScheme"]="Daňové údaje",["PostalAddress"]="Adresa",["StreetName"]="Ulica",["CityName"]="Mesto",["PostalZone"]="PSČ",["Country"]="Krajina",["IdentificationCode"]="Kód",["Contact"]="Kontakt",["Telephone"]="Telefón",["ElectronicMail"]="E-mail",["EndpointID"]="Elektronická adresa",
        ["PaymentMeans"]="Platba",["PaymentMeansCode"]="Spôsob platby",["PaymentID"]="Referencia platby / VS",["PaymentDueDate"]="Splatnosť",["PayeeFinancialAccount"]="Bankový účet",["PaymentTerms"]="Platobné podmienky",["Delivery"]="Dodanie",["ActualDeliveryDate"]="Dátum dodania",["OrderReference"]="Objednávka",["BillingReference"]="Referencia faktúry",["AllowanceCharge"]="Zľava / prirážka",["ChargeIndicator"]="Prirážka (true) / zľava (false)",["Amount"]="Suma",["InvoicePeriod"]="Obdobie",["StartDate"]="Od",["EndDate"]="Do",["Description"]="Popis",["Price"]="Cena",["PriceAmount"]="Cena",["BaseQuantity"]="Základné množstvo",
        ["LineExtensionAmount"]="Súčet položiek bez DPH",["TaxExclusiveAmount"]="Základ dane",["TaxInclusiveAmount"]="Celkom s DPH",["AllowanceTotalAmount"]="Zľavy spolu",["ChargeTotalAmount"]="Prirážky spolu",["PrepaidAmount"]="Zaplatené preddavky",["PayableRoundingAmount"]="Zaokrúhlenie",["PayableAmount"]="NA ÚHRADU",["TaxAmount"]="DPH",["TaxableAmount"]="Základ dane",["Percent"]="Sadzba %",["TaxCategory"]="Kategória dane",["TaxExemptionReason"]="Dôvod oslobodenia",["TaxTotal"]="DPH",["TaxSubtotal"]="Rozpis",["AdditionalDocumentReference"]="Príloha / referencia"
    };
}

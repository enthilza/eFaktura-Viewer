using System.Xml.Linq;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using static eFakturaViewer.Invoice;

namespace eFakturaViewer;

internal static partial class InvoicePdf
{
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
        // V nahlade stacia obchodne identifikatory. Endpointy, schemy a technicke
        // identifikatory partnera zostavaju v XML strome, bez opakovania hodnot v PDF.
        foreach (var legal in data.Elements(A + "PartyLegalEntity"))
        {
            var id = Value(legal, "CompanyID"); if (id != "") lines.Add("IČO: " + id);
            var form = Value(legal, "CompanyLegalForm"); if (form != "") lines.Add(form);
        }
        foreach (var tax in data.Elements(A + "PartyTaxScheme"))
        {
            var id = Value(tax, "CompanyID");
            if (id != "") lines.Add((Value(tax.Element(A + "TaxScheme"), "ID") == "VAT" ? "IČ DPH: " : "DIČ: ") + id);
        }
        return lines.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
    }
    internal static bool PartyNeedsFlow(List<string> lines) => lines.Count > 18 || lines.Sum(x => x.Length) > 1200 || lines.Any(x => x.Length > 240) || (lines.Count > 0 && lines[0].Length > 160);
    static void Party(Cell cell, List<string> lines, string label)
    {
        var p = cell.AddParagraph(label); p.Format.Font.Size = 8; p.Format.Font.Color = Muted; p.Format.SpaceAfter = 8;
        for (int i = 0; i < lines.Count; i++)
        {
            p = cell.AddParagraph(Wrap(lines[i], 16)); p.Format.Font.Size = i == 0 ? 11 : 9; p.Format.SpaceAfter = i == 0 ? 7 : 3; p.Format.Font.Bold = i == 0;
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

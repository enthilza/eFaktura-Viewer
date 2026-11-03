using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace eFakturaViewer;

internal sealed record Attachment(string Name, XElement Element)
{
    public byte[] Read()
    {
        var bytes = Convert.FromBase64String(Element.Value);
        if (bytes.Length > Invoice.MaxBytes || bytes.Length < 5 || System.Text.Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
            throw new InvalidDataException("Vlozena priloha nie je citatelne PDF.");
        return bytes;
    }
}

internal sealed class Invoice
{
    internal const int MaxBytes = 30 * 1024 * 1024;
    internal static readonly XNamespace C = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    internal static readonly XNamespace A = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    internal const string Profile = "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0";
    internal XDocument Xml { get; }
    internal XElement Root => Xml.Root!;
    internal bool IsInvoice { get; }
    internal bool Credit => Root.Name.LocalName == "CreditNote";
    internal string Number => Value(Root, "ID");
    internal string Currency => Value(Root, "DocumentCurrencyCode");
    internal bool Extended => Root.Descendants(A + "AdditionalItemProperty").Any(x => Properties.Value.ContainsKey(Value(x, "Name")));
    internal List<Attachment> Attachments { get; }
    internal IEnumerable<XElement> Lines => Root.Elements(A + (Credit ? "CreditNoteLine" : "InvoiceLine"));
    internal byte[] Source { get; }
    internal string Path { get; }
    internal static string Value(XElement? e, string name) => e?.Element(C + name)?.Value ?? "";
    internal static string Date(string s) => DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "yyyy-MM-ddzzz", "yyyy-MM-dd'Z'" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("dd.MM.yyyy") : s;
    internal static string Money(string s) => decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n.ToString("N2", CultureInfo.GetCultureInfo("sk-SK")) : s;
    internal static Invoice Load(string path, CancellationToken token)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (f.Length > MaxBytes) throw new InvalidDataException("XML presahuje limit 30 MB.");
        using var m = new MemoryStream(); var buffer = new byte[65536]; int n;
        while ((n = f.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); if (m.Length + n > MaxBytes) throw new InvalidDataException("XML presahuje limit 30 MB."); m.Write(buffer, 0, n); }
        return Parse(m.ToArray(), path);
    }
    internal static Invoice Parse(byte[] bytes, string path = "")
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("XML presahuje limit 30 MB.");
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes, IgnoreComments = false });
        return new Invoice(XDocument.Load(reader, LoadOptions.PreserveWhitespace), bytes, path);
    }
    Invoice(XDocument doc, byte[] bytes, string path)
    {
        Xml = doc; Source = bytes; Path = path;
        if (doc.Root == null) throw new InvalidDataException("XML nema korenovy prvok.");
        IsInvoice = (Root.Name == XName.Get("Invoice", "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2") || Root.Name == XName.Get("CreditNote", "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2")) &&
            (Value(Root, "CustomizationID") == Profile || Value(Root, "CustomizationID").StartsWith(Profile + "#", StringComparison.Ordinal));
        if (IsInvoice && Lines.Take(2001).Count() > 2000) throw new InvalidDataException("Faktura presahuje limit 2000 poloziek.");
        Attachments = Root.Elements(A + "AdditionalDocumentReference").SelectMany(x => x.Elements(A + "Attachment")).SelectMany(x => x.Elements(C + "EmbeddedDocumentBinaryObject"))
            .Where(x => string.Equals((string?)x.Attribute("mimeCode"), "application/pdf", StringComparison.OrdinalIgnoreCase))
            .Select((x, i) => new Attachment((string?)x.Attribute("filename") ?? $"Priloha {i + 1}.pdf", x)).ToList();
    }
    static readonly Lazy<Dictionary<string, (string Label, bool Date)>> Properties = new(() =>
    {
        using var s = typeof(Invoice).Assembly.GetManifestResourceStream("eFakturaViewer.Assets.fmcg-properties.json")!;
        using var json = JsonDocument.Parse(s);
        return json.RootElement.EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!, x => (x.GetProperty("label").GetString()!, x.GetProperty("type").GetString() == "date"));
    });
    internal static string Property(XElement e)
    {
        var name = Value(e, "Name"); var value = Value(e, "Value");
        return Properties.Value.TryGetValue(name, out var p) ? $"{p.Label}: {(p.Date ? Date(value) : value)}" : $"{name}: {value}";
    }
}

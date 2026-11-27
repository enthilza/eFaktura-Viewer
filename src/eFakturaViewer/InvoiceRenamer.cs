using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace eFakturaViewer;

internal sealed class RenameReport
{
    internal List<MoveSuccess> Renamed { get; } = [];
    internal List<string> Problems { get; } = [];
    internal int AlreadyNamed { get; set; }
    internal int OtherXml { get; set; }
    internal bool Cancelled { get; set; }
}

internal static class InvoiceRenamer
{
    internal const string MissingTaxDate = "bez-datumu-ZP";
    sealed record Candidate(string Path, string Supplier, string Prefix, string Fingerprint, DateTime Modified);
    static readonly Regex Named = new(@"^(?<supplier>.+)_(?<issue>\d{4}-\d{2}-\d{2})_(?<tax>\d{4}-\d{2}-\d{2}|bez-datumu-ZP)_(?<number>[1-9][0-9]*)\.xml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string Supplier(Invoice invoice)
    {
        var party = invoice.Root.Element(Invoice.A + "AccountingSupplierParty")?.Element(Invoice.A + "Party");
        var name = Invoice.Value(party?.Element(Invoice.A + "PartyLegalEntity"), "RegistrationName");
        if (string.IsNullOrWhiteSpace(name)) name = Invoice.Value(party?.Element(Invoice.A + "PartyName"), "Name");
        var clean = new StringBuilder();
        foreach (var rune in name.Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            // Zachovame diakritiku. Windows zakazuje riadiace znaky a <>:"/\|?*;
            // kratky limit nechava miesto datumom, cislu a pripone aj pri dlhom nazve firmy.
            if (clean.Length + rune.Utf16SequenceLength > 120) break;
            clean.Append(rune.Value < 32 || "<>:\"/\\|?*".Contains(rune.ToString(), StringComparison.Ordinal) ? "-" : rune.ToString());
        }
        var result = Regex.Replace(clean.ToString(), @"\s+", " ").Trim(' ', '.');
        if (result.Length == 0 || result.All(c => c is '-' or '_' or '.')) throw new IOException("V XML chýba použiteľný názov dodávateľa.");
        // Windows rezervuje napr. CON aj pred bodkou. Pripona s datumom preto
        // sama nestaci pre dodavatela CON.s.r.o.; neutralny znak zmeni prvy segment.
        if (Regex.IsMatch(result.Split('.')[0].TrimEnd(), @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            result = "_" + result; // Rezervovane mena su kratke, ale ponechame rovnaky limit aj pri dodatkoch.
        if (result.Length > 120) result = result[..(char.IsHighSurrogate(result[119]) ? 119 : 120)];
        return result;
    }
    static string DatePart(string value, string description)
    {
        var date = Invoice.Date(value.Trim());
        if (!DateOnly.TryParseExact(date, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new IOException("V XML chýba alebo nie je platný " + description + ".");
        return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
    internal static string Prefix(Invoice invoice, string supplier)
    {
        var tax = Invoice.Value(invoice.Root, "TaxPointDate");
        return supplier + "_" + DatePart(Invoice.Value(invoice.Root, "IssueDate"), "dátum vystavenia") + "_" +
            (string.IsNullOrWhiteSpace(tax) ? MissingTaxDate : DatePart(tax, "dátum zdaniteľného plnenia")) + "_";
    }
    static bool Existing(string path, out string supplier, out long number)
    {
        var match = Named.Match(Path.GetFileName(path)); supplier = match.Groups["supplier"].Value; number = 0;
        if (!match.Success || !DateOnly.TryParseExact(match.Groups["issue"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
        var tax = match.Groups["tax"].Value;
        if (!tax.Equals(MissingTaxDate, StringComparison.OrdinalIgnoreCase) && !DateOnly.TryParseExact(tax, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
        if (!long.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number)) number = long.MaxValue;
        return true;
    }
    internal static RenameReport Run(string directory, IProgress<int>? progress, CancellationToken token)
    {
        // Zachovame pouzivatelovu podobu cesty (napr. mapovany disk): pod nou su
        // ulozene priradenia aj otvorena faktura. SMB zamok funguje aj cez UNC alias.
        directory = WorkflowStore.DirectoryPath(directory);
        using var batchLock = DirectoryBatchLock.Acquire(directory);
        var report = new RenameReport(); var candidates = new List<Candidate>();
        var highest = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        // Rezervujeme cisla aj z necitatelnych XML a rovnako pomenovanych adresarov.
        // Nevyprazdnene medzery v cislovani nevyuzivame: pokracujeme za najvyssim cislom.
        var entries = Directory.GetFileSystemEntries(directory);
        foreach (var entry in entries)
            if (Existing(entry, out var supplier, out var number)) highest[supplier] = Math.Max(highest.GetValueOrDefault(supplier), number);
        var paths = entries.Where(p => Path.GetExtension(p).Equals(".xml", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(p)).ToArray();
        for (int i = 0; i < paths.Length; i++)
        {
            if (token.IsCancellationRequested) { report.Cancelled = true; return report; }
            try
            {
                batchLock.EnsureHeld();
                var path = ViewerWindow.CanonicalPath(paths[i]); var invoice = Invoice.Load(path, token);
                if (!invoice.IsInvoice) { report.OtherXml++; continue; }
                var supplier = Supplier(invoice);
                if (Existing(path, out var namedSupplier, out _) && supplier.Equals(namedSupplier, StringComparison.OrdinalIgnoreCase)) { report.AlreadyNamed++; continue; }
                candidates.Add(new(path, supplier, Prefix(invoice, supplier), WorkflowStore.Fingerprint(invoice.Source), File.GetLastWriteTimeUtc(path)));
            }
            catch (BatchLockLostException e) { report.Problems.Add(e.Message); return report; }
            catch (OperationCanceledException) { report.Cancelled = true; return report; }
            catch (Exception e) { report.Problems.Add(Path.GetFileName(paths[i]) + ": " + e.Message); }
            finally { progress?.Report((i + 1) * 40 / Math.Max(1, paths.Length)); }
        }
        var ordered = candidates.OrderBy(x => x.Modified).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        for (int i = 0; i < ordered.Length; i++)
        {
            if (token.IsCancellationRequested) { report.Cancelled = true; break; }
            var item = ordered[i];
            try
            {
                batchLock.EnsureHeld();
                string NextName()
                {
                    var last = highest.GetValueOrDefault(item.Supplier);
                    if (last == long.MaxValue) throw new IOException("Poradové číslo dodávateľa je príliš veľké.");
                    highest[item.Supplier] = ++last;
                    return item.Prefix + last.ToString(CultureInfo.InvariantCulture) + ".xml";
                }
                var result = InvoiceMover.RenameInPlace(item.Path, item.Fingerprint, NextName, batchLock.EnsureHeld, token);
                report.Renamed.Add(result.File);
                if (result.Warning != null) report.Problems.Add(result.Warning);
            }
            catch (BatchLockLostException e) { report.Problems.Add(e.Message); break; }
            catch (OperationCanceledException) { report.Cancelled = true; break; }
            catch (Exception e) { report.Problems.Add(Path.GetFileName(item.Path) + ": " + e.Message); }
            progress?.Report(40 + (i + 1) * 60 / ordered.Length);
        }
        return report;
    }
}

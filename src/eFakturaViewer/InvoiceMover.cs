using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace eFakturaViewer;

internal sealed record MoveCandidate(string Source, string DestinationDirectory, string Fingerprint);
internal sealed record MoveSuccess(string Source, string Destination, string Fingerprint);
internal sealed class MoveReport
{
    internal List<MoveSuccess> Moved { get; } = [];
    internal List<string> Problems { get; } = [];
    internal int OtherXml { get; set; }
    internal bool Cancelled { get; set; }
}

internal static class InvoiceMover
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetFileInformationByHandle(SafeFileHandle file, int kind, byte[] info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint size, uint flags);

    internal static MoveReport Run(string directory, WorkflowState state, IProgress<int>? progress, CancellationToken token)
    {
        if (state.Folders.Count == 0) throw new IOException("Najprv nastavte cieľové priečinky.");
        var resolvedSource = ResolveDirectory(directory);
        var mutexName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resolvedSource.ToUpperInvariant())));
        using var mutex = new Mutex(false, @"Local\eFakturaViewer.Move-" + mutexName);
        bool held; try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held) throw new IOException("Z tohto priečinka práve presúva faktúry iné okno.");
        try
        {
            var result = new MoveReport(); var plan = new List<MoveCandidate>();
            // Zoznam zmrazime pri kliknuti. Subory dorucene pocas operacie ostavaju
            // na dalsie triedenie, namiesto neobmedzeneho presuvania novych prirastkov.
            var paths = Directory.GetFiles(directory).Where(x => System.IO.Path.GetExtension(x).Equals(".xml", StringComparison.OrdinalIgnoreCase)).ToArray();
            for (int i = 0; i < paths.Length; i++)
            {
                if (token.IsCancellationRequested) { result.Cancelled = true; return result; }
                try
                {
                    var path = ViewerWindow.CanonicalPath(paths[i]); var invoice = Invoice.Load(path, token);
                    if (!invoice.IsInvoice) { result.OtherXml++; continue; }
                    if (WorkflowStore.Changed(state, path, invoice.Source)) throw new IOException("Súbor sa od priradenia zmenil. Znova mu vyberte cieľ.");
                    var folder = WorkflowStore.Selected(state, path, invoice.Source) ?? throw new IOException("Priradený cieľ už neexistuje v nastaveniach.");
                    plan.Add(new(path, folder.Path, WorkflowStore.Fingerprint(invoice.Source)));
                }
                catch (OperationCanceledException) { result.Cancelled = true; return result; }
                catch (Exception e) { result.Problems.Add(System.IO.Path.GetFileName(paths[i]) + ": " + e.Message); }
                progress?.Report(paths.Length == 0 ? 40 : (i + 1) * 40 / paths.Length);
            }
            for (int i = 0; i < plan.Count; i++)
            {
                if (token.IsCancellationRequested) { result.Cancelled = true; break; }
                try { result.Moved.Add(MoveOne(plan[i], token)); }
                catch (OperationCanceledException) { result.Cancelled = true; break; }
                catch (Exception e) { result.Problems.Add(System.IO.Path.GetFileName(plan[i].Source) + ": " + e.Message); }
                progress?.Report(40 + (i + 1) * 60 / plan.Count);
            }
            return result;
        }
        finally { mutex.ReleaseMutex(); }
    }

    internal static MoveSuccess MoveOne(MoveCandidate item, CancellationToken token, bool forceCopy = false)
    {
        token.ThrowIfCancellationRequested();
        if ((File.GetAttributes(item.Source) & (FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0)
            throw new IOException("Odkaz alebo súbor iba na čítanie nebol presunutý.");
        // DELETE pristup dovoluje presun presne otvoreneho suboru. Share=READ
        // blokuje cudzie zapisy aj vymenu cesty medzi kontrolou obsahu a presunom.
        using var handle = CreateFile(item.Source, 0x80010000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) throw Error("Súbor sa nedá otvoriť na presun");
        using var source = new FileStream(handle, FileAccess.Read);
        if (source.Length > Invoice.MaxBytes || Convert.ToHexString(SHA256.HashData(source)) != item.Fingerprint)
            throw new IOException("Obsah súboru sa zmenil. Znova ho skontrolujte.");
        token.ThrowIfCancellationRequested();
        var target = WorkflowStore.DirectoryPath(item.DestinationDirectory);
        Directory.CreateDirectory(target);
        if (string.Equals(System.IO.Path.GetDirectoryName(FinalPath(handle)), ResolveDirectory(target), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Cieľový priečinok je totožný so zdrojovým.");
        var destination = AvailableName(target, System.IO.Path.GetFileName(item.Source));
        int error = forceCopy ? 17 : Rename(handle, destination);
        for (int attempts = 0; error is 80 or 183 && attempts < 100; attempts++)
        { destination = AvailableName(target, System.IO.Path.GetFileName(item.Source)); error = Rename(handle, destination); }
        if (error == 0) return new(item.Source, destination, item.Fingerprint);
        if (error != 17) throw new IOException("Presun sa nepodaril: " + new Win32Exception(error).Message);

        // Medzi diskami najprv zapiseme a overime docasnu kopiu v cieli. Pripona
        // nie je XML, aby cielovy workflow neprevzal este nedokonceny dokument.
        var temp = System.IO.Path.Combine(target, ".efaktura-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool published = false, created = false;
        try
        {
            using (var copy = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                created = true;
                source.Position = 0; var buffer = new byte[65536]; int count;
                while ((count = source.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); copy.Write(buffer, 0, count); }
                copy.Flush(true); copy.Position = 0;
                if (copy.Length != source.Length || Convert.ToHexString(SHA256.HashData(copy)) != item.Fingerprint) throw new IOException("Kópia neprešla kontrolou obsahu.");
            }
            token.ThrowIfCancellationRequested();
            for (int attempts = 0; ; attempts++)
            {
                try { File.Move(temp, destination, false); break; }
                catch (IOException) when (attempts < 100 && (File.Exists(destination) || Directory.Exists(destination)))
                { destination = AvailableName(target, System.IO.Path.GetFileName(item.Source)); }
            }
            published = true;
            // Po publikovani kopie uz nerusime polovicu presunu. Ak odstranenie
            // zdroja zlyha, priznane ponechame obe kopie; nezasahujeme do cieloveho workflow.
            if (!SetFileInformationByHandle(handle, 4, [1], 1))
                throw new IOException("Overená kópia je v " + destination + ", ale zdroj sa nepodarilo odstrániť: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
            return new(item.Source, destination, item.Fingerprint);
        }
        finally { if (created && !published) { try { File.Delete(temp); } catch { /* Zdroj stale existuje; zlyhanie upratania nesmie zamaskovat pricinu. */ } } }
    }

    static string AvailableName(string directory, string name)
    {
        var candidate = System.IO.Path.Combine(directory, name); int i = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            if (i > 100000) throw new IOException("V cieľovom priečinku je príliš veľa rovnakých názvov.");
            candidate = System.IO.Path.Combine(directory, System.IO.Path.GetFileNameWithoutExtension(name) + " (" + i++ + ")" + System.IO.Path.GetExtension(name));
        }
        return candidate;
    }
    static int Rename(SafeFileHandle handle, string destination)
    {
        // FILE_RENAME_INFO ma na x64 zarovnany HANDLE na offsete 8 a nazov na 20.
        // ReplaceIfExists je nula: ani kolizia vzniknuta pocas operacie nic neprepise.
        var name = Encoding.Unicode.GetBytes(destination); int rootOffset = IntPtr.Size == 8 ? 8 : 4;
        int lengthOffset = rootOffset + IntPtr.Size, nameOffset = lengthOffset + 4;
        var data = new byte[nameOffset + name.Length + 2]; BitConverter.GetBytes(name.Length).CopyTo(data, lengthOffset); name.CopyTo(data, nameOffset);
        return SetFileInformationByHandle(handle, 3, data, (uint)data.Length) ? 0 : Marshal.GetLastWin32Error();
    }
    internal static string ResolveDirectory(string path)
    {
        using var handle = CreateFile(WorkflowStore.DirectoryPath(path), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw Error("Priečinok nie je dostupný");
        return FinalPath(handle);
    }
    static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(32768); var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw Error("Cestu súboru sa nepodarilo overiť");
        var path = buffer.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path[8..];
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path[4..];
        return System.IO.Path.TrimEndingDirectorySeparator(path);
    }
    static IOException Error(string text) => new(text + ": " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
}

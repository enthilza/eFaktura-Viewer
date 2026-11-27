using System.IO;

namespace eFakturaViewer;

internal sealed class BatchLockLostException(Exception inner) : IOException("Spojenie so zámkom priečinka sa prerušilo. Operácia bola zastavená.", inner);

internal sealed class DirectoryBatchLock : IDisposable
{
    internal const string FileName = ".efaktura-premenuj.lock";
    readonly FileStream stream;
    DirectoryBatchLock(FileStream stream) => this.stream = stream;

    internal static DirectoryBatchLock Acquire(string directory)
    {
        try
        {
            // O vlastnictve rozhoduje serverove vyhradne otvorenie, nie existencia suboru.
            // OpenOrCreate dovoli pouzit aj pozostatok po pade. Aktivny zamok sa nikdy nemaze.
            var stream = new FileStream(Path.Combine(directory, FileName), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose | FileOptions.WriteThrough);
            var result = new DirectoryBatchLock(stream);
            try { result.EnsureHeld(); return result; } catch { stream.Dispose(); throw; }
        }
        catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33)
        { throw new IOException("V tomto priečinku práve prebieha premenovanie alebo presun faktúr v inom okne či počítači. Skúste to po jeho dokončení.", e); }
        catch (UnauthorizedAccessException e)
        { throw new IOException("V priečinku sa nepodarilo získať zámok. Overte právo vytvárať a odstraňovať súbory.", e); }
    }

    internal void EnsureHeld()
    {
        // Zapisy idu cez povodny handle na server. Po strate SMB spojenia nesmieme
        // potichu otvorit novy zamok a pokracovat so starym zoznamom a cislovanim.
        try { stream.Position = 0; stream.WriteByte(1); stream.Flush(true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
        { throw new BatchLockLostException(e); }
    }
    public void Dispose() => stream.Dispose();
}

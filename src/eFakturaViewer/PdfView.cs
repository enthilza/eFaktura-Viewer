using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace eFakturaViewer;

internal sealed class PdfPageLimitException(uint pages) : Exception($"PDF má {pages} strán. Náhľad podporuje najviac 1000 strán. Úplné PDF môžete uložiť cez ponuku Uložiť PDF; údaje zostávajú dostupné v strome XML.");

internal sealed class PdfView : IDisposable
{
    // Dokument si zo streamu docitava strany neskor. Stream preto zije rovnako dlho ako PdfView,
    // nie iba pocas LoadFromStreamAsync.
    readonly InMemoryRandomAccessStream source;
    internal PdfDocument Document { get; }
    PdfView(InMemoryRandomAccessStream stream, PdfDocument doc) { source = stream; Document = doc; }
    internal static async Task<PdfView> Open(byte[] data, string password = "")
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(data); await writer.StoreAsync(); await writer.FlushAsync(); }
            var doc = await PdfDocument.LoadFromStreamAsync(stream, password);
            if (doc.PageCount == 0) throw new InvalidDataException("PDF neobsahuje žiadnu stranu.");
            if (doc.PageCount > 1000) throw new PdfPageLimitException(doc.PageCount);
            return new PdfView(stream, doc);
        }
        catch { stream.Dispose(); throw; }
    }
    internal async Task<BitmapSource> Render(uint index, uint width)
    {
        using var page = Document.GetPage(index);
        using var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = width });
        using var input = stream.AsStreamForRead();
        // OnLoad odpoji bitmapu od docasneho streamu pred jeho zavretim. Freeze umozni
        // pouzit hotovy obraz aj po navrate z vykreslovania bez dalsich zmien jeho obsahu.
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = input; image.EndInit(); image.Freeze(); return image;
    }
    public void Dispose() => source.Dispose();
}

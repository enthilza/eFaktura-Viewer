using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace eFakturaViewer;

internal sealed class PdfPrintPaginator(PdfView view, Size pageSize) : DocumentPaginator
{
    public override bool IsPageCountValid => true;
    public override int PageCount => (int)view.Document.PageCount;
    public override Size PageSize { get; set; } = pageSize;
    public override IDocumentPaginatorSource Source => null!;
    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= PageCount) return DocumentPage.Missing;
        // Tlaciaren si pyta jednotlive strany. Neukladame stovky bitmap do FixedDocument.
        // WinRT renderer pracuje mimo UI; zmrazena bitmapa sa potom vykresli na tlacovom vlakne.
        var bitmap = Task.Run(() => view.Render((uint)pageNumber, 1800)).GetAwaiter().GetResult();
        var factor = Math.Min(PageSize.Width / bitmap.PixelWidth, PageSize.Height / bitmap.PixelHeight);
        var rect = new Rect((PageSize.Width - bitmap.PixelWidth * factor) / 2, (PageSize.Height - bitmap.PixelHeight * factor) / 2, bitmap.PixelWidth * factor, bitmap.PixelHeight * factor);
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawImage(bitmap, rect);
        return new DocumentPage(visual, PageSize, rect, rect);
    }
}

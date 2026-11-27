using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace eFakturaViewer;

internal sealed partial class ViewerWindow
{
    bool CanRenameInvoices => !movingInvoices && workflowDirectory != null && files.Count > 0 && directoryWarning.Length == 0;
    async Task RenameInvoices()
    {
        if (!CanRenameInvoices) return;
        var directory = workflowDirectory!; var previous = currentPath; RenameReport? report = null;
        // Rovnake blokovanie ako pri presune: ziadne listovanie, dalsia davka ani
        // automaticka aktualizacia uprostred zmeny nazvov. Obsah XML sa nezapisuje.
        movingInvoices = true; RefreshWorkflowButtons();
        try
        {
            AppDialog.Work(this, "Premenovanie faktúr", "Načítavam dodávateľov a dátumy…", (progress, token) =>
            {
                report = InvoiceRenamer.Run(directory, progress, token); return Task.CompletedTask;
            }, p => p < 40 ? $"Kontrolujem názvy · {p} %" : $"Premenúvam faktúry · {p} %");
            if (report == null) return;
            var opened = report.Renamed.FirstOrDefault(x => string.Equals(x.Source, previous, StringComparison.OrdinalIgnoreCase))?.Destination ?? previous;
            movingInvoices = false;
            if (opened != null && File.Exists(opened)) await OpenPath(opened);
            else if (report.Renamed.Count > 0) await OpenPath(report.Renamed[0].Destination);
            var summary = $"Premenované: {report.Renamed.Count}. Už pomenované: {report.AlreadyNamed}.";
            if (report.OtherXml > 0) summary += $" Ostatné XML: {report.OtherXml}.";
            status.Text = summary;
            if (report.Problems.Count > 0 || report.Cancelled)
            {
                var dialog = new AppDialog(this, report.Cancelled ? "Premenovanie bolo prerušené" : "Výsledok premenovania", summary);
                dialog.AddContent(new ScrollViewer
                {
                    MaxHeight = Math.Max(120, Math.Min(360, ActualHeight - 250)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock { Text = string.Join("\n\n", report.Problems), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) }
                });
                dialog.Actions.Children.Add(VisualStyle.Button("", "Rozumiem", "Zavrieť", dialog.Close)); dialog.ShowDialog();
            }
        }
        catch (Exception e) { AppDialog.Info(this, "Premenovanie faktúr", e.Message); }
        finally
        {
            movingInvoices = false; RefreshWorkflows();
            // Vypnute tlacidlo Premenuj strati fokus a modalne okno ho nema kam vratit.
            // Pockame aj na obnovenie nahladu a tlacidiel. Stabilne tlacidlo vysky
            // zostava zapnute aj pri dalsom listovani; cudziemu oknu fokus neberieme.
            await Dispatcher.InvokeAsync(() =>
            {
                if (!closed && IsVisible && IsActive && !IsKeyboardFocusWithin &&
                    activeMenu?.IsOpen != true && !sources.IsDropDownOpen &&
                    !OwnedWindows.Cast<Window>().Any(w => w.IsVisible))
                    fitHeightButton.Focus();
            }, DispatcherPriority.Input);
        }
    }
    internal Task TestRenameInvoices() => RenameInvoices();
    internal Button RenameButtonForTest => renameButton;
}

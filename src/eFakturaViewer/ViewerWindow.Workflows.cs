using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace eFakturaViewer;

internal sealed partial class ViewerWindow
{
    WorkflowState workflowState = new();
    readonly StackPanel workflowPanel = new() { Margin = new Thickness(8, 0, 8, 8), Visibility = Visibility.Collapsed };
    readonly WrapPanel workflowButtons = new();
    readonly TextBlock workflowTarget = new() { FontSize = 11, Margin = new Thickness(2, 3, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    Button moveInvoicesButton = null!;
    bool movingInvoices;
    string? workflowDirectory;

    void InitializeWorkflows()
    {
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(workflowPanel, 4); Grid.SetColumnSpan(workflowPanel, 2); header.Children.Add(workflowPanel);
        workflowPanel.Children.Add(new ScrollViewer { Content = workflowButtons, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 104 });
        workflowPanel.Children.Add(workflowTarget); workflowTarget.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Activated += (_, _) => { if (!movingInvoices) RefreshWorkflows(); };
        RefreshWorkflows();
    }
    void RefreshWorkflows()
    {
        try { workflowState = WorkflowStore.Load(); RefreshWorkflowButtons(); }
        catch (Exception e) { workflowState = new(); workflowPanel.Visibility = Visibility.Collapsed; status.Text = "Triedenie nie je dostupné: " + e.Message; }
    }
    void RefreshWorkflowButtons()
    {
        workflowButtons.Children.Clear();
        workflowPanel.Visibility = workflowState.Folders.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (workflowState.Folders.Count == 0) return;
        bool ready = invoice?.IsInvoice == true && currentPath != null && !movingInvoices;
        bool changed = ready && WorkflowStore.Changed(workflowState, currentPath!, invoice!.Source);
        var selected = ready && !changed ? WorkflowStore.Selected(workflowState, currentPath, invoice!.Source) : null;
        workflowButtons.Children.Add(new TextBlock { Text = "Cieľ:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), FontSize = 12 });
        for (int i = 0; i < workflowState.Folders.Count; i++)
        {
            int index = i; var folder = workflowState.Folders[i];
            var shortcut = i < 9 ? $"Kláves {i + 1}" : i == 9 ? "Kláves 0" : "Výber kliknutím";
            var button = VisualStyle.Button("", (i + 1) + " · " + folder.Name, folder.Name + "\n" + folder.Path + "\n" + shortcut, () => AssignWorkflow(index));
            // Dlhy priatelsky nazov nesmie vytlacit ostatne ciele mimo uzkeho okna.
            // Plny nazov a cesta zostavaju v popise po podrzani mysi.
            if (button.Content is StackPanel content && content.Children[0] is TextBlock label)
            { label.MaxWidth = 168; label.TextTrimming = TextTrimming.CharacterEllipsis; }
            button.MinWidth = 32; button.Padding = new Thickness(8, 6, 8, 6); button.Margin = new Thickness(0, 0, 5, 3); button.IsEnabled = ready;
            button.SetResourceReference(Control.BackgroundProperty, folder.Id == selected?.Id ? "Accent" : "ButtonBackground");
            button.SetResourceReference(Control.ForegroundProperty, folder.Id == selected?.Id ? "AccentText" : "Text");
            workflowButtons.Children.Add(button);
        }
        moveInvoicesButton = VisualStyle.Button("move", "Presunúť faktúry", "Presunúť všetky faktúry z otvoreného priečinka podľa priradených cieľov", () => _ = MoveInvoices());
        moveInvoicesButton.IsEnabled = !movingInvoices && workflowDirectory != null && files.Count > 0 && directoryWarning.Length == 0;
        moveInvoicesButton.Margin = new Thickness(5, 0, 0, 3); workflowButtons.Children.Add(moveInvoicesButton);
        workflowTarget.Text = changed ? "Doklad sa od priradenia zmenil. Vyberte cieľ znova." : selected != null ? "Priradené: " + selected.Name : "Nepriradené faktúry pôjdu do: " + workflowState.Folders[0].Name;
        workflowTarget.ToolTip = selected?.Path ?? workflowState.Folders[0].Path;
    }
    void AssignWorkflow(int index)
    {
        if (movingInvoices || invoice?.IsInvoice != true || currentPath == null || index < 0 || index >= workflowState.Folders.Count) return;
        try { workflowState = WorkflowStore.Assign(currentPath, invoice.Source, workflowState.Folders[index].Id); RefreshWorkflowButtons(); }
        catch (Exception e) { AppDialog.Info(this, "Zaradenie faktúry", e.Message); RefreshWorkflows(); }
    }
    bool TryWorkflowKey(Key key)
    {
        if (movingInvoices || invoice?.IsInvoice != true || tree.IsKeyboardFocusWithin || sources.IsKeyboardFocusWithin || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return false;
        int digit = key is >= Key.D0 and <= Key.D9 ? key - Key.D0 : key is >= Key.NumPad0 and <= Key.NumPad9 ? key - Key.NumPad0 : -1;
        if (digit < 0) return false;
        int index = digit == 0 ? 9 : digit - 1;
        if (index >= workflowState.Folders.Count) return false;
        AssignWorkflow(index); return true;
    }
    void ConfigureWorkflows()
    {
        if (movingInvoices) return;
        try { WorkflowDialog.Edit(this); RefreshWorkflows(); }
        catch (Exception e) { AppDialog.Info(this, "Cieľové priečinky", e.Message); }
    }
    async Task MoveInvoices()
    {
        if (movingInvoices || workflowDirectory == null || files.Count == 0 || directoryWarning.Length > 0) return;
        var directory = workflowDirectory; var previous = currentPath; MoveReport? report = null;
        movingInvoices = true; RefreshWorkflowButtons();
        try
        {
            var snapshot = WorkflowStore.Load();
            AppDialog.Work(this, "Presúvanie faktúr", "Kontrolujem faktúry v otvorenom priečinku…", (progress, token) =>
            {
                report = InvoiceMover.Run(directory, snapshot, progress, token); return Task.CompletedTask;
            }, p => p < 40 ? $"Kontrolujem faktúry · {p} %" : $"Presúvam faktúry · {p} %");
            if (report == null) return;
            try { WorkflowStore.RemoveMoved(report.Moved); }
            catch (Exception e) { report.Problems.Add("Priradenia presunutých súborov sa nepodarilo vyčistiť: " + e.Message); }
            if (report.Moved.Count > 0) ClearMovedPreview();
            movingInvoices = false;
            if (report.Problems.Count > 0 || report.Cancelled)
            {
                var remaining = previous != null && File.Exists(previous) ? previous : Directory.GetFiles(directory).FirstOrDefault(p => Path.GetExtension(p).Equals(".xml", StringComparison.OrdinalIgnoreCase));
                if (remaining != null) await OpenPath(remaining);
                var dialog = new AppDialog(this, report.Cancelled ? "Presun bol prerušený" : "Časť faktúr zostala na mieste", $"Presunuté faktúry: {report.Moved.Count}. Nepresunuté súbory zostávajú v zdrojovom priečinku.");
                var text = new TextBlock { Text = string.Join("\n\n", report.Problems), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
                dialog.AddContent(new ScrollViewer { Content = text, MaxHeight = Math.Max(120, Math.Min(360, ActualHeight - 250)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                dialog.Actions.Children.Add(VisualStyle.Button("", "Rozumiem", "Zavrieť", dialog.Close)); dialog.ShowDialog();
            }
            else status.Text = report.Moved.Count > 0 ? $"Presunuté faktúry: {report.Moved.Count}." + (report.OtherXml > 0 ? " Ostatné XML zostali na mieste." : "") : "V priečinku nie sú rozpoznané faktúry na presun.";
        }
        catch (Exception e) { AppDialog.Info(this, "Presun faktúr", e.Message); }
        finally { movingInvoices = false; RefreshWorkflows(); }
    }
    void ClearMovedPreview()
    {
        ++sequence; request.Cancel(); invoice = null; currentPath = null; files.Clear(); ClearPdf();
        sources.Items.Clear(); sources.Visibility = Visibility.Collapsed; embeddedSwitch.IsEnabled = false; embeddedSwitch.IsChecked = false;
        filename.Text = counter.Text = badge.Text = ""; dock.Visibility = Visibility.Collapsed; Title = "eFaktúra Viewer";
        ShowTree(null, ""); RefreshWorkflowButtons();
    }
    internal string? WorkflowTargetForTest => WorkflowStore.Selected(workflowState, currentPath, invoice?.Source)?.Path;
    internal WrapPanel WorkflowButtonsForTest => workflowButtons;
    internal void TestAssignWorkflow(int index) => AssignWorkflow(index);
    internal void TestClearMovedPreview() => ClearMovedPreview();
    internal Task TestMoveInvoices() => MoveInvoices();
}

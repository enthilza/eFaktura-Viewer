using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace eFakturaViewer;

internal sealed class AppDialog : Window
{
    readonly StackPanel body = new() { Margin = new Thickness(28, 20, 28, 24) };
    readonly TextBlock message;
    readonly StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
    readonly Border shell;
    internal AppDialog(Window owner, string title, string text)
    {
        Owner = owner; Title = title; Width = Math.Min(490, Math.Max(300, owner.ActualWidth - 24)); SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Icon = owner.Icon;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 13;
        shell = new Border { Margin = new Thickness(12), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Child = body, Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = .3 } };
        shell.SetResourceReference(Border.BackgroundProperty, "Panel"); shell.SetResourceReference(Border.BorderBrushProperty, "Border"); Content = shell;
        var top = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 15) };
        top.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 30, 0) });
        var close = VisualStyle.Button("close", "", "Zavrieť", Close); close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top; close.Padding = new Thickness(6); close.MinHeight = 28; top.Children.Add(close);
        top.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        body.Children.Add(top);
        message = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 21 }; message.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); body.Children.Add(message);
        body.Children.Add(footer);
        SourceInitialized += (_, _) => VisualStyle.RoundedShell(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
    internal static bool Confirm(Window owner, string title, string message, string accept = "Pokračovať")
    {
        var window = new AppDialog(owner, title, message);
        var cancel = VisualStyle.Button("", "Zrušiť", "Zrušiť", () => window.DialogResult = false); cancel.IsCancel = true;
        var ok = VisualStyle.Button("", accept, accept, () => window.DialogResult = true); ok.IsDefault = true;
        ok.SetResourceReference(Button.BackgroundProperty, "Accent"); ok.SetResourceReference(Button.ForegroundProperty, "AccentText");
        window.footer.Children.Add(cancel); window.footer.Children.Add(ok); return window.ShowDialog() == true;
    }
    internal static void Info(Window owner, string title, string message)
    {
        var window = new AppDialog(owner, title, message); var ok = VisualStyle.Button("", "Rozumiem", "Zavrieť", window.Close); ok.IsDefault = true; window.footer.Children.Add(ok); window.ShowDialog();
    }
    internal void AddContent(UIElement content) => body.Children.Insert(body.Children.Count - 1, content);
    internal StackPanel Actions => footer;
    internal void Message(string text) => message.Text = text;
    internal static bool Work(Window owner, string title, string text, Func<IProgress<int>, CancellationToken, Task> operation, Func<int, string>? progressText = null)
    {
        using var cancellation = new CancellationTokenSource();
        var window = new AppDialog(owner, title, text); bool finished = false, cancelled = false; Exception? error = null;
        var bar = new ProgressBar { Height = 5, Minimum = 0, Maximum = 100, IsIndeterminate = true, Margin = new Thickness(0, 18, 0, 0), BorderThickness = new Thickness(0) };
        bar.SetResourceReference(ProgressBar.ForegroundProperty, "Accent"); bar.SetResourceReference(ProgressBar.BackgroundProperty, "Input"); window.body.Children.Insert(2, bar);
        var cancel = VisualStyle.Button("", "Zrušiť", "Zrušiť", () => { cancellation.Cancel(); }); window.footer.Children.Add(cancel);
        // Krizik ani Escape nesmu vratit riadenie volajucemu, kym operacia este pracuje.
        // Najprv poziadame o zrusenie a dialog zavrieme az po jej dobehnuti a uvolneni suborov.
        window.Closing += (_, e) => { if (!finished) { e.Cancel = true; cancellation.Cancel(); cancel.IsEnabled = false; window.message.Text = "Ukončujem operáciu…"; } };
        window.Loaded += async (_, _) =>
        {
            try
            {
                // Progress vznikne na UI vlakne a vracia sem aktualizacie z Task.Run.
                // Priznak finished ignoruje oneskorene hlasenia po ukonceni operacie.
                var progress = new Progress<int>(p => { if (!finished) { bar.IsIndeterminate = false; bar.Value = p; window.message.Text = progressText?.Invoke(p) ?? (p >= 100 ? "Overujem digitálny podpis…" : $"Sťahujem aktualizáciu · {p} %"); } });
                await Task.Run(() => operation(progress, cancellation.Token));
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception ex) { error = ex; }
            finally { finished = true; window.Close(); }
        };
        window.ShowDialog(); if (error != null) throw error; return !cancelled;
    }
    internal static AppDialog Preview(Window owner)
    {
        var window = new AppDialog(owner, "Aktualizácia aplikácie", "Máte aktuálnu verziu eFaktúra Viewer.\nKontrola aktualizácií bola dokončená.");
        window.footer.Children.Add(VisualStyle.Button("", "Rozumiem", "Zavrieť", window.Close)); return window;
    }
}

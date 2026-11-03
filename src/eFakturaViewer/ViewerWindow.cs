using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;

namespace eFakturaViewer;

internal sealed class ViewerWindow : Window
{
    readonly Preferences preferences = Preferences.Load();
    readonly Border shell = new(), dock = new();
    readonly Grid header = new() { Background = Brushes.Transparent };
    readonly StackPanel headerTools = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    readonly TextBlock logo = new() { Text = "eFaktúra", FontSize = 18, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 12, 0) };
    readonly Grid root = new(), stage = new();
    readonly StackPanel pages = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock filename = new(), counter = new(), status = new(), pageCounter = new(), emptyTitle = new();
    readonly TextBlock badge = new() { FontSize = 10, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 8, 0) };
    readonly ComboBox sources = new() { Height = 34, Margin = new Thickness(8, 0, 8, 8), Visibility = Visibility.Collapsed };
    readonly CheckBox embeddedSwitch = new() { Content = "Vlozene PDF", IsEnabled = false, Opacity = .4, Margin = new Thickness(16, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Zobrazit povodne PDF ulozene v XML" };
    readonly Button fitHeightButton, fitWidthButton;
    readonly TreeView tree = new() { BorderThickness = new Thickness(0), Padding = new Thickness(12) };
    readonly StackPanel xmlPanel = new();
    readonly List<Button> buttons = new();
    readonly SemaphoreSlim rendering = new(1, 1);
    static readonly SemaphoreSlim generation = new(1, 1);
    readonly List<Image> images = new();
    readonly List<Border> paper = new();
    CancellationTokenSource request = new();
    List<string> files = new();
    Invoice? invoice;
    PdfView? pdf;
    byte[]? pdfBytes;
    string? currentPath;
    long sequence;
    internal enum FitMode { Height, Width, Manual }
    bool sourceChanging, closed, showingXml;
    FitMode fitMode = FitMode.Height;
    double scale = 1;
    readonly string? initialPath;
    UpdateOffer? update;
    bool updating;
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int StrCmpLogicalW(string a, string b);
    internal ViewerWindow(string? path)
    {
        initialPath = path; Title = "eFaktúra Viewer";
        Width = 680; Height = 960; MinWidth = 260; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.Manual; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; AllowsTransparency = true; Background = Brushes.Transparent;
        SourceInitialized += (_, _) => { VisualStyle.RoundedShell(this); WindowPlacement.Center(this); };
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 13;
        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream)
        { var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(stream, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad); Icon = decoder.Frames[^1]; }
        shell.CornerRadius = new CornerRadius(18); shell.Padding = new Thickness(14); shell.Margin = new Thickness(12);
        shell.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Opacity = .24 }; Content = shell; shell.Child = root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) }); header.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        brand.Children.Add(new Image { Source = Icon, Width = 24, Height = 24 }); brand.Children.Add(logo);
        header.Children.Add(brand); Grid.SetColumn(headerTools, 1); header.Children.Add(headerTools);
        headerTools.Children.Add(Button("☼", "Prepnúť schému", ToggleTheme));
        headerTools.Children.Add(Button("⟳", "Aktualizovať aplikáciu", () => _ = CheckUpdates(true)));
        headerTools.Children.Add(Button("⋯", "Dalsie nastroje", () => { var m = Menu(); m.PlacementTarget = headerTools; m.Placement = PlacementMode.Bottom; m.IsOpen = true; }));
        headerTools.Children.Add(Button("×", "Zavriet (Esc)", () => Close()));
        Grid.SetRow(badge, 1); Grid.SetColumnSpan(badge, 2); header.Children.Add(badge); root.Children.Add(header);
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var viewing = new WrapPanel { Margin = new Thickness(5, 6, 5, 10) }; Grid.SetRow(viewing, 2); Grid.SetColumnSpan(viewing, 2); header.Children.Add(viewing);
        fitHeightButton = VisualStyle.Button("height", "Prispôsobiť výške", "Prispôsobiť výške", () => Fit(FitMode.Height));
        fitWidthButton = VisualStyle.Button("width", "Prispôsobiť šírke", "Prispôsobiť šírke", () => Fit(FitMode.Width));
        viewing.Children.Add(fitHeightButton); viewing.Children.Add(fitWidthButton); viewing.Children.Add(embeddedSwitch);
        Grid.SetRow(sources, 3); Grid.SetColumnSpan(sources, 2); header.Children.Add(sources);
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!CanDragHeader(e.OriginalSource as DependencyObject, e.GetPosition(this))) return;
            e.Handled = true;
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        Grid.SetRow(stage, 1); root.Children.Add(stage); scroll.Content = pages; stage.Children.Add(scroll);
        xmlPanel.Children.Add(emptyTitle); xmlPanel.Children.Add(tree); var xmlScroll = new ScrollViewer { Content = xmlPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }; xmlScroll.Visibility = Visibility.Collapsed; stage.Children.Add(xmlScroll);
        dock.CornerRadius = new CornerRadius(14); dock.Padding = new Thickness(12); dock.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(dock, 2); root.Children.Add(dock);
        var controls = new StackPanel(); dock.Child = controls;
        var info = new DockPanel(); counter.FontSize = 11; counter.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(counter, Dock.Right); info.Children.Add(counter); filename.TextTrimming = TextTrimming.CharacterEllipsis; filename.FontSize = 12; info.Children.Add(filename); controls.Children.Add(info);
        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 9, 0, 0) }; controls.Children.Add(bar);
        bar.Children.Add(Button("‹", "Predosly subor (←)", () => Navigate(-1)));
        bar.Children.Add(Button("›", "Dalsi subor (→)", () => Navigate(1)));
        bar.Children.Add(Button("−", "Oddialit", () => Zoom(.85))); bar.Children.Add(Button("+", "Priblizit", () => Zoom(1.18)));
        pageCounter.FontSize = 10; pageCounter.HorizontalAlignment = HorizontalAlignment.Center; pageCounter.Margin = new Thickness(0, 7, 0, 0); controls.Children.Add(pageCounter);
        status.TextWrapping = TextWrapping.Wrap; status.FontSize = 11; status.Margin = new Thickness(8); status.HorizontalAlignment = HorizontalAlignment.Center; status.VerticalAlignment = VerticalAlignment.Bottom; status.IsHitTestVisible = false; stage.Children.Add(status);
        emptyTitle.FontSize = 22; emptyTitle.Margin = new Thickness(20); emptyTitle.TextWrapping = TextWrapping.Wrap;
        sources.SelectionChanged += async (_, _) => { if (!sourceChanging && invoice != null && embeddedSwitch.IsChecked == true) await SelectSource(sources.SelectedIndex + 1); };
        embeddedSwitch.Click += async (_, _) => { if (!sourceChanging && invoice != null) await SelectSource(embeddedSwitch.IsChecked == true ? Math.Max(0, sources.SelectedIndex) + 1 : 0); };
        embeddedSwitch.IsEnabledChanged += (_, _) => embeddedSwitch.Opacity = embeddedSwitch.IsEnabled ? 1 : .4;
        scroll.ScrollChanged += async (_, _) => await RenderVisible();
        scroll.SizeChanged += (_, _) => LayoutPages();
        PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Zoom(e.Delta > 0 ? 1.15 : .87); e.Handled = true; } };
        PreviewKeyDown += (_, e) =>
        {
            if (sources.IsDropDownOpen || e.OriginalSource is TextBox) return;
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Left && (!showingXml || !tree.IsKeyboardFocusWithin)) { Navigate(-1); e.Handled = true; }
            else if (e.Key == Key.Right && (!showingXml || !tree.IsKeyboardFocusWithin)) { Navigate(1); e.Handled = true; }
            else if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) OpenDialog();
            else if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) SavePdf();
            else if (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) _ = Print();
        };
        MouseRightButtonUp += (_, e) => { if (e.OriginalSource is not TextBox) { var menu = Menu(); menu.IsOpen = true; e.Handled = true; } };
        AllowDrop = true; Drop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) await OpenPath(paths[0]); };
        Closed += (_, _) => { closed = true; request.Cancel(); };
        Loaded += async (_, _) => { if (initialPath != null) await OpenPath(initialPath); else Welcome(); if (preferences.CheckUpdates) _ = CheckUpdates(false); };
        Theme();
    }
    static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    Button Button(string text, string tip, Action action)
    {
        var icon = text switch { "☼" => "theme", "⟳" => "update", "⋯" => "more", "×" => "close", "‹" => "left", "›" => "right", "−" => "minus", "+" => "plus", _ => "width" };
        var b = VisualStyle.Button(icon, "", tip, action); buttons.Add(b); return b;
    }
    void ToggleTheme() { preferences.Light = !preferences.Light; SaveSettings(); Theme(); }
    void Theme()
    {
        var light = preferences.Light; VisualStyle.Apply(light); Foreground = VisualStyle.Brush("Text");
        // Krytie iba pozadia: text, tlacidla a samotny dokument zostavaju ostre a citatelne.
        shell.Background = B(light ? "#E6F5F5F5" : "#E6191919"); dock.Background = B(light ? "#F7F7F7" : "#292929");
        shell.BorderBrush = B(light ? "#55FFFFFF" : "#55777777"); shell.BorderThickness = new Thickness(1);
        logo.Foreground = Foreground; status.Foreground = Foreground; status.Background = dock.Background;
        counter.Foreground = B(light ? "#626262" : "#BEBEBE"); pageCounter.Foreground = counter.Foreground; badge.Foreground = counter.Foreground;
        tree.Background = Brushes.Transparent; tree.Foreground = Foreground;
        UpdateFitButtons();
    }
    void SaveSettings() { try { preferences.Save(); } catch (Exception e) { status.Text = "Nastavenia sa neulozili: " + e.Message; } }
    void Welcome()
    {
        dock.Visibility = Visibility.Visible; filename.Text = "Otvorte XML alebo ho sem presunte"; status.Text = "Ctrl+O  •  Prave tlacidlo: nastroje";
        ShowTree(null, "eFaktúra Viewer\nVase faktury. Prehladne, lokalne.");
    }
    void OpenDialog() { var d = new OpenFileDialog { Filter = "XML subory|*.xml|Vsetky subory|*.*", InitialDirectory = preferences.LastDirectory }; if (d.ShowDialog(this) == true) _ = OpenPath(d.FileName); }
    internal async Task OpenPath(string path)
    {
        var id = ++sequence; request.Cancel(); request.Dispose(); request = new(); var token = request.Token;
        currentPath = Path.GetFullPath(path); var loadPath = currentPath; invoice = null; ClearPdf(); tree.Items.Clear(); showingXml = false; badge.Text = "";
        filename.Text = Path.GetFileName(path); Title = filename.Text + " — eFaktúra Viewer"; status.Text = "Nacitavam dokument…";
        sourceChanging = true; sources.Items.Clear(); embeddedSwitch.IsChecked = false; embeddedSwitch.IsEnabled = false; sources.Visibility = Visibility.Collapsed; sourceChanging = false;
        try
        {
            var directory = Path.GetDirectoryName(currentPath)!;
            var listing = await Task.Run(() => Directory.EnumerateFiles(directory).Where(p => string.Equals(Path.GetExtension(p), ".xml", StringComparison.OrdinalIgnoreCase)).ToList(), token);
            token.ThrowIfCancellationRequested(); if (id != sequence) return;
            if (!listing.Contains(currentPath, StringComparer.OrdinalIgnoreCase)) listing.Add(currentPath);
            listing.Sort((a, b) => StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)) * (preferences.ReverseSort ? -1 : 1)); files = listing;
            dock.Visibility = files.Count <= 1 ? Visibility.Collapsed : Visibility.Visible;
            counter.Text = $"{files.FindIndex(x => string.Equals(x, currentPath, StringComparison.OrdinalIgnoreCase)) + 1} / {files.Count}";
            preferences.LastDirectory = directory; SaveSettings();
            var loaded = await Task.Run(() => Invoice.Load(loadPath, token), token);
            if (id != sequence || closed) return; invoice = loaded;
            if (!loaded.IsInvoice) { ShowTree(loaded.Xml.Root, "Toto nie je eFaktúra."); status.Text = "XML struktura  •  Prave tlacidlo: nastroje a dalsi subor"; return; }
            badge.Text = loaded.Extended ? "BIS3 · ROZSIRENY FMCG" : "BIS3 · ZAKLADNY";
            sourceChanging = true; foreach (var a in loaded.Attachments) sources.Items.Add(a.Name); sources.SelectedIndex = loaded.Attachments.Count > 0 ? 0 : -1; embeddedSwitch.IsEnabled = loaded.Attachments.Count > 0; sourceChanging = false;
            await SelectSource(0);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (id == sequence && !closed) { ShowTree(null, "Subor sa nepodarilo otvorit."); status.Text = Readable(e); } }
    }
    static string Readable(Exception e) => e is System.Xml.XmlException ? "XML nie je spravne zapisane alebo obsahuje nepovolene DTD. " + e.Message : e.Message;
    async Task SelectSource(int index)
    {
        if (invoice == null || index < 0) return;
        sourceChanging = true; embeddedSwitch.IsChecked = index > 0; if (index > 0) sources.SelectedIndex = index - 1;
        sources.Visibility = index > 0 && invoice.Attachments.Count > 1 ? Visibility.Visible : Visibility.Collapsed; sourceChanging = false;
        var doc = invoice; var id = ++sequence; request.Cancel(); request.Dispose(); request = new(); var token = request.Token;
        ClearPdf(); status.Text = index == 0 ? "Vytvaram nahlad z XML…" : "Nacitavam vlozene PDF…";
        try
        {
            byte[] bytes;
            if (index == 0)
            {
                await generation.WaitAsync(token);
                try { bytes = await Task.Run(() => InvoicePdf.Create(doc, token), token); }
                finally { generation.Release(); }
            }
            else bytes = await Task.Run(() => doc.Attachments[index - 1].Read(), token);
            token.ThrowIfCancellationRequested();
            PdfView view;
            try { view = await PdfView.Open(bytes); }
            catch (Exception ex) { throw new InvalidDataException("PDF nie je citatelne alebo je chranene heslom. Vyberte vygenerovane PDF. " + ex.Message, ex); }
            if (id != sequence || closed) { view.Dispose(); return; }
            pdf = view; pdfBytes = bytes; showingXml = false; stage.Children[1].Visibility = Visibility.Collapsed; scroll.Visibility = Visibility.Visible;
            badge.Text = (doc.Extended ? "BIS3 · ROZSIRENY FMCG" : "BIS3 · ZAKLADNY") + (index == 0 ? "  /  Z XML" : "  /  VLOZENE PDF");
            for (uint i = 0; i < pdf.Document.PageCount; i++)
            {
                using var page = pdf.Document.GetPage(i); var image = new Image { Stretch = Stretch.Fill };
                var frame = new Border { Background = Brushes.White, Margin = new Thickness(10, 8, 10, 12), Child = image, Tag = page.Size.Height / page.Size.Width, Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = .17 } };
                images.Add(image); paper.Add(frame); pages.Children.Add(frame);
            }
            fitMode = FitMode.Height; scale = 1; LayoutPages(); scroll.ScrollToTop(); status.Text = ""; await RenderVisible();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (id == sequence && !closed) { ShowTree(null, "Nahlad nie je dostupny."); status.Text = Readable(e); } }
    }
    void ClearPdf()
    {
        var old = pdf; pdf = null; pdfBytes = null; images.Clear(); paper.Clear(); pages.Children.Clear();
        if (old != null) _ = DisposeLater(old);
    }
    async Task DisposeLater(PdfView old) { await rendering.WaitAsync(); try { old.Dispose(); } finally { rendering.Release(); } }
    void LayoutPages()
    {
        if (paper.Count == 0) return;
        foreach (var p in paper)
        {
            var ratio = (double)p.Tag;
            var availableWidth = Math.Max(80, scroll.ActualWidth - 38);
            var width = fitMode switch { FitMode.Width => availableWidth, FitMode.Height => Math.Min(availableWidth, Math.Max(80, scroll.ActualHeight - 24) / ratio), _ => 794 * scale };
            p.Width = Math.Clamp(width, 60, 5000); p.Height = p.Width * ratio;
        }
        UpdateFitButtons();
        _ = RenderVisible();
    }
    void Fit(FitMode mode) { fitMode = mode; LayoutPages(); }
    void UpdateFitButtons()
    {
        if (fitHeightButton == null) return;
        fitHeightButton.Background = VisualStyle.Brush(fitMode == FitMode.Height ? "Selected" : "ButtonBackground");
        fitWidthButton.Background = VisualStyle.Brush(fitMode == FitMode.Width ? "Selected" : "ButtonBackground");
    }
    void Zoom(double factor) { if (fitMode != FitMode.Manual && paper.Count > 0) scale = paper[0].Width / 794; fitMode = FitMode.Manual; scale = Math.Clamp(scale * factor, .1, 5); LayoutPages(); }
    async Task RenderVisible()
    {
        if (closed || pdf == null || !await rendering.WaitAsync(0)) return;
        try
        {
            var view = pdf; double top = 0; int current = 1;
            for (int i = 0; i < paper.Count; i++)
            {
                if (view != pdf || closed) return;
                var frame = paper[i]; var height = frame.Height + 20; var visible = top + height >= scroll.VerticalOffset - 300 && top <= scroll.VerticalOffset + scroll.ViewportHeight + 300;
                if (top <= scroll.VerticalOffset + 30) current = i + 1;
                var image = images[i];
                if (visible && (image.Source == null || Math.Abs(image.Source.Width - Math.Min(1800, frame.Width * 1.5)) > 200))
                { var bitmap = await view.Render((uint)i, (uint)Math.Clamp(frame.Width * 1.5, 400, 1800)); if (view != pdf || closed) return; image.Source = bitmap; }
                else if (!visible) image.Source = null;
                top += height;
            }
            pageCounter.Text = $"Strana {current} / {paper.Count}   •   Ctrl + koliesko: zoom   •   Koliesko: posun";
        }
        catch (Exception e) { if (!closed) status.Text = "Stranu PDF sa nepodarilo zobrazit: " + e.Message; }
        finally { rendering.Release(); }
    }
    void ShowTree(XElement? element, string title)
    {
        showingXml = true; scroll.Visibility = Visibility.Collapsed; stage.Children[1].Visibility = Visibility.Visible; emptyTitle.Text = title; tree.Items.Clear();
        if (element != null) tree.Items.Add(Node(element)); pageCounter.Text = "";
    }
    static TreeViewItem Node(XElement e)
    {
        string text = e.Name.LocalName + (e.HasElements ? "" : "  =  " + (e.Value.Length > 2000 ? e.Value[..2000] + "…" : e.Value));
        var n = new TreeViewItem { Header = text, ToolTip = e.Name.ToString(), Tag = e };
        if (e.HasElements || e.HasAttributes)
        {
            n.Items.Add("…"); n.Expanded += (_, ev) => { if (ev.OriginalSource != n || n.Items.Count != 1 || n.Items[0] is not string) return; n.Items.Clear(); foreach (var a in e.Attributes()) n.Items.Add(new TreeViewItem { Header = "@" + a.Name + " = " + a.Value }); foreach (var child in e.Elements()) n.Items.Add(Node(child)); };
        }
        return n;
    }
    void Navigate(int delta) { if (files.Count <= 1) return; var index = files.FindIndex(x => string.Equals(x, currentPath, StringComparison.OrdinalIgnoreCase)); _ = OpenPath(files[(index + delta + files.Count) % files.Count]); }
    ContextMenu Menu()
    {
        var m = new ContextMenu { Background = dock.Background, Foreground = Foreground };
        void Item(string text, Action action, bool enabled = true) { var i = new MenuItem { Header = text, IsEnabled = enabled }; i.Click += (_, _) => action(); m.Items.Add(i); }
        Item("Otvorit XML…   Ctrl+O", OpenDialog);
        if (files.Count > 1) { Item("Predosly XML", () => Navigate(-1)); Item("Dalsi XML", () => Navigate(1)); Item(preferences.ReverseSort ? "Zoradit podla nazvu A → Z" : "Zoradit podla nazvu Z → A", () => { preferences.ReverseSort = !preferences.ReverseSort; SaveSettings(); if (currentPath != null) _ = OpenPath(currentPath); }); }
        m.Items.Add(new Separator());
        if (invoice?.IsInvoice == true)
        {
            Item("Vygenerovane PDF", () => _ = SelectSource(0));
            for (int i = 0; i < invoice.Attachments.Count; i++) { var index = i + 1; Item("Vlozene PDF: " + invoice.Attachments[i].Name, () => _ = SelectSource(index)); }
        }
        Item("XML struktura", () => { if (invoice != null) ShowTree(invoice.Root, invoice.IsInvoice ? "Udaje XML" : "Toto nie je eFaktúra."); }, invoice != null);
        Item("Ulozit zobrazene PDF…   Ctrl+S", SavePdf, pdfBytes != null);
        Item("Tlacit…   Ctrl+P", () => _ = Print(), pdf != null);
        Item("Prisposobit vyske", () => Fit(FitMode.Height));
        Item("Prisposobit sirke", () => Fit(FitMode.Width));
        Item(preferences.Light ? "Tmavy rezim" : "Svetly rezim", ToggleTheme);
        m.Items.Add(new Separator());
        Item(update == null ? "Aktualizovat" : "Nainstalovat " + update.Version, () => _ = CheckUpdates(true));
        Item((preferences.CheckUpdates ? "✓ " : "") + "Kontrolovat aktualizacie pri starte", () => { preferences.CheckUpdates = !preferences.CheckUpdates; SaveSettings(); });
        Item("Otvarat XML v tejto aplikacii…", Associations);
        Item("O aplikacii", () => AppDialog.Info(this, "eFaktúra Viewer", "Verzia " + typeof(App).Assembly.GetName().Version!.ToString(3) + "\nLokalny prezerac BIS3 a FMCG.\nXML sa nikdy nemeni. Nahlad nie je BIS validacia.\n\nPDFsharp / MigraDoc 6.2.0 (MIT)\nPDF zobrazuje Windows.\nZdrojovy kod: github.com/enthilza/eFaktura-Viewer"));
        return m;
    }
    void SavePdf()
    {
        if (pdfBytes == null) return; var data = pdfBytes;
        var dialog = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = Path.GetFileNameWithoutExtension(currentPath) + ".pdf", OverwritePrompt = true };
        if (dialog.ShowDialog(this) == true) try { if (string.Equals(Path.GetFullPath(dialog.FileName), currentPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Zdrojovy XML sa nesmie prepisat."); File.WriteAllBytes(dialog.FileName, data); } catch (Exception e) { AppDialog.Info(this, "Ulozenie PDF", e.Message); }
    }
    async Task Print()
    {
        if (pdfBytes == null) return; var data = pdfBytes;
        var dialog = new PrintDialog(); if (dialog.ShowDialog() != true) return;
        try
        {
            using var view = await PdfView.Open(data); var document = new FixedDocument();
            if (view.Document.PageCount > 100) throw new InvalidDataException("Pri viac ako 100 stranach ulozte PDF a vytlacte ho v PDF aplikacii.");
            status.Text = "Pripravujem tlac…";
            for (uint i = 0; i < view.Document.PageCount; i++)
            {
                var image = new Image { Source = await view.Render(i, 1800), Width = dialog.PrintableAreaWidth, Height = dialog.PrintableAreaHeight, Stretch = Stretch.Uniform };
                var page = new FixedPage { Width = dialog.PrintableAreaWidth, Height = dialog.PrintableAreaHeight }; page.Children.Add(image); var content = new PageContent { Child = page }; document.Pages.Add(content);
            }
            dialog.PrintDocument(document.DocumentPaginator, Title); status.Text = "";
        }
        catch (Exception e) { status.Text = e.Message; }
    }
    void Associations()
    {
        if (!AppDialog.Confirm(this, "Otvaranie XML", "Zaregistrovat eFaktura Viewer do ponuky Otvorit v programe pre XML? Predvolenu aplikaciu si potom vyberiete vo Windows.", "Zaregistrovat")) return;
        try
        {
            var exe = Environment.ProcessPath!;
            using var app = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\eFakturaViewer.exe"); app.SetValue("FriendlyAppName", "eFaktúra Viewer");
            using (var command = app.CreateSubKey(@"shell\open\command")) command.SetValue("", "\"" + exe + "\" \"%1\"");
            using (var types = app.CreateSubKey("SupportedTypes")) types.SetValue(".xml", "");
            using (var list = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.xml\OpenWithList\eFakturaViewer.exe")) { }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
        catch (Exception e) { AppDialog.Info(this, "Otvaranie XML", e.Message); }
    }
    async Task CheckUpdates(bool manual)
    {
        if (updating) return; updating = true;
        try
        {
            if (update == null)
            {
                if (manual) { if (!AppDialog.Work(this, "Aktualizacia aplikacie", "Vyhladavam novu verziu…", async (_, token) => update = await Updates.Check(token))) return; }
                else update = await Updates.Check();
            }
            if (closed) return;
            if (update == null) { if (manual) AppDialog.Info(this, "Vsetko je aktualne", "Mate aktualnu verziu eFaktura Viewer."); return; }
            if (!manual) { status.Text = "Dostupna verzia " + update.Version + "  •  Prave tlacidlo → Aktualizovat"; return; }
            if (!AppDialog.Confirm(this, "Nova verzia " + update.Version, "Stiahnut a nainstalovat aktualizaciu? Po overeni digitalneho podpisu sa aplikacia znova spusti s otvorenou fakturou.", "Aktualizovat")) return;
            if (!AppDialog.Work(this, "Aktualizacia aplikacie", "Pripravujem stahovanie…", (progress, token) => Updates.Install(update, currentPath, progress, token))) return;
            Close();
        }
        catch (Exception e) { if (manual) AppDialog.Info(this, "Aktualizacia nie je dostupna", e.Message); }
        finally { updating = false; }
    }
    internal async Task Capture(string path, bool light)
    {
        preferences.Light = light; Theme(); UpdateLayout(); await rendering.WaitAsync(); rendering.Release(); await RenderVisible();
        foreach (var image in images) image.InvalidateVisual(); pages.InvalidateVisual();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render); await Task.Delay(120); UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using var output = File.Create(path); png.Save(output);
    }
    internal bool NavigationVisible => dock.Visibility == Visibility.Visible;
    internal string ProfileBadge => badge.Text;
    internal string EmptyTitle => emptyTitle.Text;
    internal string? OpenedNumber => invoice?.Number;
    internal bool HasPdf => pdfBytes != null;
    internal Task TestSelectSource(int index) => SelectSource(index);
    internal bool HeaderToolsVisible => headerTools.IsVisible && headerTools.Children.OfType<Button>().All(x => x.IsVisible);
    internal void SetThemeForTest(bool light) { preferences.Light = light; Theme(); }
    internal byte BackgroundAlpha => ((SolidColorBrush)shell.Background).Color.A;
    internal bool CanDragHeader(DependencyObject? source, Point point)
    {
        if (point.Y < 0 || point.Y > shell.Margin.Top + shell.BorderThickness.Top + shell.Padding.Top + header.ActualHeight) return false;
        while (source != null)
        {
            if (source is ButtonBase or ComboBox or TextBoxBase or MenuItem) return false;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return true;
    }
    internal Grid HeaderForTest => header;
    internal StackPanel HeaderToolsForTest => headerTools;
    internal FitMode CurrentFit => fitMode;
    internal bool PageFitsHeight => paper.Count > 0 && paper[0].Height + 20 <= scroll.ActualHeight + 1;
    internal double PageWidth => paper.Count > 0 ? paper[0].Width : 0;
    internal void TestFit(FitMode mode) => Fit(mode);
    internal bool EmbeddedSwitchChecked => embeddedSwitch.IsChecked == true;
    internal bool AttachmentPickerVisible => sources.Visibility == Visibility.Visible;
}

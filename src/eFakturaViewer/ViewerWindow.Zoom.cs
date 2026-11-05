using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace eFakturaViewer;

internal sealed partial class ViewerWindow
{
    readonly Slider zoomSlider = new()
    {
        Minimum = 10, Maximum = 500, SmallChange = 5, LargeChange = 25,
        IsMoveToPointEnabled = true, Focusable = false, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(5, 0, 5, 0), ToolTip = "Priblíženie faktúry (10 – 500 %)"
    };
    readonly Border zoomControl = new()
    {
        Width = 184, Height = 32, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1),
        Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center
    };
    readonly TextBlock zoomPercent = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer zoomSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    bool syncingZoom;
    Button zoomOut = null!, zoomIn = null!, zoomReset = null!;

    Border CreateZoomControl()
    {
        zoomControl.SetResourceReference(Border.BackgroundProperty, "ButtonBackground");
        zoomControl.SetResourceReference(Border.BorderBrushProperty, "Border");
        var row = new Grid(); zoomControl.Child = row;
        foreach (var width in new[] { new GridLength(24), new GridLength(1, GridUnitType.Star), new GridLength(24), new GridLength(44) })
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        Button CompactButton(string icon, string tip, Action action)
        {
            var b = VisualStyle.Button(icon, "", tip, action);
            b.Padding = new Thickness(3); b.Margin = new Thickness(0); b.MinHeight = 26;
            b.BorderThickness = new Thickness(0); b.Background = System.Windows.Media.Brushes.Transparent;
            return b;
        }
        zoomOut = CompactButton("minus", "Oddialiť faktúru", () => Zoom(.85));
        zoomIn = CompactButton("plus", "Priblížiť faktúru", () => Zoom(1.18));
        zoomReset = CompactButton("", "Obnoviť priblíženie na 100 %", () => SetZoom(1)); zoomReset.Content = zoomPercent;
        row.Children.Add(zoomOut); row.Children.Add(zoomSlider); row.Children.Add(zoomIn); row.Children.Add(zoomReset);
        Grid.SetColumn(zoomSlider, 1); Grid.SetColumn(zoomIn, 2); Grid.SetColumn(zoomReset, 3);
        System.Windows.Automation.AutomationProperties.SetName(zoomSlider, "Priblíženie faktúry v percentách");
        // Slider neprebera fokus: vlavo/vpravo nadalej listuje fakturami a hore/dole
        // posuva dokument aj po zmene mierky mysou. Jeho uchop nesmie presuvat okno.
        zoomSlider.ValueChanged += (_, e) => { if (!syncingZoom && zoomControl.IsEnabled) SetZoom(Math.Round(e.NewValue) / 100, deferSave: true); };
        zoomSaveTimer.Tick += (_, _) => FlushZoomSave();
        return zoomControl;
    }

    void SetZoom(double value, bool deferSave = false)
    {
        if (!double.IsFinite(value)) return;
        fitMode = FitMode.Manual; scale = Math.Clamp(value, .1, 5);
        preferences.Fit = fitMode; preferences.Zoom = scale;
        zoomSaveTimer.Stop();
        // Tah ma desiatky medzikrokov. Nahlad meniame hned, register az po kratkom
        // ustaleni; zatvorenie okna musi ulozit aj posledny este cakajuci krok.
        if (deferSave) zoomSaveTimer.Start(); else SaveSettings();
        LayoutPages();
    }
    void FlushZoomSave()
    {
        if (!zoomSaveTimer.IsEnabled) return;
        zoomSaveTimer.Stop(); SaveSettings();
    }
    void UpdateZoomControl()
    {
        bool available = !showingXml && pdf != null && paper.Count > 0;
        zoomControl.IsEnabled = available; zoomControl.Opacity = available ? 1 : .4;
        if (zoomOut == null) return;
        var percent = available ? paper[0].Width / 794 * 100 : scale * 100;
        syncingZoom = true;
        try { zoomSlider.Value = Math.Clamp(percent, zoomSlider.Minimum, zoomSlider.Maximum); }
        finally { syncingZoom = false; }
        zoomPercent.Text = available ? Math.Round(percent).ToString("0") + " %" : "—";
        zoomOut.IsEnabled = available && percent > 10.01;
        zoomIn.IsEnabled = available && percent < 499.99;
    }

    internal Slider ZoomSliderForTest => zoomSlider;
    internal Border ZoomControlForTest => zoomControl;
    internal Button ZoomResetForTest => zoomReset;
    internal string ZoomPercentForTest => zoomPercent.Text;
}

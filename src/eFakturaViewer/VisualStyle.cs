using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;

namespace eFakturaViewer;

internal static class VisualStyle
{
    internal static bool Light { get; private set; }
    internal static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    internal static void Apply(bool light)
    {
        // Farby menime v zdrojoch aplikacie. DynamicResource v sablonach vdaka tomu
        // aktualizuje aj uz vytvorene ovladacie prvky a vlastne dialogy bez ich obnovovania.
        Light = light;
        var r = Application.Current.Resources;
        void Set(string key, string dark, string bright) => r[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? bright : dark));
        Set("Background", "#1C1C1C", "#F2F2F2"); Set("Panel", "#252525", "#FAFAFA"); Set("Text", "#EEEEEE", "#252525");
        Set("Muted", "#B4B4B4", "#646464"); Set("Border", "#4A4A4A", "#C6C6C6"); Set("Input", "#171717", "#FFFFFF");
        Set("Hover", "#454545", "#DEDEDE"); Set("Selected", "#505050", "#D2D2D2"); Set("Popup", "#292929", "#FAFAFA");
        Set("Track", "#686868", "#A0A0A0"); Set("ButtonBackground", "#353535", "#EAEAEA");
        Set("Selection", "#626262", "#C7C7C7"); Set("Accent", "#EEEEEE", "#333333"); Set("AccentText", "#202020", "#FFFFFF");
        Set("ScrollTrack", "#171717", "#E6E6E6"); Set("ScrollThumb", "#777777", "#999999");
        Set("ScrollThumbHover", "#AAAAAA", "#777777"); Set("ScrollThumbPressed", "#DDDDDD", "#555555");
    }
    internal static Button Button(string icon, string label, string tooltip, Action action)
    {
        var button = new Button { ToolTip = tooltip, Padding = new Thickness(9, 7, 9, 7), MinHeight = 32, Margin = new Thickness(3, 0, 3, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (Paths.TryGetValue(icon, out var data))
        {
            var shape = new System.Windows.Shapes.Path { Data = Geometry.Parse(data), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            shape.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding("Foreground") { Source = button });
            var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(shape);
            row.Children.Add(new Viewbox { Width = 17, Height = 17, Child = canvas, Margin = new Thickness(0, 0, label.Length > 0 ? 7 : 0, 0) });
        }
        if (label.Length > 0) { var text = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }; text.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { Source = button }); row.Children.Add(text); }
        button.Content = row; button.Click += (_, _) => action(); System.Windows.Automation.AutomationProperties.SetName(button, tooltip); return button;
    }
    static readonly Dictionary<string, string> Paths = new()
    {
        ["theme"] = "M12,3 A9,9 0 1 0 21,12 A7,7 0 0 1 12,3 Z", ["update"] = "M20,9 A8,8 0 1 0 20,15 M20,3 L20,9 L14,9",
        ["close"] = "M6,6 L18,18 M18,6 L6,18", ["left"] = "M15,5 L8,12 L15,19", ["right"] = "M9,5 L16,12 L9,19",
        ["height"] = "M4,3 L20,3 M4,21 L20,21 M12,6 L12,18 M8,10 L12,6 L16,10 M8,14 L12,18 L16,14",
        ["width"] = "M3,4 L3,20 M21,4 L21,20 M6,12 L18,12 M10,8 L6,12 L10,16 M14,8 L18,12 L14,16",
        ["plus"] = "M5,12 L19,12 M12,5 L12,19", ["minus"] = "M5,12 L19,12", ["more"] = "M4,11 L4,13 M12,11 L12,13 M20,11 L20,13"
    };
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    internal static void RoundedShell(Window window)
    {
        // Zaoblenie kresli WPF Border. Druhy ramec od DWM by pri priehladnom okne mohol
        // vytvorit obdlznikovy obrys mimo neho; 33/34 su atributy zaoblenia a farby okraja DWM.
        int noRounding = 1, noBorder = unchecked((int)0xFFFFFFFE);
        var handle = new WindowInteropHelper(window).Handle;
        DwmSetWindowAttribute(handle, 33, ref noRounding, 4); DwmSetWindowAttribute(handle, 34, ref noBorder, 4);
    }
}

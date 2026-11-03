using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace eFakturaViewer;

internal static class WindowPlacement
{
    [StructLayout(LayoutKind.Sequential)] internal struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    internal static Rect LastInitialBounds { get; private set; }
    internal static Rect Calculate(Rect screen, Rect? workArea = null)
    {
        // Poziadavka je 97 % vysky a pomer A4. Na uzkom monitore rozhodne sirka,
        // aby sa okno zmestilo cele; pomer stran pritom zachovame.
        var work = workArea ?? screen;
        // Pracovna plocha vyluci panel uloh na lubovolnej hrane. Dva pixely su fyzicke,
        // nie WPF jednotky, takze odstup sa pri zvacseni Windows nezmeni.
        var height = Math.Floor(Math.Min(Math.Min(screen.Height * .97, work.Height - 4), (work.Width - 4) * 297 / 210));
        var width = Math.Round(height * 210 / 297);
        return new Rect(Math.Round(work.Left + (work.Width - width) / 2), work.Bottom - 2 - height, width, height);
    }
    internal static void Center(Window window)
    {
        // Novy dokument sa otvara pri kurzore, nie vzdy na primarnom monitore.
        GetCursorPos(out var cursor); var monitor = MonitorFromPoint(cursor, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) { window.WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }
        var bounds = Calculate(new Rect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top), new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top));
        LastInitialBounds = bounds;
        uint dpiX = 96, dpiY = 96;
        if (GetDpiForMonitor(monitor, 0, out var x, out var y) == 0) { dpiX = x; dpiY = y; }
        // WPF rozmery su v jednotkach 1/96 palca, udaje monitora v pixeloch. Minimalna velkost
        // tiez nesmie prebit vypocet na malom monitore s vysokym skalovanim.
        window.MinWidth = Math.Min(260, bounds.Width * 96 / dpiX); window.MinHeight = Math.Min(360, bounds.Height * 96 / dpiY);
        window.Width = bounds.Width * 96 / dpiX; window.Height = bounds.Height * 96 / dpiY;
        // Fyzicke pixely zabezpecia spravne centrovanie aj na monitore s inym DPI ci zapornymi suradnicami.
        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y), (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 0x0014);
    }
}

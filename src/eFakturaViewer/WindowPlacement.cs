using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace eFakturaViewer;

internal static class WindowPlacement
{
    [StructLayout(LayoutKind.Sequential)] internal struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential)] struct NativePlacement { public int Length; public uint Flags, Show; public NativePoint Min, Max; public NativeRect Normal; }
    delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref NativePlacement placement);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
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
    static MonitorInfo Info(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        _ = GetMonitorInfo(monitor, ref info); return info;
    }
    static Rect RectOf(NativeRect rect) => new(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
    static uint Dpi(IntPtr monitor) => GetDpiForMonitor(monitor, 0, out var x, out _) == 0 && x > 0 ? x : 96;

    internal static Rect FitSaved(SavedWindow saved, Rect work, uint dpi)
    {
        // Ukladame fyzicke pixely aj povodne DPI. Po zmene skalovania zachovame logicku
        // velkost a odstup od pracovnej plochy monitora, nie stare globalne suradnice.
        var ratio = (double)dpi / saved.Dpi;
        var width = Math.Clamp(saved.Width * ratio, Math.Min(260 * dpi / 96.0, work.Width), work.Width);
        var height = Math.Clamp(saved.Height * ratio, Math.Min(360 * dpi / 96.0, work.Height - 2), work.Height - 2);
        var left = Math.Clamp(work.Left + (saved.Left - saved.WorkLeft) * ratio, work.Left, work.Right - width);
        var top = Math.Clamp(work.Top + (saved.Top - saved.WorkTop) * ratio, work.Top, work.Bottom - 2 - height);
        return new Rect(Math.Round(left), Math.Round(top), Math.Round(width), Math.Round(height));
    }

    static IntPtr SavedMonitor(SavedWindow saved)
    {
        IntPtr found = IntPtr.Zero;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data) =>
        {
            if (!string.Equals(Info(monitor).Device, saved.Monitor, StringComparison.OrdinalIgnoreCase)) return true;
            found = monitor; return false;
        }, IntPtr.Zero);
        if (found != IntPtr.Zero) return found;
        // Odpojeny monitor nesmie nechat cele okno mimo plochy. Pouzijeme najblizsi dostupny.
        var old = new NativeRect { Left = (int)saved.Left, Top = (int)saved.Top, Right = (int)(saved.Left + saved.Width), Bottom = (int)(saved.Top + saved.Height) };
        return MonitorFromRect(ref old, 2);
    }

    internal static void Restore(Window window, SavedWindow? saved, bool maximize = false)
    {
        if (saved is { IsValid: false }) saved = null;
        GetCursorPos(out var cursor);
        var monitor = saved == null ? MonitorFromPoint(cursor, 2) : SavedMonitor(saved);
        var info = Info(monitor);
        if (info.Work.Right <= info.Work.Left || info.Work.Bottom <= info.Work.Top + 2) { window.WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }
        var dpi = Dpi(monitor);
        var bounds = saved == null ? Calculate(RectOf(info.Monitor), RectOf(info.Work)) : FitSaved(saved, RectOf(info.Work), dpi);
        LastInitialBounds = bounds;
        // WPF rozmery su v jednotkach 1/96 palca, udaje monitora v pixeloch. Minimalna velkost
        // tiez nesmie prebit vypocet na malom monitore s vysokym skalovanim.
        window.MinWidth = Math.Min(260, bounds.Width * 96 / dpi); window.MinHeight = Math.Min(360, bounds.Height * 96 / dpi);
        window.Width = bounds.Width * 96 / dpi; window.Height = bounds.Height * 96 / dpi;
        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y), (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 0x0014);
        if (maximize && saved?.Maximized == true) window.WindowState = WindowState.Maximized;
    }

    internal static SavedWindow? Capture(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        // Minimalizovane okno nema zmysluplne rozmery; zachovame posledny normalny/maximalizovany stav.
        if (handle == IntPtr.Zero || IsIconic(handle)) return null;
        var info = Info(MonitorFromWindow(handle, 2));
        if (info.Work.Right <= info.Work.Left) return null;
        var maximized = IsZoomed(handle);
        NativeRect rect;
        if (maximized)
        {
            var placement = new NativePlacement { Length = Marshal.SizeOf<NativePlacement>() };
            if (!GetWindowPlacement(handle, ref placement)) return null;
            rect = placement.Normal;
            // rcNormalPosition je v suradniciach pracovnej plochy, nie obrazovky.
            // Tento posun je nutny pri paneli uloh hore alebo vlavo.
            var dx = info.Work.Left - info.Monitor.Left; var dy = info.Work.Top - info.Monitor.Top;
            rect.Left += dx; rect.Right += dx; rect.Top += dy; rect.Bottom += dy;
        }
        else if (!GetWindowRect(handle, out rect)) return null;
        var dpi = GetDpiForWindow(handle); if (dpi == 0) dpi = 96;
        var saved = new SavedWindow(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, dpi, info.Device, info.Work.Left, info.Work.Top, maximized);
        return saved.IsValid ? saved : null;
    }
}

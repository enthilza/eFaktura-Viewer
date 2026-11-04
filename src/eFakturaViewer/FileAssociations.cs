using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace eFakturaViewer;

internal static class FileAssociations
{
    const int Cancelled = unchecked((int)0x800704C7);

    internal static bool TryShowXmlPicker(IntPtr owner)
    {
        IOpenWithLauncher? launcher = null;
        try
        {
            // Shell musi vidiet prave doplnenu registraciu v ponuke Otvorit v programe.
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

            // ms-settings:defaultapps nevie zdokumentovanym parametrom vybrat priponu.
            // Pouzijeme systemovy vyber pre .xml, rovnako ako Chromium pre PDF.
            // Toto COM rozhranie nie je verejne garantovane, preto pri jeho nedostupnosti
            // volajuci ponukne bezne nastavenia Windows. Triedu citame z 32-bitovej vetvy.
            // Referencia: https://chromium.googlesource.com/chromium/src/+/lkgr/base/win/default_apps_util.cc
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var openWith = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\OpenWith");
            if (!Guid.TryParse(openWith?.GetValue("OpenWithLauncher") as string, out var classId)) return false;
            var interfaceId = typeof(IOpenWithLauncher).GUID;
            if (CoCreateInstance(ref classId, IntPtr.Zero, 4 /* CLSCTX_LOCAL_SERVER */, ref interfaceId, out launcher) < 0 || launcher == null) return false;
            _ = CoAllowSetForegroundWindow(launcher, IntPtr.Zero);

            // 0x2004 otvori vyber trvalej predvolby bez spustenia suboru. Posielame iba
            // priponu, nikdy cestu k fakture. Zavretie alebo Zrusit nie je chyba a nesmie
            // nasledne otvorit dalsie okno nastaveni. UserChoice meni vyhradne Windows.
            var result = launcher.Launch(owner, ".xml", 0x2004);
            return result >= 0 || result == Cancelled;
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
        finally
        {
            if (launcher != null) Marshal.ReleaseComObject(launcher);
        }
    }

    [ComImport, Guid("6A283FE2-ECFA-4599-91C4-E80957137B26"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOpenWithLauncher
    {
        [PreserveSig]
        int Launch(IntPtr owner, [MarshalAs(UnmanagedType.LPWStr)] string extension, int flags);
    }

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid classId, IntPtr outer, uint context, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IOpenWithLauncher? instance);

    [DllImport("ole32.dll")]
    static extern int CoAllowSetForegroundWindow([MarshalAs(UnmanagedType.IUnknown)] object instance, IntPtr reserved);

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}

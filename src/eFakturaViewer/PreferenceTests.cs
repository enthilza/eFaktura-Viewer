using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace eFakturaViewer;

internal static class PreferenceTests
{
    internal static async Task Run(string output, string examples, List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        var parentProfile = Preferences.TestDirectory;
        Preferences.TestDirectory = Path.Combine(output, "preferences-regression-" + Guid.NewGuid().ToString("N"));
        var windows = new List<ViewerWindow>();
        try
        {
            Check(Preferences.RegistryPath != Preferences.UserRegistryPath && Preferences.Load().Light && Preferences.Load().Fit == ViewerWindow.FitMode.Height, "Novy izolovany profil ma svetlu temu a zobrazenie na vysku");
            Directory.CreateDirectory(Preferences.DirectoryPath);
            var legacyPath = Path.Combine(Preferences.DirectoryPath, "settings.json");
            var legacy = JsonSerializer.Serialize(new Preferences { Light = false, ReverseSort = true, CheckUpdates = false, LastDirectory = examples });
            File.WriteAllText(legacyPath, legacy);
            var migrated = Preferences.Load(); migrated.Save();
            using (var key = Registry.CurrentUser.OpenSubKey(Preferences.RegistryPath))
                Check(key?.GetValue("Settings") is string && !Preferences.Load().Light && Preferences.Load().ReverseSort && File.ReadAllText(legacyPath) == legacy, "Stare nastavenia prejdu do registra bez prepisania povodneho suboru");
            var first = Preferences.Load(); var second = Preferences.Load();
            first.Fit = ViewerWindow.FitMode.Width; first.Save(); second.Light = true; second.Save();
            Check(Preferences.Load().Fit == ViewerWindow.FitMode.Width && Preferences.Load().Light, "Dve okna si neprepisu navzaj nezmenene nastavenia");
            File.WriteAllText(legacyPath, "neplatny JSON");
            Check(Preferences.Load().Fit == ViewerWindow.FitMode.Width, "Po migracii ma register prednost pred starsim suborom");
            using (var key = Registry.CurrentUser.CreateSubKey(Preferences.RegistryPath)) key.SetValue("Settings", "{\"Fit\":99,\"Zoom\":-1,\"Window\":{\"Left\":0,\"Top\":0,\"Width\":-3,\"Height\":1,\"Dpi\":0}}");
            var invalid = Preferences.Load();
            Check(invalid.Fit == ViewerWindow.FitMode.Height && invalid.Zoom == 1 && invalid.Window == null, "Neplatne rozmery a zoom sa nahradia predvolbami");
            using (var key = Registry.CurrentUser.CreateSubKey(Preferences.RegistryPath)) key.SetValue("Settings", "poskodene");
            Check(Preferences.Load().Light && Preferences.Load().Window == null, "Poskodene ulozenie neblokuje start aplikacie");

            var saved = new SavedWindow(-1600, 100, 600, 800, 96, "monitor", -1920, 0, false);
            Check(WindowPlacement.FitSaved(saved, new Rect(-1920, 0, 1920, 1040), 96) == new Rect(-1600, 100, 600, 800), "Poloha na monitore so zapornymi suradnicami sa zachova");
            Check(WindowPlacement.FitSaved(saved, new Rect(2560, 60, 2560, 1380), 144) == new Rect(3040, 210, 900, 1200), "Zmena DPI a presun monitora zachova logicku velkost a odstup");
            var smallWork = new Rect(0, 40, 800, 560);
            var constrained = WindowPlacement.FitSaved(saved with { Width = 4000, Height = 2500 }, smallWork, 192);
            Check(smallWork.Contains(constrained) && constrained.Bottom <= smallWork.Bottom - 2, "Po odpojeni monitora sa velke okno zmesti na dostupnu plochu");

            new Preferences { CheckUpdates = false }.Save();
            async Task Settle(ViewerWindow window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            ViewerWindow Open() { var w = new ViewerWindow(null); windows.Add(w); w.Show(); return w; }
            var original = Open(); await Settle(original);
            original.Width = Math.Max(280, original.ActualWidth - 60); original.Height = Math.Max(400, original.ActualHeight - 90);
            original.Left += 15; original.Top += 15; await Settle(original);
            original.TestFit(ViewerWindow.FitMode.Width);
            await original.OpenPath(Path.Combine(examples, "01_zaklad.xml"));
            await original.OpenPath(Path.Combine(examples, "02_fmcg.xml"));
            Check(original.CurrentFit == ViewerWindow.FitMode.Width && Preferences.Load().Fit == ViewerWindow.FitMode.Width, "Prisposobenie sirke prezije prechod na dalsiu fakturu");
            await original.TestSelectSource(0);
            Check(original.CurrentFit == ViewerWindow.FitMode.Width, "Zmena zdroja PDF nezrusi vybrane prisposobenie");
            var expected = WindowPlacement.Capture(original)!; original.Close();
            Check(Preferences.Load().Window == expected, "Zatvorenie ulozi skutocnu polohu a velkost do registra");
            var reopened = Open(); await Settle(reopened);
            var actual = WindowPlacement.Capture(reopened)!;
            Check(Near(expected, actual) && reopened.CurrentFit == ViewerWindow.FitMode.Width, "Nove okno obnovi polohu velkost aj prisposobenie sirke");
            await reopened.OpenPath(Path.Combine(examples, "01_zaklad.xml")); reopened.ZoomSliderForTest.Value = 175; var pageWidth = reopened.PageWidth;
            await reopened.OpenPath(Path.Combine(examples, "02_fmcg.xml"));
            Check(reopened.CurrentFit == ViewerWindow.FitMode.Manual && Math.Abs(pageWidth - reopened.PageWidth) < .1, "Rucny zoom sa zachova medzi fakturami");
            reopened.WindowState = WindowState.Maximized; await Settle(reopened);
            var normal = WindowPlacement.Capture(reopened)!;
            reopened.WindowState = WindowState.Minimized; await Settle(reopened); reopened.Close();
            Check(Preferences.Load().Window?.Maximized == true, "Minimalizacia pred zatvorenim nezmaze maximalizovany stav");
            var maximized = Open(); await Settle(maximized);
            Check(maximized.WindowState == WindowState.Maximized, "Dalsi start obnovi maximalizovane okno");
            await maximized.OpenPath(Path.Combine(examples, "01_zaklad.xml"));
            Check(maximized.CurrentFit == ViewerWindow.FitMode.Manual && Math.Abs(pageWidth - maximized.PageWidth) < .1, "Rucny zoom sa obnovi aj v novom okne");
            Check(maximized.ZoomPercentForTest == "175 %" && maximized.ZoomSliderForTest.Value == 175, "Ulozena hodnota posuvnika sa obnovi aj v zahlavi");
            maximized.WindowState = WindowState.Normal; await Settle(maximized);
            Check(Near(normal, WindowPlacement.Capture(maximized)!), "Obnovenie z maximalizacie zachova normalne rozmery a polohu");
            maximized.ZoomSliderForTest.Value = 183;
            maximized.Close();
            Check(Math.Abs(Preferences.Load().Zoom - 1.83) < .001, "Okamzite zatvorenie dokonci cakajuce ulozenie lupy");
        }
        finally
        {
            foreach (var window in windows.Where(w => w.IsVisible).ToArray()) window.Close();
            Preferences.CleanupTestProfile(); Preferences.TestDirectory = parentProfile;
        }
    }

    static bool Near(SavedWindow left, SavedWindow right) => Math.Abs(left.Left - right.Left) <= 2 && Math.Abs(left.Top - right.Top) <= 2 && Math.Abs(left.Width - right.Width) <= 2 && Math.Abs(left.Height - right.Height) <= 2;
}

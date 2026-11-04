using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace eFakturaViewer;

internal sealed class Preferences
{
    internal const string UserRegistryPath = @"Software\eFakturaViewer";
    internal static string UserDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "eFakturaViewer");
    internal static string? TestDirectory { get; set; }
    internal static string DirectoryPath => TestDirectory ?? UserDirectoryPath;
    internal static string RegistryPath => TestDirectory == null ? UserRegistryPath : @"Software\eFakturaViewer.Tests\" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TestDirectory)));
    JsonObject? baseline;

    public bool Light { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;
    public bool ReverseSort { get; set; }
    public string LastDirectory { get; set; } = "";
    public ViewerWindow.FitMode Fit { get; set; } = ViewerWindow.FitMode.Height;
    public double Zoom { get; set; } = 1;
    public SavedWindow? Window { get; set; }

    internal static Preferences Load()
    {
        var result = Read();
        result.Normalize();
        result.baseline = result.Snapshot();
        return result;
    }

    static Preferences Read()
    {
        // Nacitanie samo nic nezapisuje, takze ani startovacia sonda aktualizacie nemeni profil.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            if (key?.GetValue("Settings") is string json && JsonSerializer.Deserialize<Preferences>(json) is { } settings) return settings;
        }
        catch { /* Poskodeny alebo nepristupny profil nesmie zabranit otvoreniu faktury. */ }
        // Jednorazovy prechod zo starsich verzii: pri prvom ulozeni sa hodnoty zapisu do HKCU.
        // Povodny subor ostava nedotknuty, ale po migracii ma register prednost.
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(DirectoryPath, "settings.json"))) ?? new(); }
        catch { return new(); }
    }

    void Normalize()
    {
        if (!Enum.IsDefined(Fit)) Fit = ViewerWindow.FitMode.Height;
        if (!double.IsFinite(Zoom) || Zoom < .1 || Zoom > 5) Zoom = 1;
        LastDirectory ??= "";
        if (Window is { IsValid: false }) Window = null;
    }

    JsonObject Snapshot() => JsonSerializer.SerializeToNode(this)!.AsObject();

    internal void Save()
    {
        Normalize();
        // Pri viacerych otvorenych EXE zapisujeme iba hodnoty zmenene v tomto okne.
        // Inak by zatvorenie stareho okna vratilo temu alebo zoom zvoleny v novsom.
        using var mutex = new Mutex(false, @"Local\eFakturaViewer.Settings-" + Environment.UserName);
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(3)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Nastavenia práve ukladá iné okno. Skúste to znova.");
        try
        {
            var current = Snapshot();
            var latest = Read(); latest.Normalize();
            var merged = latest.Snapshot();
            foreach (var pair in current)
                if (baseline == null || !JsonNode.DeepEquals(pair.Value, baseline[pair.Key])) merged[pair.Key] = pair.Value?.DeepClone();
            // Jeden REG_SZ zapis udrzi polohu, rozmery a monitor pohromade.
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue("Settings", merged.ToJsonString(), RegistryValueKind.String);
            baseline = current;
        }
        finally { mutex.ReleaseMutex(); }
    }

    internal static void CleanupTestProfile()
    {
        if (TestDirectory == null) return;
        var path = RegistryPath;
        if (!path.StartsWith(@"Software\eFakturaViewer.Tests\", StringComparison.Ordinal)) throw new InvalidOperationException("Neplatný testovací profil.");
        Registry.CurrentUser.DeleteSubKeyTree(path, false);
    }
}

internal sealed record SavedWindow(double Left, double Top, double Width, double Height, uint Dpi, string Monitor, double WorkLeft, double WorkTop, bool Maximized)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => new[] { Left, Top, Width, Height, WorkLeft, WorkTop }.All(double.IsFinite)
        && Math.Abs(Left) < 1_000_000 && Math.Abs(Top) < 1_000_000 && Math.Abs(WorkLeft) < 1_000_000 && Math.Abs(WorkTop) < 1_000_000
        && Width >= 100 && Height >= 100 && Width <= 100_000 && Height <= 100_000 && Dpi is >= 48 and <= 960;
}

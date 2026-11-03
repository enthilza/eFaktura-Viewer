using System.IO;
using System.Text.Json;
using System.Windows;

namespace eFakturaViewer;

internal sealed class Preferences
{
    internal static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "eFakturaViewer");
    public bool Light { get; set; }
    public bool CheckUpdates { get; set; } = true;
    public bool ReverseSort { get; set; }
    public string LastDirectory { get; set; } = "";
    internal static Preferences Load() { try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(DirectoryPath, "settings.json"))) ?? new(); } catch { return new(); } }
    internal void Save() { Directory.CreateDirectory(DirectoryPath); var path = Path.Combine(DirectoryPath, "settings.json"); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(this)); File.Move(temp, path, true); }
}

internal sealed class App : Application
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--apply-update") return Updates.Apply(args[1], args[2], args[3]);
        if (args.Length == 2 && args[0] == "--verify-package")
        {
            try { var offer = Updates.Manifest(File.ReadAllBytes(Path.Combine(args[1], "update.json")), File.ReadAllText(Path.Combine(args[1], "update.sig"))); Updates.VerifyFile(Path.Combine(args[1], offer.File), offer); return 0; } catch { return 1; }
        }
        if (args.Length == 2 && args[0] == "--check-update")
        {
            try { File.WriteAllText(args[1], JsonSerializer.Serialize(Updates.Check().GetAwaiter().GetResult())); return 0; } catch (Exception e) { File.WriteAllText(args[1], e.Message); return 1; }
        }
        var app = new App();
        if (args.Length == 1 && args[0] == "--probe") { var probe = new ViewerWindow(null); probe.Close(); return 0; }
        if (args.Length > 0 && args[0] == "--self-test")
        {
            var results = args.Length > 1 ? args[1] : "test-results";
            app.Startup += async (_, _) => { try { await SelfTest.Run(results, args.Length > 2 ? args[2] : "examples"); app.Shutdown(0); } catch (Exception ex) { System.IO.Directory.CreateDirectory(results); File.WriteAllText(Path.Combine(results, "failure.txt"), ex.ToString()); app.Shutdown(1); } };
        }
        else app.Startup += (_, _) => { var window = new ViewerWindow(args.FirstOrDefault()); app.MainWindow = window; window.Show(); };
        return app.Run();
    }
}

using System.IO;
using System.Text.Json;
using System.Windows;

namespace eFakturaViewer;

internal sealed class Preferences
{
    internal static string UserDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "eFakturaViewer");
    internal static string? TestDirectory { get; set; }
    internal static string DirectoryPath => TestDirectory ?? UserDirectoryPath;
    // Predvolba plati pre prve spustenie alebo chybajuce pole; ulozena volba ma prednost.
    public bool Light { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;
    public bool ReverseSort { get; set; }
    public string LastDirectory { get; set; } = "";
    // Poskodene nastavenia nemaju zabranit otvoreniu faktury. Pouzijeme bezpecne predvolby.
    internal static Preferences Load() { try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(DirectoryPath, "settings.json"))) ?? new(); } catch { return new(); } }
    // Najprv dokoncit JSON vedla ciela, az potom vymenit subor. Pri chybe zapisu tak zostane stara kopia.
    internal void Save() { Directory.CreateDirectory(DirectoryPath); var path = Path.Combine(DirectoryPath, "settings.json"); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(this)); File.Move(temp, path, true); }
}

internal sealed class App : Application
{
    internal App()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/eFakturaViewer;component/VisualStyle.xaml", UriKind.Relative) });
        VisualStyle.Apply(Preferences.Load().Light);
    }
    [STAThread]
    static int Main(string[] args)
    {
        // Aktualizator a overenie balika nepotrebuju WPF okno. Obsluhujeme ich pred vytvorenim
        // Application, aby pri vymene EXE nevzniklo dalsie bezne okno ani automaticka kontrola aktualizacii.
        if (args.Length == 4 && args[0] == "--apply-update") return Updates.Apply(args[1], args[2], args[3]);
        if (args.Length == 2 && args[0] == "--verify-package")
        {
            try { var offer = Updates.Manifest(File.ReadAllBytes(Path.Combine(args[1], "update.json")), File.ReadAllText(Path.Combine(args[1], "update.sig"))); Updates.VerifyFile(Path.Combine(args[1], offer.File), offer); return 0; } catch { return 1; }
        }
        if (args.Length == 2 && args[0] == "--check-update")
        {
            try { File.WriteAllText(args[1], JsonSerializer.Serialize(Updates.Check().GetAwaiter().GetResult())); return 0; } catch (Exception e) { File.WriteAllText(args[1], e.Message); return 1; }
        }
        // Izolacia musi byt nastavena este pred App(), ktora uz nacitava temu.
        if (args.FirstOrDefault() == "--self-test")
        {
            Preferences.TestDirectory = Path.Combine(Path.GetFullPath(args.Length > 1 ? args[1] : "test-results"), "preferences-" + Guid.NewGuid().ToString("N"));
            new Preferences { CheckUpdates = false }.Save();
        }
        var app = new App();
        // Sonda iba zostavi a zavrie okno bez Show: overi inicializaciu, nespusti Loaded ani sietovu kontrolu.
        // Nenahradza test vykreslenia dokumentu; aktualizator ju pouziva ako rychlu podmienku obnovy zalohy.
        if (args.Length == 1 && args[0] == "--probe") { var probe = new ViewerWindow(null); probe.Close(); return 0; }
        if (args.Length > 0 && args[0] == "--self-test")
        {
            var results = args.Length > 1 ? args[1] : "test-results";
            app.Startup += async (_, _) => { try { await SelfTest.Run(results, args.Length > 2 ? args[2] : "examples"); app.Shutdown(0); } catch (Exception ex) { System.IO.Directory.CreateDirectory(results); File.WriteAllText(Path.Combine(results, "failure.txt"), ex.ToString()); app.Shutdown(1); } };
        }
        else app.Startup += (_, _) =>
        {
            var failed = args.FirstOrDefault() == "--update-failed";
            var window = new ViewerWindow(failed ? args.Skip(1).FirstOrDefault() : args.FirstOrDefault()); app.MainWindow = window; window.Show();
            _ = Task.Run(Updates.Cleanup);
            if (failed) AppDialog.Info(window, "Aktualizácia sa nepodarila", "Aktualizáciu sa nepodarilo dokončiť. Bola spustená dostupná verzia aplikácie. Podrobnosti sú v update-error.txt v priečinku nastavení aplikácie.");
        };
        return app.Run();
    }
}

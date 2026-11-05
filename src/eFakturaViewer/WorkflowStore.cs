using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace eFakturaViewer;

internal sealed record WorkflowFolder(Guid Id, string Name, string Path);
internal sealed record WorkflowAssignment(Guid FolderId, string Fingerprint);
internal sealed class WorkflowState
{
    public List<WorkflowFolder> Folders { get; set; } = [];
    public Dictionary<string, WorkflowAssignment> Assignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal static class WorkflowStore
{
    internal static string Fingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static WorkflowState Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Preferences.RegistryPath);
        if (key?.GetValue("Workflows") is not string json) return new();
        // Poskodene zaradenie nesmieme potichu zamenit za predvoleny ciel a presunut
        // dokumenty inde. Prezeranie funguje dalej, ale triedenie potrebuje opravu profilu.
        WorkflowState state;
        try { state = JsonSerializer.Deserialize<WorkflowState>(json) ?? throw new JsonException(); }
        catch (JsonException e) { throw new IOException("Nastavenia cieľových priečinkov nie sú čitateľné.", e); }
        if (state.Folders == null || state.Assignments == null || state.Folders.Any(x => x == null || x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Path)) || state.Folders.Select(x => x.Id).Distinct().Count() != state.Folders.Count || state.Assignments.Any(x => x.Value == null || x.Value.FolderId == Guid.Empty || string.IsNullOrEmpty(x.Value.Fingerprint) || !state.Folders.Any(f => f.Id == x.Value.FolderId)))
            throw new IOException("Nastavenia cieľových priečinkov nie sú platné.");
        state.Assignments = new(state.Assignments, StringComparer.OrdinalIgnoreCase);
        return state;
    }
    internal static WorkflowState SetFolders(IEnumerable<WorkflowFolder> folders)
    {
        var list = folders.Select(x => x with { Name = x.Name.Trim(), Path = DirectoryPath(x.Path) }).ToList();
        if (list.Any(x => x.Id == Guid.Empty || x.Name.Length == 0)) throw new IOException("Každý priečinok musí mať názov.");
        if (list.Select(x => x.Id).Distinct().Count() != list.Count) throw new IOException("Identifikátor cieľového priečinka je uvedený viackrát.");
        if (list.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count) throw new IOException("Rovnaký cieľový priečinok je uvedený viackrát.");
        return Change(s =>
        {
            s.Folders = list;
            foreach (var key in s.Assignments.Where(x => !list.Any(f => f.Id == x.Value.FolderId)).Select(x => x.Key).ToArray()) s.Assignments.Remove(key);
        });
    }
    internal static WorkflowState Assign(string path, byte[] bytes, Guid folderId) => Change(s =>
    {
        if (!s.Folders.Any(x => x.Id == folderId)) throw new IOException("Tento cieľový priečinok už nie je nastavený.");
        s.Assignments[System.IO.Path.GetFullPath(path)] = new(folderId, Fingerprint(bytes));
    });
    internal static void RemoveMoved(IEnumerable<MoveSuccess> moved) => Change(s =>
    {
        foreach (var item in moved)
            if (s.Assignments.TryGetValue(item.Source, out var old) && old.Fingerprint == item.Fingerprint) s.Assignments.Remove(item.Source);
    });
    internal static WorkflowFolder? Selected(WorkflowState state, string? path, byte[]? bytes)
    {
        if (path != null && bytes != null && state.Assignments.TryGetValue(path, out var chosen) && chosen.Fingerprint == Fingerprint(bytes))
            return state.Folders.FirstOrDefault(x => x.Id == chosen.FolderId);
        return state.Folders.FirstOrDefault();
    }
    internal static bool Changed(WorkflowState state, string path, byte[] bytes) => state.Assignments.TryGetValue(path, out var a) && a.Fingerprint != Fingerprint(bytes);
    internal static string DirectoryPath(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim());
        if (!System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) throw new IOException("Zadajte úplnú cestu k priečinku.");
        return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    }
    static WorkflowState Change(Action<WorkflowState> edit)
    {
        using var mutex = new Mutex(false, @"Local\eFakturaViewer.Workflows-" + Environment.UserName);
        bool held;
        try { held = mutex.WaitOne(TimeSpan.FromSeconds(3)); } catch (AbandonedMutexException) { held = true; }
        if (!held) throw new IOException("Zaradenie práve ukladá iné okno. Skúste to znova.");
        try
        {
            // Samostatna hodnota a citanie tesne pred zmenou chrania priradenia
            // pred prepisom starymi nastaveniami druheho otvoreneho okna.
            var state = Load(); edit(state);
            using var key = Registry.CurrentUser.CreateSubKey(Preferences.RegistryPath);
            key.SetValue("Workflows", JsonSerializer.Serialize(state), RegistryValueKind.String);
            return state;
        }
        finally { mutex.ReleaseMutex(); }
    }
}

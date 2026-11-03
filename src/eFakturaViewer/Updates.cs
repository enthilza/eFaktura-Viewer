using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace eFakturaViewer;

internal sealed record UpdateOffer(string Version, string Tag, string File, string Runtime, long Size, string Signature);
internal static class Updates
{
    internal const string Repository = "enthilza/eFaktura-Viewer";
    static string Key { get { using var reader = new StreamReader(typeof(Updates).Assembly.GetManifestResourceStream("eFakturaViewer.Assets.update-public-key.xml")!); return reader.ReadToEnd(); } }
    static HttpClient Client() { var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; c.DefaultRequestHeaders.UserAgent.ParseAdd("eFakturaViewer/1.0"); return c; }
    static string Asset(string tag, string name) => $"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{name}";
    internal static void Verify(byte[] data, string signature, string? publicKey = null)
    {
        using var rsa = RSA.Create(); rsa.FromXmlString(publicKey ?? Key);
        if (!rsa.VerifyData(data, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new InvalidDataException("Digitálny podpis aktualizácie nie je platný.");
    }
    internal static UpdateOffer Manifest(byte[] bytes, string signature, string? publicKey = null)
    {
        // Overujeme povodne bajty JSON, nie znovu serializovany objekt. Aj zmena medzier by
        // zmenila podpis; udajom o verzii, velkosti a podpise EXE doverujeme az po tomto kroku.
        Verify(bytes, signature, publicKey);
        var offer = JsonSerializer.Deserialize<UpdateOffer>(bytes) ?? throw new InvalidDataException("Chýba popis aktualizácie.");
        // Pevny nazov suboru a runtime udrzuju format balika jednoznacny. Nazov z manifestu
        // sa neskor pouziva ako cesta, preto tu nepripustame lubovolne meno ani podpriecinok.
        if (!System.Version.TryParse(offer.Version, out _) || offer.Tag != "v" + offer.Version || offer.File != "eFakturaViewer.exe" || offer.Runtime != "win-x64" || offer.Size < 10000 || offer.Size > 350L * 1024 * 1024) throw new InvalidDataException("Neplatné údaje aktualizácie.");
        return offer;
    }
    static async Task<byte[]> Small(HttpClient client, string url, CancellationToken token = default)
    {
        // Limitujeme aj realne precitane bajty. Server nemusi poslat Content-Length alebo moze
        // poslat nespravnu hodnotu; maly manifest nesmie naplnit pamat ako velke stahovanie.
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token); using var m = new MemoryStream(); byte[] buffer = new byte[4096]; int n;
        while ((n = await stream.ReadAsync(buffer, token)) != 0) { if (m.Length + n > 16384) throw new InvalidDataException("Príliš veľký popis aktualizácie."); m.Write(buffer, 0, n); } return m.ToArray();
    }
    internal static async Task<UpdateOffer?> Check(CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30)); token = deadline.Token;
        using var client = Client(); byte[] discovery;
        try { discovery = await Small(client, $"https://github.com/{Repository}/releases/latest/download/update.json", token); }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound) { return null; }
        // Odkaz latest sa moze pocas kontroly zmenit. Je iba vodidlom k tagu; manifest a jeho
        // podpis potom berieme z rovnakeho konkretneho vydania, aby sa nezmiesali dve verzie.
        var hint = JsonSerializer.Deserialize<UpdateOffer>(discovery) ?? throw new InvalidDataException("Neplatný popis.");
        if (!System.Version.TryParse(hint.Version, out var v) || hint.Tag != "v" + hint.Version) throw new InvalidDataException("Neplatná verzia.");
        var bytes = await Small(client, Asset(hint.Tag, "update.json"), token); var sig = System.Text.Encoding.UTF8.GetString(await Small(client, Asset(hint.Tag, "update.sig"), token));
        var offer = Manifest(bytes, sig);
        if (offer.Tag != hint.Tag) throw new InvalidDataException("Verzia popisu nesúhlasí.");
        return System.Version.Parse(offer.Version) > Assembly.GetExecutingAssembly().GetName().Version ? offer : null;
    }
    internal static void VerifyFile(string path, UpdateOffer offer, string? key = null)
    {
        // Dlzka zachyti neuplne stiahnutie, pravost potvrdzuje az RSA podpis celeho streamu.
        // Samotny hash z GitHubu by nepotvrdil, ze subor podpisal nas vydavatel.
        using var file = File.OpenRead(path); if (file.Length != offer.Size) throw new InvalidDataException("Neúplná aktualizácia.");
        using var rsa = RSA.Create(); rsa.FromXmlString(key ?? Key);
        if (!rsa.VerifyData(file, Convert.FromBase64String(offer.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new InvalidDataException("Podpis EXE nie je platný.");
        VerifyExecutableVersion(path, offer.Version);
    }
    internal static void VerifyExecutableVersion(string path, string expected)
    {
        var text = FileVersionInfo.GetVersionInfo(path).ProductVersion?.Split('+')[0];
        static Version Normal(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        if (!Version.TryParse(text, out var actual) || Normal(actual) != Normal(Version.Parse(expected)))
            throw new InvalidDataException("Verzia EXE nesúhlasí s podpísaným popisom aktualizácie.");
    }
    internal static async Task Install(UpdateOffer offer, string? reopen, IProgress<int> progress, CancellationToken token = default)
    {
        // Kazdy pokus ma vlastny priecinok, aby ciastocne stiahnutie z minuleho pokusu
        // nebolo povazovane za sucast noveho balika. Beziaci EXE sa tu este nemeni.
        var job = Path.Combine(Preferences.DirectoryPath, "Updates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job);
        using var jobLock = LockJob(job);
        try
        {
        using var client = Client();
        var bytes = await Small(client, Asset(offer.Tag, "update.json"), token); var sig = System.Text.Encoding.UTF8.GetString(await Small(client, Asset(offer.Tag, "update.sig"), token));
        // Medzi ponukou a potvrdenim mohol vydavatel vymenit subory vydania. Instalujeme
        // presne ponuknuty balik; zmenu nechame znovu prejst kontrolou aktualizacie.
        var verified = Manifest(bytes, sig); if (verified != offer) throw new InvalidDataException("Vydanie sa zmenilo. Skúste kontrolu znova.");
        File.WriteAllBytes(Path.Combine(job, "update.json"), bytes); File.WriteAllText(Path.Combine(job, "update.sig"), sig);
        var candidate = Path.Combine(job, offer.File);
        using (var response = await client.GetAsync(Asset(offer.Tag, offer.File), HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode(); await using var input = await response.Content.ReadAsStreamAsync(token); await using var output = File.Create(candidate);
            var buffer = new byte[65536]; int n; long total = 0;
            while ((n = await input.ReadAsync(buffer, token)) > 0) { total += n; if (total > offer.Size) throw new InvalidDataException("Súbor presahuje podpísanú veľkosť."); await output.WriteAsync(buffer.AsMemory(0, n), token); progress.Report((int)(total * 100 / offer.Size)); }
        }
        VerifyFile(candidate, offer);
        File.WriteAllText(Path.Combine(job, "reopen.json"), JsonSerializer.Serialize(reopen));
        var target = Environment.ProcessPath!; // Kontrola prava zapisu pred ukoncenim okna.
        var probe = Path.Combine(Path.GetDirectoryName(target)!, ".efaktura-write-" + Guid.NewGuid().ToString("N")); using (File.Create(probe)) { } File.Delete(probe);
        // Windows nedovoli prepisat spusteny EXE. Vymenu dokonci kopia povodnej aplikacie,
        // ktora pocka na ukoncenie okna a overi novy balik tym istym doveryhodnym klucom.
        var helper = Path.Combine(job, "updater.exe"); File.Copy(target, helper);
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
        // ArgumentList zachova cesty s medzerami bez skladania prikazu pre shell.
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(job); start.ArgumentList.Add(target); start.ArgumentList.Add(Environment.ProcessId.ToString());
        _ = Process.Start(start) ?? throw new IOException("Aktualizátor sa nespustil.");
        }
        catch
        {
            CompleteJob(job, null, null);
            throw;
        }
    }
    static FileStream LockJob(string job) => new(Path.Combine(job, "job.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    static FileStream WaitForJob(string job)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return LockJob(job); }
            catch (IOException) when (attempt < 100) { Thread.Sleep(100); }
        }
    }
    internal static int Apply(string job, string target, string parent, Action<string, string?, bool>? restart = null)
    {
        restart ??= Restart;
        bool parentStopped = false; string? reopen = null;
        try
        {
            using var jobLock = WaitForJob(job);
            try { reopen = JsonSerializer.Deserialize<string?>(File.ReadAllText(Path.Combine(job, "reopen.json"))); } catch { }
            try { using var process = Process.GetProcessById(int.Parse(parent)); if (!process.WaitForExit(60000)) throw new IOException("Aplikácia sa neukončila."); } catch (ArgumentException) { }
            parentStopped = true;
            // Kontrolu opakujeme v pomocnom procese, ale uz vieme, ci pri chybe treba obnovit okno.
            var offer = Manifest(File.ReadAllBytes(Path.Combine(job, "update.json")), File.ReadAllText(Path.Combine(job, "update.sig")));
            if (System.Version.Parse(offer.Version) <= Assembly.GetExecutingAssembly().GetName().Version) throw new InvalidDataException("Aktualizácia musí byť novšia.");
            var candidate = Path.Combine(job, offer.File); VerifyFile(candidate, offer);
            var backup = ReplaceAndStart(candidate, target, Path.GetFileName(job), p => VerifyFile(p, offer), p =>
            {
                var probe = new ProcessStartInfo(p) { UseShellExecute = false, CreateNoWindow = true }; probe.ArgumentList.Add("--probe");
                RunProbe(probe, 20000);
            }, () => restart(target, reopen, false));
            CompleteJob(job, target, backup); return 0;
        }
        catch (Exception e)
        {
            try { Directory.CreateDirectory(Preferences.DirectoryPath); File.WriteAllText(Path.Combine(Preferences.DirectoryPath, "update-error.txt"), e.ToString()); } catch { }
            // Aj chyba pred File.Replace musi vratit povodne okno a povodny dokument.
            if (parentStopped) try { restart(target, reopen, true); } catch { }
            CompleteJob(job, null, null); return 1;
        }
    }
    static void Restart(string target, string? reopen, bool failed)
    {
        var start = new ProcessStartInfo(target) { UseShellExecute = false };
        if (failed) start.ArgumentList.Add("--update-failed");
        if (!string.IsNullOrEmpty(reopen)) start.ArgumentList.Add(reopen);
        using var process = Process.Start(start) ?? throw new IOException("Aplikácia sa nespustila.");
    }
    internal static void RunProbe(ProcessStartInfo start, int timeout)
    {
        using var check = Process.Start(start) ?? throw new IOException("Novú verziu sa nepodarilo spustiť.");
        if (!check.WaitForExit(timeout))
        {
            // Iba proces, ktory sme sami spustili. Najprv ukoncit sondu a uvolnit EXE, az potom vratit zalohu.
            check.Kill(entireProcessTree: true);
            if (!check.WaitForExit(10000)) throw new IOException("Štartovaciu sondu sa nepodarilo ukončiť.");
            throw new IOException("Nová verzia prekročila čas kontroly štartu.");
        }
        if (check.ExitCode != 0) throw new IOException("Nová verzia neprešla kontrolou štartu.");
    }
    internal static string ReplaceAndStart(string candidate, string target, string id, Action<string> verify, Action<string> probe, Action start)
    {
        var next = target + ".next-" + id; var backup = target + ".previous-" + id;
        bool swapped = false;
        try
        {
            File.Copy(candidate, next, true); verify(next); File.Replace(next, target, backup, true); swapped = true;
            probe(target); start();
        }
        catch
        {
            if (swapped) File.Replace(backup, target, null, true);
            throw;
        }
        finally { TryDelete(next); }
        // Spustenie uspelo. Zamknuta zaloha je uz iba upratovanie, nikdy dovod vratit staru verziu.
        TryDelete(backup); return backup;
    }
    internal sealed record FinishedJob(string? Target, string? Backup);
    internal static void CompleteJob(string job, string? target, string? backup)
    {
        try { File.WriteAllText(Path.Combine(job, "completed.json"), JsonSerializer.Serialize(new FinishedJob(target, backup))); } catch { }
    }
    static void TryDelete(string path) { try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    internal static void Cleanup() => CleanupJobs(Path.Combine(Preferences.DirectoryPath, "Updates"), Environment.ProcessPath!);
    internal static void CleanupJobs(string root, string target)
    {
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
            foreach (var job in Directory.EnumerateDirectories(root))
            {
                // Vyhradne nase GUID priecinky, bez prechadzania odkazov alebo neznamych suborov.
                if (!Guid.TryParseExact(Path.GetFileName(job), "N", out _) || (File.GetAttributes(job) & FileAttributes.ReparsePoint) != 0) continue;
                var marker = Path.Combine(job, "completed.json");
                if (!File.Exists(marker) && Directory.GetLastWriteTimeUtc(job) > DateTime.UtcNow.AddDays(-7)) continue;
                try
                {
                    using (var jobLock = LockJob(job))
                    {
                        if (File.Exists(marker))
                        {
                            var done = JsonSerializer.Deserialize<FinishedJob>(File.ReadAllText(marker));
                            var backup = target + ".previous-" + Path.GetFileName(job);
                            if (done?.Target == target && done.Backup == backup)
                            {
                                TryDelete(backup);
                                if (File.Exists(backup)) continue;
                            }
                        }
                        // Pomocny EXE moze este bezat. Marker nechame, aby dalsi start upratovanie zopakoval.
                        foreach (var name in new[] { "updater.exe", "eFakturaViewer.exe", "update.json", "update.sig", "reopen.json" }) File.Delete(Path.Combine(job, name));
                        File.Delete(marker);
                    }
                    File.Delete(Path.Combine(job, "job.lock")); Directory.Delete(job);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

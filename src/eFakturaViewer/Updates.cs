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
        if (!rsa.VerifyData(data, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new InvalidDataException("Digitalny podpis aktualizacie nie je platny.");
    }
    internal static UpdateOffer Manifest(byte[] bytes, string signature, string? publicKey = null)
    {
        // Overujeme povodne bajty JSON, nie znovu serializovany objekt. Aj zmena medzier by
        // zmenila podpis; udajom o verzii, velkosti a podpise EXE doverujeme az po tomto kroku.
        Verify(bytes, signature, publicKey);
        var offer = JsonSerializer.Deserialize<UpdateOffer>(bytes) ?? throw new InvalidDataException("Chyba popis aktualizacie.");
        // Pevny nazov suboru a runtime udrzuju format balika jednoznacny. Nazov z manifestu
        // sa neskor pouziva ako cesta, preto tu nepripustame lubovolne meno ani podpriecinok.
        if (!System.Version.TryParse(offer.Version, out _) || offer.Tag != "v" + offer.Version || offer.File != "eFakturaViewer.exe" || offer.Runtime != "win-x64" || offer.Size < 10000 || offer.Size > 350L * 1024 * 1024) throw new InvalidDataException("Neplatne udaje aktualizacie.");
        return offer;
    }
    static async Task<byte[]> Small(HttpClient client, string url, CancellationToken token = default)
    {
        // Limitujeme aj realne precitane bajty. Server nemusi poslat Content-Length alebo moze
        // poslat nespravnu hodnotu; maly manifest nesmie naplnit pamat ako velke stahovanie.
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token); using var m = new MemoryStream(); byte[] buffer = new byte[4096]; int n;
        while ((n = await stream.ReadAsync(buffer, token)) != 0) { if (m.Length + n > 16384) throw new InvalidDataException("Prilis velky popis aktualizacie."); m.Write(buffer, 0, n); } return m.ToArray();
    }
    internal static async Task<UpdateOffer?> Check(CancellationToken token = default)
    {
        using var client = Client(); byte[] discovery;
        try { discovery = await Small(client, $"https://github.com/{Repository}/releases/latest/download/update.json", token); }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound) { return null; }
        // Odkaz latest sa moze pocas kontroly zmenit. Je iba vodidlom k tagu; manifest a jeho
        // podpis potom berieme z rovnakeho konkretneho vydania, aby sa nezmiesali dve verzie.
        var hint = JsonSerializer.Deserialize<UpdateOffer>(discovery) ?? throw new InvalidDataException("Neplatny popis.");
        if (!System.Version.TryParse(hint.Version, out var v) || hint.Tag != "v" + hint.Version) throw new InvalidDataException("Neplatna verzia.");
        var bytes = await Small(client, Asset(hint.Tag, "update.json"), token); var sig = System.Text.Encoding.UTF8.GetString(await Small(client, Asset(hint.Tag, "update.sig"), token));
        var offer = Manifest(bytes, sig);
        if (offer.Tag != hint.Tag) throw new InvalidDataException("Verzia popisu nesuhlasi.");
        return System.Version.Parse(offer.Version) > Assembly.GetExecutingAssembly().GetName().Version ? offer : null;
    }
    internal static void VerifyFile(string path, UpdateOffer offer, string? key = null)
    {
        // Dlzka zachyti neuplne stiahnutie, pravost potvrdzuje az RSA podpis celeho streamu.
        // Samotny hash z GitHubu by nepotvrdil, ze subor podpisal nas vydavatel.
        using var file = File.OpenRead(path); if (file.Length != offer.Size) throw new InvalidDataException("Neuplna aktualizacia.");
        using var rsa = RSA.Create(); rsa.FromXmlString(key ?? Key);
        if (!rsa.VerifyData(file, Convert.FromBase64String(offer.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new InvalidDataException("Podpis EXE nie je platny.");
    }
    internal static async Task Install(UpdateOffer offer, string? reopen, IProgress<int> progress, CancellationToken token = default)
    {
        // Kazdy pokus ma vlastny priecinok, aby ciastocne stiahnutie z minuleho pokusu
        // nebolo povazovane za sucast noveho balika. Beziaci EXE sa tu este nemeni.
        var job = Path.Combine(Preferences.DirectoryPath, "Updates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job);
        try
        {
        using var client = Client();
        var bytes = await Small(client, Asset(offer.Tag, "update.json"), token); var sig = System.Text.Encoding.UTF8.GetString(await Small(client, Asset(offer.Tag, "update.sig"), token));
        // Medzi ponukou a potvrdenim mohol vydavatel vymenit subory vydania. Instalujeme
        // presne ponuknuty balik; zmenu nechame znovu prejst kontrolou aktualizacie.
        var verified = Manifest(bytes, sig); if (verified != offer) throw new InvalidDataException("Vydanie sa zmenilo. Skuste kontrolu znova.");
        File.WriteAllBytes(Path.Combine(job, "update.json"), bytes); File.WriteAllText(Path.Combine(job, "update.sig"), sig);
        var candidate = Path.Combine(job, offer.File);
        using (var response = await client.GetAsync(Asset(offer.Tag, offer.File), HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode(); await using var input = await response.Content.ReadAsStreamAsync(token); await using var output = File.Create(candidate);
            var buffer = new byte[65536]; int n; long total = 0;
            while ((n = await input.ReadAsync(buffer, token)) > 0) { total += n; if (total > offer.Size) throw new InvalidDataException("Subor presahuje podpisanu velkost."); await output.WriteAsync(buffer.AsMemory(0, n), token); progress.Report((int)(total * 100 / offer.Size)); }
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
        _ = Process.Start(start) ?? throw new IOException("Aktualizator sa nespustil.");
        }
        catch
        {
            // Iba subory tejto konkretnej operacie; nikdy neprechadzat ine aktualizacie ani cielovy EXE.
            foreach (var name in new[] { "eFakturaViewer.exe", "update.json", "update.sig", "reopen.json", "updater.exe" })
                try { File.Delete(Path.Combine(job, name)); } catch (IOException) { }
            try { Directory.Delete(job); } catch (IOException) { }
            throw;
        }
    }
    internal static int Apply(string job, string target, string parent)
    {
        try
        {
            // Pomocny proces cita subory nanovo, preto nemoze spoliehat iba na kontrolu v okne.
            // Podpis aj verziu overi este pred tym, nez sa dotkne cieloveho EXE.
            var offer = Manifest(File.ReadAllBytes(Path.Combine(job, "update.json")), File.ReadAllText(Path.Combine(job, "update.sig")));
            if (System.Version.Parse(offer.Version) <= Assembly.GetExecutingAssembly().GetName().Version) throw new InvalidDataException("Aktualizacia musi byt novsia.");
            var candidate = Path.Combine(job, offer.File); VerifyFile(candidate, offer);
            try { using var process = Process.GetProcessById(int.Parse(parent)); if (!process.WaitForExit(60000)) throw new IOException("Aplikacia sa neukoncila."); } catch (ArgumentException) { }
            var next = target + ".next"; var backup = target + ".previous";
            // Pripravena kopia lezi vedla ciela na rovnakom disku. File.Replace vymeni subor
            // so zalohou; priamy zapis do ciela by pri preruseni nechal nefunkcny EXE.
            File.Copy(candidate, next, true); VerifyFile(next, offer); File.Replace(next, target, backup, true);
            try
            {
                // Platny podpis este nezarucuje spustitelnost na tomto PC. Zalohu ponechame,
                // kym prejde sonda a spustenie bezneho procesu vrati uspech. Jeho neskorsi pad uz nesledujeme.
                var probe = new ProcessStartInfo(target) { UseShellExecute = false, CreateNoWindow = true }; probe.ArgumentList.Add("--probe");
                using var check = Process.Start(probe) ?? throw new IOException("Novu verziu sa nepodarilo spustit.");
                if (!check.WaitForExit(20000) || check.ExitCode != 0) throw new IOException("Nova verzia nepresla kontrolou startu.");
                var start = new ProcessStartInfo(target) { UseShellExecute = false }; var reopen = JsonSerializer.Deserialize<string?>(File.ReadAllText(Path.Combine(job, "reopen.json"))); if (!string.IsNullOrEmpty(reopen)) start.ArgumentList.Add(reopen);
                _ = Process.Start(start) ?? throw new IOException("Aplikacia sa nespustila."); File.Delete(backup);
            }
            catch { File.Replace(backup, target, null, true); Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); throw; }
            File.Delete(candidate); return 0;
        }
        catch (Exception e) { Directory.CreateDirectory(Preferences.DirectoryPath); File.WriteAllText(Path.Combine(Preferences.DirectoryPath, "update-error.txt"), e.ToString()); return 1; }
    }
}

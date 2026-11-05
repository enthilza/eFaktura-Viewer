using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace eFakturaViewer;

internal static class UpdateTransportTests
{
    sealed class Handler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(response(++Calls)); }
    }
    sealed class Progress(Action<int> report) : IProgress<int> { public void Report(int value) => report(value); }
    sealed class BrokenStream(byte[] bytes) : MemoryStream(bytes)
    {
        bool first = true;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (!first) throw new IOException("Simulovany vypadok pocas tela odpovede");
            first = false; return base.ReadAsync(buffer[..Math.Min(17, buffer.Length)], token);
        }
    }
    internal static async Task Run(string output, List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        async Task<T> Failure<T>(Func<Task> action) where T : Exception
        { try { await action(); } catch (T e) { return e; } throw new Exception("Ocakavana chyba " + typeof(T).Name); }
        const string url = "https://updates.invalid/test";
        var waits = new List<TimeSpan>();
        Task Wait(TimeSpan duration, CancellationToken token) { token.ThrowIfCancellationRequested(); waits.Add(duration); return Task.CompletedTask; }
        HttpResponseMessage Body(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        var payload = Encoding.UTF8.GetBytes("podpisany popis aktualizacie");
        var statuses = new Handler(attempt => attempt == 1 ? new(HttpStatusCode.GatewayTimeout) : attempt == 2 ? new(HttpStatusCode.ServiceUnavailable) : Body(payload));
        using (var client = new HttpClient(statuses))
        {
            var result = await UpdateTransport.Small(client, url, CancellationToken.None, wait: Wait);
            Check(result.SequenceEqual(payload) && statuses.Calls == 3 && waits.SequenceEqual(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }), "HTTP 504 a 503 sa automaticky zopakuju s kratkymi odstupmi");
        }
        var unavailable = new Handler(_ => new(HttpStatusCode.GatewayTimeout));
        using (var client = new HttpClient(unavailable))
        {
            var error = await Failure<HttpRequestException>(() => UpdateTransport.Small(client, url, CancellationToken.None, wait: Wait));
            Check(unavailable.Calls == 3 && error.StatusCode == HttpStatusCode.GatewayTimeout, "Trvaly vypadok skonci po troch pokusoch");
            Check(UpdateTransport.Message(error).Contains("neodpovedal včas") && !UpdateTransport.Message(error).Contains("Response status"), "Chyba 504 ma slovenske vysvetlenie");
        }
        var missing = new Handler(_ => new(HttpStatusCode.NotFound));
        using (var client = new HttpClient(missing))
        {
            var error = await Failure<HttpRequestException>(() => UpdateTransport.Small(client, url, CancellationToken.None, wait: Wait));
            Check(missing.Calls == 1 && UpdateTransport.Message(error).Contains("kontrolu aktualizácií znova"), "Odstranene vydanie sa neopakuje ako docasny vypadok");
        }
        var oversized = new Handler(_ => Body(new byte[16385]));
        using (var client = new HttpClient(oversized))
        {
            await Failure<InvalidDataException>(() => UpdateTransport.Small(client, url, CancellationToken.None, wait: Wait));
            Check(oversized.Calls == 1, "Limit manifestu zostava platny a jeho porusenie sa neopakuje");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            var cancelHandler = new Handler(_ => new(HttpStatusCode.BadGateway));
            using var client = new HttpClient(cancelHandler);
            await Failure<OperationCanceledException>(() => UpdateTransport.Small(client, url, cancellation.Token, wait: (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }));
            Check(cancelHandler.Calls == 1, "Zrusenie pocas prestavky zabrani dalsiemu pokusu");
        }
        int timeoutCalls = 0;
        await Failure<TimeoutException>(() => UpdateTransport.Retry<bool>(_ => { timeoutCalls++; throw new TaskCanceledException("Casovy limit poziadavky"); }, CancellationToken.None, wait: Wait));
        Check(timeoutCalls == 3, "Sietovy timeout po troch pokusoch je chyba a nie tiche zrusenie pouzivatelom");
        int signatureCalls = 0;
        await Failure<InvalidDataException>(() => UpdateTransport.Retry<bool>(_ => { signatureCalls++; throw new InvalidDataException("Podpis EXE nie je platný."); }, CancellationToken.None, wait: Wait));
        Check(signatureCalls == 1, "Chyba integrity sa nikdy nepovazuje za docasny sietovy problem");

        var directory = Path.Combine(output, "update-transport-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var candidate = Path.Combine(directory, "candidate.bin");
        var bytes = Enumerable.Range(0, 100000).Select(i => (byte)(i % 251)).ToArray();
        var progress = new List<int>(); var reporter = new Progress(progress.Add);
        var interrupted = new Handler(attempt => attempt == 1 ? new(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream(bytes)) } : Body(bytes));
        using (var client = new HttpClient(interrupted))
        {
            await UpdateTransport.Download(client, url, candidate, bytes.Length, reporter, CancellationToken.None, Wait);
            Check(interrupted.Calls == 2 && File.ReadAllBytes(candidate).SequenceEqual(bytes), "Prerusene telo sa stiahne od zaciatku bez zlepenia bajtov");
            int retryIndex = progress.IndexOf(-2);
            Check(retryIndex > 0 && retryIndex + 1 < progress.Count && progress[retryIndex + 1] == 0 && progress[^1] == 100, "Priebeh oznami druhy pokus a percenta zacnu znova");
        }
        var truncated = new Handler(attempt => Body(attempt == 1 ? bytes[..100] : bytes));
        using (var client = new HttpClient(truncated))
        {
            await UpdateTransport.Download(client, url, candidate, bytes.Length, reporter, CancellationToken.None, Wait);
            Check(truncated.Calls == 2 && File.ReadAllBytes(candidate).SequenceEqual(bytes), "Predcasny koniec suboru sa zopakuje a zachova ocakavanu velkost");
        }
        var tooLarge = new Handler(_ => Body(bytes));
        using (var client = new HttpClient(tooLarge))
        {
            await Failure<InvalidDataException>(() => UpdateTransport.Download(client, url, candidate, 50, reporter, CancellationToken.None, Wait));
            Check(tooLarge.Calls == 1, "Prekrocenie podpisanej velkosti sa odmietne bez opakovania");
        }
        File.WriteAllText(candidate, "povodny subor");
        var locked = new Handler(_ => Body(bytes));
        using (var client = new HttpClient(locked))
        using (var held = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Failure<IOException>(() => UpdateTransport.Download(client, url, candidate, bytes.Length, reporter, CancellationToken.None, Wait));
            Check(locked.Calls == 1, "Chyba zapisu na disk sa neopakuje ako chyba siete");
        }
        Check(File.ReadAllText(candidate) == "povodny subor", "Zamknuty pracovny subor zostane nezmeneny");
    }
}

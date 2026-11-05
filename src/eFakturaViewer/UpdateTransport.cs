using System.IO;
using System.Net;
using System.Net.Http;

namespace eFakturaViewer;

internal static class UpdateTransport
{
    static bool Temporary(HttpRequestException e) => e.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    internal static async Task<T> Retry<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token, IProgress<int>? progress = null, Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        wait ??= Task.Delay;
        for (int attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            Exception failure;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                // ResponseHeadersRead ukonci HTTP volanie uz pri hlavickach.
                // Vlastny limit preto zahrna aj telo suboru, nie iba nadviazanie spojenia.
                deadline.CancelAfter(TimeSpan.FromMinutes(5));
                try { return await operation(deadline.Token); }
                catch (HttpRequestException e) when (Temporary(e) && !token.IsCancellationRequested) { failure = e; }
                catch (OperationCanceledException e) when (!token.IsCancellationRequested) { failure = new TimeoutException("Server aktualizácií neodpovedal včas.", e); }
            }
            if (attempt == 3) throw failure;
            // Opakujeme iba citanie zo siete, nikdy podpisovanie, overenie balika alebo
            // vymenu spusteneho EXE. Zrusenie musi fungovat aj pocas cakania medzi pokusmi.
            progress?.Report(-(attempt + 1));
            await wait(TimeSpan.FromSeconds(attempt), token);
        }
    }

    static async Task<int> Read(Stream stream, byte[] buffer, CancellationToken token)
    {
        try { return await stream.ReadAsync(buffer.AsMemory(), token); }
        catch (IOException e) { token.ThrowIfCancellationRequested(); throw new HttpRequestException("Spojenie sa prerušilo počas sťahovania.", e); }
    }

    internal static Task<byte[]> Small(HttpClient client, string url, CancellationToken token, IProgress<int>? progress = null, Func<TimeSpan, CancellationToken, Task>? wait = null) => Retry(async attemptToken =>
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, attemptToken); response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(attemptToken);
        using var memory = new MemoryStream(); byte[] buffer = new byte[4096]; int count;
        while ((count = await Read(stream, buffer, attemptToken)) != 0)
        {
            if (memory.Length + count > 16384) throw new InvalidDataException("Príliš veľký popis aktualizácie.");
            memory.Write(buffer, 0, count);
        }
        if (response.Content.Headers.ContentLength is long expected && memory.Length != expected) throw new HttpRequestException("Popis aktualizácie sa nestiahol celý.");
        return memory.ToArray();
    }, token, progress, wait);

    internal static Task<bool> Download(HttpClient client, string url, string path, long expectedSize, IProgress<int> progress, CancellationToken token, Func<TimeSpan, CancellationToken, Task>? wait = null) => Retry(async attemptToken =>
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, attemptToken); response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(attemptToken);
        // Ciel je len pracovny subor aktualizacie. Novy pokus ho skrati, aby sa
        // nezlepili dve rozne odpovede ani nezostal koniec predosleho stiahnutia.
        await using var output = File.Create(path);
        progress.Report(0); byte[] buffer = new byte[65536]; int count; long total = 0;
        while ((count = await Read(input, buffer, attemptToken)) != 0)
        {
            total += count;
            if (total > expectedSize) throw new InvalidDataException("Súbor presahuje podpísanú veľkosť.");
            await output.WriteAsync(buffer.AsMemory(0, count), attemptToken);
            progress.Report((int)Math.Min(99, total * 100 / expectedSize));
        }
        if (total != expectedSize) throw new HttpRequestException("Aktualizácia sa nestiahla celá.");
        progress.Report(100);
        return true;
    }, token, progress, wait);

    internal static string Message(Exception e) => e switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "Ponúknuté vydanie už nie je dostupné. Spustite kontrolu aktualizácií znova.",
        HttpRequestException { StatusCode: HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout } => "Server pri sťahovaní neodpovedal včas. Skúste aktualizáciu znova o chvíľu.",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "Server dočasne obmedzil sťahovanie. Skúste aktualizáciu znova neskôr.",
        HttpRequestException { StatusCode: { } code } => $"Server aktualizácií vrátil chybu HTTP {(int)code}. Skúste to znova o chvíľu.",
        HttpRequestException => "Spojenie so serverom aktualizácií zlyhalo alebo sa prerušilo. Skontrolujte pripojenie a skúste to znova.",
        TimeoutException or OperationCanceledException => "Sťahovanie sa nepodarilo dokončiť včas. Skúste aktualizáciu znova o chvíľu.",
        System.Text.Json.JsonException => "Server poskytol neplatný popis aktualizácie. Aktualizácia sa nenainštalovala.",
        _ => e.Message
    };
    internal static string ProgressText(int value) => value < 0 ? $"Spojenie sa prerušilo. Opakujem sťahovanie · pokus {-value} z 3…" : value >= 100 ? "Overujem digitálny podpis…" : $"Sťahujem aktualizáciu · {value} %";
}

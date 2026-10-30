using System.Net.Http.Headers;
using System.Text.Json;

namespace PokeHub.Core.Plugins;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);
public sealed record ReleaseInfo(string Tag, DateTimeOffset PublishedAt, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Consulta a última release de um repositório público do GitHub.</summary>
public sealed class GitHubReleaseClient
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PokeHub", "0.1"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public async Task<ReleaseInfo> GetLatestAsync(string repo, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(a => new ReleaseAsset(
                a.GetProperty("name").GetString()!,
                a.GetProperty("browser_download_url").GetString()!,
                a.GetProperty("size").GetInt64()))
            .ToList();
        return new ReleaseInfo(
            root.GetProperty("tag_name").GetString() ?? "unknown",
            root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTimeOffset() : default,
            assets);
    }

    public async Task DownloadAsync(string url, string destination, IProgress<string>? log, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destination);
        var buffer = new byte[1 << 16];
        long done = 0; int lastPct = -1; int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            done += n;
            if (total is > 0)
            {
                var pct = (int)(done * 100 / total.Value);
                if (pct / 10 != lastPct / 10) { lastPct = pct; log?.Report($"  download {pct}%"); }
            }
        }
    }
}

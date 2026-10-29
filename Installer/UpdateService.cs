using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PainterSetup;
record ReleaseConfig(string Repository, string BaiduUrl, string BaiduCode);
record ReleaseManifest(string Version, string FileName, string Sha256, long Size, string BaiduUrl, string BaiduCode);
record UpdateRelease(ReleaseManifest Manifest, Uri DownloadUrl, Uri PageUrl);
static class UpdateService
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public static readonly ReleaseConfig Config = JsonSerializer.Deserialize<ReleaseConfig>(InstallService.ReadResource("Painter.ReleaseConfig"), Json)!;
    static readonly HttpClient Client = CreateClient();
    static HttpClient CreateClient() { var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) }; c.DefaultRequestHeaders.UserAgent.ParseAdd("SinkingStarPainter/" + InstallService.Version); return c; }
    public static bool ValidRepository(string value) => Regex.IsMatch(value ?? "", @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");
    public static bool ValidBaidu(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "pan.baidu.com" && uri.AbsolutePath.StartsWith("/s/");
    public static Version ParseVersion(string value) => Regex.IsMatch(value ?? "", @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$") ? Version.Parse(value!) : throw new InvalidDataException("发布版本号无效。");
    public static ReleaseManifest ParseManifest(string json, string tag)
    {
        var m = JsonSerializer.Deserialize<ReleaseManifest>(json, Json) ?? throw new InvalidDataException("缺少更新清单。");
        ParseVersion(m.Version);
        if (tag != "v" + m.Version || m.FileName != "SinkingStarPainter-Setup.exe" || !Regex.IsMatch(m.Sha256 ?? "", "^[a-fA-F0-9]{64}$") || m.Size <= 0 || m.Size > 250_000_000 || (!string.IsNullOrEmpty(m.BaiduUrl) && !ValidBaidu(m.BaiduUrl)))
            throw new InvalidDataException("更新清单内容不一致，已拒绝下载。");
        return m;
    }
    static Uri AssetUrl(JsonElement release, string repository, string tag, string name)
    {
        var assets = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("正式版本尚未上传完整附件：" + name);
        var url = assets[0].GetProperty("browser_download_url").GetString();
        var expected = $"https://github.com/{repository}/releases/download/{tag}/{name}";
        if (url != expected) throw new InvalidDataException("发布附件地址不匹配。");
        return new Uri(expected);
    }
    public static async Task<UpdateRelease> CheckAsync()
    {
        if (!ValidRepository(Config.Repository)) throw new InvalidOperationException("尚未配置 GitHub 仓库。请在发布前填写 release-config.json 并重新构建。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await Client.GetAsync($"https://api.github.com/repos/{Config.Repository}/releases/latest", timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException("仓库尚无正式版本，或仓库不可公开访问。");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        var r = doc.RootElement;
        if (r.GetProperty("draft").GetBoolean() || r.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException("不是正式发布版本。");
        var tag = r.GetProperty("tag_name").GetString()!;
        if (!tag.StartsWith('v')) throw new InvalidDataException("发布标签无效。");
        ParseVersion(tag[1..]);
        var manifestUrl = AssetUrl(r, Config.Repository, tag, "release.json");
        var manifest = ParseManifest(await Client.GetStringAsync(manifestUrl, timeout.Token), tag);
        return new(manifest, AssetUrl(r, Config.Repository, tag, manifest.FileName), new Uri($"https://github.com/{Config.Repository}/releases/tag/{tag}"));
    }
    public static async Task DownloadAsync(UpdateRelease release, string destination, HttpClient? client = null)
    {
        // Stage beside destination; no executable becomes available until length/hash verification succeeds.
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var response = await (client ?? Client).GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await source.ReadAsync(buffer, timeout.Token)) > 0) { total += count; if (total > release.Manifest.Size) throw new InvalidDataException("下载文件大小不符。"); await target.WriteAsync(buffer.AsMemory(0, count), timeout.Token); }
            }
            VerifyFile(temporary, release.Manifest);
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void VerifyFile(string file, ReleaseManifest m)
    {
        using var stream = File.OpenRead(file);
        if (stream.Length != m.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(m.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA256 校验失败，文件不能用于安装。");
    }
}

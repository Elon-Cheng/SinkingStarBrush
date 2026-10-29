using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace PainterSetup;
static class UpdateTests
{
    sealed class ResponseHandler(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) });
    }
    static void Require(bool value, string label) { if (!value) throw new Exception(label); }
    public static async Task Run(string folder)
    {
        Require(UpdateService.ParseVersion("0.10.0") > UpdateService.ParseVersion("0.9.9"), "numeric version comparison");
        Require(!UpdateService.ValidBaidu("https://pan.baidu.com.evil.test/s/test") && !UpdateService.ValidBaidu("http://pan.baidu.com/s/test"), "mirror host validation");
        byte[] bytes = [1, 2, 3, 4];
        var manifest = new ReleaseManifest("0.3.1", "SinkingStarPainter-Setup.exe", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "", "");
        var json = JsonSerializer.Serialize(manifest);
        Require(UpdateService.ParseManifest(json, "v0.3.1") == manifest, "valid manifest");
        foreach (var invalid in new[] { manifest with { Version = "0.3.2" }, manifest with { Sha256 = "bad" }, manifest with { FileName = "../x.exe" }, manifest with { Size = 0 }, manifest with { BaiduUrl = "file:///x" } })
        {
            bool rejected = false; try { UpdateService.ParseManifest(JsonSerializer.Serialize(invalid), "v0.3.1"); } catch (InvalidDataException) { rejected = true; } Require(rejected, "invalid manifest rejected");
        }
        var release = new UpdateRelease(manifest, new Uri("https://github.com/test/repo/releases/download/v0.3.1/SinkingStarPainter-Setup.exe"), new Uri("https://github.com/test/repo/releases/tag/v0.3.1"));
        using (var client = new HttpClient(new ResponseHandler(bytes)))
        {
            var destination = Path.Combine(folder, "verified.exe");
            await UpdateService.DownloadAsync(release, destination, client);
            Require(File.ReadAllBytes(destination).SequenceEqual(bytes), "verified bytes delivered");
        }
        foreach (var damaged in new byte[][] { [4, 3, 2, 1], [1, 2], [1, 2, 3, 4, 5] })
        {
            var destination = Path.Combine(folder, Guid.NewGuid() + ".exe");
            using var client = new HttpClient(new ResponseHandler(damaged));
            bool rejected = false; try { await UpdateService.DownloadAsync(release, destination, client); } catch (InvalidDataException) { rejected = true; }
            Require(rejected && !File.Exists(destination) && Directory.GetFiles(folder, "*.part").Length == 0, "bad download not exposed and stage cleaned");
        }
        using (var client = new HttpClient(new ResponseHandler([], HttpStatusCode.ServiceUnavailable)))
        {
            bool rejected = false; try { await UpdateService.DownloadAsync(release, Path.Combine(folder, "network-failure.exe"), client); } catch (HttpRequestException) { rejected = true; } Require(rejected, "HTTP failure handled");
        }
    }
}

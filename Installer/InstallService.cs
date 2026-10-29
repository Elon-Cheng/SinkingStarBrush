using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PainterSetup;

record Receipt(string Product, string Version, string Sha256);
static class InstallService
{
    public static readonly string Version = JsonDocument.Parse(ReadResource("Painter.Version")).RootElement.GetProperty("version").GetString()!;
    internal static string ReadResource(string name) { using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    public static readonly byte[] Payload = ReadPayload();
    public static string PayloadHash => Convert.ToHexString(SHA256.HashData(Payload));
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    static byte[] ReadPayload() { using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.dxgi.dll")!; using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray(); }
    public static bool Running() { var processes = Process.GetProcessesByName("sinking_star"); foreach (var process in processes) process.Dispose(); return processes.Length != 0; }
    public static string Validate(string folder)
    {
        folder = Path.GetFullPath(folder.Trim());
        if (!File.Exists(Path.Combine(folder, "sinking_star.exe"))) throw new InvalidOperationException("请选择包含 sinking_star.exe 的游戏目录。");
        return folder;
    }
    public static List<string> Discover()
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in new[] { Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath"), Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")?.GetValue("InstallPath") })
            if (value is string s && Directory.Exists(s)) steamRoots.Add(s);
        steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        foreach (var steam in steamRoots)
        {
            roots.Add(steam);
            string libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraries)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s*\"([^\"]+)\"")) roots.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        var result = new List<string>();
        foreach (var root in roots)
        {
            var manifest = Path.Combine(root, "steamapps", "appmanifest_499170.acf");
            if (!File.Exists(manifest)) continue;
            var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s*\"([^\"]+)\"");
            if (!match.Success) continue;
            var folder = Path.GetFullPath(Path.Combine(root, "steamapps", "common", match.Groups[1].Value));
            if (File.Exists(Path.Combine(folder, "sinking_star.exe"))) result.Add(folder);
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static Receipt? Owned(string folder)
    {
        string dll = Path.Combine(folder, "dxgi.dll"), file = Path.Combine(folder, "sinking-star-painter-install.json");
        if (!File.Exists(dll)) return null;
        if (!File.Exists(file)) throw new InvalidOperationException("发现其他来源的 dxgi.dll，不能覆盖或卸载。请先处理其他插件的冲突。");
        Receipt? receipt;
        try { receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(file), Json); }
        catch (JsonException) { throw new InvalidOperationException("安装记录损坏，无法确认现有 DLL 的归属。未修改文件。"); }
        if (receipt == null || receipt.Product != "SinkingStarInGame" || !string.Equals(receipt.Sha256, Hash(dll), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("现有 DLL 与插件安装记录不一致，未修改文件。");
        return receipt;
    }
    static string Hash(string file) { using var s = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(s)); }
    public static string Status(string folder)
    {
        folder = Validate(folder); var owned = Owned(folder);
        return owned == null ? "尚未安装" : $"已安装 v{owned.Version}" + (owned.Sha256 == PayloadHash ? " · 当前版本" : " · 可更新 / 修复");
    }
    public static void Install(string folder, bool checkRunning = true)
    {
        folder = Validate(folder);
        if (checkRunning && Running()) throw new InvalidOperationException("请先完全退出《沉星之序》，再安装或更新。当前运行中的游戏不会热更新。");
        var existing = Owned(folder); // Refuse foreign DLLs before writing anything.
        if (existing != null && System.Version.Parse(existing.Version) > System.Version.Parse(Version)) throw new InvalidOperationException("已安装版本高于此安装器，请使用最新安装器，避免降级。");
        string dll = Path.Combine(folder, "dxgi.dll"), receipt = Path.Combine(folder, "sinking-star-painter-install.json");
        var stage = Path.Combine(folder, ".painter-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string backup = Path.Combine(folder, "sinking-star-painter-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        bool oldMoved = false, newMoved = false, hadReceipt = File.Exists(receipt);
        byte[]? oldReceipt = hadReceipt ? File.ReadAllBytes(receipt) : null;
        try
        {
            var stagedDll = Path.Combine(stage, "dxgi.dll"); File.WriteAllBytes(stagedDll, Payload);
            if (Hash(stagedDll) != PayloadHash) throw new IOException("插件文件校验失败。");
            var stagedReceipt = Path.Combine(stage, "receipt.json"); File.WriteAllText(stagedReceipt, JsonSerializer.Serialize(new Receipt("SinkingStarInGame", Version, PayloadHash), Json));
            if (checkRunning && Running()) throw new InvalidOperationException("检测到游戏正在启动，请先退出游戏。");
            if (File.Exists(dll)) { Directory.CreateDirectory(backup); File.Move(dll, Path.Combine(backup, "dxgi.dll")); oldMoved = true; if (oldReceipt != null) File.WriteAllBytes(Path.Combine(backup, "receipt.json"), oldReceipt); }
            File.Move(stagedDll, dll); newMoved = true;
            File.Move(stagedReceipt, receipt, true);
        }
        catch
        {
            if (newMoved) File.Delete(dll);
            if (oldMoved) File.Move(Path.Combine(backup, "dxgi.dll"), dll);
            if (oldReceipt != null) File.WriteAllBytes(receipt, oldReceipt);
            else if (File.Exists(receipt)) File.Delete(receipt);
            throw;
        }
        finally { foreach (var f in Directory.EnumerateFiles(stage)) File.Delete(f); Directory.Delete(stage); }
    }
    public static void Uninstall(string folder, bool checkRunning = true)
    {
        folder = Validate(folder);
        if (checkRunning && Running()) throw new InvalidOperationException("请先完全退出游戏，再卸载插件。");
        if (Owned(folder) == null) return;
        string dll = Path.Combine(folder, "dxgi.dll"), receipt = Path.Combine(folder, "sinking-star-painter-install.json");
        string backup = Path.Combine(folder, "sinking-star-painter-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup); File.Move(dll, Path.Combine(backup, "dxgi.dll"));
        try { File.Move(receipt, Path.Combine(backup, "receipt.json")); }
        catch { File.Move(Path.Combine(backup, "dxgi.dll"), dll); throw; }
    }
}

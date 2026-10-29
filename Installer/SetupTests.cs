using System.IO;
using System.Text.Json;

namespace PainterSetup;
static class SetupTests
{
    public static int Run()
    {
        var report = Path.Combine(Path.GetTempPath(), "SinkingStarSetup-test-result.txt");
        var folder = Path.Combine(Path.GetTempPath(), "SinkingStarSetupTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "sinking_star.exe"), "test fixture");
            InstallService.Install(folder, false);
            Require(InstallService.Owned(folder)?.Sha256 == InstallService.PayloadHash, "install integrity");
            InstallService.Install(folder, false); Require(Directory.GetDirectories(folder, "sinking-star-painter-backup-*").Length == 1, "update backup");
            var receiptPath = Path.Combine(folder, "sinking-star-painter-install.json");
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(new Receipt("SinkingStarInGame", "999.0.0", InstallService.PayloadHash)));
            bool downgradeRefused = false; try { InstallService.Install(folder, false); } catch (InvalidOperationException) { downgradeRefused = true; }
            Require(downgradeRefused && InstallService.Owned(folder)?.Version == "999.0.0", "downgrade refused without changes");
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(new Receipt("SinkingStarInGame", InstallService.Version, InstallService.PayloadHash)));
            File.AppendAllText(Path.Combine(folder, "dxgi.dll"), "tamper");
            bool refused = false; try { InstallService.Install(folder, false); } catch (InvalidOperationException) { refused = true; } Require(refused, "tampered DLL refused");
            File.WriteAllBytes(Path.Combine(folder, "dxgi.dll"), InstallService.Payload);
            InstallService.Uninstall(folder, false); Require(!File.Exists(Path.Combine(folder, "dxgi.dll")), "uninstall");
            File.WriteAllText(Path.Combine(folder, "dxgi.dll"), "foreign mod");
            refused = false; try { InstallService.Install(folder, false); } catch (InvalidOperationException) { refused = true; }
            Require(refused && File.ReadAllText(Path.Combine(folder, "dxgi.dll")) == "foreign mod", "foreign file preserved");
            UpdateTests.Run(folder).GetAwaiter().GetResult();
            File.WriteAllText(report, "PASS: install, checksum, update backup, downgrade refusal, tamper refusal, uninstall, foreign-file preservation; version comparison, manifest validation, verified download, corrupt/truncated/oversize download refusal and staging cleanup.\n" + folder); return 0;
        }
        catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); return 1; }
    }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}

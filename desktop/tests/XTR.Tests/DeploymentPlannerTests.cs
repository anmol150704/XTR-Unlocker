using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class DeploymentPlannerTests
{
    private static string GetBundledPackagePath()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "desktop", "src", "Runtime", "OptiScaler");
            if (Directory.Exists(candidate))
                return Path.GetFullPath(candidate);

            var directCandidate = Path.Combine(current, "src", "Runtime", "OptiScaler");
            if (Directory.Exists(directCandidate))
                return Path.GetFullPath(directCandidate);

            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the desktop/src/Runtime/OptiScaler folder.");
    }

    [TestMethod]
    public async Task CreatePlan_GeneratesDryRunWithoutTouchingGameDirectory()
    {
        var tempGameDir = Path.Combine(Path.GetTempPath(), $"xtr_dryrun_game_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempGameDir);
            var exePath = Path.Combine(tempGameDir, "ForzaHorizon5.exe");
            await File.WriteAllTextAsync(exePath, "ORIGINAL_GAME_EXE_CONTENT");

            // Snapshot directory state before plan creation
            var filesBefore = Directory.GetFiles(tempGameDir, "*", SearchOption.AllDirectories)
                .Select(f => new { Path = f, Length = new FileInfo(f).Length, LastWrite = new FileInfo(f).LastWriteTimeUtc })
                .ToList();

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "fh5-test-id",
                "Forza Horizon 5",
                tempGameDir,
                pack,
                RuntimeIntegrationPreset.LatestDlssOnly,
                "dxgi.dll");

            Assert.IsTrue(plan.CanExecute);
            Assert.AreEqual("Enable latest DLSS", plan.Headline);
            Assert.IsTrue(plan.PlannedCopies.Count >= 10, $"Expected multiple planned copies, got {plan.PlannedCopies.Count}");
            Assert.AreEqual("dxgi.dll", plan.SelectedProxy);
            Assert.IsTrue(plan.PlannedCopies.Any(c => c.RelativePath == "dxgi.dll"));

            // VERIFY STRICT SAFETY: Game directory must remain completely untouched after plan creation!
            var filesAfter = Directory.GetFiles(tempGameDir, "*", SearchOption.AllDirectories)
                .Select(f => new { Path = f, Length = new FileInfo(f).Length, LastWrite = new FileInfo(f).LastWriteTimeUtc })
                .ToList();

            Assert.AreEqual(filesBefore.Count, filesAfter.Count, "Dry run plan must NOT add or remove files from the game folder.");
            Assert.AreEqual(filesBefore[0].Path, filesAfter[0].Path);
            Assert.AreEqual(filesBefore[0].Length, filesAfter[0].Length);
            Assert.AreEqual(filesBefore[0].LastWrite, filesAfter[0].LastWrite);
        }
        finally
        {
            if (Directory.Exists(tempGameDir))
                Directory.Delete(tempGameDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task CreatePlan_IdentifiesFilesNeedingBackup()
    {
        var tempGameDir = Path.Combine(Path.GetTempPath(), $"xtr_backup_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempGameDir);
            await File.WriteAllTextAsync(Path.Combine(tempGameDir, "game.exe"), "GAME_EXE");
            await File.WriteAllTextAsync(Path.Combine(tempGameDir, "dxgi.dll"), "ORIGINAL_SYSTEM_DXGI_DLL");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "backup-game-id",
                "Game With Dxgi",
                tempGameDir,
                pack,
                RuntimeIntegrationPreset.LatestDlssOnly,
                "dxgi.dll");

            Assert.IsTrue(plan.CanExecute);
            Assert.AreEqual(1, plan.PlannedBackups.Count, "Should detect 1 file needing backup.");
            Assert.AreEqual("dxgi.dll", plan.PlannedBackups[0].GameRelativePath);

            var dxgiAction = plan.PlannedCopies.FirstOrDefault(c => c.RelativePath == "dxgi.dll");
            Assert.IsNotNull(dxgiAction);
            Assert.IsTrue(dxgiAction.WillOverwriteExisting);
            Assert.IsTrue(dxgiAction.RequiresBackup);
        }
        finally
        {
            if (Directory.Exists(tempGameDir))
                Directory.Delete(tempGameDir, recursive: true);
        }
    }
}

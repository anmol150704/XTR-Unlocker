using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier2_BoundaryCornerCases;

[TestClass]
[DoNotParallelize]
public sealed class Fh6DiscoveryAndSwapBoundaryTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    #region F01 Boundaries: Recursive Case-Insensitive Discovery

    [TestMethod]
    public async Task B01_DeeplyNestedStreamlineDirectory_FiveLevelsDeep_Discovered()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b01_deep");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("Engine/Plugins/Runtime/Nvidia/Streamline/Binaries/ThirdParty/Win64/sl.interposer.dll", "SL_INTERPOSER");
        sandbox.CreateFile("Engine/Plugins/Runtime/Nvidia/Streamline/Binaries/ThirdParty/Win64/sl.dlss.dll", "SL_DLSS");
        sandbox.CreateFile("Engine/Plugins/Runtime/Nvidia/Streamline/Binaries/ThirdParty/Win64/sl.dlss_g.dll", "SL_DLSSG");

        var pack = await LoadPackAsync();
        var preflight = await RuntimePreflightService.RunPreflightAsync("b01-deep", sandbox.GameRoot, pack);

        Assert.IsTrue(preflight.StreamlineDetected, "Streamline 7 levels deep must be discovered.");
        Assert.IsTrue(preflight.NativeDlssGDetected, "sl.dlss_g.dll in deep subdirectory must be discovered.");
        Assert.IsTrue(preflight.StreamlineDlssDetected, "sl.dlss.dll / Streamline NGX capability must be discovered.");
        Assert.IsTrue(UpscalerDiscoveryService.Discover(sandbox.GameRoot).HasAnyDlssCapability);
    }

    [TestMethod]
    public async Task B01_CyclicReparsePointJunction_SkippedSafelyWithoutInfiniteRecursion()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b01_reparse");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        string junctionPath = Path.Combine(sandbox.GameRoot, "loop_junction");

        if (OperatingSystem.IsWindows())
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junctionPath}\" \"{sandbox.GameRoot}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            proc?.WaitForExit(5000);
        }

        try
        {
            var pack = await LoadPackAsync();
            var preflight = await RuntimePreflightService.RunPreflightAsync("b01-reparse", sandbox.GameRoot, pack);
            Assert.IsTrue(preflight.CanProceed, "Reparse point junction must be skipped cleanly without hanging or crashing.");
        }
        finally
        {
            if (Directory.Exists(junctionPath))
                Directory.Delete(junctionPath, recursive: false);
        }
    }

    [TestMethod]
    public async Task B01_MissingGameRootOrCrashHandlerOnly_BlockedCleanly()
    {
        var pack = await LoadPackAsync();
        var missingResult = await RuntimePreflightService.RunPreflightAsync(
            "b01-missing", Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}"), pack);
        Assert.AreEqual(PreflightStatus.Blocked, missingResult.Status);
        Assert.IsFalse(missingResult.CanProceed);

        using var sandbox = new IsolatedGameSandbox("t2_b01_noexe");
        sandbox.CreateFile("Readme.txt", "Not a game exe");
        var noExeResult = await RuntimePreflightService.RunPreflightAsync("b01-noexe", sandbox.GameRoot, pack);
        Assert.AreEqual(PreflightStatus.Blocked, noExeResult.Status);
        Assert.IsFalse(noExeResult.CanProceed);
    }

    [TestMethod]
    public async Task B01_UpperCaseDllAndExeNames_AllDetectedCaseInsensitively()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b01_upper");
        sandbox.CreateFile("FORZAHORIZON6.EXE", "EXE");
        sandbox.CreateFile("D3D12CORE.DLL", "DX12");
        sandbox.CreateFile("SUBDIR/NVNGX_DLSS.DLL", "DLSS");
        sandbox.CreateFile("SUBDIR/SL.INTERPOSER.DLL", "SL");

        var pack = await LoadPackAsync();
        var preflight = await RuntimePreflightService.RunPreflightAsync(
            "b01-upper", sandbox.GameRoot, pack, proxyName: "version.dll", gameTitle: "FORZA HORIZON 6");

        Assert.IsTrue(preflight.CanProceed);
        Assert.AreEqual("DX12", preflight.DetectedApi);
        Assert.IsTrue(preflight.NativeDlssDetected);
        Assert.IsTrue(preflight.StreamlineDetected);
    }

    [TestMethod]
    public async Task B01_ApiDetection_VulkanAndDx11Precedence()
    {
        var pack = await LoadPackAsync();

        using (var vkSandbox = new IsolatedGameSandbox("t2_b01_vk"))
        {
            vkSandbox.CreateFile("Game.exe", "EXE");
            vkSandbox.CreateFile("vulkan-1.dll", "VK");
            var vkResult = await RuntimePreflightService.RunPreflightAsync("b01-vk", vkSandbox.GameRoot, pack);
            Assert.AreEqual("Vulkan", vkResult.DetectedApi);
        }

        using (var dx11Sandbox = new IsolatedGameSandbox("t2_b01_dx11"))
        {
            dx11Sandbox.CreateFile("Game.exe", "EXE");
            dx11Sandbox.CreateFile("d3d11.dll", "DX11");
            var dx11Result = await RuntimePreflightService.RunPreflightAsync("b01-dx11", dx11Sandbox.GameRoot, pack);
            Assert.AreEqual("DX11", dx11Result.DetectedApi);
        }
    }

    #endregion

    #region F02 Boundaries: Swap & Target Resolution Isolation

    [TestMethod]
    public async Task B02_InstallPlan_WithNullPackageRoot_ThrowsInvalidOperationException()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b02_nullpack");
        sandbox.CreateFile("Game.exe", "EXE");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "b02-nullpack", "Game", sandbox.GameRoot, pack, RuntimeIntegrationPreset.LatestDlssOnly,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        plan.Pack.PackageRoot = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => RuntimeInstallService.InstallPlanAsync(plan));
    }

    [TestMethod]
    public async Task B02_InstallAsync_UnsupportedProxyName_ThrowsInvalidDataException()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b02_badproxy");
        sandbox.CreateFile("Game.exe", "EXE");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => RuntimeInstallService.InstallAsync("b02-badproxy", sandbox.GameRoot, TestPaths.BundledPackagePath, "ntdll.dll"));
    }

    [TestMethod]
    public async Task B02_ManifestStore_PathTraversalRelativePath_Rejected()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b02_traversal");
        var badManifest = new RuntimeInstallManifest
        {
            GameId = "b02-traversal",
            RuntimeId = "optiscaler-xtr",
            RuntimeVersion = "0.9.5",
            ProxyFileName = "dxgi.dll",
            InstallRoot = sandbox.GameRoot,
            Files =
            [
                new RuntimeOwnedFile
                {
                    RelativePath = "../escape.dll",
                    InstalledSha256 = new string('a', 64)
                }
            ]
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => RuntimeInstallManifestStore.SaveAsync(badManifest));
    }

    [TestMethod]
    public async Task B02_PreExistingProxyCollision_CreatesExactOriginalBackup()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b02_collision");
        sandbox.CreateFile("ForzaHorizon6.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var stockProxy = sandbox.CreateFile("version.dll", "ORIGINAL_FORZA_VERSION_DLL");
        string expectedHash = IsolatedGameSandbox.ComputeSha256Hex(stockProxy);

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "b02-collision", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        Assert.IsTrue(plan.PlannedBackups.Exists(b => b.GameRelativePath == "version.dll" && string.Equals(b.ExistingSha256, expectedHash, StringComparison.OrdinalIgnoreCase)));

        var manifest = await RuntimeInstallService.InstallPlanAsync(plan);
        var proxyEntry = manifest.Files.Find(f => f.RelativePath == "version.dll");
        Assert.IsNotNull(proxyEntry);
        Assert.IsNotNull(proxyEntry.OriginalBackupPath);
        Assert.IsTrue(string.Equals(expectedHash, proxyEntry.OriginalSha256, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task B02_RepeatedPresetUpgrades_KeepSingleManifestAndRestoreCleanly()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b02_multigrade");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var gpu = IsolatedGameSandbox.CreateRtx4070();

        var p1 = await DeploymentPlanner.CreatePlanAsync("b02-multi", "Cyberpunk 2077", sandbox.GameRoot, pack, RuntimeIntegrationPreset.LatestDlssOnly, gpuInfo: gpu);
        await RuntimeInstallService.InstallPlanAsync(p1);

        var p2 = await DeploymentPlanner.CreatePlanAsync("b02-multi", "Cyberpunk 2077", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: gpu);
        await RuntimeInstallService.UpgradePlanAsync(p2);

        var p3 = await DeploymentPlanner.CreatePlanAsync("b02-multi", "Cyberpunk 2077", sandbox.GameRoot, pack, RuntimeIntegrationPreset.NativeDlssG, gpuInfo: gpu);
        await RuntimeInstallService.UpgradePlanAsync(p3);

        var profile = await RuntimeIntegrationProfileStore.LoadAsync("b02-multi");
        Assert.IsNotNull(profile);
        Assert.AreEqual("NativeDlssG", profile.Options["Preset"]?.ToString());

        await RuntimeInstallService.RestoreAsync("b02-multi", sandbox.GameRoot);
        Assert.IsFalse(sandbox.FileExists("dxgi.dll"));
        Assert.IsNull(await RuntimeInstallManifestStore.LoadAsync("b02-multi"));
    }

    #endregion
}

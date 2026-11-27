using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier4_RealWorldScenarios;

[TestClass]
[DoNotParallelize]
public sealed class RealWorldApplicationScenarioTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack, "OptiScaler runtime pack must load cleanly.");
        return pack;
    }

    /// <summary>
    /// Scenario 1 (F01, F02, F03, F04, F05, F06, F09):
    /// FH6 Steam subdirectory layout (Streamline / DirectSR / FidelityFX-SDK2 + NGX-OTA)
    /// -> version.dll plan -> Install -> sl.dlss.dll isolation -> HealthyActive ->
    /// Tolerant Restore with missing OptiScaler.ini &amp; externally modified amd_fidelityfx_dx12.dll.
    /// </summary>
    [TestMethod]
    public async Task Scenario1_Fh6SteamLayout_Install_SlDlssIsolation_TolerantRestore()
    {
        using var sandbox = new IsolatedGameSandbox("t4_sc1_fh6");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_BINARY");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var ffxDx12Path = sandbox.CreateFile("amd_fidelityfx_dx12.dll", "FFX_SDK2_DX12");
        string ffxDx12HashBefore = IsolatedGameSandbox.ComputeSha256Hex(ffxDx12Path);
        sandbox.CreateFile("amd_fidelityfx_upscaler_dx12.dll", "FFX_SDK2_UPSCALER");
        sandbox.CreateFile("amd_fidelityfx_framegeneration_dx12.dll", "FFX_SDK2_FG");
        sandbox.CreateFile("Plugins/Streamline/sl.interposer.dll", "SL_INTERPOSER");
        sandbox.CreateFile("Plugins/Streamline/sl.directsr.dll", "SL_DIRECTSR");
        var slDlssPath = sandbox.CreateFile("Plugins/Streamline/sl.dlss.dll", "SL_DLSS_PLUGIN");
        sandbox.CreateFile("Plugins/Streamline/sl.dlss_g.dll", "SL_DLSSG");
        sandbox.CreateFile("Plugins/Streamline/sl.common.dll", "SL_COMMON");
        string slDlssHashBefore = IsolatedGameSandbox.ComputeSha256Hex(slDlssPath);

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "fh6-sc1", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        Assert.IsTrue(plan.CanExecute);
        Assert.IsTrue(plan.PreflightResult.StreamlineDetected);
        Assert.IsTrue(plan.PreflightResult.StreamlineDlssDetected, "NGX-OTA + Streamline FH6 must detect DLSS capability.");
        Assert.IsTrue(UpscalerDiscoveryService.Discover(sandbox.GameRoot).HasAnyDlssCapability);
        Assert.AreEqual("nvngx_dlss.dll", SwapTargetResolver.ResolveTargetForMissingAsset(UpscalerSwapSlot.Dlss, sandbox.GameRoot).TargetFileName);
        Assert.AreEqual("version.dll", plan.SelectedProxy);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps").Value);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFix").Value);
        Assert.AreEqual("0.05", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "AccAddPerFrame").Value);

        await RuntimeInstallService.InstallPlanAsync(plan);
        Assert.AreEqual(slDlssHashBefore, IsolatedGameSandbox.ComputeSha256Hex(slDlssPath), "sl.dlss.dll must stay isolated.");

        string deployedIni = await File.ReadAllTextAsync(sandbox.GetFullPath("OptiScaler.ini"));
        Assert.AreEqual("dlssg", RuntimeConfigService.ReadIniValue(deployedIni, "FrameGen", "FGInput"));
        Assert.AreNotEqual("true", RuntimeConfigService.ReadIniValue(deployedIni, "Menu", "ShowFps"));

        sandbox.CreateFile("OptiScaler.log", "[OptiScaler] Hook D3D12 DLSS\n[Streamline] slSetTag\n[OptiFG] fg->Present Frametime: 7.9ms");
        var health = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "version.dll");
        Assert.AreEqual(RuntimeHealthState.HealthyActive, health.State);
        Assert.IsTrue(health.FrameGenerationActive);

        // Simulate real-world FH6 drift: delete OptiScaler.ini & overwrite amd_fidelityfx_dx12.dll (backed up stock)
        File.Delete(sandbox.GetFullPath("OptiScaler.ini"));
        await File.WriteAllTextAsync(sandbox.GetFullPath("amd_fidelityfx_dx12.dll"), "STEAM_MODIFIED_FFX");

        await RuntimeInstallService.RestoreAsync("fh6-sc1", sandbox.GameRoot);
        Assert.IsFalse(sandbox.FileExists("version.dll"));
        Assert.IsNull(await RuntimeInstallManifestStore.LoadAsync("fh6-sc1"));
        Assert.IsNull(await RuntimeIntegrationProfileStore.LoadAsync("fh6-sc1"));
    }

    /// <summary>
    /// Scenario 2 (F01, F04, F05, F07, F08):
    /// Multi-launcher directory discovery & sandbox matrix:
    /// Steam nested UE5 mixed-case Win64, Xbox GDK writable vs ACL-locked, and Non-RTX AMD 4GB VRAM guardrail.
    /// </summary>
    [TestMethod]
    public async Task Scenario2_MultiLauncherDiscovery_AndNonRtx4GbGuardrailMatrix()
    {
        var pack = await LoadPackAsync();

        // 1. Steam UE5 nested mixed-case layout
        using (var steamSandbox = new IsolatedGameSandbox("t4_sc2_steam", customGameSubfolderName: "steamapps/common/Ue5Title/Game/Binaries/Win64"))
        {
            steamSandbox.CreateFile("Game-Win64-Shipping.exe", "UE5_EXE");
            steamSandbox.CreateFile("D3D12Core.dll", "DX12");
            steamSandbox.CreateFile("ThirdParty/NVIDIA/NVNGX_DLSS.DLL", "DLSS");

            var preflight = await RuntimePreflightService.RunPreflightAsync("sc2-steam", steamSandbox.GameRoot, pack, gameTitle: "UE5 Title");
            Assert.IsTrue(preflight.CanProceed);
            Assert.IsTrue(preflight.NativeDlssDetected);
        }

        // 2. Xbox GDK writable vs WindowsApps locked layout
        using (var xboxSandbox = new IsolatedGameSandbox("t4_sc2_xbox", customGameSubfolderName: "XboxGame"))
        {
            xboxSandbox.CreateFile("MicrosoftGame.config", "<Game/>");
            xboxSandbox.CreateFile("gamelaunchhelper.exe", "HELPER");
            xboxSandbox.CreateFile("Game.exe", "EXE");
            xboxSandbox.CreateFile("d3d12.dll", "DX12");

            var writablePreflight = await RuntimePreflightService.RunPreflightAsync("sc2-xbox-ok", xboxSandbox.GameRoot, pack);
            Assert.IsTrue(writablePreflight.CanProceed);

            if (OperatingSystem.IsWindows())
            {
                string user = Environment.UserName;
                using (var denyProc = Process.Start(new ProcessStartInfo
                {
                    FileName = "icacls",
                    Arguments = $"\"{xboxSandbox.GameRoot}\" /deny \"{user}:(WD,AD)\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                }))
                {
                    denyProc?.WaitForExit(5000);
                }

                try
                {
                    var lockedPreflight = await RuntimePreflightService.RunPreflightAsync("sc2-xbox-locked", xboxSandbox.GameRoot, pack);
                    Assert.AreEqual(PreflightStatus.Blocked, lockedPreflight.Status);
                    Assert.IsFalse(lockedPreflight.CanProceed);
                    Assert.AreEqual("Xbox / WindowsApps Sandbox Locked", lockedPreflight.Headline);
                }
                finally
                {
                    using var restoreAcl = Process.Start(new ProcessStartInfo
                    {
                        FileName = "icacls",
                        Arguments = $"\"{xboxSandbox.GameRoot}\" /remove:d \"{user}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    restoreAcl?.WaitForExit(5000);
                }
            }
        }

        // 3. Non-RTX AMD 4GB VRAM guardrail universal fallback
        using (var amdSandbox = new IsolatedGameSandbox("t4_sc2_amd4gb"))
        {
            amdSandbox.CreateFile("Game.exe", "EXE");
            amdSandbox.CreateFile("d3d12.dll", "DX12");

            var amd4Gb = IsolatedGameSandbox.CreateAmdRx7800Xt();
            amd4Gb.VramBytes = 4096UL * 1024 * 1024;

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "sc2-amd", "Universal AMD Game", amdSandbox.GameRoot, pack,
                RuntimeIntegrationPreset.TurboBoost, gpuInfo: amd4Gb);

            Assert.AreEqual("fsr31", plan.PlannedConfigModifications.First(m => m.Section == "Upscalers" && m.Key == "Dx12Upscaler").Value);
            Assert.AreEqual("fsrfg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeMVCopy").Value);
            Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps").Value);

            await RuntimeInstallService.InstallPlanAsync(plan);
            await RuntimeInstallService.RestoreAsync("sc2-amd", amdSandbox.GameRoot);
            Assert.IsFalse(amdSandbox.FileExists("dxgi.dll"));
        }
    }

    /// <summary>
    /// Scenario 3 (F03, F04, F05, F07, F09):
    /// Cyberpunk 2077 TurboBoost lifecycle:
    /// Preflight -> Install -> HealthyActive Log -> In-Place Upgrade -> Proxy Deleted Health Failure -> Restore.
    /// </summary>
    [TestMethod]
    public async Task Scenario3_Cyberpunk2077TurboBoost_UpgradeHealthAndRestoreLifecycle()
    {
        using var sandbox = new IsolatedGameSandbox("t4_sc3_cp2077");
        sandbox.CreateFile("Cyberpunk2077.exe", "CP_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("sl.interposer.dll", "SL");
        sandbox.CreateFile("sl.dlss_g.dll", "SL_DLSSG");
        var stockDxgi = sandbox.CreateFile("dxgi.dll", "STOCK_DXGI_BINARY");
        string stockHash = IsolatedGameSandbox.ComputeSha256Hex(stockDxgi);

        var pack = await LoadPackAsync();
        var rtx3080 = IsolatedGameSandbox.CreateRtx3080();

        var plan1 = await DeploymentPlanner.CreatePlanAsync(
            "sc3-cp", "Cyberpunk 2077", sandbox.GameRoot, pack, RuntimeIntegrationPreset.LatestDlssOnly, gpuInfo: rtx3080);
        await RuntimeInstallService.InstallPlanAsync(plan1);
        Assert.AreEqual(RuntimeHealthState.InstalledNotYetLaunched, RuntimeHealthService.CheckHealth(sandbox.GameRoot, "dxgi.dll").State);

        var plan2 = await DeploymentPlanner.CreatePlanAsync(
            "sc3-cp", "Cyberpunk 2077", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: rtx3080);
        await RuntimeInstallService.UpgradePlanAsync(plan2);

        sandbox.CreateFile("OptiScaler.log", "[OptiScaler] Hook D3D12 DLSS\n[Streamline] slSetTag\n[OptiFG] fg->Present Frametime: 9.1ms");
        var activeHealth = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "dxgi.dll");
        Assert.AreEqual(RuntimeHealthState.HealthyActive, activeHealth.State);
        Assert.IsTrue(activeHealth.FrameGenerationActive);

        // Delete proxy and confirm health immediately reports ErrorDetected and inactive FG
        File.Delete(stockDxgi);
        var brokenHealth = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "dxgi.dll");
        Assert.AreEqual(RuntimeHealthState.ErrorDetected, brokenHealth.State);
        Assert.IsFalse(brokenHealth.ProxyFilePresent);
        Assert.IsFalse(brokenHealth.FrameGenerationActive);

        // Tolerant restore recovers stock dxgi.dll from preserved original backup
        await RuntimeInstallService.RestoreAsync("sc3-cp", sandbox.GameRoot);
        Assert.AreEqual(stockHash, IsolatedGameSandbox.ComputeSha256Hex(stockDxgi));
    }

    /// <summary>
    /// Scenario 4 (F03, F08):
    /// Multiplayer Anti-Cheat Hard Block (VALORANT / Apex / Vanguard / EAC)
    /// &amp; Offline Single-Player Override (Elden Ring + EasyAntiCheat.exe).
    /// </summary>
    [TestMethod]
    public async Task Scenario4_MultiplayerAntiCheatBlock_AndOfflineSinglePlayerOverride()
    {
        using var sandbox = new IsolatedGameSandbox("t4_sc4_anticheat");
        sandbox.CreateFile("VALORANT-Win64-Shipping.exe", "VAL_EXE");
        sandbox.CreateFile("Vanguard/vgk.sys", "VGK");

        var pack = await LoadPackAsync();
        var gpu = IsolatedGameSandbox.CreateRtx4070();

        var valPlan = await DeploymentPlanner.CreatePlanAsync(
            "sc4-val", "VALORANT", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: gpu);
        Assert.IsFalse(valPlan.CanExecute);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => RuntimeInstallService.InstallPlanAsync(valPlan));
        Assert.IsFalse(sandbox.FileExists("dxgi.dll"));

        using var erSandbox = new IsolatedGameSandbox("t4_sc4_eldenring");
        erSandbox.CreateFile("eldenring.exe", "ER_EXE");
        erSandbox.CreateFile("d3d12.dll", "DX12");
        erSandbox.CreateFile("EasyAntiCheat/EasyAntiCheat.exe", "EAC");

        var erBlocked = await DeploymentPlanner.CreatePlanAsync(
            "sc4-er", "Elden Ring", erSandbox.GameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: gpu);
        Assert.IsFalse(erBlocked.CanExecute);

        var erAllowed = await DeploymentPlanner.CreatePlanAsync(
            "sc4-er", "Elden Ring", erSandbox.GameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: gpu, allowOfflineAntiCheatOverride: true);
        Assert.IsTrue(erAllowed.CanExecute);
        Assert.AreEqual(PreflightStatus.NeedsAttention, erAllowed.PreflightResult.Status);

        await RuntimeInstallService.InstallPlanAsync(erAllowed);
        Assert.IsTrue(erSandbox.FileExists("dxgi.dll"));
        await RuntimeInstallService.RestoreAsync("sc4-er", erSandbox.GameRoot);
        Assert.IsFalse(erSandbox.FileExists("dxgi.dll"));
    }

    /// <summary>
    /// Scenario 5 (F09, F11, F12):
    /// Tampered Runtime Pack Rejection &amp; Exact xtr-runtime.json + Release Packaging Contract Audit.
    /// </summary>
    [TestMethod]
    public async Task Scenario5_TamperedRuntimePackRejection_AndReleasePackagingAudit()
    {
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        Assert.AreEqual(XtrPackagingValidatorContract.ExpectedPinnedFileCount, manifest.FileHashes.Count);
        Assert.IsTrue(XtrPackagingValidatorContract.VerifyRuntimeFilesIntegrity(TestPaths.BundledPackagePath, manifest, out var errors),
            string.Join("; ", errors));
        Assert.AreEqual(0, XtrPackagingValidatorContract.FindUntrackedRuntimeFiles(TestPaths.BundledPackagePath, manifest).Count);
        Assert.IsFalse(File.Exists(TestPaths.XtrHudAsiPath));

        string script = File.ReadAllText(TestPaths.BuildWindowsScriptPath);
        string workflow = File.ReadAllText(TestPaths.ReleaseWorkflowPath);
        Assert.IsFalse(script.Contains("xtr_hud.asi", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(script.Contains("XTR-Unlocker-Setup.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(workflow.Contains("XTR-Unlocker-Release-win-x64.zip", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(workflow.Contains("XTR-Unlocker-Release_Portable-win-x64.zip", StringComparison.OrdinalIgnoreCase));

        using var sandbox = new IsolatedGameSandbox("t4_sc5_tamper");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var tempPackRoot = sandbox.CopyBundledPackToTemp();
        await File.AppendAllTextAsync(Path.Combine(tempPackRoot, "OptiScaler.ini"), "\nShowFps=true\n");

        var tamperedPack = await RuntimePack.LoadFromDirectoryAsync(tempPackRoot);
        Assert.IsNotNull(tamperedPack);
        Assert.IsFalse(tamperedPack.IsTrusted);

        var plan = await DeploymentPlanner.CreatePlanAsync(
            "sc5-tamper", "Game", sandbox.GameRoot, tamperedPack, RuntimeIntegrationPreset.LatestDlssOnly,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());
        Assert.IsFalse(plan.CanExecute);
        Assert.IsFalse(sandbox.FileExists("dxgi.dll"));
    }

    /// <summary>
    /// Scenario 6 (F09, F10, F12):
    /// Unified Native App Architecture &amp; Monitoring-Removal Audit
    /// (MainWindow.xaml Mica, XtrStudioPage.xaml / GameControl.xaml zero FPS overlay, compiled DLL zero WebView2/Electron/legacy telemetry deps).
    /// </summary>
    [TestMethod]
    public void Scenario6_UnifiedNativeAppArchitecture_AndMonitoringRemovalAudit()
    {
        string mainXaml = File.ReadAllText(TestPaths.MainWindowXamlPath);
        string studioXaml = File.ReadAllText(TestPaths.XtrStudioPageXamlPath);
        string gameCtrlXaml = File.ReadAllText(TestPaths.GameControlXamlPath);

        Assert.IsTrue(mainXaml.Contains("MicaBackdrop", StringComparison.Ordinal));
        Assert.IsFalse(studioXaml.Contains("PresentMon", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(studioXaml.Contains("XtrTelemetrySharedMem", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(studioXaml.Contains("SimulatedHudFps", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(gameCtrlXaml.Contains("InGameHudOverlayEnabled", StringComparison.Ordinal));
        Assert.IsFalse(gameCtrlXaml.Contains("[Page Up]", StringComparison.OrdinalIgnoreCase));

        var guide = new RuntimeHealthReport().InGameOverlayGuide;
        Assert.IsFalse(guide.Contains("FPS", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("DLSS 310 - v310.6", DlssVersionHelper.FormatVersionLabel("310.6.0.0"));

        var appDll = XtrNativeAppContract.FindCompiledAppAssemblyPath();
        if (appDll != null)
        {
            Assert.IsTrue(XtrNativeAppContract.ValidatePeAssemblyReferences(appDll, out var offendingRefs),
                $"Prohibited references found: {string.Join(", ", offendingRefs)}");
            Assert.AreEqual(0, XtrNativeAppContract.FindProhibitedMonitoringTypeNames(appDll).Count);
        }
    }
}

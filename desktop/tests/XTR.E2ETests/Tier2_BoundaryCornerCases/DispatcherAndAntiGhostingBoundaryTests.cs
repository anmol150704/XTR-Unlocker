using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier2_BoundaryCornerCases;

[TestClass]
[DoNotParallelize]
public sealed class DispatcherAndAntiGhostingBoundaryTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    #region F05 Boundaries: Streamline & Universal Dispatcher

    [TestMethod]
    public async Task B05_AsyncLocalFgOutputOverride_DoesNotLeakAcrossTasks()
    {
        var taskA = Task.Run(() =>
        {
            DeploymentPlanner.OverrideFgOutput = "xefg";
            return DeploymentPlanner.ResolveFgOutput();
        });

        var taskB = Task.Run(async () =>
        {
            await Task.Delay(15);
            return DeploymentPlanner.ResolveFgOutput();
        });

        Assert.AreEqual("xefg", await taskA);
        Assert.AreEqual("fsrfg", await taskB);
    }

    [TestMethod]
    public void B05_FgBackendAliases_NormalizeToCanonicalTokens()
    {
        Assert.AreEqual("nukems", DeploymentPlanner.ResolveFgOutput("nukem"));
        Assert.AreEqual("nukems", DeploymentPlanner.ResolveFgOutput("nukem9"));
        Assert.AreEqual("xefg", DeploymentPlanner.ResolveFgOutput("intel"));
        Assert.AreEqual("xefg", DeploymentPlanner.ResolveFgOutput("xess"));
        Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput("fsr31"));
        Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput("amd"));
    }

    [TestMethod]
    public void B05_EmptyOrWhitespaceBackend_DefaultsToFsrfg()
    {
        Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput(null));
        Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput(string.Empty));
        Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput("   "));
    }

    [TestMethod]
    public void B05_IniSectionIsolation_NeverLeaksKeysIntoHotfixSection()
    {
        string baseIni = "[Menu]\nOverlayMenu=true\n\n[OptiFG]\nHUDFix=true\n\n[Hotfix]\nCrashFix=1\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "OptiFG", "MakeMVCopy", "true");

        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(mutated, "OptiFG", "MakeMVCopy"));
        Assert.AreEqual("1", RuntimeConfigService.ReadIniValue(mutated, "Hotfix", "CrashFix"));
        Assert.IsNull(RuntimeConfigService.ReadIniValue(mutated, "Hotfix", "MakeMVCopy"));
    }

    [TestMethod]
    public async Task B05_VulkanAndDx11Games_TargetCorrectUpscalerIniKeys()
    {
        var pack = await LoadPackAsync();
        var gpu = IsolatedGameSandbox.CreateRtx3080();

        using (var vk = new IsolatedGameSandbox("t2_b05_vk"))
        {
            vk.CreateFile("Game.exe", "EXE");
            vk.CreateFile("vulkan-1.dll", "VK");
            var plan = await DeploymentPlanner.CreatePlanAsync("vk", "RDR2", vk.GameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: gpu);
            Assert.IsTrue(plan.PlannedConfigModifications.Any(m => m.Section == "Upscalers" && m.Key == "VulkanUpscaler" && m.Value == "dlss"));
        }

        using (var dx11 = new IsolatedGameSandbox("t2_b05_dx11"))
        {
            dx11.CreateFile("Game.exe", "EXE");
            dx11.CreateFile("d3d11.dll", "DX11");
            var plan = await DeploymentPlanner.CreatePlanAsync("dx11", "BG3", dx11.GameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: gpu);
            Assert.IsTrue(plan.PlannedConfigModifications.Any(m => m.Section == "Upscalers" && m.Key == "Dx11Upscaler" && m.Value == "dlss"));
        }
    }

    #endregion

    #region F06 Boundaries: Racing Anti-Ghosting & INI Persistence

    [TestMethod]
    public void B06_AntiGhostingValues_RoundTripWithoutCultureDecimalDrift()
    {
        var prevCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            string ini = "[FSR]\nAccAddPerFrame=0.333\nMinDisOccAcc=-0.333\n";
            string updated = RuntimeConfigService.ReplaceIniValues(ini, new[]
            {
                new PlannedConfigEntry { Section = "FSR", Key = "AccAddPerFrame", Value = 0.001.ToString("0.###", CultureInfo.InvariantCulture) },
                new PlannedConfigEntry { Section = "FSR", Key = "MinDisOccAcc", Value = (-1.0).ToString("0.0", CultureInfo.InvariantCulture) }
            });

            Assert.AreEqual("0.001", RuntimeConfigService.ReadIniValue(updated, "FSR", "AccAddPerFrame"));
            Assert.AreEqual("-1.0", RuntimeConfigService.ReadIniValue(updated, "FSR", "MinDisOccAcc"));
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
        }
    }

    [TestMethod]
    public void B06_MissingFsrAndInitFlagsSections_CreatedAndPopulatedCleanly()
    {
        string baseIni = "[General]\nLogLevel=1\n";
        string updated = RuntimeConfigService.ReplaceIniValues(baseIni, new[]
        {
            new PlannedConfigEntry { Section = "InitFlags", Key = "DisableReactiveMask", Value = "false" },
            new PlannedConfigEntry { Section = "FSR", Key = "AccAddPerFrame", Value = "0.05" },
            new PlannedConfigEntry { Section = "FSR", Key = "MinDisOccAcc", Value = "-0.5" }
        });

        Assert.AreEqual("1", RuntimeConfigService.ReadIniValue(updated, "General", "LogLevel"));
        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(updated, "InitFlags", "DisableReactiveMask"));
        Assert.AreEqual("0.05", RuntimeConfigService.ReadIniValue(updated, "FSR", "AccAddPerFrame"));
        Assert.AreEqual("-0.5", RuntimeConfigService.ReadIniValue(updated, "FSR", "MinDisOccAcc"));
    }

    [TestMethod]
    public async Task B06_ApplyModifications_PreservesUserCommentsAndCustomSections()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b06_comments");
        sandbox.CreateFile("OptiScaler.ini", "; User custom header\n[UserCustom]\nMyFlag=42\n\n[FSR]\nAccAddPerFrame=0.333\n");

        await RuntimeConfigService.ApplyModificationsAsync(sandbox.GameRoot, new[]
        {
            new PlannedConfigEntry { Section = "FSR", Key = "AccAddPerFrame", Value = "0.05" }
        });

        string result = await System.IO.File.ReadAllTextAsync(sandbox.GetFullPath("OptiScaler.ini"));
        Assert.IsTrue(result.Contains("; User custom header", StringComparison.Ordinal));
        Assert.AreEqual("42", RuntimeConfigService.ReadIniValue(result, "UserCustom", "MyFlag"));
        Assert.AreEqual("0.05", RuntimeConfigService.ReadIniValue(result, "FSR", "AccAddPerFrame"));
    }

    [TestMethod]
    public async Task B06_RepeatedUpdateSettings_NeverDuplicatesKeys()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b06_nodup");
        sandbox.CreateFile("OptiScaler.ini", "[FSR]\nAccAddPerFrame=0.333\n");

        for (int i = 0; i < 10; i++)
        {
            await RuntimeConfigService.UpdateSettingAsync(sandbox.GameRoot, "FSR", "AccAddPerFrame", i % 2 == 0 ? "0.05" : "0.15");
        }

        string result = await System.IO.File.ReadAllTextAsync(sandbox.GetFullPath("OptiScaler.ini"));
        int occurrences = result.Split("AccAddPerFrame", StringSplitOptions.None).Length - 1;
        Assert.AreEqual(1, occurrences);
        Assert.AreEqual("0.15", RuntimeConfigService.ReadIniValue(result, "FSR", "AccAddPerFrame"));
    }

    [TestMethod]
    public void B06_HighVelocityRacingMatcher_DistinguishesRacingVsOpenWorldTitles()
    {
        Assert.IsTrue(DeploymentPlanner.IsHighVelocityRacingTitle("Forza Horizon 6"));
        Assert.IsTrue(DeploymentPlanner.IsHighVelocityRacingTitle("F1 24"));
        Assert.IsTrue(DeploymentPlanner.IsHighVelocityRacingTitle("Assetto Corsa Evo"));
        Assert.IsTrue(DeploymentPlanner.IsHighVelocityRacingTitle("Need for Speed Unbound"));
        Assert.IsFalse(DeploymentPlanner.IsHighVelocityRacingTitle("Alan Wake 2"));
        Assert.IsFalse(DeploymentPlanner.IsHighVelocityRacingTitle("Starfield"));
    }

    #endregion
}

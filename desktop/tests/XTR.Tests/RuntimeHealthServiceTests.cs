using System;
using System.IO;
using System.Linq;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class RuntimeHealthServiceTests
{
    [TestMethod]
    public void CheckHealth_DirectoryDoesNotExist_ReturnsError()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), $"xtr_nonexistent_{Guid.NewGuid():N}");
        var report = RuntimeHealthService.CheckHealth(nonExistent);

        Assert.AreEqual(RuntimeHealthState.ErrorDetected, report.State);
        Assert.IsTrue(report.Summary.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CheckHealth_ProxyMissing_ReturnsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_proxy_missing_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var report = RuntimeHealthService.CheckHealth(tempDir, "dxgi.dll");

            Assert.AreEqual(RuntimeHealthState.ErrorDetected, report.State);
            Assert.IsFalse(report.ProxyFilePresent);
            Assert.IsTrue(report.Summary.Contains("missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void CheckHealth_ProxyPresentWithoutLog_ReturnsInstalledNotYetLaunched()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_no_log_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "dxgi.dll"), "DUMMY_PROXY_DLL");

            var report = RuntimeHealthService.CheckHealth(tempDir, "dxgi.dll");

            Assert.AreEqual(RuntimeHealthState.InstalledNotYetLaunched, report.State);
            Assert.IsTrue(report.ProxyFilePresent);
            Assert.IsTrue(report.Summary.Contains("Launch the game", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void CheckHealth_HealthyLog_ReportsActiveUpscalerAndFrameGen()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_healthy_log_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "dxgi.dll"), "DUMMY_PROXY_DLL");

            var logContent = @"
[2026-10-25 12:00:01.000] [info] OptiScaler v0.9.4 initializing
[2026-10-25 12:00:01.050] [info] Hooking DirectX 12 CreateDevice... Hook confirmed.
[2026-10-25 12:00:01.100] [info] D3D12 device created successfully.
[2026-10-25 12:00:02.000] [info] DLSS upscaler initialized and active.
[2026-10-25 12:00:02.050] [info] Streamline hooked successfully: hookInterposer, hookDlssg confirmed.
[2026-10-25 12:00:02.100] [info] FrameGen Enabled: true. Selected input: dlssg, output: fsrfg.
[2026-10-25 12:00:03.000] [debug] HookFGSwapchain: swapchain hooked successfully.
[2026-10-25 12:00:03.100] [debug] flags: 0, Frametime: 16.666
";
            File.WriteAllText(Path.Combine(tempDir, "OptiScaler.log"), logContent);

            var report = RuntimeHealthService.CheckHealth(tempDir, "dxgi.dll");

            Assert.AreEqual(RuntimeHealthState.HealthyActive, report.State);
            Assert.IsTrue(report.ProxyHookConfirmed);
            Assert.IsTrue(report.StreamlineHookConfirmed);
            Assert.IsTrue(report.SwapchainHookConfirmed);
            Assert.IsTrue(report.GeneratedFramesConfirmed);
            Assert.IsTrue(report.UpscalerActive);
            Assert.AreEqual("DLSS", report.ActiveUpscaler);
            Assert.AreEqual("DX12", report.ActiveApi);
            Assert.IsTrue(report.FrameGenerationActive);
            Assert.IsTrue(report.ActiveFrameGen?.Contains("DLSS-G", StringComparison.OrdinalIgnoreCase) == true);
            Assert.IsTrue(report.Summary.Contains("Active", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void CheckHealth_LogReportsError_ReportsErrorDetected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_error_log_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "dxgi.dll"), "DUMMY_PROXY_DLL");

            var logContent = @"
[2026-10-25 12:00:01.000] [info] OptiScaler initializing...
[2026-10-25 12:00:01.050] [error] Error: Failed to hook dxgi.dll CreateSwapChain.
";
            File.WriteAllText(Path.Combine(tempDir, "OptiScaler.log"), logContent);

            var report = RuntimeHealthService.CheckHealth(tempDir, "dxgi.dll");

            Assert.AreEqual(RuntimeHealthState.ErrorDetected, report.State);
            Assert.IsTrue(report.Summary.Contains("Error", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(report.Details.Any(d => d.Contains("Failed to hook", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void CheckHealth_ProxyMissingAndIniMissing_ReportsBothAndNotOperational()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_health_both_missing_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var report = RuntimeHealthService.CheckHealth(tempDir, "dxgi.dll");

            Assert.AreEqual(RuntimeHealthState.ErrorDetected, report.State);
            Assert.IsFalse(report.ProxyFilePresent);
            Assert.IsFalse(report.IniFilePresent);
            Assert.IsFalse(report.IsRuntimeOperational);
            Assert.AreEqual("dxgi.dll", report.ExpectedProxyFileName);
            Assert.IsTrue(report.Summary.Contains("OptiScaler.ini", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void CheckHealth_ProxyPresentWithoutIni_SetsIniFilePresentFalseAndNotOperational()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_health_no_ini_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "dxgi.dll"), "DUMMY_PROXY");

            var report = RuntimeHealthService.CheckHealth(tempDir, "dxgi.dll");

            Assert.AreEqual(RuntimeHealthState.InstalledNotYetLaunched, report.State);
            Assert.IsTrue(report.ProxyFilePresent);
            Assert.IsFalse(report.IniFilePresent);
            Assert.IsFalse(report.IsRuntimeOperational);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void CheckHealth_ProxyAndIniPresent_WithoutLog_SetsIsRuntimeOperationalTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_health_op_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "dxgi.dll"), "DUMMY_PROXY");
            File.WriteAllText(Path.Combine(tempDir, "OptiScaler.ini"), "[Menu]\nOverlayMenu=true\n");

            var report = RuntimeHealthService.CheckHealth(tempDir, "dxgi.dll");

            Assert.AreEqual(RuntimeHealthState.InstalledNotYetLaunched, report.State);
            Assert.IsTrue(report.ProxyFilePresent);
            Assert.IsTrue(report.IniFilePresent);
            Assert.IsTrue(report.IsRuntimeOperational);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void CheckHealth_AlternateProxyPresent_DetectsOptiScalerUnderVersionDll()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_health_alt_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var current = AppContext.BaseDirectory;
            string? optiDll = null;
            while (!string.IsNullOrEmpty(current))
            {
                var candidate = Path.Combine(current, "desktop", "src", "Runtime", "OptiScaler", "OptiScaler.dll");
                if (File.Exists(candidate))
                {
                    optiDll = candidate;
                    break;
                }
                current = Directory.GetParent(current)?.FullName;
            }
            Assert.IsNotNull(optiDll, "Could not locate bundled OptiScaler.dll.");

            File.Copy(optiDll, Path.Combine(tempDir, "version.dll"), overwrite: true);
            File.WriteAllText(Path.Combine(tempDir, "OptiScaler.ini"), "[Menu]\nOverlayMenu=true\n");

            var report = RuntimeHealthService.CheckHealth(tempDir, "dxgi.dll");

            Assert.IsFalse(report.ProxyFilePresent);
            Assert.AreEqual("version.dll", report.DetectedAlternateProxyFileName);
            Assert.IsFalse(report.IsRuntimeOperational);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
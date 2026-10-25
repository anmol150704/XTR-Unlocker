using System;
using System.IO;
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
[2026-10-25 12:00:02.100] [info] FrameGen Enabled: true. Selected input: dlssg, output: dlssg.
";
            File.WriteAllText(Path.Combine(tempDir, "OptiScaler.log"), logContent);

            var report = RuntimeHealthService.CheckHealth(tempDir, "dxgi.dll");

            Assert.AreEqual(RuntimeHealthState.HealthyActive, report.State);
            Assert.IsTrue(report.ProxyHookConfirmed);
            Assert.IsTrue(report.UpscalerActive);
            Assert.AreEqual("DLSS", report.ActiveUpscaler);
            Assert.AreEqual("DX12", report.ActiveApi);
            Assert.IsTrue(report.FrameGenerationActive);
            Assert.AreEqual("DLSS-G native bridge", report.ActiveFrameGen);
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
}
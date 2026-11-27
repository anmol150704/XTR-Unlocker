using System.Diagnostics;
using DLSS_Swapper.Engine.Tray;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class XtrEngineTests
{
    [TestMethod]
    public void XtrTrayCompanion_TrimWorkingSet_ExecutesSafely()
    {
        // Must execute without exceptions across Windows platforms
        XtrTrayCompanion.TrimWorkingSet();

        using var currentProc = Process.GetCurrentProcess();
        currentProc.Refresh();
        Assert.IsTrue(currentProc.WorkingSet64 > 0, "Working set must remain active and valid.");
    }
}

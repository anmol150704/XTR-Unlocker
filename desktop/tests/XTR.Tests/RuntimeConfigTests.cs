using System;
using System.IO;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class RuntimeConfigTests
{
    [TestMethod]
    public async Task ApplyProfile_ModifiesOwnedKeys_PreservesUnknownKeysAndComments()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_ini_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var iniPath = Path.Combine(tempDir, "OptiScaler.ini");

            var originalIni = @"# OptiScaler Configuration File
; User custom comments should stay preserved

[Upscalers]
Dx11Upscaler=fsr22
Dx12Upscaler=xess
VulkanUpscaler=fsr21

[FrameGen]
FGInput=auto
FGOutput=auto

[CustomUserSection]
MyCustomKey=KeepThisValue
AnotherTuningValue=123
";
            await File.WriteAllTextAsync(iniPath, originalIni);

            var profile = new RuntimeIntegrationProfile
            {
                GameId = "ini-test-game",
                Api = "DX12",
                SelectedUpscaler = "dlss",
                FrameGenerationInput = "dlssg",
                FrameGenerationOutput = "fsr3"
            };

            await RuntimeConfigService.ApplyProfileAsync(tempDir, profile);

            var resultIni = await File.ReadAllTextAsync(iniPath);

            // Verify owned changes were applied
            Assert.IsTrue(resultIni.Contains("Dx12Upscaler=dlss"));
            Assert.IsTrue(resultIni.Contains("FGInput=dlssg"));
            Assert.IsTrue(resultIni.Contains("FGOutput=fsr3"));

            // Verify comments and user sections were preserved verbatim
            Assert.IsTrue(resultIni.Contains("# OptiScaler Configuration File"));
            Assert.IsTrue(resultIni.Contains("; User custom comments should stay preserved"));
            Assert.IsTrue(resultIni.Contains("[CustomUserSection]"));
            Assert.IsTrue(resultIni.Contains("MyCustomKey=KeepThisValue"));
            Assert.IsTrue(resultIni.Contains("AnotherTuningValue=123"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

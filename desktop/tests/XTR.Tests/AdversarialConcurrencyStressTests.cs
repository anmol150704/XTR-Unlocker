using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class AdversarialConcurrencyStressTests
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

        throw new DirectoryNotFoundException("Could not locate desktop/src/Runtime/OptiScaler folder.");
    }

    private static string GetOptiScalerIniPath()
    {
        return Path.Combine(GetBundledPackagePath(), "OptiScaler.ini");
    }

    /// <summary>
    /// Stress test 1: High concurrency (100 parallel tasks) setting DeploymentPlanner.OverrideFgOutput
    /// via AsyncLocal to ensure complete thread/context isolation without cross-talk.
    /// </summary>
    [TestMethod]
    public async Task StressTest_AsyncLocalFgOutputOverride_MassiveConcurrency_ZeroThreadInterference()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
        Assert.IsNotNull(pack);

        var tempDirs = new ConcurrentBag<string>();
        var errors = new ConcurrentBag<string>();

        const int TaskCount = 100;
        var backends = new[]
        {
            ("nukems", "nukems"),
            ("nukem", "nukems"),
            ("nukem9", "nukems"),
            ("xefg", "xefg"),
            ("xess", "xefg"),
            ("intel", "xefg"),
            ("fsrfg", "fsrfg"),
            ("fsr3", "fsrfg"),
            ("amd", "fsrfg"),
            ("fsr31", "fsrfg")
        };

        var tasks = Enumerable.Range(0, TaskCount).Select(async i =>
        {
            var (overrideVariant, expectedResolved) = backends[i % backends.Length];
            var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_concurrency_stress_{i}_{Guid.NewGuid():N}");
            tempDirs.Add(tempDir);
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            // Set the AsyncLocal override for this async execution context
            DeploymentPlanner.OverrideFgOutput = overrideVariant;

            // Deliberate async yields and jitter delays to force thread migration across thread pool
            await Task.Yield();
            await Task.Delay(Random.Shared.Next(1, 15));

            var plan = await DeploymentPlanner.CreatePlanAsync(
                $"game-{i}",
                $"Game {i}",
                tempDir,
                pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration);

            // Verify FGOutput inside plan matches this context's override
            var fgOutputEntry = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGOutput");
            if (fgOutputEntry == null)
            {
                errors.Add($"Task {i}: Missing FGOutput in planned modifications.");
                return;
            }

            if (fgOutputEntry.Value != expectedResolved)
            {
                errors.Add($"Task {i}: Cross-thread pollution! Expected '{expectedResolved}', got '{fgOutputEntry.Value}'. Override was '{overrideVariant}'.");
            }

            // Verify ResolveFgOutput in this context
            var resolvedLocal = DeploymentPlanner.ResolveFgOutput();
            if (resolvedLocal != expectedResolved)
            {
                errors.Add($"Task {i}: Local ResolveFgOutput mismatch! Expected '{expectedResolved}', got '{resolvedLocal}'.");
            }

            // Verify OverrideFgOutput read back in this context
            if (DeploymentPlanner.OverrideFgOutput != overrideVariant)
            {
                errors.Add($"Task {i}: OverrideFgOutput mutated unexpectedly! Expected '{overrideVariant}', got '{DeploymentPlanner.OverrideFgOutput}'.");
            }
        }).ToArray();

        try
        {
            await Task.WhenAll(tasks);
            Assert.AreEqual(0, errors.Count, $"Encountered {errors.Count} concurrency errors:\n{string.Join("\n", errors.Take(10))}");
        }
        finally
        {
            // Verify calling context has clean state
            Assert.IsNull(DeploymentPlanner.OverrideFgOutput, "Calling context OverrideFgOutput must remain null.");

            foreach (var dir in tempDirs)
            {
                if (Directory.Exists(dir))
                {
                    try { Directory.Delete(dir, recursive: true); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// Stress test 2: Hierarchical AsyncLocal context isolation.
    /// Modifying AsyncLocal in a child execution context must NEVER contaminate the parent or sibling contexts.
    /// </summary>
    [TestMethod]
    public async Task StressTest_AsyncLocalFgOutputOverride_HierarchicalIsolation()
    {
        DeploymentPlanner.OverrideFgOutput = "nukems";
        Assert.AreEqual("nukems", DeploymentPlanner.OverrideFgOutput);

        var childTasks = Enumerable.Range(0, 20).Select(async i =>
        {
            await Task.Yield();
            if (i % 2 == 0)
            {
                // Should inherit parent's value
                Assert.AreEqual("nukems", DeploymentPlanner.OverrideFgOutput, $"Child {i} should inherit nukems.");
                Assert.AreEqual("nukems", DeploymentPlanner.ResolveFgOutput());
            }
            else
            {
                // Mutate in child context
                DeploymentPlanner.OverrideFgOutput = "xefg";
                await Task.Delay(Random.Shared.Next(1, 10));
                Assert.AreEqual("xefg", DeploymentPlanner.OverrideFgOutput, $"Child {i} should see its own xefg override.");
                Assert.AreEqual("xefg", DeploymentPlanner.ResolveFgOutput());
            }
        });

        await Task.WhenAll(childTasks);

        // Parent must STILL have its original value "nukems"
        Assert.AreEqual("nukems", DeploymentPlanner.OverrideFgOutput, "Parent context was corrupted by child context!");
        DeploymentPlanner.OverrideFgOutput = null;
    }

    /// <summary>
    /// Stress test 3: Direct parameter fgBackendOverride takes precedence over AsyncLocal override
    /// across concurrent executions.
    /// </summary>
    [TestMethod]
    public async Task StressTest_FgBackendOverrideParameter_PrecedenceOverAsyncLocal_UnderConcurrency()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
        Assert.IsNotNull(pack);

        var tempDirs = new ConcurrentBag<string>();
        var errors = new ConcurrentBag<string>();

        const int TaskCount = 60;
        var tasks = Enumerable.Range(0, TaskCount).Select(async i =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_precedence_stress_{i}_{Guid.NewGuid():N}");
            tempDirs.Add(tempDir);
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            string localOverride;
            string explicitParam;
            string expected;

            if (i % 3 == 0)
            {
                localOverride = "xefg";
                explicitParam = "nukems";
                expected = "nukems";
            }
            else if (i % 3 == 1)
            {
                localOverride = "nukems";
                explicitParam = "xefg";
                expected = "xefg";
            }
            else
            {
                localOverride = "xefg";
                explicitParam = "fsrfg";
                expected = "fsrfg";
            }

            DeploymentPlanner.OverrideFgOutput = localOverride;
            await Task.Yield();

            var plan = await DeploymentPlanner.CreatePlanAsync(
                $"game-prec-{i}",
                $"Game Precedence {i}",
                tempDir,
                pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration,
                fgBackendOverride: explicitParam);

            var entry = plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput");
            if (entry.Value != expected)
            {
                errors.Add($"Task {i}: Precedence failed! Expected '{expected}' from explicit parameter '{explicitParam}', but got '{entry.Value}' (local override was '{localOverride}').");
            }
        });

        try
        {
            await Task.WhenAll(tasks);
            Assert.AreEqual(0, errors.Count, $"Precedence failures encountered:\n{string.Join("\n", errors)}");
        }
        finally
        {
            DeploymentPlanner.OverrideFgOutput = null;
            foreach (var dir in tempDirs)
            {
                if (Directory.Exists(dir))
                {
                    try { Directory.Delete(dir, recursive: true); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// Stress test 4: Validate that multi-backend dispatch fallback handles null/empty/whitespace/invalid backends
    /// gracefully without crashing.
    /// </summary>
    [TestMethod]
    [DataRow(null, "fsrfg")]
    [DataRow("", "fsrfg")]
    [DataRow("   ", "fsrfg")]
    [DataRow("\t\r\n", "fsrfg")]
    [DataRow("nukem", "nukems")]
    [DataRow("NUKEMS", "nukems")]
    [DataRow("NuKem9", "nukems")]
    [DataRow("xefg", "xefg")]
    [DataRow("XESS", "xefg")]
    [DataRow("Intel", "xefg")]
    [DataRow("fsr", "fsrfg")]
    [DataRow("FSRFG", "fsrfg")]
    [DataRow("FsR3", "fsrfg")]
    [DataRow("FSR31", "fsrfg")]
    [DataRow("AMD", "fsrfg")]
    [DataRow("custom_unknown_backend", "custom_unknown_backend")]
    [DataRow("!@#$%^&*", "!@#$%^&*")]
    public void StressTest_ResolveFgOutput_FallbackResilience(string? requestedBackend, string expectedOutput)
    {
        // 1. Direct call without static override
        DeploymentPlanner.OverrideFgOutput = null;
        var result1 = DeploymentPlanner.ResolveFgOutput(requestedBackend);
        Assert.AreEqual(expectedOutput, result1);

        // 2. Fallback via static override when requestedBackend is null
        if (requestedBackend == null)
        {
            DeploymentPlanner.OverrideFgOutput = "nukems";
            Assert.AreEqual("nukems", DeploymentPlanner.ResolveFgOutput(null));

            DeploymentPlanner.OverrideFgOutput = "xefg";
            Assert.AreEqual("xefg", DeploymentPlanner.ResolveFgOutput(null));

            DeploymentPlanner.OverrideFgOutput = "  ";
            Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput(null));

            DeploymentPlanner.OverrideFgOutput = null;
        }
    }

    /// <summary>
    /// Stress test 5: CreatePlanAsync handles null, empty, whitespace, and unknown backends without throwing.
    /// </summary>
    [TestMethod]
    [DataRow(null, "fsrfg")]
    [DataRow("", "fsrfg")]
    [DataRow("   ", "fsrfg")]
    [DataRow("invalid_backend_xyz", "invalid_backend_xyz")]
    public async Task StressTest_CreatePlanAsync_WithEdgeCaseBackends_NeverThrows(string? backend, string expectedOutput)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_fg_edge_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "edge-case-game",
                "Edge Case Title",
                tempDir,
                pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration,
                fgBackendOverride: backend);

            Assert.IsTrue(plan.CanExecute);
            var fgOutput = plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput");
            Assert.AreEqual(expectedOutput, fgOutput.Value);
            Assert.IsFalse(string.IsNullOrWhiteSpace(fgOutput.Reason), "Reason string must not be empty even for custom/unknown backend.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Stress test 6: Section-aware INI writing when duplicate key names exist across sections.
    /// Updating a key in one section MUST NOT modify or leak into another section with the same key name.
    /// </summary>
    [TestMethod]
    public void StressTest_SectionAwareIni_DuplicateKeyNamesAcrossSections_StrictIsolation()
    {
        var ini = @"
[InitFlags]
SharedKey1=initial_init1
SharedKey2=initial_init2
DisableReactiveMask=auto

[FrameGen]
SharedKey1=initial_fg1
SharedKey2=initial_fg2
DisableReactiveMask=auto

[FSR]
SharedKey1=initial_fsr1
SharedKey2=initial_fsr2
DisableReactiveMask=auto

[OptiFG]
SharedKey1=initial_opti1
SharedKey2=initial_opti2
DisableReactiveMask=auto

[Hotfix]
SharedKey1=hotfix_preserve1
SharedKey2=hotfix_preserve2
DisableReactiveMask=hotfix_preserve_mask
";

        var updates = new[]
        {
            new PlannedConfigEntry { Section = "InitFlags", Key = "SharedKey1", Value = "updated_init1" },
            new PlannedConfigEntry { Section = "InitFlags", Key = "DisableReactiveMask", Value = "false" },
            new PlannedConfigEntry { Section = "FrameGen", Key = "SharedKey1", Value = "updated_fg1" },
            new PlannedConfigEntry { Section = "FrameGen", Key = "SharedKey2", Value = "updated_fg2" },
            new PlannedConfigEntry { Section = "FSR", Key = "SharedKey1", Value = "updated_fsr1" },
            new PlannedConfigEntry { Section = "OptiFG", Key = "SharedKey2", Value = "updated_opti2" }
        };

        var mutated = RuntimeConfigService.ReplaceIniValues(ini, updates);

        // Verify target sections updated correctly
        Assert.AreEqual("updated_init1", RuntimeConfigService.ReadIniValue(mutated, "InitFlags", "SharedKey1"));
        Assert.AreEqual("initial_init2", RuntimeConfigService.ReadIniValue(mutated, "InitFlags", "SharedKey2"));
        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(mutated, "InitFlags", "DisableReactiveMask"));

        Assert.AreEqual("updated_fg1", RuntimeConfigService.ReadIniValue(mutated, "FrameGen", "SharedKey1"));
        Assert.AreEqual("updated_fg2", RuntimeConfigService.ReadIniValue(mutated, "FrameGen", "SharedKey2"));
        Assert.AreEqual("auto", RuntimeConfigService.ReadIniValue(mutated, "FrameGen", "DisableReactiveMask"));

        Assert.AreEqual("updated_fsr1", RuntimeConfigService.ReadIniValue(mutated, "FSR", "SharedKey1"));
        Assert.AreEqual("initial_fsr2", RuntimeConfigService.ReadIniValue(mutated, "FSR", "SharedKey2"));

        Assert.AreEqual("initial_opti1", RuntimeConfigService.ReadIniValue(mutated, "OptiFG", "SharedKey1"));
        Assert.AreEqual("updated_opti2", RuntimeConfigService.ReadIniValue(mutated, "OptiFG", "SharedKey2"));

        // CRITICAL CHECK: [Hotfix] must remain completely pristine and untouched
        Assert.AreEqual("hotfix_preserve1", RuntimeConfigService.ReadIniValue(mutated, "Hotfix", "SharedKey1"));
        Assert.AreEqual("hotfix_preserve2", RuntimeConfigService.ReadIniValue(mutated, "Hotfix", "SharedKey2"));
        Assert.AreEqual("hotfix_preserve_mask", RuntimeConfigService.ReadIniValue(mutated, "Hotfix", "DisableReactiveMask"));
    }

    /// <summary>
    /// Stress test 7: Real OptiScaler.ini with [Hotfix] as the final section.
    /// Adding newly introduced keys and updating existing keys across earlier sections
    /// MUST NOT append any orphaned keys after [Hotfix] or pollute [Hotfix].
    /// </summary>
    [TestMethod]
    public void StressTest_SectionAwareIni_NeverAppendsOrphanKeysAfterHotfix_WithMissingAndNewKeys()
    {
        var realIni = File.ReadAllText(GetOptiScalerIniPath());

        var modifications = new List<PlannedConfigEntry>
        {
            // 1. Existing keys in earlier sections
            new() { Section = "InitFlags", Key = "DisableReactiveMask", Value = "false" },
            new() { Section = "FrameGen", Key = "FGOutput", Value = "nukems" },
            new() { Section = "FrameGen", Key = "FGInput", Value = "dlssg" },
            new() { Section = "FrameGen", Key = "DrawUIOverFG", Value = "true" },
            new() { Section = "FSR", Key = "UseReactiveMaskForTransparency", Value = "true" },
            new() { Section = "FSR", Key = "DlssReactiveMaskBias", Value = "0.45" },
            new() { Section = "FSR", Key = "ReactiveScale", Value = "2.0" },
            new() { Section = "OptiFG", Key = "HUDFix", Value = "true" },
            new() { Section = "OptiFG", Key = "HUDFixExtended", Value = "true" },
            new() { Section = "OptiFG", Key = "MakeMVCopy", Value = "true" },
            new() { Section = "OptiFG", Key = "MakeDepthCopy", Value = "true" },

            // 2. Non-existent keys in earlier sections that must be inserted into their respective sections
            new() { Section = "InitFlags", Key = "XtrCustomInitShield", Value = "1" },
            new() { Section = "FrameGen", Key = "XtrFramePacingBuffer", Value = "2" },
            new() { Section = "FSR", Key = "XtrAntiGhostDisocclusionFlush", Value = "true" },
            new() { Section = "OptiFG", Key = "XtrDirectMemoryBarrier", Value = "true" },

            // 3. Completely new sections that must be appended cleanly with their own section headers
            new() { Section = "XtrTelemetryEngine", Key = "IpcSharedMemEnabled", Value = "true" },
            new() { Section = "XtrTelemetryEngine", Key = "HudPalette", Value = "Emerald" }
        };

        var result = RuntimeConfigService.ReplaceIniValues(realIni, modifications);

        // Verify all 17 keys can be retrieved from their designated sections
        foreach (var mod in modifications)
        {
            var value = RuntimeConfigService.ReadIniValue(result, mod.Section, mod.Key);
            Assert.AreEqual(mod.Value, value, $"Key '{mod.Key}' in section '[{mod.Section}]' mismatch.");
        }

        // Structural check: Scan line by line to verify section bounding
        var lines = result.Replace("\r\n", "\n").Split('\n');
        string? currentSection = null;
        var sectionOrder = new List<string>();
        var keysBySection = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                currentSection = trimmed[1..^1].Trim();
                sectionOrder.Add(currentSection);
                if (!keysBySection.ContainsKey(currentSection))
                    keysBySection[currentSection] = new List<string>();
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                continue;

            var sep = trimmed.IndexOf('=');
            if (sep > 0)
            {
                var k = trimmed[..sep].Trim();
                Assert.IsNotNull(currentSection, $"Found orphaned key '{k}' before any section header!");
                keysBySection[currentSection].Add(k);
            }
        }

        // Check: Newly inserted keys into earlier sections must be located inside those sections
        Assert.IsTrue(keysBySection["InitFlags"].Contains("XtrCustomInitShield", StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(keysBySection["FrameGen"].Contains("XtrFramePacingBuffer", StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(keysBySection["FSR"].Contains("XtrAntiGhostDisocclusionFlush", StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(keysBySection["OptiFG"].Contains("XtrDirectMemoryBarrier", StringComparer.OrdinalIgnoreCase));

        // Check: [Hotfix] section must NOT contain any of our earlier section keys
        var hotfixKeys = keysBySection["Hotfix"];
        var forbiddenHotfixKeys = new[]
        {
            "DisableReactiveMask", "FGOutput", "FGInput", "DrawUIOverFG",
            "UseReactiveMaskForTransparency", "DlssReactiveMaskBias", "ReactiveScale",
            "HUDFix", "HUDFixExtended", "MakeMVCopy", "MakeDepthCopy",
            "XtrCustomInitShield", "XtrFramePacingBuffer", "XtrAntiGhostDisocclusionFlush",
            "XtrDirectMemoryBarrier", "IpcSharedMemEnabled", "HudPalette"
        };

        foreach (var forbidden in forbiddenHotfixKeys)
        {
            // Note: If real baseline OptiScaler.ini happens to have DisableReactiveMask in [Hotfix],
            // its value was not updated by our modifications to InitFlags!
            if (hotfixKeys.Contains(forbidden, StringComparer.OrdinalIgnoreCase))
            {
                var hotfixVal = RuntimeConfigService.ReadIniValue(result, "Hotfix", forbidden);
                Assert.AreNotEqual("false", hotfixVal, $"Key '{forbidden}' in [Hotfix] was polluted with InitFlags value!");
            }
        }

        // Check: New section [XtrTelemetryEngine] appears cleanly with its own header
        Assert.IsTrue(keysBySection.ContainsKey("XtrTelemetryEngine"));
        Assert.IsTrue(keysBySection["XtrTelemetryEngine"].Contains("IpcSharedMemEnabled", StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(keysBySection["XtrTelemetryEngine"].Contains("HudPalette", StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Stress test 8: Extreme boundary conditions for INI rewriting.
    /// Handles empty file, whitespace-only, comment-only, and rapid repeated mutations.
    /// </summary>
    [TestMethod]
    public void StressTest_SectionAwareIni_ExtremeBoundaryCases()
    {
        // 1. Empty string
        var fromEmpty = RuntimeConfigService.ReplaceIniValues("", new[]
        {
            new PlannedConfigEntry { Section = "SecA", Key = "Key1", Value = "Val1" }
        });
        Assert.AreEqual("Val1", RuntimeConfigService.ReadIniValue(fromEmpty, "SecA", "Key1"));

        // 2. Whitespace only
        var fromWhitespace = RuntimeConfigService.ReplaceIniValues("   \r\n\t  \r\n  ", new[]
        {
            new PlannedConfigEntry { Section = "SecB", Key = "Key2", Value = "Val2" }
        });
        Assert.AreEqual("Val2", RuntimeConfigService.ReadIniValue(fromWhitespace, "SecB", "Key2"));

        // 3. Comments and non-section keys only
        var commentsOnly = @"; Only comments
# Hash comment
OrphanKey=OrphanVal
";
        var fromComments = RuntimeConfigService.ReplaceIniValues(commentsOnly, new[]
        {
            new PlannedConfigEntry { Section = "SecC", Key = "Key3", Value = "Val3" }
        });
        Assert.AreEqual("Val3", RuntimeConfigService.ReadIniValue(fromComments, "SecC", "Key3"));
        Assert.IsTrue(fromComments.Contains("; Only comments"));
        Assert.IsTrue(fromComments.Contains("# Hash comment"));

        // 4. Repeated sequential mutation (100 passes) simulating user switching settings repeatedly
        var current = "[FrameGen]\nFGOutput=fsrfg\n[Hotfix]\nFix=1\n";
        var toggle = new[] { "nukems", "xefg", "fsrfg" };
        for (var i = 0; i < 100; i++)
        {
            var target = toggle[i % toggle.Length];
            current = RuntimeConfigService.ReplaceIniValue(current, "FrameGen", "FGOutput", target);
            Assert.AreEqual(target, RuntimeConfigService.ReadIniValue(current, "FrameGen", "FGOutput"));
            Assert.AreEqual("1", RuntimeConfigService.ReadIniValue(current, "Hotfix", "Fix"));
        }

        // Verify [FrameGen] still has only 1 FGOutput key, not 100 duplicate keys
        var lines = current.Replace("\r\n", "\n").Split('\n');
        var fgOutputOccurrences = lines.Count(l => l.Trim().StartsWith("FGOutput=", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, fgOutputOccurrences, "Repeated updates must never duplicate keys!");
    }
}

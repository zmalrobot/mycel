using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Mycel.App.ViewModels;
using Mycel.Core.Geometry;
using Mycel.Core.Workflow;

namespace Mycel.App.Diagnostics;

public record ScenarioResult(
    string ScenarioName,
    int TriangleCount,
    Dictionary<string, double> StepDurationsMs,
    Dictionary<string, double> StepMaxUiGapsMs,
    double TotalTimeMs,
    double OverallMaxUiGapMs,
    bool Success,
    string? ErrorMessage
);

/// <summary>
/// Programmatically executes the end-to-end Mycel workflow at the ViewModel layer
/// while measuring wall clock time and UI thread responsiveness (gaps &gt; 100 ms).
/// </summary>
public sealed class ScenarioRunner
{
    public static async Task<ScenarioResult> RunPipelineScenarioAsync(
        MainWindowViewModel vm,
        string stlPath,
        string scenarioName = "obj_1_Assemblaggio",
        bool runAllPresets = true,
        int optIterations = 20)
    {
        var durations = new Dictionary<string, double>();
        var gaps = new Dictionary<string, double>();
        double overallMaxGap = 0;
        var totalSw = Stopwatch.StartNew();

        async Task MeasureStepAsync(string stepName, Func<Task> action)
        {
            double preGap = UiResponsivenessMonitor.Instance.MaxGapMs;
            UiResponsivenessMonitor.Instance.ResetMaxGap();
            using (UiResponsivenessMonitor.BeginOperation(stepName))
            {
                var sw = Stopwatch.StartNew();
                await action();
                sw.Stop();
                durations[stepName] = sw.Elapsed.TotalMilliseconds;
                double stepGap = UiResponsivenessMonitor.Instance.MaxGapMs;
                gaps[stepName] = stepGap;
                overallMaxGap = Math.Max(overallMaxGap, stepGap);
                string msg = $"[ScenarioRunner] Step '{stepName}' completed in {sw.Elapsed.TotalMilliseconds:F1} ms (Max UI gap: {stepGap:F1} ms)";
                Console.WriteLine(msg);
                Console.Out.Flush();
                Trace.WriteLine(msg);
                try { File.AppendAllText(Path.Combine(CrashLogger.LogDir, "scenario.log"), $"[{DateTime.UtcNow:O}] {msg}\n"); } catch { }
            }
        }

        try
        {
            if (!File.Exists(stlPath))
            {
                throw new FileNotFoundException($"Scenario STL file not found: {stlPath}");
            }

            // 1. Open STL
            await MeasureStepAsync("1. Open STL", async () =>
            {
                await vm.LoadStlFileAsync(stlPath, LengthUnit.Millimeter);
            });

            var mesh = vm.Workflow.State.Mesh ?? throw new InvalidOperationException("Mesh was not loaded.");
            int triCount = mesh.TriangleCount;

            // 2. Auto-select Load patch (normal.Z > 0.99 and centroid z within 0.5mm of top)
            // and Support patch (normal.Z < -0.99 near bottom)
            await MeasureStepAsync("2. Region Selection (Load & Support)", async () =>
            {
                float maxZ = mesh.BBox.Max.Z;
                float minZ = mesh.BBox.Min.Z;

                List<int> loadTris = new();
                List<int> suppTris = new();

                var normals = mesh.TriangleNormals.Span;

                for (int i = 0; i < mesh.TriangleCount; i++)
                {
                    mesh.GetTriangle(i, out Vector3 v0, out Vector3 v1, out Vector3 v2);
                    Vector3 n = normals[i];
                    Vector3 c = (v0 + v1 + v2) / 3.0f;

                    if (n.Z > 0.99f && c.Z >= maxZ - 0.5f)
                    {
                        loadTris.Add(i);
                    }
                    else if (n.Z < -0.99f && c.Z <= minZ + 0.5f)
                    {
                        suppTris.Add(i);
                    }
                }

                // Add Load
                vm.AddLoad();
                var loadItem = vm.Loads.Last();
                loadItem.UpdateTriangles(loadTris.ToArray());

                // Add Support
                vm.AddSupport();
                var suppItem = vm.Supports.Last();
                suppItem.UpdateTriangles(suppTris.ToArray());

                Trace.WriteLine($"[ScenarioRunner] Auto-selected {loadTris.Count} Load triangles (Z top) and {suppTris.Count} Support triangles (Z bottom).");
                await Task.Yield();
            });

            // 3. Choose Material (PLA)
            await MeasureStepAsync("3. Material Selection", async () =>
            {
                vm.SelectedMaterialPreset = MaterialPresetKind.Pla;
                await Task.Yield();
            });

            // 4. Prepare at presets
            if (runAllPresets)
            {
                await MeasureStepAsync("4a. Prepare (Draft)", async () =>
                {
                    vm.SelectedResolutionPreset = ResolutionPreset.Draft;
                    await vm.PrepareModelAsync();
                });

                await MeasureStepAsync("4b. Prepare (Fine)", async () =>
                {
                    vm.SelectedResolutionPreset = ResolutionPreset.Fine;
                    await vm.PrepareModelAsync();
                });

                await MeasureStepAsync("4c. Prepare (Standard/Balanced)", async () =>
                {
                    vm.SelectedResolutionPreset = ResolutionPreset.Standard;
                    await vm.PrepareModelAsync();
                });

                if (vm.ValidationIssues.Count > 0)
                {
                    foreach (var issue in vm.ValidationIssues)
                    {
                        string msg = $"[ScenarioRunner VALIDATION] {issue.Severity}: {issue.Message}";
                        Console.WriteLine(msg);
                        try { File.AppendAllText(Path.Combine(CrashLogger.LogDir, "scenario.log"), $"[{DateTime.UtcNow:O}] {msg}\n"); } catch { }
                    }
                }
            }
            else
            {
                await MeasureStepAsync("4. Prepare (Standard)", async () =>
                {
                    vm.SelectedResolutionPreset = ResolutionPreset.Standard;
                    await vm.PrepareModelAsync();
                });
            }

            // 5. Static Analysis
            await MeasureStepAsync("5. Static Analysis", async () =>
            {
                await vm.RunStaticAnalysisAsync();
                if (!vm.HasAnalysisResult)
                {
                    string msg = "[ScenarioRunner] Static analysis did not produce result!";
                    Console.WriteLine(msg);
                    try { File.AppendAllText(Path.Combine(CrashLogger.LogDir, "scenario.log"), $"[{DateTime.UtcNow:O}] {msg}\n"); } catch { }
                }
            });

            // 6. 20 Optimisation Iterations
            await MeasureStepAsync($"6. Optimisation ({optIterations} iters)", async () =>
            {
                var prep = vm.Workflow.State.PreparedModel;
                string debugPrep = $"PreparedModel null? {prep == null}, IsOutdated? {prep?.IsOutdated}, HasErrors? {vm.HasValidationErrors}, IsOptimising? {vm.IsOptimising}";
                Console.WriteLine($"[ScenarioRunner DEBUG] {debugPrep}");
                try { File.AppendAllText(Path.Combine(CrashLogger.LogDir, "scenario.log"), $"[{DateTime.UtcNow:O}] [ScenarioRunner DEBUG] {debugPrep}\n"); } catch { }

                if (!vm.CanStartOptimisation)
                {
                    string msg = $"[ScenarioRunner] Cannot start optimisation! Reason: {vm.StartOptimisationBlockingReason}";
                    Console.WriteLine(msg);
                    try { File.AppendAllText(Path.Combine(CrashLogger.LogDir, "scenario.log"), $"[{DateTime.UtcNow:O}] {msg}\n"); } catch { }
                }
                vm.OptMaxIterations = optIterations;
                await vm.StartOptimisationAsync();
            });

            // 7. Verify
            await MeasureStepAsync("7. Verify Design", async () =>
            {
                await vm.VerifyDesignAsync();
            });

            // 8. Export to Temp Folder
            string exportPath = Path.Combine(Path.GetTempPath(), $"scenario_export_{DateTime.UtcNow.Ticks}.stl");
            await MeasureStepAsync("8. Export STL", async () =>
            {
                await vm.ExportCurrentIterationStlAsync(exportPath);
            });

            if (File.Exists(exportPath))
            {
                try { File.Delete(exportPath); } catch { }
            }

            totalSw.Stop();
            return new ScenarioResult(
                scenarioName,
                triCount,
                durations,
                gaps,
                totalSw.Elapsed.TotalMilliseconds,
                overallMaxGap,
                true,
                null
            );
        }
        catch (Exception ex)
        {
            totalSw.Stop();
            Console.WriteLine($"[ScenarioRunner EXCEPTION] {ex}");
            Console.Out.Flush();
            try { File.AppendAllText(Path.Combine(CrashLogger.LogDir, "scenario.log"), $"[{DateTime.UtcNow:O}] [ScenarioRunner EXCEPTION] {ex}\n"); } catch { }
            return new ScenarioResult(
                scenarioName,
                vm.Workflow.State.Mesh?.TriangleCount ?? 0,
                durations,
                gaps,
                totalSw.Elapsed.TotalMilliseconds,
                overallMaxGap,
                false,
                ex.Message
            );
        }
    }
}

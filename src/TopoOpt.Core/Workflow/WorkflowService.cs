using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using TopoOpt.Core.Diagnostics;
using TopoOpt.Core.Fem;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Optimization;
using TopoOpt.Core.Persistence;
using TopoOpt.Core.PostProcessing;
using TopoOpt.Core.Voxelization;

namespace TopoOpt.Core.Workflow;

/// <summary>
/// UI-free workflow service coordinating:
/// Load STL -> Manage Selections (Loads, Supports, Preserve) -> Prepare (voxelize, map patches, validate) -> Run Static FEA.
/// Enforces dirty-tracking invalidation cascades.
/// </summary>
public sealed class WorkflowService
{
    public ProjectState State { get; } = new();

    public void LoadStl(string filePath, LengthUnit unit = LengthUnit.Millimeter)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("STL file not found.", filePath);

        IndexedMesh mesh;
        MeshReport report;
        using (var stream = File.OpenRead(filePath))
        {
            (mesh, report) = StlReader.Read(stream);
        }
        string sha = ProjectSerializer.ComputeSha256(filePath);

        State.Clear();
        State.Mesh = mesh;
        State.MeshReport = report;
        State.FilePath = filePath;
        State.StlSha256 = sha;
        State.Unit = unit;
        State.NotifySelectionChanged();
    }

    public void LoadMesh(IndexedMesh mesh, MeshReport? report = null, string? filePath = null, LengthUnit unit = LengthUnit.Millimeter)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        State.Clear();
        State.Mesh = mesh;
        State.MeshReport = report;
        State.FilePath = filePath;
        State.Unit = unit;
        State.NotifySelectionChanged();
    }

    public void RescaleMesh(float scaleFactor, LengthUnit newUnit)
    {
        if (State.Mesh == null || scaleFactor <= 0f || Math.Abs(scaleFactor - 1.0f) < 1e-6f)
            return;

        var oldPos = State.Mesh.Positions.Span;
        Vector3[] newPos = new Vector3[oldPos.Length];
        for (int i = 0; i < oldPos.Length; i++)
        {
            newPos[i] = oldPos[i] * scaleFactor;
        }

        var scaledMesh = new IndexedMesh(newPos, State.Mesh.Indices.ToArray());
        State.Mesh = scaledMesh;
        State.Unit = newUnit;

        // Update geometric metrics of existing selections
        foreach (var l in State.Loads) l.UpdateGeometryMetrics(scaledMesh);
        foreach (var s in State.Supports) s.UpdateGeometryMetrics(scaledMesh);
        foreach (var p in State.PreserveRegions) p.UpdateGeometryMetrics(scaledMesh);

        State.NotifySelectionChanged();
    }

    public LoadRegion AddLoad(
        string name,
        int[] triangles,
        float magnitude,
        LoadDirectionMode directionMode = LoadDirectionMode.SurfaceNormalInward,
        Vector3? customDir = null)
    {
        var load = new LoadRegion
        {
            Name = name,
            Triangles = triangles ?? Array.Empty<int>(),
            MagnitudeNewtons = magnitude,
            DirectionMode = directionMode,
            CustomDirection = customDir ?? -Vector3.UnitZ
        };

        if (State.Mesh != null)
        {
            load.UpdateGeometryMetrics(State.Mesh);
        }

        State.Loads.Add(load);
        State.NotifySelectionChanged();
        return load;
    }

    public void RemoveLoad(Guid id)
    {
        int removed = State.Loads.RemoveAll(l => l.Id == id);
        if (removed > 0)
        {
            State.NotifySelectionChanged();
        }
    }

    public SupportRegion AddSupport(
        string name,
        int[] triangles,
        bool fixX = true,
        bool fixY = true,
        bool fixZ = true)
    {
        var support = new SupportRegion
        {
            Name = name,
            Triangles = triangles ?? Array.Empty<int>(),
            FixX = fixX,
            FixY = fixY,
            FixZ = fixZ
        };

        if (State.Mesh != null)
        {
            support.UpdateGeometryMetrics(State.Mesh);
        }

        State.Supports.Add(support);
        State.NotifySelectionChanged();
        return support;
    }

    public void RemoveSupport(Guid id)
    {
        int removed = State.Supports.RemoveAll(s => s.Id == id);
        if (removed > 0)
        {
            State.NotifySelectionChanged();
        }
    }

    public PreserveRegion AddPreserve(string name, int[] triangles)
    {
        var preserve = new PreserveRegion
        {
            Name = name,
            Triangles = triangles ?? Array.Empty<int>()
        };

        if (State.Mesh != null)
        {
            preserve.UpdateGeometryMetrics(State.Mesh);
        }

        State.PreserveRegions.Add(preserve);
        State.NotifySelectionChanged();
        return preserve;
    }

    public void RemovePreserve(Guid id)
    {
        int removed = State.PreserveRegions.RemoveAll(p => p.Id == id);
        if (removed > 0)
        {
            State.NotifySelectionChanged();
        }
    }

    public void SetMaterial(MaterialDefinition material)
    {
        State.Material = material ?? throw new ArgumentNullException(nameof(material));
        State.NotifyMaterialChanged();
    }

    public void SetResolution(ResolutionConfig resolution)
    {
        State.Resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
        State.NotifyResolutionChanged();
    }

    public async Task<PreparedModel> PrepareAsync(
        CancellationToken ct = default,
        IProgress<double>? progress = null)
    {
        var mesh = State.Mesh ?? throw new InvalidOperationException("Cannot prepare model: No mesh loaded.");

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(0.05);

            float h = State.Resolution.ExplicitH > 0
                ? State.Resolution.ExplicitH
                : ResolutionConfig.ComputeVoxelSize(mesh, State.Resolution.TargetSolidElements);

            var grid = GridSpec.FromVoxelSize(mesh, h, padding: 1);

            // Memory guard check before allocating voxel grid and structures
            long estPrepareBytes = MemoryGuard.EstimatePrepareBytes((long)grid.Nx * grid.Ny * grid.Nz, mesh.TriangleCount);
            var assessment = MemoryGuard.Assess(estPrepareBytes);
            if (assessment.Level == MemoryGuardLevel.Refused)
            {
                throw new InvalidOperationException(assessment.Message);
            }

            progress?.Report(0.15);

            var (voxelModel, _) = Voxeliser.Voxelise(mesh, grid, cancellationToken: ct);
            progress?.Report(0.50);

            // Build patches for loads & supports
            var patchList = new List<(Guid Id, SelectionKind Kind, SurfacePatch Patch)>();
            var loadPatches = new List<SurfacePatch>();
            var supportPatches = new List<SurfacePatch>();
            var allBcNodes = new HashSet<int>();

            foreach (var load in State.Loads)
            {
                var patch = SurfacePatchBuilder.Build(voxelModel, mesh, load.Triangles, tau: 1.0);
                loadPatches.Add(patch);
                patchList.Add((load.Id, SelectionKind.Load, patch));
                foreach (int n in patch.Nodes) allBcNodes.Add(n);
            }

            foreach (var supp in State.Supports)
            {
                var patch = SurfacePatchBuilder.Build(voxelModel, mesh, supp.Triangles, tau: 1.0);
                supportPatches.Add(patch);
                patchList.Add((supp.Id, SelectionKind.Support, patch));
                foreach (int n in patch.Nodes) allBcNodes.Add(n);
            }

            foreach (var pres in State.PreserveRegions)
            {
                var patch = SurfacePatchBuilder.Build(voxelModel, mesh, pres.Triangles, tau: 1.0);
                patchList.Add((pres.Id, SelectionKind.Preserve, patch));
                foreach (int n in patch.Nodes) allBcNodes.Add(n);
            }

            progress?.Report(0.70);

            // Validate model setup
            var issues = ModelValidator.ValidatePatches(voxelModel, mesh, patchList);

            // Build protective passive shell (default 2h band)
            byte[] passiveShell = PassiveRegion.Build(grid, null, allBcNodes, radiusInVoxels: 2);
            progress?.Report(0.85);

            // Assemble FeModel
            var mat = new FeMaterial(State.Material.EffectiveE, State.Material.Nu, 1e-6);

            // Fixed DOFs from supports
            var fixedDofsList = new List<int>();
            for (int i = 0; i < State.Supports.Count; i++)
            {
                var s = State.Supports[i];
                var patch = supportPatches[i];
                int[] dofs = FeModel.FixDofs(patch, s.FixX, s.FixY, s.FixZ);
                fixedDofsList.AddRange(dofs);
            }
            int[] fixedDofs = fixedDofsList.Distinct().OrderBy(d => d).ToArray();

            // Load cases
            var loadCases = new List<LoadCase>();
            for (int i = 0; i < State.Loads.Count; i++)
            {
                var l = State.Loads[i];
                var patch = loadPatches[i];
                Vector3 force = l.ComputeTotalForce();
                loadCases.Add(new LoadCase(patch, force));
            }

            var feModel = new FeModel(grid, voxelModel.Domain, mat, fixedDofs, loadCases);

            var prepared = new PreparedModel(
                feModel,
                voxelModel,
                loadPatches.ToArray(),
                supportPatches.ToArray(),
                passiveShell,
                issues);

            State.PreparedModel = prepared;
            State.PreparedModel.IsOutdated = false;

            progress?.Report(1.0);
            return prepared;
        }, ct);
    }

    public async Task<WorkflowAnalysisResult> RunStaticAnalysisAsync(
        CancellationToken ct = default,
        IProgress<double>? progress = null)
    {
        var prep = State.PreparedModel ?? throw new InvalidOperationException("Model must be prepared before running static analysis.");
        var mesh = State.Mesh ?? throw new InvalidOperationException("No mesh loaded.");

        // Check if there are critical errors
        if (prep.ValidationIssues.Any(i => i.Severity == ValidationSeverity.Error))
        {
            string errList = string.Join("; ", prep.ValidationIssues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.Message));
            throw new InvalidOperationException($"Cannot run static analysis due to critical validation errors: {errList}");
        }

        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            ct.ThrowIfCancellationRequested();
            progress?.Report(0.10);

            // Sync latest material values into FeModel
            var mat = new FeMaterial(State.Material.EffectiveE, State.Material.Nu, 1e-6);
            var feModel = new FeModel(
                prep.FeModel.Grid,
                prep.FeModel.Domain,
                mat,
                prep.FeModel.FixedDofs,
                prep.FeModel.Loads);

            var solverOptions = new StaticAnalysisOptions(
                Tolerance: 1e-6,
                MaxIterations: 10000,
                ThrowOnNonConvergence: true);

            var feaResult = StaticAnalysis.Run(feModel, solverOptions);
            progress?.Report(0.70);

            // Compute nodal averaged von Mises stress
            double[] nodalVm = StaticAnalysis.AverageElementToNode(feModel, feaResult.VonMisesPerElement);

            // Compute nodal displacement magnitudes
            double[] nodalDisp = new double[feModel.TotalNodes];
            double[] u = feaResult.U;
            for (int node = 0; node < feModel.TotalNodes; node++)
            {
                if (feModel.ActiveNodes[node])
                {
                    double ux = u[3 * node + 0];
                    double uy = u[3 * node + 1];
                    double uz = u[3 * node + 2];
                    nodalDisp[node] = Math.Sqrt(ux * ux + uy * uy + uz * uz);
                }
            }

            progress?.Report(0.85);

            // Sample onto original STL vertices
            int vertexCount = mesh.VertexCount;
            float[] vertexVm = new float[vertexCount];
            float[] vertexDisp = new float[vertexCount];

            double minVm = double.MaxValue;
            double maxVm = 0.0;
            double minDisp = double.MaxValue;
            double maxDisp = 0.0;

            var activeVmValues = new List<double>(vertexCount);
            var meshPos = mesh.Positions.Span;

            for (int v = 0; v < vertexCount; v++)
            {
                Vector3 pt = meshPos[v];
                double vm = StaticAnalysis.SampleNodalScalar(feModel, nodalVm, pt);
                double disp = StaticAnalysis.SampleNodalScalar(feModel, nodalDisp, pt);

                vertexVm[v] = (float)vm;
                vertexDisp[v] = (float)disp;

                if (vm < minVm) minVm = vm;
                if (vm > maxVm) maxVm = vm;
                if (disp < minDisp) minDisp = disp;
                if (disp > maxDisp) maxDisp = disp;

                if (vm > 1e-6)
                {
                    activeVmValues.Add(vm);
                }
            }

            if (minVm == double.MaxValue) minVm = 0.0;
            if (minDisp == double.MaxValue) minDisp = 0.0;

            // 99th-percentile von Mises calculation
            double p99Vm = maxVm;
            if (activeVmValues.Count > 10)
            {
                activeVmValues.Sort();
                int idx99 = (int)Math.Floor(activeVmValues.Count * 0.99);
                p99Vm = activeVmValues[Math.Clamp(idx99, 0, activeVmValues.Count - 1)];
            }

            // Safety factor calculation
            double sfMax = State.Material.ComputeSafetyFactor(maxVm);
            double sfP99 = State.Material.ComputeSafetyFactor(p99Vm);

            sw.Stop();
            progress?.Report(1.0);

            var result = new WorkflowAnalysisResult
            {
                Compliance = feaResult.Compliance,
                MaxDisplacement = feaResult.MaxDisplacement,
                MaxVonMises = maxVm,
                P99VonMises = p99Vm,
                SafetyFactor = sfMax,
                SafetyFactorP99 = sfP99,
                VertexVonMises = vertexVm,
                VertexDisplacements = vertexDisp,
                MinVonMises = minVm,
                MinDisplacement = minDisp,
                SolverIterations = feaResult.SolverInfo.Iterations,
                SolverResidual = feaResult.SolverInfo.FinalResidual,
                ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds,
                IsOutdated = false
            };

            State.AnalysisResult = result;
            return result;
        }, ct);
    }

    public MemoryAssessment AssessRunMemory(OptimizerOptions options)
    {
        var prep = State.PreparedModel ?? throw new InvalidOperationException("Model must be prepared first.");
        long solidElements = prep.FeModel.ActiveElementCount;
        long nodes = prep.FeModel.TotalNodes;
        long bytes = MemoryGuard.EstimateRunBytes(solidElements, nodes, options.MaxIterations);
        return MemoryGuard.Assess(bytes);
    }

    public RunMetadata CreateRunMetadata(OptimizerOptions options, int keepEveryN = 1)
    {
        var prep = State.PreparedModel ?? throw new InvalidOperationException("Model must be prepared first.");
        var feModel = prep.FeModel;

        var loadsDto = new List<CompactLoadNodeSetDto>(feModel.Loads.Count);
        for (int i = 0; i < feModel.Loads.Count; i++)
        {
            var l = feModel.Loads[i];
            string name = i < State.Loads.Count ? State.Loads[i].Name : $"Load {i + 1}";
            loadsDto.Add(new CompactLoadNodeSetDto(
                name,
                l.Patch.Nodes,
                l.Patch.NodeArea,
                l.TotalForce.X,
                l.TotalForce.Y,
                l.TotalForce.Z));
        }

        string runName = !string.IsNullOrWhiteSpace(State.FilePath)
            ? Path.GetFileNameWithoutExtension(State.FilePath)
            : "model";

        return new RunMetadata
        {
            FormatVersion = 1,
            AppVersion = "1.0.0",
            RunName = runName,
            InputStlSha256 = State.StlSha256 ?? string.Empty,
            Grid = GridSpecDto.FromGrid(feModel.Grid),
            Material = State.Material,
            Options = new OptimizerOptionsDto(
                options.VolumeFraction,
                options.FilterRadiusMm,
                options.Penal,
                options.MoveLimit,
                options.MaxIterations,
                feModel.Material.EminRatio,
                options.Continuation,
                options.EnableHeaviside,
                options.ProtectBoundaryHinges,
                Math.Max(1, keepEveryN)),
            NodeSets = new CompactRunNodeSetsDto(feModel.FixedDofs, loadsDto),
            Status = RunStatus.Running,
            StartedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            OriginalSolidCompliance = State.AnalysisResult?.Compliance ?? 0.0
        };
    }

    public async Task<VerificationResult> VerifyIterationAsync(
        int iteration,
        float[] physicalDensity,
        float threshold = 0.5f,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(physicalDensity);
        var prep = State.PreparedModel ?? throw new InvalidOperationException("Model must be prepared before verification.");

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var baseFe = prep.FeModel;
            int totalElements = baseFe.TotalElements;

            // 1. Ensure we have baseline original solid compliance (rho = 1 everywhere in Domain)
            double originalCompliance = State.AnalysisResult is { IsOutdated: false } ar && ar.Compliance > 0.0
                ? ar.Compliance
                : ComputeBaselineSolidCompliance(baseFe);

            // 2. Threshold physical density: rho >= threshold -> 1.0 (solid), else 0.0 (void evaluated with Emin)
            double[] binaryDensity = new double[totalElements];
            int solidCount = 0;
            int domainCount = baseFe.ActiveElementCount;
            int[] activeElements = baseFe.ActiveElements;

            for (int i = 0; i < domainCount; i++)
            {
                int e = activeElements[i];
                if (e < physicalDensity.Length && physicalDensity[e] >= threshold)
                {
                    binaryDensity[e] = 1.0;
                    solidCount++;
                }
                else
                {
                    binaryDensity[e] = 0.0;
                }
            }

            // Ensure boundary nodes stay connected so thresholded solve is well-posed
            if (prep.PassiveShell != null)
            {
                for (int i = 0; i < domainCount; i++)
                {
                    int e = activeElements[i];
                    if (e < prep.PassiveShell.Length && prep.PassiveShell[e] != 0 && binaryDensity[e] < 1.0)
                    {
                        binaryDensity[e] = 1.0;
                        solidCount++;
                    }
                }
            }

            var mat = new FeMaterial(State.Material.EffectiveE, State.Material.Nu, 1e-6);
            var verifyModel = new FeModel(
                baseFe.Grid,
                baseFe.Domain,
                mat,
                baseFe.FixedDofs,
                baseFe.Loads,
                binaryDensity,
                penal: 1.0);

            var feaResult = StaticAnalysis.Run(verifyModel, new StaticAnalysisOptions(
                Tolerance: 1e-6,
                MaxIterations: 10000,
                ThrowOnNonConvergence: true));

            ct.ThrowIfCancellationRequested();

            // 3. Extract iso-surface mesh for the verified iteration
            IndexedMesh isoMesh = SurfaceNetsExtractor.Extract(baseFe.Grid, physicalDensity, threshold, ct);

            // 4. Compute nodal von Mises (averaging only over solid thresholded elements) and nodal displacements
            double[] solidElemVm = new double[totalElements];
            for (int i = 0; i < domainCount; i++)
            {
                int e = activeElements[i];
                if (binaryDensity[e] >= 0.5)
                {
                    solidElemVm[e] = feaResult.VonMisesPerElement[e];
                }
            }

            double[] nodalVm = StaticAnalysis.AverageElementToNode(verifyModel, solidElemVm);
            double[] nodalDisp = new double[verifyModel.TotalNodes];
            double[] u = feaResult.U;
            for (int n = 0; n < verifyModel.TotalNodes; n++)
            {
                if (verifyModel.ActiveNodes[n])
                {
                    double ux = u[3 * n + 0];
                    double uy = u[3 * n + 1];
                    double uz = u[3 * n + 2];
                    nodalDisp[n] = Math.Sqrt(ux * ux + uy * uz * 0 + uy * uy + uz * uz);
                }
            }

            // 5. Sample nodal fields at the iso-surface mesh vertices
            int vCount = isoMesh.VertexCount;
            float[] vertexVm = new float[vCount];
            float[] vertexDisp = new float[vCount];
            ReadOnlySpan<Vector3> pos = isoMesh.Positions.Span;

            double minVm = double.MaxValue;
            double maxVm = 0.0;
            double minDisp = double.MaxValue;
            double maxDisp = 0.0;
            var vmSamples = new List<double>(vCount);

            for (int v = 0; v < vCount; v++)
            {
                Vector3 pt = pos[v];
                double vm = StaticAnalysis.SampleNodalScalar(verifyModel, nodalVm, pt);
                double disp = StaticAnalysis.SampleNodalScalar(verifyModel, nodalDisp, pt);

                vertexVm[v] = (float)vm;
                vertexDisp[v] = (float)disp;

                if (vm < minVm) minVm = vm;
                if (vm > maxVm) maxVm = vm;
                if (disp < minDisp) minDisp = disp;
                if (disp > maxDisp) maxDisp = disp;

                if (vm > 1e-6) vmSamples.Add(vm);
            }

            // Also include solid element centroid stresses so maxVm reflects interior peaks
            for (int i = 0; i < domainCount; i++)
            {
                int e = activeElements[i];
                if (binaryDensity[e] >= 0.5)
                {
                    double evm = solidElemVm[e];
                    if (evm > maxVm) maxVm = evm;
                    if (evm > 1e-6) vmSamples.Add(evm);
                }
            }

            if (minVm == double.MaxValue) minVm = 0.0;
            if (minDisp == double.MaxValue) minDisp = 0.0;

            double p99Vm = maxVm;
            if (vmSamples.Count > 10)
            {
                vmSamples.Sort();
                int idx99 = (int)Math.Floor(vmSamples.Count * 0.99);
                p99Vm = vmSamples[Math.Clamp(idx99, 0, vmSamples.Count - 1)];
            }

            double materialSavedPct = domainCount > 0
                ? Math.Clamp((1.0 - (double)solidCount / domainCount) * 100.0, 0.0, 100.0)
                : 0.0;

            double compRatio = originalCompliance > 1e-12
                ? feaResult.Compliance / originalCompliance
                : 1.0;

            return new VerificationResult
            {
                Iteration = iteration,
                Threshold = threshold,
                IsoMesh = isoMesh,
                VertexVonMises = vertexVm,
                VertexDisplacements = vertexDisp,
                MaxVonMises = maxVm,
                MinVonMises = minVm,
                P99VonMises = p99Vm,
                MaxDisplacement = Math.Max(maxDisp, feaResult.MaxDisplacement),
                MinDisplacement = minDisp,
                Compliance = feaResult.Compliance,
                OriginalSolidCompliance = originalCompliance,
                ComplianceRatio = compRatio,
                MaterialSavedPercent = materialSavedPct,
                SafetyFactorMax = State.Material.ComputeSafetyFactor(maxVm),
                SafetyFactorP99 = State.Material.ComputeSafetyFactor(p99Vm),
                SolidElementCount = solidCount,
                DomainElementCount = domainCount
            };
        }, ct);
    }

    public static async Task<(IndexedMesh ExportedMesh, IndexedMesh ReReadMesh, MeshReport ReReadReport, double ReReadVolume)> ExportIterationStlAsync(
        string filePath,
        GridSpec grid,
        int iteration,
        float[] physicalDensity,
        float threshold = 0.5f,
        int smoothingIterations = 3,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(physicalDensity);

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            IndexedMesh rawMesh = SurfaceNetsExtractor.Extract(grid, physicalDensity, threshold, ct);

            int smoothIters = Math.Clamp(smoothingIterations, 0, 10);
            IndexedMesh finalMesh = smoothIters > 0
                ? TaubinSmoother.Smooth(rawMesh, smoothIters, lambda: 0.50f, mu: -0.53f, ct: ct)
                : rawMesh;

            string header = string.Create(
                CultureInfo.InvariantCulture,
                $"TopoOpt it={iteration} th={threshold:0.##}");

            string fullPath = Path.GetFullPath(filePath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmpPath = fullPath + ".tmp";
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                StlWriter.WriteBinary(fs, finalMesh, header);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmpPath, fullPath, overwrite: true);

            // Re-read with StlReader to verify watertightness and volume
            using var readStream = File.OpenRead(fullPath);
            var (reReadMesh, report) = StlReader.Read(readStream);
            return (finalMesh, reReadMesh, report, Math.Abs(reReadMesh.SignedVolume));
        }, ct);
    }

    private double ComputeBaselineSolidCompliance(FeModel baseFe)
    {
        var mat = new FeMaterial(State.Material.EffectiveE, State.Material.Nu, 1e-6);
        var solidModel = new FeModel(
            baseFe.Grid,
            baseFe.Domain,
            mat,
            baseFe.FixedDofs,
            baseFe.Loads);

        var res = StaticAnalysis.Run(solidModel, new StaticAnalysisOptions(
            Tolerance: 1e-6,
            MaxIterations: 10000,
            ThrowOnNonConvergence: false));
        return res.Compliance;
    }
}

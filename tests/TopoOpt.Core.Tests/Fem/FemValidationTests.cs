using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using TopoOpt.Core.Fem;
using TopoOpt.Core.Voxelization;
using Xunit;

namespace TopoOpt.Core.Tests.Fem;

public class FemValidationTests
{
    private static (FeModel Model, SurfacePatch TipPatch) CreateBarModel(
        double length, double width, double height, double h, double e0, double nu)
    {
        int nx = (int)Math.Round(length / h);
        int ny = (int)Math.Round(width / h);
        int nz = (int)Math.Round(height / h);

        var grid = new GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
        byte[] domain = new byte[nx * ny * nz];
        Array.Fill(domain, (byte)1);

        var mat = new FeMaterial(e0, nu);

        // Fixed face at x = 0 (all nodes with ix = 0)
        List<int> fixedNodes = new();
        for (int k = 0; k <= nz; k++)
        {
            for (int j = 0; j <= ny; j++)
            {
                fixedNodes.Add((int)grid.NodeIndex(0, j, k));
            }
        }
        int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

        // Tip patch at x = length (nodes with ix = nx)
        List<int> tipNodes = new();
        List<double> tipAreas = new();
        for (int k = 0; k <= nz; k++)
        {
            for (int j = 0; j <= ny; j++)
            {
                tipNodes.Add((int)grid.NodeIndex(nx, j, k));
                double dy = (j == 0 || j == ny) ? 0.5 * h : h;
                double dz = (k == 0 || k == nz) ? 0.5 * h : h;
                tipAreas.Add(dy * dz);
            }
        }

        var tipPatch = new SurfacePatch(
            Array.Empty<VoxelFace>(),
            tipNodes.ToArray(),
            tipAreas.ToArray(),
            (new Vector3((float)length, 0, 0), new Vector3((float)length, (float)width, (float)height)),
            true);

        var model = new FeModel(grid, domain, mat, fixedDofs);
        return (model, tipPatch);
    }

    [Fact]
    public void V1_AxialBar_Nu0_AnalyticDisplacementAndStress()
    {
        double L = 100.0, b = 10.0, d = 10.0, h = 2.5;
        double E = 3000.0, nu = 0.0, P = 1000.0;
        double A = b * d;

        var (model, tipPatch) = CreateBarModel(L, b, d, h, E, nu);
        model.Loads.Add(new LoadCase(tipPatch, new Vector3((float)P, 0, 0)));

        var options = new StaticAnalysisOptions(Tolerance: 1e-10, MaxIterations: 5000);
        var result = StaticAnalysis.Run(model, options);

        Assert.True(result.SolverInfo.Converged, "Solver did not converge.");

        // Analytic tip displacement = P*L / (A*E)
        double expectedTipU = (P * L) / (A * E);

        // Check tip nodes displacement in X
        foreach (int node in tipPatch.Nodes)
        {
            double ux = result.U[3 * node + 0];
            double err = Math.Abs(ux - expectedTipU);
            Assert.True(err < 1e-6, $"Tip displacement error {err} exceeds 1e-6 (ux={ux}, expected={expectedTipU})");
        }

        // Check von Mises stress away from the ends (ix between 5 and 35)
        // Analytic stress = P/A = 10.0 MPa
        double expectedStress = P / A;
        for (int iz = 0; iz < model.Grid.Nz; iz++)
        {
            for (int iy = 0; iy < model.Grid.Ny; iy++)
            {
                for (int ix = 5; ix <= 35; ix++)
                {
                    int e = model.Grid.ElementIndex(ix, iy, iz);
                    double vm = result.VonMisesPerElement[e];
                    double err = Math.Abs(vm - expectedStress);
                    Assert.True(err < 1e-6, $"Stress at elem ({ix},{iy},{iz}) err {err} exceeds 1e-6 (vm={vm}, expected={expectedStress})");
                }
            }
        }
    }

    [Fact]
    public void V2_AxialBar_Nu03_MinimalRestraints_AnalyticDisplacement()
    {
        double L = 100.0, b = 10.0, d = 10.0, h = 2.5;
        double E = 3000.0, nu = 0.3, P = 1000.0;
        double A = b * d;

        int nx = (int)Math.Round(L / h);
        int ny = (int)Math.Round(b / h);
        int nz = (int)Math.Round(d / h);

        var grid = new GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
        byte[] domain = new byte[nx * ny * nz];
        Array.Fill(domain, (byte)1);

        var mat = new FeMaterial(E, nu);

        // Minimal restraints:
        // 1. Ux on every node of fixed face x = 0
        List<int> fixedDofs = new();
        for (int k = 0; k <= nz; k++)
        {
            for (int j = 0; j <= ny; j++)
            {
                int node = (int)grid.NodeIndex(0, j, k);
                fixedDofs.Add(3 * node + 0); // fix Ux
            }
        }

        // 2. Uy and Uz at one node (0, 0, 0)
        int node0 = (int)grid.NodeIndex(0, 0, 0);
        fixedDofs.Add(3 * node0 + 1); // Uy
        fixedDofs.Add(3 * node0 + 2); // Uz

        // 3. Uz at a second node displaced along y (0, ny, 0)
        int nodeY = (int)grid.NodeIndex(0, ny, 0);
        fixedDofs.Add(3 * nodeY + 2); // Uz

        // Tip patch
        List<int> tipNodes = new();
        List<double> tipAreas = new();
        for (int k = 0; k <= nz; k++)
        {
            for (int j = 0; j <= ny; j++)
            {
                tipNodes.Add((int)grid.NodeIndex(nx, j, k));
                double dy = (j == 0 || j == ny) ? 0.5 * h : h;
                double dz = (k == 0 || k == nz) ? 0.5 * h : h;
                tipAreas.Add(dy * dz);
            }
        }

        var tipPatch = new SurfacePatch(
            Array.Empty<VoxelFace>(),
            tipNodes.ToArray(),
            tipAreas.ToArray(),
            (new Vector3((float)L, 0, 0), new Vector3((float)L, (float)b, (float)d)),
            true);

        var model = new FeModel(grid, domain, mat, fixedDofs.ToArray(), new[] { new LoadCase(tipPatch, new Vector3((float)P, 0, 0)) });

        var options = new StaticAnalysisOptions(Tolerance: 1e-10, MaxIterations: 5000);
        var result = StaticAnalysis.Run(model, options);

        Assert.True(result.SolverInfo.Converged, "Solver did not converge.");

        double expectedTipU = (P * L) / (A * E);
        foreach (int node in tipPatch.Nodes)
        {
            double ux = result.U[3 * node + 0];
            double err = Math.Abs(ux - expectedTipU);
            Assert.True(err < 1e-6, $"Tip displacement error {err} exceeds 1e-6 (ux={ux}, expected={expectedTipU})");
        }
    }

    [Fact]
    public void V3_Cantilever_TimoshenkoConvergence_MonotonicAndUnder8Percent()
    {
        double L = 100.0, b = 10.0, d = 10.0;
        double E = 3000.0, nu = 0.3, P = 1000.0;

        double I = (b * Math.Pow(d, 3)) / 12.0;
        double G = E / (2.0 * (1.0 + nu));
        double kappa = 10.0 * (1.0 + nu) / (12.0 + 11.0 * nu);
        double A = b * d;
        double deltaTimoshenko = (P * Math.Pow(L, 3)) / (3.0 * E * I) + (P * L) / (kappa * G * A);

        int[] depths = { 1, 2, 4, 8 };
        double[] relativeErrors = new double[depths.Length];

        for (int step = 0; step < depths.Length; step++)
        {
            int nz = depths[step];
            int ny = nz;
            double h = d / nz;
            int nx = (int)Math.Round(L / h);

            var grid = new GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
            byte[] domain = new byte[nx * ny * nz];
            Array.Fill(domain, (byte)1);

            var mat = new FeMaterial(E, nu);

            // Fully fixed face at x = 0
            List<int> fixedNodes = new();
            for (int k = 0; k <= nz; k++)
                for (int j = 0; j <= ny; j++)
                    fixedNodes.Add((int)grid.NodeIndex(0, j, k));

            int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

            // Tip patch at x = L with load along -Z
            List<int> tipNodes = new();
            List<double> tipAreas = new();
            for (int k = 0; k <= nz; k++)
            {
                for (int j = 0; j <= ny; j++)
                {
                    tipNodes.Add((int)grid.NodeIndex(nx, j, k));
                    double dy = (j == 0 || j == ny) ? 0.5 * h : h;
                    double dz = (k == 0 || k == nz) ? 0.5 * h : h;
                    tipAreas.Add(dy * dz);
                }
            }

            var tipPatch = new SurfacePatch(
                Array.Empty<VoxelFace>(),
                tipNodes.ToArray(),
                tipAreas.ToArray(),
                (new Vector3((float)L, 0, 0), new Vector3((float)L, (float)b, (float)d)),
                true);

            var model = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(tipPatch, new Vector3(0, 0, (float)-P)) });

            var options = new StaticAnalysisOptions(Tolerance: 1e-8, MaxIterations: 10000);
            var result = StaticAnalysis.Run(model, options);
            Assert.True(result.SolverInfo.Converged);

            // Average tip deflection along Z
            double sumTipUz = 0.0;
            double sumArea = 0.0;
            for (int i = 0; i < tipNodes.Count; i++)
            {
                int node = tipNodes[i];
                double area = tipAreas[i];
                sumTipUz += Math.Abs(result.U[3 * node + 2]) * area;
                sumArea += area;
            }
            double deltaFe = sumTipUz / sumArea;

            relativeErrors[step] = Math.Abs(deltaFe - deltaTimoshenko) / deltaTimoshenko;
        }

        // Monotonic error decrease check
        for (int i = 0; i < depths.Length - 1; i++)
        {
            Assert.True(relativeErrors[i + 1] < relativeErrors[i],
                $"Error not decreasing monotonically from depth {depths[i]} ({relativeErrors[i]:P2}) to {depths[i+1]} ({relativeErrors[i+1]:P2})");
        }

        // Require <= 8% error at 8 elements through depth
        Assert.True(relativeErrors[3] <= 0.08, $"Error at 8 elements through depth was {relativeErrors[3]:P2}, expected <= 8%");
    }

    [Fact]
    public void V4_Equilibrium_ReactionForceAndMomentEquilibrium()
    {
        double L = 100.0, b = 10.0, d = 10.0, h = 2.5;
        double E = 3000.0, nu = 0.3, P = 1000.0;

        int nx = (int)Math.Round(L / h);
        int ny = (int)Math.Round(b / h);
        int nz = (int)Math.Round(d / h);

        var grid = new GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
        byte[] domain = new byte[nx * ny * nz];
        Array.Fill(domain, (byte)1);

        var mat = new FeMaterial(E, nu);

        // Fixed face at x = 0
        List<int> fixedNodes = new();
        for (int k = 0; k <= nz; k++)
            for (int j = 0; j <= ny; j++)
                fixedNodes.Add((int)grid.NodeIndex(0, j, k));

        int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

        // Tip load along -Z at x = L
        List<int> tipNodes = new();
        List<double> tipAreas = new();
        for (int k = 0; k <= nz; k++)
        {
            for (int j = 0; j <= ny; j++)
            {
                tipNodes.Add((int)grid.NodeIndex(nx, j, k));
                double dy = (j == 0 || j == ny) ? 0.5 * h : h;
                double dz = (k == 0 || k == nz) ? 0.5 * h : h;
                tipAreas.Add(dy * dz);
            }
        }

        var tipPatch = new SurfacePatch(
            Array.Empty<VoxelFace>(),
            tipNodes.ToArray(),
            tipAreas.ToArray(),
            (new Vector3((float)L, 0, 0), new Vector3((float)L, (float)b, (float)d)),
            true);

        Vector3 appliedForce = new(0, 0, (float)-P);
        var model = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(tipPatch, appliedForce) });

        var options = new StaticAnalysisOptions(Tolerance: 1e-10, MaxIterations: 5000);
        var result = StaticAnalysis.Run(model, options);
        Assert.True(result.SolverInfo.Converged);

        // Force equilibrium: Σ reactions = -Σ applied forces (1e-8 relative)
        double rx = 0, ry = 0, rz = 0;
        foreach (int node in fixedNodes)
        {
            rx += result.Reactions[3 * node + 0];
            ry += result.Reactions[3 * node + 1];
            rz += result.Reactions[3 * node + 2];
        }

        double fAppliedNorm = P;
        Assert.True(Math.Abs(rx - (-appliedForce.X)) / fAppliedNorm < 1e-8, $"Rx equilibrium mismatch: {rx} vs {-appliedForce.X}");
        Assert.True(Math.Abs(ry - (-appliedForce.Y)) / fAppliedNorm < 1e-8, $"Ry equilibrium mismatch: {ry} vs {-appliedForce.Y}");
        Assert.True(Math.Abs(rz - (-appliedForce.Z)) / fAppliedNorm < 1e-8, $"Rz equilibrium mismatch: {rz} vs {-appliedForce.Z}");

        // Moment equilibrium about origin (0, 0, 0): Σ (r × R) + Σ (r × F) = 0 (1e-8 relative)
        double mx = 0, my = 0, mz = 0;

        // Moment from reactions at fixed face (x = 0)
        foreach (int node in fixedNodes)
        {
            grid.NodeCoords(node, out int i, out int j, out int k);
            Vector3 pos = grid.NodePosition(i, j, k);
            double posX = pos.X, posY = pos.Y, posZ = pos.Z;
            double fx = result.Reactions[3 * node + 0];
            double fy = result.Reactions[3 * node + 1];
            double fz = result.Reactions[3 * node + 2];

            // r × R
            mx += posY * fz - posZ * fy;
            my += posZ * fx - posX * fz;
            mz += posX * fy - posY * fx;
        }

        // Moment from applied load at tip face (x = L)
        double[] appliedF = model.AssembleForces();
        for (int n = 0; n < model.TotalNodes; n++)
        {
            double fx = appliedF[3 * n + 0];
            double fy = appliedF[3 * n + 1];
            double fz = appliedF[3 * n + 2];
            if (fx != 0 || fy != 0 || fz != 0)
            {
                grid.NodeCoords(n, out int i, out int j, out int k);
                Vector3 pos = grid.NodePosition(i, j, k);
                double posX = pos.X, posY = pos.Y, posZ = pos.Z;

                // r × F
                mx += posY * fz - posZ * fy;
                my += posZ * fx - posX * fz;
                mz += posX * fy - posY * fx;
            }
        }

        double momentNorm = Math.Sqrt(mx * mx + my * my + mz * mz);
        double momentScale = P * L;
        Assert.True(momentNorm / momentScale < 1e-8, $"Moment equilibrium failed: norm={momentNorm}, scale={momentScale}, relErr={momentNorm / momentScale}");
    }

    [Fact]
    public void V5_Energy_Compliance_Equals_UTKU_Within1e8()
    {
        double L = 60.0, b = 10.0, d = 10.0, h = 2.5;
        double E = 3000.0, nu = 0.3, P = 500.0;

        var (model, tipPatch) = CreateBarModel(L, b, d, h, E, nu);
        model.Loads.Add(new LoadCase(tipPatch, new Vector3((float)P, 0, (float)-P)));

        var options = new StaticAnalysisOptions(Tolerance: 1e-10, MaxIterations: 5000);
        var result = StaticAnalysis.Run(model, options);
        Assert.True(result.SolverInfo.Converged);

        double complianceFtu = result.Compliance;

        // Compute Uᵀ K U using unconstrained operator K
        var op = new FemOperator(model);
        double[] ku = new double[model.TotalDofs];
        op.ApplyK(result.U, ku, parallel: true);

        double utku = 0.0;
        for (int i = 0; i < model.TotalDofs; i++)
        {
            utku += result.U[i] * ku[i];
        }

        double relErr = Math.Abs(complianceFtu - utku) / Math.Abs(complianceFtu);
        Assert.True(relErr < 1e-8, $"Energy compliance mismatch: FᵀU={complianceFtu}, UᵀKU={utku}, relErr={relErr}");
    }

    [Fact]
    public void V6_Symmetry_MirrorSymmetricDisplacements_Within1e9()
    {
        // Cantilever symmetric about Y=0
        // Let's create a beam with Y from -5 to +5 mm
        int nx = 20, ny = 4, nz = 4;
        float h = 2.5f;
        Vector3 origin = new(0, -5.0f, -5.0f);
        var grid = new GridSpec(origin, h, nx, ny, nz);
        byte[] domain = new byte[nx * ny * nz];
        Array.Fill(domain, (byte)1);

        var mat = new FeMaterial(3000.0, 0.3);

        // Fixed face at x = 0
        List<int> fixedNodes = new();
        for (int k = 0; k <= nz; k++)
            for (int j = 0; j <= ny; j++)
                fixedNodes.Add((int)grid.NodeIndex(0, j, k));

        int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

        // Load along Z at tip face (x = nx*h), symmetric in Y
        List<int> tipNodes = new();
        List<double> tipAreas = new();
        for (int k = 0; k <= nz; k++)
        {
            for (int j = 0; j <= ny; j++)
            {
                tipNodes.Add((int)grid.NodeIndex(nx, j, k));
                double dy = (j == 0 || j == ny) ? 0.5 * h : h;
                double dz = (k == 0 || k == nz) ? 0.5 * h : h;
                tipAreas.Add(dy * dz);
            }
        }

        var tipPatch = new SurfacePatch(
            Array.Empty<VoxelFace>(),
            tipNodes.ToArray(),
            tipAreas.ToArray(),
            (new Vector3(nx * h, -5, -5), new Vector3(nx * h, 5, 5)),
            true);

        var model = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(tipPatch, new Vector3(0, 0, -1000.0f)) });

        var options = new StaticAnalysisOptions(Tolerance: 1e-10, MaxIterations: 5000);
        var result = StaticAnalysis.Run(model, options);
        Assert.True(result.SolverInfo.Converged);

        // Check mirror symmetry across Y = 0 (node j vs node ny - j):
        // Ux(i, j, k) == Ux(i, ny - j, k)
        // Uy(i, j, k) == -Uy(i, ny - j, k)
        // Uz(i, j, k) == Uz(i, ny - j, k)
        for (int k = 0; k <= nz; k++)
        {
            for (int j = 0; j <= ny / 2; j++)
            {
                int jMirror = ny - j;
                for (int i = 0; i <= nx; i++)
                {
                    int nA = (int)grid.NodeIndex(i, j, k);
                    int nB = (int)grid.NodeIndex(i, jMirror, k);

                    double uxA = result.U[3 * nA + 0], uxB = result.U[3 * nB + 0];
                    double uyA = result.U[3 * nA + 1], uyB = result.U[3 * nB + 1];
                    double uzA = result.U[3 * nA + 2], uzB = result.U[3 * nB + 2];

                    Assert.True(Math.Abs(uxA - uxB) < 1e-9, $"Symmetry Ux mismatch at ({i}, {j}, {k}) vs mirror: {uxA} vs {uxB}");
                    Assert.True(Math.Abs(uyA - (-uyB)) < 1e-9, $"Anti-symmetry Uy mismatch at ({i}, {j}, {k}) vs mirror: {uyA} vs {-uyB}");
                    Assert.True(Math.Abs(uzA - uzB) < 1e-9, $"Symmetry Uz mismatch at ({i}, {j}, {k}) vs mirror: {uzA} vs {uzB}");
                }
            }
        }
    }

    [Fact]
    public void V7_Scaling_DoublingE_HalvesUAndCompliance_AndLengthScalingHalvesCompliance()
    {
        double L = 60.0, b = 10.0, d = 10.0, h = 2.5;
        double E1 = 2000.0, nu = 0.3, P = 500.0;

        // Model 1: base
        var (model1, tipPatch1) = CreateBarModel(L, b, d, h, E1, nu);
        model1.Loads.Add(new LoadCase(tipPatch1, new Vector3(0, 0, (float)-P)));
        var res1 = StaticAnalysis.Run(model1, new StaticAnalysisOptions(Tolerance: 1e-10));

        // Model 2: double E
        var (model2, tipPatch2) = CreateBarModel(L, b, d, h, 2.0 * E1, nu);
        model2.Loads.Add(new LoadCase(tipPatch2, new Vector3(0, 0, (float)-P)));
        var res2 = StaticAnalysis.Run(model2, new StaticAnalysisOptions(Tolerance: 1e-10));

        // Doubling E must halve compliance and displacement to 1e-10 relative
        double complianceRatio = res2.Compliance / res1.Compliance;
        Assert.True(Math.Abs(complianceRatio - 0.5) < 1e-10, $"Doubling E did not halve compliance: ratio = {complianceRatio}");

        double dispRatio = res2.MaxDisplacement / res1.MaxDisplacement;
        Assert.True(Math.Abs(dispRatio - 0.5) < 1e-10, $"Doubling E did not halve MaxDisplacement: ratio = {dispRatio}");

        // Model 3: scale all lengths by s = 2, same total force P
        // In 3D linear elasticity, K scales as s (stiffness ~ E*L), so compliance ~ 1/s = 0.5
        var (model3, tipPatch3) = CreateBarModel(2.0 * L, 2.0 * b, 2.0 * d, 2.0 * h, E1, nu);
        model3.Loads.Add(new LoadCase(tipPatch3, new Vector3(0, 0, (float)-P)));
        var res3 = StaticAnalysis.Run(model3, new StaticAnalysisOptions(Tolerance: 1e-10));

        double lenScaleComplianceRatio = res3.Compliance / res1.Compliance;
        Assert.True(Math.Abs(lenScaleComplianceRatio - 0.5) < 1e-10, $"Length scaling by 2 did not halve compliance: ratio = {lenScaleComplianceRatio}");
    }

    [Fact]
    public void V8_Robustness_NoSupportsRaisesError_AndDisconnectedIslandConverges()
    {
        // 1. Model with no supports or < 6 fixed DOFs raises InvalidOperationException
        var grid = new GridSpec(Vector3.Zero, 2.5f, 4, 4, 4);
        byte[] domain = new byte[64];
        Array.Fill(domain, (byte)1);
        var mat = new FeMaterial(3000.0, 0.3);

        // 0 fixed DOFs
        var modelNoSupports = new FeModel(grid, domain, mat, Array.Empty<int>());
        Assert.Throws<InvalidOperationException>(() => StaticAnalysis.Run(modelNoSupports));

        // 3 fixed DOFs (< 6)
        var model3Dofs = new FeModel(grid, domain, mat, new int[] { 0, 1, 2 });
        Assert.Throws<InvalidOperationException>(() => StaticAnalysis.Run(model3Dofs));

        // 2. Disconnected floating island of domain does not break convergence
        // Main body: elements (0..10, 0..2, 0..2)
        // Disconnected island: element (15, 0, 0)
        int nx = 16, ny = 2, nz = 2;
        var gridIsland = new GridSpec(Vector3.Zero, 2.5f, nx, ny, nz);
        byte[] domainIsland = new byte[nx * ny * nz];
        for (int iz = 0; iz < nz; iz++)
            for (int iy = 0; iy < ny; iy++)
                for (int ix = 0; ix < 10; ix++)
                    domainIsland[gridIsland.ElementIndex(ix, iy, iz)] = 1;

        // Disconnected island at x = 15
        domainIsland[gridIsland.ElementIndex(15, 0, 0)] = 1;

        // Supports on main body at x = 0
        List<int> fixedNodes = new();
        for (int k = 0; k <= nz; k++)
            for (int j = 0; j <= ny; j++)
                fixedNodes.Add((int)gridIsland.NodeIndex(0, j, k));

        int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

        // Load at x = 9 on main body
        List<int> tipNodes = new();
        List<double> tipAreas = new();
        for (int k = 0; k <= nz; k++)
        {
            for (int j = 0; j <= ny; j++)
            {
                tipNodes.Add((int)gridIsland.NodeIndex(9, j, k));
                tipAreas.Add(2.5 * 2.5);
            }
        }
        var patch = new SurfacePatch(Array.Empty<VoxelFace>(), tipNodes.ToArray(), tipAreas.ToArray(), (Vector3.Zero, Vector3.One), true);

        var modelIsland = new FeModel(gridIsland, domainIsland, mat, fixedDofs, new[] { new LoadCase(patch, new Vector3(0, 0, -100.0f)) });

        var resIsland = StaticAnalysis.Run(modelIsland, new StaticAnalysisOptions(Tolerance: 1e-6));
        Assert.True(resIsland.SolverInfo.Converged, "Solver did not converge with disconnected island.");

        // 3. Model with zero applied loads returns zero displacement and 0 iterations
        var modelZeroLoads = new FeModel(grid, domain, mat, new int[] { 0, 1, 2, 3, 4, 5 });
        var resZero = StaticAnalysis.Run(modelZeroLoads);
        Assert.True(resZero.SolverInfo.Converged);
        Assert.Equal(0, resZero.SolverInfo.Iterations);
        Assert.Equal(0.0, resZero.Compliance);
        Assert.Equal(0.0, resZero.MaxDisplacement);
        Assert.All(resZero.U, val => Assert.Equal(0.0, val));
        Assert.All(resZero.Reactions, val => Assert.Equal(0.0, val));
    }

    [Fact]
    public void V9_Consistency_ScalarReference_ColouredParallel_AndBitIdentical()
    {
        double L = 40.0, b = 10.0, d = 10.0, h = 2.5;
        var (model, tipPatch) = CreateBarModel(L, b, d, h, 3000.0, 0.3);
        model.Loads.Add(new LoadCase(tipPatch, new Vector3(500.0f, -200.0f, -800.0f)));

        var op = new FemOperator(model);
        double[] x = new double[model.TotalDofs];
        var rand = new Random(42);
        for (int i = 0; i < x.Length; i++)
            x[i] = rand.NextDouble() * 2.0 - 1.0;

        // 1. Scalar reference operator vs optimised parallel operator
        double[] yRef = new double[model.TotalDofs];
        double[] yPar = new double[model.TotalDofs];
        double[] ySeq = new double[model.TotalDofs];

        op.ApplyKScalarReference(x, yRef);
        op.ApplyK(x, yPar, parallel: true);
        op.ApplyK(x, ySeq, parallel: false);

        for (int i = 0; i < model.TotalDofs; i++)
        {
            double scale = Math.Max(1.0, Math.Abs(yRef[i]));
            double relDiffPar = Math.Abs(yPar[i] - yRef[i]) / scale;
            double relDiffSeq = Math.Abs(ySeq[i] - yRef[i]) / scale;
            Assert.True(relDiffPar < 1e-12, $"Parallel vs reference mismatch at {i}: {yPar[i]} vs {yRef[i]}, relDiff={relDiffPar}");
            Assert.True(relDiffSeq < 1e-12, $"Sequential vs reference mismatch at {i}: {ySeq[i]} vs {yRef[i]}, relDiff={relDiffSeq}");
        }

        // 2. Two complete StaticAnalysis runs must be bit-identical
        var run1 = StaticAnalysis.Run(model, new StaticAnalysisOptions(Tolerance: 1e-8));
        var run2 = StaticAnalysis.Run(model, new StaticAnalysisOptions(Tolerance: 1e-8));

        Assert.Equal(run1.SolverInfo.Iterations, run2.SolverInfo.Iterations);
        Assert.Equal(run1.Compliance, run2.Compliance);
        Assert.Equal(run1.MaxDisplacement, run2.MaxDisplacement);

        for (int i = 0; i < model.TotalDofs; i++)
        {
            Assert.Equal(run1.U[i], run2.U[i]);
            Assert.Equal(run1.Reactions[i], run2.Reactions[i]);
        }
    }

    [Fact(Skip = "Explicit benchmark - run on demand")]
    public void V10_Benchmark_CantileverPerformanceScaling()
    {
        // Cantilever models at ~50k, 100k, 200k, 500k active elements
        int[] targetCounts = { 50_000, 100_000, 200_000, 500_000 };

        foreach (int target in targetCounts)
        {
            // Beam aspect ratio: L/d = 5, b/d = 1. Volume = 5 * d³.
            // Elements: nx = 5 * nz, ny = nz. Total elements = 5 * nz³.
            int nz = (int)Math.Round(Math.Cbrt(target / 5.0));
            int ny = nz;
            int nx = 5 * nz;
            int totalActive = nx * ny * nz;

            double h = 1.0;
            var grid = new GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
            byte[] domain = new byte[totalActive];
            Array.Fill(domain, (byte)1);

            var mat = new FeMaterial(3000.0, 0.3);

            List<int> fixedNodes = new();
            for (int k = 0; k <= nz; k++)
                for (int j = 0; j <= ny; j++)
                    fixedNodes.Add((int)grid.NodeIndex(0, j, k));

            int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

            List<int> tipNodes = new();
            List<double> tipAreas = new();
            for (int k = 0; k <= nz; k++)
            {
                for (int j = 0; j <= ny; j++)
                {
                    tipNodes.Add((int)grid.NodeIndex(nx, j, k));
                    tipAreas.Add(h * h);
                }
            }

            var tipPatch = new SurfacePatch(
                Array.Empty<VoxelFace>(),
                tipNodes.ToArray(),
                tipAreas.ToArray(),
                (new Vector3(nx * (float)h, 0, 0), new Vector3(nx * (float)h, ny * (float)h, nz * (float)h)),
                true);

            var model0 = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(tipPatch, new Vector3(0, 0, -1000.0f)) });
            var op = new FemOperator(model0);

            // Benchmark matvec
            double[] x = new double[model0.TotalDofs];
            double[] y = new double[model0.TotalDofs];
            Array.Fill(x, 1.0);

            // Warmup
            op.ApplyK(x, y, parallel: true);

            int matvecReps = 20;
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < matvecReps; r++)
                op.ApplyK(x, y, parallel: true);
            sw.Stop();
            double msPerMatvec = sw.Elapsed.TotalMilliseconds / matvecReps;

            // 1. Cold solve to 1e-4
            var swSolve4 = Stopwatch.StartNew();
            var resCold4 = StaticAnalysis.Run(model0, new StaticAnalysisOptions(Tolerance: 1e-4));
            swSolve4.Stop();

            // 2. Perturb density by 2% (rho = 0.98), simulating 1 OC step
            double[] rhoPerturbed = new double[totalActive];
            Array.Fill(rhoPerturbed, 0.98);
            var modelPerturbed = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(tipPatch, new Vector3(0, 0, -1000.0f)) }, elementDensity: rhoPerturbed);

            // 3. Warm start to 1e-4 from resCold4.U on perturbed model
            var swWarm4 = Stopwatch.StartNew();
            var resWarm4 = StaticAnalysis.Run(modelPerturbed, new StaticAnalysisOptions(Tolerance: 1e-4, WarmStartDisplacements: resCold4.U));
            swWarm4.Stop();

            // 4. Cold start to 1e-4 on perturbed model
            var swColdPert4 = Stopwatch.StartNew();
            var resColdPert4 = StaticAnalysis.Run(modelPerturbed, new StaticAnalysisOptions(Tolerance: 1e-4));
            swColdPert4.Stop();

            // 5. Cold start to 1e-6
            var swCold6 = Stopwatch.StartNew();
            var resCold6 = StaticAnalysis.Run(model0, new StaticAnalysisOptions(Tolerance: 1e-6));
            swCold6.Stop();

            long memoryBytes = GC.GetTotalMemory(forceFullCollection: false);

            Console.WriteLine($"[BENCH] Elements: {totalActive:N0}, Matvec: {msPerMatvec:F2} ms, PCG(1e-4 cold): {resCold4.SolverInfo.Iterations} it ({swSolve4.Elapsed.TotalSeconds:F2} s), PCG(1e-4 warm 2% Δρ): {resWarm4.SolverInfo.Iterations} it ({swWarm4.Elapsed.TotalSeconds:F2} s) [vs cold: {resColdPert4.SolverInfo.Iterations} it], PCG(1e-6 cold): {resCold6.SolverInfo.Iterations} it ({swCold6.Elapsed.TotalSeconds:F2} s), Memory: {memoryBytes / (1024.0 * 1024.0):F1} MB");
        }
    }

    [Fact]
    public void V11_AnisotropicBox_AxisInvariance_NxNyNzDifferent()
    {
        int nx = 8, ny = 6, nz = 4;
        double h = 1.0;
        double e0 = 1000.0;
        double nu = 0.0; // nu=0 for pure 1D uniaxial elongation delta = F*L / (E*A)
        var mat = new FeMaterial(e0, nu);

        // Case X: Length = nx*h = 8, Area = (ny*h)*(nz*h) = 24. Force = 240 N. delta = 240 * 8 / (1000 * 24) = 0.08 mm.
        {
            var grid = new GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
            byte[] domain = new byte[nx * ny * nz];
            Array.Fill(domain, (byte)1);

            List<int> fixedNodes = new();
            for (int k = 0; k <= nz; k++)
                for (int j = 0; j <= ny; j++)
                    fixedNodes.Add((int)grid.NodeIndex(0, j, k));

            int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

            List<int> loadNodes = new();
            List<double> loadAreas = new();
            for (int k = 0; k <= nz; k++)
            {
                double dz = (k == 0 || k == nz) ? 0.5 * h : h;
                for (int j = 0; j <= ny; j++)
                {
                    double dy = (j == 0 || j == ny) ? 0.5 * h : h;
                    loadNodes.Add((int)grid.NodeIndex(nx, j, k));
                    loadAreas.Add(dy * dz);
                }
            }

            var patch = new SurfacePatch(Array.Empty<VoxelFace>(), loadNodes.ToArray(), loadAreas.ToArray(), (Vector3.Zero, Vector3.One), true);
            var model = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(patch, new Vector3(240.0f, 0, 0)) });
            var res = StaticAnalysis.Run(model, new StaticAnalysisOptions(Tolerance: 1e-7));

            double expectedDisp = 240.0 * (nx * h) / (e0 * (ny * h * nz * h));
            double relErr = Math.Abs(res.MaxDisplacement - expectedDisp) / expectedDisp;
            Assert.True(relErr < 0.01, $"X-tension disp error: {res.MaxDisplacement} vs {expectedDisp}, err={relErr}");
        }

        // Case Y: Length = ny*h = 6, Area = (nx*h)*(nz*h) = 32. Force = 320 N. delta = 320 * 6 / (1000 * 32) = 0.06 mm.
        {
            var grid = new GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
            byte[] domain = new byte[nx * ny * nz];
            Array.Fill(domain, (byte)1);

            List<int> fixedNodes = new();
            for (int k = 0; k <= nz; k++)
                for (int i = 0; i <= nx; i++)
                    fixedNodes.Add((int)grid.NodeIndex(i, 0, k));

            int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

            List<int> loadNodes = new();
            List<double> loadAreas = new();
            for (int k = 0; k <= nz; k++)
            {
                double dz = (k == 0 || k == nz) ? 0.5 * h : h;
                for (int i = 0; i <= nx; i++)
                {
                    double dx = (i == 0 || i == nx) ? 0.5 * h : h;
                    loadNodes.Add((int)grid.NodeIndex(i, ny, k));
                    loadAreas.Add(dx * dz);
                }
            }

            var patch = new SurfacePatch(Array.Empty<VoxelFace>(), loadNodes.ToArray(), loadAreas.ToArray(), (Vector3.Zero, Vector3.One), true);
            var model = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(patch, new Vector3(0, 320.0f, 0)) });
            var res = StaticAnalysis.Run(model, new StaticAnalysisOptions(Tolerance: 1e-7));

            double expectedDisp = 320.0 * (ny * h) / (e0 * (nx * h * nz * h));
            double relErr = Math.Abs(res.MaxDisplacement - expectedDisp) / expectedDisp;
            Assert.True(relErr < 0.01, $"Y-tension disp error: {res.MaxDisplacement} vs {expectedDisp}, err={relErr}");
        }

        // Case Z: Length = nz*h = 4, Area = (nx*h)*(ny*h) = 48. Force = 480 N. delta = 480 * 4 / (1000 * 48) = 0.04 mm.
        {
            var grid = new GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
            byte[] domain = new byte[nx * ny * nz];
            Array.Fill(domain, (byte)1);

            List<int> fixedNodes = new();
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                    fixedNodes.Add((int)grid.NodeIndex(i, j, 0));

            int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

            List<int> loadNodes = new();
            List<double> loadAreas = new();
            for (int j = 0; j <= ny; j++)
            {
                double dy = (j == 0 || j == ny) ? 0.5 * h : h;
                for (int i = 0; i <= nx; i++)
                {
                    double dx = (i == 0 || i == nx) ? 0.5 * h : h;
                    loadNodes.Add((int)grid.NodeIndex(i, j, nz));
                    loadAreas.Add(dx * dy);
                }
            }

            var patch = new SurfacePatch(Array.Empty<VoxelFace>(), loadNodes.ToArray(), loadAreas.ToArray(), (Vector3.Zero, Vector3.One), true);
            var model = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(patch, new Vector3(0, 0, 480.0f)) });
            var res = StaticAnalysis.Run(model, new StaticAnalysisOptions(Tolerance: 1e-7));

            double expectedDisp = 480.0 * (nz * h) / (e0 * (nx * h * ny * h));
            double relErr = Math.Abs(res.MaxDisplacement - expectedDisp) / expectedDisp;
            Assert.True(relErr < 0.01, $"Z-tension disp error: {res.MaxDisplacement} vs {expectedDisp}, err={relErr}");
        }
    }

    [Fact]
    public void V12_UnderconstrainedModel_ThrowsInvalidOperationException()
    {
        // 6 fixed DOFs, but all on a single line (x=0, y=0), leaving rotation about Z free
        var grid = new GridSpec(Vector3.Zero, 1.0f, 4, 4, 4);
        byte[] domain = new byte[64];
        Array.Fill(domain, (byte)1);
        var mat = new FeMaterial(1000.0, 0.3);

        // Fix 2 nodes along the Z-axis (x=0, y=0, z=0) and (x=0, y=0, z=4) -> 6 DOFs, but body can rotate around Z
        int n0 = (int)grid.NodeIndex(0, 0, 0);
        int n1 = (int)grid.NodeIndex(0, 0, 4);
        int[] fixedDofs = FeModel.FixDofs(new[] { n0, n1 }, true, true, true);
        Assert.Equal(6, fixedDofs.Length);

        // Apply torque / tangential force at (x=4, y=0, z=2) in +Y direction
        int loadNode = (int)grid.NodeIndex(4, 0, 2);
        var patch = new SurfacePatch(Array.Empty<VoxelFace>(), new[] { loadNode }, new[] { 1.0 }, (Vector3.Zero, Vector3.One), true);
        var model = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(patch, new Vector3(0, 100.0f, 0)) });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            StaticAnalysis.Run(model, new StaticAnalysisOptions(Tolerance: 1e-6, MaxIterations: 500));
        });

        Assert.Contains("sufficiently constrained", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V13_WarmStart_ExactSolutionTakesZeroIterations()
    {
        var grid = new GridSpec(Vector3.Zero, 1.0f, 6, 2, 2);
        byte[] domain = new byte[24];
        Array.Fill(domain, (byte)1);
        var mat = new FeMaterial(1000.0, 0.3);

        List<int> fixedNodes = new();
        for (int k = 0; k <= 2; k++)
            for (int j = 0; j <= 2; j++)
                fixedNodes.Add((int)grid.NodeIndex(0, j, k));

        int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

        int tipNode = (int)grid.NodeIndex(6, 1, 1);
        var patch = new SurfacePatch(Array.Empty<VoxelFace>(), new[] { tipNode }, new[] { 1.0 }, (Vector3.Zero, Vector3.One), true);
        var model = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(patch, new Vector3(0, 0, -10.0f)) });

        // Solve cold
        var resCold = StaticAnalysis.Run(model, new StaticAnalysisOptions(Tolerance: 1e-6));
        Assert.True(resCold.SolverInfo.Iterations > 0);

        // Solve warm with exact solution
        var resWarm = StaticAnalysis.Run(model, new StaticAnalysisOptions(Tolerance: 1e-6, WarmStartDisplacements: resCold.U));
        Assert.Equal(0, resWarm.SolverInfo.Iterations);
        Assert.Equal(resCold.Compliance, resWarm.Compliance, 1e-10);
    }

    [Fact]
    public void V14_WarmStart_PerturbedDensity_ConvergesFasterThanCold()
    {
        var grid = new GridSpec(Vector3.Zero, 1.0f, 20, 6, 6);
        int totalElements = 20 * 6 * 6;
        byte[] domain = new byte[totalElements];
        Array.Fill(domain, (byte)1);
        var mat = new FeMaterial(1000.0, 0.3);

        List<int> fixedNodes = new();
        for (int k = 0; k <= 6; k++)
            for (int j = 0; j <= 6; j++)
                fixedNodes.Add((int)grid.NodeIndex(0, j, k));

        int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

        List<int> tipNodes = new();
        List<double> tipAreas = new();
        for (int k = 0; k <= 6; k++)
            for (int j = 0; j <= 6; j++)
            {
                tipNodes.Add((int)grid.NodeIndex(20, j, k));
                tipAreas.Add(1.0);
            }

        var patch = new SurfacePatch(Array.Empty<VoxelFace>(), tipNodes.ToArray(), tipAreas.ToArray(), (Vector3.Zero, Vector3.One), true);
        var model0 = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(patch, new Vector3(0, 0, -100.0f)) });

        // Step 0: solve to 1e-4
        var res0 = StaticAnalysis.Run(model0, new StaticAnalysisOptions(Tolerance: 1e-4));

        // Step 1: perturb density by 2% (rho = 0.98) simulating one topopt OC iteration
        double[] rho1 = new double[totalElements];
        Array.Fill(rho1, 0.98);
        var model1 = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(patch, new Vector3(0, 0, -100.0f)) }, elementDensity: rho1);

        // Cold solve on model1 to 1e-4
        var resCold = StaticAnalysis.Run(model1, new StaticAnalysisOptions(Tolerance: 1e-4));

        // Warm solve on model1 to 1e-4 using res0.U
        var resWarm = StaticAnalysis.Run(model1, new StaticAnalysisOptions(Tolerance: 1e-4, WarmStartDisplacements: res0.U));

        Assert.True(resWarm.SolverInfo.Iterations < resCold.SolverInfo.Iterations,
            $"Warm start iterations ({resWarm.SolverInfo.Iterations}) should be fewer than cold start ({resCold.SolverInfo.Iterations})");
    }

    [Fact]
    public void V15_StressRecovery_PureShearAndBending_WithNonUnitHAndPenalizedDensity()
    {
        // 1. Pure shear with h = 2.5 mm, rho = 0.8, penal = 3.0
        double h = 2.5;
        double e0 = 2000.0;
        double nu = 0.25;
        double rho = 0.8;
        double penal = 3.0;
        double ee = Math.Pow(rho, penal) * e0; // E_e = 0.8^3 * 2000 = 1024 MPa
        double g = ee / (2.0 * (1.0 + nu));   // G = 1024 / 2.5 = 409.6 MPa

        var grid = new GridSpec(Vector3.Zero, (float)h, 2, 2, 2);
        byte[] domain = new byte[8];
        Array.Fill(domain, (byte)1);
        double[] densities = new double[8];
        Array.Fill(densities, rho);
        var mat = new FeMaterial(e0, nu);

        var model = new FeModel(grid, domain, mat, Array.Empty<int>(), elementDensity: densities, penal: penal);
        var op = new FemOperator(model);

        // Impose pure shear displacement field: u_x = gamma * y, u_y = 0, u_z = 0
        double gamma = 0.002; // engineering shear strain gamma_xy = 0.002
        double[] u = new double[model.TotalDofs];
        for (int k = 0; k <= 2; k++)
            for (int j = 0; j <= 2; j++)
                for (int i = 0; i <= 2; i++)
                {
                    int n = (int)grid.NodeIndex(i, j, k);
                    double y = j * h;
                    u[3 * n + 0] = gamma * y;
                }

        // Stress recovery
        double[] vm = StaticAnalysis.ComputeElementVonMises(model, op, u);

        // Analytical: tau_xy = G * gamma = 409.6 * 0.002 = 0.8192 MPa
        // sigma_vm = sqrt(3 * tau_xy^2) = sqrt(3) * tau_xy
        double tauExpected = g * gamma;
        double vmExpected = Math.Sqrt(3.0) * tauExpected;

        for (int e = 0; e < 8; e++)
        {
            double relErr = Math.Abs(vm[e] - vmExpected) / vmExpected;
            Assert.True(relErr < 1e-6, $"Element {e} von Mises error: {vm[e]} vs {vmExpected}, err={relErr}");
        }
    }

    [Fact]
    public void V16_UnprojectedRhs_PcgProjectsCorrectly()
    {
        var grid = new GridSpec(Vector3.Zero, 1.0f, 4, 2, 2);
        byte[] domain = new byte[16];
        Array.Fill(domain, (byte)1);
        var mat = new FeMaterial(1000.0, 0.3);

        List<int> fixedNodes = new();
        for (int k = 0; k <= 2; k++)
            for (int j = 0; j <= 2; j++)
                fixedNodes.Add((int)grid.NodeIndex(0, j, k));

        int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);
        var model = new FeModel(grid, domain, mat, fixedDofs);
        var op = new FemOperator(model);

        // RHS with load at tip AND large force components at fixed DOFs
        double[] bUnprojected = new double[model.TotalDofs];
        int tipDof = 3 * (int)grid.NodeIndex(4, 1, 1) + 2;
        bUnprojected[tipDof] = -50.0;
        foreach (int fd in fixedDofs)
        {
            bUnprojected[fd] = 12345.67; // Large force on constrained DOF!
        }

        double[] bClean = new double[model.TotalDofs];
        bClean[tipDof] = -50.0;

        var solver = new PcgSolver();
        double[] u1 = new double[model.TotalDofs];
        double[] u2 = new double[model.TotalDofs];

        var prec = new JacobiPreconditioner();
        var res1 = solver.Solve(op, bUnprojected, u1, prec, tol: 1e-6);
        var res2 = solver.Solve(op, bClean, u2, prec, tol: 1e-6);

        Assert.True(res1.Converged);
        Assert.True(res2.Converged);

        // Displacements on constrained DOFs must be exactly 0
        foreach (int fd in fixedDofs)
        {
            Assert.Equal(0.0, u1[fd]);
        }

        // Entire solution must match the clean solve to machine precision
        for (int i = 0; i < model.TotalDofs; i++)
        {
            Assert.Equal(u2[i], u1[i], 1e-10);
        }
    }

    [Fact]
    public void V17_InvalidWarmStartLength_ThrowsArgumentException()
    {
        var grid = new GridSpec(Vector3.Zero, 1.0f, 2, 2, 2);
        byte[] domain = new byte[8];
        Array.Fill(domain, (byte)1);
        var mat = new FeMaterial(1000.0, 0.3);
        int[] fixedDofs = FeModel.FixDofs(new[] { 0, 1 }, true, true, true);
        var model = new FeModel(grid, domain, mat, fixedDofs);

        var badWarmStart = new double[model.TotalDofs - 1]; // invalid length!

        Assert.Throws<ArgumentException>(() =>
        {
            StaticAnalysis.Run(model, new StaticAnalysisOptions(WarmStartDisplacements: badWarmStart));
        });
    }

    [Fact]
    public void V18_ZeroAllocationsInHotLoops()
    {
        var grid = new GridSpec(Vector3.Zero, 1.0f, 10, 4, 4);
        int totalElements = 10 * 4 * 4;
        byte[] domain = new byte[totalElements];
        Array.Fill(domain, (byte)1);
        var mat = new FeMaterial(1000.0, 0.3);

        List<int> fixedNodes = new();
        for (int k = 0; k <= 4; k++)
            for (int j = 0; j <= 4; j++)
                fixedNodes.Add((int)grid.NodeIndex(0, j, k));

        int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);
        int tipDof = 3 * (int)grid.NodeIndex(10, 2, 2) + 2;

        var model = new FeModel(grid, domain, mat, fixedDofs);
        var op = new FemOperator(model);
        var solver = new PcgSolver();
        var prec = new JacobiPreconditioner();

        double[] b = new double[model.TotalDofs];
        b[tipDof] = -100.0;
        double[] u = new double[model.TotalDofs];
        double[] y = new double[model.TotalDofs];

        // Warmup JIT and pre-allocate solver buffers
        op.ApplyK(b, y, parallel: true);
        solver.Solve(op, b, u, prec, tol: 1e-4, maxIterations: 10);

        // 1. Assert ApplyK causes zero GC allocations
        long allocBeforeApplyK = GC.GetAllocatedBytesForCurrentThread();
        for (int rep = 0; rep < 50; rep++)
        {
            op.ApplyK(b, y, parallel: true);
        }
        long allocAfterApplyK = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, allocAfterApplyK - allocBeforeApplyK);

        // 2. Assert PCG solve loop causes zero GC allocations
        // (the only allocation across all iterations is the final PcgResult return object <= 128 bytes)
        Array.Clear(u);
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        var result = solver.Solve(op, b, u, prec, tol: 1e-4, maxIterations: 50);
        long b1 = GC.GetAllocatedBytesForCurrentThread();
        long allocDuringSolve = b1 - b0;
        Assert.True(allocDuringSolve <= 128, $"Allocations ({allocDuringSolve} bytes) should be at most the single PcgResult instance");
    }
}

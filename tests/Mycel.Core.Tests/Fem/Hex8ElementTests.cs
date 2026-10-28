using System;
using Mycel.Core.Fem;
using Xunit;

namespace Mycel.Core.Tests.Fem;

public class Hex8ElementTests
{
    [Fact]
    public void Ke_IsSymmetric_Within1e12()
    {
        double[] ke = Hex8Element.BaseKe;
        Assert.Equal(576, ke.Length);

        for (int i = 0; i < 24; i++)
        {
            for (int j = 0; j < 24; j++)
            {
                double diff = Math.Abs(ke[i * 24 + j] - ke[j * 24 + i]);
                Assert.True(diff < 1e-12, $"Ke not symmetric at ({i}, {j}): diff={diff}");
            }
        }
    }

    [Fact]
    public void Ke_RigidTranslations_ProduceZeroNodalForces()
    {
        double[] ke = Hex8Element.BaseKe;

        // Rigid translation along X, Y, Z
        for (int axis = 0; axis < 3; axis++)
        {
            double[] u = new double[24];
            for (int node = 0; node < 8; node++)
            {
                u[3 * node + axis] = 1.0;
            }

            double[] f = Multiply(ke, u);

            for (int i = 0; i < 24; i++)
            {
                Assert.True(Math.Abs(f[i]) < 1e-12, $"Rigid translation along axis {axis} produced non-zero force at {i}: {f[i]}");
            }
        }
    }

    [Fact]
    public void Ke_RigidRotations_ProduceZeroNodalForces()
    {
        double[] ke = Hex8Element.BaseKe;

        // Element-local coordinates for unit cube [0, 1]³
        // Node order:
        // 0:(0,0,0), 1:(1,0,0), 2:(1,1,0), 3:(0,1,0), 4:(0,0,1), 5:(1,0,1), 6:(1,1,1), 7:(0,1,1)
        double[][] coords = new double[][]
        {
            new double[] { 0, 0, 0 },
            new double[] { 1, 0, 0 },
            new double[] { 1, 1, 0 },
            new double[] { 0, 1, 0 },
            new double[] { 0, 0, 1 },
            new double[] { 1, 0, 1 },
            new double[] { 1, 1, 1 },
            new double[] { 0, 1, 1 }
        };

        // Infinitesimal rotation: u = ω × (r - r0), choose center r0 = (0.5, 0.5, 0.5)
        double theta = 1e-3;

        // 1. Rotation about X: ux = 0, uy = -theta*(z-0.5), uz = theta*(y-0.5)
        double[] uX = new double[24];
        for (int a = 0; a < 8; a++)
        {
            uX[3 * a + 1] = -theta * (coords[a][2] - 0.5);
            uX[3 * a + 2] =  theta * (coords[a][1] - 0.5);
        }
        double[] fX = Multiply(ke, uX);
        for (int i = 0; i < 24; i++)
            Assert.True(Math.Abs(fX[i]) < 1e-12, $"Rotation X produced non-zero force at {i}: {fX[i]}");

        // 2. Rotation about Y: ux = theta*(z-0.5), uy = 0, uz = -theta*(x-0.5)
        double[] uY = new double[24];
        for (int a = 0; a < 8; a++)
        {
            uY[3 * a + 0] =  theta * (coords[a][2] - 0.5);
            uY[3 * a + 2] = -theta * (coords[a][0] - 0.5);
        }
        double[] fY = Multiply(ke, uY);
        for (int i = 0; i < 24; i++)
            Assert.True(Math.Abs(fY[i]) < 1e-12, $"Rotation Y produced non-zero force at {i}: {fY[i]}");

        // 3. Rotation about Z: ux = -theta*(y-0.5), uy = theta*(x-0.5), uz = 0
        double[] uZ = new double[24];
        for (int a = 0; a < 8; a++)
        {
            uZ[3 * a + 0] = -theta * (coords[a][1] - 0.5);
            uZ[3 * a + 1] =  theta * (coords[a][0] - 0.5);
        }
        double[] fZ = Multiply(ke, uZ);
        for (int i = 0; i < 24; i++)
            Assert.True(Math.Abs(fZ[i]) < 1e-12, $"Rotation Z produced non-zero force at {i}: {fZ[i]}");
    }

    [Fact]
    public void Ke_UniaxialPatchTest_ProducesConsistentNodalForces()
    {
        double nu = 0.3;
        double e = 1.0;
        double h = 1.0;
        double[] ke = Hex8Element.CreateKe(h, nu);

        double eps = 0.001;
        // Displacement u = eps * x on the 8 nodes, v = 0, w = 0
        // Nodes with x=1 are 1, 2, 5, 6; nodes with x=0 are 0, 3, 4, 7
        double[] u = new double[24];
        int[] nodesX1 = { 1, 2, 5, 6 };
        foreach (int a in nodesX1)
        {
            u[3 * a + 0] = eps * h;
        }

        double[] f = Multiply(ke, u);

        // Analytic state for uniaxial strain:
        // σ_xx = E(1 - ν) / ((1 + ν)(1 - 2ν)) * ε
        // σ_yy = σ_zz = E ν / ((1 + ν)(1 - 2ν)) * ε
        double factor = e / ((1.0 + nu) * (1.0 - 2.0 * nu));
        double sigXX = factor * (1.0 - nu) * eps;
        double sigYY = factor * nu * eps;
        double sigZZ = factor * nu * eps;

        // Each face node carries σ * Area / 4 = σ * h² / 4
        double faceAreaQuarter = (h * h) / 4.0;
        double fxxExpected = sigXX * faceAreaQuarter;
        double fyyExpected = sigYY * faceAreaQuarter;
        double fzzExpected = sigZZ * faceAreaQuarter;

        // Check X forces on face x=h (nodes 1, 2, 5, 6): +fxxExpected
        foreach (int a in nodesX1)
            Assert.True(Math.Abs(f[3 * a + 0] - fxxExpected) < 1e-12, $"Node {a} Fx: expected {fxxExpected}, got {f[3 * a + 0]}");

        // Check X forces on face x=0 (nodes 0, 3, 4, 7): -fxxExpected
        int[] nodesX0 = { 0, 3, 4, 7 };
        foreach (int a in nodesX0)
            Assert.True(Math.Abs(f[3 * a + 0] - (-fxxExpected)) < 1e-12, $"Node {a} Fx: expected {-fxxExpected}, got {f[3 * a + 0]}");

        // Check Y forces on face y=h (nodes 2, 3, 6, 7): +fyyExpected
        int[] nodesY1 = { 2, 3, 6, 7 };
        foreach (int a in nodesY1)
            Assert.True(Math.Abs(f[3 * a + 1] - fyyExpected) < 1e-12, $"Node {a} Fy: expected {fyyExpected}, got {f[3 * a + 1]}");

        // Check Y forces on face y=0 (nodes 0, 1, 4, 5): -fyyExpected
        int[] nodesY0 = { 0, 1, 4, 5 };
        foreach (int a in nodesY0)
            Assert.True(Math.Abs(f[3 * a + 1] - (-fyyExpected)) < 1e-12, $"Node {a} Fy: expected {-fyyExpected}, got {f[3 * a + 1]}");

        // Check Z forces on face z=h (nodes 4, 5, 6, 7): +fzzExpected
        int[] nodesZ1 = { 4, 5, 6, 7 };
        foreach (int a in nodesZ1)
            Assert.True(Math.Abs(f[3 * a + 2] - fzzExpected) < 1e-12, $"Node {a} Fz: expected {fzzExpected}, got {f[3 * a + 2]}");

        // Check Z forces on face z=0 (nodes 0, 1, 2, 3): -fzzExpected
        int[] nodesZ0 = { 0, 1, 2, 3 };
        foreach (int a in nodesZ0)
            Assert.True(Math.Abs(f[3 * a + 2] - (-fzzExpected)) < 1e-12, $"Node {a} Fz: expected {-fzzExpected}, got {f[3 * a + 2]}");
    }

    [Fact]
    public void Ke_PureShearPatchTest_ProducesConsistentNodalForces()
    {
        double nu = 0.3;
        double e = 1.0;
        double h = 1.0;
        double[] ke = Hex8Element.CreateKe(h, nu);

        double gamma = 0.001;
        // Displacement u_x = gamma * y, u_y = 0, u_z = 0
        // Nodes with y=1: 2, 3, 6, 7
        double[] u = new double[24];
        int[] nodesY1 = { 2, 3, 6, 7 };
        foreach (int a in nodesY1)
        {
            u[3 * a + 0] = gamma * h;
        }

        double[] f = Multiply(ke, u);

        // Analytic pure shear: τ_xy = G * gamma = E / (2*(1+ν)) * gamma
        double g = e / (2.0 * (1.0 + nu));
        double tauXY = g * gamma;

        double faceAreaQuarter = (h * h) / 4.0;
        double forceExpected = tauXY * faceAreaQuarter;

        // On face y=h (nodes 2, 3, 6, 7), traction is in +X direction: +forceExpected
        foreach (int a in nodesY1)
            Assert.True(Math.Abs(f[3 * a + 0] - forceExpected) < 1e-12, $"Node {a} Fx: expected {forceExpected}, got {f[3 * a + 0]}");

        // On face y=0 (nodes 0, 1, 4, 5), traction is in -X direction: -forceExpected
        int[] nodesY0 = { 0, 1, 4, 5 };
        foreach (int a in nodesY0)
            Assert.True(Math.Abs(f[3 * a + 0] - (-forceExpected)) < 1e-12, $"Node {a} Fx: expected {-forceExpected}, got {f[3 * a + 0]}");

        // On face x=h (nodes 1, 2, 5, 6), traction is in +Y direction: +forceExpected
        int[] nodesX1 = { 1, 2, 5, 6 };
        foreach (int a in nodesX1)
            Assert.True(Math.Abs(f[3 * a + 1] - forceExpected) < 1e-12, $"Node {a} Fy: expected {forceExpected}, got {f[3 * a + 1]}");

        // On face x=0 (nodes 0, 3, 4, 7), traction is in -Y direction: -forceExpected
        int[] nodesX0 = { 0, 3, 4, 7 };
        foreach (int a in nodesX0)
            Assert.True(Math.Abs(f[3 * a + 1] - (-forceExpected)) < 1e-12, $"Node {a} Fy: expected {-forceExpected}, got {f[3 * a + 1]}");

        // All Z forces must be zero
        for (int a = 0; a < 8; a++)
            Assert.True(Math.Abs(f[3 * a + 2]) < 1e-12, $"Node {a} Fz: expected 0, got {f[3 * a + 2]}");
    }

    [Fact]
    public void Ke_ScalingLaw_KeH_Equals_H_Times_Ke1()
    {
        double nu = 0.28;
        double[] ke1 = Hex8Element.CreateKe(1.0, nu);

        double[] hValues = { 0.5, 2.0, 3.5, 10.0 };
        foreach (double h in hValues)
        {
            double[] keH = Hex8Element.CreateKe(h, nu);
            for (int i = 0; i < 576; i++)
            {
                double expected = h * ke1[i];
                double diff = Math.Abs(keH[i] - expected);
                Assert.True(diff < 1e-12, $"Ke scaling failed for h={h} at index {i}: diff={diff}");
            }
        }
    }

    private static double[] Multiply(double[] m, double[] v)
    {
        double[] res = new double[24];
        for (int i = 0; i < 24; i++)
        {
            double sum = 0;
            for (int j = 0; j < 24; j++)
            {
                sum += m[i * 24 + j] * v[j];
            }
            res[i] = sum;
        }
        return res;
    }
}

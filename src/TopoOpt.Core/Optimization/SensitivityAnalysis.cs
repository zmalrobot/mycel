using System;
using System.Threading.Tasks;
using TopoOpt.Core.Fem;

namespace TopoOpt.Core.Optimization;

public static class SensitivityAnalysis
{
    /// <summary>
    /// Computes compliance sensitivities with respect to physical (filtered) densities:
    /// dc/drhoBar_e = -p * rhoBar_e^(p - 1) * (E0 - Emin) * (u_e^T * Ke0 * u_e)
    /// </summary>
    public static unsafe void ComputeComplianceSensitivities(
        FeModel model,
        FemOperator op,
        ReadOnlySpan<double> u,
        ReadOnlySpan<double> rhoPhysical,
        Span<double> dcDrhoPhysical,
        double penal)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(op);

        int activeCount = model.ActiveElementCount;
        if (rhoPhysical.Length < activeCount || dcDrhoPhysical.Length < activeCount)
            throw new ArgumentException("Density spans must have length >= ActiveElementCount.");

        double e0 = model.Material.E0;
        double emin = model.Material.Emin;
        double dE = e0 - emin;
        double[] ke0 = op.Ke;

        int nx = model.Grid.Nx;
        int ny = model.Grid.Ny;
        int nx1 = nx + 1;
        int ny1 = ny + 1;
        int[] activeElements = model.ActiveElements;

        fixed (double* pU = u)
        fixed (double* pRho = rhoPhysical)
        fixed (double* pDc = dcDrhoPhysical)
        fixed (double* pKe = ke0)
        {
            if (activeCount > 500)
            {
                nint ptrU = (nint)pU;
                nint ptrRho = (nint)pRho;
                nint ptrDc = (nint)pDc;
                nint ptrKe = (nint)pKe;

                Parallel.For(0, activeCount, k =>
                {
                    double* localU = (double*)ptrU;
                    double* localRho = (double*)ptrRho;
                    double* localDc = (double*)ptrDc;
                    double* localKe = (double*)ptrKe;

                    int e = activeElements[k];
                    model.Grid.InverseElementIndex(e, out int ix, out int iy, out int iz);

                    int n0 = ix + nx1 * (iy + ny1 * iz);
                    int n1 = n0 + 1;
                    int n3 = n0 + nx1;
                    int n2 = n3 + 1;
                    int n4 = n0 + nx1 * ny1;
                    int n5 = n4 + 1;
                    int n7 = n4 + nx1;
                    int n6 = n7 + 1;

                    Span<int> nodes = stackalloc int[8] { n0, n1, n2, n3, n4, n5, n6, n7 };
                    Span<double> ue = stackalloc double[24];
                    for (int a = 0; a < 8; a++)
                    {
                        int dof0 = 3 * nodes[a];
                        ue[3 * a + 0] = localU[dof0 + 0];
                        ue[3 * a + 1] = localU[dof0 + 1];
                        ue[3 * a + 2] = localU[dof0 + 2];
                    }

                    // Strain energy: ue^T * Ke0 * ue
                    double strainEnergy = 0.0;
                    for (int r = 0; r < 24; r++)
                    {
                        double sum = 0.0;
                        int rowOffset = r * 24;
                        for (int c = 0; c < 24; c++)
                        {
                            sum += localKe[rowOffset + c] * ue[c];
                        }
                        strainEnergy += ue[r] * sum;
                    }

                    double rho = localRho[k];
                    double dE_dRho = penal * Math.Pow(rho, penal - 1.0) * dE;
                    localDc[k] = -dE_dRho * strainEnergy;
                });
            }
            else
            {
                Span<int> nodes = stackalloc int[8];
                Span<double> ue = stackalloc double[24];

                for (int k = 0; k < activeCount; k++)
                {
                    int e = activeElements[k];
                    model.Grid.InverseElementIndex(e, out int ix, out int iy, out int iz);

                    int n0 = ix + nx1 * (iy + ny1 * iz);
                    int n1 = n0 + 1;
                    int n3 = n0 + nx1;
                    int n2 = n3 + 1;
                    int n4 = n0 + nx1 * ny1;
                    int n5 = n4 + 1;
                    int n7 = n4 + nx1;
                    int n6 = n7 + 1;

                    nodes[0] = n0; nodes[1] = n1; nodes[2] = n2; nodes[3] = n3;
                    nodes[4] = n4; nodes[5] = n5; nodes[6] = n6; nodes[7] = n7;

                    for (int a = 0; a < 8; a++)
                    {
                        int dof0 = 3 * nodes[a];
                        ue[3 * a + 0] = pU[dof0 + 0];
                        ue[3 * a + 1] = pU[dof0 + 1];
                        ue[3 * a + 2] = pU[dof0 + 2];
                    }

                    double strainEnergy = 0.0;
                    for (int r = 0; r < 24; r++)
                    {
                        double sum = 0.0;
                        int rowOffset = r * 24;
                        for (int c = 0; c < 24; c++)
                        {
                            sum += pKe[rowOffset + c] * ue[c];
                        }
                        strainEnergy += ue[r] * sum;
                    }

                    double rho = pRho[k];
                    double dE_dRho = penal * Math.Pow(rho, penal - 1.0) * dE;
                    pDc[k] = -dE_dRho * strainEnergy;
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mycel.Core.Voxelization;

namespace Mycel.Core.Optimization;

/// <summary>
/// Density filter for regular structured Hex8 grids:
/// H_ei = max(0, rmin - dist(e, i)) in element units.
/// Row-normalized: Hs_e = sum_i H_ei.
/// W_ei = H_ei / Hs_e.
/// S_i = sum_e W_ei.
/// </summary>
public sealed class DensityFilter
{
    public GridSpec Grid { get; }
    public double FilterRadiusInElements { get; }
    public int ActiveCount { get; }
    public double[] S { get; }

    // CSR representation of W (forward)
    private readonly int[] _forwardRowOffsets;
    private readonly int[] _forwardCols;
    private readonly double[] _forwardWeights;

    // CSR representation of W^T (adjoint / transpose)
    private readonly int[] _adjointRowOffsets;
    private readonly int[] _adjointCols;
    private readonly double[] _adjointWeights;

    private readonly ParallelOptions _parallelOptions;

    public DensityFilter(GridSpec grid, byte[] domain, int[] activeElements, double filterRadiusInElements)
    {
        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(activeElements);

        FilterRadiusInElements = filterRadiusInElements;
        ActiveCount = activeElements.Length;
        S = new double[ActiveCount];

        _parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        int nx = grid.Nx;
        int ny = grid.Ny;
        int nz = grid.Nz;
        int totalElements = (int)grid.TotalElements;

        int[] elemToActive = new int[totalElements];
        Array.Fill(elemToActive, -1);
        for (int i = 0; i < ActiveCount; i++)
        {
            elemToActive[activeElements[i]] = i;
        }

        int rCeil = (int)Math.Ceiling(filterRadiusInElements);
        double rSq = filterRadiusInElements * filterRadiusInElements;

        // Build forward rows in temporary lists
        List<int>[] rowCols = new List<int>[ActiveCount];
        List<double>[] rowWeights = new List<double>[ActiveCount];
        int totalNonZeros = 0;

        for (int eIdx = 0; eIdx < ActiveCount; eIdx++)
        {
            int e = activeElements[eIdx];
            grid.InverseElementIndex(e, out int ex, out int ey, out int ez);

            var cols = new List<int>();
            var weights = new List<double>();
            double hs = 0.0;

            int minX = Math.Max(0, ex - rCeil);
            int maxX = Math.Min(nx - 1, ex + rCeil);
            int minY = Math.Max(0, ey - rCeil);
            int maxY = Math.Min(ny - 1, ey + rCeil);
            int minZ = Math.Max(0, ez - rCeil);
            int maxZ = Math.Min(nz - 1, ez + rCeil);

            for (int jz = minZ; jz <= maxZ; jz++)
            {
                int dz = jz - ez;
                int dzSq = dz * dz;
                for (int jy = minY; jy <= maxY; jy++)
                {
                    int dy = jy - ey;
                    int dyzSq = dzSq + dy * dy;
                    for (int jx = minX; jx <= maxX; jx++)
                    {
                        int dx = jx - ex;
                        int distSq = dyzSq + dx * dx;
                        if (distSq < rSq)
                        {
                            int nbrElem = jx + nx * (jy + ny * jz);
                            if (domain[nbrElem] != 0)
                            {
                                int nbrIdx = elemToActive[nbrElem];
                                if (nbrIdx >= 0)
                                {
                                    double dist = Math.Sqrt(distSq);
                                    if (dist < filterRadiusInElements)
                                    {
                                        double weight = filterRadiusInElements - dist;
                                        cols.Add(nbrIdx);
                                        weights.Add(weight);
                                        hs += weight;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Row normalize
            double invHs = hs > 0.0 ? 1.0 / hs : 1.0;
            for (int k = 0; k < weights.Count; k++)
            {
                weights[k] *= invHs;
            }

            rowCols[eIdx] = cols;
            rowWeights[eIdx] = weights;
            totalNonZeros += cols.Count;
        }

        // Pack forward CSR
        _forwardRowOffsets = new int[ActiveCount + 1];
        _forwardCols = new int[totalNonZeros];
        _forwardWeights = new double[totalNonZeros];

        int offset = 0;
        for (int i = 0; i < ActiveCount; i++)
        {
            _forwardRowOffsets[i] = offset;
            var cols = rowCols[i];
            var weights = rowWeights[i];
            for (int k = 0; k < cols.Count; k++)
            {
                _forwardCols[offset] = cols[k];
                _forwardWeights[offset] = weights[k];
                offset++;
            }
        }
        _forwardRowOffsets[ActiveCount] = offset;

        // Build adjoint CSR (transpose of W)
        // 1. Count entries per column
        int[] adjointCounts = new int[ActiveCount];
        for (int i = 0; i < totalNonZeros; i++)
        {
            adjointCounts[_forwardCols[i]]++;
        }

        _adjointRowOffsets = new int[ActiveCount + 1];
        int adjOffset = 0;
        for (int i = 0; i < ActiveCount; i++)
        {
            _adjointRowOffsets[i] = adjOffset;
            adjOffset += adjointCounts[i];
        }
        _adjointRowOffsets[ActiveCount] = adjOffset;

        _adjointCols = new int[totalNonZeros];
        _adjointWeights = new double[totalNonZeros];

        int[] currentAdjointOffsets = (int[])_adjointRowOffsets.Clone();
        for (int r = 0; r < ActiveCount; r++)
        {
            int start = _forwardRowOffsets[r];
            int end = _forwardRowOffsets[r + 1];
            for (int idx = start; idx < end; idx++)
            {
                int c = _forwardCols[idx];
                double w = _forwardWeights[idx];
                int targetIdx = currentAdjointOffsets[c]++;
                _adjointCols[targetIdx] = r;
                _adjointWeights[targetIdx] = w;
            }
        }

        // Precompute S_i = sum_e W_ei = sum over adjoint row i
        for (int i = 0; i < ActiveCount; i++)
        {
            int start = _adjointRowOffsets[i];
            int end = _adjointRowOffsets[i + 1];
            double sum = 0.0;
            for (int idx = start; idx < end; idx++)
            {
                sum += _adjointWeights[idx];
            }
            S[i] = sum;
        }
    }

    /// <summary>
    /// Evaluates forward density filter rhoFiltered = W * rho.
    /// Completely zero-allocation in hot loops.
    /// </summary>
    public unsafe void ApplyFilter(ReadOnlySpan<double> rhoIn, Span<double> rhoFilteredOut)
    {
        if (rhoIn.Length < ActiveCount || rhoFilteredOut.Length < ActiveCount)
            throw new ArgumentException("Buffer lengths must be at least ActiveCount.");

        int activeCount = ActiveCount;
        int[] rowOffsets = _forwardRowOffsets;
        int[] cols = _forwardCols;
        double[] weights = _forwardWeights;

        fixed (double* pIn = rhoIn)
        fixed (double* pOut = rhoFilteredOut)
        {
            if (activeCount > 500)
            {
                nint ptrIn = (nint)pIn;
                nint ptrOut = (nint)pOut;
                Parallel.For(0, activeCount, _parallelOptions, r =>
                {
                    double* inPtr = (double*)ptrIn;
                    double* outPtr = (double*)ptrOut;
                    int start = rowOffsets[r];
                    int end = rowOffsets[r + 1];
                    double sum = 0.0;
                    for (int idx = start; idx < end; idx++)
                    {
                        sum += weights[idx] * inPtr[cols[idx]];
                    }
                    outPtr[r] = sum;
                });
            }
            else
            {
                for (int r = 0; r < activeCount; r++)
                {
                    int start = rowOffsets[r];
                    int end = rowOffsets[r + 1];
                    double sum = 0.0;
                    for (int idx = start; idx < end; idx++)
                    {
                        sum += weights[idx] * pIn[cols[idx]];
                    }
                    pOut[r] = sum;
                }
            }
        }
    }

    /// <summary>
    /// Evaluates adjoint sensitivity filter dcFiltered = W^T * dc.
    /// Completely zero-allocation in hot loops.
    /// </summary>
    public unsafe void ApplySensitivityFilter(ReadOnlySpan<double> dcIn, Span<double> dcFilteredOut)
    {
        if (dcIn.Length < ActiveCount || dcFilteredOut.Length < ActiveCount)
            throw new ArgumentException("Buffer lengths must be at least ActiveCount.");

        int activeCount = ActiveCount;
        int[] rowOffsets = _adjointRowOffsets;
        int[] cols = _adjointCols;
        double[] weights = _adjointWeights;

        fixed (double* pIn = dcIn)
        fixed (double* pOut = dcFilteredOut)
        {
            if (activeCount > 500)
            {
                nint ptrIn = (nint)pIn;
                nint ptrOut = (nint)pOut;
                Parallel.For(0, activeCount, _parallelOptions, r =>
                {
                    double* inPtr = (double*)ptrIn;
                    double* outPtr = (double*)ptrOut;
                    int start = rowOffsets[r];
                    int end = rowOffsets[r + 1];
                    double sum = 0.0;
                    for (int idx = start; idx < end; idx++)
                    {
                        sum += weights[idx] * inPtr[cols[idx]];
                    }
                    outPtr[r] = sum;
                });
            }
            else
            {
                for (int r = 0; r < activeCount; r++)
                {
                    int start = rowOffsets[r];
                    int end = rowOffsets[r + 1];
                    double sum = 0.0;
                    for (int idx = start; idx < end; idx++)
                    {
                        sum += weights[idx] * pIn[cols[idx]];
                    }
                    pOut[r] = sum;
                }
            }
        }
    }
}

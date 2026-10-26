using System;
using System.Collections.Generic;

namespace TopoOpt.Core.Voxelization;

public static class Connectivity
{
    /// <summary>
    /// Labels 26-connected components of solid voxels (Domain[e] == 1).
    /// If keepOnlyLargest is true, all solid voxels outside the largest component are cleared (set to 0).
    /// Returns the list of component infos and any warnings generated.
    /// </summary>
    public static (List<ComponentInfo> Components, List<string> Warnings) AnalyzeAndFilter(
        GridSpec grid,
        byte[] domain,
        bool keepOnlyLargest = true)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(domain);

        int nx = grid.Nx;
        int ny = grid.Ny;
        int nz = grid.Nz;
        int totalElements = (int)grid.TotalElements;

        int[] labels = new int[totalElements]; // 0 = unvisited/void, >0 = component id
        List<List<int>> componentElements = new();
        int currentLabel = 0;

        int[] queue = new int[totalElements];

        for (int e = 0; e < totalElements; e++)
        {
            if (domain[e] == 0 || labels[e] != 0)
                continue;

            currentLabel++;
            int compId = currentLabel;
            List<int> currentComp = new();

            int qHead = 0;
            int qTail = 0;

            queue[qTail++] = e;
            labels[e] = compId;

            while (qHead < qTail)
            {
                int curr = queue[qHead++];
                currentComp.Add(curr);

                grid.ElementCoords(curr, out int ix, out int iy, out int iz);

                // 26 neighbours (all adjacent voxels sharing faces, edges, or vertices)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nzCoord = iz + dz;
                    if ((uint)nzCoord >= (uint)nz) continue;
                    int zOffset = nzCoord * ny;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nyCoord = iy + dy;
                        if ((uint)nyCoord >= (uint)ny) continue;
                        int yzOffset = (zOffset + nyCoord) * nx;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0) continue;
                            int nxCoord = ix + dx;
                            if ((uint)nxCoord >= (uint)nx) continue;

                            int nbrIdx = yzOffset + nxCoord;
                            if (domain[nbrIdx] == 1 && labels[nbrIdx] == 0)
                            {
                                labels[nbrIdx] = compId;
                                queue[qTail++] = nbrIdx;
                            }
                        }
                    }
                }
            }

            componentElements.Add(currentComp);
        }

        List<ComponentInfo> components = new(componentElements.Count);
        double voxelVolume = Math.Pow(grid.H, 3);

        for (int i = 0; i < componentElements.Count; i++)
        {
            components.Add(new ComponentInfo(i + 1, componentElements[i].Count, componentElements[i].Count * voxelVolume));
        }

        // Sort descending by size
        components.Sort((a, b) => b.VoxelCount.CompareTo(a.VoxelCount));

        List<string> warnings = new();

        if (keepOnlyLargest && componentElements.Count > 1)
        {
            int largestCount = components[0].VoxelCount;
            // Identify which original label corresponds to largest
            int largestOriginalIndex = -1;
            for (int i = 0; i < componentElements.Count; i++)
            {
                if (componentElements[i].Count == largestCount)
                {
                    largestOriginalIndex = i;
                    break;
                }
            }

            int droppedVoxels = 0;
            for (int i = 0; i < componentElements.Count; i++)
            {
                if (i == largestOriginalIndex)
                    continue;

                var comp = componentElements[i];
                droppedVoxels += comp.Count;
                for (int j = 0; j < comp.Count; j++)
                {
                    domain[comp[j]] = 0;
                }
            }

            warnings.Add(
                $"Mesh connectivity: found {componentElements.Count} disconnected components. " +
                $"Kept largest component ({largestCount:N0} voxels) and dropped {droppedVoxels:N0} disconnected voxels.");

            // Update components list to reflect retained
            components = new List<ComponentInfo>
            {
                new ComponentInfo(1, largestCount, largestCount * voxelVolume)
            };
        }

        return (components, warnings);
    }
}


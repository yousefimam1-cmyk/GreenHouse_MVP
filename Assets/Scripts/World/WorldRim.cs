using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Opens the scene terrain onto the open world: the scene terrain (Blak_Pulse_Main "New Terrain", 200 x 200 m) has a ring
/// of ~8 m berms 4-30 m inside its edges that closed the farm in. At runtime its heightmap rim is eased down to the farm's
/// own ground level: flat (exactly that level) in the outer FlatBand, blending back into the untouched terrain by
/// BlendBand. The world tiles take their edge heights from the scene terrain's edge (WorldShape.Raw), so the seam is a
/// flat line with no step. Works on GroundLook.RuntimeData (a clone in the editor), never on the TerrainData asset.
/// Heightmap arrays are [row = z, column = x], normalised 0..1 of terrainData.size.y (TerrainData.GetHeights/SetHeights).
/// </summary>
public static class WorldRim
{
    /// <summary>Metres from the edge that end up exactly at the rim level / that are blended (beyond: untouched).</summary>
    public const float FlatBand = 32f, BlendBand = 42f;

    private static readonly HashSet<TerrainData> opened = new HashSet<TerrainData>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => opened.Clear();

    /// <summary>Rim level of the last opened terrain (metres above the terrain's own y).</summary>
    public static float RimHeight { get; private set; }
    /// <summary>Height samples changed by the last Open.</summary>
    public static int Changed { get; private set; }

    public static bool IsOpen(Terrain t) => t != null && t.terrainData != null && opened.Contains(t.terrainData);

    /// <summary>Flatten the rim of t (once per terrain). Returns true when the heightmap was changed now.</summary>
    public static bool Open(Terrain t)
    {
        if (t == null || t.terrainData == null || opened.Contains(t.terrainData)) return false;
        var data = GroundLook.RuntimeData(t);
        if (data == null) return false;
        int res = data.heightmapResolution;
        Vector3 size = data.size;
        if (res < 3 || size.x < 1f || size.z < 1f || size.y <= 0f) { opened.Add(data); return false; }
        var h = data.GetHeights(0, 0, res, res);   // [z, x]
        float sx = size.x / (res - 1), sz = size.z / (res - 1);
        float flat = Mathf.Min(FlatBand, Mathf.Min(size.x, size.z) * 0.2f), band = Mathf.Min(BlendBand, Mathf.Min(size.x, size.z) * 0.3f);
        // the farm's ground level just inside the rim: median of the ring between band and band + 12 m
        var ring = new List<float>();
        for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                float d = Edge(ix * sx, iz * sz, size);
                if (d >= band && d < band + 12f) ring.Add(h[iz, ix]);
            }
        float level;
        if (ring.Count > 0) { ring.Sort(); level = ring[ring.Count / 2]; }
        else level = h[res / 2, res / 2];
        int changed = 0;
        for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                float d = Edge(ix * sx, iz * sz, size);
                if (d >= band) continue;
                float w = d <= flat ? 1f : 1f - WorldLayout.Smooth01((d - flat) / Mathf.Max(0.01f, band - flat));
                float nh = Mathf.Lerp(h[iz, ix], level, w);
                if (Mathf.Abs(nh - h[iz, ix]) > 1e-6f) { h[iz, ix] = nh; changed++; }
            }
        data.SetHeights(0, 0, h);
        opened.Add(data);
        RimHeight = level * size.y;
        Changed = changed;
        // the look painted rock on the old berm slopes: repaint the splat map for the new shape
        if (GroundLook.LastBiome.HasValue && data.alphamapLayers >= 3) GroundLook.Paint(data, t.transform.position, GroundLook.LastBiome.Value);
        return true;
    }

    private static float Edge(float x, float z, Vector3 size) => Mathf.Min(Mathf.Min(x, size.x - x), Mathf.Min(z, size.z - z));
}

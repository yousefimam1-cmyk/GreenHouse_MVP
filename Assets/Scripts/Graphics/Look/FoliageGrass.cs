using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Instanced grass: thousands of tufts drawn with Graphics.RenderMeshInstanced (no GameObjects), in 32 m chunks that
/// are frustum-culled, drawn only near the camera and thinned out toward the draw distance. Two materials (green /
/// dry, HDRP/Lit cut-out) x four tuft meshes. Draw distance follows the quality preset; how much grass is
/// added follows Settings > Graphics > Foliage (Vegetation.Scatter / ScatterGrass). No shadows are cast (cheap),
/// shadows are received.
/// </summary>
public class FoliageGrass : MonoBehaviour
{
    public const int MaxInstances = 60000;
    public const float ChunkSize = 32f;
    private const int Batch = 1023;     // instances per draw call (safe on every platform)

    public static FoliageGrass Instance { get; private set; }

    private class Chunk
    {
        public Bounds bounds;
        public bool boundsSet;
        // [dry 0/1][tuft 0..3]
        public readonly List<Matrix4x4>[] lists = new List<Matrix4x4>[8];
        public readonly Matrix4x4[][] arrays = new Matrix4x4[8][];
        public bool dirty = true;
        public int count;
    }

    private readonly Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
    private Material green, dry;
    private Plane[] planes = new Plane[6];

    /// <summary>Tufts registered (all chunks).</summary>
    public int InstanceCount { get; private set; }
    /// <summary>Tufts submitted for drawing in the last frame (after culling and distance thinning).</summary>
    public int DrawnLastFrame { get; private set; }
    /// <summary>Draw calls issued in the last frame.</summary>
    public int BatchesLastFrame { get; private set; }

    /// <summary>Grass draw distance in metres for the current preset (Low 35 .. Ultra 140).</summary>
    public static float DrawDistance
    {
        get
        {
            int p = GraphicsEnhancer.Preset;
            return p <= 0 ? 35f : p == 1 ? 60f : p == 2 ? 95f : 140f;
        }
    }

    public static FoliageGrass Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("Mazraa Grass");
        Instance = go.AddComponent<FoliageGrass>();
        return Instance;
    }

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Remove every tuft (the dressing rebuilds after a preset / foliage / map change).</summary>
    public void Clear()
    {
        chunks.Clear();
        InstanceCount = 0;
    }

    /// <summary>Remove the tufts inside an area (the world generator re-dressing one tile).</summary>
    public int ClearArea(Bounds area)
    {
        int removed = 0;
        foreach (var c in chunks.Values)
        {
            if (!c.bounds.Intersects(area)) continue;
            for (int i = 0; i < c.lists.Length; i++)
            {
                var l = c.lists[i];
                if (l == null) continue;
                int before = l.Count;
                l.RemoveAll(m => { Vector3 p = m.GetColumn(3); return p.x >= area.min.x && p.x <= area.max.x && p.z >= area.min.z && p.z <= area.max.z; });
                removed += before - l.Count;
            }
            c.dirty = true;
        }
        InstanceCount -= removed;
        return removed;
    }

    public void Add(Vector3 pos, bool isDry, float scale, int seed)
    {
        if (InstanceCount >= MaxInstances) return;
        long key = Key(pos);
        Chunk c;
        if (!chunks.TryGetValue(key, out c)) { c = new Chunk(); chunks[key] = c; }
        int tuft = ((seed % 4) + 4) % 4;
        int slot = (isDry ? 4 : 0) + tuft;
        if (c.lists[slot] == null) c.lists[slot] = new List<Matrix4x4>();
        float yaw = ((seed >> 3) & 1023) / 1023f * 360f;
        var m = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(scale, scale * (0.85f + ((seed >> 13) & 15) / 40f), scale));
        // insert at a pseudo-random index: drawing a prefix of the list is then a random subset (distance thinning)
        var list = c.lists[slot];
        int at = list.Count == 0 ? 0 : (int)((uint)(seed * 2654435761u) % (uint)(list.Count + 1));
        if (at >= list.Count) list.Add(m); else { list.Add(list[at]); list[at] = m; }
        Vector3 half = new Vector3(1.2f, 1.2f, 1.2f) * scale;
        if (!c.boundsSet) { c.bounds = new Bounds(pos, half * 2f); c.boundsSet = true; }
        else c.bounds.Encapsulate(new Bounds(pos, half * 2f));
        c.dirty = true;
        c.count++;
        InstanceCount++;
    }

    private static long Key(Vector3 p)
    {
        long x = Mathf.FloorToInt(p.x / ChunkSize), z = Mathf.FloorToInt(p.z / ChunkSize);
        return (x << 32) ^ (z & 0xffffffffL);
    }

    private void EnsureMaterials()
    {
        if (green == null) green = Vegetation.GrassMaterial(false);
        if (dry == null) dry = Vegetation.GrassMaterial(true);
    }

    /// <summary>The camera the player is looking through (vehicle / third person / first person).</summary>
    public static Camera ViewCamera()
    {
        var main = Camera.main;
        if (main != null && main.isActiveAndEnabled) return main;
        Camera best = null;
        foreach (var c in Camera.allCameras)
            if (c != null && !c.orthographic && c.targetTexture == null && (best == null || c.depth > best.depth)) best = c;
        return best;
    }

    private void Update()
    {
        DrawnLastFrame = 0; BatchesLastFrame = 0;
        if (InstanceCount == 0) return;
        var cam = ViewCamera();
        if (cam == null) return;
        EnsureMaterials();
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        Vector3 eye = cam.transform.position;
        float far = DrawDistance, near = far * 0.45f;
        foreach (var c in chunks.Values)
        {
            if (c.count == 0) continue;
            float d = Mathf.Sqrt(c.bounds.SqrDistance(eye));
            if (d > far) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, c.bounds)) continue;
            float keep = d <= near ? 1f : Mathf.Clamp01(1f - (d - near) / (far - near));
            if (c.dirty) { for (int i = 0; i < c.lists.Length; i++) c.arrays[i] = c.lists[i] != null ? c.lists[i].ToArray() : null; c.dirty = false; }
            for (int slot = 0; slot < c.arrays.Length; slot++)
            {
                var arr = c.arrays[slot];
                if (arr == null || arr.Length == 0) continue;
                int n = Mathf.CeilToInt(arr.Length * keep);
                if (n <= 0) continue;
                var mesh = FoliageMeshes.GrassTuft(slot % 4);
                var rp = new RenderParams(slot >= 4 ? dry : green)
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    worldBounds = c.bounds,
                };
                for (int start = 0; start < n; start += Batch)
                {
                    int count = Mathf.Min(Batch, n - start);
                    Graphics.RenderMeshInstanced(rp, mesh, 0, arr, count, start);
                    BatchesLastFrame++;
                }
                DrawnLastFrame += n;
            }
        }
    }
}

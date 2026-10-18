using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Crops on the FarmCore fields, drawn with Graphics.RenderMeshInstanced (no GameObjects): per field chunk (16 x 16 cells) one
/// instance list per crop and growth stage (seedling, young, grown, ripe), rebuilt only when cells of that chunk change.
/// Meshes are the FarmCrops stage models (CropVisuals), else a tinted tuft. Chunks are frustum-culled and drawn up to a
/// distance that follows the graphics preset; plants per cell thin out with the preset too.
/// </summary>
public class FarmFieldRenderer : MonoBehaviour
{
    public static FarmFieldRenderer Instance { get; private set; }
    private const int Batch = 1023;

    private class Chunk
    {
        public Bounds bounds;
        public bool dirty = true;
        public readonly Dictionary<int, List<Matrix4x4>> lists = new Dictionary<int, List<Matrix4x4>>();
        public readonly Dictionary<int, Matrix4x4[]> arrays = new Dictionary<int, Matrix4x4[]>();
        public int count;
    }

    private class Look { public Mesh mesh; public Material[] mats; }

    private readonly Dictionary<FarmField, Chunk[]> fields = new Dictionary<FarmField, Chunk[]>();
    private readonly Dictionary<int, Look> looks = new Dictionary<int, Look>();
    private readonly Plane[] planes = new Plane[6];

    public int InstanceCount { get; private set; }
    public int DrawnLastFrame { get; private set; }
    public int BatchesLastFrame { get; private set; }

    public static float DrawDistance { get { int p = GraphicsEnhancer.Preset; return p <= 0 ? 70f : p == 1 ? 110f : p == 2 ? 160f : 240f; } }
    /// <summary>Plants per cell edge (2 = four plants per 2 m cell).</summary>
    public static int PlantsPerEdge => GraphicsEnhancer.Preset <= 0 ? 1 : 2;

    public static FarmFieldRenderer Ensure()
    {
        if (Instance != null) return Instance;
        Instance = new GameObject("FarmCore Crops").AddComponent<FarmFieldRenderer>();
        return Instance;
    }

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void MarkDirty(FarmField f, int chunk)
    {
        var arr = Chunks(f);
        if (chunk >= 0 && chunk < arr.Length) arr[chunk].dirty = true;
    }

    public void Forget(FarmField f) { if (f != null) fields.Remove(f); }

    private Chunk[] Chunks(FarmField f)
    {
        Chunk[] arr;
        if (fields.TryGetValue(f, out arr)) return arr;
        arr = new Chunk[f.chunksX * f.chunksZ];
        for (int i = 0; i < arr.Length; i++) arr[i] = new Chunk();
        fields[f] = arr;
        return arr;
    }

    /// <summary>Rebuild the instance lists of one chunk from its cells.</summary>
    private void Rebuild(FarmField f, int c, Chunk ch)
    {
        foreach (var l in ch.lists.Values) l.Clear();
        int before = ch.count; ch.count = 0;
        int cx0 = (c % f.chunksX) * FarmField.ChunkCells, cz0 = (c / f.chunksX) * FarmField.ChunkCells;
        int n = PlantsPerEdge;
        var rot = f.Rotation;
        float step = f.cellSize / n;
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int cz = cz0; cz < Mathf.Min(f.rows, cz0 + FarmField.ChunkCells); cz++)
            for (int cx = cx0; cx < Mathf.Min(f.cols, cx0 + FarmField.ChunkCells); cx++)
            {
                int i = cz * f.cols + cx;
                Vector3 cc = f.CellCenter(i);
                minX = Mathf.Min(minX, cc.x); minY = Mathf.Min(minY, cc.y); minZ = Mathf.Min(minZ, cc.z);
                maxX = Mathf.Max(maxX, cc.x); maxY = Mathf.Max(maxY, cc.y); maxZ = Mathf.Max(maxZ, cc.z);
                if (f.ground[i] != (byte)Ground.Sown || f.crop[i] == 0 || f.Has(i, CellFlags.Withered)) continue;
                float g = Mathf.Min(1f, f.Get(FieldLayer.Growth, i));
                int stage = CropVisuals.StageIndex(g);
                int key = f.crop[i] * 8 + stage;
                List<Matrix4x4> list;
                if (!ch.lists.TryGetValue(key, out list)) ch.lists[key] = list = new List<Matrix4x4>();
                Vector3 lc = f.LocalCellCenter(i);
                for (int a = 0; a < n; a++)
                    for (int b = 0; b < n; b++)
                    {
                        float jx = (f.Hash01(i * 4 + a * 2 + b, 3) - 0.5f) * step * 0.4f, jz = (f.Hash01(i * 4 + a * 2 + b, 9) - 0.5f) * step * 0.4f;
                        Vector3 lp = lc + new Vector3((a + 0.5f) * step - f.cellSize * 0.5f + jx, 0.05f, (b + 0.5f) * step - f.cellSize * 0.5f + jz);
                        float yaw = f.Hash01(i * 4 + a * 2 + b, 17) * 360f;
                        float s = 0.85f + 0.3f * f.Hash01(i, 23 + a + b);
                        list.Add(Matrix4x4.TRS(f.center + rot * lp, rot * Quaternion.Euler(0f, yaw, 0f), Vector3.one * s * StageScale(key)));
                        ch.count++;
                    }
            }
        ch.arrays.Clear();
        foreach (var kv in ch.lists) if (kv.Value.Count > 0) ch.arrays[kv.Key] = kv.Value.ToArray();
        if (minX <= maxX)
        {
            var min = new Vector3(minX, minY, minZ); var max = new Vector3(maxX, maxY, maxZ);
            ch.bounds = new Bounds((min + max) * 0.5f + Vector3.up, (max - min) + new Vector3(f.cellSize * 2f, 3f, f.cellSize * 2f));
        }
        InstanceCount += ch.count - before;
        ch.dirty = false;
    }

    private float StageScale(int key) { var l = GetLook(key); return l != null && l.mats != null && l.mats.Length > 0 && l.mats[0] != null && l.mats[0].name.StartsWith("FarmCore tuft") ? 0.35f + 0.25f * (key % 8) : 1f; }

    private Look GetLook(int key)
    {
        Look l;
        if (looks.TryGetValue(key, out l)) return l;
        string crop = FcData.CropId((byte)(key / 8));
        float g = (key % 8) / 3f;
        l = new Look();
        Mesh mesh; Material[] mats;
        if (CropVisuals.TryGetMesh(crop, g, out mesh, out mats) && mesh != null && mats != null && mats.Length > 0)
        {
            l.mesh = mesh;
            l.mats = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++) { if (mats[i] == null) continue; l.mats[i] = new Material(mats[i]) { name = mats[i].name + " (instanced)" }; l.mats[i].enableInstancing = true; }
        }
        else
        {
            l.mesh = FoliageMeshes.GrassTuft(key % 4);
            var eco = EconomyManager.Instance;
            var def = eco != null ? eco.FieldCrop(crop) : null;
            Color c = g >= 0.99f && def != null ? new Color(def.r, def.g, def.b) : new Color(0.3f, 0.58f, 0.22f);
            var m = new Material(Build.Mat(c, 0.2f)) { name = "FarmCore tuft " + crop };
            m.enableInstancing = true;
            l.mats = new[] { m };
        }
        looks[key] = l;
        return l;
    }

    private void Update()
    {
        DrawnLastFrame = 0; BatchesLastFrame = 0;
        var ff = FarmFields.Instance;
        if (ff == null) return;
        var cam = FoliageGrass.ViewCamera();
        bool cull = cam != null;
        if (cull) GeometryUtility.CalculateFrustumPlanes(cam, planes);
        Vector3 eye = cull ? cam.transform.position : Vector3.zero;
        float far = DrawDistance;
        foreach (var f in ff.Fields)
        {
            var arr = Chunks(f);
            for (int c = 0; c < arr.Length; c++)
            {
                var ch = arr[c];
                if (ch.dirty) Rebuild(f, c, ch);
                if (ch.count == 0) continue;
                if (cull && (Mathf.Sqrt(ch.bounds.SqrDistance(eye)) > far || !GeometryUtility.TestPlanesAABB(planes, ch.bounds))) continue;
                Draw(ch);
            }
        }
    }

    private void Draw(Chunk ch)
    {
        foreach (var kv in ch.arrays)
        {
            var look = GetLook(kv.Key);
            if (look == null || look.mesh == null) continue;
            var arr = kv.Value;
            for (int sub = 0; sub < look.mats.Length && sub < Mathf.Max(1, look.mesh.subMeshCount); sub++)
            {
                if (look.mats[sub] == null) continue;
                var rp = new RenderParams(look.mats[sub]) { shadowCastingMode = GraphicsEnhancer.Preset >= 2 ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, worldBounds = ch.bounds };
                for (int start = 0; start < arr.Length; start += Batch)
                {
                    Graphics.RenderMeshInstanced(rp, look.mesh, sub, arr, Mathf.Min(Batch, arr.Length - start), start);
                    BatchesLastFrame++;
                }
            }
            DrawnLastFrame += arr.Length;
        }
    }
}

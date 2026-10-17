using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

/// <summary>
/// Al-Qassim desert look, built in code once the farm exists: sand-coloured ground around the farm edges, low dunes
/// along the map edges, a compacted-sand track from the loading bay to the market lined with date palms, a palm grove,
/// scattered acacia and sidr trees, a Najdi mud-brick farmhouse near the start and evaporative cooling pads on the
/// greenhouses. Nothing is placed on or near plots, zones, the pen, greenhouses, interactables or any scene collider
/// (keep-out circles are collected at runtime). Prop count follows GraphicsEnhancer.DressingDensity and is rebuilt
/// when the preset changes. The layout uses a fixed seed so it is the same every session.
/// </summary>
[MazraaSystem(70)]
public class DesertDressing : MonoBehaviour
{
    public static DesertDressing Instance { get; private set; }

    public static readonly Color SandColor = new Color(0.84f, 0.7f, 0.5f);
    public static readonly Color DuneColor = new Color(0.87f, 0.74f, 0.53f);
    public static readonly Color TrackColor = new Color(0.72f, 0.6f, 0.44f);

    public int Seed = 2026;
    public Vector3 FarmCenter { get; private set; }
    public float FarmRadius { get; private set; }
    public Vector3 HousePosition { get; private set; }
    public bool HasHouse { get; private set; }
    public readonly List<Vector3> Track = new List<Vector3>();
    public int Palms { get; private set; }
    public int Trees { get; private set; }
    public int Dunes { get; private set; }
    public int CoolingWalls { get; private set; }
    public int SandCells { get; private set; }
    public int MergedParts { get; private set; }
    /// <summary>Nile Delta look (RegionLook): canal / ditch water strips beside the fields, eucalyptus windbreak trees,
    /// sell-point buildings at the market (cotton gin, rice mill, souq) and the pigeon tower by the house.</summary>
    public int Canals { get; private set; }
    public int Eucalyptus { get; private set; }
    public int SellPoints { get; private set; }
    public bool HasPigeonTower { get; private set; }
    /// <summary>Map whose look was built last.</summary>
    public string BuiltRegion { get; private set; } = "";
    /// <summary>Ground colour of the current build (sand in Najd, green-brown alluvium on the Delta).</summary>
    public Color GroundColor { get; private set; } = SandColor;
    /// <summary>Graphics makeover: desert shrubs / dry clumps and instanced grass tufts placed by the last build.</summary>
    public int Shrubs { get; private set; }
    public int GrassTufts { get; private set; }

    private struct KeepOut { public Vector3 p; public float r; public bool major; public KeepOut(Vector3 p, float r, bool major) { this.p = p; this.r = r; this.major = major; } }
    private readonly List<KeepOut> keep = new List<KeepOut>();
    private readonly List<Vector3> placed = new List<Vector3>();
    /// <summary>Props from this or an earlier (being destroyed) dressing are never obstacles for a rebuild.</summary>
    private static bool IsDressing(Transform t) { for (; t != null; t = t.parent) if (t.name == "Desert Dressing") return true; return false; }
    private readonly List<Vector3> farmPoints = new List<Vector3>();   // plots, pads, pen, greenhouses, start
    private Transform root;
    private Vector3 mapMin, mapMax;
    private Terrain terrain;
    private Vector3 start, fwd, right;
    private bool built;

    private void Awake() => Instance = this;

    private void Start()
    {
        GraphicsEnhancer.PresetChanged += OnPreset;
        GameEvents.RegionChanged += OnRegion;
        Invoke(nameof(BuildAll), 1.6f); // after VehicleSpawner, FieldManager and AmbientLife have placed the farm
    }

    private void OnDestroy()
    {
        GraphicsEnhancer.PresetChanged -= OnPreset;
        GameEvents.RegionChanged -= OnRegion;
        if (Instance == this) Instance = null;
    }

    private void OnPreset(int p) { if (built) BuildAll(); }
    // A career on another map: rebuild with that map's look (only when the map changed; meshes and materials are reused).
    private void OnRegion(string id) { if (built && id != BuiltRegion) BuildAll(); }

    public void BuildAll()
    {
        if (root != null) Destroy(root.gameObject);
        root = new GameObject("Desert Dressing").transform;
        root.SetParent(transform, false);
        placed.Clear(); Track.Clear();
        Palms = Trees = Dunes = CoolingWalls = SandCells = 0; HasHouse = false;
        Canals = Eucalyptus = SellPoints = 0; HasPigeonTower = false;
        Shrubs = GrassTufts = 0;
        if (FoliageGrass.Instance != null) FoliageGrass.Instance.Clear();   // grass is rebuilt with the dressing
        var rnd = new System.Random(Seed);
        float density = GraphicsEnhancer.DressingDensity;
        var look = Regions.Look;
        BuiltRegion = Regions.CurrentId;
        GroundColor = RegionLook.C(look.ground, SandColor);

        if (!built) FindStart();   // the player may be hidden in a vehicle on later rebuilds
        FindMap();
        CollectKeepOuts();
        try
        {
            if (look.canals) BuildCanals();        // first: the windbreak rows and the track keep off the water
            BuildTrack(rnd, density, RegionLook.C(look.track, TrackColor));
            BuildHouse(rnd, density);
            if (look.pigeonTower) BuildPigeonTower();
            BuildSellPoints();
            BuildCooling();
            BuildSand();
            if (look.dunes) BuildDunes(rnd, RegionLook.C(look.dune, DuneColor));
            if (look.trees == "eucalyptus" && density > 0.01f) BuildWindbreaks(rnd, density);   // Settings > Foliage 0 = no trees
            BuildTrees(rnd, density, look);
            BuildUndergrowth(new System.Random(Seed + 7), density, look);
        }
        catch (System.Exception e) { Debug.LogWarning("[DesertDressing] " + e.Message); }
        built = true;
        // hundreds of primitive parts -> a few merged meshes per material (fewer objects and draw calls)
        MergedParts = ProcMesh.MergeChildren(root, r => r.gameObject.name == "Sand" || r.gameObject.name == "Track" || r.GetComponent<TMPro.TMP_Text>() != null);
    }

    // ------------------------------------------------------------------ layout data
    private void FindStart()
    {
        var inputs = FindAnyObjectByType<StarterAssetsInputs>();
        Transform s = inputs != null ? inputs.transform : transform;
        start = s.position;
        fwd = Vector3.ProjectOnPlane(s.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        right = Vector3.Cross(Vector3.up, fwd);
    }

    private void FindMap()
    {
        terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
        {
            mapMin = terrain.transform.position;
            mapMax = mapMin + terrain.terrainData.size;
        }
        else
        {
            mapMin = start - new Vector3(130f, 0f, 130f);
            mapMax = start + new Vector3(130f, 0f, 190f);
        }
    }

    public float Ground(Vector3 p)
    {
        if (terrain != null) return terrain.SampleHeight(p) + terrain.transform.position.y;
        return Build.GroundY(p);
    }

    private void Keep(Vector3 p, float r, bool major = true) => keep.Add(new KeepOut(new Vector3(p.x, 0f, p.z), r, major));

    private void CollectKeepOuts()
    {
        keep.Clear();
        Keep(start, 8f);
        var pts = new List<Vector3> { start };
        foreach (var plot in FindObjectsByType<FieldPlot>(FindObjectsSortMode.None))
        {
            Keep(plot.transform.position, Mathf.Max(plot.size.x, plot.size.y) * 0.75f + 2.5f);
            pts.Add(plot.transform.position);
        }
        foreach (var z in FindObjectsByType<DeliveryZone>(FindObjectsSortMode.None))
        {
            Keep(z.transform.position, 11f);
            if (z != DeliveryZone.Market) pts.Add(z.transform.position);
        }
        var pen = AnimalPen.Instance;
        if (pen != null) { Keep(pen.transform.position, Mathf.Max(pen.size.x, pen.size.y) * 0.6f + 7f); pts.Add(pen.transform.position); }
        foreach (var st in FindObjectsByType<ShopStall>(FindObjectsSortMode.None)) Keep(st.transform.position, 5f);
        foreach (var v in FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) Keep(v.transform.position, 6f);
        foreach (var i in FindObjectsByType<Interactable>(FindObjectsSortMode.None)) Keep(i.transform.position, 3.5f);
        foreach (var g in FindObjectsByType<GreenhouseManager>(FindObjectsSortMode.None))
        {
            Bounds b;
            if (WorldBounds(g.gameObject, out b) && b.size.x < 120f && b.size.z < 120f) Keep(b.center, Mathf.Max(b.size.x, b.size.z) * 0.6f + 5f);
            else Keep(g.transform.position, 20f);
            pts.Add(g.transform.position);
        }
        foreach (var c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (c == null || c is TerrainCollider || c.transform.IsChildOf(transform) || IsDressing(c.transform)) continue;
            var b = c.bounds;
            float span = Mathf.Max(b.size.x, b.size.z);
            if (span > 80f) continue;                   // ground planes and other huge colliders
            Keep(b.center, span * 0.5f + 1.5f, false);
        }
        // Farm core: everything except the market
        Vector3 c0 = Vector3.zero;
        foreach (var p in pts) c0 += p;
        c0 /= pts.Count;
        float r0 = 20f;
        foreach (var p in pts) r0 = Mathf.Max(r0, Flat(p - c0).magnitude + 10f);
        FarmCenter = new Vector3(c0.x, 0f, c0.z);
        FarmRadius = r0;
        farmPoints.Clear();
        foreach (var p in pts) farmPoints.Add(Flat(p));
        if (DeliveryZone.Market != null) farmPoints.Add(Flat(DeliveryZone.Market.transform.position));
    }

    /// <summary>World bounds of every enabled renderer under go.</summary>
    public static bool WorldBounds(GameObject go, out Bounds b)
    {
        b = new Bounds(go.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else { b.Encapsulate(r.bounds.min); b.Encapsulate(r.bounds.max); }
        }
        return any;
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>True when a prop of radius r at p touches nothing important and stays on the map.</summary>
    public bool Free(Vector3 p, float r)
    {
        if (p.x < mapMin.x + 3f || p.z < mapMin.z + 3f || p.x > mapMax.x - 3f || p.z > mapMax.z - 3f) return false;
        Vector3 f = Flat(p);
        foreach (var k in keep) if ((f - k.p).sqrMagnitude < (k.r + r) * (k.r + r)) return false;
        foreach (var q in placed) if ((f - q).sqrMagnitude < (r + 2f) * (r + 2f)) return false;
        return DistanceToTrack(f) > r + 3f;
    }

    /// <summary>Flat distance to the desert track (vehicles drive on it as a dirt road).</summary>
    public float DistanceToTrack(Vector3 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i + 1 < Track.Count; i++) best = Mathf.Min(best, DistToSegment(p, Flat(Track[i]), Flat(Track[i + 1])));
        return best;
    }

    private static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a; float len2 = ab.sqrMagnitude;
        float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return (p - (a + ab * t)).magnitude;
    }

    private bool InKeepOut(Vector3 p, float pad, bool majorOnly = false)
    {
        Vector3 f = Flat(p);
        foreach (var k in keep) if ((!majorOnly || k.major) && (f - k.p).sqrMagnitude < (k.r + pad) * (k.r + pad)) return true;
        return false;
    }

    private void Mark(Vector3 p) => placed.Add(Flat(p));

    /// <summary>Distance to the nearest farm feature (plot, pad, pen, greenhouse, start, market).</summary>
    public float DistanceToFarm(Vector3 p)
    {
        Vector3 f = Flat(p);
        float best = float.MaxValue;
        foreach (var q in farmPoints) best = Mathf.Min(best, (f - q).magnitude);
        return best;
    }

    // ------------------------------------------------------------------ track + palms
    private void BuildTrack(System.Random rnd, float density, Color trackColor)
    {
        if (DeliveryZone.LoadingBay == null || DeliveryZone.Market == null) return;
        Vector3 a = Flat(DeliveryZone.LoadingBay.transform.position), b = Flat(DeliveryZone.Market.transform.position);
        // Pick an L-shaped route (bay -> side point -> market) that crosses the fewest keep-outs.
        float[] sides = { 0f, 14f, 20f, 28f, -24f, -32f, 36f };
        List<Vector3> best = null; int bestHits = int.MaxValue;
        foreach (float s in sides)
        {
            Vector3 w = a + right * s + fwd * 6f;
            var path = new List<Vector3>();
            AddSegment(path, a, w); AddSegment(path, w, b);
            int hits = 0;
            foreach (var p in path) if (InKeepOut(p, 1.5f) && Flat(p - a).magnitude > 12f && Flat(p - b).magnitude > 12f) hits++;
            if (hits < bestHits) { bestHits = hits; best = path; }
            if (hits == 0) break;
        }
        if (best == null || best.Count < 2) return;
        Track.AddRange(best);

        // Draw the track in pieces, leaving gaps where it would cover a pad, plot or building.
        var piece = new List<Vector3>();
        System.Func<Vector3, float> ground = Ground;
        foreach (var p in best)
        {
            bool blocked = InKeepOut(p, 2.5f);
            if (!blocked) piece.Add(p);
            if ((blocked || p == best[best.Count - 1]) && piece.Count >= 2) { MeshPiece("Track", ProcMesh.Strip(piece, 4.5f, 0.1f, ground), Vector3.zero, trackColor, false); piece = new List<Vector3>(); }
            else if (blocked) piece.Clear();
        }

        // Date palms along both sides of the track.
        float spacing = Mathf.Lerp(16f, 9f, density);
        float run = 0f;
        for (int i = 1; i < best.Count; i++)
        {
            Vector3 d = best[i] - best[i - 1];
            run += d.magnitude;
            if (run < spacing) continue;
            run = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, d.normalized);
            foreach (float s in new[] { -6.5f, 6.5f })
            {
                Vector3 p = best[i] + side * s + d.normalized * (float)(rnd.NextDouble() - 0.5);
                if (!FreeNearTrack(p, 2.2f)) continue;
                DesertProps.DatePalm(root, OnGround(p), rnd); Mark(p); Palms++;
            }
        }
    }

    private bool FreeNearTrack(Vector3 p, float r)
    {
        if (p.x < mapMin.x + 3f || p.z < mapMin.z + 3f || p.x > mapMax.x - 3f || p.z > mapMax.z - 3f) return false;
        if (InKeepOut(p, r)) return false;
        Vector3 f = Flat(p);
        foreach (var q in placed) if ((f - q).sqrMagnitude < (r + 2f) * (r + 2f)) return false;
        return DistanceToTrack(f) > 5f;
    }

    private static void AddSegment(List<Vector3> path, Vector3 a, Vector3 b)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt(Flat(b - a).magnitude / 3f));
        for (int i = path.Count == 0 ? 0 : 1; i <= n; i++) path.Add(Vector3.Lerp(a, b, i / (float)n));
    }

    private Vector3 OnGround(Vector3 p) => new Vector3(p.x, Ground(p), p.z);

    private GameObject MeshPiece(string name, Mesh mesh, Vector3 pos, Color color, bool collider)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.position = pos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = Build.Mat(color, 0.08f);
        // graphics makeover: real sand / soil / gravel textures, world UVs matching the terrain layers
        if (name == "Sand" || name == "Track" || name == "Dune") GroundLook.DressMesh(mr, Vegetation.BiomeOf(BuiltRegion), name == "Sand" ? "ground" : name.ToLowerInvariant());
        mr.shadowCastingMode = collider ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    // ------------------------------------------------------------------ farmhouse + grove
    private void BuildHouse(System.Random rnd, float density)
    {
        Vector3[] candidates =
        {
            start - fwd * 20f - right * 4f, start - fwd * 20f + right * 12f, start - fwd * 20f - right * 18f,
            start - right * 32f - fwd * 8f, start + right * 30f - fwd * 6f, start - fwd * 32f,
        };
        foreach (var c in candidates)
        {
            if (!Free(c, 8.5f)) continue;
            Vector3 look = Flat(start - c);
            var house = DesertProps.NajdiHouse(root, OnGround(c) - new Vector3(0f, 0.15f, 0f), Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : fwd));
            HousePosition = house.transform.position; HasHouse = true;
            Keep(c, 8.5f); Mark(c);
            // A small palm grove beside the house (the farm's shade garden).
            Vector3 side = Vector3.Cross(Vector3.up, look.normalized);
            int rows = density > 0.5f ? 3 : 2;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < 3; j++)
                {
                    Vector3 p = c + side * (12f + i * 6.5f) - look.normalized * (j * 6.5f - 3f) + new Vector3((float)rnd.NextDouble(), 0f, (float)rnd.NextDouble());
                    if (!Free(p, 2.2f)) continue;
                    DesertProps.DatePalm(root, OnGround(p), rnd); Mark(p); Palms++;
                }
            return;
        }
    }

    // ------------------------------------------------------------------ greenhouse cooling pads
    private void BuildCooling()
    {
        foreach (var g in FindObjectsByType<GreenhouseManager>(FindObjectsSortMode.None))
        {
            Bounds b;
            if (!WorldBounds(g.gameObject, out b) || b.size.x > 80f || b.size.z > 80f || b.size.x < 1f || b.size.z < 1f) continue;
            DesertProps.CoolingPads(root, b, Ground(b.center));
            CoolingWalls++;
        }
    }

    // ------------------------------------------------------------------ sand ground
    private void BuildSand()
    {
        Vector3 size = mapMax - mapMin;
        float cell = Mathf.Max(2.5f, Mathf.Max(size.x, size.z) / 200f);
        int nx = Mathf.CeilToInt(size.x / cell), nz = Mathf.CeilToInt(size.z / cell);
        var v = new List<Vector3>(); var t = new List<int>();
        var index = new Dictionary<int, int>();
        System.Func<int, int, int> vert = (ix, iz) =>
        {
            int key = ix * (nz + 1) + iz;
            int id;
            if (index.TryGetValue(key, out id)) return id;
            Vector3 p = new Vector3(mapMin.x + ix * cell, 0f, mapMin.z + iz * cell);
            p.y = Ground(p) + 0.07f;
            id = v.Count; v.Add(p); index[key] = id;
            return id;
        };
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                Vector3 c = new Vector3(mapMin.x + (ix + 0.5f) * cell, 0f, mapMin.z + (iz + 0.5f) * cell);
                float edge = 24f + 10f * Mathf.PerlinNoise(c.x * 0.05f + 3.1f, c.z * 0.05f + 7.7f);
                if (DistanceToFarm(c) < edge) continue;
                if (InKeepOut(c, cell * 0.75f, true)) continue;      // small props (fence posts) don't punch holes
                int a = vert(ix, iz), b = vert(ix, iz + 1), d = vert(ix + 1, iz), e = vert(ix + 1, iz + 1);
                t.Add(a); t.Add(b); t.Add(d); t.Add(d); t.Add(b); t.Add(e);
                SandCells++;
            }
        if (t.Count == 0) return;
        MeshPiece("Sand", ProcMesh.Finish("Sand", v, t), Vector3.zero, GroundColor, false).GetComponent<MeshRenderer>().receiveShadows = true;
    }

    // ------------------------------------------------------------------ dunes at the map edges
    private void BuildDunes(System.Random rnd, Color duneColor)
    {
        System.Func<Vector3, float> ground = Ground;
        Vector3[] corners = { new Vector3(mapMin.x, 0, mapMin.z), new Vector3(mapMax.x, 0, mapMin.z), new Vector3(mapMax.x, 0, mapMax.z), new Vector3(mapMin.x, 0, mapMax.z) };
        for (int e = 0; e < 4; e++)
        {
            Vector3 a = corners[e], b = corners[(e + 1) % 4];
            Vector3 along = (b - a).normalized;
            Vector3 inward = Vector3.Cross(Vector3.up, along);     // corners go counter-clockwise seen from above -> left is inward
            if (Vector3.Dot(inward, (mapMin + mapMax) * 0.5f - a) < 0f) inward = -inward;
            float len = (b - a).magnitude, s = 10f;
            while (s < len - 10f)
            {
                float hl = 9f + (float)rnd.NextDouble() * 9f, hw = 6f + (float)rnd.NextDouble() * 4f, h = 1.8f + (float)rnd.NextDouble() * 2.6f;
                Vector3 c = a + along * (s + hl * 0.6f) + inward * (hw * 0.6f + 1f + (float)rnd.NextDouble() * 4f);
                s += hl * 1.5f + (float)rnd.NextDouble() * 6f;
                if (InKeepOut(c, hl + 3f) || DistanceToTrack(Flat(c)) < hl + 4f || DistanceToFarm(c) < hl + 12f) continue;
                c.y = Ground(c);
                Vector3 dir = Quaternion.Euler(0f, ((float)rnd.NextDouble() - 0.5f) * 30f, 0f) * along;
                // steep lee side faces Cross(dir, up): point it toward the map edge so the gentle slope faces the farm
                if (Vector3.Dot(Vector3.Cross(dir, Vector3.up), inward) > 0f) dir = -dir;
                var mesh = ProcMesh.Dune(c, dir, hl, hw, h, ground, rnd.Next());
                MeshPiece("Dune", mesh, c, duneColor, true);
                Dunes++;
            }
        }
    }

    // ------------------------------------------------------------------ acacia + sidr
    private void BuildTrees(System.Random rnd, float density, RegionLook look)
    {
        bool delta = look.trees == "eucalyptus";
        int want = Mathf.RoundToInt(look.treeCount * density);
        int tries = want * 12;
        Vector3 size = mapMax - mapMin;
        while (Trees < want && tries-- > 0)
        {
            Vector3 p = new Vector3(mapMin.x + 12f + (float)rnd.NextDouble() * (size.x - 24f), 0f, mapMin.z + 12f + (float)rnd.NextDouble() * (size.z - 24f));
            if (DistanceToFarm(p) < 16f) continue;
            if (!Free(p, 3.2f)) continue;
            if (delta) { if (rnd.NextDouble() < 0.65) { DesertProps.Eucalyptus(root, OnGround(p), rnd); Eucalyptus++; } else DesertProps.Sidr(root, OnGround(p), rnd); }
            else { double k = rnd.NextDouble(); if (k < 0.42) DesertProps.Acacia(root, OnGround(p), rnd); else if (k < 0.62) DesertProps.Ghaf(root, OnGround(p), rnd); else DesertProps.Sidr(root, OnGround(p), rnd); }
            Mark(p); Trees++;
        }
    }

    // ------------------------------------------------------------------ graphics makeover: shrubs + instanced grass
    /// <summary>True where vegetation may grow: on the map, off plots / pads / buildings / colliders and the track.</summary>
    public bool ClearForVegetation(Vector3 p)
    {
        if (p.x < mapMin.x + 2f || p.z < mapMin.z + 2f || p.x > mapMax.x - 2f || p.z > mapMax.z - 2f) return false;
        if (InKeepOut(p, 0.8f)) return false;
        return DistanceToTrack(Flat(p)) > 3.4f;
    }

    // Low shrubs and dry clumps across the open land, then grass: dry tufts in the desert, green under the date garden;
    // lush grass on the Delta. Counts follow Settings > Graphics > Foliage (density).
    private void BuildUndergrowth(System.Random rnd, float density, RegionLook look)
    {
        if (density <= 0.01f) return;
        bool delta = look.trees == "eucalyptus";
        int want = Mathf.RoundToInt((delta ? 30f : 46f) * density);
        Vector3 size = mapMax - mapMin;
        for (int tries = want * 10; Shrubs < want && tries > 0; tries--)
        {
            Vector3 p = new Vector3(mapMin.x + 6f + (float)rnd.NextDouble() * (size.x - 12f), 0f, mapMin.z + 6f + (float)rnd.NextDouble() * (size.z - 12f));
            if (DistanceToFarm(p) < 12f || !Free(p, 1.4f)) continue;
            DesertProps.Shrub(root, OnGround(p), rnd, !delta && rnd.NextDouble() < 0.6);
            Mark(p); Shrubs++;
        }
        var area = new Bounds((mapMin + mapMax) * 0.5f, new Vector3(size.x, 50f, size.z));
        System.Func<Vector3, bool> clear = p => ClearForVegetation(p) && DistanceToFarm(p) > 4f;
        GrassTufts += Vegetation.ScatterGrass(area, delta ? Biome.Delta : Biome.Desert, Seed + 11, clear, density);
        if (HasHouse)   // the farmhouse's palm garden is watered: green grass under the palms
            GrassTufts += Vegetation.ScatterGrass(new Bounds(HousePosition, new Vector3(46f, 50f, 46f)), Biome.Oasis, Seed + 13, clear, density);
    }

    // ------------------------------------------------------------------ Nile Delta: canals, windbreaks, pigeon tower, sell points
    private static bool FieldGrid(out Vector3 center, out Quaternion rot, out float minX, out float maxX, out float minZ, out float maxZ, out List<float> colGaps)
    {
        center = Vector3.zero; rot = Quaternion.identity; minX = minZ = float.MaxValue; maxX = maxZ = float.MinValue; colGaps = new List<float>();
        var fm = FieldManager.Instance;
        if (fm == null || fm.Plots.Count == 0) return false;
        center = fm.GridCenter; rot = fm.GridRotation;
        var inv = Quaternion.Inverse(rot);
        var xs = new List<float>();
        foreach (var p in fm.Plots)
        {
            if (p == null) continue;
            Vector3 l = inv * (p.transform.position - center);
            minX = Mathf.Min(minX, l.x - p.size.x * 0.5f); maxX = Mathf.Max(maxX, l.x + p.size.x * 0.5f);
            minZ = Mathf.Min(minZ, l.z - p.size.y * 0.5f); maxZ = Mathf.Max(maxZ, l.z + p.size.y * 0.5f);
            float x = Mathf.Round(l.x * 10f) / 10f;
            if (!xs.Contains(x)) xs.Add(x);
        }
        xs.Sort();
        for (int i = 0; i + 1 < xs.Count; i++) colGaps.Add((xs[i] + xs[i + 1]) * 0.5f);
        return minX < maxX;
    }

    /// <summary>Water strip (canal or ditch) in field-grid space: flat, no collider, merged with the other water (one material).</summary>
    private void WaterStrip(Vector3 center, Quaternion rot, float x0, float z0, float x1, float z1, float width)
    {
        Vector3 a = center + rot * new Vector3(x0, 0f, z0), b = center + rot * new Vector3(x1, 0f, z1);
        Vector3 mid = (a + b) * 0.5f, dir = Flat(b - a);
        float len = dir.magnitude;
        if (len < 0.5f) return;
        var water = Build.Mat(new Color(0.2f, 0.36f, 0.4f), 0.92f);
        var bank = Build.Mat(new Color(0.33f, 0.27f, 0.19f), 0.05f);
        var go = new GameObject("Canal");
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(new Vector3(mid.x, Ground(mid), mid.z), Quaternion.LookRotation(dir / len));
        Build.Part(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.035f, 0f), new Vector3(width, 0.03f, len), water, "Water");
        foreach (float side in new[] { -1f, 1f })
            Build.Part(PrimitiveType.Cube, go.transform, new Vector3(side * (width * 0.5f + 0.18f), 0.06f, 0f), new Vector3(0.36f, 0.12f, len), bank, "Bank");
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);   // flat decoration: nothing to bump into
        Canals++;
    }

    // A mesqa (field canal) along the side of the plot grid, a marwa (ditch) between each column of plots and one across the far end.
    private void BuildCanals()
    {
        Vector3 c; Quaternion rot; float minX, maxX, minZ, maxZ; List<float> gaps;
        if (!FieldGrid(out c, out rot, out minX, out maxX, out minZ, out maxZ, out gaps)) return;
        WaterStrip(c, rot, minX - 2.2f, minZ - 4f, minX - 2.2f, maxZ + 4f, 1.4f);
        WaterStrip(c, rot, minX - 2.2f, maxZ + 2.2f, maxX + 1f, maxZ + 2.2f, 0.9f);
        foreach (float x in gaps) WaterStrip(c, rot, x, minZ, x, maxZ, 0.5f);
    }

    // Eucalyptus windbreak rows along the outer side of the canal and behind the fields (as along Delta canals).
    private void BuildWindbreaks(System.Random rnd, float density)
    {
        Vector3 c; Quaternion rot; float minX, maxX, minZ, maxZ; List<float> gaps;
        if (!FieldGrid(out c, out rot, out minX, out maxX, out minZ, out maxZ, out gaps)) return;
        float step = Mathf.Lerp(9f, 5.5f, density);
        for (float z = minZ - 4f; z <= maxZ + 4f; z += step) Windbreak(c + rot * new Vector3(minX - 6.5f, 0f, z), rnd);
        for (float x = minX; x <= maxX + 1f; x += step) Windbreak(c + rot * new Vector3(x, 0f, maxZ + 6.5f), rnd);
    }

    private void Windbreak(Vector3 p, System.Random rnd)
    {
        if (!FreeNearTrack(p, 1.5f)) return;
        DesertProps.Eucalyptus(root, OnGround(p), rnd); Mark(p); Eucalyptus++; Trees++;
    }

    // Delta farmhouses keep pigeons in a tall mud-brick dovecote (burg hamam) beside the house.
    private void BuildPigeonTower()
    {
        if (!HasHouse) return;
        Vector3 h = Flat(HousePosition), look = Flat(start - h).normalized;
        if (look.sqrMagnitude < 0.01f) look = fwd;
        Vector3 side = Vector3.Cross(Vector3.up, look);
        for (int i = 0; i < 16; i++)   // first free spot on two rings around the house
        {
            Vector3 p = h + Quaternion.Euler(0f, i * 45f + (i >= 8 ? 22.5f : 0f), 0f) * -side * (i < 8 ? 10f : 15f);
            if (!Free(p, 2.5f)) continue;
            DesertProps.PigeonTower(root, OnGround(p));
            Keep(p, 2.5f); Mark(p); HasPigeonTower = true;
            return;
        }
    }

    // The map's selling-point buildings (market.json points with "building", e.g. the Delta's cotton gin, rice mill and
    // village souq) as small buildings with signs behind the market pad. Their prices come from MarketManager.
    private void BuildSellPoints()
    {
        var points = MarketManager.Buildings(Regions.CurrentId);
        if (points.Count == 0 || DeliveryZone.Market == null) return;
        Vector3 m = Flat(DeliveryZone.Market.transform.position);
        Vector3 toFarm = DeliveryZone.LoadingBay != null ? Flat(DeliveryZone.LoadingBay.transform.position) - m : fwd;
        toFarm = toFarm.sqrMagnitude < 0.01f ? fwd : toFarm.normalized;
        Vector3 side = Vector3.Cross(Vector3.up, toFarm);
        foreach (var sp in points)
        {
            if (sp == null) continue;
            foreach (var p in new[] { m - toFarm * 17f, m - toFarm * 17f + side * 13f, m - toFarm * 17f - side * 13f, m - toFarm * 28f, m + side * 20f, m - side * 20f, m - toFarm * 28f + side * 13f, m - toFarm * 28f - side * 13f })
            {
                if (!Free(p, 4f)) continue;
                var go = DesertProps.SellPoint(root, OnGround(p), Quaternion.LookRotation(toFarm), new Color(sp.r, sp.g, sp.b));
                go.name = "Sell Point " + sp.id;
                var sign = new GameObject("Sign");
                sign.transform.SetParent(root, false);
                sign.transform.position = OnGround(p);
                Build.Label(sign.transform, new Vector3(0f, 5.6f, 0f), sp.nameEn, 2.6f, Color.white);
                Keep(p, 4f); Mark(p); SellPoints++;
                break;
            }
        }
    }
}

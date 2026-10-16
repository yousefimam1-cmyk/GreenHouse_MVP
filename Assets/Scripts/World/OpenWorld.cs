using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

/// <summary>
/// The open world around the farm, built at scene start for the chosen map in every play mode (career, free play,
/// co-op; it does not depend on campaign progress - only the campaign's missions use it for the story):
/// - 24 runtime terrain tiles around the scene terrain (~2.4 x 2.4 km): noise heightmaps (Najd dunes, wadi, rocky
///   escarpment, oasis; Delta flat fields and canals), 3 small procedural surface layers, low basemap distance,
///   one shared material; they blend into the scene terrain's edges and rise into a soft edge at the rim (plus an
///   invisible wall and a "turn back" message).
/// - a road network (WorldLayout): asphalt ring highway with exits, farm road from the market pad, asphalt / dirt
///   access roads to every place; roads are merged strips on flattened ground.
/// - places (town market, fuel station, dealership + workshop, silo / dairy / fodder / dry port in Najd, cotton gin /
///   rice mill / village souq / Damietta port on the Delta, neighbour farms with workers, farmland for sale) built
///   as merged meshes with a few box colliders, map icons and bilingual (EN / AR) signposts at the junctions.
/// - the truck sells at the selling point it is parked on (that point's prices), refuelling at the fuel station,
///   repairs at the workshop, places are discovered (fast travel unlocked) when you get near.
/// - performance: everything merged per material and static batched; places, labels, trees and workers further
///   than their view distance are switched off (no rendering, no Update); zone checks run 4x a second.
/// </summary>
[MazraaSystem(68)]
public class OpenWorld : MonoBehaviour
{
    public static OpenWorld Instance { get; private set; }
    /// <summary>The scene's own terrain (captured before the world tiles exist; Terrain.activeTerrain may be a tile later).</summary>
    public static Terrain CoreTerrain { get; private set; }
    /// <summary>Raised after the world was (re)built.</summary>
    public static event System.Action Built;

    public WorldLayout Layout { get; private set; }
    public WorldShape Shape { get; private set; }
    public string BuiltRegion { get; private set; } = "";
    public bool IsBuilt => Layout != null;
    /// <summary>World y of the scene terrain's base (layout heights are relative to it).</summary>
    public float BaseY { get; private set; }
    public readonly List<Terrain> Tiles = new List<Terrain>();
    public int HeightmapResolution { get; private set; }
    public int Trees { get; private set; }
    public int Signs { get; private set; }
    public readonly Dictionary<string, Transform> Places = new Dictionary<string, Transform>();
    public readonly List<GameObject> EdgeWalls = new List<GameObject>();

    public const float SellRadius = 7.5f, FuelRadius = 12f, WorkshopRadius = 14f, DiscoverPad = 25f;
    public const int FuelPrice = 3;   // per litre, like the farm's loading bay

    private Transform root;
    private Vector3 start;
    private float tick, sellCooldown, refuelTimer, edgeWarned;
    private readonly List<(GameObject go, Vector3 c, float d)> culled = new List<(GameObject, Vector3, float)>();
    private readonly List<Object> owned = new List<Object>();   // runtime terrain data, layers, textures (destroyed on rebuild)

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        Instance = this;
        CoreTerrain = Terrain.activeTerrain;
        var inputs = FindAnyObjectByType<StarterAssetsInputs>();
        start = inputs != null ? inputs.transform.position : transform.position;
    }

    private void Start()
    {
        GameEvents.RegionChanged += OnRegion;
        GameEvents.NewGameStarted += OnNewGame;
        DriveSurfaces.WorldSurface = SurfaceAt;
        Vehicle.WorkshopAt = AtWorkshop;
        ObjectiveMarker.TargetHooks.Add(MissionTarget);
        Invoke(nameof(BuildWorld), 0.4f);   // after VehicleSpawner, FieldManager and the land office placed the farm
    }

    private void OnDestroy()
    {
        GameEvents.RegionChanged -= OnRegion;
        GameEvents.NewGameStarted -= OnNewGame;
        if (Instance != this) return;
        Instance = null;
        DriveSurfaces.WorldSurface = null;
        Vehicle.WorkshopAt = null;
        ObjectiveMarker.TargetHooks.Remove(MissionTarget);
        Clear();
    }

    private void OnRegion(string id) { if (IsBuilt && id != BuiltRegion) BuildWorld(); }

    private void OnNewGame()
    {
        foreach (var region in new[] { "najd", "nile_delta" })
            foreach (var d in WorldLayout.PoiDefs(region)) SaveData.DeleteKey(SeenKey(region, d.id));
    }

    // ------------------------------------------------------------------ build

    private void Clear()
    {
        if (root != null) Destroy(root.gameObject);
        root = null;
        foreach (var o in owned) if (o != null) Destroy(o);
        owned.Clear(); Tiles.Clear(); culled.Clear(); Places.Clear(); EdgeWalls.Clear();
        Layout = null;
    }

    /// <summary>Scene terrain rect (the playable area before the open world), like DesertDressing's map.</summary>
    public void CoreRect(out Vector3 min, out Vector3 max)
    {
        var t = CoreTerrain;
        if (t != null && t.terrainData != null) { min = t.transform.position; max = min + t.terrainData.size; }
        else { min = start - new Vector3(130f, 0f, 130f); max = start + new Vector3(130f, 0f, 190f); }
        min.y = max.y = 0f;
    }

    private float CoreHeight(Vector3 p) => CoreTerrain != null ? CoreTerrain.SampleHeight(new Vector3(p.x, 0f, p.z)) : 0f;

    /// <summary>World ground height at p (scene terrain inside it, the open world's ground outside).</summary>
    public float GroundY(Vector3 p)
    {
        if (Shape == null || Layout == null || Layout.InCore(p)) return Build.GroundY(p);
        return BaseY + Shape.Final(p.x, p.z);
    }

    public Vector3 OnGround(Vector3 p, float lift = 0f) => new Vector3(p.x, GroundY(p) + lift, p.z);

    public void BuildWorld()
    {
        Clear();
        string region = Regions.CurrentId;
        BuiltRegion = region;
        BaseY = CoreTerrain != null ? CoreTerrain.transform.position.y : 0f;
        CoreRect(out var cmin, out var cmax);
        root = new GameObject("Open World").transform;
        root.SetParent(transform, false);
        try
        {
            Shape = new WorldShape(region, cmin, cmax, CoreHeight);
            Vector3 pad = DeliveryZone.Market != null ? DeliveryZone.Market.transform.position : start + Vector3.forward * 60f;
            Layout = WorldLayout.Create(region, cmin, cmax, pad, Obstacles(), CoreHeight, Shape.Raw);
            Shape.AddFeatures(Layout);
            BuildTerrain();
            BuildRoads();
            BuildPlaces();
            BuildSigns();
            BuildScenery();
            BuildEdge();
        }
        catch (System.Exception e) { Debug.LogWarning("[OpenWorld] " + e); }
        Minimap.MaxFullRange = Layout != null ? Layout.Half + 60f : 400f;
        tick = 0f;
        Built?.Invoke();
    }

    private List<WorldLayout.Obstacle> Obstacles()
    {
        var list = new List<WorldLayout.Obstacle> { new WorldLayout.Obstacle(start, 10f) };
        var fm = FieldManager.Instance;
        if (fm != null) foreach (var p in fm.Plots) if (p != null) list.Add(new WorldLayout.Obstacle(p.transform.position, Mathf.Max(p.size.x, p.size.y) * 0.75f + 2f));
        foreach (var g in FindObjectsByType<GreenhouseManager>(FindObjectsSortMode.None)) list.Add(new WorldLayout.Obstacle(g.transform.position, 18f));
        if (AnimalPen.Instance != null) list.Add(new WorldLayout.Obstacle(AnimalPen.Instance.transform.position, 14f));
        if (DeliveryZone.LoadingBay != null) list.Add(new WorldLayout.Obstacle(DeliveryZone.LoadingBay.transform.position, 10f));
        foreach (var s in FindObjectsByType<ShopStall>(FindObjectsSortMode.None)) list.Add(new WorldLayout.Obstacle(s.transform.position, 4f));
        return list;
    }

    // ------------------------------------------------------------------ terrain tiles

    private TerrainLayer[] layers;

    private void BuildTerrain()
    {
        var look = Regions.Look;
        Color ground = RegionLook.C(look.ground, DesertDressing.SandColor);
        var colors = Shape.LayerColors(ground);
        layers = new TerrainLayer[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var tex = NoiseTexture(colors[i], 64, i * 17 + 3);
            owned.Add(tex);
            var tl = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(8f, 8f) };
            tl.name = "World layer " + i;
            owned.Add(tl);
            layers[i] = tl;
        }
        // ~4 m per height sample (roads and pads are flattened wider than that) and ~9 m per surface texel: about
        // 0.4 MB of heights for the whole world, built in well under a second.
        HeightmapResolution = 129;
        int alphaRes = 64;
        var L = Layout;
        float h = L.Half, cx = L.Center.x, cz = L.Center.z;
        float[] xs = { cx - h, cx - h * 0.5f, L.CoreMin.x, L.CoreMax.x, cx + h * 0.5f, cx + h };
        float[] zs = { cz - h, cz - h * 0.5f, L.CoreMin.z, L.CoreMax.z, cz + h * 0.5f, cz + h };
        var grid = new Terrain[5, 5];
        var tilesRoot = new GameObject("Terrain Tiles").transform;
        tilesRoot.SetParent(root, false);
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
            {
                if (i == 2 && j == 2) continue;   // the scene's own terrain
                grid[i, j] = Tile(tilesRoot, xs[i], zs[j], xs[i + 1] - xs[i], zs[j + 1] - zs[j], HeightmapResolution, alphaRes, i * 5 + j);
            }
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
            {
                var t = grid[i, j];
                if (t == null) continue;
                t.SetNeighbors(i > 0 ? grid[i - 1, j] : null, j < 4 ? grid[i, j + 1] : null, i < 4 ? grid[i + 1, j] : null, j > 0 ? grid[i, j - 1] : null);
            }
    }

    private Terrain Tile(Transform parent, float x0, float z0, float w, float l, int res, int alphaRes, int index)
    {
        var td = new TerrainData();
        td.heightmapResolution = res;
        td.size = new Vector3(w, WorldShape.Range, l);
        var hs = new float[res, res];
        float sx = w / (res - 1), sz = l / (res - 1);
        for (int iz = 0; iz < res; iz++)
        {
            float z = z0 + iz * sz;
            for (int ix = 0; ix < res; ix++)
                hs[iz, ix] = Mathf.Clamp01((Shape.Final(x0 + ix * sx, z) - WorldShape.MinH) / WorldShape.Range);
        }
        td.SetHeights(0, 0, hs);
        td.baseMapResolution = Mathf.Max(32, alphaRes);
        td.alphamapResolution = alphaRes;
        td.terrainLayers = layers;
        var al = new float[alphaRes, alphaRes, layers.Length];
        var wt = new float[layers.Length];
        for (int az = 0; az < alphaRes; az++)
            for (int ax = 0; ax < alphaRes; ax++)
            {
                float u = (ax + 0.5f) / alphaRes, v = (az + 0.5f) / alphaRes;
                int hx = Mathf.Clamp(Mathf.RoundToInt(u * (res - 1)), 1, res - 2), hz = Mathf.Clamp(Mathf.RoundToInt(v * (res - 1)), 1, res - 2);
                float gx = (hs[hz, hx + 1] - hs[hz, hx - 1]) * WorldShape.Range / (2f * sx), gz = (hs[hz + 1, hx] - hs[hz - 1, hx]) * WorldShape.Range / (2f * sz);
                Shape.Weights(x0 + u * w, z0 + v * l, Mathf.Sqrt(gx * gx + gz * gz), wt);
                for (int k = 0; k < wt.Length; k++) al[az, ax, k] = wt[k];
            }
        td.SetAlphamaps(0, 0, al);
        owned.Add(td);
        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "World Tile " + index;
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(x0, BaseY + WorldShape.MinH, z0);
        var t = go.GetComponent<Terrain>();
        if (CoreTerrain != null && CoreTerrain.materialTemplate != null) t.materialTemplate = CoreTerrain.materialTemplate;
        t.basemapDistance = 220f;
        t.heightmapPixelError = 6f;
        t.drawTreesAndFoliage = false;
        t.drawInstanced = true;
        t.allowAutoConnect = false;
        t.groupingID = 7177;
        Tiles.Add(t);
        return t;
    }

    /// <summary>Small tiling texture of a colour with grain (64 x 64 RGBA = 16 KB, mipmapped).</summary>
    private static Texture2D NoiseTexture(Color c, int size, int seed)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        tex.name = "World ground " + seed;
        tex.wrapMode = TextureWrapMode.Repeat;
        var px = new Color[size * size];
        var rnd = new System.Random(seed);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = 0.9f + 0.12f * Mathf.PerlinNoise(x * 0.17f + seed, y * 0.17f) + 0.06f * (float)(rnd.NextDouble() - 0.5);
                px[y * size + x] = new Color(c.r * n, c.g * n, c.b * n, 1f);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // ------------------------------------------------------------------ roads

    private void BuildRoads()
    {
        var look = Regions.Look;
        var asphalt = WorldMesh.Mat(new Color(0.21f, 0.21f, 0.22f), 0.18f);
        var dirt = WorldMesh.Mat(RegionLook.C(look.track, DesertDressing.TrackColor), 0.05f);
        var paint = WorldMesh.Mat(new Color(0.92f, 0.9f, 0.82f), 0.3f);
        var m = new WorldMesh();
        foreach (var r in Layout.Roads)
        {
            int n = r.path.Count;
            var hs = new float[n];
            for (int i = 0; i < n; i++) hs[i] = r.inCore ? Build.GroundY(r.path[i]) : BaseY + r.profile[i];
            m.Strip(r.path, hs, r.width, r.inCore ? 0.12f : (r.asphalt ? 0.16f : 0.12f), r.asphalt ? asphalt : dirt);
            if (!r.asphalt || r.width < 7f) continue;
            // dashed centre line
            float run = 0f;
            for (int i = 1; i < n; i++)
            {
                Vector3 a = new Vector3(r.path[i - 1].x, hs[i - 1] + 0.18f, r.path[i - 1].z), b = new Vector3(r.path[i].x, hs[i] + 0.18f, r.path[i].z);
                run += (b - a).magnitude;
                if (run % 12f < 6f) m.FlatLine(a, b, 0.18f, paint);
            }
        }
        var go = new GameObject("Roads");
        go.transform.SetParent(root, false);
        m.Emit(go.transform, "Road", false);
        StaticBatchingUtility.Combine(go);
    }

    /// <summary>What the wheels are on in the open world (null inside the scene terrain: the farm's own rules).</summary>
    public DriveSurface? SurfaceAt(Vector3 p)
    {
        if (Layout == null) return null;
        if (Layout.InCore(p))
        {
            foreach (var road in Layout.Roads)
                if (road.inCore) for (int i = 0; i + 1 < road.path.Count; i++) if (WorldShape.Seg(p, road.path[i], road.path[i + 1], out _) < road.width * 0.5f + 0.5f) return DriveSurface.Asphalt;
            return null;
        }
        float d = Shape.RoadDistance(p, out bool asphalt, out float half);
        if (d <= half + 0.8f) return asphalt ? DriveSurface.Asphalt : DriveSurface.DirtRoad;
        foreach (var poi in Layout.Pois)
            if (poi.kind != PoiKind.Farm && poi.kind != PoiKind.Neighbour && poi.kind != PoiKind.Land && Flat(p - poi.pos).magnitude < poi.radius * 0.6f) return DriveSurface.DirtRoad;
        return Layout.Delta ? DriveSurface.Field : DriveSurface.Sand;
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>Is any world road (the farm road on the scene terrain included) within r of p?</summary>
    public bool RoadNear(Vector3 p, float r)
    {
        if (Layout == null) return false;
        p.y = 0f;
        foreach (var road in Layout.Roads)
        {
            if (!road.inCore) continue;
            for (int i = 0; i + 1 < road.path.Count; i++) if (WorldShape.Seg(p, road.path[i], road.path[i + 1], out _) < r + road.width * 0.5f) return true;
        }
        if (Layout.InCore(p, -1f)) return false;
        return Shape.RoadDistance(p, out _, out float half) < r + half;
    }

    // ------------------------------------------------------------------ places

    private Vector3 W(Vector3 layoutPos) => new Vector3(layoutPos.x, BaseY + layoutPos.y, layoutPos.z);

    private void BuildPlaces()
    {
        var places = new GameObject("Places").transform;
        places.SetParent(root, false);
        int seed = 1;
        foreach (var p in Layout.Pois)
        {
            if (p.kind == PoiKind.Farm) continue;
            var go = new GameObject("Place " + p.id);
            go.transform.SetParent(places, false);
            go.transform.SetPositionAndRotation(W(p.pos), Quaternion.Euler(0f, p.yaw, 0f));
            Places[p.id] = go.transform;
            // meshes (world space, relative to the place)
            var m = new WorldMesh();
            var cols = new List<WorldCollider>();
            var local = new WorldPoi { kind = p.kind, pos = Vector3.zero, yaw = 0f, radius = p.radius, sellPoint = p.sellPoint, color = p.color };
            if (p.parcel != null) local.parcel = new LandParcel { rows = p.parcel.rows, cols = p.parcel.cols, plotSize = p.parcel.plotSize, gap = p.parcel.gap, center = Vector3.zero, rot = Quaternion.identity };
            WorldBuildings.Build(m, cols, local, Layout.Delta, seed++ * 7919);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            m.Emit(visual.transform, p.id, true);
            if (cols.Count > 0)
            {
                foreach (var c in cols)
                {
                    var bc = visual.AddComponent<BoxCollider>();
                    bc.center = c.center; bc.size = c.size;
                }
                visual.layer = DesertProps.PropLayer;   // solid, but ground raycasts ignore it
            }
            StaticBatchingUtility.Combine(visual);
            float view = p.kind == PoiKind.Town || p.kind == PoiKind.Port ? 1100f : p.kind == PoiKind.Land ? 450f : 800f;
            culled.Add((visual, go.transform.position, view));
            // name label
            if (p.kind != PoiKind.Land)
            {
                var label = new GameObject("Label");
                label.transform.SetParent(go.transform, false);
                Build.Label(label.transform, new Vector3(0f, p.kind == PoiKind.Neighbour ? 9f : 16f, 0f), p.nameEn, p.kind == PoiKind.Neighbour ? 5f : 8f, Color.white);
                culled.Add((label, go.transform.position, 320f));
            }
            // map icon (on the place's root: never switched off)
            var icon = MinimapIcon.Add(go, MinimapIconKind.Custom, p.color, p.letter, p.kind == PoiKind.Neighbour ? 15f : 20f);
            if (icon != null) icon.showOnMinimap = true;
            Life(go.transform, p);
        }
        // land parcels: signs and plots
        if (WorldLand.Instance != null) WorldLand.Instance.Apply(this);
    }

    // People: shoppers in town, workers on the neighbours' fields (switched off with the place when far away).
    private static readonly string[] People = { "Characters/character-male-a", "Characters/character-female-b", "Characters/character-male-c", "Characters/character-female-d" };

    private void Life(Transform place, WorldPoi p)
    {
        int n = p.kind == PoiKind.Neighbour ? 2 : p.kind == PoiKind.Town || p.kind == PoiKind.VillageSouq ? 3 : 0;
        if (n == 0) return;
        var life = new GameObject("Life");
        life.transform.SetParent(place, false);
        for (int i = 0; i < n; i++)
        {
            Vector3 local = p.kind == PoiKind.Neighbour ? new Vector3(-8f + i * 14f, 0f, -12f + i * 6f) : new Vector3(-6f + i * 6f, 0f, -6f);
            Vector3 pos = OnGround(place.TransformPoint(local));
            var a = Actor.Spawn(People[(i + p.id.Length) % People.Length], 1.5f, true, p.kind == PoiKind.Neighbour ? ActorMode.Worker : ActorMode.Wander, pos, life.transform);
            if (a == null) continue;
            a.home = pos; a.radius = p.kind == PoiKind.Neighbour ? 16f : 9f; a.speed = 1f;
        }
        culled.Add((life, place.position, 160f));
    }

    // ------------------------------------------------------------------ signposts (EN + AR)

    private void BuildSigns()
    {
        var signs = new GameObject("Signposts").transform;
        signs.SetParent(root, false);
        var m = new WorldMesh();
        var post = new Color(0.55f, 0.56f, 0.58f);
        var board = new Color(0.12f, 0.42f, 0.25f);   // green road signs
        Signs = 0;
        for (int ni = 0; ni < Layout.Nodes.Count; ni++)
        {
            var node = Layout.Nodes[ni];
            if (!node.sign) continue;
            var targets = SignLines(ni);
            if (targets.Count == 0) continue;
            // stand beside the road, away from the ring
            Vector3 side = Vector3.Cross(Vector3.up, targets[0].dir);
            Vector3 at = node.pos + side * 7.5f - targets[0].dir * 3f;
            at = OnGround(at);
            float hgt = 2.6f + 0.55f * targets.Count;
            var rot = Quaternion.LookRotation(-side);
            m.Box(at, new Vector3(0.18f, hgt, 0.18f), rot, post);
            m.Box(at + Vector3.up * (hgt - 0.5f * (0.4f + 0.55f * targets.Count)), new Vector3(3.6f, 0.4f + 0.55f * targets.Count, 0.1f), rot, board);
            var en = new System.Text.StringBuilder(); var ar = new System.Text.StringBuilder();
            foreach (var t in targets)
            {
                string km = (t.metres / 1000f).ToString("0.0");
                en.Append(t.poi.nameEn).Append("  ").Append(km).Append(" km\n");
                ar.Append(t.poi.nameAr).Append("  ").Append(km).Append(" كم\n");
            }
            var holder = new GameObject("Sign " + ni);
            holder.transform.SetParent(signs, false);
            holder.transform.position = at;
            SignText(holder.transform, new Vector3(0f, hgt + 0.9f + 0.32f * targets.Count, 0f), en.ToString().TrimEnd(), false);
            SignText(holder.transform, new Vector3(0f, hgt + 0.35f, 0f), ar.ToString().TrimEnd(), true);
            culled.Add((holder.gameObject, at, 140f));
            Signs++;
        }
        var go = new GameObject("Signpost meshes");
        go.transform.SetParent(signs, false);
        m.Emit(go.transform, "Signpost", true);
        StaticBatchingUtility.Combine(go);
    }

    /// <summary>What a signpost at this node lists: the branch destinations; the farm gate lists every service place by road.</summary>
    public List<(WorldPoi poi, Vector3 dir, float metres)> SignLines(int node)
    {
        var L = Layout;
        var list = new List<(WorldPoi, Vector3, float)>();
        foreach (var t in L.SignTargets(node)) if (t.poi.kind != PoiKind.Land) list.Add(t);
        var farm = L.Pois[L.FarmPoi];
        bool gate = false;
        foreach (int ri in L.Nodes[node].roads) if (L.Roads[ri].inCore) gate = true;
        if (gate)
        {
            list.Clear();
            Vector3 dir = Flat(L.Nodes[node].pos - farm.pos).normalized;
            foreach (var p in L.Pois)
            {
                if (p.kind == PoiKind.Farm || p.kind == PoiKind.Land || p.kind == PoiKind.Neighbour) continue;
                list.Add((p, dir, L.RoadDistance(node, p.node)));
            }
            list.Sort((a, b) => a.Item3.CompareTo(b.Item3));
            if (list.Count > 5) list.RemoveRange(5, list.Count - 5);
        }
        return list;
    }

    private static void SignText(Transform parent, Vector3 local, string text, bool arabic)
    {
        var go = new GameObject(arabic ? "AR" : "EN");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var t = go.AddComponent<TMPro.TextMeshPro>();
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.fontSize = 3.2f;
        t.color = Color.white;
        t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        if (arabic)
        {
            var f = Loc.ArabicFont;
            if (f != null) t.font = f;
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = ArabicShaper.Shape(lines[i]);
            t.text = string.Join("\n", lines);
        }
        else t.text = text;
        Loc.ExcludeFromScene(t);
        go.AddComponent<Billboard>();
    }

    // ------------------------------------------------------------------ trees, oasis, canals

    private void BuildScenery()
    {
        var rnd = new System.Random(BuiltRegion.Length * 977 + 31);
        var L = Layout; var S = Shape;
        var cells = new Dictionary<long, (WorldMesh mesh, Vector3 center)>();
        const float cellSize = 400f;
        System.Func<Vector3, WorldMesh> cell = p =>
        {
            int ix = Mathf.FloorToInt(p.x / cellSize), iz = Mathf.FloorToInt(p.z / cellSize);
            long k = ((long)ix << 32) ^ (uint)iz;
            if (!cells.TryGetValue(k, out var m)) cells[k] = m = (new WorldMesh(), new Vector3((ix + 0.5f) * cellSize, 0f, (iz + 0.5f) * cellSize));
            return m.mesh;
        };
        Trees = 0;
        float density = Mathf.Clamp(GraphicsEnhancer.DressingDensity, 0f, 1.5f);
        int want = Mathf.RoundToInt((L.Delta ? 260 : 220) * density);
        int tries = want * 10;
        while (Trees < want && tries-- > 0)
        {
            Vector3 p = new Vector3(L.Center.x + ((float)rnd.NextDouble() * 2f - 1f) * (L.Half - 30f), 0f, L.Center.z + ((float)rnd.NextDouble() * 2f - 1f) * (L.Half - 30f));
            if (!FreeSpot(p, 3f)) continue;
            if (L.Delta)
            {
                // eucalyptus windbreaks along the canals and the roads, a few in the fields
                float dc = S.CanalDistance(p.x, p.z);
                float dr = S.RoadDistance(p, out _, out _);
                bool near = (dc > 7f && dc < 13f) || (dr > 9f && dr < 14f);
                if (!near && rnd.NextDouble() > 0.15) continue;
                cell(p).Eucalyptus(OnGround(p), rnd);
            }
            else
            {
                // acacias gather in the wadi, palms near the oasis, shrubs everywhere else
                float wadi = S.WadiMask(p.x, p.z);
                if (wadi > 0.2f || rnd.NextDouble() < 0.35) cell(p).Acacia(OnGround(p), rnd);
                else cell(p).Shrub(OnGround(p), rnd, new Color(0.45f, 0.47f, 0.28f));
            }
            Trees++;
        }
        // Najd oasis: pond and palms beside the town
        if (S.PondRadius > 0f)
        {
            var m = cell(S.Pond);
            m.Disc(W(S.Pond) + Vector3.up * 0.75f, S.PondRadius - 2f, 24, new Color(0.2f, 0.36f, 0.4f), 0.92f);
            for (int i = 0; i < 14; i++)
            {
                Vector3 q = S.Pond + Quaternion.Euler(0f, i * 360f / 14f + (float)rnd.NextDouble() * 10f, 0f) * Vector3.forward * (S.PondRadius + 3f + (float)rnd.NextDouble() * 6f);
                if (S.RoadDistance(q, out _, out _) < 8f) continue;
                m.DatePalm(OnGround(q), rnd); Trees++;
            }
        }
        // Delta canals: water strips (no colliders) in the canal beds
        if (L.Delta)
        {
            var water = WorldMesh.Mat(new Color(0.2f, 0.36f, 0.4f), 0.92f);
            foreach (var line in S.Canals)
            {
                var pts = new List<Vector3>(); var hs = new List<float>();
                System.Action flush = () =>
                {
                    if (pts.Count >= 2) cell(pts[pts.Count / 2]).Strip(pts, hs, 7f, 0f, water);
                    pts = new List<Vector3>(); hs = new List<float>();
                };
                foreach (var q0 in line)
                {
                    bool ok = L.InWorld(q0, WorldShape.EdgeBand) && S.CoreDistance(q0.x, q0.z) > 40f;
                    if (!ok) { flush(); continue; }
                    pts.Add(q0); hs.Add(BaseY + S.Raw(q0.x, q0.z) + 1f);
                    if (pts.Count > 30) { var last = pts[pts.Count - 1]; float lh = hs[hs.Count - 1]; flush(); pts.Add(last); hs.Add(lh); }
                }
                flush();
            }
        }
        var trees = new GameObject("Scenery").transform;
        trees.SetParent(root, false);
        foreach (var kv in cells)
        {
            var go = new GameObject("Scenery cell");
            go.transform.SetParent(trees, false);
            var rs = kv.Value.mesh.Emit(go.transform, "Scenery", true);
            if (rs.Count == 0) continue;
            StaticBatchingUtility.Combine(go);
            culled.Add((go, kv.Value.center, 700f));
        }
    }

    /// <summary>Open ground for a prop: in the world, off the scene terrain, roads, places and canals.</summary>
    public bool FreeSpot(Vector3 p, float r)
    {
        var L = Layout; var S = Shape;
        if (L == null || !L.InWorld(p, 20f) || S.CoreDistance(p.x, p.z) < 25f + r) return false;
        if (S.RoadDistance(p, out _, out float half) < half + 5f + r) return false;
        foreach (var poi in L.Pois) if (Flat(p - poi.pos).magnitude < poi.radius + 10f + r) return false;
        if (S.CanalDistance(p.x, p.z) < 6f + r) return false;
        if (S.PondRadius > 0f && Flat(p - S.Pond).magnitude < S.PondRadius + 2f) return false;
        return true;
    }

    // ------------------------------------------------------------------ soft edge

    private void BuildEdge()
    {
        var L = Layout;
        var edge = new GameObject("World Edge");
        edge.transform.SetParent(root, false);
        edge.layer = DesertProps.PropLayer;
        float h = 400f, t = 10f, s = L.Half * 2f + t * 2f;
        Vector3 c = new Vector3(L.Center.x, BaseY, L.Center.z);
        var offs = new[] { new Vector3(L.Half + t * 0.5f, 0f, 0f), new Vector3(-L.Half - t * 0.5f, 0f, 0f), new Vector3(0f, 0f, L.Half + t * 0.5f), new Vector3(0f, 0f, -L.Half - t * 0.5f) };
        var sizes = new[] { new Vector3(t, h, s), new Vector3(t, h, s), new Vector3(s, h, t), new Vector3(s, h, t) };
        for (int i = 0; i < 4; i++)
        {
            Vector3 off = offs[i], size = sizes[i];
            var wall = new GameObject("Edge wall");
            wall.layer = DesertProps.PropLayer;
            wall.transform.SetParent(edge.transform, false);
            wall.transform.position = c + off;
            var bc = wall.AddComponent<BoxCollider>();
            bc.size = size; bc.center = Vector3.zero;
            EdgeWalls.Add(wall);
        }
    }

    // ------------------------------------------------------------------ discovery, selling, fuel, workshop

    public static string SeenKey(string region, string id) => "mazraa.world.seen." + region + "." + id;
    public bool Seen(WorldPoi p) => p != null && (p.kind == PoiKind.Farm || SaveData.GetInt(SeenKey(BuiltRegion, p.id), 0) == 1);

    public void Discover(WorldPoi p, bool announce = true)
    {
        if (p == null || Seen(p)) return;
        SaveData.SetInt(SeenKey(BuiltRegion, p.id), 1);
        if (announce) Alert(Loc.T("Discovered: {0} - fast travel unlocked (map)", Loc.Raw(p.nameEn)));
        GameEvents.RaiseCustom("visit_" + p.id, 1f);
        if (p.kind == PoiKind.Town) GameEvents.RaiseCustom("visitTown", 1f);
        if (p.kind == PoiKind.Dealer) GameEvents.RaiseCustom("visitDealer", 1f);
    }

    private void Update()
    {
        if (Layout == null) return;
        sellCooldown -= Time.deltaTime;
        tick -= Time.unscaledDeltaTime;
        if (tick > 0f) return;
        tick = 0.25f;
        var anchor = Minimap.Anchor();
        if (anchor == null) return;
        Vector3 a = anchor.position;
        Cull(a);
        foreach (var p in Layout.Pois)
            if (p.kind != PoiKind.Farm && !Seen(p) && Flat(a - p.pos).magnitude < p.radius + DiscoverPad) Discover(p);
        TrySell();
        Refuel();
        if (Layout.EdgeDistance(a) < WorldShape.EdgeBand * 0.6f && Time.unscaledTime > edgeWarned)
        {
            edgeWarned = Time.unscaledTime + 12f;
            Alert(Loc.T("End of the map - turn back"));
        }
    }

    private void Cull(Vector3 a)
    {
        bool all = Minimap.Instance != null && Minimap.Instance.FullOpen;   // the full map shows everything
        foreach (var e in culled)
        {
            GameObject go = e.go; Vector3 c = e.c; float d = e.d;
            if (go == null) continue;
            float dist = Flat(a - c).magnitude;
            bool on = all || dist < (go.activeSelf ? d * 1.1f : d);
            if (go.activeSelf != on) go.SetActive(on);
        }
    }

    /// <summary>Places within range are active (for tests: how many place visuals are switched on).</summary>
    public int ActiveCulled { get { int n = 0; foreach (var c in culled) if (c.go != null && c.go.activeSelf) n++; return n; } }
    public int CulledCount => culled.Count;

    /// <summary>The selling place the truck is parked on (or null).</summary>
    public WorldPoi SellPointAt(Vector3 p)
    {
        if (Layout == null) return null;
        foreach (var poi in Layout.Pois) if (poi.Sells && Flat(p - poi.pos).magnitude < SellRadius) return poi;
        return null;
    }

    private void TrySell()
    {
        var truck = FarmTruck.Instance;
        if (truck == null || !truck.IsDriving || Mathf.Abs(truck.Speed) > 3f || truck.Cargo.Count == 0 || sellCooldown > 0f) return;
        var poi = SellPointAt(truck.transform.position);
        if (poi == null) return;
        sellCooldown = 1.5f;
        var notes = new List<string>();
        int earned = SellHere(poi, truck.Cargo, notes);
        truck.SaveCargo();
        Alert(string.Join("\n", notes));
        if (earned > 0 && AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
    }

    /// <summary>Sell what this place buys from the cargo at its own prices (the rest stays loaded). Returns money earned.</summary>
    public int SellHere(WorldPoi poi, Dictionary<string, float> cargo, List<string> notes)
    {
        var market = MarketManager.Instance;
        if (market == null || poi == null || !poi.Sells) return 0;
        var sold = new Dictionary<string, float>();
        float kg = 0f;
        var eco = EconomyManager.Instance;
        foreach (var kv in cargo)
            if (kv.Value > 0f && market.Accepts(poi.sellPoint, kv.Key)) { sold[kv.Key] = kv.Value; kg += kv.Value * (eco?.Good(kv.Key)?.weight ?? 1f); }
        string name = Loc.Raw(poi.nameEn);
        if (sold.Count == 0)
        {
            notes?.Add(Loc.T("{0} doesn't buy this load", name));
            return 0;
        }
        foreach (var k in sold.Keys) cargo.Remove(k);
        int earned = market.SellDirect(poi.sellPoint, sold, notes);
        notes?.Insert(0, Loc.T("Sold at {0} for {1} SAR", name, earned.ToString("N0")));
        if (cargo.Count > 0) notes?.Add(Loc.T("{0} doesn't buy the rest of the load", name));
        GameEvents.RaiseCustom("sellAt_" + poi.sellPoint, kg);
        if (poi.kind == PoiKind.Town) GameEvents.RaiseCustom("sellAtTown", kg);
        return earned;
    }

    private void Refuel()
    {
        var v = Vehicle.Current;
        var eco = EconomyManager.Instance;
        var fuel = Layout.Poi("fuel");
        if (v == null || eco == null || fuel == null || Flat(v.transform.position - fuel.pos).magnitude > FuelRadius || Mathf.Abs(v.Speed) > 0.5f || v.Fuel >= v.fuelCapacity - 0.5f) { refuelTimer = 0f; return; }
        refuelTimer += 0.25f;
        if (refuelTimer < 1.2f) return;
        refuelTimer = 0f;
        int affordable = eco.Money / FuelPrice;
        float added = v.Refuel(Mathf.Min(affordable, v.fuelCapacity));
        if (added > 0.5f)
        {
            int cost = Mathf.CeilToInt(added) * FuelPrice;
            using (Ledger.As(Ledger.Fuel)) eco.AddMoney(-cost);
            Alert(Loc.T("Refuelled {0} L for {1} SAR", added.ToString("F0"), cost.ToString("N0")));
        }
        else Alert(Loc.T("Not enough money"));
    }

    /// <summary>Parked in the dealership's workshop yard (Vehicle.AtWorkshop: R repairs there too).</summary>
    public bool AtWorkshop(Vector3 p)
    {
        var d = Layout?.Poi("dealer");
        return d != null && Flat(p - d.pos).magnitude < WorkshopRadius;
    }

    // Objective marker for the open-world missions.
    private Transform MissionTarget(MissionDef m, out float height)
    {
        height = 12f;
        if (m == null || Layout == null) return null;
        string id = null;
        switch (m.type)
        {
            case "visitTown": case "sellAtTown": id = "town"; break;
            case "visitDealer": id = "dealer"; break;
            case "buyWorldLand":
                height = 4f;
                if (WorldLand.Instance != null) { var s = WorldLand.Instance.NextForSaleSign(); if (s != null) return s; }
                return null;
        }
        return id != null && Places.TryGetValue(id, out var t) ? t : null;
    }

    private static void Alert(string msg) { if (UIManager.Instance != null && !string.IsNullOrEmpty(msg)) UIManager.Instance.ShowAlertMessage(msg); }
}

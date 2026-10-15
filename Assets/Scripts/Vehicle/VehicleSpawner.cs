using StarterAssets;
using UnityEngine;

/// <summary>
/// Builds the farm's runtime additions next to the player's start point: the farm truck and loading bay,
/// the animal pen with its shop and collection crate, and a market (with a second shop) a drive away.
/// Positions are relative to the start point, so it works without editing the scene; move them later
/// by placing objects named "TruckSpawn", "AnimalPenSpot" or "MarketSpot" in the scene.
/// </summary>
public class VehicleSpawner : MonoBehaviour
{
    public static VehicleSpawner Instance;
    public Vector3 LoadingBay { get; private set; }
    public Vector3 MarketPos { get; private set; }
    public Vector3 PenPos { get; private set; }

    private static readonly Color Orange = new Color(0.93f, 0.42f, 0.08f);

    private void Awake() => Instance = this;

    private void Start()
    {
        var inputs = FindAnyObjectByType<StarterAssetsInputs>();
        Transform start = inputs != null ? inputs.transform : (Camera.main != null ? Camera.main.transform : transform);
        Vector3 fwd = Vector3.ProjectOnPlane(start.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        LoadingBay = Spot("TruckSpawn", start.position + right * 7f + fwd * 2f);
        PenPos = Spot("AnimalPenSpot", start.position - right * 16f + fwd * 4f);
        MarketPos = Spot("MarketSpot", PickMarket(start.position));

        BuildTruck(LoadingBay, Quaternion.LookRotation(fwd));
        BuildZone(LoadingBay, ZoneKind.LoadingBay, new Color(0.3f, 0.85f, 0.35f), "Loading bay");
        BuildPen(PenPos, Quaternion.LookRotation(fwd));
        BuildZone(MarketPos, ZoneKind.Market, new Color(1f, 0.75f, 0.2f), "Market");
        BuildStall(MarketPos + right * 8f, Quaternion.LookRotation(-right));
        IgnoreFences();
    }

    private static Vector3 Spot(string name, Vector3 fallback)
    {
        var go = GameObject.Find(name);
        return Build.OnGround(go != null ? go.transform.position : fallback);
    }

    // Market ~120 m from the farm, toward the middle of the terrain so it stays on the map.
    private static Vector3 PickMarket(Vector3 from)
    {
        var t = Terrain.activeTerrain;
        if (t == null) return from + Vector3.forward * 120f;
        Vector3 size = t.terrainData.size, origin = t.transform.position;
        Vector3 center = origin + size * 0.5f;
        Vector3 dir = center - from; dir.y = 0f;
        if (dir.magnitude < 20f) dir = Vector3.forward;
        Vector3 p = from + dir.normalized * Mathf.Min(120f, dir.magnitude + 60f);
        float m = 25f;
        p.x = Mathf.Clamp(p.x, origin.x + m, origin.x + size.x - m);
        p.z = Mathf.Clamp(p.z, origin.z + m, origin.z + size.z - m);
        return p;
    }

    private void BuildTruck(Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("Farm Truck");
        root.layer = 7; // Interactable
        root.transform.SetPositionAndRotation(pos + Vector3.up * 0.6f, rot);
        var rb = root.AddComponent<Rigidbody>();
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.62f, 0); col.size = new Vector3(1.9f, 1.3f, 4.6f);
        col.material = new PhysicsMaterial("Truck") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };

        Transform t = root.transform;
        var body = Build.Mat(Orange, 0.55f, 0.1f);
        var dark = Build.Mat(new Color(0.08f, 0.08f, 0.09f), 0.3f);
        var glass = Build.Mat(new Color(0.12f, 0.18f, 0.22f), 0.9f);
        var steel = Build.Mat(new Color(0.55f, 0.56f, 0.58f), 0.6f, 0.6f);
        var light = Build.Mat(new Color(1f, 0.95f, 0.8f), 0.8f, 0f, new Color(2.2f, 2.0f, 1.6f));
        var tail = Build.Mat(new Color(0.8f, 0.05f, 0.05f), 0.6f, 0f, new Color(1.5f, 0.05f, 0.05f));

        var model = Build.TryModel("Truck", t);
        Transform cargo;
        var wheels = new Transform[4];
        if (model == null)
        {
            Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.25f, 0), new Vector3(1.7f, 0.35f, 4.5f), dark, "Chassis");
            Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.75f, 1.55f), new Vector3(1.9f, 0.7f, 1.4f), body, "Hood");
            Build.Part(PrimitiveType.Cube, t, new Vector3(0, 1.35f, 0.75f), new Vector3(1.85f, 0.95f, 1.3f), body, "Cab");
            Build.Part(PrimitiveType.Cube, t, new Vector3(0, 1.45f, 1.41f), new Vector3(1.65f, 0.6f, 0.04f), glass, "Windshield", new Vector3(-12, 0, 0));
            Build.Part(PrimitiveType.Cube, t, new Vector3(0.93f, 1.5f, 0.75f), new Vector3(0.04f, 0.5f, 1.0f), glass);
            Build.Part(PrimitiveType.Cube, t, new Vector3(-0.93f, 1.5f, 0.75f), new Vector3(0.04f, 0.5f, 1.0f), glass);
            Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.62f, 2.27f), new Vector3(1.6f, 0.3f, 0.06f), steel, "Grille");
            Build.Part(PrimitiveType.Cube, t, new Vector3(0.65f, 0.85f, 2.26f), new Vector3(0.32f, 0.16f, 0.05f), light, "Headlight");
            Build.Part(PrimitiveType.Cube, t, new Vector3(-0.65f, 0.85f, 2.26f), new Vector3(0.32f, 0.16f, 0.05f), light, "Headlight");
            Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.5f, 2.32f), new Vector3(1.95f, 0.18f, 0.12f), dark, "Bumper");
            // Cargo bed
            Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.55f, -1.2f), new Vector3(1.9f, 0.12f, 2.2f), steel, "Bed");
            Build.Part(PrimitiveType.Cube, t, new Vector3(0.92f, 0.85f, -1.2f), new Vector3(0.08f, 0.5f, 2.2f), body);
            Build.Part(PrimitiveType.Cube, t, new Vector3(-0.92f, 0.85f, -1.2f), new Vector3(0.08f, 0.5f, 2.2f), body);
            Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.85f, -2.28f), new Vector3(1.9f, 0.5f, 0.08f), body, "Tailgate");
            Build.Part(PrimitiveType.Cube, t, new Vector3(0.7f, 0.8f, -2.33f), new Vector3(0.25f, 0.14f, 0.04f), tail);
            Build.Part(PrimitiveType.Cube, t, new Vector3(-0.7f, 0.8f, -2.33f), new Vector3(0.25f, 0.14f, 0.04f), tail);
            cargo = new GameObject("Cargo").transform; cargo.SetParent(t, false); cargo.localPosition = new Vector3(0, 0.6f, -1.2f);
            Vector3[] wp = { new Vector3(0.88f, -0.1f, 1.45f), new Vector3(-0.88f, -0.1f, 1.45f), new Vector3(0.88f, -0.1f, -1.4f), new Vector3(-0.88f, -0.1f, -1.4f) };
            for (int i = 0; i < 4; i++)
            {
                var w = new GameObject("Wheel").transform; w.SetParent(t, false); w.localPosition = wp[i];
                Build.Part(PrimitiveType.Cylinder, w, Vector3.zero, new Vector3(0.9f, 0.16f, 0.9f), dark, "Tyre", new Vector3(0, 0, 90));
                Build.Part(PrimitiveType.Cylinder, w, new Vector3(Mathf.Sign(wp[i].x) * 0.12f, 0, 0), new Vector3(0.5f, 0.05f, 0.5f), steel, "Rim", new Vector3(0, 0, 90));
                Build.Part(PrimitiveType.Cube, w, new Vector3(Mathf.Sign(wp[i].x) * 0.17f, 0, 0), new Vector3(0.02f, 0.38f, 0.08f), dark, "Spoke");
                wheels[i] = w;
            }
        }
        else cargo = t;

        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
        var exit = new GameObject("Exit").transform; exit.SetParent(t, false); exit.localPosition = new Vector3(-2.6f, 0.5f, 0.6f);

        var truck = root.AddComponent<FarmTruck>();
        truck.wheels = wheels;
        truck.cargoRoot = cargo;
        truck.exitPoint = exit;
    }

    private void BuildZone(Vector3 pos, ZoneKind kind, Color color, string label)
    {
        var root = new GameObject(kind.ToString());
        root.transform.position = pos;
        var pad = Build.Mat(color * 0.6f, 0.2f, 0f, color * 1.4f);
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.03f, 0), new Vector3(12f, 0.02f, 12f), Build.Mat(new Color(0.25f, 0.24f, 0.22f), 0.1f), "Pad");
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.05f, 0), new Vector3(12.4f, 0.01f, 12.4f), pad, "Ring");
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.06f, 0), new Vector3(11.6f, 0.012f, 11.6f), Build.Mat(new Color(0.25f, 0.24f, 0.22f), 0.1f), "Inner");
        // Beacon visible from far away
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(6.4f, 4f, 0), new Vector3(0.18f, 4f, 0.18f), pad, "Beacon");
        Build.Part(PrimitiveType.Sphere, root.transform, new Vector3(6.4f, 8.2f, 0), Vector3.one * 0.8f, pad, "Beacon top");
        Build.Label(root.transform, new Vector3(6.4f, 9.4f, 0), label, 4f, Color.white);
        var z = root.AddComponent<DeliveryZone>();
        z.kind = kind;
    }

    private void BuildStall(Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("Shop Stall");
        root.layer = 7;
        root.transform.SetPositionAndRotation(Build.OnGround(pos), rot);
        var col = root.AddComponent<BoxCollider>(); col.center = new Vector3(0, 1.2f, 0); col.size = new Vector3(3.2f, 2.4f, 1.6f);
        Transform t = root.transform;
        var wood = Build.Mat(new Color(0.55f, 0.36f, 0.2f), 0.15f);
        var cloth = Build.Mat(Orange, 0.2f);
        var cloth2 = Build.Mat(new Color(0.96f, 0.93f, 0.86f), 0.2f);
        if (Build.TryModel("ShopStall", t) == null)
        {
            Build.Part(PrimitiveType.Cube, t, new Vector3(0, 0.55f, 0), new Vector3(3f, 1.1f, 1.2f), wood, "Counter");
            for (int i = 0; i < 4; i++)
                Build.Part(PrimitiveType.Cube, t, new Vector3(i % 2 == 0 ? -1.45f : 1.45f, 1.4f, i < 2 ? -0.55f : 0.55f), new Vector3(0.1f, 2.8f, 0.1f), wood);
            for (int i = 0; i < 6; i++)
                Build.Part(PrimitiveType.Cube, t, new Vector3(-1.25f + i * 0.5f, 2.85f, 0), new Vector3(0.5f, 0.06f, 1.5f), i % 2 == 0 ? cloth : cloth2, "Awning", new Vector3(-8, 0, 0));
            for (int i = 0; i < 5; i++)
                Build.Part(PrimitiveType.Cube, t, new Vector3(-1.1f + i * 0.55f, 1.2f, 0.1f), new Vector3(0.4f, 0.2f, 0.4f), Build.Mat(new Color(0.62f, 0.45f, 0.26f), 0.1f), "Crate");
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
        Build.Label(t, new Vector3(0, 3.6f, 0), "Farm Shop", 3f, Color.white);
        root.AddComponent<ShopStall>();
    }

    private void BuildPen(Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("Animal Pen");
        root.transform.SetPositionAndRotation(pos, rot);
        var pen = root.AddComponent<AnimalPen>();
        Vector2 s = pen.size;
        var wood = Build.Mat(new Color(0.5f, 0.34f, 0.2f), 0.15f);
        var straw = Build.Mat(new Color(0.78f, 0.66f, 0.38f), 0.05f);
        Transform t = root.transform;
        // Fence posts and rails along the four sides (gap on the front for the gate).
        for (int side = 0; side < 4; side++)
        {
            bool alongX = side % 2 == 0;
            float len = alongX ? s.x : s.y;
            int posts = Mathf.CeilToInt(len / 2f);
            for (int i = 0; i <= posts; i++)
            {
                float u = -len / 2f + i * len / posts;
                if (side == 0 && Mathf.Abs(u) < 1.6f) continue; // gate gap
                Vector3 local = alongX ? new Vector3(u, 0, (side == 0 ? 1 : -1) * s.y / 2f) : new Vector3((side == 1 ? 1 : -1) * s.x / 2f, 0, u);
                Vector3 world = t.TransformPoint(local);
                var post = Build.Part(PrimitiveType.Cube, null, Vector3.zero, new Vector3(0.14f, 1.2f, 0.14f), wood, "Post");
                post.transform.position = Build.OnGround(world, 0.6f);
                post.transform.SetParent(t, true);
                if (i < posts && !(side == 0 && Mathf.Abs(u + len / posts / 2f) < 1.6f))
                {
                    Vector3 mid = alongX ? new Vector3(u + len / posts / 2f, 0, local.z) : new Vector3(local.x, 0, u + len / posts / 2f);
                    foreach (float h in new[] { 0.45f, 0.95f })
                    {
                        var rail = Build.Part(PrimitiveType.Cube, null, Vector3.zero, alongX ? new Vector3(len / posts, 0.09f, 0.06f) : new Vector3(0.06f, 0.09f, len / posts), wood, "Rail");
                        rail.transform.position = Build.OnGround(t.TransformPoint(mid), h);
                        rail.transform.rotation = t.rotation;
                        rail.transform.SetParent(t, true);
                    }
                }
            }
        }
        // Trough, hay and collection crate
        var trough = Build.Part(PrimitiveType.Cube, null, Vector3.zero, new Vector3(3f, 0.45f, 0.7f), wood, "Trough");
        trough.transform.position = Build.OnGround(t.TransformPoint(new Vector3(0, 0, -s.y / 2f + 1.2f)), 0.22f); trough.transform.rotation = t.rotation; trough.transform.SetParent(t, true);
        var hay = Build.Part(PrimitiveType.Cube, trough.transform, new Vector3(0, 0.45f, 0), new Vector3(0.94f, 0.3f, 0.8f), straw, "Hay");

        var crate = new GameObject("Collection Crate");
        crate.layer = 7;
        crate.transform.position = Build.OnGround(t.TransformPoint(new Vector3(3.2f, 0, s.y / 2f + 1.5f)));
        crate.transform.rotation = t.rotation;
        var cc = crate.AddComponent<BoxCollider>(); cc.center = new Vector3(0, 0.45f, 0); cc.size = new Vector3(1.2f, 0.9f, 1.2f);
        Build.Part(PrimitiveType.Cube, crate.transform, new Vector3(0, 0.45f, 0), new Vector3(1.1f, 0.9f, 1.1f), Build.Mat(new Color(0.62f, 0.45f, 0.26f), 0.1f), "Crate");
        foreach (var r in crate.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
        crate.AddComponent<PenCrate>();
        Build.Label(crate.transform, new Vector3(0, 1.6f, 0), "Animal products", 2f, Color.white);

        // Farm shop beside the pen so animals and feed can be bought early.
        BuildStall(t.TransformPoint(new Vector3(-3.5f, 0, s.y / 2f + 2.5f)), t.rotation * Quaternion.Euler(0, 180, 0));
        hay.SetActive(true);
    }

    // Lets the truck pass the existing farm fences so it can leave the farm.
    private static void IgnoreFences()
    {
        var truck = FarmTruck.Instance;
        if (truck == null) return;
        var truckCols = truck.GetComponentsInChildren<Collider>();
        foreach (var c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (c == null || !InFence(c.transform)) continue;
            foreach (var tc in truckCols) Physics.IgnoreCollision(tc, c, true);
        }
    }

    private static bool InFence(Transform t)
    {
        for (; t != null; t = t.parent) if (t.name.IndexOf("Fence", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}

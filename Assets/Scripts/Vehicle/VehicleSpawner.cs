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
        BuildTractor(LoadingBay + right * 5.5f, Quaternion.LookRotation(fwd));
        BuildZone(LoadingBay, ZoneKind.LoadingBay, new Color(0.3f, 0.85f, 0.35f), "Loading bay");
        BuildPen(PenPos, Quaternion.LookRotation(fwd));
        BuildZone(MarketPos, ZoneKind.Market, new Color(1f, 0.75f, 0.2f), "Market");
        BuildStall(MarketPos + right * 8f, Quaternion.LookRotation(-right));
        // Fences block vehicles; gates open where the pads, parked vehicles and the pen are (again once plots and the track exist).
        VehicleObstacles.EnsureFenceColliders();
        VehicleObstacles.OpenGates();
        Invoke(nameof(OpenGatesLater), 2.2f);
    }

    private void OpenGatesLater() => VehicleObstacles.OpenGates();

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
        // Everything but the primitive wheels sits on a "Body" pivot that pitches and rolls a little (VehicleVisuals).
        Transform bodyRoot = new GameObject("Body").transform; bodyRoot.SetParent(t, false);
        // Mazraa livery: green lower body and bed, sand cab, gold pinstripe, chrome grille and bumper, matte tyres.
        var green = VehicleVisuals.Paint(VehicleVisuals.MazraaGreen);
        var sand = VehicleVisuals.Paint(VehicleVisuals.MazraaSand);
        var gold = Build.Mat(VehicleVisuals.MazraaGold, 0.75f, 0.8f);
        var dark = Build.Mat(new Color(0.08f, 0.08f, 0.09f), 0.3f);
        var glass = VehicleVisuals.Glass;
        var chrome = VehicleVisuals.Chrome;
        var rubber = VehicleVisuals.Rubber;

        var model = Build.TryModel("Truck", bodyRoot, 4.6f, -0.55f);
        Transform cargo;
        var wheels = new Transform[4];
        VehicleVisuals.Layout layout;
        if (model != null)
        {
            wheels = Build.FaceForwardAndFindWheels(model, t);
            VehicleVisuals.Restyle(model, wheels, Resources.Load<Texture2D>("Models/Textures/colormap_truck"));
            layout = VehicleVisuals.ModelLayout(model, wheels, t, bodyRoot, false);
            cargo = new GameObject("Cargo").transform; cargo.SetParent(bodyRoot, false); cargo.localPosition = new Vector3(0, 0.55f, -1.2f);
        }
        else
        {
            Transform b = bodyRoot;
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.25f, 0), new Vector3(1.7f, 0.35f, 4.5f), dark, "Chassis");
            // bonnet sloping down to the grille, cab with a rounded roof line
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.72f, 1.6f), new Vector3(1.86f, 0.62f, 1.3f), green, "Bonnet");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 1.02f, 1.62f), new Vector3(1.7f, 0.12f, 1.2f), green, "Bonnet top", new Vector3(4f, 0, 0));
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.8f, 0.75f), new Vector3(1.9f, 0.7f, 1.3f), green, "Cab base");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 1.45f, 0.72f), new Vector3(1.84f, 0.65f, 1.24f), sand, "Cab");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 1.8f, 0.68f), new Vector3(1.7f, 0.08f, 1.1f), sand, "Roof");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 1.48f, 1.36f), new Vector3(1.66f, 0.56f, 0.04f), glass, "Windshield", new Vector3(-14, 0, 0));
            Build.Part(PrimitiveType.Cube, b, new Vector3(0.93f, 1.5f, 0.75f), new Vector3(0.04f, 0.46f, 1.0f), glass, "Side window");
            Build.Part(PrimitiveType.Cube, b, new Vector3(-0.93f, 1.5f, 0.75f), new Vector3(0.04f, 0.46f, 1.0f), glass, "Side window");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 1.5f, 0.09f), new Vector3(1.5f, 0.42f, 0.04f), glass, "Rear window");
            foreach (float x in new[] { -1f, 1f })
            {
                Build.Part(PrimitiveType.Cube, b, new Vector3(x * 0.96f, 1.02f, 0.2f), new Vector3(0.02f, 0.05f, 3.9f), gold, "Pinstripe");
                Build.Part(PrimitiveType.Cube, b, new Vector3(x * 1.06f, 1.45f, 1.28f), new Vector3(0.12f, 0.18f, 0.04f), chrome, "Mirror");
            }
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.66f, 2.27f), new Vector3(1.5f, 0.34f, 0.06f), chrome, "Grille");
            for (int i = 0; i < 4; i++) Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.55f + i * 0.08f, 2.3f), new Vector3(1.4f, 0.025f, 0.02f), dark, "Grille slat");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.45f, 2.33f), new Vector3(1.98f, 0.18f, 0.14f), chrome, "Bumper");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.42f, -2.33f), new Vector3(1.9f, 0.14f, 0.1f), chrome, "Rear bumper");
            // Cargo bed
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.55f, -1.2f), new Vector3(1.9f, 0.12f, 2.2f), dark, "Bed");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0.92f, 0.85f, -1.2f), new Vector3(0.08f, 0.5f, 2.2f), green, "Bed side");
            Build.Part(PrimitiveType.Cube, b, new Vector3(-0.92f, 0.85f, -1.2f), new Vector3(0.08f, 0.5f, 2.2f), green, "Bed side");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.85f, -2.28f), new Vector3(1.9f, 0.5f, 0.08f), green, "Tailgate");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 1.12f, -1.2f), new Vector3(1.94f, 0.05f, 2.24f), sand, "Bed rail");
            // exhaust stack behind the cab
            Build.Part(PrimitiveType.Cylinder, b, new Vector3(0.82f, 1.45f, 0.0f), new Vector3(0.1f, 0.6f, 0.1f), chrome, "Exhaust stack");
            cargo = new GameObject("Cargo").transform; cargo.SetParent(b, false); cargo.localPosition = new Vector3(0, 0.6f, -1.2f);
            Vector3[] wp = { new Vector3(0.88f, -0.1f, 1.45f), new Vector3(-0.88f, -0.1f, 1.45f), new Vector3(0.88f, -0.1f, -1.4f), new Vector3(-0.88f, -0.1f, -1.4f) };
            for (int i = 0; i < 4; i++)
            {
                var w = new GameObject("Wheel").transform; w.SetParent(t, false); w.localPosition = wp[i];
                Build.Part(PrimitiveType.Cylinder, w, Vector3.zero, new Vector3(0.9f, 0.16f, 0.9f), rubber, "Tyre", new Vector3(0, 0, 90));
                Build.Part(PrimitiveType.Cylinder, w, new Vector3(Mathf.Sign(wp[i].x) * 0.12f, 0, 0), new Vector3(0.5f, 0.05f, 0.5f), chrome, "Rim", new Vector3(0, 0, 90));
                Build.Part(PrimitiveType.Cube, w, new Vector3(Mathf.Sign(wp[i].x) * 0.17f, 0, 0), new Vector3(0.02f, 0.38f, 0.08f), dark, "Spoke");
                wheels[i] = w;
                Mudguard(b, wp[i], 0.45f, 0.3f, green);
            }
            layout = VehicleVisuals.TruckPrimitiveLayout();
        }

        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
        var exit = new GameObject("Exit").transform; exit.SetParent(t, false); exit.localPosition = new Vector3(-2.6f, 0.5f, 0.6f);

        var truck = root.AddComponent<FarmTruck>();
        truck.wheels = wheels;
        truck.cargoRoot = cargo;
        truck.exitPoint = exit;
        VehicleVisuals.Setup(truck, bodyRoot, layout);
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
        // Solid fence: one box per side (two on the front, either side of the gate) on the Ignore Raycast layer, so vehicles
        // and the player can't pass the rails but ground probes never land on them.
        for (int side = 0; side < 4; side++)
        {
            bool alongX = side % 2 == 0;
            float len = alongX ? s.x : s.y;
            float edge = alongX ? (side == 0 ? 1 : -1) * s.y / 2f : (side == 1 ? 1 : -1) * s.x / 2f;
            var runs = side == 0 ? new[] { new Vector2(-len / 2f, -1.6f), new Vector2(1.6f, len / 2f) } : new[] { new Vector2(-len / 2f, len / 2f) };
            foreach (var run in runs)
            {
                float mid = (run.x + run.y) * 0.5f, l = run.y - run.x;
                Vector3 local = alongX ? new Vector3(mid, 0f, edge) : new Vector3(edge, 0f, mid);
                var wall = new GameObject("Pen Fence");
                wall.layer = 2;
                wall.transform.SetPositionAndRotation(Build.OnGround(t.TransformPoint(local), 0.6f), t.rotation);
                wall.transform.SetParent(t, true);
                var bc = wall.AddComponent<BoxCollider>();
                bc.size = alongX ? new Vector3(l, 1.2f, 0.2f) : new Vector3(0.2f, 1.2f, l);
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

        // Water trough (interactable)
        var water = new GameObject("Water Trough");
        water.layer = 7;
        water.transform.position = Build.OnGround(t.TransformPoint(new Vector3(-s.x / 2f + 1.5f, 0, 0)));
        water.transform.rotation = t.rotation * Quaternion.Euler(0, 90, 0);
        var wc = water.AddComponent<BoxCollider>(); wc.center = new Vector3(0, 0.3f, 0); wc.size = new Vector3(2.4f, 0.6f, 0.8f);
        Build.Part(PrimitiveType.Cube, water.transform, new Vector3(0, 0.3f, 0), new Vector3(2.4f, 0.6f, 0.8f), Build.Mat(new Color(0.55f, 0.56f, 0.58f), 0.6f, 0.5f), "Tank");
        Build.Part(PrimitiveType.Cube, water.transform, new Vector3(0, 0.55f, 0), new Vector3(2.2f, 0.05f, 0.6f), Build.Mat(new Color(0.25f, 0.5f, 0.75f), 0.95f), "Water");
        foreach (var r in water.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
        water.AddComponent<PenTask>().kind = PenTaskKind.Water;
        water.transform.SetParent(t, true);

        // Barn in the back corner; its door is where you clean the pen.
        var barn = new GameObject("Barn");
        barn.transform.position = Build.OnGround(t.TransformPoint(new Vector3(s.x / 2f - 3.5f, 0, -s.y / 2f + 2.8f)));
        barn.transform.rotation = t.rotation;
        var barnRed = Build.Mat(new Color(0.6f, 0.13f, 0.1f), 0.2f);
        var trim = Build.Mat(new Color(0.95f, 0.93f, 0.88f), 0.2f);
        var roof = Build.Mat(new Color(0.25f, 0.24f, 0.26f), 0.4f, 0.3f);
        if (Build.TryModel("Barn", barn.transform) == null)
        {
            Build.Part(PrimitiveType.Cube, barn.transform, new Vector3(0, 1.6f, 0), new Vector3(5f, 3.2f, 4.5f), barnRed, "Walls");
            Build.Part(PrimitiveType.Cube, barn.transform, new Vector3(-1.3f, 3.7f, 0), new Vector3(3.2f, 0.15f, 4.8f), roof, "Roof", new Vector3(0, 0, 38));
            Build.Part(PrimitiveType.Cube, barn.transform, new Vector3(1.3f, 3.7f, 0), new Vector3(3.2f, 0.15f, 4.8f), roof, "Roof", new Vector3(0, 0, -38));
            Build.Part(PrimitiveType.Cube, barn.transform, new Vector3(0, 1.2f, 2.26f), new Vector3(2f, 2.4f, 0.05f), Build.Mat(new Color(0.45f, 0.1f, 0.08f), 0.2f), "Door");
            Build.Part(PrimitiveType.Cube, barn.transform, new Vector3(0, 1.2f, 2.29f), new Vector3(2.1f, 0.12f, 0.03f), trim, "Brace", new Vector3(0, 0, 50));
            Build.Part(PrimitiveType.Cube, barn.transform, new Vector3(0, 1.2f, 2.29f), new Vector3(2.1f, 0.12f, 0.03f), trim, "Brace", new Vector3(0, 0, -50));
        }
        var walls = barn.AddComponent<BoxCollider>(); walls.center = new Vector3(0, 1.6f, 0); walls.size = new Vector3(5f, 3.2f, 4.5f);
        barn.layer = 2; // Ignore Raycast: still solid, but never treated as ground
        var door = new GameObject("Barn Door");
        door.layer = 7;
        door.transform.SetParent(barn.transform, false);
        door.transform.localPosition = new Vector3(0, 0, 2.5f);
        var dc = door.AddComponent<BoxCollider>(); dc.center = new Vector3(0, 1.2f, 0); dc.size = new Vector3(2.2f, 2.4f, 0.4f);
        door.AddComponent<PenTask>().kind = PenTaskKind.Clean;
        barn.transform.SetParent(t, true);

        // Farm shop beside the pen so animals and feed can be bought early.
        BuildStall(t.TransformPoint(new Vector3(-3.5f, 0, s.y / 2f + 2.5f)), t.rotation * Quaternion.Euler(0, 180, 0));
        hay.SetActive(true);
    }

    private void BuildTractor(Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("Tractor");
        root.layer = 7;
        root.transform.SetPositionAndRotation(Build.OnGround(pos, 0.9f), rot);
        root.AddComponent<Rigidbody>();
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.75f, 0.2f); col.size = new Vector3(2.0f, 1.6f, 3.4f);
        col.material = new PhysicsMaterial("Tractor") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
        Transform t = root.transform;
        Transform bodyRoot = new GameObject("Body").transform; bodyRoot.SetParent(t, false);
        var red = VehicleVisuals.Paint(VehicleVisuals.TractorRed);
        var dark = Build.Mat(new Color(0.08f, 0.08f, 0.09f), 0.3f);
        var steel = Build.Mat(new Color(0.55f, 0.56f, 0.58f), 0.6f, 0.6f);
        var chrome = VehicleVisuals.Chrome;
        var glass = VehicleVisuals.Glass;
        var rubber = VehicleVisuals.Rubber;
        var yellow = Build.Mat(new Color(0.95f, 0.75f, 0.1f), 0.5f, 0.2f);
        var wheels = new Transform[4];
        VehicleVisuals.Layout layout;
        Build.MissingMaterialColor = new Color(0.72f, 0.12f, 0.08f);     // tractor red if the model's material is missing
        var tractorModel = Build.TryModel("Tractor", bodyRoot, 3.6f, -0.75f);
        Build.MissingMaterialColor = new Color(0.6f, 0.6f, 0.6f);
        if (tractorModel != null)
        {
            wheels = Build.FaceForwardAndFindWheels(tractorModel, t);
            VehicleVisuals.Restyle(tractorModel, wheels, Resources.Load<Texture2D>("Models/Textures/colormap_tractor"));
            layout = VehicleVisuals.ModelLayout(tractorModel, wheels, t, bodyRoot, true);
        }
        else
        {
            Transform b = bodyRoot;
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.55f, 0.9f), new Vector3(1.0f, 0.75f, 2.2f), red, "Engine");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.95f, 1.0f), new Vector3(0.9f, 0.1f, 1.9f), red, "Bonnet top", new Vector3(3f, 0, 0));
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.6f, 2.02f), new Vector3(0.95f, 0.6f, 0.06f), steel, "Grille");
            for (int i = 0; i < 4; i++) Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.42f + i * 0.12f, 2.05f), new Vector3(0.85f, 0.03f, 0.02f), dark, "Grille slat");
            Build.Part(PrimitiveType.Cylinder, b, new Vector3(0.3f, 1.4f, 1.4f), new Vector3(0.1f, 0.28f, 0.1f), chrome, "Exhaust stack");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.75f, -0.45f), new Vector3(1.6f, 0.35f, 1.3f), red, "Fenders");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 0.25f, 2.15f), new Vector3(1.0f, 0.3f, 0.25f), dark, "Front weights");
            // Cab: roof + four posts + glass
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 2.25f, -0.45f), new Vector3(1.55f, 0.1f, 1.5f), red, "Roof");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 2.18f, -0.45f), new Vector3(1.45f, 0.06f, 1.4f), Build.Mat(new Color(0.92f, 0.92f, 0.9f), 0.4f), "Roof liner");
            foreach (var px in new[] { -0.7f, 0.7f }) foreach (var pz in new[] { -1.1f, 0.2f })
                Build.Part(PrimitiveType.Cube, b, new Vector3(px, 1.6f, pz), new Vector3(0.07f, 1.3f, 0.07f), dark, "Post");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 1.6f, 0.22f), new Vector3(1.35f, 1.1f, 0.03f), glass, "Glass");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 1.6f, -1.12f), new Vector3(1.35f, 1.0f, 0.03f), glass, "Rear glass");
            Build.Part(PrimitiveType.Cube, b, new Vector3(0, 1.15f, -0.6f), new Vector3(0.5f, 0.12f, 0.5f), dark, "Seat");
            Build.Part(PrimitiveType.Cylinder, b, new Vector3(0, 1.45f, -0.05f), new Vector3(0.36f, 0.02f, 0.36f), dark, "Steering wheel", new Vector3(-55f, 0, 0));
            Vector3[] wp = { new Vector3(0.65f, -0.15f, 1.55f), new Vector3(-0.65f, -0.15f, 1.55f), new Vector3(0.95f, 0.15f, -0.45f), new Vector3(-0.95f, 0.15f, -0.45f) };
            float[] ws = { 0.75f, 0.75f, 1.45f, 1.45f };
            for (int i = 0; i < 4; i++)
            {
                var w = new GameObject("Wheel").transform; w.SetParent(t, false); w.localPosition = wp[i];
                Build.Part(PrimitiveType.Cylinder, w, Vector3.zero, new Vector3(ws[i], i < 2 ? 0.14f : 0.24f, ws[i]), rubber, "Tyre", new Vector3(0, 0, 90));
                Build.Part(PrimitiveType.Cylinder, w, new Vector3(Mathf.Sign(wp[i].x) * 0.14f, 0, 0), new Vector3(ws[i] * 0.55f, 0.04f, ws[i] * 0.55f), yellow, "Rim", new Vector3(0, 0, 90));
                for (int k = 0; k < 3; k++)
                    Build.Part(PrimitiveType.Cube, w, new Vector3(Mathf.Sign(wp[i].x) * 0.18f, 0, 0), new Vector3(0.03f, ws[i] * 0.9f, 0.08f), dark, "Lug", new Vector3(k * 60f, 0, 0));
                wheels[i] = w;
                if (i < 2) Mudguard(b, wp[i], ws[i] * 0.5f, 0.2f, red);   // front mudguards (the rear wheels sit under the fenders)
            }
            layout = VehicleVisuals.TractorPrimitiveLayout();
        }
        var hitch = new GameObject("Hitch").transform; hitch.SetParent(t, false); hitch.localPosition = new Vector3(0, 0, -2.6f);
        var exit = new GameObject("Exit").transform; exit.SetParent(t, false); exit.localPosition = new Vector3(-2.4f, 0.5f, 0f);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;

        var tractor = root.AddComponent<Tractor>();
        tractor.wheels = wheels; tractor.exitPoint = exit; tractor.hitch = hitch;
        tractor.rideHeight = 0.75f; tractor.footprint = new Vector2(0.95f, 1.5f); tractor.camDistance = 8f;
        VehicleVisuals.Setup(tractor, bodyRoot, layout);

        // Implements (shown one at a time behind the tractor)
        var plow = new GameObject("Plow"); plow.transform.SetParent(hitch, false);
        Build.Part(PrimitiveType.Cube, plow.transform, new Vector3(0, -0.2f, 0.5f), new Vector3(0.15f, 0.15f, 1.0f), dark);
        Build.Part(PrimitiveType.Cube, plow.transform, new Vector3(0, -0.25f, 0), new Vector3(2.4f, 0.15f, 0.3f), red);
        for (int i = 0; i < 4; i++) Build.Part(PrimitiveType.Cube, plow.transform, new Vector3(-0.9f + i * 0.6f, -0.5f, 0), new Vector3(0.12f, 0.5f, 0.5f), steel, "Share", new Vector3(30, 0, 0));
        var seeder = new GameObject("Seeder"); seeder.transform.SetParent(hitch, false);
        Build.Part(PrimitiveType.Cube, seeder.transform, new Vector3(0, 0.15f, 0), new Vector3(2.6f, 0.7f, 0.9f), yellow, "Hopper");
        Build.Part(PrimitiveType.Cube, seeder.transform, new Vector3(0, -0.35f, 0), new Vector3(2.8f, 0.12f, 0.5f), dark);
        for (int i = 0; i < 6; i++) Build.Part(PrimitiveType.Cylinder, seeder.transform, new Vector3(-1.25f + i * 0.5f, -0.55f, 0), new Vector3(0.08f, 0.2f, 0.08f), steel);
        var harvester = new GameObject("Harvester"); harvester.transform.SetParent(hitch, false);
        Build.Part(PrimitiveType.Cube, harvester.transform, new Vector3(0, 0f, 0), new Vector3(3f, 0.6f, 1.1f), Build.Mat(new Color(0.15f, 0.5f, 0.2f), 0.4f), "Header");
        Build.Part(PrimitiveType.Cylinder, harvester.transform, new Vector3(0, 0.1f, -0.5f), new Vector3(0.5f, 1.5f, 0.5f), yellow, "Reel", new Vector3(0, 0, 90));
        foreach (var r in hitch.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
        tractor.RegisterVisual(Implement.Plow, plow);
        tractor.RegisterVisual(Implement.Seeder, seeder);
        tractor.RegisterVisual(Implement.Harvester, harvester);
    }

    /// <summary>Mudguard over a wheel: a curved arch of three thin plates (primitive vehicles).</summary>
    private static void Mudguard(Transform parent, Vector3 wheelPos, float radius, float width, Material mat)
    {
        float r = radius + 0.08f;
        Build.Part(PrimitiveType.Cube, parent, wheelPos + new Vector3(0f, r, 0f), new Vector3(width, 0.05f, r * 0.9f), mat, "Mudguard");
        foreach (float s in new[] { -1f, 1f })
            Build.Part(PrimitiveType.Cube, parent, wheelPos + new Vector3(0f, r * 0.72f, s * r * 0.62f), new Vector3(width, 0.05f, r * 0.62f), mat, "Mudguard", new Vector3(s * 42f, 0f, 0f));
    }
}

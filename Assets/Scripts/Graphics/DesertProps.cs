using UnityEngine;

/// <summary>
/// Builders for Al-Qassim desert props: date palm, acacia (talh/samur), sidr tree, Najdi mud-brick farmhouse and
/// evaporative cooling pads / exhaust fans for greenhouse walls. A model dropped at Resources/Models/DatePalm,
/// Acacia, Sidr or NajdiHouse replaces the primitive version automatically.
/// Prop colliders use the Ignore Raycast layer so ground-following raycasts (actors, vehicles) never land on them.
/// </summary>
public static class DesertProps
{
    public const int PropLayer = 2; // Ignore Raycast

    private static Material M(float r, float g, float b, float smooth = 0.1f) => Build.Mat(new Color(r, g, b), smooth);

    private static void Blocker(GameObject go, Vector3 center, Vector3 size)
    {
        var c = go.AddComponent<BoxCollider>();
        c.center = center; c.size = size;
        go.layer = PropLayer;
    }

    // ------------------------------------------------------------------ date palm
    public static GameObject DatePalm(Transform parent, Vector3 pos, System.Random rnd)
    {
        var root = new GameObject("Date Palm");
        root.transform.SetParent(parent, true);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
        float height = 5.5f + (float)rnd.NextDouble() * 3.5f;
        if (Build.TryModel("DatePalm", root.transform, 0f, 0f) == null)
        {
            var bark = M(0.42f, 0.32f, 0.22f); var bark2 = M(0.36f, 0.27f, 0.18f);
            var frond = M(0.28f, 0.45f, 0.2f, 0.25f); var frondDry = M(0.55f, 0.48f, 0.28f);
            var dates = M(0.62f, 0.32f, 0.08f, 0.35f);
            float lean = ((float)rnd.NextDouble() - 0.5f) * 0.5f;
            const int segs = 7;
            Vector3 top = Vector3.zero;
            for (int i = 0; i < segs; i++)
            {
                float k = i / (float)segs;
                float y = height * (i + 0.5f) / segs;
                float w = Mathf.Lerp(0.55f, 0.38f, k);
                var seg = Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(lean * k * k * 2f, y, 0f), new Vector3(w, height / segs * 0.5f, w), i % 2 == 0 ? bark : bark2, "Trunk");
                seg.transform.localEulerAngles = new Vector3(0f, i * 25f, -lean * 12f * k);
                top = new Vector3(lean * 2f, height, 0f);
            }
            int fronds = 13;
            for (int i = 0; i < fronds; i++)
            {
                float yaw = i * 360f / fronds + (float)rnd.NextDouble() * 12f;
                bool dry = i % 5 == 4;
                float droop = dry ? 70f : 18f + (float)rnd.NextDouble() * 30f;
                var pivot = new GameObject("Frond").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = top;
                pivot.localEulerAngles = new Vector3(droop, yaw, 0f);
                float len = 2.6f + (float)rnd.NextDouble() * 0.8f;
                Build.Part(PrimitiveType.Cube, pivot, new Vector3(0f, 0f, len * 0.5f), new Vector3(0.9f, 0.05f, len), dry ? frondDry : frond, "Leaf");
                Build.Part(PrimitiveType.Cube, pivot, new Vector3(0f, -0.15f, len * 0.95f), new Vector3(0.55f, 0.04f, len * 0.4f), dry ? frondDry : frond, "Tip", new Vector3(25f, 0f, 0f));
            }
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f + 30f;
                Vector3 off = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 0.45f);
                Build.Part(PrimitiveType.Sphere, root.transform, top + off + new Vector3(0f, -0.55f, 0f), new Vector3(0.45f, 0.7f, 0.45f), dates, "Dates");
            }
            Build.Part(PrimitiveType.Sphere, root.transform, top + new Vector3(0, 0.05f, 0), new Vector3(0.7f, 0.5f, 0.7f), frond, "Heart");
        }
        Blocker(root, new Vector3(0f, 1.5f, 0f), new Vector3(0.6f, 3f, 0.6f));
        return root;
    }

    // ------------------------------------------------------------------ acacia / sidr
    public static GameObject Acacia(Transform parent, Vector3 pos, System.Random rnd)
    {
        var root = new GameObject("Acacia");
        root.transform.SetParent(parent, true);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
        float s = 0.8f + (float)rnd.NextDouble() * 0.6f;
        root.transform.localScale = Vector3.one * s;
        if (Build.TryModel("Acacia", root.transform, 0f, 0f) == null)
        {
            var bark = M(0.35f, 0.28f, 0.22f); var leaf = M(0.45f, 0.5f, 0.27f, 0.15f); var leaf2 = M(0.5f, 0.52f, 0.3f, 0.15f);
            Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 1f, 0f), new Vector3(0.28f, 1f, 0.28f), bark, "Trunk", new Vector3(0f, 0f, 8f));
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f + (float)rnd.NextDouble() * 40f;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 2.3f, 0f) + dir * 0.6f, new Vector3(0.14f, 0.75f, 0.14f), bark, "Branch",
                    new Vector3(dir.z * 40f, 0f, -dir.x * 40f));
                Build.Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 3.1f, 0f) + dir * 1.4f, new Vector3(3.2f, 0.55f, 2.6f), i == 1 ? leaf2 : leaf, "Canopy");
            }
            Build.Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 3.35f, 0f), new Vector3(3.6f, 0.6f, 3.6f), leaf, "Canopy");
        }
        Blocker(root, new Vector3(0f, 1f, 0f), new Vector3(0.5f, 2f, 0.5f));
        return root;
    }

    public static GameObject Sidr(Transform parent, Vector3 pos, System.Random rnd)
    {
        var root = new GameObject("Sidr");
        root.transform.SetParent(parent, true);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
        float s = 0.8f + (float)rnd.NextDouble() * 0.5f;
        root.transform.localScale = Vector3.one * s;
        if (Build.TryModel("Sidr", root.transform, 0f, 0f) == null)
        {
            var bark = M(0.3f, 0.24f, 0.19f); var leaf = M(0.27f, 0.4f, 0.19f, 0.2f); var leaf2 = M(0.32f, 0.44f, 0.22f, 0.2f);
            Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.8f, 0f), new Vector3(0.32f, 0.8f, 0.32f), bark, "Trunk");
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f + (float)rnd.NextDouble() * 30f;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                float r = 1.6f + (float)rnd.NextDouble() * 0.6f;
                Build.Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 2.2f + (float)rnd.NextDouble() * 0.5f, 0f) + dir * 0.9f, new Vector3(r, r * 0.8f, r), i % 2 == 0 ? leaf : leaf2, "Crown");
            }
            Build.Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 2.9f, 0f), new Vector3(2.2f, 1.6f, 2.2f), leaf, "Crown");
        }
        Blocker(root, new Vector3(0f, 1f, 0f), new Vector3(0.6f, 2f, 0.6f));
        return root;
    }

    // ------------------------------------------------------------------ Najdi farmhouse
    /// <summary>Mud-brick (tin) farmhouse: thick walls, stepped crenellations, rows of triangular vents, wooden door,
    /// water spouts and a corner tower. Footprint about 11 x 9 m; front (door) faces +Z of rot.</summary>
    public static GameObject NajdiHouse(Transform parent, Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("Najdi Farmhouse");
        root.transform.SetParent(parent, true);
        root.transform.SetPositionAndRotation(pos, rot);
        var t = root.transform;
        if (Build.TryModel("NajdiHouse", t, 11f, 0f) == null)
        {
            var mud = M(0.7f, 0.53f, 0.36f, 0.05f); var mudDark = M(0.6f, 0.44f, 0.29f, 0.05f);
            var plaster = M(0.86f, 0.78f, 0.64f, 0.08f); var hole = M(0.12f, 0.08f, 0.06f, 0f);
            var wood = M(0.36f, 0.22f, 0.12f, 0.2f); var woodLight = M(0.62f, 0.45f, 0.25f, 0.2f);
            var tri = ProcMesh.TrianglePrism();

            // main block, base band, parapet
            Build.Part(PrimitiveType.Cube, t, new Vector3(0f, 2.2f, 0f), new Vector3(10f, 4.4f, 8f), mud, "Walls");
            Build.Part(PrimitiveType.Cube, t, new Vector3(0f, 0.35f, 0f), new Vector3(10.2f, 0.7f, 8.2f), mudDark, "Base");
            Build.Part(PrimitiveType.Cube, t, new Vector3(0f, 4.55f, 0f), new Vector3(10.15f, 0.3f, 8.15f), plaster, "Band");
            // corner tower (back-left)
            Build.Part(PrimitiveType.Cube, t, new Vector3(-4.2f, 3.3f, -3.2f), new Vector3(3f, 6.6f, 3f), mud, "Tower");
            Build.Part(PrimitiveType.Cube, t, new Vector3(-4.2f, 6.7f, -3.2f), new Vector3(3.1f, 0.25f, 3.1f), plaster, "TowerBand");

            // stepped/triangular crenellations (shurafat) along the roof edge and tower
            Crenels(t, tri, plaster, new Vector3(-5f, 4.7f, 4f), new Vector3(5f, 4.7f, 4f), 0f);
            Crenels(t, tri, plaster, new Vector3(-5f, 4.7f, -4f), new Vector3(-2.7f, 4.7f, -4f), 0f);
            Crenels(t, tri, plaster, new Vector3(5f, 4.7f, -4f), new Vector3(5f, 4.7f, 4f), 90f);
            Crenels(t, tri, plaster, new Vector3(-5f, 4.7f, -1.7f), new Vector3(-5f, 4.7f, 4f), 90f);
            Crenels(t, tri, plaster, new Vector3(-5.7f, 6.85f, -1.7f), new Vector3(-2.7f, 6.85f, -1.7f), 0f);

            // rows of triangular vents high on the walls (front, sides) and on the tower
            for (int i = 0; i < 7; i++)
                Shape(t, tri, hole, new Vector3(-3.6f + i * 1.2f, 3.6f, 4.02f), new Vector3(0.45f, 0.5f, 0.06f), 0f);
            for (int i = 0; i < 5; i++)
            {
                Shape(t, tri, hole, new Vector3(5.02f, 3.6f, -2.6f + i * 1.3f), new Vector3(0.45f, 0.5f, 0.06f), 90f);
                Shape(t, tri, hole, new Vector3(-5.02f, 3.6f, -0.2f + i * 1.0f), new Vector3(0.45f, 0.5f, 0.06f), 90f);
            }
            for (int i = 0; i < 3; i++)
                Shape(t, tri, hole, new Vector3(-5.2f + i * 1f, 5.6f, -1.68f), new Vector3(0.4f, 0.45f, 0.06f), 0f);

            // wooden door with a painted frame (Najdi doors are decorated), small windows with shutters
            Build.Part(PrimitiveType.Cube, t, new Vector3(1.2f, 1.3f, 4.03f), new Vector3(1.9f, 2.6f, 0.08f), plaster, "DoorFrame");
            Build.Part(PrimitiveType.Cube, t, new Vector3(1.2f, 1.2f, 4.07f), new Vector3(1.5f, 2.4f, 0.08f), wood, "Door");
            for (int i = 0; i < 4; i++)
                Build.Part(PrimitiveType.Cube, t, new Vector3(1.2f, 0.4f + i * 0.55f, 4.12f), new Vector3(1.4f, 0.06f, 0.03f), woodLight, "DoorBand");
            Shape(t, tri, woodLight, new Vector3(1.2f, 2.45f, 4.1f), new Vector3(1.2f, 0.45f, 0.05f), 0f);
            foreach (float x in new[] { -3f, 3.8f })
            {
                Build.Part(PrimitiveType.Cube, t, new Vector3(x, 1.8f, 4.03f), new Vector3(0.9f, 0.9f, 0.06f), plaster, "WindowFrame");
                Build.Part(PrimitiveType.Cube, t, new Vector3(x, 1.8f, 4.07f), new Vector3(0.7f, 0.7f, 0.06f), wood, "Shutter");
            }
            // wooden water spouts (marazim) and protruding roof beams
            foreach (float x in new[] { -3.5f, 0f, 3.5f })
                Build.Part(PrimitiveType.Cube, t, new Vector3(x, 4.25f, 4.35f), new Vector3(0.18f, 0.14f, 0.8f), wood, "Spout");
            for (int i = 0; i < 6; i++)
                Build.Part(PrimitiveType.Cylinder, t, new Vector3(5.15f, 4.15f, -3f + i * 1.2f), new Vector3(0.1f, 0.2f, 0.1f), wood, "Beam", new Vector3(0f, 0f, 90f));
            // low courtyard wall stub and a bench (dakka) by the door
            Build.Part(PrimitiveType.Cube, t, new Vector3(-0.8f, 0.3f, 4.5f), new Vector3(2f, 0.6f, 0.8f), mudDark, "Dakka");
        }
        var c = root.AddComponent<BoxCollider>(); c.center = new Vector3(0f, 2.3f, 0f); c.size = new Vector3(10.2f, 4.6f, 8.2f);
        var tower = new GameObject("TowerCollider"); tower.transform.SetParent(t, false);
        var tc = tower.AddComponent<BoxCollider>(); tc.center = new Vector3(-4.2f, 3.3f, -3.2f); tc.size = new Vector3(3f, 6.6f, 3f);
        var bench = new GameObject("DakkaCollider"); bench.transform.SetParent(t, false);
        var bc = bench.AddComponent<BoxCollider>(); bc.center = new Vector3(-0.8f, 0.3f, 4.5f); bc.size = new Vector3(2f, 0.6f, 0.8f);
        root.layer = PropLayer; tower.layer = PropLayer; bench.layer = PropLayer;
        return root;
    }

    private static void Crenels(Transform t, Mesh tri, Material mat, Vector3 from, Vector3 to, float yaw)
    {
        float len = Vector3.Distance(from, to);
        int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.9f));
        for (int i = 0; i <= n; i++)
        {
            Vector3 p = Vector3.Lerp(from, to, i / (float)n);
            Shape(t, tri, mat, p, new Vector3(0.55f, 0.6f, 0.25f), yaw);
        }
    }

    /// <summary>A triangle prism part (scale.x = base width, scale.y = height, scale.z = depth).</summary>
    public static GameObject Shape(Transform parent, Mesh mesh, Material mat, Vector3 localPos, Vector3 scale, float yaw)
    {
        var go = new GameObject("Triangle");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    // ------------------------------------------------------------------ greenhouse cooling
    /// <summary>
    /// Evaporative cooling pad (wet cellulose honeycomb with water pipe and gutter) on one long wall and exhaust fans on
    /// the opposite wall of a greenhouse, from its world bounds. Visual only (no colliders), mounted flush on the wall.
    /// </summary>
    public static GameObject CoolingPads(Transform parent, Bounds b, float groundY)
    {
        var root = new GameObject("Cooling Pads");
        root.transform.SetParent(parent, true);
        bool longX = b.size.x >= b.size.z;
        float length = longX ? b.size.x : b.size.z;
        float depth = longX ? b.size.z : b.size.x;
        float wallH = Mathf.Clamp(b.max.y - groundY, 2.5f, 8f);
        Vector3 along = longX ? Vector3.right : Vector3.forward;
        Vector3 outward = longX ? Vector3.forward : Vector3.right;
        Vector3 centre = new Vector3(b.center.x, groundY, b.center.z);
        var pad = M(0.66f, 0.52f, 0.32f, 0.35f); var padDark = M(0.5f, 0.38f, 0.22f, 0.3f);
        var steel = Build.Mat(new Color(0.62f, 0.64f, 0.66f), 0.6f, 0.6f); var dark = M(0.12f, 0.12f, 0.13f, 0.4f);
        float padLen = length * 0.7f, padH = Mathf.Min(2f, wallH * 0.45f);
        Quaternion face = Quaternion.LookRotation(outward);

        // pad wall (+outward side)
        var padRoot = new GameObject("Pad").transform; padRoot.SetParent(root.transform, false);
        padRoot.SetPositionAndRotation(centre + outward * (depth * 0.5f + 0.08f), face);
        Build.Part(PrimitiveType.Cube, padRoot, new Vector3(0f, 0.4f + padH * 0.5f, 0f), new Vector3(padLen, padH, 0.12f), pad, "Cellulose");
        int strips = Mathf.Max(4, Mathf.RoundToInt(padLen / 0.6f));
        for (int i = 0; i <= strips; i++)
            Build.Part(PrimitiveType.Cube, padRoot, new Vector3(-padLen * 0.5f + i * padLen / strips, 0.4f + padH * 0.5f, 0.07f), new Vector3(0.04f, padH, 0.02f), padDark, "Flute");
        Build.Part(PrimitiveType.Cylinder, padRoot, new Vector3(0f, 0.5f + padH, 0.04f), new Vector3(0.12f, padLen * 0.5f, 0.12f), steel, "WaterPipe", new Vector3(0f, 0f, 90f));
        Build.Part(PrimitiveType.Cube, padRoot, new Vector3(0f, 0.32f, 0.08f), new Vector3(padLen, 0.16f, 0.25f), steel, "Gutter");
        Build.Part(PrimitiveType.Cube, padRoot, new Vector3(padLen * 0.5f + 0.4f, 0.45f, 0.4f), new Vector3(0.6f, 0.9f, 0.6f), steel, "PumpTank");

        // exhaust fans (-outward side)
        var fanRoot = new GameObject("Fans").transform; fanRoot.SetParent(root.transform, false);
        fanRoot.SetPositionAndRotation(centre - outward * (depth * 0.5f + 0.08f), Quaternion.LookRotation(-outward));
        int fans = Mathf.Clamp(Mathf.RoundToInt(length / 8f), 2, 4);
        for (int i = 0; i < fans; i++)
        {
            float x = (i - (fans - 1) * 0.5f) * (length * 0.7f / fans);
            Build.Part(PrimitiveType.Cube, fanRoot, new Vector3(x, 1.3f, 0f), new Vector3(1.3f, 1.3f, 0.14f), steel, "FanBox");
            Build.Part(PrimitiveType.Cylinder, fanRoot, new Vector3(x, 1.3f, 0.06f), new Vector3(1.1f, 0.02f, 1.1f), dark, "Fan", new Vector3(90f, 0f, 0f));
            for (int k = 0; k < 4; k++)
                Build.Part(PrimitiveType.Cube, fanRoot, new Vector3(x, 1.3f, 0.09f), new Vector3(1.15f, 0.04f, 0.02f), steel, "Guard", new Vector3(0f, 0f, k * 45f));
        }
        return root;
    }

    // ------------------------------------------------------------------ Nile Delta props (primitives, shared cached materials)
    /// <summary>Eucalyptus (kafour): tall pale trunk with a narrow, open blue-green crown; planted as windbreaks along Delta
    /// canals and roads. A model at Resources/Models/Eucalyptus replaces it.</summary>
    public static GameObject Eucalyptus(Transform parent, Vector3 pos, System.Random rnd)
    {
        var root = new GameObject("Eucalyptus");
        root.transform.SetParent(parent, true);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
        float s = 0.85f + (float)rnd.NextDouble() * 0.4f;
        root.transform.localScale = Vector3.one * s;
        if (Build.TryModel("Eucalyptus", root.transform, 0f, 0f) == null)
        {
            var bark = M(0.72f, 0.68f, 0.6f); var leaf = M(0.33f, 0.45f, 0.36f, 0.2f); var leaf2 = M(0.4f, 0.5f, 0.38f, 0.2f);
            Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 3f, 0f), new Vector3(0.3f, 3f, 0.3f), bark, "Trunk");
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + (float)rnd.NextDouble() * 40f;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                float h = 5.2f + i * 0.9f;
                Build.Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, h, 0f) + dir * 0.55f, new Vector3(1.6f, 2.2f, 1.6f), i % 2 == 0 ? leaf : leaf2, "Crown");
            }
            Build.Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 8.6f, 0f), new Vector3(1.3f, 1.8f, 1.3f), leaf, "Crown");
        }
        Blocker(root, new Vector3(0f, 1.5f, 0f), new Vector3(0.5f, 3f, 0.5f));
        return root;
    }

    /// <summary>Delta dovecote (burg hamam): a tapering mud-brick tower with rows of clay pots for pigeons, about 7 m high.</summary>
    public static GameObject PigeonTower(Transform parent, Vector3 pos)
    {
        var root = new GameObject("Pigeon Tower");
        root.transform.SetParent(parent, true);
        root.transform.position = pos;
        if (Build.TryModel("PigeonTower", root.transform, 0f, 0f) == null)
        {
            var mud = M(0.62f, 0.5f, 0.36f); var lime = M(0.9f, 0.88f, 0.82f); var pot = M(0.24f, 0.17f, 0.12f);
            Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 1.5f, 0f), new Vector3(3.2f, 1.5f, 3.2f), mud, "Base");
            Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 4.2f, 0f), new Vector3(2.5f, 1.2f, 2.5f), lime, "Tower");
            Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 6.1f, 0f), new Vector3(1.7f, 0.7f, 1.7f), lime, "Top");
            Build.Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 7f, 0f), new Vector3(1.4f, 0.9f, 1.4f), mud, "Dome");
            for (int ring = 0; ring < 2; ring++)
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 45f + ring * 22.5f, r = ring == 0 ? 1.27f : 0.87f, y = ring == 0 ? 4f : 6f;
                    Vector3 d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    Build.Part(PrimitiveType.Cube, root.transform, d * r + new Vector3(0f, y, 0f), new Vector3(0.22f, 0.22f, 0.06f), pot, "Nest", new Vector3(0f, a, 0f));
                }
        }
        Blocker(root, new Vector3(0f, 1.5f, 0f), new Vector3(3f, 3f, 3f));
        return root;
    }

    /// <summary>Small market building with a flat roof and a coloured awning (cotton gin, rice mill, souq stall); door faces +Z of rot
    /// (toward the farm road). About 7 x 5 m.</summary>
    public static GameObject SellPoint(Transform parent, Vector3 pos, Quaternion rot, Color color)
    {
        var root = new GameObject("Sell Point");
        root.transform.SetParent(parent, true);
        root.transform.SetPositionAndRotation(pos, rot);
        var wall = Build.Mat(color, 0.08f); var roof = M(0.35f, 0.3f, 0.26f); var door = M(0.3f, 0.2f, 0.13f);
        var awning = Build.Mat(new Color(Mathf.Clamp01(color.r * 0.6f + 0.3f), color.g * 0.45f, color.b * 0.35f), 0.2f);
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 1.7f, 0f), new Vector3(7f, 3.4f, 5f), wall, "Walls");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 3.5f, 0f), new Vector3(7.4f, 0.2f, 5.4f), roof, "Roof");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 1.1f, 2.51f), new Vector3(1.8f, 2.2f, 0.05f), door, "Door");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 2.6f, 3.1f), new Vector3(5.5f, 0.08f, 1.4f), awning, "Awning", new Vector3(-12f, 0f, 0f));
        for (int i = 0; i < 3; i++)
            Build.Part(PrimitiveType.Cube, root.transform, new Vector3(-2.2f + i * 2.2f, 0.4f, 3.3f), new Vector3(0.8f, 0.8f, 0.8f), M(0.85f, 0.82f, 0.74f), "Sack");
        Blocker(root, new Vector3(0f, 1.7f, 0f), new Vector3(7f, 3.4f, 5f));
        return root;
    }
}

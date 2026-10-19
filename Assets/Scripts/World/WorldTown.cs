using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The open world's town (the "town" place): a real street grid instead of a few boxes. Main street (asphalt with dashed
/// lines) from the access road through a paved market square (the selling pad, souq stalls, palms), two cross streets and
/// two side streets, raised sidewalks with sloped edges (no kerb to snag on) and ~45 terraced buildings on lots:
/// - every building is a shell of walls with real openings (doors, windows, shop fronts: reveals + recessed glass / wood),
///   floor cornices, parapets (Najdi crenellations or Delta rebar "next floor" columns), roof water tanks, AC units, dishes,
///   stair kiosks, Najdi wooden mashrabiya bays or Delta balconies, striped shop awnings and EN / AR shop signs;
/// - working places: seed & fertiliser shop, general store, vehicle dealership (showroom with real tractor / truck
///   models), animal market (pen with sheep), bank (loans), cafe (tea), the town fuel station (refuels a parked
///   vehicle), the mosque as the landmark (dome + minaret), souq stalls on the square;
/// - life: regional characters (thobe / ghutra and abaya in Najd, galabeya and tarha on the Delta) walking the sidewalks
///   and stopping at shop doors, sitting at the cafe, keeping and visiting the stalls; town cars driving two loops (they
///   stop for the player and pedestrians) and cars parked at the kerb.
/// Materials are the graphics makeover's PBR set (LookLibrary: mud brick, plaster, concrete, wood, metal, linen...),
/// with world box UVs (WorldMesh.Emit). Everything is merged per material and static batched; life is culled by distance.
/// Najd = Najdi mud-brick and plaster town; Nile Delta = Egyptian village town (red brick, pastel plaster, concrete frames).
/// Local frame: +z faces the access road, origin = the market square (selling pad), y = 0 = the flattened pad.
/// </summary>
public static class WorldTown
{
    public const float Radius = 78f;          // flattened pad around the grid
    public const float Extent = 56f;          // half size of the street grid
    public const float GateDistance = 60f;    // where the access road meets the main street
    public const float StreetHalf = 4f, Walk = 3f, WalkHeight = 0.12f;
    public const float PlazaX = 15f, PlazaZ = 12f;
    public const float FuelPumpRadius = 9f;
    public static readonly float[] CrossZ = { 30f, -30f };
    public static readonly float[] SideX = { 40f, -40f };

    public enum Use { House, Shop, Mosque, Bank, SeedShop, Dealer, AnimalMarket, GeneralStore, Cafe, Fuel }

    public class Lot
    {
        public Use use;
        public Vector3 c;            // footprint centre (local)
        public float w, d, yaw;      // frontage, depth, facing (front = local +z of the lot turned by yaw)
        public int floors, style;
        public bool openLeft, openRight;
        public string nameEn = "", nameAr = "";
        public float Height;         // walls + parapet (set when built)
        public Quaternion Rot => Quaternion.Euler(0f, yaw, 0f);
        public Vector3 Facing => Rot * Vector3.forward;
        public Vector3 P(float x, float y, float z) => c + Rot * new Vector3(x, y, z);
    }

    public struct Spot { public Vector3 p; public float yaw; public Spot(Vector3 p, float yaw) { this.p = p; this.yaw = yaw; } }
    public struct Sign { public Vector3 p; public float yaw; public string en, ar; public float size; }
    public struct ShopSpot { public Spot s; public string category, en, ar; public Use use; }
    public struct Prop { public Spot s; public string model; public float length; public bool solid; }
    public struct Area { public float x0, z0, x1, z1; public Area(float x0, float z0, float x1, float z1) { this.x0 = x0; this.z0 = z0; this.x1 = x1; this.z1 = z1; } public bool Contains(Vector3 p, float pad = 0f) => p.x > x0 - pad && p.x < x1 + pad && p.z > z0 - pad && p.z < z1 + pad; }

    /// <summary>Everything the town is made of (pure data; tests read it).</summary>
    public class Plan
    {
        public bool delta;
        public readonly List<Lot> lots = new List<Lot>();
        public readonly List<Sign> signs = new List<Sign>();
        public readonly List<ShopSpot> shops = new List<ShopSpot>();
        public readonly List<(Vector3 p, TreeType type)> trees = new List<(Vector3, TreeType)>();
        public readonly List<List<Vector3>> walks = new List<List<Vector3>>();
        public readonly List<Spot> doors = new List<Spot>();
        public readonly List<Spot> stalls = new List<Spot>();
        public readonly List<Spot> seats = new List<Spot>();
        public readonly List<List<Vector3>> carLoops = new List<List<Vector3>>();
        public readonly List<Prop> props = new List<Prop>();
        public readonly List<Area> streets = new List<Area>();      // carriageways: no building, tree or prop on them
        public Vector3 fuel, pen;
        public float penRadius;
        public int Buildings => lots.Count;
    }

    /// <summary>The town's live objects (OpenWorld.Town).</summary>
    public class Info
    {
        public Plan plan;
        public Transform root, fuel;
        public readonly List<Interactable> shops = new List<Interactable>();
        public readonly List<TownWalker> walkers = new List<TownWalker>();
        public readonly List<TownCar> cars = new List<TownCar>();
        public int people, trees, parked;
    }

    /// <summary>The plan of the last BuildMesh (Populate uses it right after, tests read it).</summary>
    public static Plan LastPlan { get; private set; }

    // ------------------------------------------------------------------ materials

    private class Mats
    {
        public Material[] walls; public Material trim, roof, wood, door, metal, tank, glass, dark, asphalt, walk, kerb, plaza, paint, corrugated, board, stone, canvasA, canvasB, hessian, dome, green, steel;
    }

    private static Mats MakeMats(bool delta)
    {
        var m = new Mats();
        if (!delta)
            m.walls = new[]
            {
                LookLibrary.Tinted(LookMat.MudBrick, Color.white), LookLibrary.Tinted(LookMat.MudBrick, new Color(0.9f, 0.84f, 0.76f)),
                LookLibrary.Tinted(LookMat.Plaster, new Color(0.97f, 0.92f, 0.82f)), LookLibrary.Tinted(LookMat.Plaster, new Color(0.93f, 0.84f, 0.7f)),
                LookLibrary.Tinted(LookMat.Plaster, Color.white), LookLibrary.Tinted(LookMat.MudBrick, new Color(0.82f, 0.72f, 0.6f)),
            };
        else
            m.walls = new[]
            {
                LookLibrary.Tinted(LookMat.MudBrick, new Color(0.78f, 0.45f, 0.34f)), LookLibrary.Tinted(LookMat.Plaster, new Color(0.96f, 0.88f, 0.68f)),
                LookLibrary.Tinted(LookMat.Plaster, new Color(0.76f, 0.86f, 0.9f)), LookLibrary.Tinted(LookMat.Plaster, new Color(0.95f, 0.76f, 0.66f)),
                LookLibrary.Tinted(LookMat.Concrete, Color.white), LookLibrary.Tinted(LookMat.MudBrick, new Color(0.7f, 0.4f, 0.3f)),
            };
        m.trim = LookLibrary.Tinted(LookMat.Concrete, new Color(0.95f, 0.93f, 0.88f));
        m.roof = LookLibrary.Tinted(LookMat.Concrete, new Color(0.8f, 0.77f, 0.72f));
        m.wood = LookLibrary.Tinted(LookMat.Wood, new Color(0.75f, 0.58f, 0.42f));
        m.door = LookLibrary.Tinted(LookMat.Wood, delta ? new Color(0.45f, 0.55f, 0.75f) : new Color(0.4f, 0.62f, 0.58f));
        m.metal = LookLibrary.Tinted(LookMat.Metal, new Color(0.92f, 0.92f, 0.9f), 0.4f);
        m.steel = LookLibrary.Tinted(LookMat.Metal, new Color(0.55f, 0.56f, 0.58f), 0.5f);
        m.tank = delta ? LookLibrary.Tinted(LookMat.Metal, new Color(0.15f, 0.15f, 0.16f), 0.3f) : LookLibrary.Tinted(LookMat.Metal, Color.white, 0.5f);
        m.glass = Build.Mat(new Color(0.1f, 0.13f, 0.16f), 0.92f);
        m.dark = Build.Mat(new Color(0.08f, 0.08f, 0.09f), 0.2f);
        m.asphalt = LookLibrary.Tinted(LookMat.Concrete, new Color(0.34f, 0.34f, 0.36f), 0.2f);
        m.walk = LookLibrary.Tinted(LookMat.Concrete, new Color(0.86f, 0.83f, 0.78f));
        m.kerb = LookLibrary.Tinted(LookMat.Concrete, new Color(0.95f, 0.95f, 0.93f));
        m.plaza = delta ? LookLibrary.Tinted(LookMat.Concrete, new Color(0.88f, 0.8f, 0.7f)) : LookLibrary.Tinted(LookMat.Plaster, new Color(0.92f, 0.85f, 0.72f));
        m.paint = Build.Mat(new Color(0.95f, 0.93f, 0.85f), 0.3f);
        m.corrugated = LookLibrary.Tinted(LookMat.Corrugated, Color.white);
        m.board = Build.Mat(new Color(0.1f, 0.32f, 0.22f), 0.35f);
        m.stone = LookLibrary.Tinted(LookMat.Concrete, new Color(0.96f, 0.92f, 0.84f));
        m.canvasA = LookLibrary.Tinted(LookMat.Linen, new Color(0.85f, 0.3f, 0.2f));
        m.canvasB = LookLibrary.Tinted(LookMat.Linen, new Color(0.96f, 0.94f, 0.88f));
        m.hessian = LookLibrary.Tinted(LookMat.Hessian, Color.white);
        m.dome = delta ? LookLibrary.Tinted(LookMat.Plaster, new Color(0.62f, 0.82f, 0.74f)) : LookLibrary.Tinted(LookMat.Plaster, new Color(0.98f, 0.96f, 0.9f));
        m.green = LookLibrary.Tinted(LookMat.Metal, new Color(0.3f, 0.6f, 0.45f), 0.4f);
        return m;
    }

    private static readonly Color[] AwningColors = { new Color(0.85f, 0.3f, 0.2f), new Color(0.2f, 0.45f, 0.72f), new Color(0.25f, 0.55f, 0.32f), new Color(0.92f, 0.68f, 0.2f), new Color(0.55f, 0.25f, 0.45f) };

    private static readonly (string en, string ar)[] NajdShops =
    {
        ("Al-Noor Grocery", "بقالة النور"), ("Tailor", "خياط"), ("Bakery", "مخبز"), ("Spices & Dates", "بهارات وتمور"), ("Hardware", "أدوات وعدد"),
        ("Barber", "حلاق"), ("Mobile phones", "جوالات"), ("Perfumes", "عطور"), ("Restaurant", "مطعم"), ("Fabrics", "أقمشة"),
    };
    private static readonly (string en, string ar)[] DeltaShops =
    {
        ("Koshary", "كشري"), ("Ful & Taameya", "فول وطعمية"), ("Bakery", "مخبز"), ("Tailor", "خياط"), ("Hardware", "أدوات وعدد"),
        ("Barber", "حلاق"), ("Mobile phones", "موبايلات"), ("Fabrics", "أقمشة"), ("Restaurant", "مطعم"), ("Spices", "عطارة"),
    };

    // ------------------------------------------------------------------ plan

    /// <summary>Lay out lots, streets, trees, walks, car loops (deterministic for a seed).</summary>
    public static Plan MakePlan(bool delta, int seed)
    {
        var pl = new Plan { delta = delta };
        var rnd = new System.Random(seed);
        // carriageways
        pl.streets.Add(new Area(-StreetHalf, -Extent, StreetHalf, GateDistance));
        foreach (float z in CrossZ) pl.streets.Add(new Area(-Extent, z - StreetHalf, Extent, z + StreetHalf));
        foreach (float x in SideX) pl.streets.Add(new Area(x - StreetHalf, -Extent, x + StreetHalf, Extent));
        foreach (int s in new[] { 1, -1 })
        {
            bool east = s > 0;
            // inner north: front row (main street) / back row (side street)
            if (east) Row(pl, rnd, s, 7.5f, 18.5f, 37.5f, 56f, -s, 1, Use.Fuel);
            else Row(pl, rnd, s, 7.5f, 18.5f, 37.5f, 56f, -s, 1, Use.Dealer);
            Row(pl, rnd, s, 21.5f, 32.5f, 37.5f, 56f, s, 2, Use.House);
            // inner middle: two lots beside the square, two small shops facing it, the back row
            Row(pl, rnd, s, 7.5f, 18.5f, 12f, 22.5f, -s, 1, east ? Use.Bank : Use.Shop);
            Row(pl, rnd, s, 7.5f, 18.5f, -22.5f, -12f, -s, 1, east ? Use.Cafe : Use.Shop);
            Row(pl, rnd, s, 15.5f, 21f, 0f, 12f, -s, 1, east ? Use.SeedShop : Use.GeneralStore);
            Row(pl, rnd, s, 15.5f, 21f, -12f, 0f, -s, 1, Use.Shop);
            if (east) Row(pl, rnd, s, 21.5f, 32.5f, -22.5f, 22.5f, s, 4, Use.House);
            else Row(pl, rnd, s, 21.5f, 32.5f, -22.5f, 22.5f, s, 1, Use.Mosque);
            // inner south
            if (east) Row(pl, rnd, s, 7.5f, 32.5f, -56f, -37.5f, -s, 1, Use.AnimalMarket);
            else { Row(pl, rnd, s, 7.5f, 18.5f, -56f, -37.5f, -s, 2, Use.Shop); Row(pl, rnd, s, 21.5f, 32.5f, -56f, -37.5f, s, 2, Use.House); }
            // outer column (faces the side street)
            Row(pl, rnd, s, 47.5f, 56f, 37.5f, 56f, -s, 2, Use.House);
            Row(pl, rnd, s, 47.5f, 56f, -22.5f, 22.5f, -s, 4, Use.House);
            Row(pl, rnd, s, 47.5f, 56f, -56f, -37.5f, -s, 2, Use.House);
        }
        // shop names (no two alike)
        var names = new List<(string, string)>();
        foreach (var sh in delta ? DeltaShops : NajdShops) names.Add((sh.en, sh.ar));
        foreach (var l in pl.lots)
        {
            switch (l.use)
            {
                case Use.Shop:
                    if (names.Count == 0) { l.use = Use.House; break; }
                    int k = rnd.Next(names.Count); l.nameEn = names[k].Item1; l.nameAr = names[k].Item2; names.RemoveAt(k); break;
                case Use.Bank: l.nameEn = "Farmers' Bank"; l.nameAr = "بنك المزارعين"; break;
                case Use.SeedShop: l.nameEn = "Seeds & Fertiliser"; l.nameAr = "بذور وأسمدة"; break;
                case Use.GeneralStore: l.nameEn = "General store"; l.nameAr = "بقالة"; break;
                case Use.Cafe: l.nameEn = delta ? "Ahwa (cafe)" : "Qahwa (cafe)"; l.nameAr = delta ? "قهوة بلدي" : "مقهى"; break;
                case Use.Dealer: l.nameEn = "Vehicle dealership"; l.nameAr = "معرض المركبات"; break;
                case Use.AnimalMarket: l.nameEn = "Animal market"; l.nameAr = "سوق المواشي"; break;
                case Use.Fuel: l.nameEn = "Fuel station"; l.nameAr = delta ? "بنزينة" : "محطة الوقود"; break;
                case Use.Mosque: l.nameEn = "Mosque"; l.nameAr = "المسجد"; break;
            }
        }
        // street trees on the sidewalks (not in front of the fuel forecourt, the market gate or at junctions)
        TreeType street = delta ? TreeType.Eucalyptus : TreeType.Sidr;
        foreach (int s in new[] { 1, -1 })
            for (float z = 16f; z <= 54f; z += 12.5f)
                foreach (float sz in new[] { 1f, -1f })
                {
                    float zz = z * sz;
                    if (Mathf.Abs(zz - CrossZ[0]) < 7f || Mathf.Abs(zz - CrossZ[1]) < 7f) continue;
                    if (s > 0 && zz > 34f) continue;                  // fuel forecourt
                    if (s > 0 && zz < -34f) continue;                 // animal market gate
                    pl.trees.Add((new Vector3(s * 6.4f, 0f, zz), street));
                }
        foreach (float cz in CrossZ)
            foreach (float x in new[] { -28f, -14f, 14f, 28f, 50f, -50f })
                pl.trees.Add((new Vector3(x, 0f, cz + (cz > 0 ? 6.4f : -6.4f)), street));
        foreach (var c in new[] { new Vector3(13.6f, 0f, 10.6f), new Vector3(-13.6f, 0f, 10.6f), new Vector3(13.6f, 0f, -10.6f), new Vector3(-13.6f, 0f, -10.6f) })
            pl.trees.Add((c, TreeType.DatePalm));
        // sidewalk walks (pedestrians go back and forth along them)
        float wk = StreetHalf + Walk * 0.45f;
        pl.walks.Add(Line(new Vector3(wk, 0f, -Extent + 1f), new Vector3(wk, 0f, GateDistance - 3f)));
        pl.walks.Add(Line(new Vector3(-wk, 0f, -Extent + 1f), new Vector3(-wk, 0f, GateDistance - 3f)));
        foreach (float cz in CrossZ)
            foreach (float side in new[] { 1f, -1f })
                pl.walks.Add(Line(new Vector3(-Extent + 1f, 0f, cz + side * wk), new Vector3(Extent - 1f, 0f, cz + side * wk)));
        foreach (float sx in SideX)
            foreach (float side in new[] { 1f, -1f })
                pl.walks.Add(Line(new Vector3(sx + side * wk, 0f, -Extent + 1f), new Vector3(sx + side * wk, 0f, Extent - 1f)));
        // souq stalls on the square (facing its middle)
        foreach (int s in new[] { 1, -1 })
            foreach (float z in new[] { -6.5f, 0f, 6.5f })
                pl.stalls.Add(new Spot(new Vector3(s * 11.2f, 0f, z), s > 0 ? -90f : 90f));
        // town cars: right-hand traffic, an outer anticlockwise loop and an inner clockwise loop over the cross / side streets
        float o = 2f;
        pl.carLoops.Add(new List<Vector3> { new Vector3(SideX[0] + o, 0f, CrossZ[1] - o), new Vector3(SideX[0] + o, 0f, CrossZ[0] + o), new Vector3(SideX[1] - o, 0f, CrossZ[0] + o), new Vector3(SideX[1] - o, 0f, CrossZ[1] - o) });
        pl.carLoops.Add(new List<Vector3> { new Vector3(SideX[0] - o, 0f, CrossZ[0] - o), new Vector3(SideX[0] - o, 0f, CrossZ[1] + o), new Vector3(SideX[1] + o, 0f, CrossZ[1] + o), new Vector3(SideX[1] + o, 0f, CrossZ[0] - o) });
        // cars parked at the kerb of the main street (one side each, the street stays two lanes wide)
        pl.props.Add(new Prop { s = new Spot(new Vector3(StreetHalf - 1.1f, 0f, -16f), 0f), model = "Truck", length = 4.6f, solid = true });
        pl.props.Add(new Prop { s = new Spot(new Vector3(StreetHalf - 1.1f, 0f, -21.5f), 0f), model = "CoveredCar", length = 4.4f, solid = true });
        pl.props.Add(new Prop { s = new Spot(new Vector3(-StreetHalf + 1.1f, 0f, 16f), 180f), model = "Truck", length = 4.6f, solid = true });
        pl.props.Add(new Prop { s = new Spot(new Vector3(-StreetHalf + 1.1f, 0f, 21.5f), 180f), model = "TownSuv", length = 4.8f, solid = true });
        return pl;
    }

    private static List<Vector3> Line(Vector3 a, Vector3 b) => new List<Vector3> { a, b };

    // A row of lots between |x| = xa..xb (on side s) and z = za..zb, facing +x (fx > 0) or -x; n lots along z.
    private static void Row(Plan pl, System.Random rnd, int s, float xa, float xb, float za, float zb, int fx, int n, Use use)
    {
        float len = (zb - za) / n;
        for (int i = 0; i < n; i++)
        {
            float z0 = za + i * len;
            var l = new Lot
            {
                use = use, c = new Vector3(s * (xa + xb) * 0.5f, 0f, z0 + len * 0.5f), w = len - 0.12f, d = xb - xa, yaw = fx > 0 ? 90f : -90f,
                style = rnd.Next(1 << 20),
            };
            // the lot's left / right as seen from the street: corner lots show their side walls
            l.openLeft = l.openRight = false;
            bool first = i == 0, last = i == n - 1;
            // facing +x: local right = -z world; facing -x: local right = +z world
            if (fx > 0) { l.openRight = first; l.openLeft = last; } else { l.openLeft = first; l.openRight = last; }
            int r = rnd.Next(100);
            if (use == Use.House) l.floors = pl.delta ? 2 + r % 3 : 1 + (r % 10 < 6 ? 1 : r % 10 < 9 ? 0 : 2);
            else if (use == Use.Shop) l.floors = pl.delta ? 3 + r % 2 : 2;
            else l.floors = 1;
            pl.lots.Add(l);
        }
    }

    // ------------------------------------------------------------------ mesh

    private class T
    {
        public WorldMesh m; public List<WorldCollider> cols; public Mats mat; public Plan pl; public System.Random rnd; public bool delta;
    }

    /// <summary>Build the town's merged meshes and colliders (local frame) and remember the plan for Populate.</summary>
    public static Plan BuildMesh(WorldMesh m, List<WorldCollider> cols, bool delta, int seed)
    {
        var pl = MakePlan(delta, seed);
        var t = new T { m = m, cols = cols, mat = MakeMats(delta), pl = pl, rnd = new System.Random(seed * 31 + 7), delta = delta };
        Streets(t);
        foreach (var l in pl.lots)
        {
            switch (l.use)
            {
                case Use.Mosque: Mosque(t, l); break;
                case Use.Fuel: FuelStation(t, l); break;
                case Use.Dealer: Dealer(t, l); break;
                case Use.AnimalMarket: AnimalMarket(t, l); break;
                case Use.Cafe: Cafe(t, l); break;
                default: Building(t, l); break;
            }
        }
        foreach (var s in pl.stalls) Stall(t, s);
        Arch(t);
        LastPlan = pl;
        return pl;
    }

    // Auto-oriented quad (n = the side it faces).
    private static void Q(WorldMesh m, Material mat, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) m.AddQuad(mat, a, d, c, b);
        else m.AddQuad(mat, a, b, c, d);
    }

    // Flat rect (x0..x1, z0..z1) at height y, facing up.
    private static void Flat(T t, Material mat, float x0, float z0, float x1, float z1, float y)
    {
        if (x1 - x0 < 0.01f || z1 - z0 < 0.01f) return;
        Q(t.m, mat, new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up);
    }

    // A raised walkway slab: top at h, its four edges slope down to the ground over 0.25 m (no kerb face to catch a foot or a wheel).
    private static void Slab(T t, Material top, Material edge, float x0, float z0, float x1, float z1, float h)
    {
        if (x1 - x0 < 0.3f || z1 - z0 < 0.3f) return;
        const float e = 0.25f;
        Flat(t, top, x0 + e, z0 + e, x1 - e, z1 - e, h);
        Vector3 a0 = new Vector3(x0, 0.02f, z0), b0 = new Vector3(x1, 0.02f, z0), c0 = new Vector3(x1, 0.02f, z1), d0 = new Vector3(x0, 0.02f, z1);
        Vector3 a1 = new Vector3(x0 + e, h, z0 + e), b1 = new Vector3(x1 - e, h, z0 + e), c1 = new Vector3(x1 - e, h, z1 - e), d1 = new Vector3(x0 + e, h, z1 - e);
        Q(t.m, edge, a0, b0, b1, a1, new Vector3(0f, 1f, -1f));
        Q(t.m, edge, b0, c0, c1, b1, new Vector3(1f, 1f, 0f));
        Q(t.m, edge, c0, d0, d1, c1, new Vector3(0f, 1f, 1f));
        Q(t.m, edge, d0, a0, a1, d1, new Vector3(-1f, 1f, 0f));
    }

    private static void Streets(T t)
    {
        var M = t.mat;
        const float ay = 0.03f;
        // main street (the square takes its middle), cross streets (own their junctions with the side streets)
        Flat(t, M.asphalt, -StreetHalf, PlazaZ, StreetHalf, GateDistance, ay);
        Flat(t, M.asphalt, -StreetHalf, -Extent, StreetHalf, -PlazaZ, ay);
        foreach (float z in CrossZ)
        {
            Flat(t, M.asphalt, -Extent, z - StreetHalf, -StreetHalf, z + StreetHalf, ay);
            Flat(t, M.asphalt, StreetHalf, z - StreetHalf, Extent, z + StreetHalf, ay);
        }
        foreach (float x in SideX)
        {
            Flat(t, M.asphalt, x - StreetHalf, -Extent, x + StreetHalf, CrossZ[1] - StreetHalf, ay);
            Flat(t, M.asphalt, x - StreetHalf, CrossZ[1] + StreetHalf, x + StreetHalf, CrossZ[0] - StreetHalf, ay);
            Flat(t, M.asphalt, x - StreetHalf, CrossZ[0] + StreetHalf, x + StreetHalf, Extent, ay);
        }
        // dashed centre lines
        for (float z = PlazaZ + 2f; z < GateDistance - 2f; z += 6f) Flat(t, M.paint, -0.07f, z, 0.07f, z + 3f, ay + 0.01f);
        for (float z = -Extent + 2f; z < -PlazaZ - 2f; z += 6f) Flat(t, M.paint, -0.07f, z, 0.07f, z + 3f, ay + 0.01f);
        foreach (float cz in CrossZ)
            for (float x = -Extent + 2f; x < Extent - 2f; x += 6f)
            {
                if (Mathf.Abs(x) < StreetHalf + 1f || Mathf.Abs(Mathf.Abs(x) - SideX[0]) < StreetHalf + 1f) continue;
                Flat(t, M.paint, x, cz - 0.07f, x + 3f, cz + 0.07f, ay + 0.01f);
            }
        // zebra crossings at the square
        foreach (float z in new[] { PlazaZ + 1.6f, -PlazaZ - 1.6f })
            for (float x = -StreetHalf + 0.3f; x < StreetHalf - 0.3f; x += 0.9f) Flat(t, M.paint, x, z - 1.2f, x + 0.45f, z + 1.2f, ay + 0.01f);
        // the market square
        Flat(t, M.plaza, -PlazaX, -PlazaZ, PlazaX, PlazaZ, 0.045f);
        // sidewalks: a slab along every block edge that faces a street
        float[] xs = { StreetHalf, SideX[0] - StreetHalf, SideX[0] + StreetHalf, Extent };
        float[,] zs = { { CrossZ[0] + StreetHalf, Extent }, { CrossZ[1] + StreetHalf, CrossZ[0] - StreetHalf }, { -Extent, CrossZ[1] - StreetHalf } };
        foreach (int s in new[] { 1, -1 })
            for (int b = 0; b < 3; b++)
            {
                float z0 = zs[b, 0], z1 = zs[b, 1];
                // inner block: x from the main street to the side street
                SidewalkBlock(t, s, xs[0], xs[1], z0, z1, true, true, b != 0, b != 2, b == 1);
                // outer block: from the side street outwards
                SidewalkBlock(t, s, xs[2], xs[3], z0, z1, true, false, b != 0, b != 2, false);
            }
        // the gate end of the main street (north of the blocks): sidewalks to the arch
        foreach (int s in new[] { 1, -1 }) Slab(t, M.walk, M.kerb, Mathf.Min(s * StreetHalf, s * (StreetHalf + Walk)), Extent, Mathf.Max(s * StreetHalf, s * (StreetHalf + Walk)), GateDistance - 1f, WalkHeight);
    }

    // Sidewalk strips inside a block rect (inner edge x0 / outer x1 measured from the main street on side s).
    private static void SidewalkBlock(T t, int s, float x0, float x1, float z0, float z1, bool innerEdge, bool outerEdge, bool topEdge, bool bottomEdge, bool plaza)
    {
        var M = t.mat;
        float W = Walk;
        System.Action<float, float, float, float> slab = (ax, az, bx, bz) =>
        {
            float lx = Mathf.Min(s * ax, s * bx), hx = Mathf.Max(s * ax, s * bx);
            Slab(t, M.walk, M.kerb, lx, Mathf.Min(az, bz), hx, Mathf.Max(az, bz), WalkHeight);
        };
        if (innerEdge)
        {
            if (plaza) { slab(x0, z0, x0 + W, -PlazaZ); slab(x0, PlazaZ, x0 + W, z1); }
            else slab(x0, z0, x0 + W, z1);
        }
        if (outerEdge) slab(x1 - W, z0, x1, z1);
        float ix0 = innerEdge ? x0 + W : x0, ix1 = outerEdge ? x1 - W : x1;
        if (plaza) ix0 = Mathf.Max(ix0, PlazaX);
        if (bottomEdge) slab(innerEdge && plaza ? x0 + W : ix0, z0, ix1, z0 + W);
        if (topEdge) slab(innerEdge && plaza ? x0 + W : ix0, z1 - W, ix1, z1);
    }

    // ------------------------------------------------------------------ building shells

    private struct Hole { public float u0, v0, u1, v1; public Material pane; public int kind; }   // kind 0 window, 1 door, 2 shop front, 3 arch window

    // A wall of the shell in lot space: origin = bottom corner at u = 0, R = along the wall, n = outward. Openings get reveals
    // (depth 0.22) and a recessed pane, so nothing is see-through; then frames, sills and mullions.
    private static void Wall(T t, Lot l, Material mat, Vector3 o, Vector3 R, Vector3 n, float len, float height, List<Hole> holes)
    {
        var m = t.m; var rot = l.Rot;
        System.Func<float, float, float, Vector3> P = (u, v, depth) => l.c + rot * (o + R * u + Vector3.up * v - n * depth);
        Vector3 N = rot * n, Rw = rot * R;
        var us = new List<float> { 0f, len };
        foreach (var h in holes) { us.Add(Mathf.Clamp(h.u0, 0f, len)); us.Add(Mathf.Clamp(h.u1, 0f, len)); }
        us.Sort();
        for (int i = 0; i + 1 < us.Count; i++)
        {
            float ua = us[i], ub = us[i + 1];
            if (ub - ua < 1e-3f) continue;
            float mid = (ua + ub) * 0.5f;
            var spans = new List<Vector2>();
            foreach (var h in holes) if (h.u0 < mid && h.u1 > mid) spans.Add(new Vector2(h.v0, h.v1));
            spans.Sort((a, b) => a.x.CompareTo(b.x));
            float v = 0f;
            foreach (var sp in spans)
            {
                if (sp.x > v + 1e-3f) Q(m, mat, P(ua, v, 0f), P(ua, sp.x, 0f), P(ub, sp.x, 0f), P(ub, v, 0f), N);
                v = Mathf.Max(v, sp.y);
            }
            if (height > v + 1e-3f) Q(m, mat, P(ua, v, 0f), P(ua, height, 0f), P(ub, height, 0f), P(ub, v, 0f), N);
        }
        const float dep = 0.22f;
        foreach (var h in holes)
        {
            Q(m, mat, P(h.u0, h.v0, 0f), P(h.u1, h.v0, 0f), P(h.u1, h.v0, dep), P(h.u0, h.v0, dep), Vector3.up);
            Q(m, mat, P(h.u0, h.v1, 0f), P(h.u1, h.v1, 0f), P(h.u1, h.v1, dep), P(h.u0, h.v1, dep), Vector3.down);
            Q(m, mat, P(h.u0, h.v0, 0f), P(h.u0, h.v1, 0f), P(h.u0, h.v1, dep), P(h.u0, h.v0, dep), Rw);
            Q(m, mat, P(h.u1, h.v0, 0f), P(h.u1, h.v1, 0f), P(h.u1, h.v1, dep), P(h.u1, h.v0, dep), -Rw);
            Q(m, h.pane, P(h.u0, h.v0, dep), P(h.u0, h.v1, dep), P(h.u1, h.v1, dep), P(h.u1, h.v0, dep), N);
            var fr = Quaternion.LookRotation(N, Vector3.up);
            float cu = (h.u0 + h.u1) * 0.5f, w = h.u1 - h.u0, hh = h.v1 - h.v0;
            if (h.kind == 0 || h.kind == 3)
            {
                // sill, frame cross
                m.Box(P(cu, h.v0 - 0.08f, -0.06f), new Vector3(w + 0.2f, 0.08f, 0.16f), fr, t.mat.trim);
                m.Box(P(cu, h.v0, dep - 0.03f), new Vector3(0.05f, hh, 0.04f), fr, t.mat.wood);
                if (h.kind == 0) m.Box(P(cu, h.v0 + hh * 0.55f, dep - 0.03f), new Vector3(w, 0.05f, 0.04f), fr, t.mat.wood);
            }
            else if (h.kind == 2)
            {
                for (float u = h.u0 + 1.4f; u < h.u1 - 0.5f; u += 1.4f) m.Box(P(u, h.v0, dep - 0.04f), new Vector3(0.06f, hh, 0.05f), fr, t.mat.steel);
                m.Box(P(cu, h.v1, -0.12f), new Vector3(w + 0.1f, 0.38f, 0.3f), fr, t.mat.metal);   // rolling shutter box
            }
            else if (h.kind == 1)
            {
                m.Box(P(cu, h.v1, -0.05f), new Vector3(w + 0.3f, 0.14f, 0.12f), fr, t.mat.trim);   // lintel
            }
        }
    }

    private static Hole Win(float u, float v, float w, float h, Material pane, int kind = 0) => new Hole { u0 = u - w * 0.5f, u1 = u + w * 0.5f, v0 = v, v1 = v + h, pane = pane, kind = kind };

    /// <summary>A house / shop-house / bank / small shop on its lot.</summary>
    private static void Building(T t, Lot l)
    {
        var M = t.mat; var r = new System.Random(l.style);
        bool delta = t.delta;
        bool shop = l.use != Use.House;
        float fh = delta ? 3.05f : 3.3f;
        int floors = Mathf.Max(1, l.floors + (l.use == Use.Bank ? 1 : 0));
        float H = floors * fh, par = delta ? 0.6f : 0.95f;
        bool unfinished = delta && l.use == Use.House && r.Next(3) == 0;
        if (unfinished) par = 0.25f;
        float w = l.w, d = l.d;
        Material wall = l.use == Use.Bank ? M.stone : M.walls[r.Next(M.walls.Length)];
        // ---- front wall
        var front = new List<Hole>();
        float doorU;
        if (shop)
        {
            float sw = Mathf.Min(w - 1.6f, Mathf.Max(3.2f, w * 0.72f));
            front.Add(new Hole { u0 = (w - sw) * 0.5f, u1 = (w + sw) * 0.5f, v0 = 0.05f, v1 = 2.9f, pane = M.glass, kind = 2 });
            doorU = w * 0.5f;
        }
        else
        {
            doorU = w * (0.3f + 0.4f * (float)r.NextDouble());
            front.Add(Win(doorU, 0f, 1.2f, 2.3f, M.door, 1));
            for (float u = 1.4f; u < w - 1f; u += 2.6f) if (Mathf.Abs(u - doorU) > 1.6f) front.Add(Win(u, 1.0f, delta ? 1.1f : 0.85f, delta ? 1.3f : 1.05f, M.glass));
        }
        var bayU = new List<float>();
        for (int f = 1; f < floors; f++)
            for (float u = 1.5f; u < w - 1.2f; u += 2.8f)
            {
                front.Add(Win(u, f * fh + 0.9f, delta ? 1.1f : 0.9f, delta ? 1.5f : 1.2f, M.glass, l.use == Use.Bank ? 3 : 0));
                if (f == 1) bayU.Add(u);
            }
        Wall(t, l, wall, new Vector3(w * 0.5f, 0f, d * 0.5f), Vector3.left, Vector3.forward, w, H + par, front);
        // ---- back wall: windows on the upper floors, a back door
        var back = new List<Hole> { Win(w * 0.7f, 0f, 1f, 2.2f, M.door, 1) };
        for (int f = 0; f < floors; f++) for (float u = 1.6f; u < w - 1.2f; u += 3.2f) if (f > 0 || Mathf.Abs(u - w * 0.7f) > 1.5f) back.Add(Win(u, f * fh + 1f, 0.8f, 1f, M.glass));
        Wall(t, l, wall, new Vector3(-w * 0.5f, 0f, -d * 0.5f), Vector3.right, Vector3.back, w, H + par, back);
        // ---- sides (windows only where the side is a street corner)
        var left = new List<Hole>(); var right = new List<Hole>();
        for (int f = 0; f < floors; f++)
            for (float u = 1.8f; u < d - 1.2f; u += 3f)
            {
                if (l.openLeft) left.Add(Win(u, f * fh + 1f, 0.8f, 1.1f, M.glass));
                if (l.openRight) right.Add(Win(u, f * fh + 1f, 0.8f, 1.1f, M.glass));
            }
        Wall(t, l, wall, new Vector3(-w * 0.5f, 0f, d * 0.5f), Vector3.back, Vector3.left, d, H + par, left);
        Wall(t, l, wall, new Vector3(w * 0.5f, 0f, -d * 0.5f), Vector3.forward, Vector3.right, d, H + par, right);
        // ---- roof, parapet inner faces and cap
        const float pt = 0.22f;
        float iw = w * 0.5f - pt, id = d * 0.5f - pt;
        Q(t.m, M.roof, l.P(-iw, H, -id), l.P(-iw, H, id), l.P(iw, H, id), l.P(iw, H, -id), Vector3.up);
        ParapetRing(t, l, wall, w, d, H, par, pt);
        // ---- cornices between floors (front) and the roof line
        var fr = l.Rot;
        for (int f = 1; f <= floors; f++) t.m.Box(l.P(0f, f * fh - 0.08f, d * 0.5f + 0.06f), new Vector3(w + 0.04f, 0.16f, 0.12f), fr, M.trim);
        if (!delta && !unfinished) Crenellations(t, l, w, d, H + par);
        if (unfinished) Rebar(t, l, w, d, H);
        // ---- bank: pilasters and steps-free entrance canopy
        if (l.use == Use.Bank)
            for (int i = 0; i < 4; i++) t.m.Box(l.P(-w * 0.42f + i * w * 0.28f, 0f, d * 0.5f + 0.15f), new Vector3(0.5f, H, 0.3f), fr, M.trim);
        // ---- mashrabiya bays (Najd) / balconies (Delta) on the first upper floor
        var used = new HashSet<int>();
        if (floors > 1 && bayU.Count > 0 && l.use != Use.Bank)
        {
            int k = r.Next(bayU.Count);
            if (!delta && r.Next(10) < 6) { Mashrabiya(t, l, bayU[k], fh + 0.75f, d); used.Add(k); }
            if (delta) for (int i = 0; i < bayU.Count; i++) if ((i + l.style) % 2 == 0) { Balcony(t, l, bayU[i], fh, d); used.Add(i); }
        }
        // ---- facade AC units under some windows
        for (int f = 1; f < floors; f++)
            for (int i = 0; i < bayU.Count; i++)
                if (!(f == 1 && used.Contains(i)) && r.Next(4) == 0) AcUnit(t, l, bayU[i], f * fh + 0.25f, d);
        // ---- shop: awning, sign board, goods
        if (shop)
        {
            float sw = Mathf.Min(w - 1.2f, Mathf.Max(3.6f, w * 0.78f));
            if (l.use != Use.Bank) Awning(t, l, sw, 3.25f, d, r);
            const float bh = 0.6f;
            t.m.Box(l.P(0f, 3.3f, d * 0.5f + 0.06f), new Vector3(sw, bh, 0.1f), fr, l.use == Use.Bank ? M.green : M.board);
            if (!string.IsNullOrEmpty(l.nameEn)) t.pl.signs.Add(new Sign { p = l.P(0f, 3.3f + bh * 0.5f, d * 0.5f + 0.13f), yaw = l.yaw, en = l.nameEn, ar = l.nameAr, size = 3.8f });
            if (l.use == Use.SeedShop)
                for (int i = 0; i < 6; i++) t.m.Box(l.P(-w * 0.35f + (i % 3) * 0.75f, (i / 3) * 0.42f, d * 0.5f + 0.6f), new Vector3(0.6f, 0.42f, 0.45f), fr * Quaternion.Euler(0f, (i * 17) % 11 - 5f, 0f), M.hessian);
        }
        // ---- roof things: water tanks, AC units, a dish, the stair kiosk
        Roof(t, l, w, d, H, r, floors);
        // ---- interactable door / shop counter
        Vector3 door = l.P(0f, 0f, d * 0.5f);
        if (shop) t.pl.doors.Add(new Spot(l.P(0f, 0f, d * 0.5f + 1.2f), l.yaw + 180f));   // stand there facing the shop
        switch (l.use)
        {
            case Use.SeedShop: t.pl.shops.Add(new ShopSpot { s = new Spot(door, l.yaw), category = "Seeds", en = l.nameEn, ar = l.nameAr, use = l.use }); break;
            case Use.GeneralStore: t.pl.shops.Add(new ShopSpot { s = new Spot(door, l.yaw), category = "Supplies", en = l.nameEn, ar = l.nameAr, use = l.use }); break;
            case Use.Bank: t.pl.shops.Add(new ShopSpot { s = new Spot(door, l.yaw), category = "Bank", en = l.nameEn, ar = l.nameAr, use = l.use }); break;
        }
        l.Height = H + par;
        t.cols.Add(new WorldCollider(new Vector3(l.c.x, (H + par) * 0.5f, l.c.z), Size(l, w, H + par, d)));
    }

    // Collider size in the place frame: lots face +-x (yaw 90 / -90), so width runs along z.
    private static Vector3 Size(Lot l, float w, float h, float d)
    {
        float a = Mathf.Abs(Mathf.DeltaAngle(0f, l.yaw));
        return Mathf.Abs(a - 90f) < 1f ? new Vector3(d, h, w) : new Vector3(w, h, d);
    }

    private static void ParapetRing(T t, Lot l, Material wall, float w, float d, float H, float par, float pt)
    {
        var m = t.m;
        float iw = w * 0.5f - pt, id = d * 0.5f - pt, top = H + par;
        // inner faces
        Q(m, wall, l.P(-iw, H, id), l.P(-iw, top, id), l.P(iw, top, id), l.P(iw, H, id), l.Rot * Vector3.back);
        Q(m, wall, l.P(-iw, H, -id), l.P(-iw, top, -id), l.P(iw, top, -id), l.P(iw, H, -id), l.Rot * Vector3.forward);
        Q(m, wall, l.P(iw, H, -id), l.P(iw, top, -id), l.P(iw, top, id), l.P(iw, H, id), l.Rot * Vector3.left);
        Q(m, wall, l.P(-iw, H, -id), l.P(-iw, top, -id), l.P(-iw, top, id), l.P(-iw, H, id), l.Rot * Vector3.right);
        // caps
        var cap = t.mat.trim;
        float hw = w * 0.5f, hd = d * 0.5f;
        Q(m, cap, l.P(-hw, top, id), l.P(-hw, top, hd), l.P(hw, top, hd), l.P(hw, top, id), Vector3.up);
        Q(m, cap, l.P(-hw, top, -hd), l.P(-hw, top, -id), l.P(hw, top, -id), l.P(hw, top, -hd), Vector3.up);
        Q(m, cap, l.P(-hw, top, -id), l.P(-hw, top, id), l.P(-iw, top, id), l.P(-iw, top, -id), Vector3.up);
        Q(m, cap, l.P(iw, top, -id), l.P(iw, top, id), l.P(hw, top, id), l.P(hw, top, -id), Vector3.up);
    }

    // Najdi parapet: stepped triangular merlons along the front and back.
    private static void Crenellations(T t, Lot l, float w, float d, float top)
    {
        var mat = t.mat.walls[l.style % 2];
        foreach (float z in new[] { d * 0.5f - 0.11f, -d * 0.5f + 0.11f })
            for (float x = -w * 0.5f + 0.35f; x < w * 0.5f - 0.2f; x += 0.75f)
            {
                t.m.Box(l.P(x, top, z), new Vector3(0.42f, 0.22f, 0.2f), l.Rot, mat);
                t.m.Box(l.P(x, top + 0.22f, z), new Vector3(0.22f, 0.2f, 0.2f), l.Rot, mat);
            }
    }

    // Delta: concrete columns with bare rebar waiting for the next floor.
    private static void Rebar(T t, Lot l, float w, float d, float H)
    {
        foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
        {
            Vector3 b = l.P(c.x * (w * 0.5f - 0.2f), H, c.y * (d * 0.5f - 0.2f));
            t.m.Box(b, new Vector3(0.36f, 0.9f, 0.36f), l.Rot, t.mat.walls[4]);
            for (int i = 0; i < 4; i++) t.m.Cylinder(b + l.Rot * new Vector3(i % 2 == 0 ? -0.1f : 0.1f, 0.9f, i < 2 ? -0.1f : 0.1f), 0.014f, 0.75f, 4, Quaternion.identity, t.mat.steel, false);
        }
    }

    private static void Mashrabiya(T t, Lot l, float u, float v, float d)
    {
        var M = t.mat; var fr = l.Rot;
        float x = l.w * 0.5f - u, z = d * 0.5f;   // u runs from +x to -x on the front
        float bw = 2.3f, bh = 1.9f, bd = 0.62f;
        t.m.Box(l.P(x, v - 0.22f, z + bd * 0.5f), new Vector3(bw + 0.2f, 0.22f, bd + 0.1f), fr, M.wood);   // corbelled base
        t.m.Box(l.P(x, v + bh, z + bd * 0.5f), new Vector3(bw + 0.26f, 0.18f, bd + 0.14f), fr, M.wood);    // cap
        Q(t.m, M.dark, l.P(x - bw * 0.5f, v, z + bd - 0.08f), l.P(x - bw * 0.5f, v + bh, z + bd - 0.08f), l.P(x + bw * 0.5f, v + bh, z + bd - 0.08f), l.P(x + bw * 0.5f, v, z + bd - 0.08f), fr * Vector3.forward);
        foreach (float sx in new[] { -1f, 1f })
        {
            Q(t.m, M.dark, l.P(x + sx * (bw * 0.5f - 0.04f), v, z), l.P(x + sx * (bw * 0.5f - 0.04f), v + bh, z), l.P(x + sx * (bw * 0.5f - 0.04f), v + bh, z + bd), l.P(x + sx * (bw * 0.5f - 0.04f), v, z + bd), fr * new Vector3(sx, 0f, 0f));
            t.m.Box(l.P(x + sx * bw * 0.5f, v, z + bd), new Vector3(0.08f, bh, 0.08f), fr, M.wood);
        }
        for (float s = -bw * 0.5f + 0.19f; s < bw * 0.5f - 0.1f; s += 0.19f) t.m.Box(l.P(x + s, v, z + bd), new Vector3(0.035f, bh, 0.035f), fr, M.wood);   // lattice
        for (float h = 0.22f; h < bh - 0.1f; h += 0.24f) t.m.Box(l.P(x, v + h, z + bd), new Vector3(bw, 0.035f, 0.035f), fr, M.wood);
        foreach (float sx in new[] { -1f, 1f }) for (float h = 0.22f; h < bh - 0.1f; h += 0.24f) t.m.Box(l.P(x + sx * bw * 0.5f, v + h, z + bd * 0.5f), new Vector3(0.035f, 0.035f, bd), fr, M.wood);
    }

    private static void Balcony(T t, Lot l, float u, float floorY, float d)
    {
        var M = t.mat; var fr = l.Rot;
        float x = l.w * 0.5f - u, z = d * 0.5f, bw = 2.4f, bd = 1.0f;
        t.m.Box(l.P(x, floorY - 0.15f, z + bd * 0.5f), new Vector3(bw, 0.15f, bd), fr, M.walls[4]);
        t.m.Box(l.P(x, floorY + 0.95f, z + bd), new Vector3(bw, 0.06f, 0.06f), fr, M.steel);
        foreach (float sx in new[] { -1f, 1f }) t.m.Box(l.P(x + sx * bw * 0.5f, floorY + 0.95f, z + bd * 0.5f), new Vector3(0.06f, 0.06f, bd), fr, M.steel);
        for (float s = -bw * 0.5f; s <= bw * 0.5f + 0.01f; s += 0.16f) t.m.Box(l.P(x + s, floorY, z + bd), new Vector3(0.025f, 0.95f, 0.025f), fr, M.steel);
        foreach (float sx in new[] { -1f, 1f }) for (float s = 0.16f; s < bd; s += 0.16f) t.m.Box(l.P(x + sx * bw * 0.5f, floorY, z + s), new Vector3(0.025f, 0.95f, 0.025f), fr, M.steel);
    }

    private static void AcUnit(T t, Lot l, float u, float v, float d)
    {
        var fr = l.Rot;
        float x = l.w * 0.5f - u, z = d * 0.5f;
        t.m.Box(l.P(x, v, z + 0.16f), new Vector3(0.78f, 0.52f, 0.32f), fr, t.mat.metal);
        Q(t.m, t.mat.dark, l.P(x - 0.3f, v + 0.06f, z + 0.325f), l.P(x - 0.3f, v + 0.46f, z + 0.325f), l.P(x + 0.18f, v + 0.46f, z + 0.325f), l.P(x + 0.18f, v + 0.06f, z + 0.325f), fr * Vector3.forward);
    }

    private static void Awning(T t, Lot l, float sw, float y, float d, System.Random r)
    {
        var fr = l.Rot;
        float z = d * 0.5f, depth = 1.5f, drop = 0.5f;
        var colA = LookLibrary.Tinted(LookMat.Linen, AwningColors[r.Next(AwningColors.Length)]);
        var colB = t.mat.canvasB;
        int n = Mathf.Max(3, Mathf.RoundToInt(sw / 0.55f));
        for (int i = 0; i < n; i++)
        {
            float x0 = -sw * 0.5f + sw * i / n, x1 = -sw * 0.5f + sw * (i + 1) / n;
            var mat = i % 2 == 0 ? colA : colB;
            Vector3 a = l.P(x0, y, z), b = l.P(x1, y, z), c = l.P(x1, y - drop, z + depth), e = l.P(x0, y - drop, z + depth);
            Vector3 up = fr * new Vector3(0f, depth, drop).normalized;
            Q(t.m, mat, a, b, c, e, up);
            Q(t.m, mat, a, b, c, e, -up);
            Vector3 c2 = l.P(x1, y - drop - 0.28f, z + depth), e2 = l.P(x0, y - drop - 0.28f, z + depth);
            Q(t.m, mat, e, c, c2, e2, fr * Vector3.forward);   // valance
            Q(t.m, mat, e, c, c2, e2, fr * Vector3.back);
        }
    }

    private static void Roof(T t, Lot l, float w, float d, float H, System.Random r, int floors)
    {
        var M = t.mat; var fr = l.Rot;
        int tanks = 1 + r.Next(2);
        for (int i = 0; i < tanks; i++)
        {
            Vector3 b = l.P((i == 0 ? -1f : 1f) * w * 0.22f, H, -d * 0.25f + (float)r.NextDouble() * 0.5f);
            t.m.Box(b, new Vector3(1.3f, 0.35f, 1.3f), fr, M.steel);
            t.m.Cylinder(b + Vector3.up * 0.35f, 0.55f, 1.15f, 10, Quaternion.identity, M.tank);
        }
        if (r.Next(2) == 0)
        {
            Vector3 b = l.P(w * 0.1f, H, d * 0.2f);
            t.m.Box(b, new Vector3(0.9f, 0.6f, 0.6f), fr, M.metal);
            t.m.Disc(b + Vector3.up * 0.61f, 0.24f, 10, M.dark);
        }
        if (r.Next(3) == 0)
        {
            Vector3 b = l.P(-w * 0.3f, H, d * 0.3f);
            t.m.Box(b, new Vector3(0.08f, 0.9f, 0.08f), fr, M.steel);
            t.m.Cylinder(b + Vector3.up * 0.9f, 0.42f, 0.05f, 12, fr * Quaternion.Euler(-55f, 0f, 0f), M.metal);
        }
        if (floors >= 2 && w > 7f)
        {
            Vector3 b = l.P(w * 0.25f, H, -d * 0.18f);
            Material wall = M.walls[(l.style >> 3) % M.walls.Length];
            t.m.Box(b, new Vector3(2.2f, 2.4f, 2.4f), fr, wall);
            Q(t.m, M.door, l.P(w * 0.25f - 0.45f, H, -d * 0.18f + 1.21f), l.P(w * 0.25f - 0.45f, H + 2f, -d * 0.18f + 1.21f), l.P(w * 0.25f + 0.45f, H + 2f, -d * 0.18f + 1.21f), l.P(w * 0.25f + 0.45f, H, -d * 0.18f + 1.21f), fr * Vector3.forward);
        }
    }

    // ------------------------------------------------------------------ special buildings

    private static void Mosque(T t, Lot l)
    {
        var M = t.mat; var fr = l.Rot;
        float w = l.w, d = l.d;   // w along the street (45 m), d = 11 m
        Material wall = t.delta ? M.walls[1] : M.walls[2];
        // prayer hall (local x -6..18, z -5.5..3.5), tall arched windows, the door facing the courtyard
        var hall = new Lot { c = l.P(6f, 0f, -1f), w = 24f, d = 9f, yaw = l.yaw, style = l.style };
        float H = 7.5f;
        var front = new List<Hole> { Win(12f, 0f, 2f, 3.4f, M.door, 1) };
        for (float u = 3f; u < 22f; u += 3.2f) if (Mathf.Abs(u - 12f) > 2f) front.Add(Win(u, 2.4f, 1.2f, 3f, M.glass, 3));
        Wall(t, hall, wall, new Vector3(12f, 0f, 4.5f), Vector3.left, Vector3.forward, 24f, H + 0.8f, front);
        var back = new List<Hole>(); for (float u = 3f; u < 22f; u += 3.2f) back.Add(Win(u, 2.4f, 1.2f, 3f, M.glass, 3));
        Wall(t, hall, wall, new Vector3(-12f, 0f, -4.5f), Vector3.right, Vector3.back, 24f, H + 0.8f, back);
        Wall(t, hall, wall, new Vector3(-12f, 0f, 4.5f), Vector3.back, Vector3.left, 9f, H + 0.8f, new List<Hole> { Win(4.5f, 2.4f, 1.2f, 3f, M.glass, 3) });
        Wall(t, hall, wall, new Vector3(12f, 0f, -4.5f), Vector3.forward, Vector3.right, 9f, H + 0.8f, new List<Hole> { Win(4.5f, 2.4f, 1.2f, 3f, M.glass, 3) });
        Q(t.m, M.roof, hall.P(-11.8f, H, -4.3f), hall.P(-11.8f, H, 4.3f), hall.P(11.8f, H, 4.3f), hall.P(11.8f, H, -4.3f), Vector3.up);
        ParapetRing(t, hall, wall, 24f, 9f, H, 0.8f, 0.2f);
        if (!t.delta) Crenellations(t, hall, 24f, 9f, H + 0.8f);
        // dome on a drum
        Vector3 dc = hall.P(0f, H, 0f);
        t.m.Cylinder(dc, 3.6f, 1.4f, 16, Quaternion.identity, wall);
        t.m.Dome(dc + Vector3.up * 1.4f, 3.7f, M.dome);
        t.m.Cylinder(dc + Vector3.up * 5.1f, 0.06f, 1.1f, 6, Quaternion.identity, M.green, false);
        t.m.Dome(dc + Vector3.up * 6.25f, 0.18f, M.green, false);
        // minaret at the courtyard corner
        Vector3 mb = l.P(-13f, 0f, 2.5f);
        t.m.Box(mb, new Vector3(3.2f, 7f, 3.2f), fr, wall);
        t.m.Box(mb + Vector3.up * 7f, new Vector3(3.5f, 0.3f, 3.5f), fr, M.trim);
        t.m.Cylinder(mb + Vector3.up * 7.3f, 1.25f, 12.5f, 8, Quaternion.identity, wall);
        t.m.Cylinder(mb + Vector3.up * 19.8f, 1.85f, 0.45f, 8, Quaternion.identity, M.trim);
        for (int i = 0; i < 8; i++) t.m.Box(mb + Vector3.up * 20.25f + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 1.75f, new Vector3(0.5f, 0.9f, 0.12f), Quaternion.Euler(0f, i * 45f, 0f), M.trim);
        t.m.Cylinder(mb + Vector3.up * 20.25f, 1.0f, 3.2f, 8, Quaternion.identity, wall);
        t.m.Cone(mb + Vector3.up * 23.45f, 1.15f, 2.8f, 8, M.dome);
        t.m.Cylinder(mb + Vector3.up * 26.2f, 0.05f, 1f, 6, Quaternion.identity, M.green, false);
        // courtyard: low wall with the gate toward the street, paving, palms
        float cy = 2.2f;
        LotFlat(t, l, M.plaza, -22.4f, -5.4f, -6.2f, 5.4f, 0.05f);
        t.m.Box(l.P(-14.25f, cy, 5.5f), new Vector3(5.9f, 0.7f, 0.45f), fr, wall);   // gate lintel
        CourtWall(t, l, wall, -22.5f, -5.5f, -22.5f, 5.5f, cy);
        CourtWall(t, l, wall, -22.5f, -5.5f, -6f, -5.5f, cy);
        CourtWall(t, l, wall, -22.5f, 5.5f, -17f, 5.5f, cy);
        CourtWall(t, l, wall, -11.5f, 5.5f, -6f, 5.5f, cy);
        t.pl.trees.Add((l.P(-19f, 0f, -2f), TreeType.DatePalm));
        t.pl.trees.Add((l.P(-9f, 0f, -2.5f), TreeType.DatePalm));
        t.pl.signs.Add(new Sign { p = l.P(-14.25f, 2.55f, 5.75f), yaw = l.yaw, en = l.nameEn, ar = l.nameAr, size = 4.2f });
        t.cols.Add(new WorldCollider(Mid(hall.c, H * 0.5f + 0.4f), Size(l, 24f, H + 0.8f, 9f)));
        t.cols.Add(new WorldCollider(Mid(mb, 13f), new Vector3(3.2f, 26f, 3.2f)));
        l.Height = 27f;
    }

    private static Vector3 Mid(Vector3 p, float y) => new Vector3(p.x, y, p.z);
    // A flat rect given in lot space (lots are turned by multiples of 90 degrees, so it stays axis aligned in the place).
    private static void LotFlat(T t, Lot l, Material mat, float lx0, float lz0, float lx1, float lz1, float y)
    {
        Vector3 a = l.P(lx0, 0f, lz0), b = l.P(lx1, 0f, lz1);
        Flat(t, mat, Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z), y);
    }

    // A courtyard wall from (x0, z0) to (x1, z1) in lot space: a box mesh and its collider.
    private static void CourtWall(T t, Lot l, Material mat, float x0, float z0, float x1, float z1, float h)
    {
        Vector3 a = l.P(x0, 0f, z0), b = l.P(x1, 0f, z1);
        Vector3 mid = (a + b) * 0.5f, dir = b - a; float len = dir.magnitude;
        if (len < 0.2f) return;
        var rot = Quaternion.LookRotation(dir / len);
        t.m.Box(mid, new Vector3(0.35f, h, len), rot, mat);
        t.m.Box(mid + Vector3.up * h, new Vector3(0.45f, 0.12f, len + 0.1f), rot, t.mat.trim);
        bool alongX = Mathf.Abs(dir.x) > Mathf.Abs(dir.z);
        t.cols.Add(new WorldCollider(new Vector3(mid.x, h * 0.5f, mid.z), alongX ? new Vector3(len, h, 0.35f) : new Vector3(0.35f, h, len)));
    }

    private static void FuelStation(T t, Lot l)
    {
        var M = t.mat; var fr = l.Rot;
        float w = l.w, d = l.d;
        var red = LookLibrary.Tinted(LookMat.Metal, new Color(0.85f, 0.2f, 0.16f), 0.45f);
        LotFlat(t, l, M.walk, -w * 0.5f, -d * 0.5f, w * 0.5f, d * 0.5f, 0.04f);
        // canopy on four columns
        float ch = 5.2f;
        const float cz = 1.2f;
        foreach (var c in new[] { new Vector2(-5.5f, cz - 2.6f), new Vector2(5.5f, cz - 2.6f), new Vector2(-5.5f, cz + 2.6f), new Vector2(5.5f, cz + 2.6f) })
            t.m.Box(l.P(c.x, 0f, c.y), new Vector3(0.35f, ch, 0.35f), fr, M.steel);
        t.m.Box(l.P(0f, ch, cz), new Vector3(14f, 0.25f, 8f), fr, M.metal);
        Q(t.m, M.metal, l.P(-7f, ch, cz - 4f), l.P(7f, ch, cz - 4f), l.P(7f, ch, cz + 4f), l.P(-7f, ch, cz + 4f), Vector3.down);
        t.m.Box(l.P(0f, ch + 0.25f, cz), new Vector3(14.2f, 0.6f, 8.2f), fr, red);
        t.pl.signs.Add(new Sign { p = l.P(0f, ch + 0.55f, cz + 4.15f), yaw = l.yaw, en = l.nameEn, ar = l.nameAr, size = 4.5f });
        // pump islands (kerb only 15 cm: driven over at the ends, not a wall), pumps
        foreach (float x in new[] { -3.2f, 3.2f })
        {
            t.m.Box(l.P(x, 0f, cz), new Vector3(1.2f, 0.15f, 3.2f), fr, M.kerb);
            t.m.Box(l.P(x, 0.15f, cz), new Vector3(0.6f, 1.75f, 0.9f), fr, red);
            Q(t.m, M.dark, l.P(x - 0.2f, 1.1f, cz + 0.451f), l.P(x - 0.2f, 1.6f, cz + 0.451f), l.P(x + 0.2f, 1.6f, cz + 0.451f), l.P(x + 0.2f, 1.1f, cz + 0.451f), fr * Vector3.forward);
            t.cols.Add(new WorldCollider(Mid(l.P(x, 0f, cz), 0.95f), Size(l, 0.6f, 1.9f, 0.9f)));
        }
        // kiosk at the back of the forecourt
        var kiosk = new Lot { c = l.P(0f, 0f, -d * 0.5f + 1.9f), w = 7f, d = 3.6f, yaw = l.yaw, floors = 1, style = l.style, use = Use.Shop };
        var front = new List<Hole> { new Hole { u0 = 1f, u1 = 6f, v0 = 0.05f, v1 = 2.6f, pane = M.glass, kind = 2 } };
        Wall(t, kiosk, M.walls[4], new Vector3(3.5f, 0f, 1.8f), Vector3.left, Vector3.forward, 7f, 3.2f, front);
        Wall(t, kiosk, M.walls[4], new Vector3(-3.5f, 0f, -1.8f), Vector3.right, Vector3.back, 7f, 3.2f, new List<Hole>());
        Wall(t, kiosk, M.walls[4], new Vector3(-3.5f, 0f, 1.8f), Vector3.back, Vector3.left, 3.6f, 3.2f, new List<Hole>());
        Wall(t, kiosk, M.walls[4], new Vector3(3.5f, 0f, -1.8f), Vector3.forward, Vector3.right, 3.6f, 3.2f, new List<Hole>());
        Q(t.m, M.roof, kiosk.P(-3.5f, 3.2f, -1.8f), kiosk.P(-3.5f, 3.2f, 1.8f), kiosk.P(3.5f, 3.2f, 1.8f), kiosk.P(3.5f, 3.2f, -1.8f), Vector3.up);
        t.cols.Add(new WorldCollider(Mid(kiosk.c, 1.6f), Size(l, 7f, 3.2f, 3.6f)));
        // a tall price sign at the street corner
        Vector3 pole = l.P(w * 0.5f - 0.6f, 0f, d * 0.5f - 0.6f);
        t.m.Box(pole, new Vector3(0.3f, 6f, 0.3f), fr, M.steel);
        t.m.Box(pole + Vector3.up * 6f, new Vector3(0.25f, 2.2f, 1.8f), fr, red);
        t.pl.fuel = l.P(0f, 0f, cz);
        l.Height = ch + 0.85f;
    }

    private static void Dealer(T t, Lot l)
    {
        var M = t.mat; var fr = l.Rot;
        float w = l.w, d = l.d, H = 5.6f;
        var blue = LookLibrary.Tinted(LookMat.Metal, new Color(0.2f, 0.42f, 0.78f), 0.45f);
        LotFlat(t, l, M.walk, -w * 0.5f, -d * 0.5f, w * 0.5f, d * 0.5f, 0.05f);
        // open showroom: back and side walls, roof slab on columns at the front
        Material wall = M.walls[4];
        var back = new List<Hole> { Win(w * 0.5f, 0f, 3.6f, 3.4f, M.dark, 1) };
        Wall(t, l, wall, new Vector3(-w * 0.5f, 0f, -d * 0.5f), Vector3.right, Vector3.back, w, H, back);
        Q(t.m, wall, l.P(-w * 0.5f + 0.01f, 0f, -d * 0.5f + 0.01f), l.P(-w * 0.5f + 0.01f, H, -d * 0.5f + 0.01f), l.P(w * 0.5f - 0.01f, H, -d * 0.5f + 0.01f), l.P(w * 0.5f - 0.01f, 0f, -d * 0.5f + 0.01f), fr * Vector3.forward);
        foreach (float sx in new[] { -1f, 1f })
        {
            t.m.Box(l.P(sx * (w * 0.5f - 0.15f), 0f, -d * 0.25f), new Vector3(0.3f, H, d * 0.5f), fr, wall);
            t.m.Box(l.P(sx * (w * 0.5f - 0.2f), 0f, d * 0.5f - 0.3f), new Vector3(0.4f, H, 0.4f), fr, M.steel);
        }
        t.m.Box(l.P(0f, 0f, d * 0.5f - 0.3f), new Vector3(0.4f, H, 0.4f), fr, M.steel);
        t.m.Box(l.P(0f, H, 0f), new Vector3(w + 0.4f, 0.35f, d + 0.4f), fr, M.metal);
        Q(t.m, M.metal, l.P(-w * 0.5f - 0.2f, H, -d * 0.5f - 0.2f), l.P(w * 0.5f + 0.2f, H, -d * 0.5f - 0.2f), l.P(w * 0.5f + 0.2f, H, d * 0.5f + 0.2f), l.P(-w * 0.5f - 0.2f, H, d * 0.5f + 0.2f), Vector3.down);
        t.m.Box(l.P(0f, H + 0.35f, d * 0.5f + 0.1f), new Vector3(w + 0.4f, 0.9f, 0.2f), fr, blue);
        t.pl.signs.Add(new Sign { p = l.P(0f, H + 0.8f, d * 0.5f + 0.22f), yaw = l.yaw, en = l.nameEn, ar = l.nameAr, size = 5f });
        // sales desk
        t.m.Box(l.P(-w * 0.3f, 0f, -d * 0.3f), new Vector3(2.2f, 1.05f, 0.7f), fr, M.wood);
        t.cols.Add(new WorldCollider(Mid(l.P(0f, 0f, -d * 0.5f + 0.2f), H * 0.5f), Size(l, w, H, 0.4f)));
        foreach (float sx in new[] { -1f, 1f }) t.cols.Add(new WorldCollider(Mid(l.P(sx * (w * 0.5f - 0.15f), 0f, -d * 0.25f), H * 0.5f), Size(l, 0.3f, H, d * 0.5f)));
        t.cols.Add(new WorldCollider(Mid(l.P(-w * 0.3f, 0f, -d * 0.3f), 0.53f), Size(l, 2.2f, 1.05f, 0.7f)));
        t.pl.shops.Add(new ShopSpot { s = new Spot(l.P(-w * 0.3f, 0f, -d * 0.3f + 0.35f), l.yaw), category = "Upgrades", en = l.nameEn, ar = l.nameAr, use = Use.Dealer });
        // display models: a tractor in the showroom, a pickup outside
        t.pl.props.Add(new Prop { s = new Spot(l.P(w * 0.18f, 0.06f, -0.5f), l.yaw + 150f), model = "Tractor", length = 4.5f, solid = true });
        t.pl.props.Add(new Prop { s = new Spot(l.P(-w * 0.12f, 0.06f, 0.8f), l.yaw + 200f), model = "Truck", length = 4.6f, solid = true });
        l.Height = H + 1.25f;
    }

    private static void AnimalMarket(T t, Lot l)
    {
        var M = t.mat; var fr = l.Rot;
        float w = l.w, d = l.d;   // w = 18.5 along the street, d = 25 deep
        LotFlat(t, l, LookLibrary.Tinted(LookMat.Gravel, Color.white), -w * 0.5f, -d * 0.5f, w * 0.5f, d * 0.5f, 0.03f);
        // pen fence: posts and two rails, a gate toward the street
        float px = w * 0.5f - 1f, pz = d * 0.5f - 1f, gate = 2.2f;
        Fence(t, l, -px, -pz, px, -pz); Fence(t, l, -px, -pz, -px, pz); Fence(t, l, px, -pz, px, pz);
        Fence(t, l, -px, pz, -gate, pz); Fence(t, l, gate, pz, px, pz);
        // shade roof over the back half
        foreach (var c in new[] { new Vector2(-px + 0.5f, -pz + 0.5f), new Vector2(px - 0.5f, -pz + 0.5f), new Vector2(-px + 0.5f, -1f), new Vector2(px - 0.5f, -1f), new Vector2(0f, -pz + 0.5f), new Vector2(0f, -1f) })
            t.m.Box(l.P(c.x, 0f, c.y), new Vector3(0.18f, 3f, 0.18f), fr, M.wood);
        var roofRot = fr * Quaternion.Euler(-6f, 0f, 0f);
        t.m.Box(l.P(0f, 3f, (-pz - 1f) * 0.5f), new Vector3(px * 2f, 0.06f, pz - 0.5f), roofRot, M.corrugated);
        // troughs, hay
        t.m.Box(l.P(-px + 1.5f, 0f, -pz + 2.5f), new Vector3(0.7f, 0.55f, 3f), fr, M.steel);
        t.m.Box(l.P(px - 2f, 0f, -pz + 2f), new Vector3(1.2f, 0.8f, 2f), fr, LookLibrary.Tinted(LookMat.Hessian, new Color(1f, 0.9f, 0.6f)));
        // seller's booth beside the gate (outside the pen)
        Vector3 booth = l.P(-gate - 3.2f, 0f, d * 0.5f - 0.2f);
        t.m.Box(booth, new Vector3(2.6f, 1.05f, 0.9f), fr, M.wood);
        t.m.Box(booth + fr * new Vector3(0f, 2.5f, 0f), new Vector3(3f, 0.08f, 1.6f), fr, t.mat.canvasA);
        foreach (float sx in new[] { -1.4f, 1.4f }) t.m.Box(booth + fr * new Vector3(sx, 0f, -0.6f), new Vector3(0.08f, 2.5f, 0.08f), fr, M.wood);
        t.cols.Add(new WorldCollider(Mid(booth, 0.53f), Size(l, 2.6f, 1.05f, 0.9f)));
        t.pl.signs.Add(new Sign { p = booth + Vector3.up * 3.2f, yaw = l.yaw, en = l.nameEn, ar = l.nameAr, size = 3.6f });
        t.pl.shops.Add(new ShopSpot { s = new Spot(booth + fr * new Vector3(0f, 0f, 0.46f), l.yaw), category = "Animals", en = l.nameEn, ar = l.nameAr, use = Use.AnimalMarket });
        t.pl.pen = l.P(0f, 0f, -1f); t.pl.penRadius = Mathf.Min(px, pz) - 1.5f;
        l.Height = 3.4f;
    }

    // Pen fence run in lot space: posts every 2 m, two rails; one thin collider for the run.
    private static void Fence(T t, Lot l, float x0, float z0, float x1, float z1)
    {
        Vector3 a = l.P(x0, 0f, z0), b = l.P(x1, 0f, z1);
        Vector3 dir = b - a; float len = dir.magnitude;
        if (len < 0.3f) return;
        var rot = Quaternion.LookRotation(dir / len);
        int n = Mathf.Max(1, Mathf.CeilToInt(len / 2f));
        for (int i = 0; i <= n; i++) t.m.Box(Vector3.Lerp(a, b, i / (float)n), new Vector3(0.12f, 1.15f, 0.12f), rot, t.mat.wood);
        foreach (float y in new[] { 0.45f, 0.95f }) t.m.Box((a + b) * 0.5f + Vector3.up * y, new Vector3(0.06f, 0.1f, len), rot, t.mat.wood);
        Vector3 mid = (a + b) * 0.5f;
        bool alongX = Mathf.Abs(dir.x) > Mathf.Abs(dir.z);
        t.cols.Add(new WorldCollider(new Vector3(mid.x, 0.6f, mid.z), alongX ? new Vector3(len + 0.12f, 1.2f, 0.12f) : new Vector3(0.12f, 1.2f, len + 0.12f)));
    }

    private static void Cafe(T t, Lot l)
    {
        // the building stands back 3 m: a terrace with tables in front, under a wide awning
        var M = t.mat; var fr = l.Rot;
        const float terrace = 3f;
        var b = new Lot { use = Use.Cafe, c = l.P(0f, 0f, -terrace * 0.5f), w = l.w, d = l.d - terrace, yaw = l.yaw, floors = 2, style = l.style, nameEn = l.nameEn, nameAr = l.nameAr, openLeft = l.openLeft, openRight = l.openRight };
        Building(t, b);
        l.Height = b.Height;
        LotFlat(t, l, M.plaza, -l.w * 0.5f, l.d * 0.5f - terrace, l.w * 0.5f, l.d * 0.5f, 0.06f);
        for (int i = 0; i < 3; i++)
        {
            float x = -l.w * 0.33f + i * l.w * 0.33f, z = l.d * 0.5f - terrace * 0.5f;
            Vector3 c = l.P(x, 0.06f, z);
            t.m.Cylinder(c, 0.05f, 0.72f, 6, Quaternion.identity, M.steel, false);
            t.m.Cylinder(c + Vector3.up * 0.72f, 0.42f, 0.04f, 12, Quaternion.identity, M.wood);
            foreach (float sx in new[] { -0.75f, 0.75f })
            {
                Vector3 s = l.P(x + sx, 0.06f, z);
                t.m.Box(s, new Vector3(0.42f, 0.45f, 0.42f), fr, M.wood);
                t.m.Box(s + fr * new Vector3(sx > 0 ? 0.19f : -0.19f, 0.45f, 0f), new Vector3(0.05f, 0.45f, 0.42f), fr, M.wood);
                t.pl.seats.Add(new Spot(s + Vector3.up * 0.0f, l.yaw + (sx > 0 ? -90f : 90f)));
            }
        }
        t.pl.shops.Add(new ShopSpot { s = new Spot(b.P(0f, 0f, b.d * 0.5f), l.yaw), category = "Cafe", en = l.nameEn, ar = l.nameAr, use = Use.Cafe });
    }

    // Souq stall on the square (counter, posts, striped canopy, crates of produce).
    private static void Stall(T t, Spot s)
    {
        var M = t.mat; var fr = Quaternion.Euler(0f, s.yaw, 0f);
        Vector3 b = s.p;
        t.m.Box(b, new Vector3(3f, 0.95f, 1.1f), fr, M.wood);
        foreach (var c in new[] { new Vector2(-1.45f, -0.55f), new Vector2(1.45f, -0.55f), new Vector2(-1.45f, 0.55f), new Vector2(1.45f, 0.55f) })
            t.m.Box(b + fr * new Vector3(c.x, 0f, c.y), new Vector3(0.08f, 2.5f, 0.08f), fr, M.wood);
        int k = Mathf.Abs(Mathf.RoundToInt(s.p.z * 3f + s.p.x)) % AwningColors.Length;
        t.m.Box(b + fr * new Vector3(0f, 2.5f, 0.15f), new Vector3(3.3f, 0.06f, 1.9f), fr * Quaternion.Euler(-7f, 0f, 0f), LookLibrary.Tinted(LookMat.Linen, AwningColors[k]));
        Color[] produce = { new Color(0.9f, 0.3f, 0.15f), new Color(0.95f, 0.75f, 0.2f), new Color(0.35f, 0.6f, 0.2f), new Color(0.55f, 0.3f, 0.5f) };
        for (int i = 0; i < 4; i++)
        {
            Vector3 c = b + fr * new Vector3(-1.1f + i * 0.73f, 0.95f, 0.1f);
            t.m.Box(c, new Vector3(0.6f, 0.18f, 0.5f), fr, M.wood);
            t.m.Box(c + Vector3.up * 0.18f, new Vector3(0.5f, 0.12f, 0.42f), fr, produce[(i + k) % produce.Length]);
        }
        t.cols.Add(new WorldCollider(new Vector3(b.x, 0.5f, b.z), new Vector3(3.05f, 1f, 1.15f), s.yaw));
        t.pl.shops.Add(new ShopSpot { s = new Spot(b + fr * new Vector3(0f, 0f, 0.56f), s.yaw), category = "Supplies", en = "Souq stall", ar = "بسطة السوق", use = Use.Shop });
    }

    // Welcome arch over the main street at the town's gate.
    private static void Arch(T t)
    {
        var M = t.mat;
        Material wall = t.delta ? M.walls[1] : M.walls[0];
        float z = GateDistance - 3f, x = StreetHalf + Walk + 0.9f, h = 6.4f;
        foreach (float sx in new[] { -1f, 1f })
        {
            t.m.Box(new Vector3(sx * x, 0f, z), new Vector3(1.2f, h, 1.2f), Quaternion.identity, wall);
            t.m.Box(new Vector3(sx * x, h, z), new Vector3(1.5f, 0.4f, 1.5f), Quaternion.identity, M.trim);
            t.cols.Add(new WorldCollider(new Vector3(sx * x, h * 0.5f, z), new Vector3(1.2f, h, 1.2f)));
        }
        t.m.Box(new Vector3(0f, h - 1.3f, z), new Vector3(x * 2f, 1.3f, 0.8f), Quaternion.identity, wall);
        t.m.Box(new Vector3(0f, h, z), new Vector3(x * 2f + 0.4f, 0.25f, 1f), Quaternion.identity, M.trim);
        t.pl.signs.Add(new Sign { p = new Vector3(0f, h - 0.65f, z + 0.42f), yaw = 0f, en = "Welcome", ar = "أهلاً وسهلاً", size = 5f });
    }

    // ------------------------------------------------------------------ life (GameObjects)

    private static readonly string[] Men = { "Characters/character-male-a", "Characters/character-male-c" };
    private static readonly string[] Women = { "Characters/character-female-b", "Characters/character-female-d" };

    /// <summary>Signs, shops, trees, people, parked and moving cars of the town just built (OpenWorld.BuildPlaces).</summary>
    public static Info Populate(OpenWorld w, Transform place, WorldPoi p, bool delta, List<(GameObject go, Vector3 c, float d)> culled)
    {
        var pl = LastPlan;
        var info = new Info { plan = pl, root = place };
        if (pl == null) return info;
        System.Func<Vector3, Vector3> W = place.TransformPoint;
        float yaw0 = place.eulerAngles.y;
        // ---- signs (flat on the facades, EN above AR)
        var signs = new GameObject("Town Signs");
        signs.transform.SetParent(place, false);
        foreach (var s in pl.signs) FacadeSign(signs.transform, W(s.p), Quaternion.Euler(0f, yaw0 + s.yaw, 0f), s.en, s.ar, s.size);
        culled.Add((signs, place.position, 380f));
        // ---- shops
        var shops = new GameObject("Town Shops");
        shops.transform.SetParent(place, false);
        foreach (var s in pl.shops)
        {
            var go = new GameObject("Shop " + s.en);
            go.layer = 7;   // Interactable (InteractionController's mask)
            go.transform.SetParent(shops.transform, false);
            go.transform.SetPositionAndRotation(W(s.s.p), Quaternion.Euler(0f, yaw0 + s.s.yaw, 0f));
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.2f, 0.12f); col.size = new Vector3(1.6f, 2.2f, 0.3f);
            Interactable it;
            if (s.category == "Bank") it = go.AddComponent<BankSign>();
            else
            {
                var shop = go.AddComponent<TownShop>();
                shop.category = s.category; shop.nameEn = s.en; shop.nameAr = s.ar;
                it = shop;
            }
            info.shops.Add(it);
        }
        // the fuel station's pumps
        if (pl.fuel != Vector3.zero)
        {
            var f = new GameObject("Town Fuel");
            f.transform.SetParent(place, false);
            f.transform.position = W(pl.fuel);
            info.fuel = f.transform;
        }
        // ---- trees (graphics makeover vegetation: LODs, wind, trunk colliders)
        var trees = new GameObject("Town Trees");
        trees.transform.SetParent(place, false);
        int seed = 11;
        foreach (var tr in pl.trees)
        {
            var g = Vegetation.PlaceTree(W(tr.p) + Vector3.up * WalkHeight, tr.type, seed++ * 7, trees.transform, false);
            if (g != null) info.trees++;
        }
        culled.Add((trees, place.position, 600f));
        // ---- display / parked vehicles (real models)
        var cars = new GameObject("Town Cars");
        cars.transform.SetParent(place, false);
        foreach (var pr in pl.props)
        {
            var holder = new GameObject("Parked " + pr.model);
            holder.transform.SetParent(cars.transform, false);
            holder.transform.SetPositionAndRotation(W(pr.s.p), Quaternion.Euler(0f, yaw0 + pr.s.yaw, 0f));
            var model = Build.TryModel(pr.model, holder.transform, pr.length, 0f);
            if (model == null) { Object.Destroy(holder); continue; }
            if (pr.solid)
            {
                holder.layer = DesertProps.PropLayer;
                var bc = holder.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.85f, 0f); bc.size = new Vector3(pr.length * 0.42f, 1.7f, pr.length * 0.95f);
            }
            info.parked++;
        }
        int ci = 0;
        foreach (var loop in pl.carLoops)
            for (int k = 0; k < 2; k++)
            {
                var pts = new List<Vector3>();
                foreach (var q in loop) pts.Add(W(q));
                var car = TownCar.Create(cars.transform, pts, k * 0.5f + ci * 0.25f, 6.5f + (ci % 3) * 0.8f, ci);
                if (car != null) info.cars.Add(car);
                ci++;
            }
        culled.Add((cars, place.position, 420f));
        // ---- people
        var life = new GameObject("Town People");
        life.transform.SetParent(place, false);
        var rnd = new System.Random(pl.lots.Count * 13 + (delta ? 5 : 0));
        int n = 0;
        // walkers on the sidewalks (two per walk), pausing at shop doors on their way
        for (int i = 0; i < pl.walks.Count; i++)
            for (int k = 0; k < (i < 2 ? 3 : 1); k++)
            {
                bool female = (n % 3) == 1;
                var path = new List<Vector3>();
                foreach (var q in pl.walks[i]) path.Add(W(q) + Vector3.up * WalkHeight);
                var stops = new List<Spot>();
                foreach (var dsp in pl.doors) if (Near(pl.walks[i], dsp.p, 2.5f)) stops.Add(new Spot(W(dsp.p) + Vector3.up * WalkHeight, yaw0 + dsp.yaw));
                var a = Person(life.transform, female, n, W(Vector3.Lerp(pl.walks[i][0], pl.walks[i][1], (float)rnd.NextDouble())) + Vector3.up * WalkHeight);
                if (a == null) continue;
                var walker = a.gameObject.AddComponent<TownWalker>();
                walker.Setup(a, path, stops, 1.05f + (float)rnd.NextDouble() * 0.35f, rnd.Next());
                info.walkers.Add(walker);
                n++;
            }
        // cafe guests
        foreach (var s in pl.seats)
        {
            if (rnd.Next(3) == 0) continue;
            var a = Person(life.transform, false, n, W(s.p));
            if (a == null) continue;
            a.transform.rotation = Quaternion.Euler(0f, yaw0 + s.yaw, 0f);
            var walker = a.gameObject.AddComponent<TownWalker>();
            walker.SetupSitting(a);
            info.walkers.Add(walker);
            n++;
        }
        // stall keepers behind the counters, shoppers in front of some
        int si = 0;
        foreach (var s in pl.stalls)
        {
            var rot = Quaternion.Euler(0f, yaw0 + s.yaw, 0f);
            var keeper = Person(life.transform, false, n, W(s.p) + rot * new Vector3(0f, 0f, -1.1f));
            if (keeper != null) { keeper.mode = ActorMode.Stand; keeper.transform.rotation = rot; n++; }
            if (si++ % 2 == 0)
            {
                var buyer = Person(life.transform, si % 4 == 1, n, W(s.p) + rot * new Vector3(0.4f, 0f, 1.25f));
                if (buyer != null) { buyer.mode = ActorMode.Stand; buyer.transform.rotation = rot * Quaternion.Euler(0f, 180f, 0f); n++; }
            }
        }
        // animals in the market pen
        if (pl.penRadius > 0f)
            for (int i = 0; i < 5; i++)
            {
                Vector3 at = W(pl.pen + new Vector3((i - 2) * 1.6f, 0f, (i % 2) * 1.5f));
                var sheep = Actor.Spawn(i == 4 ? "cow" : "sheep", i == 4 ? 1.9f : 1.1f, false, ActorMode.Wander, at, life.transform);
                if (sheep == null) continue;
                sheep.home = W(pl.pen); sheep.radius = pl.penRadius; sheep.speed = 0.5f;
            }
        info.people = n;
        culled.Add((life, place.position, 240f));
        return info;
    }

    private static bool Near(List<Vector3> line, Vector3 p, float r)
    {
        for (int i = 0; i + 1 < line.Count; i++) if (WorldShape.Seg(p, line[i], line[i + 1], out _) < r) return true;
        return false;
    }

    private static Actor Person(Transform parent, bool female, int i, Vector3 pos)
    {
        var models = female ? Women : Men;
        var a = Actor.Spawn(models[i % models.Length], 1.72f + (i % 4) * 0.03f, true, ActorMode.Stand, pos, parent);
        if (a == null) return null;
        NpcWardrobe.DressRegional(a.gameObject, female, i * 7 + 3);   // thobe / abaya in Najd, galabeya / tarha on the Delta
        return a;
    }

    // Shop sign text on a facade board: EN on top, AR below (shaped), facing the street (not a billboard).
    private static void FacadeSign(Transform parent, Vector3 pos, Quaternion rot, string en, string ar, float size)
    {
        var go = new GameObject("Sign " + en);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot * Quaternion.Euler(0f, 180f, 0f));   // TMP faces -z
        foreach (bool arabic in new[] { false, true })
        {
            var tgo = new GameObject(arabic ? "AR" : "EN");
            tgo.transform.SetParent(go.transform, false);
            tgo.transform.localPosition = new Vector3(0f, arabic ? -0.19f : 0.17f, 0f);
            var t = tgo.AddComponent<TMPro.TextMeshPro>();
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.fontSize = size * (arabic ? 0.8f : 0.7f);
            t.color = Color.white;
            t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            if (arabic)
            {
                var f = Loc.ArabicFont;
                if (f != null) t.font = f;
                t.text = ArabicShaper.Shape(ar);
            }
            else t.text = en;
            Loc.ExcludeFromScene(t);
        }
    }
}

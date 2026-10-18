using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dealer configurations for the farm's own vehicles (Farming-Simulator-style "configure" with Mazraa's own palette and
/// names): paint colour and wheel type, bought at a workshop (the farm loading bay or the dealership yard) from the
/// Vehicles tab of the farm menu.
/// - Paint: recolours the body paint (primitive bodies: the livery colour; Kenney models: a tint over the colour map),
///   never glass, chrome, tyres or the implements. Index 0 = factory colours.
/// - Wheels: Standard (follows Settings > Low tyre pressure), Wide flotation (always low pressure: more grip in the field,
///   slower on the road, wider tyres) or Road (always high pressure, narrower tyres).
/// Saved per vehicle ("mazraa.parity.paint.&lt;id&gt;", "mazraa.parity.wheels.&lt;id&gt;"); a new game wipes the save and
/// VehicleSwitcher re-applies the factory look.
/// </summary>
public static class VehicleConfig
{
    public enum WheelType { Standard, Wide, Road }

    public const string PaintKey = "mazraa.parity.paint.", WheelKey = "mazraa.parity.wheels.";
    public const int PaintCost = 400, WheelCost = 900;

    public static readonly string[] PaintNames = { "Factory colours", "Desert sand", "Palm green", "Night blue", "Saffron yellow", "Pearl white", "Date brown" };
    public static readonly Color[] PaintColors =
    {
        Color.white,
        new Color(0.86f, 0.74f, 0.52f), new Color(0.16f, 0.45f, 0.2f), new Color(0.1f, 0.18f, 0.4f),
        new Color(0.95f, 0.72f, 0.12f), new Color(0.93f, 0.93f, 0.9f), new Color(0.42f, 0.26f, 0.14f),
    };
    public static readonly string[] WheelNames = { "Standard tyres", "Wide flotation tyres", "Road tyres" };

    private static readonly Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();
    private static readonly Dictionary<Transform, Vector3> wheelScale = new Dictionary<Transform, Vector3>();
    private static readonly Dictionary<string, Material> tinted = new Dictionary<string, Material>();
    private static readonly HashSet<Vehicle> applied = new HashSet<Vehicle>();
    private static readonly HashSet<Material> ourPaint = new HashSet<Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { originals.Clear(); wheelScale.Clear(); tinted.Clear(); applied.Clear(); ourPaint.Clear(); }

    public static int Paint(string id) => Mathf.Clamp(SaveData.GetInt(PaintKey + id, 0), 0, PaintNames.Length - 1);
    public static WheelType Wheels(string id) => (WheelType)Mathf.Clamp(SaveData.GetInt(WheelKey + id, 0), 0, 2);

    /// <summary>Tyre pressure used by the drivetrain: wide tyres are always low pressure, road tyres always high.</summary>
    public static bool LowPressure(string id, bool setting)
    {
        var w = Wheels(id);
        return w == WheelType.Wide || (w == WheelType.Standard && setting);
    }

    public static string PaintName(string id) => Loc.Raw(PaintNames[Paint(id)]);
    public static string WheelName(string id) => Loc.Raw(WheelNames[(int)Wheels(id)]);

    /// <summary>English Loc key of why the vehicle can't be configured now (null = it can).</summary>
    public static string Block(Vehicle v, int cost)
    {
        if (v == null) return "No vehicle";
        if (!v.AtWorkshop) return "Park at a workshop (farm loading bay or dealership) to change the configuration";
        var eco = EconomyManager.Instance;
        if (eco != null && eco.Money < cost) return "Not enough money";
        return null;
    }

    /// <summary>Buy the next paint colour (or a given one). Returns true when it was applied.</summary>
    public static bool BuyPaint(Vehicle v, int index)
    {
        string block = Block(v, PaintCost);
        if (block != null) { Alert(Loc.T(block)); return false; }
        index = (index % PaintNames.Length + PaintNames.Length) % PaintNames.Length;
        Pay(PaintCost);
        SaveData.SetInt(PaintKey + v.Id, index);
        Apply(v);
        Alert(Loc.Display(Loc.Raw("Repainted") + ": " + Loc.Raw(PaintNames[index]) + " (" + PaintCost + " " + Loc.Raw("SAR") + ")"));
        return true;
    }

    public static bool BuyWheels(Vehicle v, WheelType type)
    {
        string block = Block(v, WheelCost);
        if (block != null) { Alert(Loc.T(block)); return false; }
        Pay(WheelCost);
        SaveData.SetInt(WheelKey + v.Id, (int)type);
        Apply(v);
        v.RefreshSurface();
        Alert(Loc.Display(Loc.Raw("New wheels") + ": " + Loc.Raw(WheelNames[(int)type]) + " (" + WheelCost + " " + Loc.Raw("SAR") + ")"));
        return true;
    }

    private static void Pay(int cost)
    {
        var eco = EconomyManager.Instance;
        if (eco != null) using (Ledger.As(Ledger.Purchases)) eco.AddMoney(-cost);
    }

    private static void Alert(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }

    public static bool IsApplied(Vehicle v) => applied.Contains(v);

    // ---------------------------------------------------------------- visuals

    /// <summary>Show the saved paint and wheels on a vehicle (idempotent; originals are remembered on first use).</summary>
    public static void Apply(Vehicle v)
    {
        if (v == null) return;
        applied.Add(v);
        int paint = Paint(v.Id);
        Transform hitch = v is Tractor t ? t.hitch : null;
        foreach (var r in v.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || IsWheel(v, r.transform) || (hitch != null && r.transform.IsChildOf(hitch))) continue;
            // What the renderer shows now; if it is not our paint, it is the (possibly restyled) original: remember it,
            // so other systems that swap vehicle materials (graphics presets) are never undone with stale ones.
            var cur = r.sharedMaterials;
            if (cur == null || cur.Length == 0) cur = r.sharedMaterial != null ? new[] { r.sharedMaterial } : null;
            if (cur == null) continue;
            bool ours = false;
            foreach (var m in cur) if (m != null && ourPaint.Contains(m)) ours = true;
            Material[] orig;
            if (!ours || !originals.TryGetValue(r, out orig)) { orig = (Material[])cur.Clone(); originals[r] = orig; }
            if (paint == 0 && !ours) continue;   // factory colours already showing
            var mats = new Material[orig.Length];
            bool any = false;
            for (int i = 0; i < orig.Length; i++)
            {
                mats[i] = orig[i];
                if (paint == 0 || !IsPaint(orig[i])) continue;
                mats[i] = Tint(orig[i], paint);
                any = true;
            }
            if (any || ours) r.sharedMaterials = mats;
        }
        float width = Wheels(v.Id) == WheelType.Wide ? 1.3f : Wheels(v.Id) == WheelType.Road ? 0.85f : 1f;
        if (v.wheels != null)
            foreach (var w in v.wheels)
            {
                if (w == null) continue;
                Vector3 baseScale;
                if (!wheelScale.TryGetValue(w, out baseScale)) { baseScale = w.localScale; wheelScale[w] = baseScale; }
                Vector3 axle = Quaternion.Inverse(w.rotation) * v.transform.right;   // axle direction in the wheel's own axes
                int ax = Mathf.Abs(axle.x) >= Mathf.Abs(axle.y) && Mathf.Abs(axle.x) >= Mathf.Abs(axle.z) ? 0 : Mathf.Abs(axle.y) >= Mathf.Abs(axle.z) ? 1 : 2;
                Vector3 s = baseScale;
                if (ax == 0) s.x *= width; else if (ax == 1) s.y *= width; else s.z *= width;
                w.localScale = s;
            }
    }

    /// <summary>Wheel scale factor across the axle now (tests).</summary>
    public static float WheelWidthFactor(Vehicle v)
    {
        if (v == null || v.wheels == null) return 1f;
        foreach (var w in v.wheels)
        {
            if (w == null || !wheelScale.TryGetValue(w, out var b)) continue;
            float bs = Mathf.Max(Mathf.Abs(b.x), Mathf.Max(Mathf.Abs(b.y), Mathf.Abs(b.z)));
            Vector3 s = w.localScale;
            float r = Mathf.Abs(s.x / Mathf.Max(1e-4f, b.x));
            if (Mathf.Abs(r - 1f) < 1e-3f) r = Mathf.Abs(s.y / Mathf.Max(1e-4f, b.y));
            if (Mathf.Abs(r - 1f) < 1e-3f) r = Mathf.Abs(s.z / Mathf.Max(1e-4f, b.z));
            return r;
        }
        return 1f;
    }

    private static bool IsWheel(Vehicle v, Transform t)
    {
        if (v.wheels == null) return false;
        foreach (var w in v.wheels) if (w != null && t.IsChildOf(w)) return true;
        return false;
    }

    /// <summary>Body paint: a restyled model's "(paint)" material, or the primitive livery colours.</summary>
    public static bool IsPaint(Material m)
    {
        if (m == null) return false;
        if (!string.IsNullOrEmpty(m.name) && m.name.EndsWith("(paint)")) return true;
        return Near(m.color, VehicleVisuals.TractorRed) || Near(m.color, VehicleVisuals.MazraaGreen);
    }

    private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

    private static Material Tint(Material src, int paint)
    {
        string key = src.GetHashCode() + "|" + paint;
        if (tinted.TryGetValue(key, out var m) && m != null) return m;
        bool textured = !string.IsNullOrEmpty(src.name) && src.name.EndsWith("(paint)");
        Color c = textured ? Color.Lerp(Color.white, PaintColors[paint], 0.8f) : PaintColors[paint];
        m = new Material(src);
        m.name = (src.name ?? "") + " " + PaintNames[paint];
        m.SetColor("_BaseColor", c);
        m.color = c;
        tinted[key] = m;
        ourPaint.Add(m);
        return m;
    }

    /// <summary>Colour now shown by the first painted material of a vehicle (tests); white if none.</summary>
    public static Color BodyColor(Vehicle v)
    {
        if (v == null) return Color.white;
        foreach (var kv in originals)
        {
            if (kv.Key == null || !kv.Key.transform.IsChildOf(v.transform)) continue;
            var now = kv.Key.sharedMaterials;
            for (int i = 0; i < kv.Value.Length && now != null && i < now.Length; i++) if (IsPaint(kv.Value[i])) return now[i] != null ? now[i].color : Color.white;
        }
        return Color.white;
    }
}

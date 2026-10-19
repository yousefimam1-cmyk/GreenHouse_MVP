using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a tractor implement does to the ground. The vehicle side (attaching, lowering, switching implements, HUD) belongs to
/// Tractor / the vehicle code; FarmCore owns the field effect: Tractor.Work calls TractorWork every frame and the implement paints
/// the cells under its working width along the path the hitch travelled (FieldWork). Implement mass, draft and work speed come
/// from Data/FarmCore tools. Lowered is the hook for the hitch's attach / lower / fold state (default: TractorHitch InWorkPose).
/// </summary>
public static class FarmTools
{
    /// <summary>Is the implement on this tractor lowered into the ground? Set by the vehicle code (attach / lower state).</summary>
    public static Func<Tractor, bool> Lowered = DefaultLowered;
    /// <summary>The hitch (TractorHitch): an implement attached, lowered and unfolded.</summary>
    public static bool DefaultLowered(Tractor t) => t != null && t.ActiveImplement != Implement.None && t.InWorkPose;

    /// <summary>Number of Implement values (Tractor cycles through them).</summary>
    public static int ImplementCount => Enum.GetValues(typeof(Implement)).Length;

    private static readonly Dictionary<int, Vector3> lastHitch = new Dictionary<int, Vector3>();
    private static readonly HashSet<int> visualsBuilt = new HashSet<int>();
    private static float lastBlockedAlert = -10f;
    public static string LastBlocked { get; private set; }
    public static int CellsWorked { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Lowered = DefaultLowered; lastHitch.Clear(); visualsBuilt.Clear(); lastBlockedAlert = -10f; LastBlocked = null; CellsWorked = 0; }

    public static FieldTool ToolOf(Implement i)
    {
        switch (i)
        {
            case Implement.Plow: return FieldTool.Plough;
            case Implement.Seeder: return FieldTool.Seeder;
            case Implement.Harvester: return FieldTool.Harvester;
            case Implement.Cultivator: return FieldTool.Cultivator;
            case Implement.Spreader: return FieldTool.Spreader;
            case Implement.Sprayer: return FieldTool.Sprayer;
            case Implement.Roller: return FieldTool.Roller;
            case Implement.Mulcher: return FieldTool.Mulcher;
            case Implement.Mower: return FieldTool.Mower;
            case Implement.Baler: return FieldTool.Baler;
            default: return FieldTool.None;
        }
    }

    public static Implement ImplementOf(FieldTool t)
    {
        foreach (Implement i in Enum.GetValues(typeof(Implement))) if (i != Implement.None && ToolOf(i) == t) return i;
        return Implement.None;
    }

    public static float Width(Implement i) => i == Implement.None ? 0f : FcData.Tool(ToolOf(i)).width;
    public static float Mass(Implement i) => i == Implement.None ? 0f : FcData.Tool(ToolOf(i)).mass;
    /// <summary>Pull needed while the implement works the soil (N).</summary>
    public static float Draft(Implement i) => i == Implement.None ? 0f : FcData.Tool(ToolOf(i)).draft;
    /// <summary>Highest working speed (m/s).</summary>
    public static float MaxSpeed(Implement i) => i == Implement.None ? 99f : FcData.Tool(ToolOf(i)).speedKmh / 3.6f;

    /// <summary>One frame of a tractor pass: paint the strip the hitch moved over since the last frame. Returns cells changed.</summary>
    public static int TractorWork(Tractor t, Implement imp, Vector3 hitch, float speed)
    {
        var ff = FarmFields.Instance;
        if (t == null || ff == null) return 0;
        int key = t.GetInstanceID();
        if (imp == Implement.None || Mathf.Abs(speed) < 0.5f || !Lowered(t))
        {
            lastHitch.Remove(key);
            return 0;
        }
        Vector3 last;
        if (!lastHitch.TryGetValue(key, out last)) { lastHitch[key] = hitch; return 0; }
        Vector3 step = hitch - last; step.y = 0f;
        if (step.magnitude < 0.25f) return 0;            // paint in ~25 cm steps
        lastHitch[key] = hitch;
        if (step.magnitude > 8f) return 0;               // teleported (reset, fast travel)
        float width = imp == t.ActiveImplement ? t.WorkingWidth : Width(imp);   // hitch: 0 when folded
        if (width <= 0f) return 0;
        if (ff.FieldAt(hitch, width) == null && ff.FieldAt(last, width) == null) return 0;
        var ctx = ff.Work(ToolOf(imp), last, hitch, width, false, true);
        CellsWorked += ctx.cells;
        LastBlocked = ctx.blocked;
        if (ctx.blocked != null && ctx.cells == 0 && Time.time - lastBlockedAlert > 4f)
        {
            lastBlockedAlert = Time.time;
            FcText.Say(FcText.R(ctx.blocked));
        }
        return ctx.cells;
    }

    /// <summary>Forget the tractor's last hitch point (implement switched, vehicle reset).</summary>
    public static void ResetPass(Tractor t) { if (t != null) lastHitch.Remove(t.GetInstanceID()); }

    /// <summary>Is the tractor's hitch over a field?</summary>
    public static bool OverField(Vector3 hitch) => FarmFields.Instance != null && FarmFields.Instance.OnField(hitch);

    // ------------------------------------------------------------------ visuals of the FarmCore implements

    /// <summary>Simple models for the implements the vehicle spawner doesn't build (cultivator, spreader, sprayer, roller, mulcher, mower, baler).</summary>
    public static void EnsureVisuals(Tractor t)
    {
        if (t == null || t.hitch == null || !visualsBuilt.Add(t.GetInstanceID())) return;
        var steel = Build.Mat(new Color(0.55f, 0.56f, 0.58f), 0.6f, 0.6f);
        var dark = Build.Mat(new Color(0.12f, 0.12f, 0.13f), 0.3f);
        var paint = Build.Mat(new Color(0.85f, 0.18f, 0.12f), 0.45f);
        var green = Build.Mat(new Color(0.18f, 0.48f, 0.22f), 0.4f);
        var yellow = Build.Mat(new Color(0.95f, 0.75f, 0.1f), 0.4f);
        var white = Build.Mat(new Color(0.9f, 0.9f, 0.88f), 0.4f);

        var cult = Root(t, "Cultivator");
        Build.Part(PrimitiveType.Cube, cult, new Vector3(0, -0.15f, 0.3f), new Vector3(3f, 0.12f, 0.12f), paint, "Frame");
        Build.Part(PrimitiveType.Cube, cult, new Vector3(0, -0.15f, -0.3f), new Vector3(3f, 0.12f, 0.12f), paint, "Frame");
        for (int i = 0; i < 7; i++) Build.Part(PrimitiveType.Cube, cult, new Vector3(-1.35f + i * 0.45f, -0.45f, (i % 2) * 0.6f - 0.3f), new Vector3(0.05f, 0.5f, 0.08f), steel, "Tine");
        var spread = Root(t, "Spreader");
        Build.Part(PrimitiveType.Cylinder, spread, new Vector3(0, 0.3f, 0), new Vector3(1.2f, 0.45f, 1.2f), white, "Hopper");
        Build.Part(PrimitiveType.Cylinder, spread, new Vector3(-0.35f, -0.35f, -0.2f), new Vector3(0.5f, 0.03f, 0.5f), steel, "Disc");
        Build.Part(PrimitiveType.Cylinder, spread, new Vector3(0.35f, -0.35f, -0.2f), new Vector3(0.5f, 0.03f, 0.5f), steel, "Disc");
        var spray = Root(t, "Sprayer");
        Build.Part(PrimitiveType.Cube, spray, new Vector3(0, 0.3f, 0.2f), new Vector3(1.1f, 0.9f, 0.9f), white, "Tank");
        Build.Part(PrimitiveType.Cube, spray, new Vector3(0, -0.1f, -0.4f), new Vector3(12f, 0.06f, 0.06f), steel, "Boom");
        for (int i = 0; i < 24; i++) Build.Part(PrimitiveType.Cube, spray, new Vector3(-5.75f + i * 0.5f, -0.2f, -0.4f), new Vector3(0.03f, 0.12f, 0.03f), dark, "Nozzle");
        var roll = Root(t, "Roller");
        Build.Part(PrimitiveType.Cylinder, roll, new Vector3(0, -0.25f, -0.2f), new Vector3(0.6f, 2f, 0.6f), dark, "Drum", new Vector3(0, 0, 90));
        Build.Part(PrimitiveType.Cube, roll, new Vector3(0, 0.15f, 0.2f), new Vector3(0.2f, 0.1f, 0.8f), paint, "Drawbar");
        var mulch = Root(t, "Mulcher");
        Build.Part(PrimitiveType.Cube, mulch, new Vector3(0, -0.2f, -0.1f), new Vector3(3f, 0.6f, 0.8f), green, "Hood");
        Build.Part(PrimitiveType.Cylinder, mulch, new Vector3(0, -0.45f, -0.1f), new Vector3(0.35f, 1.45f, 0.35f), steel, "Rotor", new Vector3(0, 0, 90));
        var mow = Root(t, "Mower");
        Build.Part(PrimitiveType.Cube, mow, new Vector3(0, -0.45f, -0.2f), new Vector3(3f, 0.1f, 0.6f), paint, "Cutter bar");
        Build.Part(PrimitiveType.Cube, mow, new Vector3(0, -0.2f, 0.2f), new Vector3(0.3f, 0.3f, 0.6f), dark, "Gearbox");
        var baler = Root(t, "Baler");
        Build.Part(PrimitiveType.Cube, baler, new Vector3(0, 0.25f, -0.6f), new Vector3(1.8f, 1.4f, 1.8f), yellow, "Chamber");
        Build.Part(PrimitiveType.Cube, baler, new Vector3(0, -0.4f, 0.5f), new Vector3(2.2f, 0.25f, 0.6f), dark, "Pickup");
        foreach (var r in t.hitch.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
        t.RegisterVisual(Implement.Cultivator, cult.gameObject);
        t.RegisterVisual(Implement.Spreader, spread.gameObject);
        t.RegisterVisual(Implement.Sprayer, spray.gameObject);
        t.RegisterVisual(Implement.Roller, roll.gameObject);
        t.RegisterVisual(Implement.Mulcher, mulch.gameObject);
        t.RegisterVisual(Implement.Mower, mow.gameObject);
        t.RegisterVisual(Implement.Baler, baler.gameObject);
    }

    private static Transform Root(Tractor t, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(t.hitch, false);
        return go.transform;
    }
}

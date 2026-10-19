using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The look of a vehicle, cheap on RAM and CPU: URP/Lit paint, rubber, chrome and glass materials (glossy paint, matte
/// tyres); the Mazraa livery (green / sand truck, red tractor: recoloured copies of the Kenney colour map, 6 KB each);
/// emissive head, tail / brake, indicator and reversing lamps and an amber roof beacon that react to driving; one
/// headlight spot at night; exhaust smoke that thickens when you accelerate and a dust trail on sand, dirt and fields
/// (two small particle systems); and a little body pitch / roll when accelerating, braking and turning (the "Body" pivot).
/// Wheel spin and steering are done by Vehicle. Built by VehicleSpawner.
/// </summary>
public class VehicleVisuals : MonoBehaviour
{
    public static readonly Color MazraaGreen = new Color(0.1f, 0.4f, 0.22f);
    public static readonly Color MazraaSand = new Color(0.9f, 0.82f, 0.64f);
    public static readonly Color MazraaGold = new Color(0.86f, 0.66f, 0.2f);
    public static readonly Color TractorRed = new Color(0.72f, 0.1f, 0.08f);

    /// <summary>Where the lamps, exhaust and beacon go (vehicle space).</summary>
    public struct Layout
    {
        public Vector3 headL, headR, tailL, tailR, exhaust, beacon, dust;
        public bool exhaustUp;
        public float lamp;   // lamp size
    }

    // shared materials (cached by Build.Mat), on / off variants switched per frame: no allocations, batching friendly
    public static Material Paint(Color c) => Build.Mat(c, 0.62f, 0.25f);
    public static Material Chrome => Build.Mat(new Color(0.78f, 0.79f, 0.8f), 0.92f, 1f);
    public static Material Rubber => Build.Mat(new Color(0.05f, 0.05f, 0.055f), 0.15f);
    public static Material Glass => Build.Mat(new Color(0.08f, 0.12f, 0.16f), 0.95f, 0.1f);
    private static Material HeadOff => Build.Mat(new Color(0.9f, 0.9f, 0.85f), 0.9f, 0f, new Color(0.25f, 0.24f, 0.2f));
    private static Material HeadOn => Build.Mat(new Color(1f, 0.97f, 0.88f), 0.9f, 0f, new Color(3.2f, 3f, 2.5f));
    private static Material TailOff => Build.Mat(new Color(0.55f, 0.04f, 0.04f), 0.85f);
    private static Material TailOn => Build.Mat(new Color(0.75f, 0.05f, 0.05f), 0.85f, 0f, new Color(0.9f, 0.04f, 0.04f));
    private static Material BrakeOn => Build.Mat(new Color(1f, 0.08f, 0.06f), 0.85f, 0f, new Color(3.5f, 0.15f, 0.1f));
    private static Material AmberOff => Build.Mat(new Color(0.75f, 0.42f, 0.05f), 0.85f);
    private static Material AmberOn => Build.Mat(new Color(1f, 0.6f, 0.1f), 0.85f, 0f, new Color(3.2f, 1.6f, 0.2f));
    private static Material ReverseOn => Build.Mat(new Color(1f, 1f, 1f), 0.9f, 0f, new Color(2.5f, 2.5f, 2.5f));

    private static Material smokeMat;
    private static readonly Dictionary<string, Material> modelMats = new Dictionary<string, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { smokeMat = null; modelMats.Clear(); }

    private Vehicle v;
    private Transform body;
    private Renderer[] heads, tails, indL, indR, reverse, beacons;
    private ParticleSystem smoke, dust;
    private Light headLamp;
    private float lastSpeed, lastYaw, accel, roll, pitch, blink;

    // ---- state for tests / debugging
    public float SmokeRate { get; private set; }
    public float DustRate { get; private set; }
    public bool HeadlightsOn { get; private set; }
    public bool BrakeLightsOn { get; private set; }
    public bool IndicatorLeftOn { get; private set; }
    public bool IndicatorRightOn { get; private set; }
    public bool BeaconOn { get; private set; }
    public float Pitch => pitch;
    public float Roll => roll;
    public int LampCount => heads.Length + tails.Length + indL.Length + indR.Length + reverse.Length + beacons.Length;

    // ------------------------------------------------------------------ setup
    public static VehicleVisuals Setup(Vehicle vehicle, Transform body, Layout layout)
    {
        var vis = vehicle.gameObject.AddComponent<VehicleVisuals>();
        vis.v = vehicle;
        vis.body = body;
        vis.Construct(layout);
        return vis;
    }

    private void Construct(Layout l)
    {
        float s = l.lamp > 0f ? l.lamp : 0.22f;
        Vector3 head = new Vector3(s * 1.3f, s * 0.7f, 0.05f), tail = new Vector3(s, s * 0.6f, 0.05f), ind = new Vector3(s * 0.45f, s * 0.4f, 0.05f);
        heads = new[] { Lamp("Headlight", l.headL, head, HeadOff), Lamp("Headlight", l.headR, head, HeadOff) };
        tails = new[] { Lamp("Tail light", l.tailL, tail, TailOff), Lamp("Tail light", l.tailR, tail, TailOff) };
        // indicators just outside the head and tail lamps, reversing lamps just inside the tail lamps
        indL = new[] { Lamp("Indicator", l.headL + new Vector3(-Side(l.headR) * s * 1.05f, 0f, -0.01f), ind, AmberOff), Lamp("Indicator", l.tailL + new Vector3(-Side(l.tailR) * s * 0.85f, 0f, 0.01f), ind, AmberOff) };
        indR = new[] { Lamp("Indicator", l.headR + new Vector3(Side(l.headR) * s * 1.05f, 0f, -0.01f), ind, AmberOff), Lamp("Indicator", l.tailR + new Vector3(Side(l.tailR) * s * 0.85f, 0f, 0.01f), ind, AmberOff) };
        reverse = new[] { Lamp("Reversing light", l.tailL + new Vector3(Side(l.tailR) * s * 0.75f, 0f, 0.01f), ind, TailOff) };
        if (l.headL.x > l.headR.x) { var t = indL; indL = indR; indR = t; }   // left is -x

        // amber roof beacon (farm vehicles carry one on the road)
        var b = new GameObject("Beacon").transform;
        b.SetParent(body, false);
        b.localPosition = l.beacon;
        Build.Part(PrimitiveType.Cylinder, b, new Vector3(0f, 0.03f, 0f), new Vector3(0.24f, 0.03f, 0.24f), Rubber, "Base");
        beacons = new[] { Build.Part(PrimitiveType.Sphere, b, new Vector3(0f, 0.12f, 0f), new Vector3(0.2f, 0.2f, 0.2f), AmberOff, "Lens").GetComponent<Renderer>() };

        smoke = Emitter("Exhaust smoke", l.exhaust, new Color(0.35f, 0.35f, 0.36f, 0.45f), 1.6f, 0.45f, l.exhaustUp ? 1.4f : 0.7f, -0.06f, 70);
        smoke.transform.localRotation = l.exhaustUp ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.Euler(0f, 180f, 0f);
        dust = Emitter("Dust trail", l.dust, new Color(0.84f, 0.72f, 0.52f, 0.5f), 2.2f, 1.1f, 0.8f, -0.02f, 90);
        dust.transform.localRotation = Quaternion.Euler(-20f, 180f, 0f);

        var lampGo = new GameObject("Headlamp");
        lampGo.transform.SetParent(body, false);
        lampGo.transform.localPosition = (l.headL + l.headR) * 0.5f + new Vector3(0f, 0.05f, 0.1f);
        lampGo.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        headLamp = lampGo.AddComponent<Light>();
        headLamp.type = LightType.Spot;
        headLamp.range = 32f; headLamp.spotAngle = 70f; Rp.LegacyLight(headLamp, 3f);
        headLamp.color = new Color(1f, 0.93f, 0.8f);
        headLamp.shadows = LightShadows.None;
        headLamp.enabled = false;

        foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r.gameObject.layer == 0) r.gameObject.layer = 7;
        lastSpeed = v.Speed; lastYaw = v.Yaw;
    }

    private static float Side(Vector3 p) => p.x < 0f ? -1f : 1f;

    private Renderer Lamp(string name, Vector3 pos, Vector3 size, Material mat)
    {
        var go = Build.Part(PrimitiveType.Cube, body, pos, size, mat, name);
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }

    private ParticleSystem Emitter(string name, Vector3 pos, Color color, float life, float size, float speed, float gravity, int max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(body, false);
        go.transform.localPosition = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.startLifetime = life; main.startSpeed = speed; main.startSize = size; main.maxParticles = max;
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // the trail stays behind the vehicle
        main.gravityModifier = gravity;
        var em = ps.emission; em.rateOverTime = 0f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(0.15f, 0.15f, 0.15f);
        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.sharedMaterial = SmokeMaterial();
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        ps.Play();
        return ps;
    }

    /// <summary>Soft round particle shared by every vehicle (white base: the particle colour tints it).</summary>
    private static Material SmokeMaterial()
    {
        if (smokeMat != null) return smokeMat;
        smokeMat = new Material(DesertWeatherFX.DustMaterial());
        smokeMat.name = "Vehicle smoke (runtime)";
        Rp.SetColor(smokeMat, Color.white);
        return smokeMat;
    }

    // ------------------------------------------------------------------ model materials + layout
    /// <summary>
    /// Give a Kenney vehicle model lit (HDRP/Lit) paint (glossy, a touch metallic) with the livery colour map, and matte rubber
    /// on its wheels (original colour map, so hubs keep their grey). Materials are shared between copies.
    /// </summary>
    public static void Restyle(GameObject model, Transform[] wheels, Texture livery)
    {
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            bool wheel = false;
            if (wheels != null) foreach (var w in wheels) if (w != null && r.transform.IsChildOf(w)) wheel = true;
            var mats = r.sharedMaterials;
            if (mats == null) continue;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i] = Restyled(mats[i], wheel ? null : livery, wheel);
            r.sharedMaterials = mats;
        }
    }

    private static Material Restyled(Material src, Texture tex, bool rubber)
    {
        string key = (rubber ? "rubber|" : "paint|") + (tex != null ? tex.name : "-") + "|" + src.name;
        Material m;
        if (modelMats.TryGetValue(key, out m) && m != null) return m;
        m = new Material(src);
        m.name = src.name + (rubber ? " (rubber)" : " (paint)");
        if (tex != null) { Rp.SetBaseMap(m, tex); m.mainTexture = tex; }
        Rp.SetSmoothness(m, rubber ? 0.18f : 0.62f);
        Rp.SetMetallic(m, rubber ? 0f : 0.22f);
        modelMats[key] = m;
        return m;
    }

    /// <summary>
    /// Lamp / beacon / exhaust spots on a Kenney model, from its body mesh bounds (fractions measured on the CC0 models:
    /// x across, y up, z from the back to the front). Mirrored to the right end if the model faces the other way.
    /// </summary>
    public static Layout ModelLayout(GameObject model, Transform[] wheels, Transform root, Transform body, bool tractor)
    {
        MeshFilter bodyMesh = null; float best = 0f;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            bool wheel = false;
            if (wheels != null) foreach (var w in wheels) if (w != null && mf.transform.IsChildOf(w)) wheel = true;
            if (wheel || mf.sharedMesh == null) continue;
            var sz = mf.sharedMesh.bounds.size;
            if (sz.x * sz.y * sz.z >= best) { best = sz.x * sz.y * sz.z; bodyMesh = mf; }
        }
        Bounds mb = bodyMesh != null ? bodyMesh.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.zero);
        bool ok = bodyMesh != null && mb.size.x > 1e-4f && mb.size.y > 1e-4f && mb.size.z > 1e-4f;
        bool flip = false;
        System.Func<float, float, float, Vector3> P = (fx, fy, fz) =>
        {
            if (flip) fz = 1f - fz;
            Vector3 local = mb.min + Vector3.Scale(mb.size, new Vector3(1f - fx, fy, fz));
            return body.InverseTransformPoint(bodyMesh.transform.TransformPoint(local));
        };
        var l = new Layout();
        if (!ok) return tractor ? TractorPrimitiveLayout() : TruckPrimitiveLayout();
        if (P(0.5f, 0.5f, 1f).z < P(0.5f, 0.5f, 0f).z) flip = true;   // the model's front must be +z of the vehicle
        if (!tractor)
        {
            l.headL = P(0.82f, 0.355f, 1f) + new Vector3(0f, 0f, 0.03f); l.headR = P(0.18f, 0.355f, 1f) + new Vector3(0f, 0f, 0.03f);
            l.tailL = P(0.82f, 0.435f, 0f) - new Vector3(0f, 0f, 0.03f); l.tailR = P(0.18f, 0.435f, 0f) - new Vector3(0f, 0f, 0.03f);
            l.beacon = P(0.5f, 1f, 0.66f);
            l.exhaust = P(0.25f, 0.12f, 0f) - new Vector3(0f, 0f, 0.05f);
            l.dust = P(0.5f, 0.05f, 0.02f);
            l.lamp = 0.22f;
        }
        else
        {
            l.headL = P(0.6f, 0.4f, 1f) + new Vector3(0f, 0f, 0.03f); l.headR = P(0.4f, 0.4f, 1f) + new Vector3(0f, 0f, 0.03f);
            l.tailL = P(0.88f, 0.47f, 0f) - new Vector3(0f, 0f, 0.03f); l.tailR = P(0.12f, 0.47f, 0f) - new Vector3(0f, 0f, 0.03f);
            l.beacon = P(0.5f, 1f, 0.25f);
            l.exhaust = P(0.42f, 0.75f, 0.63f); l.exhaustUp = true;
            l.dust = P(0.5f, 0.05f, 0.1f);
            l.lamp = 0.16f;
        }
        return l;
    }

    public static Layout TruckPrimitiveLayout() => new Layout
    {
        headL = new Vector3(-0.65f, 0.85f, 2.3f), headR = new Vector3(0.65f, 0.85f, 2.3f),
        tailL = new Vector3(-0.7f, 0.8f, -2.36f), tailR = new Vector3(0.7f, 0.8f, -2.36f),
        beacon = new Vector3(0f, 1.83f, 0.75f), exhaust = new Vector3(0.82f, 2.05f, 0.0f), exhaustUp = true,
        dust = new Vector3(0f, -0.3f, -2.2f), lamp = 0.24f,
    };

    public static Layout TractorPrimitiveLayout() => new Layout
    {
        headL = new Vector3(-0.3f, 0.75f, 2.07f), headR = new Vector3(0.3f, 0.75f, 2.07f),
        tailL = new Vector3(-0.75f, 0.85f, -1.12f), tailR = new Vector3(0.75f, 0.85f, -1.12f),
        beacon = new Vector3(0f, 2.3f, -0.45f), exhaust = new Vector3(0.3f, 1.68f, 1.4f), exhaustUp = true,
        dust = new Vector3(0f, -0.5f, -1.2f), lamp = 0.16f,
    };

    // ------------------------------------------------------------------ per frame
    private void LateUpdate()
    {
        if (v == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        bool driving = v.IsDriving || v.RemoteDriven;
        bool night = DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
        blink = Mathf.Repeat(blink + dt * 1.6f, 1f);
        bool blinkOn = blink < 0.5f;

        HeadlightsOn = driving;
        Set(heads, HeadlightsOn ? HeadOn : HeadOff);
        if (headLamp != null && headLamp.enabled != (driving && night)) headLamp.enabled = driving && night;
        BrakeLightsOn = v.BrakeLights;
        Set(tails, BrakeLightsOn ? BrakeOn : (driving || night) ? TailOn : TailOff);
        Set(reverse, driving && v.Speed < -0.2f ? ReverseOn : TailOff);
        float steer = v.SteerInput;
        IndicatorLeftOn = driving && steer < -0.45f && blinkOn;
        IndicatorRightOn = driving && steer > 0.45f && blinkOn;
        Set(indL, IndicatorLeftOn ? AmberOn : AmberOff);
        Set(indR, IndicatorRightOn ? AmberOn : AmberOff);
        BeaconOn = driving && Mathf.Repeat(blink * 2f, 1f) < 0.5f;
        Set(beacons, BeaconOn ? AmberOn : AmberOff);

        // exhaust: idle puffs with the engine on, thick when the throttle opens under load
        var d = v.Drive;
        bool engine = d != null && d.EngineOn && v.Fuel > 0f;
        float push = Mathf.Clamp01(Mathf.Abs(v.ThrottleInput)) * (0.4f + 0.6f * (d != null ? d.Load : 0f));
        SmokeRate = engine ? 2f + 34f * push : 0f;
        var em = smoke.emission; em.rateOverTime = SmokeRate;
        // dust on loose dry ground, more with speed
        var surf = v.SurfaceNow;
        bool loose = !v.SurfaceWet && (surf == DriveSurface.Sand || surf == DriveSurface.DirtRoad || surf == DriveSurface.Field);
        DustRate = loose ? Mathf.Clamp01((Mathf.Abs(v.Speed) - 1.5f) / 9f) * (surf == DriveSurface.Sand ? 40f : 28f) : 0f;
        var dem = dust.emission; dem.rateOverTime = DustRate;

        // body pitch (accelerating lifts the nose, braking dives) and roll (leans out of turns), springy and small
        float a = (v.Speed - lastSpeed) / dt;
        accel = Mathf.Lerp(accel, Mathf.Clamp(a, -12f, 12f), 1f - Mathf.Exp(-6f * dt));
        float yawRate = Mathf.DeltaAngle(lastYaw, v.Yaw) * Mathf.Deg2Rad / dt;
        lastSpeed = v.Speed; lastYaw = v.Yaw;
        float targetPitch = Mathf.Clamp(-accel * 0.4f, -2.5f, 2.5f);
        float targetRoll = Mathf.Clamp(v.Speed * yawRate * 0.45f, -3.5f, 3.5f);
        pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-5f * dt));
        roll = Mathf.Lerp(roll, targetRoll, 1f - Mathf.Exp(-5f * dt));
        if (body != null) body.localRotation = Quaternion.Euler(pitch, 0f, roll);
    }

    private static void Set(Renderer[] rs, Material m)
    {
        if (rs == null) return;
        foreach (var r in rs) if (r != null && r.sharedMaterial != m) r.sharedMaterial = m;
    }
}

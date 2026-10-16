using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What vehicles bump into. Fences, walls, buildings, the well, greenhouses, tree trunks and stalls keep their colliders
/// and stop a vehicle (Vehicle sweeps its body box ahead every physics step and asks Blocks). Gates stay open: fence panels
/// on the loading bay and market pads, around the parked vehicles, across the desert track, over field plots and inside the
/// animal pen are taken down at start (OpenGates), so fields and pads can always be reached by road. Never blocking:
/// triggers, the terrain, walkable slopes, low things under 35 cm (kerbs), field plots, loose crates / bales and people /
/// animals (NpcBump bumps them).
/// Fence pieces of the scene (FenceLayout panels and posts) that lack a collider get a box from their mesh at runtime.
/// </summary>
public static class VehicleObstacles
{
    public const float LowObstacle = 0.35f;
    /// <summary>Extra clearance around a pad, plot or the track when taking fence panels down (m).</summary>
    public const float GateMargin = 2.5f;
    public static int GatesOpened { get; private set; }
    public static int CollidersAdded { get; private set; }
    public static readonly List<GameObject> OpenedPanels = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { GatesOpened = 0; CollidersAdded = 0; OpenedPanels.Clear(); }

    /// <summary>True for a fence of the scene (anything under an object named "...Fence..."), not the animal pen's own fence.</summary>
    public static bool InFence(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            if (t.name.IndexOf("Fence", System.StringComparison.OrdinalIgnoreCase) >= 0) return t.GetComponentInParent<AnimalPen>() == null;
        }
        return false;
    }

    /// <summary>Should a sweep hit stop the vehicle?</summary>
    public static bool Blocks(RaycastHit hit, float groundY)
    {
        var c = hit.collider;
        if (c == null || c.isTrigger || c is TerrainCollider) return false;
        if (hit.normal.y > 0.55f) return false;                       // ground, ramps, dunes: drive over them
        if (c.bounds.max.y < groundY + LowObstacle) return false;      // kerbs, flat pads
        if (c.GetComponentInParent<FieldPlot>() != null) return false; // fields are driven on, even where the soil sits up a slope
        if (c.GetComponentInParent<Carriable>() != null) return false; // loose crates and bales
        if (NpcBump.IsBumpable(c.transform)) return false;            // people and animals get bumped instead
        return true;
    }

    /// <summary>Give scene fence meshes without any collider a box (their mesh bounds). Returns how many were added.</summary>
    public static int EnsureFenceColliders()
    {
        int n = 0;
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            if (mf == null || mf.sharedMesh == null || !InFence(mf.transform)) continue;
            if (mf.GetComponentInParent<Collider>() != null || mf.GetComponentsInChildren<Collider>().Length > 0) continue;
            var box = mf.gameObject.AddComponent<BoxCollider>();
            var b = mf.sharedMesh.bounds;
            box.center = b.center; box.size = b.size;
            n++;
        }
        CollidersAdded += n;
        return n;
    }

    /// <summary>Take down scene fence panels where a gate must be. Safe to call again (after the track or plots appear).</summary>
    public static int OpenGates()
    {
        int n = 0;
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (c == null || !c.enabled || c.isTrigger || !InFence(c.transform)) continue;
            var b = c.bounds;
            float span = Mathf.Max(b.size.x, b.size.z);
            if (span > 8f) continue;                                   // a whole fence line on one collider: leave it
            if (!InGate(b.center, span * 0.5f)) continue;
            c.gameObject.SetActive(false);
            OpenedPanels.Add(c.gameObject);
            n++;
        }
        GatesOpened += n;
        return n;
    }

    /// <summary>Is a fence piece at p (half size r) on a pad, plot, the track, the pen or where a vehicle is parked?</summary>
    public static bool InGate(Vector3 p, float r)
    {
        p.y = 0f;
        foreach (var z in Object.FindObjectsByType<DeliveryZone>(FindObjectsSortMode.None))
            if (Flat(z.transform.position - p).magnitude < z.radius + GateMargin + r) return true;
        foreach (var v in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
            if (Flat(v.transform.position - p).magnitude < v.BodyHalfExtents.z + 1.5f + r) return true;
        foreach (var plot in Object.FindObjectsByType<FieldPlot>(FindObjectsSortMode.None))
            if (InRect(plot.transform, plot.size, p, 1.5f + r)) return true;
        var pen = AnimalPen.Instance;
        if (pen != null && InRect(pen.transform, pen.size, p, 1f + r)) return true;
        var dressing = DesertDressing.Instance;
        if (dressing != null && dressing.Track.Count > 1 && dressing.DistanceToTrack(p) < 3.5f + r) return true;
        return false;
    }

    private static bool InRect(Transform t, Vector2 size, Vector3 p, float pad)
    {
        Vector3 d = p - t.position; d.y = 0f;
        Vector3 l = Quaternion.Inverse(t.rotation) * d;
        return Mathf.Abs(l.x) < size.x * 0.5f + pad && Mathf.Abs(l.z) < size.y * 0.5f + pad;
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}

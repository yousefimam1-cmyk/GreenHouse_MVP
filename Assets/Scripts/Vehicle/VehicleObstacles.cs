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
    private static void ResetStatics() { GatesOpened = 0; CollidersAdded = 0; LinesSplit = 0; OpenedPanels.Clear(); }

    /// <summary>True for a fence of the scene (anything under an object named "...Fence..."), not the animal pen's own fence.</summary>
    public static bool InFence(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            if (t.name.IndexOf("Fence", System.StringComparison.OrdinalIgnoreCase) >= 0) return t.GetComponentInParent<AnimalPen>() == null;
        }
        return false;
    }

    /// <summary>Drivable / walkable surfaces that are never obstacles: roads, pads, ramps, dunes, sidewalks (by name or marker).</summary>
    public static bool IsWalkable(Collider c)
    {
        if (c == null) return false;
        if (c is TerrainCollider) return true;
        for (var t = c.transform; t != null; t = t.parent)
        {
            if (t.GetComponent<Walkable>() != null) return true;
            string n = t.name;
            if (n == "Roads" || n == "Road" || n == "Dune" || n == "Track" || n == "Sand" || n.StartsWith("Sidewalk") || n.StartsWith("Pad")) return true;
        }
        return false;
    }

    /// <summary>Should a sweep hit stop the vehicle?</summary>
    public static bool Blocks(RaycastHit hit, float groundY)
    {
        var c = hit.collider;
        if (c == null || c.isTrigger || c is TerrainCollider) return false;
        if (hit.normal.y > 0.55f) return false;                       // ground, ramps, dunes: drive over them
        if (IsWalkable(c)) return false;                               // road / sidewalk meshes, pads: never a wall
        if (c.bounds.max.y < groundY + LowObstacle) return false;      // kerbs, flat pads
        if (c.GetComponentInParent<FarmFieldObject>() != null) return false; // fields are driven on, even where the soil sits up a slope
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

    /// <summary>Long fence colliders cut where a road crosses them (OpenGates).</summary>
    public static int LinesSplit { get; private set; }

    /// <summary>
    /// Take down scene fence panels where a gate must be. Safe to call again (after the track or plots appear).
    /// Small pieces (panels, posts, rails: colliders or bare meshes) in a gate are switched off; a whole fence line on one
    /// long box collider is cut into boxes that leave the gate open.
    /// </summary>
    public static int OpenGates()
    {
        int n = 0;
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (c == null || !c.enabled || c.isTrigger || !InFence(c.transform)) continue;
            var b = c.bounds;
            float span = Mathf.Max(b.size.x, b.size.z);
            if (span > 8f)
            {
                if (c is BoxCollider box && SplitLine(box)) n++;
                continue;
            }
            if (!InGate(b.center, span * 0.5f)) continue;
            c.gameObject.SetActive(false);
            OpenedPanels.Add(c.gameObject);
            n++;
        }
        // bare fence meshes (posts, rails) standing in a gate: hide them too (nothing to see or snag on in the opening)
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r == null || !r.gameObject.activeInHierarchy || !InFence(r.transform)) continue;
            if (r.GetComponentInParent<Collider>() != null && r.GetComponentInParent<Collider>().enabled) continue;   // handled with its collider
            var b = r.bounds;
            float span = Mathf.Max(b.size.x, b.size.z);
            if (span > 8f || !InGate(b.center, span * 0.5f)) continue;
            r.gameObject.SetActive(false);
            OpenedPanels.Add(r.gameObject);
            n++;
        }
        GatesOpened += n;
        return n;
    }

    /// <summary>Replace a long fence box by pieces that skip every gate along it. True when a gap was cut.</summary>
    public static bool SplitLine(BoxCollider box)
    {
        var t = box.transform;
        Vector3 size = Vector3.Scale(box.size, Abs(t.lossyScale));
        bool alongX = size.x >= size.z;
        float len = alongX ? size.x : size.z;
        Vector3 axis = t.rotation * (alongX ? Vector3.right : Vector3.forward);
        Vector3 c = t.TransformPoint(box.center);
        float thick = alongX ? size.z : size.x;
        const float step = 0.5f;
        int n = Mathf.Max(2, Mathf.CeilToInt(len / step));
        var open = new bool[n];
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = c + axis * (-len * 0.5f + (i + 0.5f) * len / n);
            open[i] = InGate(p, Mathf.Max(thick, 0.3f) * 0.5f);
            any |= open[i];
        }
        if (!any) return false;
        box.enabled = false;
        int start = -1;
        for (int i = 0; i <= n; i++)
        {
            bool solid = i < n && !open[i];
            if (solid && start < 0) start = i;
            if (!solid && start >= 0)
            {
                float a = -len * 0.5f + start * len / n, b = -len * 0.5f + i * len / n;
                var piece = new GameObject(t.name + " piece");
                piece.layer = t.gameObject.layer;
                piece.transform.SetParent(t, false);
                piece.transform.position = c + axis * ((a + b) * 0.5f);
                piece.transform.rotation = t.rotation;
                var s = Vector3.Scale(t.lossyScale, Vector3.one);
                piece.transform.localScale = new Vector3(1f / Safe(s.x), 1f / Safe(s.y), 1f / Safe(s.z));
                var bc = piece.AddComponent<BoxCollider>();
                bc.size = alongX ? new Vector3(b - a, size.y, size.z) : new Vector3(size.x, size.y, b - a);
                start = -1;
            }
        }
        OpenedPanels.Add(box.gameObject);
        LinesSplit++;
        return true;
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    private static float Safe(float v) => Mathf.Abs(v) < 1e-4f ? 1f : v;

    /// <summary>Is a fence piece at p (half size r) on a pad, plot, the track, the pen or where a vehicle is parked?</summary>
    public static bool InGate(Vector3 p, float r)
    {
        p.y = 0f;
        foreach (var z in Object.FindObjectsByType<DeliveryZone>(FindObjectsSortMode.None))
            if (Flat(z.transform.position - p).magnitude < z.radius + GateMargin + r) return true;
        foreach (var v in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
            if (Flat(v.transform.position - p).magnitude < v.BodyHalfExtents.z + 1.5f + r) return true;
        if (FarmFields.Instance != null)
            foreach (var f in FarmFields.Instance.Fields) if (f.Contains(p, 1.5f + r)) return true;
        var pen = AnimalPen.Instance;
        if (pen != null && InRect(pen.transform, pen.size, p, 1f + r)) return true;
        var dressing = DesertDressing.Instance;
        if (dressing != null && dressing.Track.Count > 1 && dressing.DistanceToTrack(p) < 3.5f + r) return true;
        if (OpenWorld.Instance != null && OpenWorld.Instance.RoadNear(p, 1.5f + r)) return true;   // open world: the farm road and world roads
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

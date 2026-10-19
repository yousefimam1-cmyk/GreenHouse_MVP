using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A town car (World/WorldTown): the game's pickup model driving a loop of the town's streets in its right-hand lane,
/// slowing for corners and stopping for the player (on foot or driving) and for pedestrians in front of it. It has no
/// collider (it never shoves the player or blocks a gate); parked cars are separate solid props.
/// </summary>
public class TownCar : MonoBehaviour
{
    public readonly List<Vector3> loop = new List<Vector3>();
    public float cruise = 7f;
    public float Speed { get; private set; }
    public float Travelled { get; private set; }
    public bool Yielding { get; private set; }
    private int next;

    public static TownCar Create(Transform parent, List<Vector3> pts, float phase, float speed, int index)
    {
        if (pts == null || pts.Count < 2) return null;
        var go = new GameObject("Town car " + index);
        go.transform.SetParent(parent, false);
        // mixed town traffic from the model catalog (sedans, SUVs, vans); falls back to the pickup model
        string id = ModelCatalog.Variant("town_car", index) ?? "Truck";
        var e = ModelCatalog.Find(id);
        if (Build.TryModel(id, go.transform, e != null && e.fit > 0f ? e.fit : 4.5f, 0f) == null) { Destroy(go); return null; }
        var car = go.AddComponent<TownCar>();
        car.loop.AddRange(pts);
        car.cruise = speed;
        // start part-way round the loop
        float total = 0f; for (int i = 0; i < pts.Count; i++) total += Vector3.Distance(pts[i], pts[(i + 1) % pts.Count]);
        float at = Mathf.Repeat(phase, 1f) * total;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 a = pts[i], b = pts[(i + 1) % pts.Count];
            float len = Vector3.Distance(a, b);
            if (at <= len || i == pts.Count - 1)
            {
                go.transform.position = Vector3.Lerp(a, b, Mathf.Clamp01(at / Mathf.Max(0.01f, len)));
                Vector3 d = b - a; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(d.normalized);
                car.next = (i + 1) % pts.Count;
                break;
            }
            at -= len;
        }
        return car;
    }

    /// <summary>Is something the car must stop for within reach ahead (player, the vehicle being driven, a pedestrian)?</summary>
    public bool Blocked()
    {
        Vector3 p = transform.position, f = transform.forward;
        if (Check(Vehicle.Current != null ? Vehicle.Current.transform : null, p, f, 9f)) return true;
        var anchor = Minimap.Anchor();
        if (Check(anchor, p, f, 7f)) return true;
        foreach (var w in TownWalker.All) if (w != null && Check(w.transform, p, f, 5f)) return true;
        return false;
    }

    private static bool Check(Transform t, Vector3 p, Vector3 f, float reach)
    {
        if (t == null) return false;
        Vector3 d = t.position - p; d.y = 0f;
        float ahead = Vector3.Dot(d, f);
        if (ahead < 0.5f || ahead > reach) return false;
        return (d - f * ahead).magnitude < 1.8f;
    }

    private void Update()
    {
        if (loop.Count < 2) return;
        float dt = Time.deltaTime;
        Vector3 goal = loop[next];
        Vector3 to = goal - transform.position; to.y = 0f;
        float dist = to.magnitude;
        Yielding = Blocked();
        float want = Yielding ? 0f : dist < 8f ? Mathf.Min(cruise, 3.5f) : cruise;   // slow for the corner
        Speed = Mathf.MoveTowards(Speed, want, (Yielding ? 9f : 3f) * dt);
        if (dist < 0.6f) { next = (next + 1) % loop.Count; return; }
        Vector3 dir = to / dist;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 70f * dt);
        float step = Mathf.Min(Speed * dt, dist);
        Vector3 fwd = transform.forward; fwd.y = 0f;
        Vector3 move = Vector3.Lerp(fwd.normalized, dir, 0.6f).normalized * step;
        Vector3 pos = transform.position + move;
        pos.y = goal.y;
        transform.position = pos;
        Travelled += step;
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Euro-Truck-Simulator-style GPS route to the map waypoint (WorldMap): the shortest way by road over the open world's
/// road graph (WorldLayout nodes and roads, Dijkstra), joining the road network at the nearest road point and leaving
/// it at the one nearest the waypoint (off-road legs are straight). Drawn as an orange line hovering over the roads in
/// the world, as dots on the minimap and the full map (Minimap), and as a HUD status line with the distance by road.
/// Recomputed when the waypoint changes or you have moved; hidden once you arrive. Without the open world (or when the
/// waypoint is closer than the roads) it is a straight line.
/// </summary>
[MazraaSystem(51)]
public class GpsRoute : MonoBehaviour
{
    public static GpsRoute Instance { get; private set; }

    public const float RecomputeMove = 10f, LineLift = 0.45f, LineWidth = 0.7f;

    private readonly List<Vector3> points = new List<Vector3>();
    public IReadOnlyList<Vector3> Points => points;
    public bool HasRoute => points.Count >= 2;
    public float RouteLength { get; private set; }
    /// <summary>Road segments used by the current route (0 = straight line).</summary>
    public int RoadsUsed { get; private set; }
    public int Recomputes { get; private set; }
    public LineRenderer Line => line;

    private LineRenderer line;
    private Vector3? lastTarget;
    private Vector3 lastFrom;
    private float timer;

    private void Awake() => Instance = this;

    private void Start()
    {
        HudLines.Status.Add(HudLine);
        OpenWorld.Built += Invalidate;
        var go = new GameObject("GPS route");
        go.transform.SetParent(transform, false);
        line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = LineWidth;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = Build.Mat(new Color(1f, 0.55f, 0.1f), 0.2f, 0f, new Color(1f, 0.45f, 0.05f) * 1.6f);
        line.positionCount = 0;
        go.SetActive(false);
    }

    private void OnDestroy()
    {
        HudLines.Status.Remove(HudLine);
        OpenWorld.Built -= Invalidate;
        if (Instance == this) Instance = null;
    }

    public void Invalidate() { lastTarget = null; timer = 0f; }

    private void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = 0.5f;
        var wm = WorldMap.Instance;
        if (wm == null || !wm.Waypoint.HasValue) { Clear(); return; }
        var anchor = Minimap.Anchor();
        if (anchor == null) { Clear(); return; }
        Vector3 target = wm.Waypoint.Value, from = anchor.position;
        if (Flat(target - from).magnitude < WorldMap.ReachedRadius) { Clear(); return; }
        bool changed = !lastTarget.HasValue || Flat(lastTarget.Value - target).sqrMagnitude > 0.01f || Flat(from - lastFrom).magnitude > RecomputeMove;
        if (changed) Compute(from, target);
    }

    private void Clear()
    {
        if (points.Count == 0 && lastTarget == null) return;
        points.Clear(); RouteLength = 0f; RoadsUsed = 0; lastTarget = null;
        if (line != null) { line.positionCount = 0; line.gameObject.SetActive(false); }
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private string HudLine()
    {
        if (!HasRoute) return null;
        return "<color=#FFA040>" + Loc.Display(Loc.Raw("GPS") + ": " + WorldMap.Metres(RouteLength) + " " + Loc.Raw(RoadsUsed > 0 ? "by road" : "straight")) + "</color>";
    }

    // ------------------------------------------------------------------ routing

    private struct RoadPos { public int road, idx; public float dist; }

    /// <summary>Build the route from a position to a target now (also used by tests).</summary>
    public void Compute(Vector3 from, Vector3 target)
    {
        lastTarget = target; lastFrom = from;
        Recomputes++;
        points.Clear(); RoadsUsed = 0;
        var w = OpenWorld.Instance;
        var L = w != null ? w.Layout : null;
        if (L != null && L.Roads.Count > 0 && L.Nodes.Count > 0) RouteByRoad(L, from, target);
        if (points.Count < 2) { points.Clear(); points.Add(from); points.Add(target); RoadsUsed = 0; }
        RouteLength = 0f;
        for (int i = 1; i < points.Count; i++) RouteLength += Flat(points[i] - points[i - 1]).magnitude;
        DrawLine(w);
    }

    private static RoadPos Nearest(WorldLayout L, Vector3 p)
    {
        var best = new RoadPos { road = -1, dist = float.MaxValue };
        for (int r = 0; r < L.Roads.Count; r++)
        {
            var path = L.Roads[r].path;
            for (int i = 0; i < path.Count; i++)
            {
                float d = Flat(path[i] - p).sqrMagnitude;
                if (d < best.dist) { best.dist = d; best.road = r; best.idx = i; }
            }
        }
        best.dist = Mathf.Sqrt(best.dist);
        return best;
    }

    private static float Along(List<Vector3> path, int i0, int i1)
    {
        float s = 0f; int step = i1 >= i0 ? 1 : -1;
        for (int i = i0; i != i1; i += step) s += Flat(path[i + step] - path[i]).magnitude;
        return s;
    }

    private void RouteByRoad(WorldLayout L, Vector3 from, Vector3 target)
    {
        var s = Nearest(L, from); var t = Nearest(L, target);
        if (s.road < 0 || t.road < 0) return;
        float straight = Flat(target - from).magnitude;
        if (straight <= s.dist + t.dist) return;   // closer than the roads: straight line
        var sr = L.Roads[s.road]; var tr = L.Roads[t.road];
        int last = sr.path.Count - 1;
        // Dijkstra from a virtual source on the start road's point to both of its end nodes.
        var dist = new Dictionary<int, float>(); var prev = new Dictionary<int, int>(); var via = new Dictionary<int, int>();
        var open = new List<int>();
        System.Action<int, float, int, int> relax = (node, d, from_, road) =>
        {
            if (dist.TryGetValue(node, out var cur) && cur <= d) return;
            dist[node] = d; prev[node] = from_; via[node] = road;
            if (!open.Contains(node)) open.Add(node);
        };
        relax(sr.a, Along(sr.path, s.idx, 0), -1, s.road);
        relax(sr.b, Along(sr.path, s.idx, last), -1, s.road);
        var done = new HashSet<int>();
        while (open.Count > 0)
        {
            int best = open[0];
            foreach (int o in open) if (dist[o] < dist[best]) best = o;
            open.Remove(best);
            if (!done.Add(best)) continue;
            foreach (int ri in L.Nodes[best].roads)
            {
                var r = L.Roads[ri];
                int o = r.a == best ? r.b : r.a;
                relax(o, dist[best] + r.Length, best, ri);
            }
        }
        int tLast = tr.path.Count - 1;
        float viaA = dist.TryGetValue(tr.a, out var da) ? da + Along(tr.path, 0, t.idx) : float.MaxValue;
        float viaB = dist.TryGetValue(tr.b, out var db) ? db + Along(tr.path, tLast, t.idx) : float.MaxValue;
        float same = s.road == t.road ? Along(sr.path, s.idx, t.idx) : float.MaxValue;
        float bestCost = Mathf.Min(same, Mathf.Min(viaA, viaB));
        if (bestCost == float.MaxValue) return;
        points.Add(from);
        if (same <= viaA && same <= viaB)
        {
            AddPath(sr.path, s.idx, t.idx);
            RoadsUsed = 1;
        }
        else
        {
            int endNode = viaA <= viaB ? tr.a : tr.b;
            // chain of (node, road into it) back to the source
            var chain = new List<int>();
            for (int n = endNode; n >= 0; n = prev[n]) { chain.Add(n); if (prev[n] < 0) break; }
            chain.Reverse();
            // first leg: start point to the first node along the start road
            int first = chain[0];
            AddPath(sr.path, s.idx, first == sr.a ? 0 : last);
            for (int i = 1; i < chain.Count; i++)
            {
                var r = L.Roads[via[chain[i]]];
                if (r.a == chain[i - 1]) AddPath(r.path, 0, r.path.Count - 1); else AddPath(r.path, r.path.Count - 1, 0);
            }
            AddPath(tr.path, endNode == tr.a ? 0 : tLast, t.idx);
            RoadsUsed = chain.Count + 1;
        }
        points.Add(target);
    }

    private void AddPath(List<Vector3> path, int i0, int i1)
    {
        int step = i1 >= i0 ? 1 : -1;
        for (int i = i0; ; i += step)
        {
            var p = path[i];
            if (points.Count == 0 || Flat(points[points.Count - 1] - p).sqrMagnitude > 0.25f) points.Add(p);
            if (i == i1) break;
        }
    }

    private void DrawLine(OpenWorld w)
    {
        if (line == null) return;
        var pos = new Vector3[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            float y = w != null ? w.GroundY(p) : Build.GroundY(p);
            pos[i] = new Vector3(p.x, y + LineLift, p.z);
        }
        line.positionCount = pos.Length;
        line.SetPositions(pos);
        line.gameObject.SetActive(pos.Length >= 2);
    }
}

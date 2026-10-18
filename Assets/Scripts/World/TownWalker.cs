using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A town pedestrian (World/WorldTown): walks back and forth along a sidewalk, now and then stops at a shop door on the
/// way (facing it, idling / gesturing), or sits at the cafe. Drives the Actor's clips itself (its own Update is off);
/// stays bumpable by vehicles (NpcBump sees the Actor). Never leaves its sidewalk.
/// </summary>
public class TownWalker : MonoBehaviour
{
    public static readonly List<TownWalker> All = new List<TownWalker>();

    public Actor actor;
    public float speed = 1.2f;
    public bool sitting;
    public float Travelled { get; private set; }
    private readonly List<Vector3> path = new List<Vector3>();
    private readonly List<WorldTown.Spot> stops = new List<WorldTown.Spot>();
    private int target, dir = 1;
    private float wait;
    private WorldTown.Spot? stopAt;
    private System.Random rnd;
    private readonly HashSet<int> visited = new HashSet<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => All.Clear();

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    public void Setup(Actor a, List<Vector3> line, List<WorldTown.Spot> doorStops, float metresPerSecond, int seed)
    {
        actor = a; speed = metresPerSecond; rnd = new System.Random(seed);
        path.Clear(); path.AddRange(line);
        stops.Clear(); stops.AddRange(doorStops);
        if (a != null) a.enabled = false;   // we move it
        target = rnd.Next(2) == 0 ? 0 : path.Count - 1;
        dir = target == 0 ? -1 : 1;
    }

    public void SetupSitting(Actor a)
    {
        actor = a; sitting = true;
        if (a != null) a.enabled = false;
        if (a != null) a.Play("sit", "idle");
    }

    private void Update()
    {
        if (actor == null) return;
        if (sitting) { actor.Play("sit", "idle"); return; }
        if (path.Count < 2) return;
        float dt = Time.deltaTime;
        if (wait > 0f)
        {
            wait -= dt;
            if (stopAt.HasValue) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, stopAt.Value.yaw, 0f), 240f * dt);
            actor.Play(wait > 2.5f && stopAt.HasValue ? "interact-right" : "idle", "idle");
            return;
        }
        stopAt = null;
        Vector3 goal = path[target];
        // a shop door close to where we are, not visited on this leg: stop there a moment
        for (int i = 0; i < stops.Count; i++)
        {
            if (visited.Contains(i)) continue;
            Vector3 d = stops[i].p - transform.position; d.y = 0f;
            if (d.magnitude < 1.4f && rnd.Next(3) == 0) { visited.Add(i); stopAt = stops[i]; wait = 3f + (float)rnd.NextDouble() * 5f; return; }
            if (d.magnitude < 1.4f) visited.Add(i);
        }
        Vector3 to = goal - transform.position; to.y = 0f;
        if (to.magnitude < 0.3f)
        {
            target = target == 0 ? path.Count - 1 : 0;   // turn round at the end of the sidewalk
            visited.Clear();
            wait = 1f + (float)rnd.NextDouble() * 3f;
            return;
        }
        Vector3 step = to.normalized * Mathf.Min(speed * dt, to.magnitude);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to.normalized), 240f * dt);
        Vector3 p = transform.position + step;
        p.y = Mathf.Lerp(path[0].y, path[path.Count - 1].y, 0.5f);
        transform.position = p;
        Travelled += step.magnitude;
        actor.Play("walking", "walk", "run");
    }
}

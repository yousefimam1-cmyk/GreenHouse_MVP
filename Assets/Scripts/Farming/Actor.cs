using System.Collections.Generic;
using UnityEngine;

public enum ActorMode { Wander, FollowPlayer, Fly, Stand, Driver, Worker, Remote }   // Remote: moved by RemotePlayer (co-op)

/// <summary>
/// Animated ambient life (CC0 models with legacy animation clips): wandering animals and people, a dog that
/// follows you, birds circling, shopkeepers, field workers, and a driver that appears in the cab while you drive.
/// Clips are picked by keyword (walk, run, idle, fly, drive, pick-up, emote-yes), so any similar model works.
/// </summary>
public class Actor : MonoBehaviour
{
    public ActorMode mode;
    public Vector3 home;
    public float radius = 8f, speed = 1.2f, flyHeight = 25f;
    public Vehicle vehicle;            // Driver mode

    private Animation anim;
    private readonly Dictionary<string, string> clips = new Dictionary<string, string>();
    private string playing;
    private Vector3 target;
    private float wait, angle;
    private GameObject model;

    public static Actor Spawn(string modelName, float size, bool byHeight, ActorMode mode, Vector3 pos, Transform parent = null)
    {
        var go = new GameObject("Actor " + modelName);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var m = byHeight ? Build.TryModelHeight(modelName, go.transform, size) : Build.TryModel(modelName, go.transform, size, 0f);
        if (m == null) { Destroy(go); return null; }   // model not in Resources: skip quietly
        var a = go.AddComponent<Actor>();
        a.model = m;
        a.mode = mode;
        a.home = pos;
        a.SetupClips();
        return a;
    }

    private void SetupClips()
    {
        anim = model.GetComponentInChildren<Animation>();
        if (anim == null) return;
        foreach (AnimationState s in anim)
        {
            s.wrapMode = WrapMode.Loop;
            string n = s.name.ToLowerInvariant();
            foreach (var key in new[] { "walking", "walk", "run", "sprint", "idle", "fly", "drive", "pick-up", "interact-right", "emote-yes", "sit" })
                if (n.Contains(key) && !clips.ContainsKey(key) && !(key == "walk" && n.Contains("walkslow"))) clips[key] = s.name;
        }
    }

    private string Clip(params string[] keys)
    {
        foreach (var k in keys) if (clips.TryGetValue(k, out var c)) return c;
        return null;
    }

    public void Play(params string[] keys)
    {
        string c = Clip(keys);
        if (anim == null || c == null || c == playing) return;
        anim.CrossFade(c, BlendTime);
        playing = c;
    }

    // ---- graphics makeover: smoother blends, feet that match the ground speed, idle variety
    /// <summary>Cross-fade time between loops (longer = smoother, 0.35 s reads well for walk / idle).</summary>
    public const float BlendTime = 0.35f;
    private float idleVariety = -1f;
    /// <summary>One-off idle gestures played now and then while standing (picked from the clips the model has).</summary>
    private static readonly string[] IdleGestures = { "interact-right", "emote-yes", "pick-up" };

    /// <summary>Walk / run clips play at a speed matching how fast the actor really moves (no foot sliding).</summary>
    private void SyncSpeed(float metresPerSecond)
    {
        if (anim == null || playing == null) return;
        var st = anim[playing];
        if (st == null) return;
        bool run = playing.ToLowerInvariant().Contains("run") || playing.ToLowerInvariant().Contains("sprint");
        float nominal = (run ? 3.2f : 1.25f) * Mathf.Max(0.3f, transform.lossyScale.y);
        st.speed = Mathf.Clamp(metresPerSecond / nominal, 0.6f, 1.8f);
    }

    /// <summary>While idling, now and then a short gesture (look busy) instead of a frozen loop.</summary>
    private void IdleVariety()
    {
        if (idleVariety < 0f) idleVariety = Random.Range(6f, 14f);
        idleVariety -= Time.deltaTime;
        if (idleVariety > 0f) return;
        idleVariety = Random.Range(8f, 16f);
        string g = IdleGestures[Random.Range(0, IdleGestures.Length)];
        if (Clip(g) != null) React(g, 1.6f);
    }

    /// <summary>One-off reaction (e.g. shopkeeper nods after a sale), then back to the loop.</summary>
    public void React(string key, float seconds = 1.5f)
    {
        string c = Clip(key);
        if (anim == null || c == null) return;
        anim.CrossFade(c, 0.15f);
        playing = c;
        wait = seconds;
    }

    private void Start() { target = home; wait = Random.Range(0f, 3f); angle = Random.Range(0f, 360f); }

    private void Update()
    {
        switch (mode)
        {
            case ActorMode.Wander: case ActorMode.Worker: Wander(); break;
            case ActorMode.FollowPlayer: Follow(); break;
            case ActorMode.Fly: Fly(); break;
            case ActorMode.Stand: if ((wait -= Time.deltaTime) <= 0f) { Play("idle"); IdleVariety(); } break;
            case ActorMode.Driver:
                bool show = vehicle != null && (vehicle.IsDriving || vehicle.RemoteDriven);
                if (model.activeSelf != show) model.SetActive(show);
                if (show) Play("drive", "sit", "idle");
                break;
        }
    }

    private void Wander()
    {
        Vector3 to = target - transform.position; to.y = 0f;
        if (wait > 0f)
        {
            wait -= Time.deltaTime;
            if (mode == ActorMode.Worker && wait > 1f) Play("pick-up", "interact-right", "idle"); else Play("idle");
            return;
        }
        if (to.magnitude < 0.4f)
        {
            Vector2 r = Random.insideUnitCircle * radius;
            target = home + new Vector3(r.x, 0f, r.y);
            wait = mode == ActorMode.Worker ? Random.Range(3f, 6f) : Random.Range(1.5f, 5f);
            return;
        }
        MoveTowards(to, speed);
        Play("walking", "walk", "run");
        SyncSpeed(speed);
    }

    private void Follow()
    {
        Transform leader = Vehicle.Current != null ? Vehicle.Current.transform : PlayerTransform();
        if (leader == null) return;
        Vector3 to = leader.position - transform.position; to.y = 0f;
        float d = to.magnitude;
        if (d > 45f) { transform.position = Build.OnGround(leader.position - leader.forward * 3f); return; } // catch up after driving away
        if (d > 3.5f)
        {
            float s = d > 9f ? speed * 3f : speed * 1.6f;
            MoveTowards(to, s);
            Play(d > 9f ? "run" : "walking", "walking", "walk", "run");
            SyncSpeed(s);
        }
        else Play("idle", "sit");
    }

    private static Transform cachedPlayer;
    private static Transform PlayerTransform()
    {
        if (cachedPlayer == null)
        {
            var p = FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
            if (p != null) cachedPlayer = p.transform;
        }
        return cachedPlayer;
    }

    private void Fly()
    {
        angle += speed * 6f * Time.deltaTime;
        float rad = angle * Mathf.Deg2Rad;
        Vector3 p = home + new Vector3(Mathf.Cos(rad) * radius, flyHeight + Mathf.Sin(rad * 2f) * 2f, Mathf.Sin(rad) * radius);
        Vector3 dir = p - transform.position;
        transform.position = p;
        if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(dir.normalized);
        Play("fly", "idle");
    }

    private void MoveTowards(Vector3 to, float s)
    {
        Quaternion look = Quaternion.LookRotation(to.normalized);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 240f * Time.deltaTime);
        transform.position = Build.OnGround(transform.position + transform.forward * s * Time.deltaTime);
    }
}

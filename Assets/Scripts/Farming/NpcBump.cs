using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bumping into people and animals, kept light and family friendly (no injuries, nobody is hurt): a vehicle that drives
/// into a villager, worker, shopkeeper or animal (or the player sprinting into them) knocks them back. People stagger, or
/// at vehicle speed tumble over and get back up (BumpReaction, no ragdoll), and say something cross or surprised in a
/// speech bubble (EN/AR); animals hop aside and run off, and animals nearby are startled and run too. Villagers like you
/// a little less (Friendship), and hitting a person faster than about 22 km/h costs a small fine (Ledger "fines"). The
/// vehicle loses speed. Everyone goes back to what they were doing afterwards.
/// People and animals are found by a cheap scan (once a second) and tested against the vehicle's body box each physics step.
/// </summary>
[MazraaSystem(82)]
public class NpcBump : MonoBehaviour
{
    public static NpcBump Instance { get; private set; }

    public const float MinVehicleSpeed = 1.2f;    // m/s: slower than this the vehicle just stops at them (no bump)
    public const float RunSpeed = 4.2f;           // m/s: the player sprinting faster than this bumps people
    public const float FineSpeed = 6f;            // m/s (~22 km/h)
    public const int BaseFine = 50, MaxFine = 150;
    public const float VehicleFriendLoss = 15f, RunFriendLoss = 5f;
    public const float StartleRadius = 7f;

    /// <summary>English keys of the speech bubbles (translated in Loc/97_bump.json).</summary>
    public static readonly string[] VehicleLines = { "Hey! Watch where you're driving!", "Slow down, please!", "Careful with that truck!", "Whoa! I nearly spilled my tea!" };
    public static readonly string[] RunLines = { "Oops! Easy there!", "Hey, no running on the farm!", "Watch your step, friend!" };

    public int Bumps { get; private set; }
    public int FinesTotal { get; private set; }
    public int LastFine { get; private set; }
    public float LastFriendLoss { get; private set; }
    public string LastLine { get; private set; } = "";
    public GameObject LastBumped { get; private set; }

    private readonly List<Transform> targets = new List<Transform>();
    private readonly List<float> radii = new List<float>();   // cached with the scan: no per-step allocations
    private readonly HashSet<Transform> seen = new HashSet<Transform>();
    private float scanTimer;
    private Transform player;
    private Vector3 lastPlayer, playerDir = Vector3.forward;
    private bool hasLast;
    private int lineIndex;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if ((scanTimer -= Time.deltaTime) <= 0f) Rescan();
        CheckPlayer(Time.deltaTime);
    }

    // ------------------------------------------------------------------ who can be bumped
    public static bool Bumpable(Actor a) => a != null && (a.mode == ActorMode.Wander || a.mode == ActorMode.Worker || a.mode == ActorMode.Stand || a.mode == ActorMode.FollowPlayer);

    private static bool IsVillagerObject(GameObject go) => go.name.StartsWith("Villager ") || go.name == "Visitor";

    /// <summary>Is this collider part of a person or animal (bumped, never a wall)?</summary>
    public static bool IsBumpable(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            var a = t.GetComponent<Actor>();
            if (a != null) return Bumpable(a);
            if (t.GetComponent<FarmAnimal>() != null || IsVillagerObject(t.gameObject)) return true;
            if (t.GetComponent<Vehicle>() != null) return false;
        }
        return false;
    }

    public static bool IsAnimal(GameObject go)
    {
        if (go.GetComponent<FarmAnimal>() != null) return true;
        var a = go.GetComponent<Actor>();
        return a != null && a.IsAnimal && !IsVillagerObject(go);
    }

    /// <summary>Rough body radius (m) for the overlap test.</summary>
    public static float Radius(GameObject go)
    {
        var fa = go.GetComponent<FarmAnimal>();
        if (fa != null) return fa.animalId == "cow" || fa.animalId == "camel" ? 0.8f : fa.animalId == "chicken" ? 0.25f : 0.5f;
        if (IsAnimal(go))
        {
            string n = go.name.ToLowerInvariant();
            return n.Contains("horse") ? 0.8f : n.Contains("cat") ? 0.25f : 0.4f;
        }
        return 0.5f;   // people (their E-talk box is 0.9 m wide)
    }

    /// <summary>Refresh the list of people and animals (cheap: once a second).</summary>
    public void Rescan()
    {
        scanTimer = 1f;
        targets.Clear(); radii.Clear(); seen.Clear();
        foreach (var a in FindObjectsByType<Actor>(FindObjectsSortMode.None)) if (Bumpable(a)) Add(a.transform);
        foreach (var f in FindObjectsByType<FarmAnimal>(FindObjectsSortMode.None)) Add(f.transform);
        if (Friendship.Instance != null) foreach (var kv in Friendship.Instance.Npcs) if (kv.Value != null) Add(kv.Value.transform);
        if (Villagers.Instance != null) foreach (var kv in Villagers.Instance.Npcs) if (kv.Value != null) Add(kv.Value.transform);
    }

    private void Add(Transform t) { if (t != null && seen.Add(t)) { targets.Add(t); radii.Add(Radius(t.gameObject)); } }

    public IReadOnlyList<Transform> Targets => targets;

    // ------------------------------------------------------------------ detection
    /// <summary>Called by a moving vehicle every physics step: bump anyone its body box runs into.</summary>
    public void CheckVehicle(Vehicle v)
    {
        if (v == null) return;
        if (targets.Count == 0 && scanTimer <= 0f) Rescan();
        Vector3 he = v.BodyHalfExtents, c = v.BodyCenter;
        float sign = Mathf.Sign(v.Speed);
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            Vector3 local = v.transform.InverseTransformPoint(t.position) - c;
            float r = radii[i];
            if (Mathf.Abs(local.x) > he.x + r || Mathf.Abs(local.z) > he.z + r + 0.1f) continue;
            if (local.y < -he.y - 1.5f || local.y > he.y + 1f) continue;      // birds overhead, someone on a roof
            if (local.z * sign < -he.z * 0.5f) continue;                      // only what the vehicle drives into
            var react = t.GetComponent<BumpReaction>();
            if (react != null && react.Busy && !react.Fleeing) { v.SlowFromBump(0.85f); continue; }   // still getting up: ease off
            // shoved mostly sideways, out of the vehicle's path
            Vector3 push = v.transform.forward * sign * 0.5f + v.transform.right * (local.x >= 0f ? 1f : -1f);
            Hit(t.gameObject, push, Mathf.Abs(v.Speed), v);
        }
    }

    private void CheckPlayer(float dt)
    {
        if (player == null)
        {
            var inputs = FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
            if (inputs != null) player = inputs.transform;
        }
        if (player == null || !player.gameObject.activeInHierarchy || Vehicle.Current != null || dt <= 0f) { hasLast = false; return; }
        Vector3 p = player.position;
        Vector3 d = p - lastPlayer; d.y = 0f;
        bool had = hasLast;
        lastPlayer = p; hasLast = true;
        if (!had) return;
        float speed = d.magnitude / dt;
        if (speed < RunSpeed || speed > 12f) return;                          // walking, or a teleport
        playerDir = d / d.magnitude;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            Vector3 to = t.position - p; to.y = 0f;
            if (to.magnitude > radii[i] + 0.45f || Vector3.Dot(to, playerDir) < 0f) continue;
            Hit(t.gameObject, playerDir, speed, null);
        }
    }

    // ------------------------------------------------------------------ the bump
    /// <summary>
    /// Bump go along dir at speed (m/s). by = the vehicle, or null for the player on foot. Returns false if it is still
    /// recovering from the last bump.
    /// </summary>
    public bool Hit(GameObject go, Vector3 dir, float speed, Vehicle by)
    {
        if (go == null) return false;
        var react = go.GetComponent<BumpReaction>();
        if (react == null) react = go.AddComponent<BumpReaction>();
        if (react.Cooldown > 0f || (react.Busy && !react.Fleeing)) return false;
        bool animal = IsAnimal(go);
        bool vehicle = by != null;
        float knock = vehicle ? Mathf.Clamp(0.6f + speed * 0.22f, 0.6f, 3f) : 0.6f;
        react.Knock(dir, knock, !animal && vehicle && speed > 2.5f, animal);
        if (vehicle) by.SlowFromBump(animal && Radius(go) < 0.45f ? 0.85f : 0.6f);
        Bumps++;
        LastBumped = go;

        // speech bubble (people) or a startled "!" (animals); it stays where it was said while they tumble
        float head = animal ? 1.2f + Radius(go) : 2.3f;
        if (animal) LastLine = "!";
        else
        {
            var lines = vehicle ? VehicleLines : RunLines;
            LastLine = lines[lineIndex++ % lines.Length];
        }
        var bubble = Build.Label(null, go.transform.position + new Vector3(0f, head, 0f), LastLine, animal ? 4f : 2.4f, animal ? new Color(1f, 0.85f, 0.3f) : Color.white);
        if (bubble != null) Destroy(bubble.gameObject, 2.6f);
        Startle(go.transform.position, go);

        // friendship and fine
        string msg = "";
        LastFriendLoss = 0f;
        string vid = VillagerId(go);
        if (vid != null && Friendship.Instance != null)
        {
            LastFriendLoss = Friendship.Instance.Bumped(vid, vehicle ? VehicleFriendLoss : RunFriendLoss);
            if (LastFriendLoss > 0f) msg = string.Format(Loc.Raw("{0} is upset with you"), Friendship.ShortName(Friendship.Def(vid)));
        }
        LastFine = 0;
        var eco = EconomyManager.Instance;
        if (vehicle && !animal && speed > FineSpeed && eco != null)
        {
            int fine = Mathf.Clamp(BaseFine + Mathf.RoundToInt((speed - FineSpeed) * 10f), BaseFine, MaxFine);
            int before = eco.Money;
            using (Ledger.As(Ledger.Fines)) eco.AddMoney(-fine);
            LastFine = before - eco.Money;
            FinesTotal += LastFine;
            msg = (msg.Length > 0 ? msg + "  " : "") + string.Format(Loc.Raw("Fine for reckless driving: {0} SAR"), fine);
        }
        if (msg.Length > 0 && UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(msg));
        if (vehicle && AmbientAudio.Instance != null) AmbientAudio.Instance.PlayThump(go.transform.position, Mathf.Clamp01(speed / 14f) * 0.6f);
        return true;
    }

    /// <summary>Animals near a bump get a fright and run away from it for a moment.</summary>
    public void Startle(Vector3 at, GameObject except)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || t.gameObject == except || !t.gameObject.activeInHierarchy || !IsAnimal(t.gameObject)) continue;
            Vector3 away = t.position - at; away.y = 0f;
            if (away.magnitude > StartleRadius) continue;
            var react = t.GetComponent<BumpReaction>();
            if (react == null) react = t.gameObject.AddComponent<BumpReaction>();
            react.Flee(away.sqrMagnitude > 1e-4f ? away : t.forward);
        }
    }

    /// <summary>Friendship id of a villager object (new and old villagers), or null.</summary>
    public static string VillagerId(GameObject go)
    {
        if (Friendship.Instance != null) foreach (var kv in Friendship.Instance.Npcs) if (kv.Value == go) return kv.Key;
        if (Villagers.Instance != null) foreach (var kv in Villagers.Instance.Npcs) if (kv.Value == go) return kv.Key;
        return null;
    }
}

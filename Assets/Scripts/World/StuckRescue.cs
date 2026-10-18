using StarterAssets;
using UnityEngine;

/// <summary>
/// Stuck rescue: if the player keeps pushing to move (or the driven vehicle keeps its throttle) for StuckSeconds without
/// getting anywhere, they are lifted to the nearest open spot - preferably a road (world roads, the farm road), else free
/// ground around them (no collider in a standing capsule, gentle slope). The vehicle's R reset stays as it was.
/// Also gives the player's CharacterController enough step height / slope to walk over small bumps and road edges.
/// </summary>
[MazraaSystem(69)]
public class StuckRescue : MonoBehaviour
{
    public static StuckRescue Instance { get; private set; }
    public const float StuckSeconds = 3f, MinProgress = 0.35f, VehicleProgress = 0.6f;
    /// <summary>Character controller minimums: steps up to 35 cm (kerbs, island edges, seams), slopes up to 50 degrees.</summary>
    public const float StepOffset = 0.35f, SlopeLimit = 50f;
    /// <summary>Tests: pretend the move / throttle input is held (no input devices in the headless run).</summary>
    public static bool ForceInput;

    public int PlayerRescues { get; private set; }
    public int VehicleRescues { get; private set; }
    public Vector3 LastRescue { get; private set; }

    private StarterAssetsInputs inputs;
    private CharacterController cc;
    private Vector3 anchor; private float held;
    private Vehicle anchorVehicle;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (Time.timeScale == 0f) { held = 0f; return; }
        if (inputs == null) { inputs = FindAnyObjectByType<StarterAssetsInputs>(); if (inputs != null) cc = inputs.GetComponent<CharacterController>(); TuneController(); }
        var v = Vehicle.Current;
        if (v != null && v.IsDriving) { TickVehicle(v, Time.deltaTime); return; }
        anchorVehicle = null;
        if (inputs == null || !inputs.gameObject.activeInHierarchy) { held = 0f; return; }
        bool ui = UIManager.Instance != null && UIManager.Instance.IsUIOpened;
        Vector2 mv = ForceInput ? new Vector2(0f, 1f) : inputs.move;
        bool pushing = !ui && mv.x * mv.x + mv.y * mv.y > 0.25f;
        if (pushing && AgainstWall(inputs.transform, mv)) pushing = false;   // walking into a wall is not stuck: turn round
        Tick(inputs.transform, pushing, MinProgress, Time.deltaTime, false);
    }

    private void TuneController()
    {
        if (cc == null) return;
        if (cc.stepOffset < StepOffset) cc.stepOffset = Mathf.Min(StepOffset, cc.height * 0.5f);
        if (cc.slopeLimit < SlopeLimit) cc.slopeLimit = SlopeLimit;
    }

    private void TickVehicle(Vehicle v, float dt)
    {
        if (anchorVehicle != v) { anchorVehicle = v; held = 0f; anchor = v.transform.position; }
        // pressed against a fence or a wall: that is not stuck (reverse out), only being wedged on the ground is
        bool pushing = (ForceInput || Mathf.Abs(v.ThrottleInput) > 0.3f) && Time.time - v.LastObstacleTime > 0.5f;
        Tick(v.transform, pushing, VehicleProgress, dt, true);
    }

    private void Tick(Transform t, bool pushing, float progress, float dt, bool vehicle)
    {
        Vector3 moved = t.position - anchor; moved.y = 0f;
        if (!pushing || moved.magnitude > progress) { held = 0f; anchor = t.position; return; }
        held += dt;
        if (held < StuckSeconds) return;
        held = 0f;
        Rescue(t, vehicle);
        anchor = t.position;
    }

    /// <summary>A solid wall right ahead in the walking direction with free room behind (the player can just turn round).</summary>
    public static bool AgainstWall(Transform t, Vector2 move)
    {
        float yaw = t.eulerAngles.y + Mathf.Atan2(move.x, move.y) * Mathf.Rad2Deg;
        Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, p = t.position;
        return Solid(p + dir * 0.6f, 0.3f) && !Solid(p - dir * 0.6f, 0.3f);
    }

    private static bool Solid(Vector3 q, float r)
    {
        const int mask = ~((1 << 8) | (1 << 7) | (1 << 6));
        return Physics.CheckCapsule(q + Vector3.up * (0.4f + r), q + Vector3.up * (1.6f - r), r, mask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>Move the player / the vehicle to the nearest open spot now. Returns where (or the old position).</summary>
    public Vector3 Rescue(Transform t, bool vehicle)
    {
        Vector3 from = t.position;
        Vector3 spot; float yaw = t.eulerAngles.y;
        if (!FindSpot(from, vehicle ? 2.2f : 0.6f, vehicle, out spot, ref yaw)) return from;
        if (vehicle)
        {
            var v = t.GetComponent<Vehicle>();
            if (v != null) v.RescueTo(spot, yaw);
            VehicleRescues++;
            Say(Loc.T("Vehicle stuck - moved back onto the road"));
        }
        else
        {
            if (cc != null) cc.enabled = false;
            t.position = spot + Vector3.up * 0.05f;
            if (cc != null) cc.enabled = true;
            PlayerRescues++;
            Say(Loc.T("You were stuck - moved to open ground"));
        }
        LastRescue = spot;
        return spot;
    }

    /// <summary>Nearest open spot: a road point within 40 m (vehicles: 60 m) that is clear, else a clear spot on rings around p.</summary>
    public static bool FindSpot(Vector3 p, float radius, bool vehicle, out Vector3 spot, ref float yaw)
    {
        spot = p;
        var w = OpenWorld.Instance;
        float best = float.MaxValue; bool found = false;
        if (w != null && w.Layout != null)
        {
            float reach = vehicle ? 60f : 40f;
            foreach (var road in w.Layout.Roads)
                for (int i = 0; i + 1 < road.path.Count; i++)
                {
                    Vector3 a = road.path[i], b = road.path[i + 1];
                    float d = WorldShape.Seg(p, a, b, out float tt);
                    if (d > reach || d < 0.8f || d >= best) continue;   // d < 0.8: that's where we are stuck
                    Vector3 q = Vector3.Lerp(a, b, tt);
                    q = Ground(q);
                    if (!Clear(q, radius)) continue;
                    best = d; spot = q; found = true;
                    Vector3 dir = b - a; dir.y = 0f;
                    if (dir.sqrMagnitude > 0.01f)
                    {
                        float ry = Quaternion.LookRotation(dir.normalized).eulerAngles.y;
                        yaw = Mathf.Abs(Mathf.DeltaAngle(ry, yaw)) <= 90f ? ry : ry + 180f;   // keep roughly the same heading
                    }
                }
        }
        if (found) return true;
        for (float r = 1.5f; r <= 14f; r += 1.5f)
            for (int k = 0; k < 12; k++)
            {
                Vector3 q = p + Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward * r;
                q = Ground(q);
                if (!Clear(q, radius)) continue;
                spot = q;
                return true;
            }
        return false;
    }

    private static Vector3 Ground(Vector3 q)
    {
        var w = OpenWorld.Instance;
        q.y = w != null && w.IsBuilt ? w.TerrainY(q) : Build.GroundY(q);
        return q;
    }

    /// <summary>Room to stand / park at q: no solid collider in a capsule from 0.3 m to 1.9 m above the ground, gentle slope.</summary>
    public static bool Clear(Vector3 q, float radius)
    {
        const int mask = ~((1 << 8) | (1 << 7) | (1 << 6));   // ignore the player and the interactables' layers
        if (Physics.CheckCapsule(q + Vector3.up * (0.3f + radius), q + Vector3.up * Mathf.Max(0.3f + radius, 1.9f - radius), radius, mask, QueryTriggerInteraction.Ignore)) return false;
        var w = OpenWorld.Instance;
        if (w != null && w.IsBuilt)
        {
            float s = Mathf.Max(radius, 1f);
            float hx = w.TerrainY(q + Vector3.right * s) - w.TerrainY(q - Vector3.right * s), hz = w.TerrainY(q + Vector3.forward * s) - w.TerrainY(q - Vector3.forward * s);
            if (Mathf.Sqrt(hx * hx + hz * hz) / (2f * s) > 0.45f) return false;
        }
        return true;
    }

    private static void Say(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }
}

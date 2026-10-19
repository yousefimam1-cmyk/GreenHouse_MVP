using UnityEngine;

public enum CruiseMode { Off, Cruise, Limiter }

/// <summary>
/// Driver assists (plain C#, stepped by Vehicle): cruise control that holds a set speed against load and slopes
/// (PI controller), a speed limiter for field work (you keep the throttle, it never goes past the set speed),
/// and a GPS straight-line lane assist: hold a heading and lateral line, and after a manual headland turn snap
/// to the nearest parallel lane one working width over.
/// </summary>
public class DriveAssist
{
    public CruiseMode Cruise { get; private set; }
    public float CruiseSpeed = 10f / 3.6f;                // m/s
    public const float MinCruise = 2f / 3.6f, MaxCruise = 60f / 3.6f;
    private float integ;

    public bool LaneAssist { get; private set; }
    public float LaneWidth = 6f;
    public float LaneYaw { get; private set; }
    private Vector3 laneOrigin;
    private float laneTarget;                             // lateral offset (along the base line's right) of the active lane
    private bool manual, reversed;

    public void Reset() { Cruise = CruiseMode.Off; integ = 0f; LaneAssist = false; }

    /// <summary>Off -> Cruise (set to the current speed) -> Limiter -> Off.</summary>
    public CruiseMode CycleCruise(float speed)
    {
        if (Cruise == CruiseMode.Off)
        {
            Cruise = CruiseMode.Cruise;
            if (Mathf.Abs(speed) > MinCruise * 2f) CruiseSpeed = Mathf.Clamp(Mathf.Abs(speed), MinCruise, MaxCruise);
        }
        else Cruise = Cruise == CruiseMode.Cruise ? CruiseMode.Limiter : CruiseMode.Off;
        integ = 0f;
        return Cruise;
    }

    public void SetCruise(CruiseMode m, float speed) { Cruise = m; CruiseSpeed = Mathf.Clamp(speed, MinCruise, MaxCruise); integ = 0f; }

    public void AdjustCruise(float kmh) => CruiseSpeed = Mathf.Clamp(CruiseSpeed + kmh / 3.6f, MinCruise, MaxCruise);

    /// <summary>Throttle after cruise / limiter. Braking or pulling back cancels cruise; more throttle overrides it.</summary>
    public float ApplyThrottle(float player, bool brake, float speed, float dt)
    {
        if (Cruise == CruiseMode.Off) return player;
        if (Cruise == CruiseMode.Cruise && (brake || player < -0.1f)) { Cruise = CruiseMode.Off; integ = 0f; return player; }
        float err = CruiseSpeed - speed;
        integ = Mathf.Clamp(integ + err * 0.6f * dt, 0f, 0.8f);
        float auto = Mathf.Clamp(err * 0.9f + integ, -1f, 1f);
        if (Cruise == CruiseMode.Cruise)
        {
            if (err < -1.2f && speed > 0f) return -0.25f;            // running away downhill: brake gently
            return Mathf.Max(player, Mathf.Clamp01(auto));
        }
        return player > 0f ? Mathf.Min(player, Mathf.Clamp01(auto)) : player;   // limiter
    }

    public void ToggleLane(Vector3 pos, float yaw)
    {
        LaneAssist = !LaneAssist;
        if (!LaneAssist) return;
        LaneYaw = yaw; laneOrigin = pos; laneTarget = 0f; manual = false; reversed = false;
    }

    /// <summary>Lateral distance (m) from the active lane, positive = right of it in the base direction.</summary>
    public float LaneOffset(Vector3 pos)
    {
        Vector3 right = Quaternion.Euler(0f, LaneYaw, 0f) * Vector3.right;
        Vector3 d = pos - laneOrigin; d.y = 0f;
        return Vector3.Dot(d, right) - laneTarget;
    }

    /// <summary>Steering (-1..1). Any real stick / key input takes over; letting go near a parallel heading picks up the nearest lane.</summary>
    public float Steer(float player, Vector3 pos, float yaw, float speed)
    {
        if (!LaneAssist) return player;
        if (Mathf.Abs(player) > 0.15f) { manual = true; return player; }
        if (speed < 0.5f) return player;
        if (manual)
        {
            float dFwd = Mathf.Abs(Mathf.DeltaAngle(yaw, LaneYaw)), dBack = Mathf.Abs(Mathf.DeltaAngle(yaw, LaneYaw + 180f));
            if (Mathf.Min(dFwd, dBack) > 35f) return 0f;               // still turning on the headland
            reversed = dBack < dFwd;
            Vector3 right = Quaternion.Euler(0f, LaneYaw, 0f) * Vector3.right;
            Vector3 d = pos - laneOrigin; d.y = 0f;
            laneTarget = Mathf.Round(Vector3.Dot(d, right) / LaneWidth) * LaneWidth;
            manual = false;
        }
        float heading = reversed ? LaneYaw + 180f : LaneYaw;
        float e = LaneOffset(pos) * (reversed ? -1f : 1f);              // + = right of the line in the travel direction
        float desired = heading - Mathf.Clamp(e * 7f, -25f, 25f);
        return Mathf.Clamp(Mathf.DeltaAngle(yaw, desired) * 0.12f, -1f, 1f);
    }
}

/// <summary>What the wheels are on: plots are field, the loading bay / market pads asphalt, the desert track and
/// farmyard compacted dirt, open desert beyond the farm sand.</summary>
public static class DriveSurfaces
{
    /// <summary>Open world (World/OpenWorld): asphalt / dirt roads, sand or fields beyond the scene terrain (null = not there).</summary>
    public static System.Func<Vector3, DriveSurface?> WorldSurface;

    public static DriveSurface At(Vector3 p)
    {
        var world = WorldSurface?.Invoke(p);
        if (world.HasValue) return world.Value;
        if (FarmFields.Instance != null && FarmFields.Instance.OnField(p)) return DriveSurface.Field;
        if (OnPad(DeliveryZone.LoadingBay, p) || OnPad(DeliveryZone.Market, p)) return DriveSurface.Asphalt;
        var dd = DesertDressing.Instance;
        if (dd != null)
        {
            if (dd.DistanceToTrack(Flat(p)) < 3f) return DriveSurface.DirtRoad;
            if (dd.DistanceToFarm(p) > 30f) return DriveSurface.Sand;
        }
        return DriveSurface.DirtRoad;
    }

    private static bool OnPad(DeliveryZone z, Vector3 p)
    {
        if (z == null) return false;
        Vector3 d = p - z.transform.position; d.y = 0f;
        return d.magnitude < z.radius + 3f;
    }

    private static Vector3 Flat(Vector3 p) { p.y = 0f; return p; }

    public static bool Raining => WorldClock.Instance != null && WorldClock.Instance.Weather == Weather.Rain;

    public static string Name(DriveSurface s, bool wet)
    {
        switch (s)
        {
            case DriveSurface.Asphalt: return Loc.Raw("Asphalt");
            case DriveSurface.Field: return wet ? Loc.Raw("Wet field") : Loc.Raw("Field");
            case DriveSurface.WetField: return Loc.Raw("Wet field");
            case DriveSurface.Sand: return Loc.Raw("Sand");
            default: return Loc.Raw("Dirt road");
        }
    }
}

using UnityEngine;

/// <summary>
/// On-foot movement feel (Farming Simulator 25 style), used by the StarterAssets FirstPersonController:
/// - walk 4.5 m/s, sprint 7.5 m/s (sprint), speed follows stick tilt; load / crouch scale it (PlayerLife);
/// - quick, linear acceleration and braking (no sluggish ramp, no ice-skating), the direction turns quickly while
///   the speed is kept, only a real reversal brakes first; little control in the air;
/// - jump ~0.85 m with a short coyote time and input buffer, firm gravity;
/// - gamepad look with a response curve (fine aim near the centre) and a turn boost when the stick is held at the rim;
///   mouse look is raw (no smoothing, no threshold);
/// - a slight head bob (Settings > Controls > Head bob; never with Reduce motion);
/// - ground snapping distance for slopes and steps.
/// Every step is written as a rate times dt, so 30, 60 or 144 fps give the same motion (tests simulate them).
/// Pure functions only: the headless tests compile this file (the controller itself lives in StarterAssets).
/// </summary>
public static class MoveFeel
{
    public const float WalkSpeed = 4.5f, RunSpeed = 7.5f;
    /// <summary>m/s²: 0 -> walk in 0.11 s, 0 -> run in 0.19 s.</summary>
    public const float Accel = 40f;
    /// <summary>m/s²: run -> stop in 0.15 s.</summary>
    public const float Decel = 50f;
    /// <summary>Degrees per second the walking direction turns towards the input (keeps the speed).</summary>
    public const float TurnRate = 900f;
    /// <summary>Above this angle between velocity and input it is a reversal: brake, then accelerate.</summary>
    public const float ReverseAngle = 110f;
    public const float AirControl = 0.25f;

    public const float JumpHeight = 0.85f, Gravity = -22f, TerminalSpeed = 53f;
    public const float CoyoteTime = 0.12f, JumpBuffer = 0.15f, JumpCooldown = 0.2f;
    /// <summary>Small downward speed kept while grounded (keeps the controller touching the ground).</summary>
    public const float GroundStick = -2f;

    // CharacterController shape (applied at start; the scene's 0.02 m skin caught on small edges)
    public const float StepOffset = 0.35f, SlopeLimit = 50f, SkinWidth = 0.05f;

    public const float CrouchTime = 0.22f;

    /// <summary>Right-stick Look is scaled by 300 in StarterAssets.inputactions (ScaleVector2): divide to get the stick.</summary>
    public const float StickLookScale = 300f;
    public const float LookYawRate = 170f, LookPitchRate = 115f;   // degrees / s at full tilt
    public const float LookExponent = 1.8f;
    public const float BoostAt = 0.9f, BoostDelay = 0.2f, BoostRamp = 0.45f, BoostMax = 1.7f;

    public const float BobWalk = 0.022f, BobRun = 0.04f;

    // ------------------------------------------------------------------ speed

    /// <summary>Wanted ground speed (m/s): walk or run, times PlayerLife's factor, times stick tilt (keys = 1).</summary>
    public static float TargetSpeed(bool sprint, bool sprintAllowed, float speedFactor, float tilt, float walk = WalkSpeed, float run = RunSpeed)
    {
        float m = Mathf.Clamp01(tilt);
        if (m <= 0.001f) return 0f;
        return (sprint && sprintAllowed ? run : walk) * Mathf.Max(0f, speedFactor) * m;
    }

    /// <summary>
    /// One step of the horizontal velocity towards the wanted velocity (both flat, m/s). accelFactor = PlayerLife's
    /// load factor (1 = empty hands). Grounded: fast accel / brake, quick turns; in the air only AirControl of it.
    /// </summary>
    public static Vector3 StepVelocity(Vector3 current, Vector3 target, float dt, float accelFactor, bool grounded)
    {
        current.y = 0f; target.y = 0f;
        if (dt <= 0f) return current;
        float f = Mathf.Clamp(accelFactor, 0.2f, 2f) * (grounded ? 1f : AirControl);
        float cm = current.magnitude, tm = target.magnitude;
        if (grounded && cm > 0.05f && tm > 0.05f && Vector3.Angle(current, target) < ReverseAngle)
        {
            // turn the direction (speed kept), then speed up / slow down along it
            Vector3 dir = RotateFlat(current / cm, target / tm, TurnRate * dt);
            float rate = (tm >= cm ? Accel : Decel) * f;
            return dir * Mathf.MoveTowards(cm, tm, rate * dt);
        }
        // starting, stopping or reversing: straight line in velocity space
        float r = (tm > cm && Vector3.Dot(current, target) >= 0f ? Accel : Decel) * f;
        return MoveTowards(current, target, r * dt);
    }

    /// <summary>After CharacterController.Move: a wall took part of the step, so the velocity is what really happened (slides along walls, no stored push).</summary>
    public static Vector3 AfterCollision(Vector3 wanted, Vector3 actual)
    {
        wanted.y = 0f; actual.y = 0f;
        float am = actual.magnitude;
        if (am >= wanted.magnitude) return wanted;
        return am < 0.01f ? Vector3.zero : actual;
    }

    public static Vector3 MoveTowards(Vector3 a, Vector3 b, float maxDelta)
    {
        Vector3 d = b - a;
        float m = d.magnitude;
        if (m <= maxDelta || m < 1e-6f) return b;
        return a + d / m * maxDelta;
    }

    /// <summary>Rotate unit flat vector from towards unit flat vector to by at most maxDegrees (around Y).</summary>
    public static Vector3 RotateFlat(Vector3 from, Vector3 to, float maxDegrees)
    {
        float a0 = Mathf.Atan2(from.x, from.z), a1 = Mathf.Atan2(to.x, to.z);
        float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg);
        float step = Mathf.Clamp(delta, -maxDegrees, maxDegrees) * Mathf.Deg2Rad;
        float a = a0 + step;
        return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
    }

    // ------------------------------------------------------------------ jump / ground

    public static float JumpVelocity(float height = JumpHeight, float gravity = Gravity) => Mathf.Sqrt(Mathf.Max(0f, height * -2f * gravity));

    /// <summary>One frame of falling / rising: updates the vertical speed and returns the exact vertical move (average speed x dt).</summary>
    public static float Fall(ref float verticalVelocity, float dt, float gravity = Gravity)
    {
        float v0 = verticalVelocity, v1 = Mathf.Max(v0 + gravity * dt, -TerminalSpeed);
        verticalVelocity = v1;
        return (v0 + v1) * 0.5f * dt;
    }

    /// <summary>Horizontal move for one frame from the velocity before and after StepVelocity (average x dt).</summary>
    public static Vector3 StepMove(Vector3 before, Vector3 after, float dt) => (before + after) * (0.5f * dt);

    /// <summary>How far down to look for the ground after a step so walking down a slope or off a step never "bounces".</summary>
    public static float SnapDistance(float horizontalStep, float stepOffset = StepOffset, float slopeLimit = SlopeLimit)
    {
        float tan = (float)System.Math.Tan(Mathf.Clamp(slopeLimit, 0f, 80f) * Mathf.Deg2Rad);
        return Mathf.Max(stepOffset, horizontalStep * tan) + 0.05f;
    }

    // ------------------------------------------------------------------ look

    /// <summary>
    /// Gamepad look rate in degrees / s from the stick (x, y; magnitude 0..1, above 1 when boosted by a setting):
    /// response curve, then a turn boost after the stick is held near the rim. hold = seconds at the rim (kept by the caller).
    /// </summary>
    public static Vector2 GamepadLookRate(float sx, float sy, float dt, ref float hold)
    {
        float m = Mathf.Sqrt(sx * sx + sy * sy);
        if (m < 1e-4f) { hold = 0f; return Vector2.zero; }
        float mc = Mathf.Min(m, 1f);
        float shaped = Mathf.Pow(mc, LookExponent) * Mathf.Max(1f, m);
        if (mc >= BoostAt) hold += dt; else hold = 0f;
        float boost = 1f + (BoostMax - 1f) * Mathf.Clamp01((hold - BoostDelay) / BoostRamp);
        float k = shaped * boost / m;
        return new Vector2(sx * k * LookYawRate, sy * k * LookPitchRate * (Mathf.Abs(sx) > Mathf.Abs(sy) * 2f ? 0.6f : 1f));
    }

    // ------------------------------------------------------------------ head bob

    /// <summary>Head bob on/off: Settings > Controls > Head bob, never with Accessibility > Reduce motion.</summary>
    public static bool BobEnabled => GameSettings.HeadBob && !GameSettings.ReduceMotion;

    /// <summary>Advance the step phase (radians) by the distance walked: one full cycle = two steps.</summary>
    public static float AdvanceBob(float phase, float groundSpeed, float dt)
    {
        float stride = Mathf.Lerp(0.75f, 1.25f, Mathf.InverseLerp(WalkSpeed, RunSpeed, groundSpeed));   // metres per step
        phase += groundSpeed * dt / (stride * 2f) * Mathf.PI * 2f;
        return phase % (Mathf.PI * 4f);
    }

    /// <summary>Camera offset (local x, y) for the step phase at this ground speed (0 when still or in the air).</summary>
    public static Vector2 BobOffset(float phase, float groundSpeed, bool grounded)
    {
        if (!grounded || groundSpeed < 0.2f) return Vector2.zero;
        float amp = Mathf.Lerp(BobWalk * 0.6f, BobRun, Mathf.InverseLerp(0.2f, RunSpeed, groundSpeed));
        return new Vector2(Mathf.Sin(phase) * amp * 0.35f, -Mathf.Abs(Mathf.Sin(phase)) * amp + amp * 0.5f);
    }

    /// <summary>Frame-rate independent ease factor: approach a target by this fraction this frame.</summary>
    public static float Ease(float ratePerSecond, float dt) => 1f - Mathf.Exp(-ratePerSecond * Mathf.Max(0f, dt));
}

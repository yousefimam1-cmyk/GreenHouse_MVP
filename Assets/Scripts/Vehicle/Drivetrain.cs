using UnityEngine;

public enum GearboxMode { Automatic, Manual, CVT }
public enum DriveSurface { Asphalt, DirtRoad, Field, WetField, Sand }

/// <summary>
/// Plain-C# drivetrain model stepped from Vehicle.FixedUpdate (no allocations, a few multiplies per step):
/// diesel engine with a torque curve and governor, automatic / manual / CVT gearbox with reverse and shift gaps,
/// engine braking, key start / stop, fuel by load + RPM (idle burn too), mass (vehicle + cargo / implement + ballast),
/// surface grip and rolling resistance, wheel slip with 2WD / 4WD / diff lock / tyre pressure, implement draft,
/// slopes, and engine / tyre wear that costs power, grip and fuel.
/// Speeds are m/s, masses kg, forces N.
/// </summary>
public class Drivetrain
{
    // ---------------------------------------------------------------- spec (set by the vehicle)
    public float emptyMass = 3000f, refMass = 3000f;      // refMass = the mass at which `acceleration` is the real feel
    public float maxSpeed = 16f, reverseSpeed = 6f, acceleration = 7f, brakeForce = 18f;
    public float fuelPerSecond = 0.35f;                   // full load at rated RPM
    public int gears = 6;
    public float drivenAxleShare = 0.6f;                  // share of the weight on the driven axle in 2WD
    public float idleRpm = 800f, peakTorqueRpm = 1400f, ratedRpm = 2100f, governorRpm = 2300f, redlineRpm = 2600f;

    // ---------------------------------------------------------------- state
    public GearboxMode Mode = GearboxMode.Automatic;
    public bool EngineOn { get; private set; }
    public bool Starting => startTimer > 0f;
    public int Gear { get; private set; } = 1;            // -1 reverse, 1..gears forward
    public float Rpm { get; private set; }
    public float Slip { get; private set; }               // 0..1 wheel slip
    public float Load { get; private set; }               // 0..1 engine load
    public float FuelRate { get; private set; }           // litres / s right now
    public bool Shifting => shiftTimer > 0f;
    public float EngineWear, TyreWear;                    // 0 new .. 1 worn out

    public bool FourWD, DiffLock, LowPressure;
    public float ExtraMass;                               // cargo / implement / ballast
    public float BallastMass;                             // part of ExtraMass sitting on the front axle (helps grip)
    public float DraftForce;                              // implement pulling back while working (N)
    public float WorkSpeedLimit;                          // > 0: implement max working speed
    public DriveSurface Surface = DriveSurface.DirtRoad;
    public bool Wet;                                      // raining (wet field / slick roads)
    public float SlopeSin;                                // + = nose up

    private float startTimer, shiftTimer, cvtTop, sinceUpshift = 10f;
    public const float G = 9.81f, TyreMu = 1.5f;          // arcade tyres: a bit more grip than real rubber
    public const float StartTime = 0.7f, ShiftTime = 0.25f;

    public float Mass => emptyMass + ExtraMass;

    // ---------------------------------------------------------------- engine
    /// <summary>Normalised diesel torque: 0.75 at idle, 1.0 at 1400, 0.85 at rated, 0 at the governor.</summary>
    public float Torque(float rpm)
    {
        if (rpm <= idleRpm) return 0.75f;
        if (rpm <= peakTorqueRpm) return Mathf.Lerp(0.75f, 1f, (rpm - idleRpm) / (peakTorqueRpm - idleRpm));
        if (rpm <= ratedRpm) return Mathf.Lerp(1f, 0.85f, (rpm - peakTorqueRpm) / (ratedRpm - peakTorqueRpm));
        if (rpm <= governorRpm) return Mathf.Lerp(0.85f, 0f, (rpm - ratedRpm) / (governorRpm - ratedRpm));
        return 0f;
    }

    public void StartEngine(bool instant)
    {
        if (EngineOn) return;
        EngineOn = true;
        startTimer = instant ? 0f : StartTime;
        if (instant) Rpm = Mathf.Max(Rpm, idleRpm);
        if (Gear == 0) Gear = 1;
    }

    public void StopEngine() { EngineOn = false; startTimer = 0f; }

    // ---------------------------------------------------------------- gearbox
    public const float GearStep = 1.4f;                   // ratio between neighbouring gears

    /// <summary>Road speed (m/s) at rated RPM in a forward gear (geometric spread, top gear = maxSpeed).</summary>
    public float GearTop(int g)
    {
        if (g < 0) return reverseSpeed;
        g = Mathf.Clamp(g, 1, gears);
        return maxSpeed * Mathf.Pow(GearStep, g - gears);
    }

    private float RatioTop => Mode == GearboxMode.CVT ? cvtTop : GearTop(Gear);

    /// <summary>Lower gears multiply torque at the wheels (1.35 in first .. 0.85 in top).</summary>
    private float GearMul(float top) => Mathf.Lerp(1.35f, 0.85f, Mathf.InverseLerp(GearTop(1), maxSpeed, top));

    public string GearLabel
    {
        get
        {
            if (Gear < 0) return "R";
            if (Mode == GearboxMode.CVT) return "CVT";
            return (Mode == GearboxMode.Manual ? "M" : "A") + Gear;
        }
    }

    public bool ShiftUp() => Shift(+1);
    public bool ShiftDown() => Shift(-1);

    private bool Shift(int d)
    {
        if (Mode != GearboxMode.Manual) return false;
        int g = Gear;
        if (g < 0) g = d > 0 ? 1 : -1;
        else { g += d; if (g < 1) g = 1; if (g > gears) g = gears; }
        if (g == Gear) return false;
        Gear = g; shiftTimer = ShiftTime * 0.6f;
        return true;
    }

    private void AutoShift(float speed, float throttle)
    {
        if (Gear < 0 || Mode != GearboxMode.Automatic) return;
        float rpm = Mathf.Abs(speed) / GearTop(Gear) * ratedRpm;
        // badly wrong gear (e.g. the vehicle was teleported / pushed): pick the right one at once
        if (rpm > governorRpm || (Gear > 1 && rpm < idleRpm * 0.6f))
        {
            int best = 1;
            for (int g = 1; g <= gears; g++) { best = g; if (Mathf.Abs(speed) / GearTop(g) * ratedRpm <= 1900f) break; }
            Gear = best; return;
        }
        if (shiftTimer > 0f) return;
        float up = Mathf.Lerp(1500f, 1980f, throttle), down = Mathf.Lerp(950f, 1250f, throttle);
        float v = Mathf.Abs(speed);
        // under load (hill, heavy trailer, plow) only upshift if the next gear can still pull: no gear hunting
        bool nextPulls = throttle < 0.2f || WheelAccel(Gear + 1, v, throttle) > lastResist + 0.3f;
        bool lugging = throttle > 0.5f && rpm < peakTorqueRpm && WheelAccel(Gear, v, throttle) < lastResist;
        if (rpm > up && Gear < gears && v / GearTop(Gear + 1) * ratedRpm > down + 80f && nextPulls) { Gear++; shiftTimer = ShiftTime; sinceUpshift = 0f; }
        else if ((rpm < down || lugging) && (sinceUpshift > 1f || rpm < idleRpm + 100f) && Gear > 1 && v / GearTop(Gear - 1) * ratedRpm < up - 60f) { Gear--; shiftTimer = ShiftTime * 0.8f; }
    }

    /// <summary>Net push (m/s^2) a forward gear would give at this speed: engine torque x gearing, capped by traction.</summary>
    private float WheelAccel(int g, float v, float throttle)
    {
        float top = GearTop(g);
        float rpm = Mathf.Max(v / top * ratedRpm, idleRpm);
        float a = throttle * acceleration * Torque(rpm) * GearMul(top) * refMass / Mathf.Max(100f, Mass) * (1f - 0.35f * EngineWear);
        return Mathf.Min(a, TractionLimit() * 0.94f);
    }

    private float lastResist;   // rolling + draft + climbing, from the last step (for the automatic's shift logic)

    // ---------------------------------------------------------------- traction
    public static float SurfaceGrip(DriveSurface s, bool wet)
    {
        switch (s)
        {
            case DriveSurface.Asphalt: return wet ? 0.85f : 1f;
            case DriveSurface.Field: return wet ? 0.45f : 0.65f;
            case DriveSurface.WetField: return 0.45f;
            case DriveSurface.Sand: return 0.5f;
            default: return wet ? 0.7f : 0.8f;
        }
    }

    public static float RollingResistance(DriveSurface s, bool wet)
    {
        switch (s)
        {
            case DriveSurface.Asphalt: return 0.015f;
            case DriveSurface.Field: return wet ? 0.12f : 0.08f;
            case DriveSurface.WetField: return 0.12f;
            case DriveSurface.Sand: return 0.1f;
            default: return 0.03f;
        }
    }

    public static bool OffRoad(DriveSurface s) => s == DriveSurface.Field || s == DriveSurface.WetField || s == DriveSurface.Sand;

    /// <summary>Wheel-force limit as acceleration (m/s^2): grip x driven share x diff lock x tyre pressure x wear x ballast.</summary>
    public float TractionLimit()
    {
        bool off = OffRoad(Surface);
        float grip = SurfaceGrip(Surface, Wet);
        float share = FourWD ? 1f : drivenAxleShare;
        float diff = DiffLock ? 1f : grip < 0.7f ? 0.82f : 1f;             // an open diff spins the lighter-loaded wheel
        float tyre = LowPressure ? (off ? 1.18f : 1.02f) : (off ? 0.95f : 1f);
        float ballast = 1f + 0.6f * BallastMass / Mathf.Max(1f, emptyMass);
        return grip * G * TyreMu * share * diff * tyre * ballast * (1f - 0.3f * TyreWear);
    }

    /// <summary>Slip for a demanded wheel acceleration: a little natural slip up to the limit, then spinning.</summary>
    public static float SlipFor(float demand, float limit)
    {
        if (demand <= 0.001f) return 0f;
        float u = demand / Mathf.Max(0.01f, limit);
        return u <= 1f ? 0.06f * u * u : 0.06f + 0.94f * (1f - 1f / u);
    }

    public float TopSpeed
    {
        get
        {
            float tyres = LowPressure ? 0.75f : 1f;                         // low pressure is speed-limited
            float t = maxSpeed * tyres * (1f - 0.15f * EngineWear);
            if (WorkSpeedLimit > 0f) t = Mathf.Min(t, WorkSpeedLimit);
            return t;
        }
    }

    // ---------------------------------------------------------------- step
    /// <summary>
    /// Advance one physics step. throttle -1..1 (negative = brake when moving forward, reverse when stopped),
    /// brake = service brake, driver = someone is in the seat (otherwise the parking brake holds). Returns the new speed.
    /// fuel is reduced by what the engine burned.
    /// </summary>
    public float Step(float speed, float throttle, bool brake, bool driver, float dt, ref float fuel)
    {
        if (startTimer > 0f) { startTimer -= dt; if (startTimer <= 0f) startTimer = 0f; }
        if (shiftTimer > 0f) shiftTimer -= dt;
        sinceUpshift += dt;
        if (fuel <= 0f) { if (EngineOn) StopEngine(); fuel = 0f; }
        bool running = EngineOn && startTimer <= 0f;
        float mass = Mass, massF = refMass / Mathf.Max(100f, mass);
        float absV = Mathf.Abs(speed);
        float sign = speed > 0.05f ? 1f : speed < -0.05f ? -1f : 0f;

        // shuttle: negative throttle at a standstill selects reverse, positive selects forward
        if (running && absV < 0.4f)
        {
            if (throttle < -0.05f && Gear > 0) { Gear = -1; shiftTimer = ShiftTime; }
            else if (throttle > 0.05f && Gear < 0) { Gear = 1; shiftTimer = ShiftTime; }
        }
        float dirGear = Gear < 0 ? -1f : 1f;
        bool opposing = throttle != 0f && sign != 0f && Mathf.Sign(throttle) != sign && absV > 0.3f;
        float brakeAmt = brake ? 1f : opposing ? Mathf.Abs(throttle) : 0f;   // pulling the other way = brake pedal
        float drive = running && brakeAmt <= 0f ? Mathf.Clamp01(throttle * dirGear) : 0f;

        // gear + RPM
        AutoShift(speed, drive);
        if (Mode == GearboxMode.CVT && Gear > 0)
        {
            float targetRpm = Mathf.Lerp(1050f, 1600f, drive);
            cvtTop = Mathf.Clamp(absV * ratedRpm / targetRpm, GearTop(1), maxSpeed);
        }
        float top = RatioTop;
        float wheelRpm = absV / Mathf.Max(0.05f, top) * ratedRpm;
        float targetEngine;
        if (!EngineOn) targetEngine = 0f;
        else if (startTimer > 0f) targetEngine = idleRpm * 0.35f;
        else if (Shifting) targetEngine = Mathf.Max(idleRpm, Mathf.Lerp(Rpm, wheelRpm, 0.5f));
        else targetEngine = Mathf.Max(wheelRpm, Mathf.Lerp(idleRpm, 1300f, drive));   // auto clutch slips below idle
        Rpm = Mathf.MoveTowards(Rpm, Mathf.Min(targetEngine, redlineRpm + 200f), 9000f * dt);

        // engine force at the wheels (as acceleration) -> traction -> slip
        float power = 1f - 0.35f * EngineWear;
        float aDrive = 0f;
        if (drive > 0f) aDrive = drive * acceleration * Torque(Rpm) * GearMul(top) * massF * power * (Mode == GearboxMode.CVT ? 0.97f : 1f) * (Shifting ? 0.5f : 1f);   // powershift: half torque while changing
        float limit = TractionLimit();
        Slip = SlipFor(aDrive, limit);
        float aEff = aDrive * (1f - Slip);

        // resistances (always oppose motion) + engine braking + slope
        float rolling = RollingResistance(Surface, Wet) * G * (LowPressure && !OffRoad(Surface) ? 1.4f : 1f);
        float draft = DraftForce / Mathf.Max(100f, mass);
        float engineBrake = 0f;
        if (running && drive <= 0f && !Shifting && absV > 0.3f) engineBrake = 0.3f + 1.8f * Mathf.Clamp01((Rpm - idleRpm) / (ratedRpm - idleRpm));
        float massBrake = Mathf.Clamp(Mathf.Sqrt(refMass / Mathf.Max(100f, mass)), 0.55f, 1.15f);   // heavier = longer stopping
        float gripBrake = Mathf.Clamp(SurfaceGrip(Surface, Wet) * 1.25f, 0.5f, 1f);

        if (brakeAmt > 0f) speed = Mathf.MoveTowards(speed, 0f, brakeForce * massBrake * gripBrake * brakeAmt * dt);
        else if (!driver) speed = Mathf.MoveTowards(speed, 0f, 6f * dt);       // parking brake
        speed += dirGear * aEff * dt;
        speed = Mathf.MoveTowards(speed, 0f, (rolling + draft + engineBrake) * dt);
        bool hold = absV < 0.3f && drive <= 0f;                                 // hill hold at a standstill
        if (!hold && driver) speed -= G * SlopeSin * dt;

        lastResist = rolling + draft + G * Mathf.Max(0f, SlopeSin * (Gear < 0 ? -1f : 1f));
        float fwdTop = Mathf.Min(TopSpeed, Mode == GearboxMode.CVT || Gear < 0 ? float.MaxValue : GearTop(Gear) * redlineRpm / ratedRpm);
        speed = Mathf.Clamp(speed, -reverseSpeed, fwdTop);

        // fuel by load and RPM (idle burn while running), CVT keeps the engine in its efficient band
        float rpm01 = Mathf.Clamp01((Rpm - idleRpm) / (ratedRpm - idleRpm));
        Load = running ? Mathf.Clamp01(drive * Torque(Rpm) + draft / Mathf.Max(0.5f, acceleration) * 0.5f) : 0f;
        FuelRate = running ? fuelPerSecond * (0.08f + 0.92f * Load * (0.55f + 0.45f * rpm01)) * (Mode == GearboxMode.CVT ? 0.93f : 1f) * (1f + 0.25f * EngineWear) : 0f;
        fuel = Mathf.Max(0f, fuel - FuelRate * dt);

        // wear: hours under load, over-revving, spinning tyres, soft tyres on hard roads
        if (running) EngineWear += dt * (1f / 10800f) * (0.25f + Load);
        if (Rpm > governorRpm + 60f) EngineWear += dt * 0.01f;
        float metres = Mathf.Abs(speed) * dt;
        TyreWear += metres * (1f / 150000f) * (1f + 6f * Slip) * (LowPressure && !OffRoad(Surface) ? 1.6f : 1f);
        EngineWear = Mathf.Clamp01(EngineWear); TyreWear = Mathf.Clamp01(TyreWear);
        return speed;
    }

    /// <summary>Repair price in whole money units (engine counts more than tyres).</summary>
    public int RepairCost(float scale) => Mathf.CeilToInt((EngineWear * 1500f + TyreWear * 600f) * scale);
}

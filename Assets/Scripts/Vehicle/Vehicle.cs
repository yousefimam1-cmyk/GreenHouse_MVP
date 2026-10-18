using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Base for drivable vehicles: "hover" body that follows the ground (robust on any terrain), driven by a Drivetrain
/// (engine RPM + torque curve, automatic / manual / CVT gearbox, fuel by load, mass, surface grip, wheel slip, 4WD,
/// diff lock, tyre pressure, wear) and DriveAssist (cruise control / limiter, GPS straight line), its own chase camera,
/// fuel, enter/exit. E in/out, WASD drive, Space brake, R reset (on the loading bay: repair), F engine, Shift / Ctrl gears,
/// Y cruise, . / , cruise speed, Z 4WD / diff lock, / GPS line.
/// Gamepad: A / Cross in, B / Circle (or A) out, left stick steer, RT throttle, LT brake / reverse, X / Square reset,
/// L3 engine, RB / LB gears, D-pad up cruise, right / left cruise speed, down 4WD / diff lock, R3 GPS line.
/// Subclasses: FarmTruck (cargo), Tractor (implements).
/// Collisions: each physics step the body box is swept ahead (Rigidbody.SweepTest); fences, walls, buildings, trees and
/// stalls stop the vehicle or make it slide along them (VehicleObstacles decides what blocks), with a thump, lost speed
/// and a little wear. People and animals are bumped instead (NpcBump). Wheels spin with speed and the front wheels steer.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public abstract class Vehicle : Interactable
{
    public static Vehicle Current { get; private set; }

    [Header("Driving")]
    public float maxSpeed = 16f;
    public float reverseSpeed = 6f;
    public float acceleration = 7f;
    public float brakeForce = 18f;
    public float turnRate = 70f;
    public float rideHeight = 0.55f;
    public Vector2 footprint = new Vector2(0.9f, 1.6f);   // half width, half length for ground probes
    public float camDistance = 9f;

    [Header("Drivetrain")]
    public float emptyMass = 3000f;
    public int gears = 6;
    public float repairCostScale = 1f;

    [Header("Fuel")]
    public float fuelCapacity = 100f;
    public float fuelPerSecond = 0.35f;   // full load at rated RPM (idle burns ~8%)

    [Header("Parts (assigned by the spawner)")]
    public Transform[] wheels;
    public Transform exitPoint;

    public bool IsDriving { get; private set; }
    public float Speed { get; protected set; }
    /// <summary>Raw driver input of the last step (lights and visuals): throttle -1..1, brake held.</summary>
    public float ThrottleInput { get; private set; }
    public bool BrakeInput { get; private set; }
    /// <summary>Smoothed steering -1..1 (front wheels, indicators).</summary>
    public float SteerInput => steerSmooth;
    /// <summary>Brake lights: braking, or throttle against the direction of travel.</summary>
    public bool BrakeLights => IsDriving && (BrakeInput || (Mathf.Abs(Speed) > 0.3f && ThrottleInput * Speed < -0.01f));
    /// <summary>Hits against solid obstacles (fences, walls, buildings...) and the speed into the last one (m/s).</summary>
    public int ImpactCount { get; private set; }
    public float LastImpactSpeed { get; private set; }
    /// <summary>Time.time of the last frame the body was held by an obstacle (StuckRescue: a wall is reversed away from, not rescued).</summary>
    public float LastObstacleTime { get; private set; } = -100f;
    public Collider LastObstacle { get; private set; }
    /// <summary>Half size and centre of the body box (from its BoxCollider) in vehicle space.</summary>
    public Vector3 BodyHalfExtents { get; private set; } = new Vector3(0.95f, 0.65f, 2.3f);
    public Vector3 BodyCenter { get; private set; } = new Vector3(0f, 0.62f, 0f);
    /// <summary>Gap kept to obstacles by the sweep (m).</summary>
    public const float Skin = 0.05f;
    private float impactCooldown;
    public float Fuel { get; protected set; }

    protected Rigidbody rb;
    protected readonly Drivetrain drive = new Drivetrain();
    public Drivetrain Drive => drive;
    public DriveAssist Assist { get; } = new DriveAssist();
    /// <summary>Tests / scripted scenes can pin the surface; null = detect it from the world.</summary>
    public DriveSurface? SurfaceOverride;
    public DriveSurface SurfaceNow => drive.Surface;
    public bool SurfaceWet => drive.Wet;
    public float TotalMass => drive.Mass;
    /// <summary>0 off, 1 4WD, 2 4WD + diff lock.</summary>
    public int TractionMode => drive.DiffLock ? 2 : drive.FourWD ? 1 : 0;
    private float surfaceTimer, steerSmooth;
    protected float yaw;
    private Camera chaseCam, mainCam;
    private AudioListener mainListener;
    private GameObject player;
    private InteractionController interaction;
    private int enteredFrame;
    private float camYaw;

    /// <summary>Co-op: another player is driving this vehicle; it follows their poses and can't be entered here.</summary>
    public bool RemoteDriven { get; private set; }

    protected abstract string SaveId { get; }
    public override bool CanInteract => !IsDriving && !RemoteDriven;

    protected override void Awake()
    {
        base.Awake();
        rb = GetComponent<Rigidbody>();
        rb.mass = 1400f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.freezeRotation = true;
        yaw = transform.eulerAngles.y;
        var box = GetComponent<BoxCollider>();
        if (box != null) { BodyHalfExtents = box.size * 0.5f; BodyCenter = box.center; }
        Fuel = SaveData.GetFloat("mazraa.fuel." + SaveId, fuelCapacity);
        LoadCondition();
        GameEvents.NewGameStarted += OnNewGame;
    }

    // ---------------------------------------------------------------- condition (wear) + repair

    private void LoadCondition()
    {
        drive.EngineWear = SaveData.GetFloat("mazraa.wear.engine." + SaveId, 0f);
        drive.TyreWear = SaveData.GetFloat("mazraa.wear.tyre." + SaveId, 0f);
    }

    protected void SaveCondition()
    {
        SaveData.SetFloat("mazraa.wear.engine." + SaveId, drive.EngineWear);
        SaveData.SetFloat("mazraa.wear.tyre." + SaveId, drive.TyreWear);
    }

    protected virtual void OnNewGame()
    {
        drive.EngineWear = drive.TyreWear = 0f;
        drive.FourWD = drive.DiffLock = false;
        Assist.Reset();
        SaveCondition();
    }

    public float EngineCondition => 1f - drive.EngineWear;
    public float TyreCondition => 1f - drive.TyreWear;
    public int RepairCost => drive.RepairCost(repairCostScale);
    /// <summary>Worn enough that the workshop offers a repair (R / X on the loading bay repairs instead of resetting).</summary>
    public bool NeedsRepair => drive.EngineWear + drive.TyreWear > 0.01f;

    /// <summary>Open world (World/OpenWorld): the dealership's workshop yard also repairs.</summary>
    public static System.Func<Vector3, bool> WorkshopAt;

    /// <summary>Parked on the loading bay (the farm workshop) or in the dealership's workshop yard.</summary>
    public bool AtWorkshop
    {
        get
        {
            if (WorkshopAt != null && Mathf.Abs(Speed) <= 0.5f && WorkshopAt(transform.position)) return true;
            var bay = DeliveryZone.LoadingBay;
            if (bay == null || Mathf.Abs(Speed) > 0.5f) return false;
            Vector3 d = transform.position - bay.transform.position; d.y = 0f;
            return d.magnitude < bay.radius;
        }
    }

    /// <summary>Repair engine and tyres for RepairCost (paid like fuel). False if nothing to fix, not at the workshop or too poor.</summary>
    public bool TryRepair()
    {
        int cost = RepairCost;
        var eco = EconomyManager.Instance;
        if (cost <= 0 || eco == null || !AtWorkshop) return false;
        if (eco.Money < cost) { Alert(Loc.T("Not enough money")); return false; }
        using (Ledger.As(Ledger.Repairs)) eco.AddMoney(-cost);
        drive.EngineWear = drive.TyreWear = 0f;
        SaveCondition();
        GameEvents.RaiseCustom("repairVehicle");
        Alert(Loc.T("Repaired engine and tyres for {0} SAR", cost.ToString("N0")));
        return true;
    }

    protected static void Alert(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }

    // ---------------------------------------------------------------- load (overridden by the truck / tractor)

    /// <summary>Cargo / implement / ballast mass on top of emptyMass (kg).</summary>
    protected virtual float LoadMass => 0f;
    protected virtual float BallastMass => 0f;
    /// <summary>Implement draft while working (N): plowing pulls much harder than seeding.</summary>
    protected virtual float DraftForce => 0f;
    protected virtual float WorkSpeedLimit => 0f;
    /// <summary>Mass at which `acceleration` is the classic feel (empty truck, tractor with its plow).</summary>
    protected virtual float ReferenceMass => emptyMass;

    private void SyncDrive(float dt)
    {
        drive.emptyMass = emptyMass; drive.refMass = ReferenceMass;
        drive.maxSpeed = maxSpeed; drive.reverseSpeed = reverseSpeed; drive.acceleration = acceleration; drive.brakeForce = brakeForce;
        drive.fuelPerSecond = fuelPerSecond; drive.gears = Mathf.Max(1, gears);
        drive.ExtraMass = LoadMass; drive.BallastMass = BallastMass;
        drive.DraftForce = DraftForce; drive.WorkSpeedLimit = WorkSpeedLimit;
        drive.Mode = GameSettings.Gearbox; drive.LowPressure = GameSettings.LowTyrePressure;
        surfaceTimer -= dt;
        if (surfaceTimer <= 0f)
        {
            surfaceTimer = 0.2f;
            drive.Surface = SurfaceOverride ?? DriveSurfaces.At(transform.position);
            drive.Wet = DriveSurfaces.Raining || drive.Surface == DriveSurface.WetField;
            if (rb != null) rb.mass = drive.Mass;
        }
    }

    /// <summary>Force a surface refresh now (tests, teleports).</summary>
    public void RefreshSurface() { surfaceTimer = 0f; SyncDrive(0f); }

    public override void Interact()
    {
        if (IsDriving) return;
        if (Current != null && Current != this) return;
        base.Interact();
        Enter();
    }

    private void Enter()
    {
        var inputs = FindAnyObjectByType<StarterAssetsInputs>();
        player = inputs != null ? inputs.gameObject : GameObject.FindGameObjectWithTag("Player");
        interaction = FindAnyObjectByType<InteractionController>();
        mainCam = Camera.main;
        if (player != null) player.SetActive(false);
        if (interaction != null) interaction.enabled = false;
        if (mainCam != null)
        {
            mainCam.enabled = false;
            mainListener = mainCam.GetComponent<AudioListener>();
            if (mainListener != null) mainListener.enabled = false;
        }
        EnsureCamera();
        chaseCam.gameObject.SetActive(true);
        chaseCam.transform.position = transform.position - transform.forward * camDistance + Vector3.up * 4f;
        camYaw = yaw;
        IsDriving = true;
        Current = this;
        enteredFrame = Time.frameCount;
        GraphicsEnhancer.ConfigureCamera(chaseCam);
        Assist.Reset();
        if (GameSettings.AutoEngineStart && Fuel > 0f) drive.StartEngine(true);
        GameEvents.RaiseVehicleChanged(true);
        OnEntered();
    }

    public void Exit()
    {
        IsDriving = false;
        Speed = 0f;
        drive.StopEngine();
        Assist.Reset();
        if (Current == this) Current = null;
        if (chaseCam != null) chaseCam.gameObject.SetActive(false);
        if (mainCam != null) mainCam.enabled = true;
        if (mainListener != null) mainListener.enabled = true;
        if (player != null)
        {
            Vector3 p = exitPoint != null ? exitPoint.position : transform.position - transform.right * 2.6f;
            p = Build.OnGround(p, 1.1f);
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = p;
            player.SetActive(true);
            if (cc != null) cc.enabled = true;
        }
        if (interaction != null) interaction.enabled = true;
        SaveData.SetFloat("mazraa.fuel." + SaveId, Fuel);
        SaveCondition();
        SaveData.Save();
        GameEvents.RaiseVehicleChanged(false);
    }

    protected virtual void OnEntered() { }
    protected virtual void DrivingUpdate(Keyboard kb) { }

    private void EnsureCamera()
    {
        if (chaseCam != null) return;
        var go = new GameObject(name + " Camera");
        chaseCam = go.AddComponent<Camera>();
        chaseCam.fieldOfView = 62f;
        chaseCam.nearClipPlane = 0.2f;
        chaseCam.farClipPlane = mainCam != null ? mainCam.farClipPlane : 1000f;
        go.AddComponent<AudioListener>();
        go.SetActive(false);
    }

    protected virtual void Update()
    {
        UpdateWheels();
        if (!IsDriving || Time.timeScale == 0f) return;
        var kb = Keyboard.current;
        if (Time.frameCount == enteredFrame || NetSession.Typing) return;
        // A menu closing this frame (e.g. Resume with A / Esc) must not also exit or reset the vehicle.
        if ((UIManager.Instance != null && UIManager.Instance.IsUIOpened) || UIManager.PanelClosedFrame == Time.frameCount) return;
        if ((GameInput.Pressed(GameAction.Interact) || MenuNav.Back()) && Mathf.Abs(Speed) < 4f) { Exit(); return; }
        if (GameInput.Pressed(GameAction.VehicleAction)) { if (AtWorkshop && NeedsRepair) TryRepair(); else Recover(); }
        DriveControls();
        DrivingUpdate(kb);
    }

    /// <summary>Engine key, gears, cruise, 4WD / diff lock, GPS line (keyboard and gamepad via GameInput).</summary>
    private void DriveControls()
    {
        if (GameInput.Pressed(GameAction.EngineToggle))
        {
            if (drive.EngineOn) { drive.StopEngine(); Assist.Reset(); Alert(Loc.T("Engine off")); }
            else if (Fuel > 0f) { drive.StartEngine(false); Alert(Loc.T("Engine starting")); }
            else Alert(Loc.T("Out of fuel"));
        }
        if (GameInput.Pressed(GameAction.GearUp) || GameInput.Pressed(GameAction.GearDown))
        {
            if (drive.Mode != GearboxMode.Manual) Alert(Loc.T("Gearbox: {0} - choose Manual in Settings to shift", Loc.Raw(GameSettings.GearboxName(drive.Mode))));
            else if (GameInput.Pressed(GameAction.GearUp)) drive.ShiftUp();
            else drive.ShiftDown();
        }
        if (GameInput.Pressed(GameAction.Cruise))
        {
            var m = Assist.CycleCruise(Speed);
            Alert(m == CruiseMode.Off ? Loc.T("Cruise control off")
                : m == CruiseMode.Cruise ? Loc.T("Cruise control {0} km/h", Kmh(Assist.CruiseSpeed)) : Loc.T("Speed limiter {0} km/h", Kmh(Assist.CruiseSpeed)));
        }
        int step = (GameInput.Pressed(GameAction.CruiseFaster) ? 1 : 0) - (GameInput.Pressed(GameAction.CruiseSlower) ? 1 : 0);
        if (step != 0) { Assist.AdjustCruise(step); Alert(Loc.T("Set speed {0} km/h", Kmh(Assist.CruiseSpeed))); }
        if (GameInput.Pressed(GameAction.Traction))
        {
            int t = (TractionMode + 1) % 3;
            drive.FourWD = t >= 1; drive.DiffLock = t == 2;
            Alert(t == 0 ? Loc.T("2WD, diff lock off") : t == 1 ? Loc.T("4WD on") : Loc.T("4WD + diff lock on"));
        }
        if (GameInput.Pressed(GameAction.LaneAssist))
        {
            Assist.LaneWidth = WorkWidth;
            Assist.ToggleLane(transform.position, yaw);
            Alert(Assist.LaneAssist ? Loc.T("GPS straight line on (lanes {0} m)", WorkWidth.ToString("F0")) : Loc.T("GPS straight line off"));
        }
    }

    /// <summary>Lane spacing for the GPS line (the field implement's working width).</summary>
    protected virtual float WorkWidth => 6f;

    public static string Kmh(float ms) => Mathf.RoundToInt(Mathf.Abs(ms) * 3.6f).ToString();

    /// <summary>Add fuel (returns litres added).</summary>
    public float Refuel(float maxLitres)
    {
        float add = Mathf.Min(maxLitres, fuelCapacity - Fuel);
        if (add <= 0f) return 0f;
        Fuel += add;
        SaveData.SetFloat("mazraa.fuel." + SaveId, Fuel);
        return add;
    }

    private void FixedUpdate()
    {
        if (RemoteDriven) return;
        float throttle = 0f, steer = 0f; bool brake = false;
        var kb = Keyboard.current;
        if (IsDriving && kb != null && !NetSession.Typing)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
        }
        var pad = Gamepad.current;
        if (IsDriving && pad != null)
        {
            throttle += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();   // LT brakes, then reverses
            steer += pad.leftStick.ReadValue().x;
        }
        if (IsDriving) brake = GameInput.Held(GameAction.Brake);
        throttle = Mathf.Clamp(throttle, -1f, 1f);
        steer = GameSettings.ShapeSteer(Mathf.Clamp(steer, -1f, 1f));   // Settings > Controls: dead zone, steering sensitivity
        float dt = Time.fixedDeltaTime;
        StepDrive(throttle, steer, brake, dt);

        Vector3 pos = rb.position;
        Vector3 n = Vector3.zero; float groundY = 0f; int hits = 0;
        Vector3 f = Quaternion.Euler(0, yaw, 0) * Vector3.forward, r = Quaternion.Euler(0, yaw, 0) * Vector3.right;
        // four ground probes at the corners (no array: this runs every physics step)
        for (int c = 0; c < 4; c++)
        {
            Vector3 o = f * (c < 2 ? footprint.y : -footprint.y) + r * ((c & 1) == 0 ? footprint.x : -footprint.x);
            if (Physics.Raycast(pos + o + Vector3.up * 2f, Vector3.down, out var h, 4f + rideHeight, Build.GroundMask, QueryTriggerInteraction.Ignore))
            { n += h.normal; groundY += h.point.y; hits++; }
        }
        bool grounded = hits >= 2;
        // A crest or a road edge under the middle of the body (between the wheels): ride over it instead of high-centring.
        float crest = float.MinValue;
        if (grounded && Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var mid, 4f + rideHeight, Build.GroundMask, QueryTriggerInteraction.Ignore))
            crest = mid.point.y - CrestClearance(rideHeight);
        Vector3 up = grounded ? (n / hits).normalized : Vector3.up;
        Vector3 fwd = Vector3.ProjectOnPlane(f, up).normalized;
        drive.SlopeSin = grounded ? fwd.y : 0f;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(fwd, up), 10f * dt));

        if (Mathf.Abs(Speed) > NpcBump.MinVehicleSpeed && NpcBump.Instance != null) NpcBump.Instance.CheckVehicle(this);
        if (impactCooldown > 0f) impactCooldown -= dt;
        Vector3 vel = Collide(fwd * Speed, dt, grounded ? groundY / hits : pos.y - rideHeight);
        if (grounded)
        {
            float targetY = Mathf.Max(groundY / hits, crest) + rideHeight;
            vel.y += Mathf.Clamp((targetY - pos.y) * 12f, -8f, 8f);
            rb.useGravity = false;
        }
        else
        {
            vel.y = rb.linearVelocity.y;
            rb.useGravity = true;
        }
        rb.linearVelocity = vel;
    }

    /// <summary>
    /// Sweep the body box along this step's motion. A solid obstacle ahead (VehicleObstacles.Blocks) stops the part of the
    /// motion that goes into it: head-on the vehicle stops (a hard hit bounces back a little), at an angle it slides along.
    /// Returns the velocity to apply.
    /// </summary>
    private Vector3 Collide(Vector3 vel, float dt, float groundY)
    {
        Vector3 flat = new Vector3(vel.x, 0f, vel.z);
        float v = flat.magnitude;
        if (v < 0.05f || dt <= 0f || rb == null || rb.isKinematic) return vel;
        Vector3 dir = flat / v;
        RaycastHit hit;
        if (!rb.SweepTest(dir, out hit, v * dt + Skin * 2f, QueryTriggerInteraction.Ignore)) return vel;
        if (!VehicleObstacles.Blocks(hit, groundY)) return vel;
        Vector3 n = hit.normal; n.y = 0f;
        n = n.sqrMagnitude > 1e-4f ? n.normalized : -dir;
        float into = -Vector3.Dot(dir, n);          // 1 = head-on, 0 = grazing
        if (into <= 0.02f) return vel;
        float allowed = Mathf.Max(0f, hit.distance - Skin) / dt;   // speed that just reaches the obstacle this step
        Vector3 tangent = flat - n * Vector3.Dot(flat, n);
        Vector3 result = tangent * (1f - 0.35f * into) - n * Mathf.Min(allowed, v * into);
        float impact = Mathf.Abs(Speed) * into;
        // Speed along the heading after the hit: what is left of the slide, or a small bounce back from a hard head-on hit.
        float slide = Vector3.Dot(result, dir) / v;
        Speed = into > 0.75f && impact > 4f ? -Mathf.Sign(Speed) * Mathf.Min(1.5f, impact * 0.15f) : Speed * Mathf.Clamp01(slide);
        OnImpact(hit.collider, impact, hit.point);
        vel.x = result.x; vel.z = result.z;
        return vel;
    }

    /// <summary>Thump and a little engine / tyre wear (the speed loss is done by the caller) and a "Bump!" note. Rate-limited.</summary>
    private void OnImpact(Collider c, float impactSpeed, Vector3 point)
    {
        LastObstacle = c;
        LastObstacleTime = Time.time;
        if (impactCooldown > 0f || impactSpeed < 0.8f) return;
        impactCooldown = 0.4f;
        ImpactCount++;
        LastImpactSpeed = impactSpeed;
        drive.EngineWear = Mathf.Clamp01(drive.EngineWear + 0.003f * impactSpeed);   // bent bumper, knocked radiator
        drive.TyreWear = Mathf.Clamp01(drive.TyreWear + 0.0015f * impactSpeed);
        if (AmbientAudio.Instance != null) AmbientAudio.Instance.PlayThump(point, Mathf.Clamp01(impactSpeed / 10f));
        if (IsDriving && impactSpeed > 2.5f && UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.T("Bump!"));
    }

    /// <summary>A person or animal was bumped: the vehicle keeps only part of its speed.</summary>
    public void SlowFromBump(float keep) => Speed *= Mathf.Clamp01(keep);

    /// <summary>One drive step: assists -> drivetrain -> speed-sensitive steering. Public so tests can drive without input devices.</summary>
    public void StepDrive(float throttle, float steer, bool brake, float dt)
    {
        SyncDrive(dt);
        if (!IsDriving) { throttle = 0f; steer = 0f; brake = false; }
        ThrottleInput = throttle; BrakeInput = brake;
        if (IsDriving && throttle > 0.05f && !drive.EngineOn && GameSettings.AutoEngineStart && Fuel > 0f) drive.StartEngine(false);
        throttle = Assist.ApplyThrottle(throttle, brake, Speed, dt);
        float fuel = Fuel;
        Speed = drive.Step(Speed, throttle, brake, IsDriving, dt, ref fuel);
        Fuel = fuel;

        steer = Assist.Steer(steer, transform.position, yaw, Speed);
        steerSmooth = Mathf.MoveTowards(steerSmooth, steer, 6f * dt);
        float lowSpeed = Mathf.Clamp01(Mathf.Abs(Speed) / 4f) * Mathf.Sign(Speed);
        float highSpeed = Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(Mathf.Abs(Speed) / Mathf.Max(1f, maxSpeed)));   // calmer steering when fast
        float grip = (drive.DiffLock ? 0.7f : 1f) * (1f - 0.4f * drive.Slip);                                  // diff lock and spinning wheels push wide
        yaw += steerSmooth * turnRate * lowSpeed * highSpeed * grip * dt;
    }

    public float Yaw => yaw;

    private void LateUpdate()
    {
        if (!IsDriving || chaseCam == null) return;
        camYaw = Mathf.LerpAngle(camYaw, yaw, 4f * Time.deltaTime);
        Quaternion rot = Quaternion.Euler(14f, camYaw, 0f);
        Vector3 target = transform.position + Vector3.up * 1.6f;
        Vector3 desired = target - rot * Vector3.forward * camDistance + Vector3.up * 1.5f;
        float gy = Build.GroundY(desired) + 0.8f;
        if (desired.y < gy) desired.y = gy;
        chaseCam.transform.position = Vector3.Lerp(chaseCam.transform.position, desired, 1f - Mathf.Exp(-8f * Time.deltaTime));
        chaseCam.transform.LookAt(target);
    }

    /// <summary>How far the ground under the body's middle may rise above the wheels' average before the body lifts (m).</summary>
    public static float CrestClearance(float rideHeight) => Mathf.Max(0.05f, rideHeight * 0.45f);

    // The physics solver still reports contacts the sweep did not see (something moved into the vehicle, steep ground).
    // Terrain and walkable ground never bounce the vehicle back (that is what made it stick on dune crests and road edges).
    private void OnCollisionEnter(Collision c)
    {
        if (c.contactCount == 0 || c.collider == null || NpcBump.IsBumpable(c.collider.transform)) return;
        var contact = c.GetContact(0);
        if (c.collider is TerrainCollider || contact.normal.y > 0.55f || VehicleObstacles.IsWalkable(c.collider)) return;
        float into = -Vector3.Dot(contact.normal, transform.forward * Mathf.Sign(Speed));
        if (Mathf.Abs(Speed) > 3f && into > 0.4f)
        {
            float impact = Mathf.Abs(Speed) * into;
            Speed *= -0.2f;
            OnImpact(c.collider, impact, contact.point);
        }
    }

    public const int TowFee = 150;

    /// <summary>Stuck rescue (World/StuckRescue): put the vehicle down at p facing yaw, stopped, on its wheels.</summary>
    public void RescueTo(Vector3 p, float newYaw)
    {
        Speed = 0f;
        yaw = newYaw;
        rb.position = Build.OnGround(p, rideHeight + 0.6f);
        rb.rotation = Quaternion.Euler(0, yaw, 0);
        rb.linearVelocity = Vector3.zero;
        Assist.Reset();
    }

    /// <summary>Put the vehicle back on its wheels. Out of fuel (or off the map): tow it to the loading bay for a fee.</summary>
    public void Recover()
    {
        Speed = 0f;
        Vector3 p = transform.position;
        bool tow = (Fuel <= 0.01f || p.y < -50f) && VehicleSpawner.Instance != null;
        if (tow)
        {
            p = VehicleSpawner.Instance.LoadingBay + transform.right * 3f;
            if (EconomyManager.Instance != null) using (Ledger.As(Ledger.Repairs)) EconomyManager.Instance.AddMoney(-TowFee);
            Fuel = Mathf.Max(Fuel, 5f);
            Assist.Reset();
            if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.T("Towed to the loading bay for {0} SAR", TowFee));
        }
        rb.position = Build.OnGround(p, rideHeight + 1f);
        rb.rotation = Quaternion.Euler(0, yaw, 0);
        rb.linearVelocity = Vector3.zero;
    }

    // ---------------------------------------------------------------- wheels: spin with speed, front wheels steer
    public const float MaxSteerDeg = 32f;
    private Quaternion[] wheelBase;
    private Vector3[] wheelUp;
    private float[] wheelRadius, wheelSpin;
    private bool[] wheelFront;
    /// <summary>Spin of the first wheel (degrees, 0..360) and the front wheels' steering angle (degrees).</summary>
    public float WheelSpin => wheelSpin != null && wheelSpin.Length > 0 ? wheelSpin[0] : 0f;
    public float WheelSteer { get; private set; }
    public bool IsFrontWheel(int i) => wheelFront != null && i >= 0 && i < wheelFront.Length && wheelFront[i];
    public float WheelRadius(int i) => wheelRadius != null && i >= 0 && i < wheelRadius.Length ? wheelRadius[i] : 0.45f;

    private void InitWheels()
    {
        int n = wheels.Length;
        wheelBase = new Quaternion[n]; wheelUp = new Vector3[n]; wheelRadius = new float[n]; wheelSpin = new float[n]; wheelFront = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var w = wheels[i];
            wheelRadius[i] = 0.45f;
            if (w == null) continue;
            wheelBase[i] = w.localRotation;
            Quaternion parentRot = w.parent != null ? w.parent.rotation : Quaternion.identity;
            wheelUp[i] = Quaternion.Inverse(parentRot) * (transform.rotation * Vector3.up);   // steering axis in the wheel's parent space
            string nm = w.name.ToLowerInvariant();
            wheelFront[i] = nm.Contains("front") || (!nm.Contains("back") && !nm.Contains("rear") && transform.InverseTransformPoint(w.position).z > 0.05f);
            float r = 0f;
            foreach (var rend in w.GetComponentsInChildren<Renderer>())
            {
                var b = rend.bounds.size;
                r = Mathf.Max(r, Mathf.Max(b.x, Mathf.Max(b.y, b.z)) * 0.5f);
            }
            if (r > 0.05f) wheelRadius[i] = Mathf.Clamp(r, 0.25f, 1.2f);
        }
    }

    private void UpdateWheels()
    {
        if (wheels == null || wheels.Length == 0) return;
        if (wheelBase == null || wheelBase.Length != wheels.Length) InitWheels();
        float dt = Time.deltaTime;
        WheelSteer = Mathf.Lerp(WheelSteer, steerSmooth * MaxSteerDeg, 1f - Mathf.Exp(-10f * dt));
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            if (w == null) continue;
            wheelSpin[i] = Mathf.Repeat(wheelSpin[i] + Speed / wheelRadius[i] * Mathf.Rad2Deg * dt, 360f);
            Quaternion steer = wheelFront[i] ? Quaternion.AngleAxis(WheelSteer, wheelUp[i]) : Quaternion.identity;
            w.localRotation = steer * wheelBase[i] * Quaternion.AngleAxis(wheelSpin[i], Vector3.right);
        }
    }

    public void SetRemoteDriven(bool on)
    {
        if (RemoteDriven == on || (on && IsDriving)) return;
        RemoteDriven = on;
        rb.isKinematic = on;
        if (!on) { Speed = 0f; rb.position = transform.position; rb.rotation = transform.rotation; }
    }

    /// <summary>Move to the pose another player's copy of this vehicle reported (smoothed).</summary>
    public void ApplyRemotePose(Vector3 pos, float newYaw, float speed)
    {
        if (!RemoteDriven) return;
        yaw = newYaw;
        Speed = speed;
        Vector3 p = (pos - transform.position).sqrMagnitude > 100f ? pos : Vector3.Lerp(transform.position, pos, 0.5f);
        Quaternion r = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, newYaw, 0f), 0.5f);
        transform.SetPositionAndRotation(p, r);
        rb.position = p; rb.rotation = r;
    }

    private static bool quitting;
    private void OnApplicationQuit() => quitting = true;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetQuitting() => quitting = false;   // play mode without domain reload

    protected virtual void OnDestroy()
    {
        GameEvents.NewGameStarted -= OnNewGame;
        // Removed while driving (sold, replaced, despawned): give the player back their body, camera and interactions,
        // otherwise they are left hidden with no active camera. Not while quitting (everything is going away);
        // a save-slot reload exits the vehicle first (SaveSlots.Load).
        if (IsDriving && !quitting)
        {
            if (mainCam != null) mainCam.enabled = true;
            if (mainListener != null) mainListener.enabled = true;
            if (player != null) player.SetActive(true);
            if (interaction != null) interaction.enabled = true;
            IsDriving = false;
        }
        if (chaseCam != null) Destroy(chaseCam.gameObject);
        if (Current == this) Current = null;
    }
}

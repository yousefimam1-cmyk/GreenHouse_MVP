using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Base for drivable vehicles: arcade "hover" physics that follows the ground (robust on any terrain),
/// its own chase camera, fuel, enter/exit. E in/out, WASD drive, Space brake, R reset.
/// Subclasses: FarmTruck (cargo), Tractor (implements).
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

    [Header("Fuel")]
    public float fuelCapacity = 100f;
    public float fuelPerSecond = 0.35f;   // while throttling

    [Header("Parts (assigned by the spawner)")]
    public Transform[] wheels;
    public Transform exitPoint;

    public bool IsDriving { get; private set; }
    public float Speed { get; protected set; }
    public float Fuel { get; protected set; }

    protected Rigidbody rb;
    protected float yaw;
    private Camera chaseCam, mainCam;
    private AudioListener mainListener;
    private GameObject player;
    private InteractionController interaction;
    private int enteredFrame;
    private float camYaw;

    protected abstract string SaveId { get; }
    public override bool CanInteract => !IsDriving;

    protected override void Awake()
    {
        base.Awake();
        rb = GetComponent<Rigidbody>();
        rb.mass = 1400f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.freezeRotation = true;
        yaw = transform.eulerAngles.y;
        Fuel = SaveData.GetFloat("mazraa.fuel." + SaveId, fuelCapacity);
    }

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
        GameEvents.RaiseVehicleChanged(true);
        OnEntered();
    }

    public void Exit()
    {
        IsDriving = false;
        Speed = 0f;
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
        if (kb == null || Time.frameCount == enteredFrame) return;
        if (kb.eKey.wasPressedThisFrame && Mathf.Abs(Speed) < 4f) { Exit(); return; }
        if (kb.rKey.wasPressedThisFrame) Recover();
        DrivingUpdate(kb);
    }

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
        float throttle = 0f, steer = 0f; bool brake = false;
        var kb = Keyboard.current;
        if (IsDriving && kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
            brake = kb.spaceKey.isPressed;
        }
        float dt = Time.fixedDeltaTime;
        if (Fuel <= 0f) throttle = 0f;
        else if (throttle != 0f) Fuel = Mathf.Max(0f, Fuel - fuelPerSecond * dt);

        if (brake) Speed = Mathf.MoveTowards(Speed, 0f, brakeForce * dt);
        else if (throttle != 0f)
        {
            bool opposing = Mathf.Sign(throttle) != Mathf.Sign(Speed) && Mathf.Abs(Speed) > 0.3f;
            Speed += throttle * (opposing ? brakeForce : acceleration) * dt;
        }
        else Speed = Mathf.MoveTowards(Speed, 0f, 2.5f * dt);
        Speed = Mathf.Clamp(Speed, -reverseSpeed, maxSpeed);

        float steerFactor = Mathf.Clamp01(Mathf.Abs(Speed) / 4f) * Mathf.Sign(Speed);
        yaw += steer * turnRate * steerFactor * dt;

        Vector3 pos = rb.position;
        Vector3 n = Vector3.zero; float groundY = 0f; int hits = 0;
        Vector3 f = Quaternion.Euler(0, yaw, 0) * Vector3.forward, r = Quaternion.Euler(0, yaw, 0) * Vector3.right;
        foreach (var o in new[] { f * footprint.y + r * footprint.x, f * footprint.y - r * footprint.x, -f * footprint.y + r * footprint.x, -f * footprint.y - r * footprint.x })
        {
            if (Physics.Raycast(pos + o + Vector3.up * 2f, Vector3.down, out var h, 4f + rideHeight, Build.GroundMask, QueryTriggerInteraction.Ignore))
            { n += h.normal; groundY += h.point.y; hits++; }
        }
        bool grounded = hits >= 2;
        Vector3 up = grounded ? (n / hits).normalized : Vector3.up;
        Vector3 fwd = Vector3.ProjectOnPlane(f, up).normalized;
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(fwd, up), 10f * dt));

        Vector3 vel = fwd * Speed;
        if (grounded)
        {
            float targetY = groundY / hits + rideHeight;
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

    private void OnCollisionEnter(Collision c)
    {
        if (c.contactCount == 0) return;
        if (Mathf.Abs(Speed) > 3f && Vector3.Dot(c.GetContact(0).normal, transform.forward * Mathf.Sign(Speed)) < -0.4f)
        {
            Speed *= -0.25f;
            if (IsDriving && UIManager.Instance != null && Mathf.Abs(Speed) > 2f) UIManager.Instance.ShowAlertMessage(Loc.T("Bump!"));
        }
    }

    public const int TowFee = 150;

    /// <summary>Put the vehicle back on its wheels. Out of fuel (or off the map): tow it to the loading bay for a fee.</summary>
    public void Recover()
    {
        Speed = 0f;
        Vector3 p = transform.position;
        bool tow = (Fuel <= 0.01f || p.y < -50f) && VehicleSpawner.Instance != null;
        if (tow)
        {
            p = VehicleSpawner.Instance.LoadingBay + transform.right * 3f;
            if (EconomyManager.Instance != null) EconomyManager.Instance.AddMoney(-TowFee);
            Fuel = Mathf.Max(Fuel, 5f);
            if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.T("Towed to the loading bay for {0} SAR", TowFee));
        }
        rb.position = Build.OnGround(p, rideHeight + 1f);
        rb.rotation = Quaternion.Euler(0, yaw, 0);
        rb.linearVelocity = Vector3.zero;
    }

    private void UpdateWheels()
    {
        if (wheels == null) return;
        float deg = Speed / 0.45f * Mathf.Rad2Deg * Time.deltaTime;
        foreach (var w in wheels) if (w != null) w.Rotate(Vector3.right, deg, Space.Self);
    }

    protected virtual void OnDestroy() { if (Current == this) Current = null; }
}

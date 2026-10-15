using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drivable farm truck: arcade "hover" physics that follows the ground (robust on any terrain),
/// its own chase camera, and a cargo bed. E to get in/out, WASD drive, Space brake, R flips it upright.
/// Loads farm storage at the loading bay and sells at the market (see DeliveryZone).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FarmTruck : Interactable
{
    public static FarmTruck Instance;

    [Header("Driving")]
    public float maxSpeed = 16f;          // m/s (~58 km/h)
    public float reverseSpeed = 6f;
    public float acceleration = 7f;
    public float brakeForce = 18f;
    public float turnRate = 70f;          // deg/s at speed
    public float rideHeight = 0.55f;

    [Header("Parts (assigned by the spawner)")]
    public Transform[] wheels;
    public Transform cargoRoot;
    public Transform exitPoint;

    public bool IsDriving { get; private set; }
    public float Speed { get; private set; }
    public readonly Dictionary<string, float> Cargo = new Dictionary<string, float>();

    private Rigidbody rb;
    private Camera chaseCam;
    private Camera mainCam;
    private AudioListener mainListener;
    private GameObject player;
    private InteractionController interaction;
    private int enteredFrame;
    private float yaw;
    private float camYaw;
    private readonly List<GameObject> crateVisuals = new List<GameObject>();

    public override string InteractionPrompt => Loc.T("[E] Drive truck");
    public override bool CanInteract => !IsDriving;

    public float CargoWeight
    {
        get
        {
            float w = 0f;
            var eco = EconomyManager.Instance;
            foreach (var kv in Cargo) { var g = eco != null ? eco.Good(kv.Key) : null; w += kv.Value * (g != null ? g.weight : 1f); }
            return w;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        rb = GetComponent<Rigidbody>();
        rb.mass = 1400f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.freezeRotation = true;
        yaw = transform.eulerAngles.y;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public override void Interact()
    {
        if (IsDriving) return;
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
        camYaw = yaw;
        IsDriving = true;
        enteredFrame = Time.frameCount;
        GraphicsEnhancer.ConfigureCamera(chaseCam);
        GameEvents.RaiseVehicleChanged(true);
    }

    private void Exit()
    {
        IsDriving = false;
        Speed = 0f;
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
        GameEvents.RaiseVehicleChanged(false);
    }

    private void EnsureCamera()
    {
        if (chaseCam != null) return;
        var go = new GameObject("Truck Camera");
        chaseCam = go.AddComponent<Camera>();
        chaseCam.fieldOfView = 62f;
        chaseCam.nearClipPlane = 0.2f;
        chaseCam.farClipPlane = mainCam != null ? mainCam.farClipPlane : 1000f;
        go.AddComponent<AudioListener>();
        go.SetActive(false);
    }

    private void Update()
    {
        if (!IsDriving) { UpdateWheels(); return; }
        var kb = Keyboard.current;
        if (kb != null && Time.frameCount != enteredFrame)
        {
            if (kb.eKey.wasPressedThisFrame && Mathf.Abs(Speed) < 4f) { Exit(); return; }
            if (kb.rKey.wasPressedThisFrame) Recover();
        }
        UpdateWheels();
        UpdateCargoVisual();
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

        // Ground follow: average normal under four corners, keep ride height.
        Vector3 pos = rb.position;
        Vector3 n = Vector3.zero; float groundY = 0f; int hits = 0;
        Vector3 f = Quaternion.Euler(0, yaw, 0) * Vector3.forward, r = Quaternion.Euler(0, yaw, 0) * Vector3.right;
        foreach (var o in new[] { f * 1.6f + r * 0.9f, f * 1.6f - r * 0.9f, -f * 1.6f + r * 0.9f, -f * 1.6f - r * 0.9f })
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
        Vector3 desired = target - rot * Vector3.forward * 9f + Vector3.up * 1.5f;
        // Keep the camera out of the ground.
        float gy = Build.GroundY(desired) + 0.8f;
        if (desired.y < gy) desired.y = gy;
        chaseCam.transform.position = Vector3.Lerp(chaseCam.transform.position, desired, 1f - Mathf.Exp(-8f * Time.deltaTime));
        chaseCam.transform.LookAt(target);
    }

    private void OnCollisionEnter(Collision c)
    {
        if (Mathf.Abs(Speed) > 3f && Vector3.Dot(c.GetContact(0).normal, transform.forward * Mathf.Sign(Speed)) < -0.4f)
        {
            Speed *= -0.25f;
            if (UIManager.Instance != null && Mathf.Abs(Speed) > 2f) UIManager.Instance.ShowAlertMessage(Loc.T("Bump!"));
        }
    }

    /// <summary>Put the truck back on its wheels (or back at the loading bay if it fell off the map).</summary>
    public void Recover()
    {
        Speed = 0f;
        Vector3 p = transform.position;
        if (p.y < -50f && VehicleSpawner.Instance != null) p = VehicleSpawner.Instance.LoadingBay;
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

    private void UpdateCargoVisual()
    {
        if (cargoRoot == null) return;
        var eco = EconomyManager.Instance;
        float cap = eco != null ? eco.TruckCapacity : 150f;
        int want = Mathf.Clamp(Mathf.CeilToInt(CargoWeight / cap * 6f), 0, 6);
        while (crateVisuals.Count < want)
        {
            int i = crateVisuals.Count;
            var crate = Build.Part(PrimitiveType.Cube, cargoRoot, new Vector3((i % 2 == 0 ? -0.42f : 0.42f), 0.25f + (i / 4) * 0.5f, -0.55f + (i / 2 % 2) * 0.75f),
                new Vector3(0.75f, 0.45f, 0.65f), Build.Mat(new Color(0.62f, 0.45f, 0.26f), 0.1f), "Crate");
            crateVisuals.Add(crate);
        }
        while (crateVisuals.Count > want)
        {
            Destroy(crateVisuals[crateVisuals.Count - 1]);
            crateVisuals.RemoveAt(crateVisuals.Count - 1);
        }
    }
}

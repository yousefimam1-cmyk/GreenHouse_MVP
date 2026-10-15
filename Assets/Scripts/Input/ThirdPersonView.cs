using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

/// <summary>
/// V (or right stick press) switches between the original StarterAssets first-person view (default) and a
/// third-person camera behind and above the player capsule, with a visible farmer model
/// (Resources/Models/Characters/character-male-a, legacy idle / walk / sprint clips). Movement is unchanged:
/// the FirstPersonController still turns the capsule and pitches PlayerCameraRoot; this camera follows them.
/// Interaction keeps using the eye ray of the first-person camera, so reach is the same in both views.
/// </summary>
[MazraaSystem(70)]
public class ThirdPersonView : MonoBehaviour
{
    public const string ModelName = "Characters/character-male-a";
    public static ThirdPersonView Instance { get; private set; }

    public float distance = 4.2f, height = 0.35f, shoulder = 0.45f, modelHeight = 1.75f;
    public bool IsThirdPerson { get; private set; }
    public Camera Camera => cam;
    public GameObject Model => model;

    private Transform player, cameraRoot;
    private Camera fpCam, cam;
    private AudioListener fpListener;
    private GameObject model;
    private Animation anim;
    private readonly Dictionary<string, string> clips = new Dictionary<string, string>();
    private string playing;
    private Vector3 lastPos;
    private float speed;

    private bool pendingSwitch;

    private void Awake() => Instance = this;
    private void OnEnable() => GameEvents.VehicleChanged += OnVehicle;
    private void OnDisable() => GameEvents.VehicleChanged -= OnVehicle;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnVehicle(bool entered)
    {
        if (entered || !pendingSwitch) return;
        pendingSwitch = false;
        Set(GameSettings.ThirdPerson);
    }

    private void Start()
    {
        if (GameSettings.ThirdPerson) Invoke(nameof(ApplySaved), 0.1f);
    }

    private void ApplySaved() { if (GameSettings.ThirdPerson && !IsThirdPerson) Set(true); }

    /// <summary>Switch view (Settings menu, V key). Saved in PlayerPrefs.</summary>
    public static void SetMode(bool thirdPerson)
    {
        GameSettings.ThirdPerson = thirdPerson;
        if (Instance != null) Instance.Set(thirdPerson);
    }

    private void Update()
    {
        if (Time.timeScale == 0f || (UIManager.Instance != null && UIManager.Instance.IsUIOpened)) return;
        if (Vehicle.Current == null && GameInput.Pressed(GameAction.View)) SetMode(!IsThirdPerson);
    }

    private bool FindPlayer()
    {
        if (player != null && fpCam != null) return true;
        var inputs = FindAnyObjectByType<StarterAssetsInputs>();
        if (inputs == null) return false;
        player = inputs.transform;
        cameraRoot = FindChild(player, "PlayerCameraRoot");
        if (cameraRoot == null) cameraRoot = player;
        fpCam = Camera.main;
        if (fpCam == null) return false;
        fpListener = fpCam.GetComponent<AudioListener>();
        return true;
    }

    private static Transform FindChild(Transform t, string name)
    {
        foreach (Transform c in t)
        {
            if (c.name == name) return c;
            var d = FindChild(c, name);
            if (d != null) return d;
        }
        return null;
    }

    public void Set(bool third)
    {
        if (third == IsThirdPerson) return;
        if (!FindPlayer()) return;
        // While driving the vehicle owns the camera; switch when the player gets out.
        if (Vehicle.Current != null) { pendingSwitch = true; return; }
        IsThirdPerson = third;
        if (third)
        {
            EnsureCamera();
            EnsureModel();
            // The TP camera becomes "the" main camera for prompts, markers and the vehicle code.
            fpCam.enabled = false;
            fpCam.tag = "Untagged";
            if (fpListener != null) fpListener.enabled = false;
            cam.tag = "MainCamera";
            cam.gameObject.SetActive(true);
            cam.enabled = true;
            if (model != null) model.SetActive(true);
            lastPos = player.position;
            LateUpdate();
        }
        else
        {
            if (cam != null) { cam.tag = "Untagged"; cam.gameObject.SetActive(false); }
            fpCam.tag = "MainCamera";
            fpCam.enabled = true;
            if (fpListener != null) fpListener.enabled = true;
            if (model != null) model.SetActive(false);
        }
    }

    private void EnsureCamera()
    {
        if (cam != null) return;
        var go = new GameObject("Third Person Camera");
        cam = go.AddComponent<Camera>();
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.15f;
        cam.farClipPlane = fpCam.farClipPlane;
        go.AddComponent<AudioListener>();
        GraphicsEnhancer.ConfigureCamera(cam);
        go.SetActive(false);
    }

    private void EnsureModel()
    {
        if (model != null) return;
        var holder = new GameObject("Player Model (third person)");
        holder.transform.SetParent(player, false);
        // The capsule pivot is at its feet in StarterAssets; the model stands on it and faces forward (+Z).
        model = Build.TryModelHeight(ModelName, holder.transform, modelHeight);
        if (model == null)
        {
            // Placeholder farmer if the model is missing: body + head.
            model = new GameObject("Placeholder");
            model.transform.SetParent(holder.transform, false);
            Build.Part(PrimitiveType.Capsule, model.transform, new Vector3(0, 0.8f, 0), new Vector3(0.6f, 0.8f, 0.6f), Build.Mat(new Color(0.3f, 0.45f, 0.7f)), "Body");
            Build.Part(PrimitiveType.Sphere, model.transform, new Vector3(0, 1.62f, 0), new Vector3(0.35f, 0.35f, 0.35f), Build.Mat(new Color(0.85f, 0.7f, 0.55f)), "Head");
        }
        model = holder;
        anim = holder.GetComponentInChildren<Animation>();
        clips.Clear();
        if (anim != null)
            foreach (AnimationState s in anim)
            {
                s.wrapMode = WrapMode.Loop;
                string n = s.name.ToLowerInvariant();
                foreach (var key in new[] { "idle", "walk", "sprint", "run" })
                    if (n.Contains(key) && !clips.ContainsKey(key) && !n.Contains("walkslow")) clips[key] = s.name;
            }
    }

    private void Play(params string[] keys)
    {
        if (anim == null) return;
        foreach (var k in keys)
        {
            string c;
            if (!clips.TryGetValue(k, out c)) continue;
            if (c != playing) { anim.CrossFade(c, 0.2f); playing = c; }
            return;
        }
    }

    /// <summary>Camera position for a pivot (eye), view yaw / pitch: behind, a little above and over the shoulder.</summary>
    public static Vector3 CameraPosition(Vector3 pivot, float yaw, float pitch, float distance, float height, float shoulder)
    {
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        return pivot + rot * new Vector3(shoulder, height, -distance);
    }

    private void LateUpdate()
    {
        if (!IsThirdPerson || cam == null || player == null) return;
        if (!player.gameObject.activeInHierarchy) return;      // driving: the vehicle camera is in charge
        float yaw = player.eulerAngles.y;
        float pitch = cameraRoot != player ? cameraRoot.localEulerAngles.x : 0f;
        if (pitch > 180f) pitch -= 360f;
        Vector3 pivot = cameraRoot.position;
        Vector3 desired = CameraPosition(pivot, yaw, pitch, distance, height, shoulder);
        // Pull in when something is between the player and the camera.
        Vector3 dir = desired - pivot;
        float len = dir.magnitude;
        RaycastHit hit;
        if (len > 0.01f && Physics.Raycast(pivot, dir / len, out hit, len, Build.GroundMask, QueryTriggerInteraction.Ignore))
            desired = pivot + dir / len * Mathf.Max(0.4f, hit.distance - 0.2f);
        cam.transform.position = desired;
        cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // Animation from the capsule's ground speed.
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 d = player.position - lastPos; d.y = 0f;
        lastPos = player.position;
        speed = Mathf.Lerp(speed, d.magnitude / dt, 0.25f);
        if (speed > 5.2f) Play("sprint", "run", "walk");
        else if (speed > 0.4f) Play("walk", "run");
        else Play("idle");
    }

    public float GroundSpeed => speed;
}

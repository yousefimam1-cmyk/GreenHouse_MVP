using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Photo mode: F9 (or pause menu / journal "Photo mode") freezes the game, hides the HUD and gives a free camera.
/// Keyboard: WASD move, mouse look, Page Up / Page Down rise / sink, F next filter, F1 show / hide the help,
/// F12 save a photo, Esc or F9 leave. Gamepad: left stick move, right stick look, RT / LT rise / sink, LB / RB filter,
/// Y / Triangle save a photo, X / Square help, B / Circle leave. Filters (warm desert, golden hour, black and white,
/// vivid, old photo) are a post-processing volume. Photos go to persistentDataPath/Photos. The camera stays within
/// 60 m of where photo mode started.
/// </summary>
[MazraaSystem(80)]
public class PhotoMode : MonoBehaviour
{
    public static PhotoMode Instance { get; private set; }
    public static readonly string[] FilterNames = { "No filter", "Warm desert", "Golden hour", "Black and white", "Vivid", "Old photo" };
    public const float MaxDistance = 60f, MoveSpeed = 8f;

    public bool Active { get; private set; }
    public int Filter { get; private set; }
    public Camera PhotoCamera => cam;
    public string LastPhoto { get; private set; }
    public bool HelpVisible { get; private set; } = true;

    private Camera cam;
    private Vector3 origin;
    private float yaw, pitch, timeScaleBefore = 1f;
    private int enteredFrame;
    private readonly List<Camera> disabledCameras = new List<Camera>();
    private readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    private Canvas hintCanvas;
    private TextMeshProUGUI hint;
    private Volume volume;
    private ColorAdjustments color;
    private Vignette vignette;
    private WhiteBalance white;
    private CursorLockMode cursorBefore;
    private bool cursorVisibleBefore;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Active) Exit(); if (Instance == this) Instance = null; }

    private static bool K(KeyControl k) => k != null && k.wasPressedThisFrame;
    private static bool B(ButtonControl b) => b != null && b.wasPressedThisFrame;

    private void Update()
    {
        var kb = Keyboard.current;
        if (!Active)
        {
            if (kb != null && K(kb.f9Key) && CozyKeys.Free && !NetSession.Typing) Enter();
            return;
        }
        if (Time.frameCount == enteredFrame) return;
        var pad = Gamepad.current;
        if ((kb != null && K(kb.f9Key)) || MenuNav.Back()) { Exit(); return; }
        if ((kb != null && K(kb.fKey)) || (pad != null && B(pad.rightShoulder))) SetFilter(Filter + 1);
        if (pad != null && B(pad.leftShoulder)) SetFilter(Filter - 1);
        if ((kb != null && K(kb.f1Key)) || (pad != null && B(pad.buttonWest))) { HelpVisible = !HelpVisible; RefreshHint(); }
        if ((kb != null && K(kb.f12Key)) || (pad != null && B(pad.buttonNorth))) TakePhoto();

        // free camera (unscaled time: the game is frozen)
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        var inputs = CozyFx.Player != null ? CozyFx.Player.GetComponent<StarterAssets.StarterAssetsInputs>() : null;
        Vector2 move = inputs != null ? inputs.move : Vector2.zero;
        float lookX = 0f, lookY = 0f;
        if (pad != null)
        {
            if (Mathf.Abs(move.x) + Mathf.Abs(move.y) < 0.05f) move = pad.leftStick.ReadValue();
            var rs = pad.rightStick.ReadValue();
            lookX += rs.x * 120f * dt; lookY += rs.y * 120f * dt;
        }
        if (Mouse.current != null) { var md = Mouse.current.delta.ReadValue(); lookX += md.x * 0.1f; lookY += md.y * 0.1f; }
        var look = new Vector2(lookX, lookY);
        float rise = 0f;
        if (kb != null && kb.pageUpKey != null && kb.pageUpKey.isPressed) rise += 1f;
        if (kb != null && kb.pageDownKey != null && kb.pageDownKey.isPressed) rise -= 1f;
        if (pad != null) rise += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
        Fly(move, look, rise, dt);
    }

    /// <summary>Move the photo camera (also used by tests): move = strafe / forward, look = yaw / pitch degrees, rise = up / down.</summary>
    public void Fly(Vector2 move, Vector2 look, float rise, float dt)
    {
        if (!Active || cam == null) return;
        yaw += look.x;
        pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 delta = (rot * new Vector3(move.x, 0f, move.y) + Vector3.up * rise) * MoveSpeed * dt;
        Vector3 pos = cam.transform.position + delta;
        Vector3 off = pos - origin;
        if (off.magnitude > MaxDistance) pos = origin + off.normalized * MaxDistance;
        pos.y = Mathf.Max(pos.y, Build.GroundY(pos) + 0.3f);
        cam.transform.SetPositionAndRotation(pos, rot);
    }

    public void Enter()
    {
        if (Active) return;
        var src = Camera.main;
        if (src == null) foreach (var c in Camera.allCameras) { src = c; break; }
        Active = true;
        enteredFrame = Time.frameCount;
        timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(true);
        cursorBefore = Cursor.lockState; cursorVisibleBefore = Cursor.visible;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;

        var go = new GameObject("Photo Camera");
        cam = go.AddComponent<Camera>();
        if (src != null)
        {
            go.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
            cam.fieldOfView = src.fieldOfView; cam.nearClipPlane = src.nearClipPlane; cam.farClipPlane = src.farClipPlane;
        }
        else { go.transform.position = CozyFx.FarmStart + Vector3.up * 2f; cam.fieldOfView = 60f; }
        cam.depth = 50f;
        origin = go.transform.position;
        var e = go.transform.rotation.eulerAngles;
        yaw = e.y; pitch = e.x > 180f ? e.x - 360f : e.x;

        disabledCameras.Clear();
        foreach (var c in Camera.allCameras) if (c != cam && c.enabled) { c.enabled = false; disabledCameras.Add(c); }
        hiddenCanvases.Clear();
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c != null && c.enabled && c.isRootCanvas && c != hintCanvas) { c.enabled = false; hiddenCanvases.Add(c); }
        BuildHint();
        BuildVolume();
        SetFilter(Filter);
        if (CelebrationCard.Instance != null) CelebrationCard.Instance.Skip();
    }

    public void Exit()
    {
        if (!Active) return;
        Active = false;
        foreach (var c in disabledCameras) if (c != null) c.enabled = true;
        disabledCameras.Clear();
        foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
        hiddenCanvases.Clear();
        if (cam != null) Destroy(cam.gameObject);
        cam = null;
        if (hintCanvas != null) hintCanvas.gameObject.SetActive(false);
        if (volume != null) volume.weight = 0f;
        Time.timeScale = timeScaleBefore;
        Cursor.lockState = cursorBefore; Cursor.visible = cursorVisibleBefore;
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(false);
    }

    public void SetFilter(int f)
    {
        Filter = ((f % FilterNames.Length) + FilterNames.Length) % FilterNames.Length;
        if (color != null)
        {
            // none, warm desert, golden hour, black and white, vivid, old photo
            float[] sat = { 0f, 10f, 15f, -100f, 35f, -45f };
            float[] con = { 0f, 8f, 12f, 20f, 18f, -10f };
            float[] temp = { 0f, 25f, 40f, 0f, 0f, 30f };
            float[] vig = { 0f, 0.25f, 0.35f, 0.4f, 0.15f, 0.5f };
            color.saturation.Override(sat[Filter]);
            color.contrast.Override(con[Filter]);
            color.postExposure.Override(Filter == 2 ? 0.2f : 0f);
            color.colorFilter.Override(Filter == 5 ? new Color(1f, 0.9f, 0.75f) : Filter == 2 ? new Color(1f, 0.92f, 0.8f) : Color.white);
            if (white != null) white.temperature.Override(temp[Filter]);
            if (vignette != null) { vignette.intensity.Override(vig[Filter]); vignette.smoothness.Override(0.4f); }
        }
        if (volume != null) volume.weight = Active && Filter != 0 ? 1f : 0f;
        RefreshHint();
    }

    public static string FilterName(int f) => Loc.Raw(FilterNames[((f % FilterNames.Length) + FilterNames.Length) % FilterNames.Length]);

    /// <summary>Save a photo (PNG) to persistentDataPath/Photos. Returns the path.</summary>
    public string TakePhoto()
    {
        string dir = Path.Combine(Application.persistentDataPath, "Photos");
        try { Directory.CreateDirectory(dir); } catch (Exception) { }
        string path = Path.Combine(dir, "mazraa_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
        bool help = HelpVisible;
        if (hintCanvas != null) hintCanvas.enabled = false;   // the help is not in the picture
        ScreenCapture.CaptureScreenshot(path);
        if (hintCanvas != null) hintCanvas.enabled = help;
        LastPhoto = path;
        GameEvents.RaiseCustom("photoTaken", 1f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX();
        return path;
    }

    private void BuildHint()
    {
        if (hintCanvas == null)
        {
            hintCanvas = MenuKit.Canvas("Photo Mode UI", transform, 95);
            var panel = MenuKit.Rect("Hint", hintCanvas.transform, typeof(UnityEngine.UI.Image));
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0, 30f);
            panel.sizeDelta = new Vector2(1500, 110);
            panel.GetComponent<UnityEngine.UI.Image>().color = new Color(0f, 0f, 0f, 0.5f);
            hint = MenuKit.Text(panel, 22, Color.white);
            MenuKit.Stretch(hint.rectTransform, 12f);
        }
        hintCanvas.gameObject.SetActive(true);
        RefreshHint();
    }

    public string HintText => Loc.Raw("Photo mode") + " - " + FilterName(Filter) + "\n" + (GameInput.UsingGamepad
        ? Loc.Raw("Sticks: move / look   RT / LT: up / down   LB / RB: filter   Y: take photo   X: hide help   B: leave")
        : Loc.Raw("WASD + mouse: move / look   Page Up / Down: up / down   F: filter   F12: take photo   F1: hide help   Esc: leave"));

    private void RefreshHint()
    {
        if (hint == null) return;
        Loc.ApplyFont(hint);
        hint.alignment = TextAlignmentOptions.Center;
        hint.text = Loc.Display(HintText);
        if (hintCanvas != null) hintCanvas.enabled = HelpVisible;
    }

    private void BuildVolume()
    {
        if (volume != null) return;
        var go = new GameObject("Photo Mode Volume");
        go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true; volume.priority = 100f; volume.weight = 0f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        color = profile.Add<ColorAdjustments>(true);
        vignette = profile.Add<Vignette>(true);
        white = profile.Add<WhiteBalance>(true);
        volume.sharedProfile = profile;
    }
}

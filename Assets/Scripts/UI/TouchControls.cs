using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// On-screen controls for phones and tablets (only when Application.isMobilePlatform, or ForceShow for testing in
/// the editor). Built with the Input System's OnScreenStick / OnScreenButton, which feed virtual devices:
/// walking: left stick = Gamepad left stick (Move), right pad = Gamepad right stick (Look), Run = left trigger,
/// Jump = Space. E = interact / enter / exit. Driving (Vehicle.Current set): W / S / A / D pad, Space = brake,
/// R = reset, Q = tractor implement. P = pause. All keys are the same ones the game already reads.
/// </summary>
[MazraaSystem(95)]
public class TouchControls : MonoBehaviour
{
    public static bool ForceShow;
    public static TouchControls Instance { get; private set; }
    public bool Active { get; private set; }

    private GameObject walkGroup, driveGroup;
    private Sprite circle;

    private static readonly string[] Labels = { "Run", "Jump", "Brake", "Reset", "Tool", "Pause" };

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        Active = Application.isMobilePlatform || ForceShow;
        if (!Active) { enabled = false; return; }
        Build();
    }

    private void Update()
    {
        if (!Active) return;
        bool driving = Vehicle.Current != null;
        if (walkGroup != null && walkGroup.activeSelf == driving) walkGroup.SetActive(!driving);
        if (driveGroup != null && driveGroup.activeSelf != driving) driveGroup.SetActive(driving);
    }

    private void Build()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem (touch)");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }
        circle = MakeCircle();
        var canvasGo = new GameObject("Touch Controls", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        var rt = (RectTransform)canvasGo.transform;

        walkGroup = Group(rt, "Walk");
        Stick(walkGroup.transform, "Move", "<Gamepad>/leftStick", new Vector2(0f, 0f), new Vector2(260f, 300f), 110f);
        Stick(walkGroup.transform, "Look", "<Gamepad>/rightStick", new Vector2(1f, 0f), new Vector2(-520f, 280f), 120f);
        Button(walkGroup.transform, "Run", "<Gamepad>/leftTrigger", new Vector2(0f, 0f), new Vector2(470f, 130f), 120f, Labels[0]);
        Button(walkGroup.transform, "Jump", "<Keyboard>/space", new Vector2(1f, 0f), new Vector2(-150f, 120f), 150f, Labels[1]);
        Button(walkGroup.transform, "E", "<Keyboard>/e", new Vector2(1f, 0f), new Vector2(-150f, 300f), 150f, "E");

        driveGroup = Group(rt, "Drive");
        Button(driveGroup.transform, "W", "<Keyboard>/w", new Vector2(1f, 0f), new Vector2(-170f, 330f), 160f, "^");
        Button(driveGroup.transform, "S", "<Keyboard>/s", new Vector2(1f, 0f), new Vector2(-170f, 150f), 150f, "v");
        Button(driveGroup.transform, "A", "<Keyboard>/a", new Vector2(0f, 0f), new Vector2(140f, 170f), 170f, "<");
        Button(driveGroup.transform, "D", "<Keyboard>/d", new Vector2(0f, 0f), new Vector2(330f, 170f), 170f, ">");
        Button(driveGroup.transform, "Brake", "<Keyboard>/space", new Vector2(1f, 0f), new Vector2(-350f, 150f), 140f, Labels[2]);
        Button(driveGroup.transform, "E", "<Keyboard>/e", new Vector2(1f, 0f), new Vector2(-350f, 330f), 130f, "E");
        Button(driveGroup.transform, "R", "<Keyboard>/r", new Vector2(0f, 0f), new Vector2(140f, 360f), 110f, Labels[3]);
        Button(driveGroup.transform, "Q", "<Keyboard>/q", new Vector2(0f, 0f), new Vector2(290f, 360f), 110f, Labels[4]);
        driveGroup.SetActive(false);

        Button(rt, "P", "<Keyboard>/p", new Vector2(1f, 1f), new Vector2(-90f, -90f), 100f, Labels[5]);
    }

    private static GameObject Group(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        return go;
    }

    private RectTransform Pad(Transform parent, string name, Vector2 anchor, Vector2 pos, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.SetActive(false); // configure before the on-screen control's OnEnable runs
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = anchor; r.anchorMax = anchor; r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos; r.sizeDelta = new Vector2(size, size);
        var img = go.AddComponent<Image>();
        img.sprite = circle; img.color = color; img.raycastTarget = true;
        return r;
    }

    private void Stick(Transform parent, string name, string path, Vector2 anchor, Vector2 pos, float range)
    {
        var bg = Pad(parent, name + " Area", anchor, pos, range * 2.2f, new Color(1f, 1f, 1f, 0.12f));
        bg.gameObject.SetActive(true);
        var knob = Pad(bg, name, new Vector2(0.5f, 0.5f), Vector2.zero, range * 0.9f, new Color(1f, 1f, 1f, 0.45f));
        var stick = knob.gameObject.AddComponent<OnScreenStick>();
        stick.movementRange = range;
        stick.controlPath = path;
        knob.gameObject.SetActive(true);
    }

    private void Button(Transform parent, string name, string path, Vector2 anchor, Vector2 pos, float size, string label)
    {
        var r = Pad(parent, name, anchor, pos, size, new Color(0.95f, 0.85f, 0.6f, 0.38f));
        var b = r.gameObject.AddComponent<OnScreenButton>();
        b.controlPath = path;
        var textGo = new GameObject("Label", typeof(RectTransform));
        var tr = (RectTransform)textGo.transform;
        tr.SetParent(r, false);
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
        var t = textGo.AddComponent<TMPro.TextMeshProUGUI>();
        t.text = label.Length > 1 ? Loc.T(label) : label;
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.fontSize = label.Length > 1 ? 30f : 56f;
        t.color = new Color(0.15f, 0.1f, 0.05f, 0.9f);
        t.raycastTarget = false;
        r.gameObject.SetActive(true);
    }

    private static Sprite MakeCircle()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - n * 0.5f + 0.5f) / (n * 0.5f), dy = (y - n * 0.5f + 0.5f) / (n * 0.5f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((1f - d) * n * 0.25f);                  // anti-aliased edge
                float ring = d > 0.86f ? 1f : 0.75f;                             // brighter rim
                tex.SetPixel(x, y, new Color(ring, ring, ring, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
    }
}

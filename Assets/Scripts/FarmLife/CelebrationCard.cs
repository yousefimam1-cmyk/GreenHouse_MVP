using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Non-blocking celebration card at the top of the screen (level up, mastery star, heart event, festival, daily gift).
/// Cards queue up and each stays ~6 s (unscaled time); it never takes input, so play goes on underneath.
/// </summary>
public class CelebrationCard : MonoBehaviour
{
    public static CelebrationCard Instance { get; private set; }
    public const float Seconds = 6f;

    private class Item { public string title, body, footer; }
    private readonly Queue<Item> queue = new Queue<Item>();
    private Canvas canvas;
    private TextMeshProUGUI text;
    private float left;

    /// <summary>Title / body of the card on screen (unshaped), for tests and logs.</summary>
    public string Current { get; private set; } = "";
    public int Queued => queue.Count;
    /// <summary>Every card shown since start (unshaped titles), newest last. For tests.</summary>
    public static readonly List<string> History = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { History.Clear(); }

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public static void Show(string title, string body, string footer)
    {
        if (Instance == null)
        {
            var host = GameManager.Instance != null ? GameManager.Instance.transform : null;
            var go = new GameObject("Celebration Card");
            if (host != null) go.transform.SetParent(host, false);
            go.AddComponent<CelebrationCard>();
        }
        History.Add(title);
        if (History.Count > 500) History.RemoveAt(0);
        Instance.queue.Enqueue(new Item { title = title, body = body, footer = footer });
        if (Instance.left <= 0f) Instance.Next();
    }

    private void Build()
    {
        if (canvas != null) return;
        canvas = MenuKit.Canvas("Celebration Card UI", transform, 70);
        var panel = MenuKit.Rect("Card", canvas.transform, typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.VerticalLayoutGroup), typeof(UnityEngine.UI.ContentSizeFitter));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0, -90f);
        panel.sizeDelta = new Vector2(760, 0);
        panel.GetComponent<UnityEngine.UI.Image>().color = new Color(0.16f, 0.1f, 0.03f, 0.93f);
        var vl = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        vl.padding = new RectOffset(26, 26, 18, 18); vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
        panel.GetComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        text = MenuKit.Text(panel, 24, Color.white);
        canvas.gameObject.SetActive(false);
    }

    private void Next()
    {
        if (queue.Count == 0) { left = 0f; Current = ""; if (canvas != null) canvas.gameObject.SetActive(false); return; }
        Build();
        var it = queue.Dequeue();
        Current = it.title + "\n" + it.body + (string.IsNullOrEmpty(it.footer) ? "" : "\n" + it.footer);
        Loc.ApplyFont(text);
        text.alignment = Loc.IsArabic ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        text.text = "<size=125%><color=#FFC94A><b>" + Loc.Display(it.title) + "</b></color></size>\n" + Loc.Display(it.body)
                  + (string.IsNullOrEmpty(it.footer) ? "" : "\n<size=80%><color=#D8CDB8>" + Glyphs.Apply(Loc.Display(it.footer)) + "</color></size>");
        canvas.gameObject.SetActive(true);
        left = Seconds;
    }

    private void Update()
    {
        if (left <= 0f) return;
        left -= Time.unscaledDeltaTime;
        if (left <= 0f) Next();
    }

    /// <summary>Hide the current card now (photo mode, tests).</summary>
    public void Skip() { left = 0f; Next(); }
}

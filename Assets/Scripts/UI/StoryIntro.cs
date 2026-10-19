using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Opening story card shown on a new game (Enter or click to start). Bilingual.</summary>
public class StoryIntro : MonoBehaviour
{
    private GameObject canvasGo;
    private TextMeshProUGUI text;
    private bool open;

    private void Start()
    {
        if (SaveData.GetInt("mazraa.introSeen", 0) == 1 || !GameModes.HasStory) return;
        Invoke(nameof(Show), 1.2f);
    }

    private void Show()
    {
        if (GameMenu.IsOpen) { Invoke(nameof(Show), 0.5f); return; }   // after the main menu closes
        if (!GameModes.HasStory || SaveData.GetInt("mazraa.introSeen", 0) == 1) return;   // free play / challenge (or loaded a farm): no story
        canvasGo = new GameObject("Story Intro", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var bg = new GameObject("Backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
        bg.transform.SetParent(canvasGo.transform, false);
        var brt = (RectTransform)bg.transform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(UiTheme.Scrim.r, UiTheme.Scrim.g, UiTheme.Scrim.b, 0.86f);
        bg.GetComponent<Button>().onClick.AddListener(Close);

        var tgo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        tgo.transform.SetParent(bg.transform, false);
        var trt = (RectTransform)tgo.transform; trt.anchorMin = new Vector2(0.2f, 0.15f); trt.anchorMax = new Vector2(0.8f, 0.85f); trt.offsetMin = trt.offsetMax = Vector2.zero;
        text = tgo.GetComponent<TextMeshProUGUI>();
        text.fontSize = 30; text.color = Color.white; text.richText = true; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        Loc.ExcludeFromScene(text);
        Refresh();
        Loc.OnLanguageChanged += Refresh;
        open = true;
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(true);
    }

    private void Refresh()
    {
        if (text == null) return;
        Loc.ApplyFont(text);
        text.alignment = Loc.IsArabic ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        text.text = "<size=150%><color=#F2A93B><b>" + Loc.T("Mazraa") + "</b></color></size>\n\n" + Body() +
                    "\n\n<size=80%><color=#BBBBBB>" + Loc.T("[Enter] Start   [L] Arabic / English") + "</color></size>";
    }

    /// <summary>The opening story: the base game's text, or chapter 1 of a DLC map's own campaign (told by its narrator).</summary>
    public static string Body()
    {
        var cm = CampaignManager.Instance;
        if (cm == null || !cm.IsRegionCampaign || cm.Chapters.Length == 0) return Loc.T("STORY_BODY");
        var c = cm.Chapters[0];
        return "<b>" + Loc.Display(CampaignManager.Title(c)) + "</b>\n\n" + Loc.Display(CampaignManager.Intro(c)) + "\n\n<i>- " + Loc.Display(cm.Narrator) + "</i>";
    }

    private void Update()
    {
        if (!open) return;
        if (MenuNav.Confirm()) Close();   // Enter / Space / A (Cross)
    }

    private void Close()
    {
        if (!open) return;
        open = false;
        SaveData.SetInt("mazraa.introSeen", 1);
        SaveData.Save();
        Loc.OnLanguageChanged -= Refresh;
        if (canvasGo != null) Destroy(canvasGo);
        if (UIManager.Instance != null) UIManager.Instance.SetExternalPanel(false);
    }

    private void OnDestroy() => Loc.OnLanguageChanged -= Refresh;
}

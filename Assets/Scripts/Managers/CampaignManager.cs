using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class CampaignChapter
{
    public string id, titleEn, titleAr, introEn, introAr, outroEn, outroAr;
    public int fromMission = 1, reward;
}

[Serializable]
public class CampaignFile
{
    /// <summary>Map this story belongs to (Resources/Data/Campaigns/*.json); empty = the base game's campaign (Najd).</summary>
    public string region;
    public string narratorEn, narratorAr;
    public CampaignChapter[] chapters;
}

/// <summary>
/// Story campaign over the mission chain (Resources/Data/campaign.json): chapters group missions, each new chapter opens
/// with a narrated chapter card (Abu Saleh), finishing a chapter shows its closing line and pays the chapter reward once.
/// Chapter 1's intro is the existing opening StoryIntro. Cards do not pause the game; [Enter] / A or a click closes them
/// (they also close by themselves after a while). The pause menu shows a Campaign page with every chapter's progress.
/// </summary>
[MazraaSystem(36)]
public class CampaignManager : MonoBehaviour
{
    public const string SeenKey = "mazraa.campaign.seen";       // highest chapter whose intro was shown
    public const string PaidKey = "mazraa.campaign.paid";       // highest chapter whose reward was paid

    public static CampaignManager Instance { get; private set; }
    public CampaignChapter[] Chapters { get; private set; } = new CampaignChapter[0];
    public bool CardOpen => cardRoot != null && cardRoot.activeSelf;

    private CampaignFile file;
    private GameObject cardRoot;
    private TextMeshProUGUI cardText;
    private float closeAt, checkTimer = 1f;
    private int shownFrame;

    private CampaignFile baseFile;
    private readonly System.Collections.Generic.List<CampaignFile> regionFiles = new System.Collections.Generic.List<CampaignFile>();

    /// <summary>True when the current map has its own story (e.g. the Egypt DLC with Am Hassan).</summary>
    public bool IsRegionCampaign => file != null && !string.IsNullOrEmpty(file.region);
    public CampaignFile File => file;

    private void Awake()
    {
        Instance = this;
        var ta = Resources.Load<TextAsset>("Data/campaign");
        try { if (ta != null) baseFile = JsonUtility.FromJson<CampaignFile>(ta.text); }
        catch (Exception e) { Debug.LogError("[Campaign] Invalid campaign.json: " + e.Message); }
        // DLC maps bring their own story (Resources/Data/Campaigns/*.json, "region" = map id).
        foreach (var c in DataPacks.LoadAs<CampaignFile>("Campaigns")) if (c != null && !string.IsNullOrEmpty(c.region) && c.chapters != null) regionFiles.Add(c);
        SelectForRegion();
    }

    /// <summary>Pick the campaign of the current map (the base campaign when the map has none).</summary>
    public void SelectForRegion()
    {
        file = baseFile;
        string id = Regions.CurrentId;
        foreach (var c in regionFiles) if (c.region == id) { file = c; break; }
        Chapters = file != null && file.chapters != null ? file.chapters : new CampaignChapter[0];
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
    private void OnEnable() { GameEvents.MissionCompleted += OnMissionDone; GameEvents.NewGameStarted += OnNewGame; GameEvents.RegionChanged += OnRegion; }
    private void OnDisable() { GameEvents.MissionCompleted -= OnMissionDone; GameEvents.NewGameStarted -= OnNewGame; GameEvents.RegionChanged -= OnRegion; }

    private void OnRegion(string regionId) { SelectForRegion(); SaveData.SetInt(SeenKey, 0); SaveData.SetInt(PaidKey, 0); Hide(); }

    private void OnNewGame() { SaveData.SetInt(SeenKey, 0); SaveData.SetInt(PaidKey, 0); Hide(); }

    // ------------------------------------------------------------------ chapter maths

    /// <summary>Chapter index (0-based) that a 0-based mission index belongs to.</summary>
    public int ChapterOfMission(int missionIndex)
    {
        int c = 0;
        for (int i = 0; i < Chapters.Length; i++) if (missionIndex + 1 >= Chapters[i].fromMission) c = i;
        return c;
    }

    /// <summary>First and last (exclusive) 0-based mission index of a chapter.</summary>
    public void Range(int chapter, out int first, out int end)
    {
        int total = MissionManager.Instance != null ? MissionManager.Instance.Missions.Length : 0;
        first = Mathf.Clamp(Chapters[chapter].fromMission - 1, 0, total);
        end = chapter + 1 < Chapters.Length ? Mathf.Clamp(Chapters[chapter + 1].fromMission - 1, first, total) : total;
    }

    /// <summary>0..1 progress of a chapter from the mission chain.</summary>
    public float ChapterProgress(int chapter)
    {
        var mm = MissionManager.Instance;
        if (mm == null || chapter < 0 || chapter >= Chapters.Length) return 0f;
        int first, end; Range(chapter, out first, out end);
        if (end <= first) return 1f;
        return Mathf.Clamp01((mm.Index - first) / (float)(end - first));
    }

    public int CurrentChapter => MissionManager.Instance != null ? ChapterOfMission(Mathf.Min(MissionManager.Instance.Index, Mathf.Max(0, MissionManager.Instance.Missions.Length - 1))) : 0;

    public static string Title(CampaignChapter c) => c == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(c.titleAr) ? c.titleAr : c.titleEn);
    public static string Intro(CampaignChapter c) => c == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(c.introAr) ? c.introAr : c.introEn);
    public static string Outro(CampaignChapter c) => c == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(c.outroAr) ? c.outroAr : c.outroEn);
    public string Narrator => file == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(file.narratorAr) ? file.narratorAr : file.narratorEn);

    // ------------------------------------------------------------------ events

    private void OnMissionDone(MissionDef m)
    {
        var mm = MissionManager.Instance;
        if (mm == null || Chapters.Length == 0) return;
        int doneIndex = mm.Index;                         // index already moved on to the next mission
        int chapterDone = ChapterOfMission(Mathf.Max(0, doneIndex - 1));
        int first, end; Range(chapterDone, out first, out end);
        if (doneIndex < end) return;                      // chapter not finished yet
        if (SaveData.GetInt(PaidKey, 0) > chapterDone) return;
        SaveData.SetInt(PaidKey, chapterDone + 1);
        int reward = Chapters[chapterDone].reward;
        if (reward > 0 && EconomyManager.Instance != null) EconomyManager.Instance.AddMoney(reward);
        bool last = chapterDone + 1 >= Chapters.Length;
        Show(ChapterCard(chapterDone, false, reward), 14f);
        if (last && UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.T("Campaign complete!"));
    }

    private void Update()
    {
        if (CardOpen)
        {
            if (Time.frameCount != shownFrame && (MenuNav.TutorialNext() || Time.unscaledTime >= closeAt)) Hide();
            return;
        }
        checkTimer -= Time.unscaledDeltaTime;
        if (checkTimer > 0f) return;
        checkTimer = 1f;
        // A new chapter starts: show its intro once (chapter 1 uses the opening story card).
        if (GameMenu.IsOpen || Time.timeScale == 0f || (UIManager.Instance != null && UIManager.Instance.IsUIOpened)) return;
        if (MissionManager.Instance == null || Chapters.Length == 0) return;
        int ch = CurrentChapter;
        int seen = SaveData.GetInt(SeenKey, 0);
        if (ch == 0) { if (seen < 1) SaveData.SetInt(SeenKey, 1); return; }
        if (ch + 1 <= seen) return;
        SaveData.SetInt(SeenKey, ch + 1);
        Show(ChapterCard(ch, true, 0), 16f);
    }

    /// <summary>Rich text of a chapter card (intro or closing).</summary>
    public string ChapterCard(int ch, bool intro, int reward)
    {
        var c = Chapters[ch];
        var sb = new StringBuilder(512);
        sb.Append("<size=70%><color=#FAC240><b>").Append(Loc.T("CHAPTER")).Append(' ').Append(ch + 1).Append(" / ").Append(Chapters.Length)
          .Append(intro ? "" : "  -  " + Loc.T("COMPLETE")).Append("</b></color></size>\n");
        sb.Append("<size=150%><b>").Append(Loc.Display(Title(c))).Append("</b></size>\n\n");
        sb.Append("<size=95%>").Append(Loc.Display(intro ? Intro(c) : Outro(c))).Append("</size>\n");
        if (intro) sb.Append("\n<size=80%><i><color=#E9D8B4>- ").Append(Loc.Display(Narrator)).Append("</color></i></size>");
        if (!intro && reward > 0) sb.Append("\n<color=#9BE07A>").Append(Loc.T("Chapter reward: +{0} SAR", reward)).Append("</color>");
        sb.Append("\n\n<size=70%><color=#FFFFFF99>").Append(GameInput.UsingGamepad ? Loc.T("[D-pad right] Continue") : Loc.T("[Enter] Continue")).Append("</color></size>");
        return Glyphs.Apply(sb.ToString());
    }

    // ------------------------------------------------------------------ card UI

    private void Build()
    {
        var canvas = MenuKit.Canvas("Campaign Card UI", transform, 82);
        cardRoot = canvas.gameObject;
        var dim = MenuKit.Rect("Dim", canvas.transform, typeof(Image));
        MenuKit.Stretch(dim);
        dim.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.02f, 0.6f);
        dim.GetComponent<Image>().raycastTarget = false;
        var panel = MenuKit.Rect("Card", canvas.transform, typeof(Image));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(820f, 440f);
        panel.GetComponent<Image>().color = ControlsTutorial.GreenDark;
        panel.GetComponent<Image>().raycastTarget = false;
        var stripe = MenuKit.Rect("Stripe", panel, typeof(Image));
        stripe.anchorMin = new Vector2(0f, 1f); stripe.anchorMax = new Vector2(1f, 1f); stripe.pivot = new Vector2(0.5f, 1f);
        stripe.sizeDelta = new Vector2(0f, 6f);
        stripe.GetComponent<Image>().color = ControlsTutorial.Gold;
        stripe.GetComponent<Image>().raycastTarget = false;
        cardText = MenuKit.Text(panel, 26, Color.white, "Text");
        var rt = cardText.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(48f, 32f); rt.offsetMax = new Vector2(-48f, -36f);
        cardText.alignment = TextAlignmentOptions.Center;
        var f = Loc.ArabicFont; if (f != null) cardText.font = f;
        cardRoot.SetActive(false);
    }

    public void Show(string text, float seconds)
    {
        if (cardRoot == null) Build();
        cardText.text = text;
        cardRoot.SetActive(true);
        closeAt = Time.unscaledTime + seconds;
        shownFrame = Time.frameCount;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
    }

    public void Hide() { if (cardRoot != null) cardRoot.SetActive(false); }

    /// <summary>Campaign page text for the pause menu.</summary>
    public string Overview()
    {
        var sb = new StringBuilder(512);
        int cur = CurrentChapter;
        bool allDone = MissionManager.Instance != null && MissionManager.Instance.AllDone;
        for (int i = 0; i < Chapters.Length; i++)
        {
            float p = ChapterProgress(i);
            bool done = allDone || i < cur || p >= 1f;
            string state = done ? "<color=#9BE07A>" + Loc.T("Done") + "</color>"
                : i == cur ? "<color=#FAC240>" + Mathf.RoundToInt(p * 100f) + "%</color>"
                : "<color=#FFFFFF66>" + Loc.T("Locked") + "</color>";
            sb.Append(i + 1).Append(".  ").Append(i <= cur || done ? Loc.Display(Title(Chapters[i])) : "<color=#FFFFFF66>" + Loc.Display(Title(Chapters[i])) + "</color>")
              .Append("   ").Append(state).Append('\n');
        }
        return sb.ToString();
    }
}

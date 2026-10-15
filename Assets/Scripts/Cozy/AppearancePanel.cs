using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// C = farmer look panel: 1 thobe/abaya, 2 colour, 3 shemagh/ghutra or headscarf. Applies the look to a third-person
/// player model if present and to the drivers in the vehicles; dresses the other character models (shopkeepers,
/// workers, buyers, villagers) in Saudi clothing too.
/// </summary>
[MazraaSystem(60)]
public class AppearancePanel : MonoBehaviour
{
    public static AppearancePanel Instance;
    public CozyPanel Panel { get; private set; }

    private void Awake() { Instance = this; Appearance.Load(); }

    private void Start()
    {
        Panel = new CozyPanel(transform, "Look UI", 640f);
        Appearance.Changed += OnChanged;
        Loc.OnLanguageChanged += Refresh;
        GameEvents.NewGameStarted += OnNewGame;
        Invoke(nameof(DressEveryone), 2.5f);   // after AmbientLife and the villagers are spawned
    }

    private void OnDestroy()
    {
        Appearance.Changed -= OnChanged;
        Loc.OnLanguageChanged -= Refresh;
        GameEvents.NewGameStarted -= OnNewGame;
        if (Instance == this) Instance = null;
    }

    // The look is a player setting: write it back after the new-game wipe.
    private void OnNewGame() => Appearance.Save();

    private void Update()
    {
        if (Panel == null) return;
        if (Panel.IsOpen)
        {
            if (Panel.CloseRequested || (CozyKeys.Down("c") && Time.frameCount != Panel.OpenedFrame)) { Panel.Close(); return; }
            int d = CozyKeys.Digit();
            if (d == 1) Appearance.ToggleStyle();
            else if (d == 2) Appearance.NextMain();
            else if (d == 3) Appearance.NextHead();
        }
        else if (CozyKeys.Free && CozyKeys.Down("c")) Open();
    }

    public void Open() { Panel.Open(); Refresh(); }

    private void OnChanged() { DressPlayer(); Refresh(); }

    private void Refresh()
    {
        if (Panel == null || !Panel.IsOpen) return;
        bool thobe = Appearance.CurrentStyle == OutfitStyle.Thobe;
        var lines = new List<string>
        {
            "1  " + Loc.Raw("Outfit") + ": " + Loc.Raw(thobe ? "Thobe" : "Abaya"),
            "2  " + Loc.Raw("Colour") + ": " + Loc.Raw(Appearance.MainName),
            "3  " + Loc.Raw(thobe ? "Head cloth" : "Headscarf") + ": " + Loc.Raw(Appearance.HeadName),
        };
        Panel.Show(Loc.Raw("Farmer look"), lines, Loc.Raw("[1-3] change   [C/Esc] close"));
    }

    /// <summary>Third-person player model (skinned mesh under the player) and the vehicle drivers get the player's look.</summary>
    public static void DressPlayer()
    {
        var p = CozyFx.Player;
        if (p != null && p.GetComponentInChildren<SkinnedMeshRenderer>() != null) Appearance.ApplyTo(p.gameObject);
        foreach (var a in FindObjectsByType<Actor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (a.mode == ActorMode.Driver) Appearance.ApplyTo(a.gameObject);
    }

    /// <summary>Dress the character models spawned by AmbientLife (and the villagers) in thobes/shemaghs and abayas.</summary>
    public void DressEveryone()
    {
        DressPlayer();
        int i = 0;
        foreach (var a in FindObjectsByType<Actor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (a.mode == ActorMode.Driver || !a.name.Contains("Characters/") || a.name.StartsWith("Villager")) continue;
            DressNpc(a.gameObject, a.name.Contains("female"), i++);
        }
    }

    public static void DressNpc(GameObject go, bool female, int seed)
    {
        if (female) Appearance.ApplyOutfit(go, OutfitStyle.Abaya, Appearance.AbayaColors[seed % 2], Appearance.ScarfColors[0]);
        else Appearance.ApplyOutfit(go, OutfitStyle.Thobe, Appearance.ThobeColors[seed % 3 == 2 ? 1 : 0], Appearance.ShemaghColors[seed % 2]);
    }
}

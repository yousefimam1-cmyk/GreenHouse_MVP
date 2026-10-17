using System;
using UnityEngine;

public enum OutfitStyle { Thobe, Abaya }

/// <summary>
/// The farmer's look (thobe + shemagh/ghutra, or abaya + headscarf, with colours). Saved under mazraa.cozy.look.*
/// and kept on a new game (it is a player setting). Other features can read it and call ApplyTo on any character model;
/// Changed fires when the player edits it in the look panel (C).
/// </summary>
public static class Appearance
{
    public static readonly Color[] ThobeColors =
    {
        new Color(0.97f, 0.96f, 0.93f), new Color(0.93f, 0.88f, 0.76f), new Color(0.62f, 0.63f, 0.64f),
        new Color(0.48f, 0.37f, 0.27f), new Color(0.16f, 0.2f, 0.32f),
    };
    public static readonly string[] ThobeColorNames = { "White", "Cream", "Grey", "Brown", "Navy" };
    public static readonly Color[] AbayaColors =
    {
        new Color(0.06f, 0.06f, 0.07f), new Color(0.1f, 0.12f, 0.26f), new Color(0.35f, 0.25f, 0.18f),
        new Color(0.42f, 0.08f, 0.15f), new Color(0.08f, 0.22f, 0.16f),
    };
    public static readonly string[] AbayaColorNames = { "Black", "Navy", "Brown", "Burgundy", "Dark green" };
    // Head cloth: thobe -> red shemagh / white ghutra; abaya -> headscarf colours
    public static readonly Color[] ShemaghColors = { new Color(0.78f, 0.1f, 0.12f), new Color(0.97f, 0.97f, 0.95f) };
    public static readonly string[] ShemaghNames = { "Red shemagh", "White ghutra" };
    public static readonly Color[] ScarfColors = { new Color(0.06f, 0.06f, 0.07f), new Color(0.45f, 0.45f, 0.48f), new Color(0.9f, 0.85f, 0.74f) };
    public static readonly string[] ScarfNames = { "Black", "Grey", "Cream" };

    public static OutfitStyle Style { get; private set; }
    public static int MainIndex { get; private set; }
    public static int HeadIndex { get; private set; }
    public static event Action Changed;

    private static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { loaded = false; Changed = null; Style = OutfitStyle.Thobe; MainIndex = 0; HeadIndex = 0; }

    public static void Load()
    {
        loaded = true;
        Style = (OutfitStyle)Mathf.Clamp(SaveData.GetInt("mazraa.cozy.look.style", 0), 0, 1);
        MainIndex = SaveData.GetInt("mazraa.cozy.look.main", 0);
        HeadIndex = SaveData.GetInt("mazraa.cozy.look.head", 0);
        Clamp();
    }

    private static void EnsureLoaded() { if (!loaded) Load(); }

    public static Color MainColor { get { EnsureLoaded(); return Style == OutfitStyle.Thobe ? ThobeColors[MainIndex] : AbayaColors[MainIndex]; } }
    public static Color HeadColor { get { EnsureLoaded(); return Style == OutfitStyle.Thobe ? ShemaghColors[HeadIndex] : ScarfColors[HeadIndex]; } }
    public static string MainName { get { EnsureLoaded(); return Style == OutfitStyle.Thobe ? ThobeColorNames[MainIndex] : AbayaColorNames[MainIndex]; } }
    public static string HeadName { get { EnsureLoaded(); return Style == OutfitStyle.Thobe ? ShemaghNames[HeadIndex] : ScarfNames[HeadIndex]; } }
    public static OutfitStyle CurrentStyle { get { EnsureLoaded(); return Style; } }

    public static void Set(OutfitStyle style, int main, int head)
    {
        loaded = true;
        Style = style; MainIndex = main; HeadIndex = head;
        Clamp();
        Save();
        if (Changed != null) Changed();
    }

    public static void ToggleStyle() { EnsureLoaded(); Set(Style == OutfitStyle.Thobe ? OutfitStyle.Abaya : OutfitStyle.Thobe, 0, 0); }
    public static void NextMain() { EnsureLoaded(); Set(Style, MainIndex + 1, HeadIndex); }
    public static void NextHead() { EnsureLoaded(); Set(Style, MainIndex, HeadIndex + 1); }

    private static void Clamp()
    {
        int mains = Style == OutfitStyle.Thobe ? ThobeColors.Length : AbayaColors.Length;
        int heads = Style == OutfitStyle.Thobe ? ShemaghColors.Length : ScarfColors.Length;
        MainIndex = ((MainIndex % mains) + mains) % mains;
        HeadIndex = ((HeadIndex % heads) + heads) % heads;
    }

    /// <summary>Write to the save (also called after a new game wipes the save, so the look is kept).</summary>
    public static void Save()
    {
        SaveData.SetInt("mazraa.cozy.look.style", (int)Style);
        SaveData.SetInt("mazraa.cozy.look.main", MainIndex);
        SaveData.SetInt("mazraa.cozy.look.head", HeadIndex);
        SaveData.Save();
    }

    /// <summary>Apply the player's look to a character model (tint + head cloth).</summary>
    public static void ApplyTo(GameObject character) { EnsureLoaded(); ApplyOutfit(character, CurrentStyle, MainColor, HeadColor); }

    public const string HeadwearName = "CozyHeadwear";

    /// <summary>Dress any character model: tints its materials towards the robe colour, adds a draped thobe / abaya and a
    /// shemagh/ghutra with agal or a headscarf (graphics makeover: real cloth meshes and fabric, see NpcWardrobe).</summary>
    public static void ApplyOutfit(GameObject character, OutfitStyle style, Color robe, Color head)
    {
        if (character == null) return;
        NpcWardrobe.Dress(character, style == OutfitStyle.Abaya ? Outfit.Abaya : Outfit.Thobe, robe, head);
    }
}

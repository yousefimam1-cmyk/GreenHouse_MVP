using System.Collections.Generic;
using UnityEngine;

public enum MinimapIconKind { Vehicle, Greenhouse, Field, Pen, Shop, LoadingBay, Market, Custom }

/// <summary>
/// Shows its GameObject on the minimap and the full map (N). The Minimap adds these to the farm's buildings,
/// fields and vehicles automatically; features can add their own:
/// <code>MinimapIcon.Add(gameObject, MinimapIconKind.Custom, new Color(0.3f, 0.6f, 1f), "F");</code>
/// </summary>
public class MinimapIcon : MonoBehaviour
{
    public static readonly List<MinimapIcon> All = new List<MinimapIcon>();

    public MinimapIconKind kind = MinimapIconKind.Custom;
    public Color color = Color.white;
    public string letter = "";          // optional short label drawn on the icon
    public float size = 18f;            // pixels on the minimap
    public bool showOnMinimap = true;   // false = only on the full map

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => All.Clear();

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    public static MinimapIcon Add(GameObject go, MinimapIconKind kind, Color color, string letter = "", float size = 18f)
    {
        if (go == null) return null;
        var icon = go.GetComponent<MinimapIcon>();
        if (icon == null) icon = go.AddComponent<MinimapIcon>();
        icon.kind = kind; icon.color = color; icon.letter = letter ?? ""; icon.size = size;
        if (!All.Contains(icon) && icon.isActiveAndEnabled) All.Add(icon);
        return icon;
    }

    public static Color DefaultColor(MinimapIconKind k)
    {
        switch (k)
        {
            case MinimapIconKind.Vehicle: return new Color(0.95f, 0.3f, 0.25f);
            case MinimapIconKind.Greenhouse: return new Color(0.55f, 0.9f, 0.95f);
            case MinimapIconKind.Field: return new Color(0.75f, 0.55f, 0.25f);
            case MinimapIconKind.Pen: return new Color(0.95f, 0.85f, 0.6f);
            case MinimapIconKind.Shop: return new Color(1f, 0.55f, 0.15f);
            case MinimapIconKind.LoadingBay: return new Color(0.35f, 0.85f, 0.35f);
            case MinimapIconKind.Market: return new Color(1f, 0.8f, 0.2f);
            default: return Color.white;
        }
    }
}

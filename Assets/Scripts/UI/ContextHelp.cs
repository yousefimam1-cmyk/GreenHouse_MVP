using System.Collections.Generic;
using System.Text;

/// <summary>
/// Farming-Simulator-style context help box (top-left of the HUD): the keys that matter right now, for the device in
/// use - on foot, in the truck, or in the tractor with its implement (attach / lower / fold, with the implement in reach
/// named). Lines are translated and already carry the device's button labels (Glyphs). Settings > Game > Help hints hides it.
/// </summary>
public static class ContextHelp
{
    public enum Context { OnFoot, Truck, Tractor }

    public static Context Current => Vehicle.Current is Tractor ? Context.Tractor : Vehicle.Current != null ? Context.Truck : Context.OnFoot;

    /// <summary>Title of the box for the current context (translated).</summary>
    public static string Title()
    {
        switch (Current)
        {
            case Context.Tractor: return UiText.T("Tractor");
            case Context.Truck: return UiText.T("Farm truck");
            default: return UiText.T("On foot");
        }
    }

    public static List<string> Lines()
    {
        var lines = new List<string>();
        bool pad = GameInput.UsingGamepad;
        var v = Vehicle.Current;
        if (v is Tractor t)
        {
            if (t.ActiveImplement == Implement.None)
            {
                var near = ParkedImplement.Nearest(t.HitchPoint, Tractor.AttachRange);
                if (near != null) lines.Add(Hi(G(pad ? "Hold [Q] Attach" : "[Q] Attach") + ": " + Loc.Display(Tractor.ImplementName(near.kind))));
                else lines.Add(G("Reverse up to an implement in the yard to attach it"));
            }
            else
            {
                string name = Loc.Display(Tractor.ImplementName(t.ActiveImplement));
                lines.Add((t.ImplementLowered ? G("[V] Raise") : Hi(G("[V] Lower"))) + ": " + name);
                if (!pad) lines.Add(G(t.ImplementFolded ? "Hold [V] Unfold" : "Hold [V] Fold"));
                lines.Add(G(pad ? "Hold [Q] Detach" : "[Q] Detach"));
            }
            AddDriving(lines, pad);
        }
        else if (v != null)
        {
            lines.Add(G("[Space] Brake"));
            AddDriving(lines, pad);
        }
        else
        {
            lines.Add(G("[E] Interact / get in"));
            if (!pad) lines.Add(G("[Tab] Get into the nearest vehicle"));
            lines.Add(G("[N] Map   [P] Menu"));
            lines.Add(G("[I] Inventory   [O] Stats   [V] View"));
            lines.Add(G("[H] Help   [L] Arabic   [T] Training"));
        }
        return lines;
    }

    private static void AddDriving(List<string> lines, bool pad)
    {
        var v = Vehicle.Current;
        if (v != null && !v.Drive.EngineOn) lines.Add(Hi(G("[F] Start engine")));
        lines.Add(G("[Y] Cruise   [Plus] / [Minus] Speed"));
        lines.Add(G("[Z] 4WD / diff lock   [Slash] GPS line"));
        if (!pad) lines.Add(G("[Tab] Switch vehicle"));
        lines.Add(G("[E] Get out   [R] Reset"));
        if (v != null && v.AtWorkshop && v.NeedsRepair) lines.Add(Hi(G("[R] Repair")));
    }

    private static string G(string key) => Glyphs.Apply(UiText.T(key));
    private static string Hi(string s) => "<color=#FFC24D>" + s + "</color>";

    /// <summary>
    /// The one short prompt for the vehicle you are in, or null: engine off, repair at the workshop, an implement in reach.
    /// On foot the prompt is the interactable under the crosshair (InteractionController.CurrentPrompt).
    /// </summary>
    public static string Prompt()
    {
        var v = Vehicle.Current;
        if (v == null) return null;
        if (!v.Drive.EngineOn) return G("[F] Start engine");
        if (v.AtWorkshop && v.NeedsRepair) return G("[R] Repair");
        if (v is Tractor t && t.ActiveImplement == Implement.None)
        {
            var near = ParkedImplement.Nearest(t.HitchPoint, Tractor.AttachRange);
            if (near != null) return G(GameInput.UsingGamepad ? "Hold [Q] Attach" : "[Q] Attach") + ": " + Loc.Display(Tractor.ImplementName(near.kind));
        }
        return null;
    }

    /// <summary>The full controls list text (no longer on the HUD).</summary>
    public static string Text()
    {
        var sb = new StringBuilder();
        sb.Append("<color=#8CD95A><b>").Append(Title()).Append("</b></color>");
        foreach (var l in Lines()) sb.Append('\n').Append(l);
        return sb.ToString();
    }
}

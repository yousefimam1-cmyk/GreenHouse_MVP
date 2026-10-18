using System.Text;

/// <summary>
/// Button labels for the device in use. Prompts and HUD hints are written with keyboard keys in brackets
/// ("[E] Harvest"); Apply() swaps each known key for the current label: the rebound key on keyboard,
/// A / B / X / Y on Xbox-style pads, or the PlayStation shapes (cross, circle, square, triangle).
/// Works on shaped Arabic text too, because "[E]" keeps its order after shaping.
/// </summary>
public static class Glyphs
{
    public enum Pad { South, East, West, North, LB, RB, LT, RT, Start, Select, LStick, RStick, DPad }

    public static string PadLabel(Pad b, bool playStation)
    {
        switch (b)
        {
            case Pad.South: return playStation ? "<color=#7CB2E8>✕</color>" : "<color=#6BCB5B>A</color>";
            case Pad.East: return playStation ? "<color=#FF6B6B>○</color>" : "<color=#FF6B6B>B</color>";
            case Pad.West: return playStation ? "<color=#E89BD0>□</color>" : "<color=#5BA8FF>X</color>";
            case Pad.North: return playStation ? "<color=#4FD1A5>△</color>" : "<color=#FFD24D>Y</color>";
            case Pad.LB: return playStation ? "L1" : "LB";
            case Pad.RB: return playStation ? "R1" : "RB";
            case Pad.LT: return playStation ? "L2" : "LT";
            case Pad.RT: return playStation ? "R2" : "RT";
            case Pad.Start: return playStation ? "OPTIONS" : "MENU";
            case Pad.Select: return playStation ? "SHARE" : "VIEW";
            case Pad.LStick: return playStation ? "L3" : "LS";
            case Pad.RStick: return playStation ? "R3" : "RS";
            default: return playStation ? "D-PAD" : "D-PAD";
        }
    }

    public static string PadLabel(Pad b) => PadLabel(b, GameInput.IsPlayStation);

    /// <summary>Label (without brackets) for an action on the current device.</summary>
    public static string For(GameAction a)
    {
        if (!GameInput.UsingGamepad) return GameInput.KeyName(a);
        return PadFor(a, GameInput.IsPlayStation) ?? GameInput.KeyName(a);
    }

    /// <summary>Gamepad label of an action (null when it has no pad button).</summary>
    public static string PadFor(GameAction a, bool ps)
    {
        switch (a)
        {
            case GameAction.Interact: return PadLabel(Pad.South, ps);
            case GameAction.VehicleAction: return PadLabel(Pad.West, ps);
            case GameAction.Implement: return PadLabel(Pad.North, ps);
            case GameAction.Brake: return PadLabel(Pad.LT, ps);
            case GameAction.Pause: return PadLabel(Pad.Start, ps);
            case GameAction.Map: return PadLabel(Pad.Select, ps);
            case GameAction.View: return PadLabel(Pad.RStick, ps);
            case GameAction.EngineToggle: return PadLabel(Pad.LStick, ps);
            case GameAction.GearUp: return PadLabel(Pad.RB, ps);
            case GameAction.GearDown: return PadLabel(Pad.LB, ps);
            case GameAction.Cruise: return PadLabel(Pad.DPad, ps) + "↑";
            case GameAction.CruiseFaster: return PadLabel(Pad.DPad, ps) + "→";
            case GameAction.CruiseSlower: return PadLabel(Pad.DPad, ps) + "←";
            case GameAction.Traction: return PadLabel(Pad.DPad, ps) + "↓";
            case GameAction.LaneAssist: return PadLabel(Pad.RStick, ps);
            case GameAction.Crouch: return PadLabel(Pad.LStick, ps);
            case GameAction.Carry: return PadLabel(Pad.DPad, ps) + "↑";
            case GameAction.UseTool: return PadLabel(Pad.RT, ps);
            case GameAction.ToolPrev: return PadLabel(Pad.LB, ps);
            case GameAction.ToolNext: return PadLabel(Pad.RB, ps);
            case GameAction.ImplementLower: return PadLabel(Pad.North, ps);   // tap Y (TractorHitch)
            default: return null;
        }
    }

    // Bracketed keyboard tokens used in texts -> action (or a fixed gamepad button for non-rebindable keys).
    // F, Ctrl and Z are shared: in a vehicle they mean engine / gear down / 4WD, on foot pick up / crouch / hand tool.
    private static readonly string[] Tokens = { "E", "R", "Q", "Space", "P", "N", "I", "O", "V", "Esc", "Enter", "Backspace", "LB", "RB",
        "F", "Shift", "Ctrl", "Y", "Plus", "Minus", "Z", "Slash", ",", ".", "Tab" };

    private static string Replacement(string token)
    {
        bool pad = GameInput.UsingGamepad;
        switch (token)
        {
            case "E": return For(GameAction.Interact);
            case "R": return For(GameAction.VehicleAction);
            case "Q": return For(GameAction.Implement);
            case "Space": return For(GameAction.Brake);
            case "P": return For(GameAction.Pause);
            case "N": return For(GameAction.Map);
            case "I": return For(GameAction.Inventory);
            case "O": return For(GameAction.Stats);
            case "V": return For(Vehicle.Current != null ? GameAction.ImplementLower : GameAction.View);   // in a vehicle V lowers / raises the implement
            case "Tab": return For(GameAction.SwitchVehicle);
            case "F": return For(Vehicle.Current != null ? GameAction.EngineToggle : GameAction.Carry);
            case "Shift": return For(GameAction.GearUp);
            case "Ctrl": return For(Vehicle.Current != null ? GameAction.GearDown : GameAction.Crouch);
            case "Y": return For(GameAction.Cruise);
            case "Plus": return For(GameAction.CruiseFaster);
            case "Minus": return For(GameAction.CruiseSlower);
            case "Z": return For(Vehicle.Current != null ? GameAction.Traction : GameAction.UseTool);
            case "Slash": return For(GameAction.LaneAssist);
            case "Esc": return pad ? PadLabel(Pad.East) : null;
            case "Enter": return pad ? PadLabel(Pad.South) : null;
            case "Backspace": return pad ? PadLabel(Pad.DPad) + "↓" : null;
            case "LB": return pad ? PadLabel(Pad.LB) : null;
            case "RB": return pad ? PadLabel(Pad.RB) : null;
            case ",": return For(GameAction.ToolPrev);
            case ".": return For(GameAction.ToolNext);
            default: return null;
        }
    }

    /// <summary>Replace "[E]"-style keys with the current device's labels. Unknown keys are left as they are.</summary>
    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('[') < 0) return text;
        var sb = new StringBuilder(text.Length + 16);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '[')
            {
                int close = text.IndexOf(']', i + 1);
                if (close > i + 1 && close - i <= 12)
                {
                    string token = text.Substring(i + 1, close - i - 1);
                    string rep = null;
                    if (System.Array.IndexOf(Tokens, token) >= 0) rep = Replacement(token);
                    if (rep != null && rep != token) { sb.Append('[').Append(rep).Append(']'); i = close + 1; continue; }
                }
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}

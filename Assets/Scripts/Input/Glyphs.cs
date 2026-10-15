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
        switch (a)
        {
            case GameAction.Interact: return PadLabel(Pad.South);
            case GameAction.VehicleAction: return PadLabel(Pad.West);
            case GameAction.Implement: return PadLabel(Pad.North);
            case GameAction.Brake: return PadLabel(Pad.LT);
            case GameAction.Pause: return PadLabel(Pad.Start);
            case GameAction.Map: return PadLabel(Pad.Select);
            case GameAction.View: return PadLabel(Pad.RStick);
            default: return GameInput.KeyName(a);
        }
    }

    // Bracketed keyboard tokens used in texts -> action (or a fixed gamepad button for non-rebindable keys).
    private static readonly string[] Tokens = { "E", "R", "Q", "Space", "P", "N", "I", "O", "V", "Esc", "Enter", "Backspace", "LB", "RB" };

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
            case "V": return For(GameAction.View);
            case "Esc": return pad ? PadLabel(Pad.East) : null;
            case "Enter": return pad ? PadLabel(Pad.South) : null;
            case "Backspace": return pad ? PadLabel(Pad.DPad) + "↓" : null;
            case "LB": return pad ? PadLabel(Pad.LB) : null;
            case "RB": return pad ? PadLabel(Pad.RB) : null;
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

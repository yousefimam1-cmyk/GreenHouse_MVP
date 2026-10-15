using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Menu navigation shared by every panel built in code: arrows / W S / D-pad / left stick move,
/// Enter / Space / A (Cross) confirm, Esc / B (Circle) back, LB RB page, number keys for shortcuts.
/// One place reads these keys so panels never fight over them. Uses unscaled time (works while paused).
/// </summary>
public static class MenuNav
{
    private static int frame = -1;
    private static int vert, horiz;
    private static float stickRepeatV, stickRepeatH;
    private static int lastStickV, lastStickH;

    private static bool K(KeyControl k) => k != null && k.wasPressedThisFrame;
    private static bool B(ButtonControl b) => b != null && b.wasPressedThisFrame;

    private static void Sample()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        vert = 0; horiz = 0;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (K(kb.upArrowKey) || K(kb.wKey)) vert = -1;
            if (K(kb.downArrowKey) || K(kb.sKey)) vert = 1;
            if (K(kb.leftArrowKey) || K(kb.aKey)) horiz = -1;
            if (K(kb.rightArrowKey) || K(kb.dKey)) horiz = 1;
        }
        var pad = Gamepad.current;
        if (pad != null)
        {
            if (B(pad.dpad.up)) vert = -1;
            if (B(pad.dpad.down)) vert = 1;
            if (B(pad.dpad.left)) horiz = -1;
            if (B(pad.dpad.right)) horiz = 1;
            var s = pad.leftStick.ReadValue();
            int sv = s.y > 0.6f ? -1 : s.y < -0.6f ? 1 : 0;
            int sh = s.x > 0.6f ? 1 : s.x < -0.6f ? -1 : 0;
            float dt = Time.unscaledDeltaTime;
            if (sv != 0 && (sv != lastStickV || (stickRepeatV -= dt) <= 0f)) { if (vert == 0) vert = sv; stickRepeatV = sv != lastStickV ? 0.4f : 0.15f; }
            if (sh != 0 && (sh != lastStickH || (stickRepeatH -= dt) <= 0f)) { if (horiz == 0) horiz = sh; stickRepeatH = sh != lastStickH ? 0.4f : 0.15f; }
            lastStickV = sv; lastStickH = sh;
        }
    }

    /// <summary>-1 up, +1 down, 0 none (this frame).</summary>
    public static int Vertical() { Sample(); return vert; }
    /// <summary>-1 left, +1 right, 0 none.</summary>
    public static int Horizontal() { Sample(); return horiz; }

    public static bool Confirm()
    {
        var kb = Keyboard.current; var pad = Gamepad.current;
        return (kb != null && (K(kb.enterKey) || K(kb.numpadEnterKey) || K(kb.spaceKey))) || (pad != null && B(pad.buttonSouth));
    }

    public static bool Back()
    {
        var kb = Keyboard.current; var pad = Gamepad.current;
        return (kb != null && K(kb.escapeKey)) || (pad != null && B(pad.buttonEast));
    }

    /// <summary>Tab (keyboard only) also closes the game's panels.</summary>
    public static bool BackOrTab()
    {
        var kb = Keyboard.current;
        return Back() || (kb != null && K(kb.tabKey));
    }

    public static bool PageNext() { var p = Gamepad.current; return p != null && B(p.rightShoulder); }
    public static bool PagePrev() { var p = Gamepad.current; return p != null && B(p.leftShoulder); }

    /// <summary>Number key pressed this frame as a row index (1 = 0 ... 9 = 8, 0 = 9), or -1.</summary>
    public static int Digit()
    {
        var kb = Keyboard.current;
        if (kb == null) return -1;
        KeyControl[] keys = { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key, kb.digit0Key };
        for (int i = 0; i < keys.Length; i++) if (K(keys[i])) return i;
        return -1;
    }

    /// <summary>Tutorial overlay: Enter / D-pad right = next hint.</summary>
    public static bool TutorialNext()
    {
        var kb = Keyboard.current; var pad = Gamepad.current;
        return (kb != null && K(kb.enterKey)) || (pad != null && B(pad.dpad.right));
    }

    /// <summary>Tutorial overlay: Backspace / D-pad down = close.</summary>
    public static bool TutorialClose()
    {
        var kb = Keyboard.current; var pad = Gamepad.current;
        return (kb != null && K(kb.backspaceKey)) || (pad != null && B(pad.dpad.down));
    }
}

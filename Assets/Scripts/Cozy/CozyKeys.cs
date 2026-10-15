using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// All keyboard reads of the cozy features in one place (C look, J book (journal), K learn, X place decor, 0-4 panel choices,
/// Up/Down in panels). Only one cozy panel is open at a time, so the panel keys never clash.
/// </summary>
public static class CozyKeys
{
    public static bool Down(string key)
    {
        var kb = Keyboard.current;
        if (kb == null) return false;
        switch (key)
        {
            case "c": return kb.cKey != null && kb.cKey.wasPressedThisFrame;
            case "j": return kb.jKey != null && kb.jKey.wasPressedThisFrame;
            case "k": return kb.kKey != null && kb.kKey.wasPressedThisFrame;
            case "x": return kb.xKey != null && kb.xKey.wasPressedThisFrame;
            case "e": return kb.eKey != null && kb.eKey.wasPressedThisFrame;
            case "esc": return (kb.escapeKey != null && kb.escapeKey.wasPressedThisFrame) || (kb.tabKey != null && kb.tabKey.wasPressedThisFrame);
            case "0": return kb.digit0Key != null && kb.digit0Key.wasPressedThisFrame;
            case "1": return kb.digit1Key != null && kb.digit1Key.wasPressedThisFrame;
            case "2": return kb.digit2Key != null && kb.digit2Key.wasPressedThisFrame;
            case "3": return kb.digit3Key != null && kb.digit3Key.wasPressedThisFrame;
            case "4": return kb.digit4Key != null && kb.digit4Key.wasPressedThisFrame;
            case "up": return kb.upArrowKey != null && kb.upArrowKey.wasPressedThisFrame;
            case "down": return kb.downArrowKey != null && kb.downArrowKey.wasPressedThisFrame;
        }
        return false;
    }

    /// <summary>1..4 pressed this frame, else 0.</summary>
    public static int Digit()
    {
        if (Down("1")) return 1;
        if (Down("2")) return 2;
        if (Down("3")) return 3;
        if (Down("4")) return 4;
        return 0;
    }

    /// <summary>True when gameplay keys may open a cozy panel (no other panel open, not paused).</summary>
    public static bool Free => Time.timeScale > 0f && (UIManager.Instance == null || !UIManager.Instance.IsUIOpened) && UIManager.PanelClosedFrame != Time.frameCount;
}

using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Connects PlayStation / Xbox controllers and the Settings menu to the StarterAssets first-person controller.
/// The StarterAssets PlayerInput already moves with the left stick and looks with the right stick (Gamepad scheme);
/// this adds: mouse / stick sensitivity and invert Y (re-scales StarterAssetsInputs.look each frame),
/// A / Cross not jumping while it is used to interact or confirm a menu, device tracking for button glyphs,
/// and applying the saved audio / graphics settings. Runs before the controller's Update.
/// </summary>
[MazraaSystem(-90)]
[DefaultExecutionOrder(-50)]
public class GamepadBridge : MonoBehaviour
{
    private StarterAssetsInputs inputs;
    private PlayerInput playerInput;
    private InputAction lookAction;
    private float findTimer;

    private void Start()
    {
        GameSettings.Changed += GameSettings.Apply;
        GameSettings.Apply();
    }

    private void OnDestroy() => GameSettings.Changed -= GameSettings.Apply;

    private void Update()
    {
        GameInput.TrackDevice();
        if (inputs == null)
        {
            findTimer -= Time.unscaledDeltaTime;
            if (findTimer > 0f) return;
            findTimer = 1f;
            inputs = FindAnyObjectByType<StarterAssetsInputs>();
            if (inputs == null) return;
            playerInput = inputs.GetComponent<PlayerInput>();
            lookAction = playerInput != null && playerInput.actions != null ? playerInput.actions.FindAction("Look") : null;
        }
        if (!inputs.isActiveAndEnabled) return;
        bool uiBlocked = (UIManager.Instance != null && UIManager.Instance.IsUIOpened) || Time.timeScale == 0f || Time.frameCount - UIManager.PanelClosedFrame < 3;
        FeedLook(uiBlocked);
        SuppressJump(uiBlocked);
    }

    /// <summary>Look value the controller should use: the action value times the sensitivity, Y flipped when inverted.</summary>
    public static Vector2 ScaleLook(Vector2 raw, bool gamepad, float mouseSens, float stickSens, bool invertY)
    {
        float k = gamepad ? stickSens : mouseSens;
        return new Vector2(raw.x * k, raw.y * k * (invertY ? -1f : 1f));
    }

    private void FeedLook(bool uiBlocked)
    {
        if (lookAction == null || uiBlocked || !inputs.cursorInputForLook) return;
        float ms = GameSettings.MouseSensitivity, ss = GameSettings.StickSensitivity; bool inv = GameSettings.InvertY;
        bool dz = Mathf.Abs(GameSettings.StickDeadZone - 0.1f) > 0.001f;   // Settings > Controls > Stick dead zone (0.1 = Input System default feel)
        if (Mathf.Abs(ms - 1f) < 0.001f && Mathf.Abs(ss - 1f) < 0.001f && !inv && !dz) return; // defaults: leave StarterAssets untouched
        bool pad = playerInput != null && playerInput.currentControlScheme != "KeyboardMouse";
        Vector2 raw = lookAction.ReadValue<Vector2>();
        if (pad) raw = GameSettings.ApplyDeadZone(raw);
        inputs.LookInput(ScaleLook(raw, pad, ms, ss, inv));
    }

    // A / Cross is both StarterAssets' Jump and our Interact / Confirm: don't hop when it opens or confirms something.
    private void SuppressJump(bool uiBlocked)
    {
        var pad = Gamepad.current;
        if (pad == null || !pad.buttonSouth.isPressed) return;
        if (uiBlocked || InteractionController.HasTarget)
            inputs.SendMessage("JumpInput", false, SendMessageOptions.DontRequireReceiver);
    }
}

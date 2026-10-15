// Stubs for packages (TMP, uGUI, Input System, URP, DOTween, StarterAssets) + Unity 6 renames, compiled against real UnityEngine 2021.3 modules.
using System;
namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.EventSystems.UIBehaviour { public Color color; public bool raycastTarget; public RectTransform rectTransform => (RectTransform)transform; }
    public class Image : Graphic { }
    public class Button : UnityEngine.EventSystems.UIBehaviour { public bool interactable; public UnityEngine.Events.UnityEvent onClick = new UnityEngine.Events.UnityEvent(); }
    public class LayoutElement : UnityEngine.EventSystems.UIBehaviour { public float minHeight; public bool ignoreLayout; }
    public class GraphicRaycaster : UnityEngine.EventSystems.UIBehaviour { }
    public class CanvasScaler : UnityEngine.EventSystems.UIBehaviour { public enum ScaleMode { ScaleWithScreenSize } public ScaleMode uiScaleMode; public Vector2 referenceResolution; public float matchWidthOrHeight; }
    public class VerticalLayoutGroup : UnityEngine.EventSystems.UIBehaviour { public RectOffset padding; public float spacing; public bool childControlWidth, childControlHeight, childForceExpandWidth, childForceExpandHeight; }
    public class ContentSizeFitter : UnityEngine.EventSystems.UIBehaviour { public enum FitMode { Unconstrained, PreferredSize } public FitMode verticalFit; }
}
namespace UnityEngine.EventSystems { public class UIBehaviour : MonoBehaviour { } }
namespace TMPro
{
    public class TMP_FontAsset : UnityEngine.ScriptableObject { public static TMP_FontAsset CreateFontAsset(UnityEngine.Font f) => null; }
    public enum TextAlignmentOptions { TopLeft, TopRight, Top, Center, Left, Right, MidlineLeft, MidlineRight }
    public enum TextWrappingModes { NoWrap, Normal }
    [Flags] public enum FontStyles { Normal = 0, Bold = 1 }
    public class TMP_Text : UnityEngine.UI.Graphic { public string text; public TMP_FontAsset font; public float fontSize; public float alpha; public bool richText; public TextAlignmentOptions alignment; public TextWrappingModes textWrappingMode; public FontStyles fontStyle; }
    public class TextMeshProUGUI : TMP_Text { }
    public class TextMeshPro : TMP_Text { }
}
namespace UnityEngine.InputSystem
{
    public enum Key { Digit0, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9 }
    public class KeyControl { public bool wasPressedThisFrame, isPressed; }
    public class Keyboard
    {
        public static Keyboard current;
        public KeyControl aKey, bKey, cKey, dKey, eKey, fKey, gKey, hKey, iKey, jKey, kKey, lKey, mKey, nKey, oKey, pKey, qKey, rKey, sKey, tKey, uKey, vKey, wKey, xKey, yKey, zKey, f1Key, f2Key, f3Key, f4Key, f5Key, f6Key, f7Key, f8Key, f9Key, f10Key, f11Key, f12Key, digit0Key, digit1Key, digit2Key, digit3Key, digit4Key, digit5Key, digit6Key, digit7Key, digit8Key, digit9Key, enterKey, escapeKey, tabKey, spaceKey, backspaceKey, upArrowKey, downArrowKey, leftArrowKey, rightArrowKey, leftShiftKey, rightShiftKey, leftCtrlKey, numpadEnterKey, pageUpKey, pageDownKey, homeKey, endKey, deleteKey, insertKey, minusKey, equalsKey, commaKey, periodKey, slashKey, backquoteKey;
        public KeyControl this[Key k] => null;
    }
    public class ButtonControl { public bool wasPressedThisFrame, isPressed, wasReleasedThisFrame; public float value; public float ReadValue() => value; }
    public class StickControl { public UnityEngine.Vector2 value; public UnityEngine.Vector2 ReadValue() => value; }
    public class InputDevice { public string displayName = "", name = "", description_product = ""; }
    public class Gamepad : InputDevice
    {
        public static Gamepad current;
        public static Gamepad[] all = new Gamepad[0];
        public ButtonControl buttonSouth = new ButtonControl(), buttonEast = new ButtonControl(), buttonWest = new ButtonControl(), buttonNorth = new ButtonControl(),
            leftShoulder = new ButtonControl(), rightShoulder = new ButtonControl(), leftTrigger = new ButtonControl(), rightTrigger = new ButtonControl(),
            startButton = new ButtonControl(), selectButton = new ButtonControl(), leftStickButton = new ButtonControl(), rightStickButton = new ButtonControl();
        public StickControl leftStick = new StickControl(), rightStick = new StickControl();
        public DpadControl dpad = new DpadControl();
    }
    public class DpadControl { public ButtonControl up = new ButtonControl(), down = new ButtonControl(), left = new ButtonControl(), right = new ButtonControl(); public UnityEngine.Vector2 ReadValue() => UnityEngine.Vector2.zero; }
    public class Mouse : InputDevice { public static Mouse current; public ButtonControl leftButton = new ButtonControl(), rightButton = new ButtonControl(); public StickControl delta = new StickControl(), position = new StickControl(), scroll = new StickControl(); }
    public class InputValue { }
}
namespace UnityEngine.Rendering
{
    public class VolumeParameter<T> { public T value; public void Override(T v) { value = v; } }
    public class VolumeComponent : ScriptableObject { }
    public class VolumeProfile : ScriptableObject
    {
        public T Add<T>(bool overrides = false) where T : VolumeComponent
        {
            var c = System.Activator.CreateInstance<T>();
            foreach (var f in typeof(T).GetFields()) if (f.FieldType.IsGenericType) f.SetValue(c, System.Activator.CreateInstance(f.FieldType));
            return c;
        }
    }
    public class Volume : MonoBehaviour { public bool isGlobal; public float priority, weight; public VolumeProfile sharedProfile; }
}
namespace UnityEngine.Rendering.Universal
{
    using UnityEngine.Rendering;
    public enum TonemappingMode { None, Neutral, ACES }
    public class Tonemapping : VolumeComponent { public VolumeParameter<TonemappingMode> mode; }
    public class Bloom : VolumeComponent { public VolumeParameter<float> intensity, threshold, scatter; }
    public class ColorAdjustments : VolumeComponent { public VolumeParameter<float> postExposure, contrast, saturation; }
    public class WhiteBalance : VolumeComponent { public VolumeParameter<float> temperature; }
    public class Vignette : VolumeComponent { public VolumeParameter<float> intensity, smoothness; }
    public class UniversalRenderPipelineAsset : RenderPipelineAsset { public int msaaSampleCount; public float renderScale, shadowDistance;
        public override RenderPipeline CreatePipeline() => null; }
    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing }
    public enum AntialiasingQuality { Low, Medium, High }
    public class UniversalAdditionalCameraData : MonoBehaviour { public bool renderPostProcessing, renderShadows; public AntialiasingMode antialiasing; public AntialiasingQuality antialiasingQuality; }
    public static class CameraExtensions { public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera c) => null; }
}
namespace StarterAssets
{
    public class StarterAssetsInputs : UnityEngine.MonoBehaviour { public bool cursorInputForLook; public void SetCursorState(bool b) { } public void LookInput(UnityEngine.Vector2 v) { } }
}
namespace DG.Tweening
{
    public enum Ease { Linear, OutCubic, InQuad } public enum RotateMode { FastBeyond360 } public enum LoopType { Restart }
    public class Tween { } public class Tweener : Tween { }
    public delegate void TweenCallback();
    public static class Ext {
        public static Tweener DORotate(this UnityEngine.Transform t, UnityEngine.Vector3 v, float d, RotateMode m) => null;
        public static Tweener DOMoveY(this UnityEngine.Transform t, float v, float d) => null;
        public static Tweener DOPunchScale(this UnityEngine.Transform t, UnityEngine.Vector3 v, float d, int vib, float el) => null;
        public static Tweener DOFade(this UnityEngine.CanvasGroup t, float v, float d) => null;
        public static Tweener DOFade(this UnityEngine.UI.Graphic t, float v, float d) => null;
        public static int DOKill(this UnityEngine.Component c, bool complete = false) => 0;
        public static T SetRelative<T>(this T t, bool b) where T : Tween => t; public static T SetEase<T>(this T t, Ease e) where T : Tween => t;
        public static T SetLoops<T>(this T t, int l, LoopType lt) where T : Tween => t; public static T SetLink<T>(this T t, UnityEngine.GameObject g) where T : Tween => t;
        public static T OnComplete<T>(this T t, TweenCallback c) where T : Tween => t;
    }
}

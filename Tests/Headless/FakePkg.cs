// Stubs for packages (TMP, uGUI, Input System, URP, DOTween, StarterAssets) + Unity 6 renames, compiled against real UnityEngine 2021.3 modules.
using System;
namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.EventSystems.UIBehaviour { public Color color; public bool raycastTarget; public RectTransform rectTransform => (RectTransform)transform; }
    public class Image : Graphic { public UnityEngine.Sprite sprite; }
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
        public KeyControl anyKey;
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
        public float lowMotor, highMotor;
        public void SetMotorSpeeds(float low, float high) { lowMotor = low; highMotor = high; }
    }
    public class DpadControl { public ButtonControl up = new ButtonControl(), down = new ButtonControl(), left = new ButtonControl(), right = new ButtonControl(); public UnityEngine.Vector2 ReadValue() => UnityEngine.Vector2.zero; }
    public class Mouse : InputDevice { public static Mouse current; public ButtonControl leftButton = new ButtonControl(), rightButton = new ButtonControl(); public StickControl delta = new StickControl(), position = new StickControl(), scroll = new StickControl(); }
    public class InputValue { }
}
namespace UnityEngine.Rendering
{
    public class VolumeParameter<T> { public T value; public void Override(T v) { value = v; } }
    public class VolumeComponent : ScriptableObject { public bool active = true; }
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
    public class ColorAdjustments : VolumeComponent { public VolumeParameter<float> postExposure, contrast, saturation; public VolumeParameter<UnityEngine.Color> colorFilter; }
    public class WhiteBalance : VolumeComponent { public VolumeParameter<float> temperature; }
    public class Vignette : VolumeComponent { public VolumeParameter<float> intensity, smoothness; }
    public class UniversalRenderPipelineAsset : RenderPipelineAsset { public int msaaSampleCount, mainLightShadowmapResolution = 2048; public float renderScale, shadowDistance;
        public override RenderPipeline CreatePipeline() => null; }
    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing }
    public enum AntialiasingQuality { Low, Medium, High }
    public class UniversalAdditionalCameraData : MonoBehaviour { public bool renderPostProcessing, renderShadows; public AntialiasingMode antialiasing; public AntialiasingQuality antialiasingQuality; }
    public static class CameraExtensions { public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera c) => null; }
}
namespace StarterAssets
{
    public class StarterAssetsInputs : UnityEngine.MonoBehaviour { public UnityEngine.Vector2 move, look; public bool jump, sprint; public bool cursorInputForLook; public void SetCursorState(bool b) { } public void LookInput(UnityEngine.Vector2 v) { } }
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

// ---- UI / input / menus: working fakes for GameInput (actions, rebinding, overrides JSON), PlayerInput,
// RawImage / Mask, pointer handlers, textures, scenes.
namespace UnityEngine.InputSystem.Controls { internal class ControlsNamespaceStub { } }
namespace UnityEngine.InputSystem
{
    using System.Collections.Generic;
    using System.Reflection;
    public enum InputActionType { Value, Button, PassThrough }
    public struct InputBinding { public string path, overridePath, groups, name; public string effectivePath => string.IsNullOrEmpty(overridePath) ? path : overridePath; }
    public struct BindingSyntax { public BindingSyntax WithGroup(string g) => this; }
    public class InputAction
    {
        public string name; public InputActionMap actionMap;
        internal bool selfEnabled;
        public bool enabled => selfEnabled;
        internal readonly List<InputBinding> list = new List<InputBinding>();
        public IReadOnlyList<InputBinding> bindings => list;
        public void Enable() => selfEnabled = true; public void Disable() => selfEnabled = false;
        public BindingSyntax AddBinding(string path, string interactions = null, string processors = null, string groups = null) { list.Add(new InputBinding { path = path, groups = groups }); return default(BindingSyntax); }
        // Resolve "<Keyboard>/e", "<Gamepad>/buttonSouth", "<Gamepad>/dpad/up" against the fake devices.
        internal static object Control(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int i = path.IndexOf('/'); if (i < 0) return null;
            string dev = path.Substring(0, i), c = path.Substring(i + 1);
            if (dev == "<Keyboard>")
            {
                var kb = Keyboard.current; if (kb == null) return null;
                string f = c.Length == 1 && char.IsDigit(c[0]) ? "digit" + c + "Key" : c + "Key";
                var fi = typeof(Keyboard).GetField(f); return fi?.GetValue(kb);
            }
            if (dev == "<Gamepad>")
            {
                var pad = Gamepad.current; if (pad == null) return null;
                if (c.StartsWith("dpad/")) { var d = typeof(DpadControl).GetField(c.Substring(5)); return d?.GetValue(pad.dpad); }
                string f = c == "start" ? "startButton" : c == "select" ? "selectButton" : c == "leftStickPress" ? "leftStickButton" : c == "rightStickPress" ? "rightStickButton" : c;
                var fi = typeof(Gamepad).GetField(f); return fi?.GetValue(pad);
            }
            return null;
        }
        bool Any(System.Func<object, bool> test)
        {
            if (!enabled || (actionMap != null && !actionMap.enabled)) return false;
            foreach (var b in list) { var c = Control(b.effectivePath); if (c != null && test(c)) return true; }
            return false;
        }
        public bool WasPressedThisFrame() => Any(c => c is KeyControl k ? k.wasPressedThisFrame : c is ButtonControl b && b.wasPressedThisFrame);
        public bool IsPressed() => Any(c => c is KeyControl k ? k.isPressed : c is ButtonControl b && (b.isPressed || b.value > 0.5f));
        public bool WasReleasedThisFrame() => Any(c => c is ButtonControl b && b.wasReleasedThisFrame);
        public TValue ReadValue<TValue>() where TValue : struct => default(TValue);
        public string GetBindingDisplayString(int bindingIndex)
        {
            string p = list[bindingIndex].effectivePath; int i = p.LastIndexOf('/'); string t = p.Substring(i + 1);
            return t.Length == 1 ? t.ToUpperInvariant() : char.ToUpperInvariant(t[0]) + t.Substring(1);
        }
        public void ApplyBindingOverride(int bindingIndex, string path) { var b = list[bindingIndex]; b.overridePath = path; list[bindingIndex] = b; }
        public void RemoveBindingOverride(int bindingIndex) { var b = list[bindingIndex]; b.overridePath = null; list[bindingIndex] = b; }
        public void RemoveAllBindingOverrides() { for (int i = 0; i < list.Count; i++) RemoveBindingOverride(i); }
        public InputActionRebindingExtensions.RebindingOperation PerformInteractiveRebinding(int bindingIndex = -1) => new InputActionRebindingExtensions.RebindingOperation { act = this, index = bindingIndex };
    }
    public class InputActionMap
    {
        public string name; internal bool on; public bool enabled => on;
        public readonly List<InputAction> actions = new List<InputAction>();
        public InputAction AddAction(string name, InputActionType type = InputActionType.Value, string binding = null, string interactions = null, string processors = null, string groups = null, string expectedControlLayout = null)
        { var a = new InputAction { name = name, actionMap = this }; if (binding != null) a.AddBinding(binding); actions.Add(a); return a; }
        public InputAction FindAction(string nameOrId, bool throwIfNotFound = false) => actions.Find(a => a.name == nameOrId);
        public void Enable() { on = true; foreach (var a in actions) a.Enable(); }
        public void Disable() { on = false; foreach (var a in actions) a.Disable(); }
    }
    public class InputActionAsset : UnityEngine.ScriptableObject
    {
        public readonly List<InputActionMap> actionMaps = new List<InputActionMap>();
        public InputActionMap AddActionMap(string name) { var m = new InputActionMap { name = name }; actionMaps.Add(m); return m; }
        public InputActionMap FindActionMap(string nameOrId, bool throwIfNotFound = false) => actionMaps.Find(m => m.name == nameOrId);
        public InputAction FindAction(string actionNameOrId, bool throwIfNotFound = false) { foreach (var m in actionMaps) { var a = m.FindAction(actionNameOrId); if (a != null) return a; } return null; }
        public void Enable() { foreach (var m in actionMaps) m.Enable(); } public void Disable() { foreach (var m in actionMaps) m.Disable(); }
        class Ov { public string action; public int index; public string path; }
        public string SaveBindingOverridesAsJson()
        {
            var l = new List<Ov>();
            foreach (var m in actionMaps) foreach (var a in m.actions) for (int i = 0; i < a.list.Count; i++) if (!string.IsNullOrEmpty(a.list[i].overridePath)) l.Add(new Ov { action = m.name + "/" + a.name, index = i, path = a.list[i].overridePath });
            return Newtonsoft.Json.JsonConvert.SerializeObject(new { bindings = l });
        }
        public void LoadBindingOverridesFromJson(string json, bool removeExisting = true)
        {
            if (removeExisting) RemoveAllBindingOverrides();
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);
            foreach (var b in o["bindings"])
            {
                string[] an = ((string)b["action"]).Split('/');
                var a = FindActionMap(an[0])?.FindAction(an[1]);
                if (a != null) a.ApplyBindingOverride((int)b["index"], (string)b["path"]);
            }
        }
        public void RemoveAllBindingOverrides() { foreach (var m in actionMaps) foreach (var a in m.actions) a.RemoveAllBindingOverrides(); }
    }
    public static class InputActionRebindingExtensions
    {
        public sealed class RebindingOperation : System.IDisposable
        {
            /// <summary>Fake: the running interactive rebind, finished by FakeComplete / Cancel.</summary>
            public static RebindingOperation Current;
            internal InputAction act; internal int index;
            System.Action<RebindingOperation> complete, cancel;
            public InputAction action => act;
            public RebindingOperation WithControlsHavingToMatchPath(string path) => this; public RebindingOperation WithControlsExcluding(string path) => this;
            public RebindingOperation WithCancelingThrough(string path) => this; public RebindingOperation OnMatchWaitForAnother(float seconds) => this;
            public RebindingOperation OnComplete(System.Action<RebindingOperation> callback) { complete = callback; return this; }
            public RebindingOperation OnCancel(System.Action<RebindingOperation> callback) { cancel = callback; return this; }
            public RebindingOperation Start() { Current = this; return this; }
            public void FakeComplete(string path) { act.ApplyBindingOverride(index, path); Current = null; complete?.Invoke(this); }
            public void Cancel() { if (Current != this) return; Current = null; cancel?.Invoke(this); }
            public void Dispose() { if (Current == this) Current = null; }
        }
    }
    public class PlayerInput : UnityEngine.MonoBehaviour { public string currentControlScheme = "KeyboardMouse"; public InputActionAsset actions; }
}
namespace UnityEngine.UI
{
    public class RawImage : Graphic { public UnityEngine.Texture texture; public UnityEngine.Rect uvRect; }
    public class Mask : UnityEngine.EventSystems.UIBehaviour { public bool showMaskGraphic = true; }
}
namespace UnityEngine.EventSystems
{
    public class PointerEventData { }
    public interface IPointerClickHandler { void OnPointerClick(PointerEventData eventData); }
    public interface IPointerEnterHandler { void OnPointerEnter(PointerEventData eventData); }
}
namespace UnityEngine
{
    public enum TextureFormat { RGBA32 = 4, ARGB32 = 5 }
    public enum FilterMode { Point, Bilinear }
    public enum CameraClearFlags { Skybox = 1, SolidColor = 2, Depth = 3, Nothing = 4 }
    public enum SendMessageOptions { RequireReceiver, DontRequireReceiver }
    public class Texture : Object { public int width, height; public FilterMode filterMode; public TextureWrapMode wrapMode; }
    public class Texture2D : Texture
    {
        public Color[] pixels;
        public Texture2D(int w, int h, TextureFormat f, bool mips) { width = w; height = h; }
        public void SetPixels(Color[] c) => pixels = c; public void Apply() { } public void SetPixel(int x, int y, Color c) { }
    }
    public class RenderTexture : Texture { public bool released; public RenderTexture(int w, int h, int depth) { width = w; height = h; } public void Release() => released = true; }
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }
}
namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }
    public struct Scene { public int buildIndex; public string name; }
    public static class SceneManager
    {
        public static int LoadCount;
        public static event System.Action<Scene, LoadSceneMode> sceneLoaded;
        public static event System.Action<Scene> sceneUnloaded;
        public static Scene GetActiveScene() => new Scene { buildIndex = 0, name = "Fake" };
        /// <summary>Fake: no real objects are swapped; the unload / load callbacks run straight away.</summary>
        public static void LoadScene(int index) { LoadCount++; var s = new Scene { buildIndex = index }; sceneUnloaded?.Invoke(s); sceneLoaded?.Invoke(s, LoadSceneMode.Single); }
    }
}
// Added for art/audio/touch (DesertWeatherFX, TouchControls)
namespace UnityEngine.Rendering.Universal
{
    using UnityEngine.Rendering;
    public class LensDistortion : VolumeComponent { public VolumeParameter<float> intensity, scale; }
    public class ChromaticAberration : VolumeComponent { public VolumeParameter<float> intensity; }
    // Settings > Graphics
    public class MotionBlur : VolumeComponent { public VolumeParameter<float> intensity; }
    public enum DepthOfFieldMode { Off, Gaussian, Bokeh }
    public class DepthOfField : VolumeComponent { public VolumeParameter<DepthOfFieldMode> mode; public VolumeParameter<float> gaussianStart, gaussianEnd; }
}
namespace UnityEngine.EventSystems { public class EventSystem : UIBehaviour { public static EventSystem current; } }
namespace UnityEngine.InputSystem.UI { public class InputSystemUIInputModule : UnityEngine.EventSystems.UIBehaviour { } }
namespace UnityEngine.InputSystem.OnScreen
{
    public abstract class OnScreenControl : UnityEngine.MonoBehaviour { public string controlPath { get; set; } }
    public class OnScreenStick : OnScreenControl { public float movementRange { get; set; } = 50f; }
    public class OnScreenButton : OnScreenControl { }
}

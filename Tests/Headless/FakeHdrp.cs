// Fake HDRP 17 types used by the game (Assets/Scripts after the HDRP switch). Every member mirrors a real
// UnityEngine.Rendering.HighDefinition / core API with the same name; volume parameters are the generic fake VolumeParameter<T>
// (real HDRP uses BoolParameter, MinFloatParameter... which all have .value and .Override). Rendering is inert.
namespace UnityEngine
{
    public partial struct Color { public Color linear => this; }
    public partial class Material
    {
        public readonly System.Collections.Generic.Dictionary<string, string> tags = new System.Collections.Generic.Dictionary<string, string>();
        public void SetOverrideTag(string tag, string val) => tags[tag] = val;
    }
}
namespace UnityEngine.Rendering
{
    public enum DynamicResScalePolicyType { ReturnsPercentage, ReturnsMinMaxLerpFactor }
    public delegate float PerformDynamicRes();
    public static class DynamicResolutionHandler
    {
        public static PerformDynamicRes Scaler;
        public static void SetDynamicResScaler(PerformDynamicRes scaler, DynamicResScalePolicyType type = DynamicResScalePolicyType.ReturnsMinMaxLerpFactor) => Scaler = scaler;
    }
}
namespace UnityEngine.Rendering.HighDefinition
{
    using UnityEngine.Rendering;
    public enum SkyType { HDRI = 1, Procedural = 2, Gradient = 3, PhysicallyBased = 4 }
    public enum FogColorMode { ConstantColor, SkyColor }
    public enum ExposureMode { Fixed = 0, Automatic = 1, UsePhysicalCamera = 3, CurveMapping = 4, AutomaticHistogram = 5 }
    public class VisualEnvironment : VolumeComponent { public VolumeParameter<int> skyType, cloudType; }
    public class PhysicallyBasedSky : VolumeComponent { }
    public class VolumetricClouds : VolumeComponent { public VolumeParameter<bool> enable; }
    public class Fog : VolumeComponent
    {
        public VolumeParameter<bool> enabled, enableVolumetricFog; public VolumeParameter<float> meanFreePath, baseHeight, maximumHeight;
        public VolumeParameter<FogColorMode> colorMode; public VolumeParameter<Color> color;
    }
    public class Exposure : VolumeComponent { public VolumeParameter<ExposureMode> mode; public VolumeParameter<float> fixedExposure, compensation; }
    public class ScreenSpaceAmbientOcclusion : VolumeComponent { public VolumeParameter<float> intensity; }
    public class ContactShadows : VolumeComponent { public VolumeParameter<bool> enable; public VolumeParameter<float> length; }
    public class ScreenSpaceReflection : VolumeComponent { public VolumeParameter<bool> enabled; }
    public class HDShadowSettings : VolumeComponent { public VolumeParameter<float> maxShadowDistance; public VolumeParameter<int> cascadeShadowSplitCount; }

    public enum FrameSettingsField { ShadowMaps = 20, SSR = 23, SSAO = 24, ContactShadows = 21, Volumetrics = 28, VolumetricClouds = 79, Postprocess = 15 }
    public class BitArray128 { readonly System.Collections.Generic.HashSet<uint> on = new System.Collections.Generic.HashSet<uint>(); public bool this[uint i] { get => on.Contains(i); set { if (value) on.Add(i); else on.Remove(i); } } }
    public struct FrameSettingsOverrideMask { private BitArray128 m; public BitArray128 mask => m ?? (m = new BitArray128()); }
    public class FrameSettings
    {
        public readonly System.Collections.Generic.Dictionary<FrameSettingsField, bool> enabled = new System.Collections.Generic.Dictionary<FrameSettingsField, bool>();
        public void SetEnabled(FrameSettingsField f, bool on) => enabled[f] = on;
    }
    public class HDAdditionalCameraData : MonoBehaviour
    {
        public enum AntialiasingMode { None, FastApproximateAntialiasing, TemporalAntialiasing, SubpixelMorphologicalAntiAliasing }
        public enum SMAAQualityLevel { Low, Medium, High }
        public enum ClearColorMode { Sky, Color, None }
        public AntialiasingMode antialiasing; public SMAAQualityLevel SMAAQuality; public bool allowDynamicResolution, customRenderingSettings;
        public ClearColorMode clearColorMode; public Color backgroundColorHDR;
        public FrameSettingsOverrideMask renderingPathCustomFrameSettingsOverrideMask;
        public FrameSettings renderingPathCustomFrameSettings = new FrameSettings();
    }
    public class HDAdditionalLightData : MonoBehaviour { public int shadowResolution; public void SetShadowResolution(int r) => shadowResolution = r; public void SetShadowResolutionOverride(bool o) { } }
}

using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// HDRP project setup, safe to run any number of times (editor load once per session, Mazraa > HDRP menu, and before every
/// Windows build in MazraaBuild.WindowsNoExit):
/// - makes sure HDRP's global settings asset exists (HDRP creates it in Assets/HDRPDefaultResources);
/// - assigns Assets/Settings/HDRP/MazraaHDRP_{Low,Medium,High,Ultra} as the default pipeline (High) and to the quality
///   levels of the same name (GraphicsEnhancer presets 0..3), re-applying their feature switches (SerializedObject paths, so
///   a renamed HDRP field is only a warning);
/// - re-derives keywords / render states of every HDRP material (HDShaderUtils.ResetMaterialKeywords) after the
///   URP -> HDRP/Lit .mat conversion;
/// - logs "[Mazraa HDRP] pink:" for every material whose shader is missing, unsupported, has errors or is still URP
///   (CI greps the build log for it).
/// Everything goes through reflection or string paths where the HDRP API moved between versions, so this file always compiles.
/// </summary>
[InitializeOnLoad]
public static class MazraaHdrpSetup
{
    public const string Folder = "Assets/Settings/HDRP/";
    public static readonly string[] Levels = { "Low", "Medium", "High", "Ultra" };
    const string SessionKey = "Mazraa.HdrpSetup.Done";

    static MazraaHdrpSetup()
    {
        if (Application.isBatchMode) return;   // batch builds call Ensure() themselves
        EditorApplication.delayCall += () =>
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            try { Ensure(); } catch (Exception e) { Debug.LogWarning("[Mazraa HDRP] setup failed: " + e.Message); }
        };
    }

    [MenuItem("Mazraa/HDRP/Set up pipeline and fix materials")]
    public static void Ensure()
    {
        EnsureGlobalSettings();
        AssignPipelines();
        int fixedMats = FixMaterials(out int pink);
        AssetDatabase.SaveAssets();
        Debug.Log("[Mazraa HDRP] setup done: pipeline " + (GraphicsSettings.defaultRenderPipeline != null ? GraphicsSettings.defaultRenderPipeline.name : "none")
            + ", " + fixedMats + " HDRP materials validated, " + pink + " pink.");
    }

    static Type HdType(string name, string asm = "Unity.RenderPipelines.HighDefinition.Runtime") => Type.GetType("UnityEngine.Rendering.HighDefinition." + name + ", " + asm);

    static void EnsureGlobalSettings()
    {
        try
        {
            var t = HdType("HDRenderPipelineGlobalSettings");
            if (t == null) { Debug.LogWarning("[Mazraa HDRP] HDRenderPipelineGlobalSettings type not found"); return; }
            var ensure = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(m => m.Name == "Ensure" && m.GetParameters().All(p => p.IsOptional || p.ParameterType == typeof(bool)));
            if (ensure != null) ensure.Invoke(null, ensure.GetParameters().Select(p => p.ParameterType == typeof(bool) ? (object)true : p.DefaultValue).ToArray());
            else Debug.Log("[Mazraa HDRP] no HDRenderPipelineGlobalSettings.Ensure (HDRP creates its global settings itself)");
        }
        catch (Exception e) { Debug.LogWarning("[Mazraa HDRP] global settings: " + (e.InnerException ?? e).Message); }
    }

    static RenderPipelineAsset LoadLevel(string level)
    {
        var a = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(Folder + "MazraaHDRP_" + level + ".asset");
        if (a != null) return a;
        // The committed YAML did not load (script GUID moved?): make a fresh HDRP asset next to it.
        var t = HdType("HDRenderPipelineAsset");
        if (t == null) { Debug.LogError("[Mazraa HDRP] HDRenderPipelineAsset type not found: is the HDRP package installed?"); return null; }
        string path = Folder + "MazraaHDRP_" + level + " (generated).asset";
        a = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
        if (a != null) return a;
        a = (RenderPipelineAsset)ScriptableObject.CreateInstance(t);
        AssetDatabase.CreateAsset(a, path);
        Debug.LogWarning("[Mazraa HDRP] created " + path);
        return a;
    }

    static void AssignPipelines()
    {
        var assets = Levels.Select(LoadLevel).ToArray();
        for (int i = 0; i < assets.Length; i++) if (assets[i] != null) ApplyFeatures(assets[i], i);
        if (assets[2] != null && GraphicsSettings.defaultRenderPipeline != assets[2]) GraphicsSettings.defaultRenderPipeline = assets[2];
        int current = QualitySettings.GetQualityLevel();
        var names = QualitySettings.names;
        for (int q = 0; q < names.Length; q++)
        {
            int i = Array.IndexOf(Levels, names[q]);
            var a = i >= 0 ? assets[i] : assets[2];
            if (a == null) continue;
            QualitySettings.SetQualityLevel(q, false);
            if (QualitySettings.renderPipeline != a) QualitySettings.renderPipeline = a;
        }
        QualitySettings.SetQualityLevel(current, false);
    }

    /// <summary>Per-level HDRP features (same values as the committed YAML): Low must run on a GTX 1660 at 1080p.</summary>
    static void ApplyFeatures(RenderPipelineAsset asset, int level)
    {
        var so = new SerializedObject(asset);
        const string S = "m_RenderPipelineSettings.";
        bool high = level >= 2;
        Set(so, S + "supportSSR", high);
        Set(so, S + "supportSSAO", level >= 1);
        Set(so, S + "supportVolumetrics", high);
        Set(so, S + "supportVolumetricClouds", level >= 1);
        Set(so, S + "supportDitheringCrossFade", false);
        Set(so, S + "dynamicResolutionSettings.enabled", true);
        Set(so, S + "dynamicResolutionSettings.minPercentage", 50f);
        Set(so, S + "dynamicResolutionSettings.maxPercentage", 100f);
        int dir = level == 0 ? 1024 : level == 3 ? 4096 : 2048;
        Set(so, S + "hdShadowInitParams.maxDirectionalShadowMapResolution", level >= 2 ? 4096 : dir);
        var vals = so.FindProperty(S + "hdShadowInitParams.shadowResolutionDirectional.m_Values");
        if (vals != null && vals.isArray) for (int i = 0; i < vals.arraySize; i++) vals.GetArrayElementAtIndex(i).intValue = dir;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Set(SerializedObject so, string path, object v)
    {
        var p = so.FindProperty(path);
        if (p == null) { Debug.LogWarning("[Mazraa HDRP] no property " + path + " on " + so.targetObject.name); return; }
        switch (p.propertyType)
        {
            case SerializedPropertyType.Boolean: p.boolValue = (bool)v; break;
            case SerializedPropertyType.Integer: p.intValue = Convert.ToInt32(v); break;
            case SerializedPropertyType.Float: p.floatValue = Convert.ToSingle(v); break;
        }
    }

    /// <summary>Validate every HDRP material asset; count (and log) the ones that would draw pink.</summary>
    static int FixMaterials(out int pink)
    {
        pink = 0;
        int n = 0;
        var utils = Type.GetType("UnityEditor.Rendering.HighDefinition.HDShaderUtils, Unity.RenderPipelines.HighDefinition.Editor");
        var reset = utils != null ? utils.GetMethod("ResetMaterialKeywords", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Material) }, null) : null;
        if (reset == null) Debug.LogWarning("[Mazraa HDRP] HDShaderUtils.ResetMaterialKeywords not found: materials keep their converted keywords");
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".mat")) continue;
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) continue;
            var sh = m.shader;
            string sn = sh != null ? sh.name : "";
            if (sh == null || sn == "Hidden/InternalErrorShader" || sn.StartsWith("Universal Render Pipeline/") || !sh.isSupported || ShaderUtil.ShaderHasError(sh))
            {
                bool shipped = path.Contains("/Resources/") || path.StartsWith("Assets/Scenes/") || path.StartsWith("Assets/Materials/");
                if (shipped || sn.StartsWith("Universal Render Pipeline/")) { pink++; Debug.LogWarning("[Mazraa HDRP] pink: " + path + " (" + (sh != null ? sn : "no shader") + ")"); }
                continue;
            }
            if (reset != null && sn.StartsWith("HDRP/"))
            {
                try { reset.Invoke(null, new object[] { m }); EditorUtility.SetDirty(m); n++; }
                catch (Exception e) { Debug.LogWarning("[Mazraa HDRP] could not validate " + path + ": " + (e.InnerException ?? e).Message); }
            }
        }
        return n;
    }
}

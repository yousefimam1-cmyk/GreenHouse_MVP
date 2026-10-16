using System.Collections.Generic;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Build-time shader variant stripping. A clean Windows build spent most of its time compiling thousands of URP/Lit and
/// Terrain variants the game never uses (XR, debug views, Forward+, light cookies/layers, additional-light shadows,
/// reflection-probe blending, LOD cross-fade, rendering layers, DOTS instancing). Variants that need any of these keywords
/// are removed before compilation. The matching features are off in PC_RPAsset (commit a8b668d), so nothing visible changes.
/// PC_Renderer uses the Forward path (Forward+ keywords are kept
/// automatically if a renderer is switched back to Forward+), and a pass is never stripped to zero variants.
/// Lightmap, fog, shadow and SSAO keywords are kept. To disable: delete this file or set Enabled = false.
/// Note: the first build after changing graphics settings still compiles every remaining variant once; later builds reuse
/// Library/ShaderCache and take a few minutes.
/// </summary>
public class MazraaShaderStripper : IPreprocessShaders
{
    public static bool Enabled = true;
    public int callbackOrder => 100;

    private static readonly HashSet<string> Strip = new HashSet<string>
    {
        // VR / XR
        "STEREO_INSTANCING_ON", "STEREO_MULTIVIEW_ON", "STEREO_CUBEMAP_RENDER_ON", "UNITY_SINGLE_PASS_STEREO", "_FOVEATED_RENDERING_NON_UNIFORM_RASTER",
        // Debug views and entities
        "DEBUG_DISPLAY", "DOTS_INSTANCING_ON",
        // Features turned off in PC_RPAsset
        "_FORWARD_PLUS", "_CLUSTER_LIGHT_LOOP", "_LIGHT_COOKIES", "_LIGHT_LAYERS", "_ADDITIONAL_LIGHT_SHADOWS",
        "_REFLECTION_PROBE_BLENDING", "_REFLECTION_PROBE_BOX_PROJECTION", "_REFLECTION_PROBE_ATLAS",
        "LOD_FADE_CROSSFADE", "_WRITE_RENDERING_LAYERS", "_ALPHATEST_ON_TERRAIN_HOLES", "_TERRAIN_HOLES",
        // Probe volumes (APV) are not used
        "PROBE_VOLUMES_L1", "PROBE_VOLUMES_L2",
    };

    private static readonly HashSet<string> ForwardPlusKeywords = new HashSet<string> { "_FORWARD_PLUS", "_CLUSTER_LIGHT_LOOP" };
    private static int removed, kept, buildsChecked = -1;
    private static bool forwardPlusUsed;

    /// <summary>True when any URP renderer asset in the project uses Forward+ / Deferred+ (m_RenderingMode 2 or 3).
    /// Then the Forward+ keywords must be kept: stripping them leaves the lit shaders with no variants and the game
    /// renders black (the Oct 16 store build). Read from the asset text so it works for every URP version.</summary>
    public static bool ForwardPlusUsed()
    {
        try
        {
            foreach (var f in Directory.GetFiles("Assets", "*.asset", SearchOption.AllDirectories))
            {
                if (!f.Replace('\\', '/').Contains("Renderer")) continue;
                string t = File.ReadAllText(f);
                if (t.Contains("m_RenderingMode: 2") || t.Contains("m_RenderingMode: 3")) return true;
            }
        }
        catch (IOException) { return true; }
        return false;
    }

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        if (!Enabled || data == null || data.Count == 0) return;
        if (buildsChecked != UnityEngine.Time.frameCount) { forwardPlusUsed = ForwardPlusUsed(); buildsChecked = UnityEngine.Time.frameCount; }
        var keep = new List<ShaderCompilerData>(data.Count);
        int dropped = 0;
        foreach (var d in data)
        {
            bool strip = false;
            foreach (var k in d.shaderKeywordSet.GetShaderKeywords())
            {
                string n = k.name;
                if (!Strip.Contains(n)) continue;
                if (forwardPlusUsed && ForwardPlusKeywords.Contains(n)) continue;
                strip = true;
                break;
            }
            if (strip) dropped++; else keep.Add(d);
        }
        // Safety net: never strip every variant of a pass (that renders black or invisible); keep it untouched instead.
        if (keep.Count == 0) { kept += data.Count; return; }
        if (dropped == 0) { kept += data.Count; return; }
        data.Clear();
        foreach (var d in keep) data.Add(d);
        removed += dropped;
        kept += keep.Count;
    }

    /// <summary>Called by MazraaBuild after a build to log how much was skipped.</summary>
    public static void LogAndReset()
    {
        if (removed > 0) Debug.Log("[Mazraa] Shader stripping: removed " + removed + " variants, compiled " + kept + ".");
        removed = kept = 0;
    }
}

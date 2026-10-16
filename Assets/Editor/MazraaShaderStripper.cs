using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Build-time shader variant stripping. A clean Windows build spent most of its time compiling thousands of URP/Lit and
/// Terrain variants the game never uses (XR, debug views, Forward+, light cookies/layers, additional-light shadows,
/// reflection-probe blending, LOD cross-fade, rendering layers, DOTS instancing). Variants that need any of these keywords
/// are removed before compilation. The matching features are off in PC_RPAsset (commit a8b668d), so nothing visible changes.
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

    private static int removed, kept;

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        if (!Enabled || data == null) return;
        for (int i = data.Count - 1; i >= 0; i--)
        {
            foreach (var k in data[i].shaderKeywordSet.GetShaderKeywords())
            {
                if (!Strip.Contains(k.name)) continue;
                data.RemoveAt(i);
                removed++;
                break;
            }
        }
        kept += data.Count;
    }

    /// <summary>Called by MazraaBuild after a build to log how much was skipped.</summary>
    public static void LogAndReset()
    {
        if (removed > 0) Debug.Log("[Mazraa] Shader stripping: removed " + removed + " variants, compiled " + kept + ".");
        removed = kept = 0;
    }
}

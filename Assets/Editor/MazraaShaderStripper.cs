using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Build-time shader variant stripping on top of HDRP's own stripping (which already drops every feature the
/// MazraaHDRP_* assets don't support). Removed here: XR / stereo, runtime debug views, DOTS instancing, adaptive probe
/// volumes (APV is not used) and LOD cross-fade (supportDitheringCrossFade is off in every MazraaHDRP asset; Vegetation
/// uses LODFadeMode.None). Lightmap, fog, shadow, alpha-test, transparency and screen-space keywords are always kept, and a
/// pass is never stripped to zero variants. To disable: delete this file or set Enabled = false.
/// Compute shaders: the STP upscaler (StpSetup / StpPreTaa / StpTaa) is never used (dynamic resolution upsamples with
/// Catmull-Rom, AA is SMAA / TAA) and its kernels need DXC, which a Linux editor cannot run for D3D: they are skipped
/// (MazraaComputeStripper) so the CI build log has no shader errors.
/// Note: the first build after changing graphics settings compiles every remaining variant once; later builds reuse
/// Library/ShaderCache.
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
        // Adaptive probe volumes are not used
        "PROBE_VOLUMES_L1", "PROBE_VOLUMES_L2",
        // LOD cross-fade is off (MazraaHDRP_*: supportDitheringCrossFade 0)
        "LOD_FADE_CROSSFADE",
    };

    private static int removed, kept;

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        if (!Enabled || data == null || data.Count == 0) return;
        var keep = new List<ShaderCompilerData>(data.Count);
        int dropped = 0;
        foreach (var d in data)
        {
            bool strip = false;
            foreach (var k in d.shaderKeywordSet.GetShaderKeywords())
                if (Strip.Contains(k.name)) { strip = true; break; }
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

    /// <summary>Compute shaders the game never dispatches (see the class summary).</summary>
    public static bool StripCompute(string computeShaderName) => Enabled && computeShaderName != null && computeShaderName.StartsWith("Stp");

    /// <summary>Called by MazraaBuild after a build to log how much was skipped.</summary>
    public static void LogAndReset()
    {
        if (removed > 0) Debug.Log("[Mazraa] Shader stripping: removed " + removed + " variants, compiled " + kept + ".");
        removed = kept = 0;
    }
}

/// <summary>Skips the STP upscaler compute kernels (unused; DXC-only on a Linux build machine). See MazraaShaderStripper.</summary>
public class MazraaComputeStripper : IPreprocessComputeShaders
{
    public int callbackOrder => 100;

    public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
    {
        if (shader == null || data == null || !MazraaShaderStripper.StripCompute(shader.name)) return;
        data.Clear();
    }
}

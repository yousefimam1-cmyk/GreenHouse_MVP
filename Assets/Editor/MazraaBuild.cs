using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Menu Mazraa > Build WebGL, also used by the GitHub Actions build (buildMethod MazraaBuild.WebGL).</summary>
public static class MazraaBuild
{
    [MenuItem("Mazraa/Build WebGL")]
    public static void WebGL()
    {
        // GitHub Pages does not send Brotli/Gzip headers, so build uncompressed (or gzip with the JS fallback).
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        string outDir = "build/WebGL/Mazraa";
        Directory.CreateDirectory(outDir);
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var report = BuildPipeline.BuildPlayer(scenes, outDir, BuildTarget.WebGL, BuildOptions.None);
        Debug.Log("Mazraa WebGL build: " + report.summary.result + " -> " + outDir);
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}

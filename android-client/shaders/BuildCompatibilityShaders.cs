using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildCompatibilityShaders
{
    public static void Run()
    {
        try
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Path.GetFullPath(Path.Combine(root, "../unity6/compat-shaders"));
            Directory.CreateDirectory(output);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan });
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] {
                new AssetBundleBuild { assetBundleName = "durango-br-shaders.bundle",
                    assetNames = new[] { "Assets/Resources/Shaders/Floor2AlphaUV.shader" } }
            }, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.Android);
            if (manifest == null) throw new Exception("Shader bundle build failed.");
            Debug.Log("Brazilian compatibility shader bundle built: " + output);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}

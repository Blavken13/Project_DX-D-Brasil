using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public static class RecoveredBundleTests
{
    [Serializable] public class Entry { public string name; public string path; public bool candidate; }
    [Serializable] public class Spec { public Entry[] entries; public string output; }
    [Serializable] public class TextureResult { public string name; public int width; public int height; public string format; }
    [Serializable] public class MaterialResult { public string name; public string shader; public int nonNullTextures; }
    [Serializable] public class BundleResult {
        public string name; public bool loaded; public string exception;
        public int assets; public string[] assetNames; public int meshVertices; public int missingRendererMaterials;
        public List<TextureResult> textures = new List<TextureResult>();
        public List<MaterialResult> materials = new List<MaterialResult>();
    }

    static void InspectMaterial(Material material, BundleResult result, HashSet<Material> materials, HashSet<Texture2D> textures)
    {
        if (!materials.Add(material)) return;
        int count = 0;
        foreach (string property in material.GetTexturePropertyNames()) {
            Texture2D texture = material.GetTexture(property) as Texture2D;
            if (texture == null) continue;
            count++;
            if (textures.Add(texture)) result.textures.Add(new TextureResult {
                name = texture.name, width = texture.width, height = texture.height,
                format = texture.format.ToString() });
        }
        result.materials.Add(new MaterialResult { name = material.name,
            shader = material.shader == null ? null : material.shader.name, nonNullTextures = count });
    }
    [Serializable] public class Report {
        public string unityVersion; public string platform; public bool graphicsRenderingTested;
        public List<BundleResult> bundles = new List<BundleResult>();
        public List<string> errors = new List<string>();
    }

    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        Spec spec = JsonUtility.FromJson<Spec>(File.ReadAllText(Path.Combine(root, "test-spec.json")));
        Report report = new Report { unityVersion = Application.unityVersion,
            platform = Application.platform.ToString(), graphicsRenderingTested = false };
        Application.logMessageReceived += (message, trace, type) => {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && report.errors.Count < 1000)
                report.errors.Add(message);
        };
        Dictionary<string, AssetBundle> loaded = new Dictionary<string, AssetBundle>();
        foreach (Entry entry in spec.entries) {
            try {
                AssetBundle bundle = AssetBundle.LoadFromMemory(File.ReadAllBytes(entry.path));
                if (bundle != null) loaded[entry.name] = bundle;
                else if (!entry.candidate) report.errors.Add("Dependency failed to load: " + entry.name);
            } catch (Exception e) {
                report.errors.Add("Load exception for " + entry.name + ": " + e.Message);
            }
        }
        foreach (Entry entry in spec.entries) {
            if (!entry.candidate) continue;
            BundleResult result = new BundleResult { name = entry.name, loaded = loaded.ContainsKey(entry.name) };
            report.bundles.Add(result);
            if (!result.loaded) continue;
            try {
                AssetBundle bundle = loaded[entry.name];
                result.assetNames = bundle.GetAllAssetNames();
                UnityEngine.Object[] assets = bundle.LoadAllAssets();
                result.assets = assets.Length;
                HashSet<Material> materialIds = new HashSet<Material>();
                HashSet<Texture2D> textureIds = new HashSet<Texture2D>();
                foreach (UnityEngine.Object asset in assets) {
                    Texture2D texture = asset as Texture2D;
                    if (texture != null) result.textures.Add(new TextureResult {
                        name = texture.name, width = texture.width, height = texture.height,
                        format = texture.format.ToString() });
                    Material material = asset as Material;
                    if (material != null) InspectMaterial(material, result, materialIds, textureIds);
                    GameObject model = asset as GameObject;
                    if (model != null) {
                        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true)) {
                            foreach (Material attached in renderer.sharedMaterials) {
                                if (attached == null) result.missingRendererMaterials++;
                                else InspectMaterial(attached, result, materialIds, textureIds);
                            }
                            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                            if (skinned != null && skinned.sharedMesh != null)
                                result.meshVertices += skinned.sharedMesh.vertexCount;
                        }
                    }
                }
            } catch (Exception e) { result.exception = e.ToString(); }
        }
        File.WriteAllText(spec.output, JsonUtility.ToJson(report, true));
        Debug.Log("RecoveredBundleTests finished. Report: " + spec.output);
        AssetBundle.UnloadAllAssetBundles(true);
        EditorApplication.Exit(0);
    }
}

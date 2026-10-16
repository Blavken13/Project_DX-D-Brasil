using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public static class BuildAridBasin
{
    const string Root = "Assets/Models/Maps/AridBasin";
    const string PrefabPath = Root + "/AridBasinTerrain.prefab";
    const string BundleName = "models$maps$aridbasin$aridbasinterrain.prefab.bundle";
    static string Runtime => Path.GetFullPath(Path.Combine(Application.dataPath, "../../..", "Desert Maps/Generated_Arid_Basin/Runtime"));
    static int vertexCount, triangleCount;

    [Serializable] class Report
    {
        public string unity_version, platform, hash;
        public int vertices, triangles;
        public bool build_verified, bundle_load_verified, center_open;
        public string[] assets;
    }

    public static void Run()
    {
        try
        {
            Prepare();
            Build(BuildTarget.StandaloneWindows64, "Windows");
            Build(BuildTarget.Android, "Android");
            Debug.Log("[AridBasin] Visual terrain bundle completed for both platforms.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    static void Save(UnityEngine.Object asset, string path)
    {
        var previous = AssetDatabase.LoadMainAssetAtPath(path);
        if (previous == null) AssetDatabase.CreateAsset(asset, path);
        else { EditorUtility.CopySerialized(asset, previous); UnityEngine.Object.DestroyImmediate(asset); }
    }

    static float Coast(float x, float z)
    {
        float nx = x / 25600, nz = z / 25600;
        float angle = Mathf.Atan2(nz, nx);
        float radius = Mathf.Sqrt(nx * nx / (.96f * .96f) + nz * nz / (.94f * .94f));
        return .92f + .035f * Mathf.Sin(3 * angle + .4f) + .025f * Mathf.Cos(5 * angle - .7f) - radius;
    }

    static void Prepare()
    {
        AssetDatabase.Refresh();
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        foreach (string line in File.ReadLines(Root + "/SourceTerrain.obj.txt"))
        {
            var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            if (tokens[0] == "v")
            {
                float x = float.Parse(tokens[1], CultureInfo.InvariantCulture);
                float y = float.Parse(tokens[2], CultureInfo.InvariantCulture);
                float z = float.Parse(tokens[3], CultureInfo.InvariantCulture);
                // The game walks on its native horizontal ground. Preserve the
                // authored mountains as real geometry around the open arena.
                float radius = new Vector2(x, z).magnitude;
                float arenaBlend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(9000, 12032, radius));
                float shoreBlend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.025f, .12f, Coast(x, z)));
                vertices.Add(new Vector3(x, Mathf.Max(0, y - 200) * arenaBlend * shoreBlend, z));
            }
            else if (tokens[0] == "f")
            {
                var face = tokens.Skip(1).Select(t => int.Parse(t.Split('/')[0], CultureInfo.InvariantCulture) - 1).ToArray();
                for (int k = 1; k + 1 < face.Length; k++)
                {
                    int a = face[0], b = face[k], c = face[k + 1];
                    if (Mathf.Max(vertices[a].y, Mathf.Max(vertices[b].y, vertices[c].y)) <= .01f) continue;
                    var middle = (vertices[a] + vertices[b] + vertices[c]) / 3;
                    if (Coast(middle.x, middle.z) < .025f) continue;
                    var normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    if (normal.y < 0) { int swap = b; b = c; c = swap; }
                    indices.Add(a); indices.Add(b); indices.Add(c);
                }
            }
        }
        if (vertices.Count != 66049 || indices.Count < 3000) throw new InvalidOperationException("Unexpected authored terrain mesh");
        var mesh = new Mesh { name = "AridBasinTerrain", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices.Select(v => v / 100f).ToList());
        mesh.uv = vertices.Select(v => new Vector2((v.x + 25600) / 1600, (v.z + 25600) / 1600)).ToArray();
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        Save(mesh, Root + "/AridBasinTerrain.asset");
        mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/AridBasinTerrain.asset");
        foreach (string name in new[] { "Sand", "Rock" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/" + name + ".png");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true; importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 2048; importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/AridBasin.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Arid Basin shader did not compile");
        var material = new Material(shader) { name = "AridBasinDesert" };
        // The extracted sand atlas causes visible texture artifacts on the
        // raised mesh. Use the clean desert ground albedo over the whole model.
        var ground = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Rock.png");
        material.SetTexture("_SandTex", ground);
        material.SetTexture("_RockTex", ground);
        Save(material, Root + "/AridBasinDesert.mat");
        var prefab = new GameObject("AridBasinTerrain");
        var model = new GameObject("AridBasinModel");
        model.transform.SetParent(prefab.transform, false);
        model.transform.localScale = Vector3.one * 100;
        model.AddComponent<MeshFilter>().sharedMesh = mesh;
        model.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/AridBasinDesert.mat");
        prefab.isStatic = true;
        PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        UnityEngine.Object.DestroyImmediate(prefab);
        vertexCount = vertices.Count; triangleCount = indices.Count / 3;
        AssetDatabase.SaveAssets();
    }

    static void Build(BuildTarget target, string platform)
    {
        PlayerSettings.stripEngineCode = false;
        PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
        PlayerSettings.SetGraphicsAPIs(target, target == BuildTarget.Android
            ? new[] { GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan }
            : new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12 });
        string output = Path.Combine(Runtime, "Build", platform);
        Directory.CreateDirectory(output);
        var manifest = BuildPipeline.BuildAssetBundles(output, new[] {
            new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { PrefabPath }, addressableNames = new[] { PrefabPath.ToLowerInvariant() } }
        }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
        if (manifest == null || manifest.GetAllDependencies(BundleName).Length != 0)
            throw new InvalidOperationException("Arid Basin must build as a self-contained bundle");
        var report = new Report { unity_version = Application.unityVersion, platform = platform,
            vertices = vertexCount, triangles = triangleCount, hash = manifest.GetAssetBundleHash(BundleName).ToString(),
            build_verified = true, center_open = true, assets = new[] { PrefabPath.ToLowerInvariant() } };
        if (target == BuildTarget.StandaloneWindows64)
        {
            var bundle = AssetBundle.LoadFromFile(Path.Combine(output, BundleName));
            if (bundle == null) throw new InvalidOperationException("Built Windows bundle did not load");
            try
            {
                var loaded = bundle.LoadAsset<GameObject>(PrefabPath.ToLowerInvariant());
                Debug.Log("[AridBasin] Loaded prefab: " + (loaded != null) + "; mesh=" +
                    (loaded == null ? -1 : loaded.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount) + "; material=" +
                    (loaded != null && loaded.GetComponentInChildren<MeshRenderer>().sharedMaterial != null));
                if (loaded == null || loaded.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount < 1000 ||
                    loaded.GetComponentInChildren<MeshFilter>().sharedMesh.triangles.Length != triangleCount * 3 ||
                    loaded.GetComponentInChildren<MeshRenderer>().sharedMaterial == null)
                    throw new InvalidOperationException("Built terrain prefab lost its mesh or material");
                report.bundle_load_verified = true;
                report.assets = bundle.GetAllAssetNames();
            }
            finally { bundle.Unload(true); }
        }
        File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
        Debug.Log("[AridBasin] " + platform + ": " + triangleCount + " mountain triangles; open central arena.");
    }
}

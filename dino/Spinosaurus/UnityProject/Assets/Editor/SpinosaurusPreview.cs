using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SpinosaurusPreview
{
    public static void Run() {
        AssetBundle framework = null, bundle = null;
        try {
            var source = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
            var dir = Path.Combine(source, "AssetBundles/Windows");
            framework = AssetBundle.LoadFromFile(Path.Combine(dir, "models$animals$spinosaurus$spinosaurus_framework.asset.bundle"));
            bundle = AssetBundle.LoadFromFile(Path.Combine(dir, "models$animals$spinosaurus$spinosaurusprefab.prefab.bundle"));
            if (framework == null || bundle == null) throw new Exception("Preview bundles could not be loaded.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var asset = bundle.GetAllAssetNames().Single(n => n.EndsWith("/spinosaurusprefab.prefab"));
            var instance = UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>(asset));
            var body = instance.GetComponent<AnimalBehavior>()._meshObject;
            var skin = body.GetComponentInChildren<SkinnedMeshRenderer>();
            var animation = body.GetComponent<Animation>();
            if (!skin.sharedMaterial.shader.isSupported) throw new Exception("Repaired Spino shader is not supported by the preview device.");
            var snapshot = new SpinosaurusGeometry.PoseSnapshot(body);
            var bounds = SpinosaurusGeometry.BoundsOf(SpinosaurusGeometry.WorldVertices(skin));
            float span = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            var camera = new GameObject("Spinosaurus preview camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .19f, .21f, 1);
            camera.orthographic = true;
            camera.orthographicSize = span * .4f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = span * 20;
            camera.transform.position = bounds.center + new Vector3(span * 1.2f, span * .35f, span * .55f);
            camera.transform.LookAt(bounds.center);
            camera.allowHDR = false;
            RenderSettings.fog = false;
            var output = Path.Combine(source, "Converted/ActorXRepair/Unity");
            Directory.CreateDirectory(output);
            var atlas = new Texture2D(1920, 1280, TextureFormat.RGB24, false);
            var poses = new[] { "REST", "Spino-Idle", "Spino-Move-Fwd", "Spino-Attack-Bite" };
            Color[] restPixels = null;
            for (int index = 0; index < poses.Length; index++) {
                snapshot.Restore();
                string pose = poses[index];
                if (pose != "REST") {
                    var clip = animation.GetClip(pose);
                    clip.SampleAnimation(body, clip.length * .5f);
                }
                // Camera.Render is called multiple times in one editor frame;
                // invalidate the skin buffer so every preview uses its new pose.
                var sourceMesh = skin.sharedMesh;
                skin.sharedMesh = null;
                skin.sharedMesh = sourceMesh;
                skin.forceMatrixRecalculationPerRender = true;
                skin.updateWhenOffscreen = true;
                var target = new RenderTexture(960, 640, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                target.antiAliasing = 4;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(960, 640, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0);
                image.Apply();
                var pixels = image.GetPixels();
                int brown = pixels.Count(p => p.r > p.b * 1.25f && p.r > .07f);
                if (brown < 1000) throw new Exception(pose + " preview is missing the textured model: colored pixels=" + brown);
                int changed = restPixels == null ? 0 : pixels.Zip(restPixels, (a, b) => Mathf.Abs(a.r-b.r) + Mathf.Abs(a.g-b.g) + Mathf.Abs(a.b-b.b)).Count(v => v > .04f);
                if (restPixels != null && changed < 500) throw new Exception(pose + " GPU preview did not update its skinned pose.");
                if (restPixels == null) restPixels = pixels;
                File.WriteAllBytes(Path.Combine(output, pose + ".png"), image.EncodeToPNG());
                atlas.SetPixels((index % 2) * 960, (1 - index / 2) * 640, 960, 640, pixels);
                Debug.Log("[SpinoRender] " + pose + " colored pixels=" + brown + ", changed from bind pose=" + changed);
                camera.targetTexture = null;
                RenderTexture.active = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
            atlas.Apply();
            File.WriteAllBytes(Path.Combine(output, "contact-sheet.png"), atlas.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(atlas);
            UnityEngine.Object.DestroyImmediate(instance);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            bundle.Unload(true); bundle = null;
            framework.Unload(true); framework = null;
            EditorApplication.Exit(0);
        } catch (Exception ex) {
            Debug.LogException(ex);
            if (bundle != null) bundle.Unload(true);
            if (framework != null) framework.Unload(true);
            EditorApplication.Exit(1);
        }
    }
}

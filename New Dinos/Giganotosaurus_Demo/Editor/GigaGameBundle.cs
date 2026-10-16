using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace Durango.GigaDemo
{
    // Builds an AssetBundle that stands in for the game's brachioprefab bundle (in-game integration test).
    // Legacy "Animation" component (like the game's animals), builtin Legacy Diffuse shader (resolved from the player's own builtin-extra).
    public static class GigaGameBundle
    {
        const string Root = "Assets/Giganotosaurus_Demo";
        public const string OrigName = "models$animals$brachio$brachioprefab.prefab.0331ce92897881bc42c1197f1905a726.bundle";
        public static float Scale = 300f;

        static AnimationClip Legacy(string name, AnimationClip src)
        {
            var c = new AnimationClip { name = name, legacy = true, wrapMode = WrapMode.Loop, frameRate = src.frameRate };
            EditorUtility.CopySerialized(src, c); c.name = name; c.legacy = true; c.wrapMode = WrapMode.Loop;
            return c;
        }

        // Walk speed (m/s, model space) from the real stride: slope of the planted foot's z over time during ground contact.
        static float WalkSpeed(GameObject model, AnimationClip walk)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(model);
            Transform T(string n) { foreach (var t in g.GetComponentsInChildren<Transform>()) if (t.name == n) return t; return null; }
            var sum = 0f; int cnt = 0;
            foreach (var fn in new[] { "l_ankle", "r_ankle" })
            {
                var f = T(fn); var ts = new System.Collections.Generic.List<float>(); var ys = new System.Collections.Generic.List<float>(); var zs = new System.Collections.Generic.List<float>();
                for (float t = 0; t < walk.length; t += walk.length / 50f) { walk.SampleAnimation(g, t); var p = g.transform.InverseTransformPoint(f.position); ts.Add(t); ys.Add(p.y); zs.Add(p.z); }
                float ymin = ys.Min(), n = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
                for (int i = 0; i < ts.Count; i++) if (ys[i] < ymin + 0.08f) { n++; sx += ts[i]; sy += zs[i]; sxx += ts[i] * ts[i]; sxy += ts[i] * zs[i]; }
                if (n > 3) { float slope = (n * sxy - sx * sy) / (n * sxx - sx * sx); sum += -slope; cnt++; Debug.Log($"[Giga] stride {fn}: contact samples {n}, z-slope {slope:F3} m/s, ymin {ymin:F3}"); }
            }
            Object.DestroyImmediate(g);
            return cnt > 0 ? Mathf.Clamp(sum / cnt, 0.3f, 6f) : 1.5f;
        }

        // Clip = sequence of segments (clip, loops, moving?) + a stadium path (straight, 180deg arc, straight back, arc) baked into the root transform.
        static AnimationClip Demo(System.Collections.Generic.List<(AnimationClip clip, int loops, string kind)> seq, float v, float R)
        {
            var c = new AnimationClip { name = "GigaDemo", legacy = true, wrapMode = WrapMode.Loop, frameRate = seq[0].clip.frameRate };
            float off = 0;
            var tPos = new System.Collections.Generic.List<Keyframe>[3]; for (int i = 0; i < 3; i++) tPos[i] = new System.Collections.Generic.List<Keyframe>();
            var qk = new System.Collections.Generic.List<Keyframe>[4]; for (int i = 0; i < 4; i++) qk[i] = new System.Collections.Generic.List<Keyframe>();
            float x = 0, z = 0, yaw = 90f * Mathf.Deg2Rad;   // +X = away from the Brachio
            Quaternion qPrev = Quaternion.identity; bool first = true;
            void Key(float t) { tPos[0].Add(new Keyframe(t, x)); tPos[1].Add(new Keyframe(t, 0f)); tPos[2].Add(new Keyframe(t, z));
                var q = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0); if (!first && Quaternion.Dot(q, qPrev) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); qPrev = q; first = false;
                qk[0].Add(new Keyframe(t, q.x)); qk[1].Add(new Keyframe(t, q.y)); qk[2].Add(new Keyframe(t, q.z)); qk[3].Add(new Keyframe(t, q.w)); }
            Key(0);
            foreach (var (src, loops, kind) in seq)
            {
                for (int i = 0; i < loops; i++)
                {
                    foreach (var b in AnimationUtility.GetCurveBindings(src))
                    {
                        var cv = AnimationUtility.GetEditorCurve(src, b);
                        var existing = AnimationUtility.GetEditorCurve(c, b) ?? new AnimationCurve();
                        foreach (var k in cv.keys) { var kk = k; kk.time += off; existing.AddKey(kk); }
                        AnimationUtility.SetEditorCurve(c, b, existing);
                    }
                    float dur = src.length; int steps = Mathf.Max(1, Mathf.RoundToInt(dur / 0.1f)); float dt = dur / steps;
                    for (int s = 1; s <= steps; s++)
                    {
                        if (kind == "straight") { x += Mathf.Sin(yaw) * v * dt; z += Mathf.Cos(yaw) * v * dt; }
                        else if (kind == "turn") { float w = v / R; float mid = yaw - w * dt * 0.5f; x += Mathf.Sin(mid) * v * dt; z += Mathf.Cos(mid) * v * dt; yaw -= w * dt; }
                        Key(off + s * dt);
                    }
                    off += dur;
                }
            }
            Debug.Log($"[Giga] path end error x={x:F2} z={z:F2} yaw={(yaw * Mathf.Rad2Deg):F1} total={off:F1}s");
            string[] pn = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" }; string[] qn = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            for (int i = 0; i < 3; i++) AnimationUtility.SetEditorCurve(c, EditorCurveBinding.FloatCurve("", typeof(Transform), pn[i]), new AnimationCurve(tPos[i].ToArray()));
            for (int i = 0; i < 4; i++) AnimationUtility.SetEditorCurve(c, EditorCurveBinding.FloatCurve("", typeof(Transform), qn[i]), new AnimationCurve(qk[i].ToArray()));
            return c;
        }

        public static void Run()
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(Root + "/Mesh/Giganotosaurus_Demo.fbx");
            var idle = all.OfType<AnimationClip>().First(c => c.name == "Idle");
            var walk = all.OfType<AnimationClip>().First(c => c.name == "Move-Fwd");

            // uncompressed + 1024 max: the bundle is a PC-target build loaded on Android
            var ti = (TextureImporter)AssetImporter.GetAtPath(Root + "/Textures/giga_d_colorized.png");
            var ps = ti.GetPlatformTextureSettings("Standalone"); ps.overridden = true; ps.maxTextureSize = 1024; ps.format = TextureImporterFormat.RGBA32;
            ti.SetPlatformTextureSettings(ps); ti.mipmapEnabled = true; ti.SaveAndReimport();

            var ni = (TextureImporter)AssetImporter.GetAtPath(Root + "/Textures/giga_N_game.png");
            ni.textureType = TextureImporterType.Default; ni.sRGBTexture = false; ni.mipmapEnabled = true; ni.textureCompression = TextureImporterCompression.Uncompressed;
            var nps = ni.GetPlatformTextureSettings("Standalone"); nps.overridden = true; nps.maxTextureSize = 1024; nps.format = TextureImporterFormat.RGBA32; ni.SetPlatformTextureSettings(nps); ni.SaveAndReimport();
            var mat = new Material(Shader.Find("Standard")) { name = "Giganotosaurus_Demo_Game_Mat" };
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.0f); AssetDatabase.ImportAsset(Root + "/Textures/giga_subtex.png");
            var si = (TextureImporter)AssetImporter.GetAtPath(Root + "/Textures/giga_subtex.png"); si.mipmapEnabled = false; si.textureCompression = TextureImporterCompression.Uncompressed; si.sRGBTexture = false; si.SaveAndReimport();
            mat.SetTexture("_DetailMask", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/giga_subtex.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/giga_N_game.png"));
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/giga_d_colorized.png");
            AssetDatabase.CreateAsset(mat, Root + "/Materials/Giganotosaurus_Demo_Game_Mat.mat");

            string ap = Root + "/Animations/GigaDemo_Legacy.anim";
            AnimationClip G(string n) => all.OfType<AnimationClip>().First(c => c.name == n);
            var modelGO = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Mesh/Giganotosaurus_Demo.fbx");
            float v = WalkSpeed(modelGO, G("Move-Fwd")); Debug.Log("[Giga] walk speed m/s = " + v);
            int turnLoops = Mathf.Max(1, Mathf.RoundToInt(Mathf.PI * 1.8f / v / G("Move-Fwd").length)); float R = v * turnLoops * G("Move-Fwd").length / Mathf.PI; Debug.Log("[Giga] turn radius m = " + R + " loops " + turnLoops);
            var demo = Demo(new System.Collections.Generic.List<(AnimationClip, int, string)> {
                (G("Idle"),1,"stay"), (G("Eat"),1,"stay"), (G("Move-Fwd"),5,"straight"), (G("Move-Fwd"),turnLoops,"turn"), (G("Roar"),1,"stay"),
                (G("Move-Fwd"),5,"straight"), (G("Move-Fwd"),turnLoops,"turn"), (G("Idle"),1,"stay"), (G("Startled"),1,"stay") }, v, R);
            AssetDatabase.CreateAsset(demo, ap);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Mesh/Giganotosaurus_Demo.fbx");
            var rootGO = new GameObject("BrachioPrefab"); // keeps the game's name; contents are Giganotosaurus_Demo
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model); inst.name = "Giganotosaurus_Demo";
            inst.transform.SetParent(rootGO.transform, false);
            Object.DestroyImmediate(inst.GetComponent<Animator>());
            foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.sharedMaterials = new[] { mat }; r.updateWhenOffscreen = true;
                var fm = Object.Instantiate(r.sharedMesh); fm.name = "Giga_LOD0_flipped";
                for (int sm = 0; sm < fm.subMeshCount; sm++) { var tr = fm.GetTriangles(sm); for (int i = 0; i + 2 < tr.Length; i += 3) { int t = tr[i]; tr[i] = tr[i + 2]; tr[i + 2] = t; } fm.SetTriangles(tr, sm); }
                fm.RecalculateTangents();
                AssetDatabase.DeleteAsset(Root + "/Mesh/Giga_LOD0_flipped.asset"); AssetDatabase.CreateAsset(fm, Root + "/Mesh/Giga_LOD0_flipped.asset");
                r.sharedMesh = fm;
            }
            rootGO.transform.localScale = Vector3.one * Scale;
            bool standalone = System.Environment.GetEnvironmentVariable("GIGA_STANDALONE") == "1";
            rootGO.AddComponent<Durango.Render.PlaneShadows>();   // the game binds its real planar-shadow script by identity
            var an = inst.AddComponent<Animation>();
            an.AddClip(demo, "GigaDemo"); an.clip = demo; an.playAutomatically = true; an.wrapMode = WrapMode.Loop;
            an.cullingType = AnimationCullingType.AlwaysAnimate;
            if (standalone)
            {
                rootGO.name = "Giganotosaurus_Demo";
                // real ARK sounds baked on the circuit timeline (Tools/giga_audio_timeline.py), Vorbis in the bundle
                string wp = Root + "/Audio/giga_timeline.wav"; AssetDatabase.ImportAsset(wp);
                var ai = (AudioImporter)AssetImporter.GetAtPath(wp); var asx = ai.defaultSampleSettings; asx.loadType = AudioClipLoadType.DecompressOnLoad; asx.compressionFormat = AudioCompressionFormat.Vorbis; asx.quality = 0.7f; ai.defaultSampleSettings = asx; ai.forceToMono = false; ai.loadInBackground = false; ai.SaveAndReimport();
                var src = inst.AddComponent<AudioSource>();
                src.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(wp); src.loop = true; src.playOnAwake = true; src.volume = 1f;
                src.spatialBlend = float.TryParse(System.Environment.GetEnvironmentVariable("GIGA_SPATIAL"), out var sb) ? sb : 0.6f;
                src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 4000f; src.maxDistance = 60000f; src.dopplerLevel = 0f;
                string sp = Root + "/Prefab/Giganotosaurus_Demo_Standalone.prefab";
                PrefabUtility.SaveAsPrefabAsset(rootGO, sp); Object.DestroyImmediate(rootGO);
                var one = new AssetBundleBuild { assetBundleName = "giganotosaurus_demo.bundle", assetNames = new[] { sp }, addressableNames = new[] { "giganotosaurus_demo" } };
                string od1 = "D:/Games/Durango/Brachio_Unity6/giga_standalone"; Directory.CreateDirectory(od1);
                var m1 = BuildPipeline.BuildAssetBundles(od1, new[] { one }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
                Debug.Log("[Giga] standalone bundle built: " + (m1 != null) + " clip=" + one.assetBundleName);
                return;
            }
            string pp = Root + "/Prefab/Giganotosaurus_Demo_Game.prefab";
            PrefabUtility.SaveAsPrefabAsset(rootGO, pp); Object.DestroyImmediate(rootGO);
            var variants = new[] {
                ("models$ancora$animals$brachio_ancora.prefab.bundle", "assets/models/ancora/animals/brachio_ancora.prefab", 300f, 0f),
                ("models$ancora$animals$brachiosmall_ancora.prefab.bundle", "assets/models/ancora/animals/brachiosmall_ancora.prefab", 300f, 150f) };
            var builds = new List<AssetBundleBuild>();
            foreach (var (bn, addr, sc, yaw) in variants)
            {
                string vp = Root + "/Prefab/Giga_" + Path.GetFileNameWithoutExtension(addr) + ".prefab";
                AssetDatabase.DeleteAsset(vp); AssetDatabase.CopyAsset(pp, vp);
                var g = PrefabUtility.LoadPrefabContents(vp); g.transform.localScale = Vector3.one * sc; g.transform.GetChild(0).localRotation = Quaternion.Euler(0, yaw, 0); PrefabUtility.SaveAsPrefabAsset(g, vp); PrefabUtility.UnloadPrefabContents(g);
                builds.Add(new AssetBundleBuild { assetBundleName = bn, assetNames = new[] { vp }, addressableNames = new[] { addr } });
            }
            string od = "D:/Games/Durango/Brachio_Unity6/override_giga"; Directory.CreateDirectory(od);
            var m = BuildPipeline.BuildAssetBundles(od, builds.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
            Debug.Log("[Giga] bundle built: " + (m != null) + " demoLen=" + demo.length);
        }
    }
}

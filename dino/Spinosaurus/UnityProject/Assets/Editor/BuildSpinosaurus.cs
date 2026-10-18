using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BuildSpinosaurus
{
    const string Root = "Assets/Models/Animals/Spinosaurus";
    const string Meshes = Root + "/Meshes";
    const string Materials = Root + "/Materials";
    const string Animations = Root + "/Animations";
    const string BodyPath = Meshes + "/SpinosaurusBody.fbx";
    const string ClassicPath = Meshes + "/SpinosaurusClassic.fbx";
    const string SaddlePath = Meshes + "/SpinosaurusSaddle.fbx";
    const string PrefabPath = Root + "/SpinosaurusPrefab.prefab";
    const string FrameworkPath = Root + "/Spinosaurus_framework.asset";
    const string EventsPath = Root + "/Spinosaurus_ae.asset";
    const string ClassicPrefabPath = Root + "/SpinosaurusClassicPrefab.prefab";
    const string BundleName = "models$animals$spinosaurus$spinosaurusprefab.prefab.bundle";
    const string FrameworkBundleName = "models$animals$spinosaurus$spinosaurus_framework.asset.bundle";
    const bool ClassicVisualTestOnly = false;

    [Serializable] class Report {
        public string unityVersion, prefab, activeModel, classicPrefab, unityPackage, windowsBundle, androidBundle;
        public int bodyBones, classicBones, saddleBones, matchedSaddleBones, animationClips, idleBindingPaths;
        public string[] clipNames;
        public string[] warnings;
    }

    static string SourceRoot { get { return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName; } }
    static string Source(string relative) { return Path.Combine(SourceRoot, relative); }

    [MenuItem("Durango/Spinosaurus/Convert and build")]
    public static void Run()
    {
        try {
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory(Animations);
            ConfigureModel(BodyPath, true);
            ConfigureModel(ClassicPath, false);
            ConfigureModel(SaddlePath, false);
            ConfigureNormal(Root + "/Textures/BodyNew/Spino_N.png");
            ConfigureNormal(Root + "/Textures/BodyClassic/spino_N.png");
            ConfigureNormal(Root + "/Textures/Saddle/T_SpinoSaddle_N.png");

            var bodyMaterial = MakeMaterial("SpinosaurusBody", Root + "/Textures/BodyNew/Spino_colorized_D.png", Root + "/Textures/BodyNew/Spino_N.png");
            bodyMaterial.color = new Color(.72f, .60f, .38f, 1f);
            var classicMaterial = MakeMaterial("SpinosaurusClassic", Root + "/Textures/BodyClassic/spino_Colorize_d.png", Root + "/Textures/BodyClassic/spino_N.png");

            var sourceClips = AssetDatabase.LoadAllAssetsAtPath(BodyPath).OfType<AnimationClip>()
                .Where(c => c.name.Contains("Spino-") && !c.name.StartsWith("__preview__"))
                .GroupBy(c => ClipName(c.name)).Select(g => g.First()).OrderBy(c => ClipName(c.name)).ToArray();
            if (sourceClips.Length != 49) throw new Exception("Expected 49 imported animations, got " + sourceClips.Length + ": " + string.Join(", ", sourceClips.Select(c => c.name)));
            var clips = new List<AnimationClip>();
            foreach (var sourceClip in sourceClips) {
                string name = ClipName(sourceClip.name);
                string path = Animations + "/" + name + ".anim";
                var clip = ClassicVisualTestOnly ? new AnimationClip() : UnityEngine.Object.Instantiate(sourceClip);
                clip.name = name;
                clip.legacy = true;
                clip.frameRate = sourceClip.frameRate;
                clip.wrapMode = name.Contains("Idle") || name.Contains("Move") || name.Contains("Swim-Fwd") ? WrapMode.Loop : WrapMode.Once;
                if (ClassicVisualTestOnly) {
                    var classicRootBone = AssetDatabase.LoadAssetAtPath<GameObject>(ClassicPath)
                        .transform.Find("SpinosaurusClassicRig/root");
                    if (classicRootBone == null) throw new Exception("Classic root bone missing.");
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve("SpinosaurusRig/root", typeof(Transform), "m_LocalPosition.x"),
                        AnimationCurve.Linear(0, classicRootBone.localPosition.x,
                            Mathf.Max(sourceClip.length, 1f / sourceClip.frameRate), classicRootBone.localPosition.x));
                }
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(clip, path);
                clips.Add(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
            }

            var framework = ScriptableObject.CreateInstance<AnimalFrameworkResource>();
            string frameworkJson = File.ReadAllText("Assets/Editor/framework-data.json");
            frameworkJson = System.Text.RegularExpressions.Regex.Replace(frameworkJson,
                "\"(SameAbove|bound_enemy|use_target_origin)\": ([01])",
                m => "\"" + m.Groups[1].Value + "\": " + (m.Groups[2].Value == "1" ? "true" : "false"));
            JsonUtility.FromJsonOverwrite(frameworkJson, framework);
            framework.name = "Spinosaurus_framework";
            BindFrameworkClips(framework, clips);
            SaveAsset(framework, FrameworkPath);
            framework = AssetDatabase.LoadAssetAtPath<AnimalFrameworkResource>(FrameworkPath);

            var events = ScriptableObject.CreateInstance<AnimationEventResource>();
            events.name = "Spinosaurus_ae";
            events.AnimationEventPairs = clips.Select(c => new AnimationEventPair {
                Name = c.name, Infos = new List<AnimationEventInfo>() }).ToList();
            SaveAsset(events, EventsPath);

            var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
            var classicPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClassicPath);
            var saddlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SaddlePath);
            if (bodyPrefab == null || classicPrefab == null || saddlePrefab == null) throw new Exception("One or more FBX models failed import.");

            var root = new GameObject("SpinosaurusPrefab");
            var body = (GameObject)PrefabUtility.InstantiatePrefab(ClassicVisualTestOnly ? classicPrefab : bodyPrefab);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            var activeRig = body.transform.Find(ClassicVisualTestOnly ? "SpinosaurusClassicRig" : "SpinosaurusRig");
            if (activeRig == null) throw new Exception("Active Spinosaurus rig root missing.");
            var bodyRenderer = SingleSkin(body, "body");
            int bodyBoneCount = bodyRenderer.bones.Length;
            bodyRenderer.sharedMaterial = ClassicVisualTestOnly ? classicMaterial : bodyMaterial;
            bodyRenderer.updateWhenOffscreen = true;
            foreach (var clip in clips)
                if (clip.length <= 0 || AnimationUtility.GetCurveBindings(clip).Length == 0)
                    throw new Exception("Animation has no usable curves: " + clip.name);
            var idlePaths = AnimationUtility.GetCurveBindings(clips.Single(c => c.name == "Spino-Idle"))
                .Select(binding => binding.path).Distinct().ToArray();
            if (ClassicVisualTestOnly) {
                foreach (var boneName in new[] { "l_forearmTwist", "r_forearmTwist", "transform5" }) {
                    var source = bodyPrefab.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(t => t.name == boneName);
                    if (source == null) throw new Exception("Compatibility bone source missing: " + boneName);
                    var parent = body.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(t => t.name == source.parent.name);
                    if (parent == null) throw new Exception("Compatibility bone parent missing: " + boneName);
                    var bone = new GameObject(source.name).transform;
                    bone.SetParent(parent, false);
                    bone.localPosition = source.localPosition;
                    bone.localRotation = source.localRotation;
                    bone.localScale = source.localScale;
                }
            }
            var missingPaths = clips.SelectMany(AnimationUtility.GetCurveBindings)
                .Select(binding => binding.path).Distinct()
                .Where(path => body.transform.Find(path) == null).ToArray();
            if (missingPaths.Length > 0) throw new Exception("Animation paths missing in body rig: " + string.Join(", ", missingPaths));
            var bodyBones = body.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var animation = body.GetComponent<Animation>();
            if (animation == null) animation = body.AddComponent<Animation>();
            var importedNames = animation.Cast<AnimationState>().Select(state => state.name).ToArray();
            foreach (var name in importedNames) animation.RemoveClip(name);
            foreach (var clip in clips) animation.AddClip(clip, clip.name);
            animation.clip = clips.Single(c => c.name == "Spino-Idle");
            animation.playAutomatically = false;
            animation.cullingType = AnimationCullingType.AlwaysAnimate;
            var animal = root.AddComponent<AnimalBehavior>();
            animal.AnimalFrameworkResource = framework;
            animal._meshObject = body;
            animal._xRadius = 110; animal._yRadius = 200; animal._size = 1;
            animal._bushWhackEffectPeriod = .5f;
            animal._bushWhackEffectOffset = new Vector3(0, 120, 0);
            animal._hitEffect = new IntegratedEffectType { Path = "" };
            animal._bodyPartBip001 = bodyRenderer.rootBone.name;
            animal._bodyPartHead = FindBone(body, "head");
            animal._bodyPartBody = FindBone(body, "spine");
            animal._bodyPartArm = FindBone(body, "hand_l");
            animal._bodyPartArmR = FindBone(body, "hand_r");
            animal._bodyPartLeg = FindBone(body, "foot_l");
            animal._bodyPartLegR = FindBone(body, "foot_r");
            animal._bodyPartTail = FindBone(body, "tail");
            animal._bodyPartBack = animal._bodyPartBody;
            animal._closedEyes = new List<Renderer>();
            animal._nightEyes = new List<GameObject>();
            animal._eyeTrail = new List<GameObject>();
            animal.UIViewScale = 1.2f;
            animal._lootableRimMax = 20; animal._lootableRimMin = 5;
            animal._lootableRimIntensity = 2;
            root.AddComponent<Durango.Render.PlaneShadows>();
            var ambient = root.AddComponent<Durango.Environment.AmbientLighting>();
            ambient._maxWetness = .65f;
            ambient._wetParticleOnThreashold = .5f;
            ambient._wetParticleType = new ParticleType { Path = "" };
            var flinch = root.AddComponent<Durango.Model.BoneFlinchingController>();
            flinch._isLimbCanceling = true; flinch._numIter = 3;
            flinch._cullDistance = 1000; flinch._flinchDeg = 1;
            flinch._flinchDuration = .2f;
            var exporter = root.AddComponent<Durango.Model.RootMotionExporter>();
            exporter._deadband = 5; exporter._deadbandYaw = 5;
            exporter._rootMotionBoneName = animal._bodyPartBip001;
            exporter._rootMotionForward = Vector3.forward;
            root.AddComponent<AnimationEventController>().AnimationEventResource =
                AssetDatabase.LoadAssetAtPath<AnimationEventResource>(EventsPath);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);

            var classicRoot = new GameObject("SpinosaurusClassicPrefab");
            var classic = (GameObject)PrefabUtility.InstantiatePrefab(classicPrefab);
            classic.name = "BodyClassic";
            classic.transform.SetParent(classicRoot.transform, false);
            SingleSkin(classic, "classic body").sharedMaterial = classicMaterial;
            PrefabUtility.SaveAsPrefabAsset(classicRoot, ClassicPrefabPath);
            UnityEngine.Object.DestroyImmediate(classicRoot);
            AssetDatabase.SaveAssets();

            var importer = AssetImporter.GetAtPath(PrefabPath);
            importer.assetBundleName = BundleName;
            importer.SaveAndReimport();
            var frameworkImporter = AssetImporter.GetAtPath(FrameworkPath);
            frameworkImporter.assetBundleName = FrameworkBundleName;
            frameworkImporter.SaveAndReimport();
            var packagePath = Source("Spinosaurus_Unity6.unitypackage");
            AssetDatabase.ExportPackage(Root, packagePath, ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);

            var report = new Report {
                unityVersion = Application.unityVersion,
                prefab = PrefabPath,
                activeModel = ClassicVisualTestOnly ? ClassicPath : BodyPath,
                classicPrefab = ClassicPrefabPath,
                unityPackage = Path.GetFileName(packagePath),
                bodyBones = bodyBoneCount,
                classicBones = SingleSkin(classicPrefab, "classic body").bones.Length,
                saddleBones = SingleSkin(saddlePrefab, "saddle source").bones.Length,
                matchedSaddleBones = 0,
                animationClips = clips.Count,
                idleBindingPaths = idlePaths.Length,
                clipNames = clips.Select(c => c.name).ToArray(),
                warnings = ClassicVisualTestOnly ? new[] {
                    "Classic visual test mode is enabled; imported clips do not animate the Classic rig."
                } : new[] {
                    "The Spino_new mesh uses its matching 94-bone rig and imported PSA clips.",
                    "The wild prefab excludes the saddle mesh."
                }
            };
            try { BuildBundles(BuildTarget.StandaloneWindows64, "Windows"); report.windowsBundle = VerifyWindowsBundle(); }
            catch (Exception ex) { report.windowsBundle = "failed: " + ex.Message; Debug.LogWarning("[Spinosaurus] Windows bundle: " + ex); }
            try { BuildBundles(BuildTarget.Android, "Android"); report.androidBundle = "built"; }
            catch (Exception ex) { report.androidBundle = "failed: " + ex.Message; Debug.LogWarning("[Spinosaurus] Android bundle: " + ex); }
            File.WriteAllText(Source("Unity_conversion_report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[Spinosaurus] Unity 6 conversion complete: " + report.bodyBones + " body bones, " + report.saddleBones + " saddle bones, " + report.animationClips + " clips.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        } catch (Exception ex) {
            Debug.LogException(ex);
            if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
        }
    }

    static string ClipName(string imported) {
        int start = imported.LastIndexOf("Spino-", StringComparison.Ordinal);
        return start < 0 ? imported : imported.Substring(start);
    }

    static void SaveAsset(UnityEngine.Object obj, string path) {
        var existing = AssetDatabase.LoadMainAssetAtPath(path);
        if (existing != null) {
            EditorUtility.CopySerialized(obj, existing);
            UnityEngine.Object.DestroyImmediate(obj);
        } else AssetDatabase.CreateAsset(obj, path);
    }

    static string FindBone(GameObject model, string part) {
        var bone = model.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
        return bone != null ? bone.name : model.transform.name;
    }

    static void BindFrameworkClips(AnimalFrameworkResource fw, IEnumerable<AnimationClip> clips) {
        var byName = clips.ToDictionary(c => c.name);
        var serialized = new SerializedObject(fw);
        var iterator = serialized.GetIterator();
        var fields = new Dictionary<string,string> {
            {"_clipObj", "motion"}, {"_clipObjBegin", "begin"},
            {"_clipObjLooping", "during"}, {"_clipObjEnd", "end"},
            {"_clipObjMove", "motion"}, {"_clipObjRotateCW", "rot_motion_cw"},
            {"_clipObjRotateCCW", "rot_motion_ccw"},
            {"_clipObjTurnReverse", "turn_reverse_motion"},
            {"_clipObjFront", "front"}, {"_clipObjBack", "back"},
            {"_clipObjLeft", "left"}, {"_clipObjRight", "right"}
        };
        while (iterator.Next(true)) {
            string sibling;
            if (iterator.propertyType != SerializedPropertyType.ObjectReference ||
                !fields.TryGetValue(iterator.name, out sibling)) continue;
            string path = iterator.propertyPath;
            var motion = serialized.FindProperty(path.Substring(0, path.LastIndexOf('.') + 1) + sibling);
            if (motion == null || string.IsNullOrEmpty(motion.stringValue)) continue;
            AnimationClip clip;
            if (!byName.TryGetValue(motion.stringValue, out clip))
                throw new Exception("Unresolved framework motion: " + motion.stringValue);
            iterator.objectReferenceValue = clip;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureModel(string path, bool animations) {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new Exception("Model importer missing: " + path);
        importer.animationType = ModelImporterAnimationType.Legacy;
        importer.importAnimation = animations;
        importer.SaveAndReimport();
    }

    static void ConfigureNormal(string path) {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new Exception("Texture importer missing: " + path);
        importer.textureType = TextureImporterType.NormalMap;
        importer.SaveAndReimport();
    }

    static Material MakeMaterial(string name, string diffusePath, string normalPath) {
        var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (diffuse == null || normal == null) throw new Exception("Material texture missing: " + name);
        var shader = Shader.Find("Standard");
        if (shader == null) throw new Exception("Unity Standard shader unavailable.");
        var path = Materials + "/" + name + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
        var material = new Material(shader) { name = name };
        material.SetTexture("_MainTex", diffuse);
        material.SetTexture("_BumpMap", normal);
        material.EnableKeyword("_NORMALMAP");
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static SkinnedMeshRenderer SingleSkin(GameObject model, string label) {
        var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (skins.Length != 1 || skins[0].sharedMesh == null) throw new Exception("Expected one skinned mesh for " + label + ", got " + skins.Length);
        return skins[0];
    }

    static void BuildBundles(BuildTarget target, string platform) {
        string dir = Source("AssetBundles/" + platform);
        Directory.CreateDirectory(dir);
        if (BuildPipeline.BuildAssetBundles(dir, BuildAssetBundleOptions.ChunkBasedCompression, target) == null)
            throw new Exception("AssetBundle build failed for " + platform);
    }

    static string VerifyWindowsBundle() {
        string path = Source("AssetBundles/Windows/" + BundleName);
        var frameworkBundle = AssetBundle.LoadFromFile(Source("AssetBundles/Windows/" + FrameworkBundleName));
        if (frameworkBundle == null) throw new Exception("Framework bundle could not be opened.");
        var bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null) { frameworkBundle.Unload(true); throw new Exception("Windows AssetBundle could not be opened."); }
        try {
            var assetName = bundle.GetAllAssetNames().Single(n => n.EndsWith("/spinosaurusprefab.prefab"));
            var prefab = bundle.LoadAsset<GameObject>(assetName);
            if (prefab == null) throw new Exception("Spinosaurus prefab missing from bundle.");
            var skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var animation = prefab.GetComponentInChildren<Animation>(true);
            var animal = prefab.GetComponent<AnimalBehavior>();
            if (skins.Length != 1 || animation == null || animation.GetClipCount() != 49)
                throw new Exception("Bundle prefab content mismatch: skins=" + skins.Length + ", clips=" + (animation == null ? -1 : animation.GetClipCount()));
            if (animal == null || animal.AnimalFrameworkResource == null || animal._meshObject == null)
                throw new Exception("AnimalBehavior or framework missing from root prefab.");
            var bodySkin = skins.SingleOrDefault(s => s.sharedMesh != null &&
                s.sharedMesh.name == "SpinosaurusBody" &&
                s.sharedMaterial != null && s.sharedMaterial.name == "SpinosaurusBody");
            if (bodySkin == null) throw new Exception("Animated Spino_new body mesh or material missing from Windows bundle.");
            var bodyWorldSize = bodySkin.bounds.size;
            if (bodySkin.sharedMesh.vertexCount < 1000 || bodyWorldSize.y < 1f ||
                bodyWorldSize.x < .5f || bodyWorldSize.z < 1f)
                throw new Exception("Active body is not a full 3D Spinosaurus mesh: vertices=" +
                    bodySkin.sharedMesh.vertexCount + ", world bounds=" + bodyWorldSize + ".");
            if (prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Saddle"))
                throw new Exception("Wild Spinosaurus prefab unexpectedly contains the saddle.");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try {
                var animatedSkin = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(s => s.sharedMesh.name == "SpinosaurusBody");
                var animated = instance.GetComponentInChildren<Animation>(true);
                var rest = new Mesh();
                animatedSkin.BakeMesh(rest, false);
                float restEdge = LongestTriangleEdge(rest);
                foreach (var clipName in new[] { "Spino-Idle", "Spino-Move-Fwd" }) {
                    animated.Play(clipName);
                    animated[clipName].time = animated[clipName].length * .5f;
                    animated.Sample();
                    var sample = new Mesh();
                    animatedSkin.BakeMesh(sample, false);
                    float edge = LongestTriangleEdge(sample);
                    if (edge > restEdge * 5f)
                        throw new Exception(clipName + " stretches Spino_new mesh: rest edge " + restEdge + ", animated edge " + edge);
                    if (clipName == "Spino-Move-Fwd" &&
                        !rest.vertices.Zip(sample.vertices, (a, b) => (a - b).sqrMagnitude).Any(delta => delta > .000001f))
                        throw new Exception("Spino-Move-Fwd does not deform the active body rig.");
                    UnityEngine.Object.DestroyImmediate(sample);
                }
                UnityEngine.Object.DestroyImmediate(rest);
            } finally { UnityEngine.Object.DestroyImmediate(instance); }
            return "built and verified";
        } finally { bundle.Unload(true); frameworkBundle.Unload(true); }
    }

    static float LongestTriangleEdge(Mesh mesh) {
        var vertices = mesh.vertices;
        var triangles = mesh.triangles;
        float longest = 0;
        for (int i = 0; i < triangles.Length; i += 3)
            longest = Mathf.Max(longest,
                Vector3.Distance(vertices[triangles[i]], vertices[triangles[i+1]]),
                Vector3.Distance(vertices[triangles[i+1]], vertices[triangles[i+2]]),
                Vector3.Distance(vertices[triangles[i+2]], vertices[triangles[i]]));
        return longest;
    }
}

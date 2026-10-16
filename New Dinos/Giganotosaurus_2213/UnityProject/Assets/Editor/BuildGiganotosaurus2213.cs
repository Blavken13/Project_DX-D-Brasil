using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

// The runtime scripts in ClientSchema describe serialization only. The installed
// client's own Assembly-CSharp supplies their behavior when the bundles are loaded.
public static class BuildGiganotosaurus2213
{
    const string Root = "Assets/Models/Animals/Giganotosaurus";
    const string PrefabPath = Root + "/GiganotosaurusPrefab.prefab";
    const string FrameworkPath = Root + "/Giganotosaurus_framework.asset";
    const string Prefix = "Giganotosaurus-";
    const float ModelScale = 100f; // FBX metres -> Durango centimetres. Keep the entity root at 1.
    const string PrefabBundle = "models$animals$giganotosaurus$giganotosaurusprefab.prefab.bundle";
    const string FrameworkBundle = "models$animals$giganotosaurus$giganotosaurus_framework.asset.bundle";

    [Serializable] public class ClipReport { public string name; public float duration; public float fps; public string wrap; public int curves; }
    [Serializable] public class AssetReport { public int entity_type=2213; public float model_scale=ModelScale; public int vertices; public int triangles; public int bones; public Vector3 bounds; public ClipReport[] clips; public string[] bone_names; }
    [Serializable] public class BundleReport { public string name; public string hash; public string[] dependencies; }
    [Serializable] public class BuildReport { public string unity_version; public string platform; public BundleReport[] bundles; }

    [MenuItem("Durango/Giganotosaurus 2213/Build Windows and Android")]
    public static void Run()
    {
        try
        {
            PrepareAssets();
            BuildTargetBundles(BuildTarget.StandaloneWindows64, "Windows");
            VerifyWindowsLoad();
            BuildTargetBundles(BuildTarget.Android, "Android");
            Debug.Log("[Giga2213] Both platform bundles generated successfully.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
        }
    }

    static string PackageRoot { get { return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName; } }

    static void SaveAsset(UnityEngine.Object obj, string path)
    {
        // Retain meta GUIDs, so rebuilds preserve all references.
        var existing = AssetDatabase.LoadMainAssetAtPath(path);
        if (existing != null) { EditorUtility.CopySerialized(obj, existing); UnityEngine.Object.DestroyImmediate(obj); }
        else AssetDatabase.CreateAsset(obj, path);
    }

    static void PrepareAssets()
    {
        Directory.CreateDirectory(Root + "/Animations");
        Directory.CreateDirectory(Root + "/Materials");
        AssetDatabase.Refresh();
        string fbxPath = Root + "/Mesh/Giganotosaurus.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        importer.animationType = ModelImporterAnimationType.Legacy;
        importer.importAnimation = true;
        importer.optimizeGameObjects = false;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.isReadable = true;
        importer.SaveAndReimport();

        var originals = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
        if (originals.Length != 43) throw new InvalidOperationException("Expected 43 FBX clips, found " + originals.Length);
        var clips = new Dictionary<string, AnimationClip>();
        var loop = new HashSet<string> { "Idle", "Move-Fwd", "Move-Lft", "Move-Rit", "Charge-Fwd", "Charge-Lft", "Charge-Rit", "Swim-Idle", "Swim-Fwd", "Torpid-Idle" };
        foreach (var src in originals)
        {
            var clip = UnityEngine.Object.Instantiate(src);
            clip.name = Prefix + src.name;
            clip.legacy = true;
            clip.wrapMode = loop.Contains(src.name) ? WrapMode.Loop : WrapMode.ClampForever;
            AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
            // Object displacement is authoritative on the server. Keep bone motion,
            // remove any curves on the model object's transform itself.
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.path == "" && binding.type == typeof(Transform)) AnimationUtility.SetEditorCurve(clip, binding, null);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop.Contains(src.name);
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            string path = Root + "/Animations/" + clip.name + ".anim";
            SaveAsset(clip, path);
            clips.Add(Prefix + src.name, AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
        }
        // User-approved death fallback: falling into torpor, clamped at the last frame.
        var die = UnityEngine.Object.Instantiate(clips[Prefix + "Torpid-In"]);
        die.name = Prefix + "Die";
        die.wrapMode = WrapMode.ClampForever;
        var deathSettings = AnimationUtility.GetAnimationClipSettings(die); deathSettings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(die, deathSettings);
        SaveAsset(die, Root + "/Animations/" + die.name + ".anim");
        clips.Add(Prefix + "Die", AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Animations/" + Prefix + "Die.anim"));

        var framework = ScriptableObject.CreateInstance<AnimalFrameworkResource>();
        string json = File.ReadAllText("Assets/Editor/framework-data.json");
        json = System.Text.RegularExpressions.Regex.Replace(json, "\"(SameAbove|bound_enemy|use_target_origin)\": ([01])", m => "\"" + m.Groups[1].Value + "\": " + (m.Groups[2].Value == "1" ? "true" : "false"));
        JsonUtility.FromJsonOverwrite(json, framework);
        framework.name = "Giganotosaurus_framework";
        BindFrameworkClips(framework, clips);
        SaveAsset(framework, FrameworkPath);
        framework = AssetDatabase.LoadAssetAtPath<AnimalFrameworkResource>(FrameworkPath);

        // This portable authoring material previews the original albedo. Runtime
        // packaging replaces it with LitSphere/DualMode/Animal and the native
        // litsphere_dino_giant matcap from each platform's preload bundle.
        var shader = Shader.Find("Durango/Animals/GiganotosaurusAuthoring");
        if (shader == null) throw new InvalidOperationException("Authoring preview shader is missing.");
        ConfigureTexture(Root + "/Textures/Giganotosaurus_Albedo.png", false);
        ConfigureTexture(Root + "/Textures/LitSphere_Dino_Giant.png", false);
        var material = new Material(shader) { name = "Giganotosaurus_Material" };
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Giganotosaurus_Albedo.png"));
        material.SetTexture("_LitSphereTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/LitSphere_Dino_Giant.png"));
        SaveAsset(material, Root + "/Materials/Giganotosaurus.mat");
        material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Giganotosaurus.mat");

        var entity = new GameObject("GiganotosaurusPrefab");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "GiganotosaurusModel";
        model.transform.SetParent(entity.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * ModelScale;
        var animator = model.GetComponent<Animator>();
        if (animator != null) UnityEngine.Object.DestroyImmediate(animator);
        var importedAnimation = model.GetComponent<Animation>();
        if (importedAnimation != null) UnityEngine.Object.DestroyImmediate(importedAnimation);
        var animation = model.AddComponent<Animation>();
        foreach (var clip in clips.Values) animation.AddClip(clip, clip.name);
        animation.clip = clips[Prefix + "Idle"];
        animation.playAutomatically = false;
        animation.cullingType = AnimationCullingType.BasedOnRenderers;

        var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin == null) throw new InvalidOperationException("SkinnedMeshRenderer missing.");
        // Recreate the demo's winding correction using this Legacy import's bind poses.
        var flipped = UnityEngine.Object.Instantiate(skin.sharedMesh);
        flipped.name = "GiganotosaurusMesh";
        for (int sm=0; sm<flipped.subMeshCount; sm++)
        {
            var indices = flipped.GetTriangles(sm);
            for (int k=0; k<indices.Length; k+=3) { int a=indices[k]; indices[k]=indices[k+2]; indices[k+2]=a; }
            flipped.SetTriangles(indices,sm);
        }
        flipped.RecalculateTangents();
        SaveAsset(flipped, Root + "/Mesh/GiganotosaurusMesh.asset");
        var corrected = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Mesh/GiganotosaurusMesh.asset");
        skin.sharedMesh = corrected;
        skin.sharedMaterials = new[] { material };
        skin.updateWhenOffscreen = true;

        var animal = entity.AddComponent<AnimalBehavior>();
        animal.AnimalFrameworkResource = framework;
        animal._meshObject = model;
        animal._xRadius = 110; animal._yRadius = 200; animal._size = 1;
        animal._bushWhackEffectPeriod = .5f; animal._bushWhackEffectOffset = new Vector3(0,120,0);
        animal._hitEffect = new IntegratedEffectType { Path = "" };
        animal._bodyPartHead = Bone(model, "c_head");
        animal._bodyPartBody = Bone(model, "c_back1");
        animal._bodyPartArm = Bone(model, "l_wrist");
        animal._bodyPartArmR = Bone(model, "r_wrist");
        animal._bodyPartLeg = Bone(model, "l_ankle");
        animal._bodyPartLegR = Bone(model, "r_ankle");
        animal._bodyPartTail = Bone(model, "c_tail3");
        animal._bodyPartBack = Bone(model, "c_back3");
        animal._bodyPartBip001 = Bone(model, "root");
        animal._processDepth = true;
        animal._closedEyes = new List<Renderer>(); animal._nightEyes = new List<GameObject>(); animal._eyeTrail = new List<GameObject>();
        animal.UIViewScale = 1.2f; animal._lootableRimMax = 20; animal._lootableRimMin = 5; animal._lootableRimIntensity = 2;
        entity.AddComponent<Durango.Render.PlaneShadows>();
        var ambient = entity.AddComponent<Durango.Environment.AmbientLighting>();
        ambient._maxWetness = .65f; ambient._wetParticleOnThreashold = .5f; ambient._wetParticleType = new ParticleType { Path = "" };
        var flinch = entity.AddComponent<Durango.Model.BoneFlinchingController>();
        flinch._isLimbCanceling = true; flinch._numIter = 3; flinch._cullDistance = 1000; flinch._flinchDeg = 1; flinch._flinchDuration = .2f;
        var exporter = entity.AddComponent<Durango.Model.RootMotionExporter>();
        exporter._deadband = 5; exporter._deadbandYaw = 5; exporter._rootMotionBoneName = animal._bodyPartBip001; exporter._rootMotionForward = Vector3.forward;
        var events = ScriptableObject.CreateInstance<AnimationEventResource>();
        events.name = "Giganotosaurus_ae";
        events.AnimationEventPairs = clips.Keys.Select(n => new AnimationEventPair { Name = n, Infos = new List<AnimationEventInfo>() }).ToList();
        SaveAsset(events, Root + "/Giganotosaurus_ae.asset");
        var eventController = entity.AddComponent<AnimationEventController>();
        eventController.AnimationEventResource = AssetDatabase.LoadAssetAtPath<AnimationEventResource>(Root + "/Giganotosaurus_ae.asset");

        VerifyBindings(model, clips);
        ExpandBounds(model, skin, clips.Values);
        clips[Prefix + "Idle"].SampleAnimation(model, 0);
        skin.updateWhenOffscreen = false;
        PrefabUtility.SaveAsPrefabAsset(entity, PrefabPath);
        var report = new AssetReport { vertices = corrected.vertexCount, triangles = corrected.triangles.Length/3,
            bones = skin.bones.Length, bounds = skin.bounds.size,
            bone_names = model.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray(),
            clips = clips.Values.OrderBy(c => c.name).Select(c => new ClipReport { name = c.name, duration = c.length, fps=c.frameRate, wrap=c.wrapMode.ToString(), curves=AnimationUtility.GetCurveBindings(c).Length }).ToArray() };
        Directory.CreateDirectory(Path.Combine(PackageRoot, "Metadata"));
        File.WriteAllText(Path.Combine(PackageRoot, "Metadata/unity-assets.json"), JsonUtility.ToJson(report,true));
        UnityEngine.Object.DestroyImmediate(entity);
        AssetDatabase.SaveAssets();
    }

    static string Bone(GameObject model, string name)
    {
        if (!model.GetComponentsInChildren<Transform>(true).Any(t => t.name == name))
            throw new InvalidOperationException("Missing required bone: " + name + ". Available: " + string.Join(",",model.GetComponentsInChildren<Transform>(true).Select(t=>t.name)));
        return name;
    }

    static void BindFrameworkClips(AnimalFrameworkResource fw, Dictionary<string, AnimationClip> clips)
    {
        var serialized = new SerializedObject(fw);
        var iterator = serialized.GetIterator();
        var fields = new Dictionary<string,string> { {"_clipObj","motion"}, {"_clipObjBegin","begin"}, {"_clipObjLooping","during"}, {"_clipObjEnd","end"}, {"_clipObjMove","motion"}, {"_clipObjRotateCW","rot_motion_cw"}, {"_clipObjRotateCCW","rot_motion_ccw"}, {"_clipObjTurnReverse","turn_reverse_motion"}, {"_clipObjFront","front"}, {"_clipObjBack","back"}, {"_clipObjLeft","left"}, {"_clipObjRight","right"} };
        while (iterator.Next(true))
        {
            string sibling;
            if (iterator.propertyType != SerializedPropertyType.ObjectReference || !fields.TryGetValue(iterator.name,out sibling)) continue;
            string path = iterator.propertyPath;
            var motion = serialized.FindProperty(path.Substring(0,path.LastIndexOf('.')+1) + sibling);
            if (motion == null || string.IsNullOrEmpty(motion.stringValue)) continue;
            AnimationClip clip;
            if (!clips.TryGetValue(motion.stringValue,out clip)) throw new InvalidOperationException("Unresolved motion " + motion.stringValue);
            iterator.objectReferenceValue = clip;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureTexture(string path, bool normal)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normal; importer.mipmapEnabled = true; importer.maxTextureSize = 512;
        foreach (string platform in new[] { "Standalone", "Android" })
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            settings.overridden = true; settings.maxTextureSize = 512;
            // RGBA32 works on all Android GPUs and keeps normal encoding platform-correct.
            settings.format = TextureImporterFormat.RGBA32;
            importer.SetPlatformTextureSettings(settings);
        }
        importer.SaveAndReimport();
    }

    static void VerifyBindings(GameObject model, Dictionary<string,AnimationClip> clips)
    {
        foreach (var clip in clips.Values)
        {
            if (!clip.legacy || clip.length <= 0) throw new InvalidOperationException("Invalid clip " + clip.name);
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.path != "" && model.transform.Find(binding.path) == null)
                    throw new InvalidOperationException(clip.name + " references absent transform " + binding.path);
        }
    }

    static void ExpandBounds(GameObject model, SkinnedMeshRenderer skin, IEnumerable<AnimationClip> clips)
    {
        var scale = model.transform.localScale;
        model.transform.localScale = Vector3.one;
        var baked = new Mesh(); bool first = true; var bounds = new Bounds();
        foreach (var clip in clips)
            for (int sample=0; sample<=8; sample++)
            {
                clip.SampleAnimation(model, clip.length * sample/8f);
                skin.BakeMesh(baked, false);
                foreach (var vertex in baked.vertices)
                {
                    if (first) { bounds = new Bounds(vertex,Vector3.zero); first=false; } else bounds.Encapsulate(vertex);
                }
            }
        bounds.Expand(bounds.size * .1f);
        skin.localBounds = bounds;
        model.transform.localScale = scale;
        UnityEngine.Object.DestroyImmediate(baked);
    }

    static void VerifyWindowsLoad()
    {
        string dir=Path.Combine(PackageRoot,"Build","Windows");
        var fwBundle=AssetBundle.LoadFromFile(Path.Combine(dir,FrameworkBundle));
        var modelBundle=AssetBundle.LoadFromFile(Path.Combine(dir,PrefabBundle));
        if (fwBundle==null || modelBundle==null) throw new InvalidOperationException("Cannot reload the generated Windows bundles.");
        var fw=fwBundle.LoadAsset<AnimalFrameworkResource>("giganotosaurus_framework");
        var prefab=modelBundle.LoadAsset<GameObject>("giganotosaurusprefab");
        if (fw==null || prefab==null) throw new InvalidOperationException("Bundle cannot be addressed with the names used by AssetBundleItemInfo.GetAssetName.");
        var instance=UnityEngine.Object.Instantiate(prefab);
        var animal=instance.GetComponent<AnimalBehavior>();
        if (animal==null || animal.AnimalFrameworkResource!=fw || animal._meshObject==null) throw new InvalidOperationException("Broken gameplay prefab references.");
        var animation=animal._meshObject.GetComponent<Animation>();
        foreach (var clip in fw.normal_motions)
            if (clip._clipObj==null || animation.GetClip(clip.motion)==null) throw new InvalidOperationException("Required clip not available at runtime: "+clip.motion);
        var skin=instance.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin.bones.Any(t=>t==null)) throw new InvalidOperationException("Missing skin bone in built prefab.");
        if (skin.bounds.size.magnitude > 5000 || skin.bounds.size.magnitude < 300) throw new InvalidOperationException("Invalid game-space bounds: "+skin.bounds.size);
        File.WriteAllText(Path.Combine(PackageRoot,"Metadata/windows-load-check.json"), "{\"bundle_loading\":true,\"loader_short_names\":true,\"framework_reference\":true,\"required_clips\":true,\"skin_bones\":true,\"game_scale\":true}");
        UnityEngine.Object.DestroyImmediate(instance);
        modelBundle.Unload(true); fwBundle.Unload(true);
    }

    static void BuildTargetBundles(BuildTarget target, string platform)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroupFor(target), target)) throw new InvalidOperationException("Unity module missing for " + target);
        if (target == BuildTarget.Android)
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);
            PlayerSettings.SetGraphicsAPIs(target,new[] { GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan });
        }
        else
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);
            PlayerSettings.SetGraphicsAPIs(target,new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12 });
        }
        // Explicitly assign clip ownership. Otherwise Unity duplicates the clips
        // referenced both by the framework and the prefab's Animation component.
        var frameworkAssets = new[] { FrameworkPath }.Concat(AssetDatabase.FindAssets("t:AnimationClip",new[] { Root+"/Animations" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p)).ToArray();
        var builds = new[] {
            new AssetBundleBuild { assetBundleName=FrameworkBundle, assetNames=frameworkAssets, addressableNames=frameworkAssets.Select(p=>p.ToLowerInvariant()).ToArray() },
            new AssetBundleBuild { assetBundleName=PrefabBundle, assetNames=new[] { PrefabPath }, addressableNames=new[] { PrefabPath.ToLowerInvariant() } }
        };
        string output = Path.Combine(PackageRoot,"Build",platform); Directory.CreateDirectory(output);
        var manifest = BuildPipeline.BuildAssetBundles(output,builds,BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,target);
        if (manifest==null) throw new InvalidOperationException("Bundle build failed for " + platform);
        var report = new BuildReport { unity_version=Application.unityVersion, platform=platform,
            bundles=manifest.GetAllAssetBundles().Select(n=>new BundleReport { name=n,hash=manifest.GetAssetBundleHash(n).ToString(),dependencies=manifest.GetAllDependencies(n) }).ToArray() };
        File.WriteAllText(Path.Combine(output,"build-manifest.json"),JsonUtility.ToJson(report,true));
    }

    static BuildTargetGroup BuildTargetGroupFor(BuildTarget target) { return target==BuildTarget.Android ? BuildTargetGroup.Android : BuildTargetGroup.Standalone; }

    public static void Preview()
    {
        try
        {
            string dir=Path.Combine(PackageRoot,"Build","Windows");
            var fw=AssetBundle.LoadFromFile(Path.Combine(dir,FrameworkBundle));
            var bundle=AssetBundle.LoadFromFile(Path.Combine(dir,PrefabBundle));
            var instance=UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>("giganotosaurusprefab"));
            var animal=instance.GetComponent<AnimalBehavior>();
            var model=animal._meshObject; var anim=model.GetComponent<Animation>();
            var skin=instance.GetComponentInChildren<SkinnedMeshRenderer>();
            RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.4f,.4f,.4f);
            var lightObject=new GameObject("PreviewLight"); var light=lightObject.AddComponent<Light>();
            light.type=LightType.Directional; light.intensity=1.1f; light.transform.rotation=Quaternion.Euler(35,-35,0);
            var cameraObject=new GameObject("PreviewCamera"); var camera=cameraObject.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.13f,.17f,.22f);
            camera.fieldOfView=35; camera.nearClipPlane=.1f; camera.farClipPlane=20000;
            string output=Path.Combine(PackageRoot,"Work","previews");Directory.CreateDirectory(output);
            foreach (string name in new[] { "Idle", "Attack-Bite", "Die" })
            {
                var clip=anim.GetClip(Prefix+name); float time=name=="Die"?clip.length:(name=="Idle"?0:clip.length*.5f);
                model.transform.localScale=Vector3.one; clip.SampleAnimation(model,time);
                var mesh=new Mesh();skin.BakeMesh(mesh,false);
                model.transform.localScale=Vector3.one*ModelScale;
                bool first=true;var bounds=new Bounds();
                foreach (var vertex in mesh.vertices)
                {
                    var point=skin.transform.TransformPoint(vertex);
                    if (first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
                }
                Debug.Log("[Giga2213] Preview "+name+" bounds="+bounds.size+" center="+bounds.center);
                camera.transform.position=bounds.center+new Vector3(1,.2f,-.35f).normalized*bounds.extents.magnitude*3.5f;
                camera.transform.LookAt(bounds.center);
                var rt=new RenderTexture(960,640,24); camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                var pixels=new Texture2D(960,640,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,960,640),0,0);pixels.Apply();
                var colors=pixels.GetPixels32();
                using(var stream=File.Create(Path.Combine(output,name+".ppm")))
                {
                    var header=System.Text.Encoding.ASCII.GetBytes("P6\n960 640\n255\n");stream.Write(header,0,header.Length);
                    for(int y=639;y>=0;y--)for(int x=0;x<960;x++){var c=colors[y*960+x];stream.WriteByte(c.r);stream.WriteByte(c.g);stream.WriteByte(c.b);}
                }
                camera.targetTexture=null;RenderTexture.active=null;
                UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(mesh);
            }
            UnityEngine.Object.DestroyImmediate(instance);bundle.Unload(true);fw.Unload(true);
            EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}

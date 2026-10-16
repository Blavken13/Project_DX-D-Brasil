using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

namespace Durango.GigaDemo
{
    public static class GigaDemoBuilder
    {
        const string Root = "Assets/Giganotosaurus_Demo";
        const string Fbx = Root + "/Mesh/Giganotosaurus_Demo.fbx";

        public static void Run()
        {
            // textures: normal map import type
            string np = Root + "/Textures/gigantosaurus_N.png";
            var ni = (TextureImporter)AssetImporter.GetAtPath(np);
            ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport();

            // FBX import: generic rig, loop idle/move
            var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false;
            mi.optimizeGameObjects = false;
            var clips = mi.defaultClipAnimations;
            Debug.Log("[Giga] clips in fbx: " + string.Join(",", clips.Select(c => c.name)));
            foreach (var c in clips)
            {
                bool keep = c.name.EndsWith("Idle") && !c.name.Contains("Torpid") && !c.name.Contains("Swim") || c.name.EndsWith("Move-Fwd");
                c.loopTime = keep; c.loopPose = keep;
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();

            var all = AssetDatabase.LoadAllAssetsAtPath(Fbx);
            var idle = all.OfType<AnimationClip>().FirstOrDefault(c => c.name == "Idle");
            var walk = all.OfType<AnimationClip>().FirstOrDefault(c => c.name == "Move-Fwd");
            Debug.Log($"[Giga] idle={(idle?idle.length:-1)} walk={(walk?walk.length:-1)} loop={idle?.isLooping}/{walk?.isLooping}");

            // material
            Shader sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(sh) { name = "Giganotosaurus_Demo_Mat" };
            var d = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/gigantosaurus_Colorize_d.png");
            mat.mainTexture = d;
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(np)); mat.EnableKeyword("_NORMALMAP");
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.25f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(mat, Root + "/Materials/Giganotosaurus_Demo_Mat.mat");

            // controller
            string cp = Root + "/Animations/Giganotosaurus_Demo.controller";
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(cp);
            var sm = ctrl.layers[0].stateMachine;
            var sIdle = sm.AddState("Idle"); sIdle.motion = idle;
            var sWalk = sm.AddState("Walk"); sWalk.motion = walk;
            sm.defaultState = sIdle;
            ctrl.AddParameter("Walk", AnimatorControllerParameterType.Bool);
            var t1 = sIdle.AddTransition(sWalk); t1.hasExitTime = false; t1.duration = 0.25f; t1.AddCondition(AnimatorConditionMode.If, 0, "Walk");
            var t2 = sWalk.AddTransition(sIdle); t2.hasExitTime = false; t2.duration = 0.25f; t2.AddCondition(AnimatorConditionMode.IfNot, 0, "Walk");

            // prefab
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Giganotosaurus_Demo";
            foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            { r.sharedMaterials = new[] { mat }; r.updateWhenOffscreen = true; }
            if (!inst.TryGetComponent<Animator>(out var anim)) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false;
            inst.AddComponent<GigaDemoCycle>();
            var pp = Root + "/Prefab/Giganotosaurus_Demo.prefab";
            PrefabUtility.SaveAsPrefabAsset(inst, pp);
            Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
            Debug.Log("[Giga] prefab saved " + pp);
        }
    }
}

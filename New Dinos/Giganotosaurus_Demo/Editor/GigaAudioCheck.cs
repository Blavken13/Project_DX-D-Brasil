using System.Linq;
using UnityEngine;
using UnityEditor;
namespace Durango.GigaDemo
{
    public static class GigaAudioCheck
    {
        public static void Run()
        {
            var root = "Assets/Giganotosaurus_Demo";
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(root + "/Prefab/Giganotosaurus_Demo_Standalone.prefab");
            var an = pf.GetComponentInChildren<Animation>(); var src = pf.GetComponentInChildren<AudioSource>();
            Debug.Log($"[GigaCheck] anim clip {an.clip.length:F3}s, audio clip {src.clip.length:F3}s, loop={src.loop} playOnAwake={src.playOnAwake} 3D={src.spatialBlend}");
            var all = AssetDatabase.LoadAllAssetsAtPath(root + "/Mesh/Giganotosaurus_Demo.fbx");
            var walk = all.OfType<AnimationClip>().First(c => c.name == "Move-Fwd");
            var g = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(root + "/Mesh/Giganotosaurus_Demo.fbx"));
            Transform T(string n) { foreach (var t in g.GetComponentsInChildren<Transform>()) if (t.name == n) return t; return null; }
            foreach (var fn in new[] { "l_ankle", "r_ankle" })
            {
                var f = T(fn); float bt = 0, by = 9; var s = "";
                for (float t = 0; t < walk.length; t += walk.length / 50f) { walk.SampleAnimation(g, t); float y = g.transform.InverseTransformPoint(f.position).y; if (y < by) { by = y; bt = t; } s += $"{t:F2}:{y:F2} "; }
                Debug.Log($"[GigaCheck] {fn}: lowest y {by:F3} at t={bt:F3}s (cycle {walk.length:F2}s) | footstep events at 0.3025s and 0.9275s");
            }
            Object.DestroyImmediate(g);
        }
    }
}

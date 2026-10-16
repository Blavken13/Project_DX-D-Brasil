using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Durango.GigaDemo
{
    public static class GigaDemoScene
    {
        const string Root = "Assets/Giganotosaurus_Demo";
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Ground"; ground.transform.localScale = new Vector3(10, 1, 10);
            var giga = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefab/Giganotosaurus_Demo.prefab"));
            giga.transform.position = Vector3.zero;
            var br = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Brachio/Prefabs/Brachio.prefab"));
            float bs = float.Parse(System.Environment.GetEnvironmentVariable("BRACHIO_SCALE") ?? "0.01");
            br.transform.localScale = Vector3.one * bs; br.transform.position = new Vector3(-30, 0, 14);
            EditorSceneManager.SaveScene(scene, Root + "/Scenes/Giganotosaurus_Demo_Test.unity");
            Shoot(giga, br);
        }
        static Transform FindT(GameObject g,string n){foreach(var t in g.GetComponentsInChildren<Transform>()) if(t.name==n) return t; return null;}
        static void Shoot(GameObject giga, GameObject br)
        {
            var cam = Camera.main; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.5f, .65f, .8f);
            cam.farClipPlane = 500; cam.nearClipPlane = 0.1f;
            string dir = "Screenshots/giga"; Directory.CreateDirectory(dir);
            var anim = giga.GetComponent<Animator>(); anim.Rebind();
            var views = new[] { ("side", new Vector3(14, 2, 0), new Vector3(0, 1.5f, 0)), ("top", new Vector3(8, 14, 8), new Vector3(0, 1.0f, 0)) };
            var all = AssetDatabase.LoadAllAssetsAtPath("Assets/Giganotosaurus_Demo/Mesh/Giganotosaurus_Demo.fbx");
            var cl = new System.Collections.Generic.Dictionary<string, AnimationClip>();
            foreach (var o in all) if (o is AnimationClip c && (c.name == "Idle" || c.name == "Eat" || c.name == "Roar" || c.name == "Startled")) cl[c.name] = c;
            anim.enabled = false;
            Transform foot = null; foreach (var t in giga.GetComponentsInChildren<Transform>()) if (t.name == "l_ankle") foot = t;
            foreach (var kv in cl)
            {
                foreach (float tt in new[] { kv.Value.length * 0.5f })
                {
                    kv.Value.SampleAnimation(giga, tt);
                    Debug.Log($"[Giga] {kv.Key} t={tt:F2} l_ankle={foot.position:F3} head={FindT(giga,"c_head").position:F3}");
                    var sm0 = giga.GetComponentInChildren<SkinnedMeshRenderer>(); sm0.enabled = false;
                    var bm = new Mesh(); sm0.BakeMesh(bm);
                    var bg = new GameObject("baked"); bg.AddComponent<MeshFilter>().sharedMesh = bm; bg.AddComponent<MeshRenderer>().sharedMaterial = sm0.sharedMaterial;
                    bg.transform.SetPositionAndRotation(sm0.transform.position, sm0.transform.rotation);
                    foreach (var v in views)
                    {
                        cam.transform.position = v.Item2; cam.transform.LookAt(v.Item3);
                        var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; cam.Render();
                        RenderTexture.active = rt; var tex = new Texture2D(1280, 720); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                        File.WriteAllBytes($"{dir}/{kv.Key}_{(tt>0?"mid":"t0")}_{v.Item1}.png", tex.EncodeToPNG());
                        cam.targetTexture = null; RenderTexture.active = null;
                    }
                    Object.DestroyImmediate(bg); sm0.enabled = true;
                }
            }
            var smr = giga.GetComponentInChildren<SkinnedMeshRenderer>();
            Debug.Log($"[Giga] bounds {smr.bounds.size} bones={smr.bones.Length} verts={smr.sharedMesh.vertexCount} tris={smr.sharedMesh.triangles.Length/3} brachioBounds={br.GetComponentInChildren<SkinnedMeshRenderer>().bounds.size}");
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class DiagnoseSpinosaurus
{
    public static void Run()
    {
        try {
            foreach (var path in new[] {
                "Assets/Models/Animals/Spinosaurus/SpinosaurusPrefab.prefab",
                "Assets/Models/Animals/Spinosaurus/SpinosaurusClassicPrefab.prefab"
            }) {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new Exception("Spino prefab missing: " + path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try {
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                    var mesh = skin.sharedMesh;
                    var vertices = mesh.vertices;
                    var normals = mesh.normals;
                    var triangles = mesh.triangles;
                    int aligned = 0, reversed = 0;
                    for (int i = 0; i < triangles.Length; i += 3) {
                        int a = triangles[i], b = triangles[i+1], c = triangles[i+2];
                        var geometric = Vector3.Cross(vertices[b]-vertices[a], vertices[c]-vertices[a]);
                        float dot = Vector3.Dot(geometric, normals[a]+normals[b]+normals[c]);
                        if (dot > 1e-8f) aligned++; else if (dot < -1e-8f) reversed++;
                    }
                    var baked = new Mesh();
                    skin.BakeMesh(baked, false);
                    var bakedScaled = new Mesh();
                    skin.BakeMesh(bakedScaled, true);
                    Debug.Log("[SpinoDiagnostic] " + path + "/" + skin.name + " vertices=" + mesh.vertexCount +
                        " triangles=" + triangles.Length/3 + " meshBounds=" + mesh.bounds +
                        " bakedBounds=" + baked.bounds + " bakedScaledBounds=" + bakedScaled.bounds +
                        " localBounds=" + skin.localBounds + " worldBounds=" + skin.bounds +
                        " scale=" + skin.transform.lossyScale + " rootBone=" + skin.rootBone.name +
                        " rootBoneScale=" + skin.rootBone.lossyScale + " bindpose00=" +
                        mesh.bindposes[0].GetColumn(0) + " windingAligned=" + aligned +
                        " windingReversed=" + reversed + " bones=" + skin.bones.Length +
                        " material=" + (skin.sharedMaterial == null ? "null" : skin.sharedMaterial.name) +
                        " shader=" + (skin.sharedMaterial == null || skin.sharedMaterial.shader == null ?
                            "null" : skin.sharedMaterial.shader.name));
                    UnityEngine.Object.DestroyImmediate(baked);
                    UnityEngine.Object.DestroyImmediate(bakedScaled);
                }
                if (path.EndsWith("/SpinosaurusPrefab.prefab")) {
                    var animation = instance.GetComponentInChildren<Animation>(true);
                    var skin = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .Single(s => s.sharedMaterial.name == "SpinosaurusClassic");
                    foreach (var clipName in new[] { "REST", "Spino-Idle", "Spino-Move-Fwd", "Spino-Attack-Bite" }) {
                        if (clipName != "REST") {
                            animation.Play(clipName);
                            animation[clipName].time = animation[clipName].length * .5f;
                            animation.Sample();
                        }
                        var sampled = new Mesh();
                        skin.BakeMesh(sampled, false);
                        var positions = sampled.vertices;
                        var indices = sampled.triangles;
                        float longest = 0;
                        for (int i = 0; i < indices.Length; i += 3) {
                            longest = Mathf.Max(longest,
                                Vector3.Distance(positions[indices[i]], positions[indices[i+1]]),
                                Vector3.Distance(positions[indices[i+1]], positions[indices[i+2]]),
                                Vector3.Distance(positions[indices[i+2]], positions[indices[i]]));
                        }
                        Debug.Log("[SpinoPose] " + clipName + " bounds=" + sampled.bounds +
                            " longestEdge=" + longest);
                        var distorted = instance.GetComponentsInChildren<Transform>(true)
                            .OrderByDescending(t => t.lossyScale.magnitude)
                            .Take(8).Select(t => t.name + ":local=" + t.localScale + ":world=" + t.lossyScale);
                        Debug.Log("[SpinoPoseScale] " + clipName + " " + string.Join("; ", distorted));
                        var displaced = instance.GetComponentsInChildren<Transform>(true)
                            .OrderByDescending(t => t.position.magnitude)
                            .Take(8).Select(t => t.name + ":local=" + t.localPosition + ":world=" + t.position);
                        Debug.Log("[SpinoPosePosition] " + clipName + " " + string.Join("; ", displaced));
                        if (clipName == "REST" || clipName == "Spino-Idle") {
                            var output = new StringBuilder();
                            foreach (var localPoint in positions) {
                                var p = skin.transform.localToWorldMatrix.MultiplyPoint3x4(localPoint);
                                output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                                    "v {0} {1} {2}\n", p.x, p.y, p.z);
                            }
                            for (int i = 0; i < indices.Length; i += 3)
                                output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                                    "f {0} {1} {2}\n", indices[i] + 1, indices[i+1] + 1, indices[i+2] + 1);
                            var directory = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
                            File.WriteAllText(Path.Combine(directory, "preview-classic-" + clipName + ".obj"), output.ToString());
                        }
                        UnityEngine.Object.DestroyImmediate(sampled);
                    }
                }
                } finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Models/Animals/Spinosaurus/Meshes/SpinosaurusBody.fbx");
            var bodyInstance = (GameObject)PrefabUtility.InstantiatePrefab(original);
            try {
                var skin = bodyInstance.GetComponentInChildren<SkinnedMeshRenderer>();
                var animation = bodyInstance.GetComponent<Animation>();
                if (animation == null) animation = bodyInstance.AddComponent<Animation>();
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/Models/Animals/Spinosaurus/Animations/Spino-Idle.anim");
                animation.AddClip(idle, "Spino-Idle");
                foreach (var pose in new[] { "REST", "Spino-Idle" }) {
                    if (pose != "REST") {
                        animation.Play("Spino-Idle");
                        animation["Spino-Idle"].time = idle.length * .5f;
                        animation.Sample();
                    }
                    var sampled = new Mesh();
                    skin.BakeMesh(sampled, false);
                    var positions = sampled.vertices;
                    var indices = sampled.triangles;
                    float longest = 0;
                    for (int i = 0; i < indices.Length; i += 3)
                        longest = Mathf.Max(longest,
                            Vector3.Distance(positions[indices[i]], positions[indices[i+1]]),
                            Vector3.Distance(positions[indices[i+1]], positions[indices[i+2]]),
                            Vector3.Distance(positions[indices[i+2]], positions[indices[i]]));
                    Debug.Log("[SpinoOriginalBody] " + pose + " bounds=" + sampled.bounds +
                        " longestEdge=" + longest);
                    var output = new StringBuilder();
                    foreach (var localPoint in positions) {
                        var p = skin.transform.localToWorldMatrix.MultiplyPoint3x4(localPoint);
                        output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                            "v {0} {1} {2}\n", p.x, p.y, p.z);
                    }
                    for (int i = 0; i < indices.Length; i += 3)
                        output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                            "f {0} {1} {2}\n", indices[i]+1, indices[i+1]+1, indices[i+2]+1);
                    var directory = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
                    File.WriteAllText(Path.Combine(directory, "preview-body-" + pose + ".obj"), output.ToString());
                    UnityEngine.Object.DestroyImmediate(sampled);
                }
            } finally { UnityEngine.Object.DestroyImmediate(bodyInstance); }
            var classicModel = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Models/Animals/Spinosaurus/Meshes/SpinosaurusClassic.fbx");
            var classicInstance = (GameObject)PrefabUtility.InstantiatePrefab(classicModel);
            try {
                classicInstance.transform.Find("SpinosaurusClassicRig").name = "SpinosaurusRig";
                var skin = classicInstance.GetComponentInChildren<SkinnedMeshRenderer>();
                var animation = classicInstance.GetComponent<Animation>();
                if (animation == null) animation = classicInstance.AddComponent<Animation>();
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/Models/Animals/Spinosaurus/Animations/Spino-Idle.anim");
                animation.AddClip(idle, "Spino-Idle");
                foreach (var pose in new[] { "REST", "Spino-Idle" }) {
                    if (pose != "REST") {
                        animation.Play("Spino-Idle");
                        animation["Spino-Idle"].time = idle.length * .5f;
                        animation.Sample();
                    }
                    var sampled = new Mesh();
                    skin.BakeMesh(sampled, false);
                    var positions = sampled.vertices;
                    var indices = sampled.triangles;
                    float longest = 0;
                    for (int i = 0; i < indices.Length; i += 3)
                        longest = Mathf.Max(longest,
                            Vector3.Distance(positions[indices[i]], positions[indices[i+1]]),
                            Vector3.Distance(positions[indices[i+1]], positions[indices[i+2]]),
                            Vector3.Distance(positions[indices[i+2]], positions[indices[i]]));
                    Debug.Log("[SpinoClassicRig] " + pose + " bounds=" + sampled.bounds +
                        " longestEdge=" + longest);
                    UnityEngine.Object.DestroyImmediate(sampled);
                }
            } finally { UnityEngine.Object.DestroyImmediate(classicInstance); }
            var retargetBody = (GameObject)PrefabUtility.InstantiatePrefab(original);
            var retargetClassic = (GameObject)PrefabUtility.InstantiatePrefab(classicModel);
            try {
                var sourceBones = retargetBody.GetComponentsInChildren<Transform>(true)
                    .GroupBy(t => t.name).ToDictionary(group => group.Key, group => group.First());
                var targetBones = retargetClassic.GetComponentsInChildren<Transform>(true)
                    .Where(t => sourceBones.ContainsKey(t.name)).GroupBy(t => t.name)
                    .Select(group => group.First()).ToArray();
                var sourceRest = sourceBones.ToDictionary(pair => pair.Key, pair =>
                    Matrix4x4.TRS(pair.Value.localPosition, pair.Value.localRotation, pair.Value.localScale));
                var targetRest = targetBones.ToDictionary(t => t.name, t =>
                    Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale));
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/Models/Animals/Spinosaurus/Animations/Spino-Idle.anim");
                idle.SampleAnimation(retargetBody, idle.length * .5f);
                foreach (var targetBone in targetBones) {
                    var sourceBone = sourceBones[targetBone.name];
                    var sourcePose = Matrix4x4.TRS(sourceBone.localPosition, sourceBone.localRotation, sourceBone.localScale);
                    var targetPose = targetRest[targetBone.name] * sourceRest[targetBone.name].inverse * sourcePose;
                    targetBone.localPosition = targetPose.GetColumn(3);
                    targetBone.localRotation = targetPose.rotation;
                    targetBone.localScale = targetPose.lossyScale;
                }
                var skin = retargetClassic.GetComponentInChildren<SkinnedMeshRenderer>();
                var sampled = new Mesh();
                skin.BakeMesh(sampled, false);
                var vertices = sampled.vertices;
                var indices = sampled.triangles;
                float longest = 0;
                for (int i = 0; i < indices.Length; i += 3)
                    longest = Mathf.Max(longest,
                        Vector3.Distance(vertices[indices[i]], vertices[indices[i+1]]),
                        Vector3.Distance(vertices[indices[i+1]], vertices[indices[i+2]]),
                        Vector3.Distance(vertices[indices[i+2]], vertices[indices[i]]));
                Debug.Log("[SpinoRetarget] Idle bounds=" + sampled.bounds + " longestEdge=" + longest);
                var output = new StringBuilder();
                foreach (var localPoint in vertices) {
                    var p = skin.transform.localToWorldMatrix.MultiplyPoint3x4(localPoint);
                    output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "v {0} {1} {2}\n", p.x, p.y, p.z);
                }
                for (int i = 0; i < indices.Length; i += 3)
                    output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "f {0} {1} {2}\n", indices[i]+1, indices[i+1]+1, indices[i+2]+1);
                var directory = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
                File.WriteAllText(Path.Combine(directory, "preview-classic-RetargetIdle.obj"), output.ToString());
                UnityEngine.Object.DestroyImmediate(sampled);
            } finally {
                UnityEngine.Object.DestroyImmediate(retargetBody);
                UnityEngine.Object.DestroyImmediate(retargetClassic);
            }
            var lockedBack = (GameObject)PrefabUtility.InstantiatePrefab(classicModel);
            try {
                lockedBack.transform.Find("SpinosaurusClassicRig").name = "SpinosaurusRig";
                var backBones = lockedBack.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name == "root" || t.name.StartsWith("c_back") ||
                        t.name.StartsWith("c_tail")).ToArray();
                var rest = backBones.ToDictionary(t => t.name, t =>
                    Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale));
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/Models/Animals/Spinosaurus/Animations/Spino-Idle.anim");
                idle.SampleAnimation(lockedBack, idle.length * .5f);
                foreach (var bone in backBones) {
                    var matrix = rest[bone.name];
                    bone.localPosition = matrix.GetColumn(3);
                    bone.localRotation = matrix.rotation;
                    bone.localScale = matrix.lossyScale;
                }
                var skin = lockedBack.GetComponentInChildren<SkinnedMeshRenderer>();
                var sampled = new Mesh();
                skin.BakeMesh(sampled, false);
                var output = new StringBuilder();
                foreach (var localPoint in sampled.vertices) {
                    var p = skin.transform.localToWorldMatrix.MultiplyPoint3x4(localPoint);
                    output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "v {0} {1} {2}\n", p.x, p.y, p.z);
                }
                var indices = sampled.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                    output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "f {0} {1} {2}\n", indices[i]+1, indices[i+1]+1, indices[i+2]+1);
                var directory = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
                File.WriteAllText(Path.Combine(directory, "preview-classic-LockedCore.obj"), output.ToString());
                Debug.Log("[SpinoLockedCore] Idle bounds=" + sampled.bounds);
                UnityEngine.Object.DestroyImmediate(sampled);
            } finally { UnityEngine.Object.DestroyImmediate(lockedBack); }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        } catch (Exception e) {
            Debug.LogException(e);
            if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Editor-only checks use actual skinned vertices, never renderer culling AABBs
// as a measure of animal size. No extra runtime script is required by the client.
public static class SpinosaurusGeometry
{
    public const string ShaderName = "Spinosaurus/TexturedAnimal";
    public const string AlbedoName = "Spino_Albedo_Baked";
    public const float TargetSizeRatio = 2.20f;
    public static void InspectInteraction() {
        string sourceRoot = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        using (var reference = new DimetrodonReference(sourceRoot)) {
            var lines = new List<string>();
            foreach (var t in reference.instance.GetComponentsInChildren<Transform>(true)) {
                var components = t.GetComponents<Component>();
                if (components.Length <= 1) continue;
                lines.Add(AnimationUtility.CalculateTransformPath(t, reference.instance.transform) + " tag=" + t.gameObject.tag + " layer=" + t.gameObject.layer + " components=" +
                    string.Join(",", components.Select(c => c == null ? "MISSING" : c.GetType().FullName)));
                foreach (var c in components.OfType<Collider>()) lines.Add(EditorJsonUtility.ToJson(c, true));
            }
            File.WriteAllLines(Path.Combine(sourceRoot, "Converted/ActorXRepair/native-interaction.txt"), lines);
        }
    }
    [Serializable] public class BodyCircle { public float x, z, radius; }
    [Serializable] public class CombatShape {
        public float attack_padding = 200f;
        public float client_x_radius, client_y_radius, client_bound_radius;
        public int selection_colliders;
        public BodyCircle[] circles;
    }
    [Serializable] public class ScaleReport {
        public float dimetrodonBaseScale, spinosaurusBaseScale, bodyVisualScale, sizeRatio;
        public Vector3 dimetrodonWorldSize, spinosaurusWorldSize;
    }
    [Serializable] public class PoseReport {
        public string clip;
        public float maximumEdgeStretch, p99EdgeStretch;
        public int samples;
    }
    [Serializable] public class MaterialReport {
        public string shader, albedo;
        public bool baked_color_mask;
        public int chromatic_pixels;
    }
    [Serializable] public class BundleReport {
        public string prefab_md5, pipeline;
        public bool geometry_validated, bind_pose_restored, client_motion_node_validated, interaction_validated;
        public MaterialReport SpinosaurusBody;
        public ScaleReport scale;
        public CombatShape combat_shape;
        public PoseReport[] poses;
    }
    public sealed class PoseSnapshot {
        readonly Transform[] transforms;
        readonly Vector3[] positions, scales;
        readonly Quaternion[] rotations;
        public PoseSnapshot(GameObject root) {
            transforms = root.GetComponentsInChildren<Transform>(true);
            positions = transforms.Select(t => t.localPosition).ToArray();
            rotations = transforms.Select(t => t.localRotation).ToArray();
            scales = transforms.Select(t => t.localScale).ToArray();
        }
        public void Restore() {
            for (int i = 0; i < transforms.Length; i++) {
                transforms[i].localPosition = positions[i];
                transforms[i].localRotation = rotations[i];
                transforms[i].localScale = scales[i];
            }
        }
    }
    public static Vector3[] WorldVertices(SkinnedMeshRenderer skin) {
        var mesh = skin.sharedMesh;
        var vertices = mesh.vertices;
        var weights = mesh.boneWeights;
        var bindposes = mesh.bindposes;
        var matrices = skin.bones.Select((bone, i) => bone.localToWorldMatrix * bindposes[i]).ToArray();
        if (weights.Length != vertices.Length) throw new Exception("Spino skin weights missing.");
        var result = new Vector3[vertices.Length];
        for (int i = 0; i < result.Length; i++) {
            var w = weights[i];
            float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
            if (Mathf.Abs(sum - 1) > .01f) throw new Exception("Skin weight sum is " + sum);
            var v = vertices[i];
            result[i] = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 +
                matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 +
                matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 +
                matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
        }
        return result;
    }
    public static Bounds BoundsOf(IEnumerable<Vector3> points) {
        bool first = true;
        var bounds = new Bounds();
        foreach (var p in points) {
            if (float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) ||
                float.IsInfinity(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.z))
                throw new Exception("Non-finite skinned vertex.");
            if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
            else bounds.Encapsulate(p);
        }
        if (first) throw new Exception("Empty skinned mesh.");
        return bounds;
    }
    static float Longest(Vector3 size) { return Mathf.Max(size.x, size.y, size.z); }
    static string Repo(string sourceRoot) { return Path.GetFullPath(Path.Combine(sourceRoot, "../..")); }
    public static float BaseScale(string sourceRoot, string id) {
        var text = File.ReadAllText(Path.Combine(Repo(sourceRoot), "Durango-CustomServer/server/data/assets/entity_types/animal.json"));
        var entry = Regex.Match(text, "(?ms)^  \"" + id + "\": \\{(.*?)(?=^  \"[0-9]+\":|\\z)");
        var scale = Regex.Match(entry.Value, "\"represent_scale\"\\s*:\\s*([0-9.]+)");
        if (!scale.Success) throw new Exception("Server BaseScale missing for " + id);
        return float.Parse(scale.Groups[1].Value, CultureInfo.InvariantCulture);
    }
    public sealed class DimetrodonReference : IDisposable {
        public GameObject instance;
        public SkinnedMeshRenderer skin;
        AssetBundle prefabBundle, frameworkBundle;
        public DimetrodonReference(string sourceRoot) {
            var dir = Path.Combine(Repo(sourceRoot), "dino/Dimetrodon");
            frameworkBundle = AssetBundle.LoadFromFile(Directory.GetFiles(dir, "models$animals$dimetrodon$dimetrodon_framework.asset.*.bundle").Single());
            prefabBundle = AssetBundle.LoadFromFile(Directory.GetFiles(dir, "models$animals$dimetrodon$dimetrodonprefab.prefab.*.bundle").Single());
            if (prefabBundle == null) throw new Exception("Native Dimetrodon bundle could not be loaded.");
            var path = prefabBundle.GetAllAssetNames().Single(n => n.EndsWith("/dimetrodonprefab.prefab"));
            instance = UnityEngine.Object.Instantiate(prefabBundle.LoadAsset<GameObject>(path));
            // AnimalManager overwrites the prefab root scale with Display.BaseScale.
            instance.transform.localScale = Vector3.one * BaseScale(sourceRoot, "2034");
            skin = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s.sharedMesh != null).OrderByDescending(s => s.sharedMesh.vertexCount).First();
        }
        public void Dispose() {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (prefabBundle != null) prefabBundle.Unload(true);
            if (frameworkBundle != null) frameworkBundle.Unload(true);
        }
    }
    public static ScaleReport ConfigureScale(GameObject root, GameObject visual, GameObject body,
        SkinnedMeshRenderer skin, string sourceRoot) {
        root.transform.localScale = Vector3.one * BaseScale(sourceRoot, "2214");
        body.transform.localScale = Vector3.one;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localPosition = Vector3.zero;
        visual.transform.localScale = Vector3.one;
        var head = body.GetComponentsInChildren<Transform>(true).First(t => t.name == "c_head");
        var spine = body.GetComponentsInChildren<Transform>(true).First(t => t.name == "c_back1");
        var forward = head.position - spine.position;
        forward.y = 0;
        visual.transform.rotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward) * visual.transform.rotation;
        var original = BoundsOf(WorldVertices(skin));
        ScaleReport report;
        using (var reference = new DimetrodonReference(sourceRoot)) {
            var referenceBounds = BoundsOf(WorldVertices(reference.skin));
            float scale = Longest(referenceBounds.size) * TargetSizeRatio / Longest(original.size);
            visual.transform.localScale = Vector3.one * scale;
            var actual = BoundsOf(WorldVertices(skin));
            visual.transform.position += Vector3.up * -actual.min.y;
            report = new ScaleReport {
                dimetrodonBaseScale = BaseScale(sourceRoot, "2034"),
                spinosaurusBaseScale = BaseScale(sourceRoot, "2214"),
                bodyVisualScale = scale, dimetrodonWorldSize = referenceBounds.size,
                spinosaurusWorldSize = actual.size,
                sizeRatio = Longest(actual.size) / Longest(referenceBounds.size)
            };
        }
        File.WriteAllText(Path.Combine(sourceRoot, "Converted/ActorXRepair/scale-reference.json"), JsonUtility.ToJson(report, true));
        Debug.Log("[SpinoScale] " + JsonUtility.ToJson(report));
        return report;
    }
    public static CombatShape ConfigureCombatShape(GameObject root, SkinnedMeshRenderer skin, string sourceRoot) {
        // Footprint in runtime world units, before animal yaw. Separate short
        // slices preserve the narrow tail instead of turning length into radius.
        var points = WorldVertices(skin).Select(p => root.transform.InverseTransformDirection(p - root.transform.position)).ToArray();
        var bounds = BoundsOf(points);
        float baseScale = root.transform.lossyScale.x;
        var rigidbody = root.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        var interaction = new GameObject("Interaction_Point");
        interaction.transform.SetParent(skin.bones.First(b => b.name == "c_back1"), false);
        const int slices = 24;
        float step = bounds.size.z / slices;
        var circles = new List<BodyCircle>();
        for (int i = 0; i < slices; i++) {
            float low = bounds.min.z + step * i;
            var section = points.Where(p => p.z >= low && (i == slices - 1 || p.z < low + step)).ToArray();
            if (section.Length == 0) continue;
            float x = (section.Min(p => p.x) + section.Max(p => p.x)) * .5f;
            float z = low + step * .5f;
            float radius = section.Max(p => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(x, z)));
            circles.Add(new BodyCircle { x = x, z = z, radius = radius });
            // Native target filters use GetComponent<CharacterBehavior>() on
            // the collider's own GameObject. Keep every trigger on the owner.
            var sectionBounds = BoundsOf(section);
            var collider = root.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = sectionBounds.center / baseScale;
            collider.size = Vector3.Max(sectionBounds.size / baseScale, Vector3.one * .01f);
        }
        var shape = new CombatShape { circles = circles.ToArray(),
            client_bound_radius = bounds.size.x * .5f / baseScale,
            selection_colliders = root.GetComponents<Collider>().Length };
        using (var reference = new DimetrodonReference(sourceRoot)) {
            var size = BoundsOf(WorldVertices(reference.skin)).size;
            var native = reference.instance.GetComponent<AnimalBehavior>();
            shape.client_x_radius = native._xRadius * bounds.size.x / Mathf.Min(size.x, size.z);
            shape.client_y_radius = native._yRadius * bounds.size.z / Mathf.Max(size.x, size.z);
        }
        File.WriteAllText(Path.Combine(sourceRoot, "Converted/ActorXRepair/combat-shape.json"), JsonUtility.ToJson(shape, true));
        return shape;
    }
    public static void CheckBindPose(SkinnedMeshRenderer skin) {
        var actual = WorldVertices(skin);
        var source = skin.sharedMesh.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
        float error = actual.Zip(source, (a, b) => Vector3.Distance(a, b)).Max();
        float span = Longest(BoundsOf(source).size);
        if (error > span * .0002f) throw new Exception("Prefab is not in bind pose: relative error=" + error / span);
    }
    public static void RestoreBindPose(SkinnedMeshRenderer skin) {
        // FBX animation stacks can leave default node transforms at an authored
        // pose. Recover the neutral hierarchy from the mesh's exact bind matrices.
        var desired = skin.bones.Select((bone, i) => new {
            bone, matrix = skin.transform.localToWorldMatrix * skin.sharedMesh.bindposes[i].inverse
        }).ToDictionary(pair => pair.bone, pair => pair.matrix);
        Func<Transform, int> depth = transform => {
            int count = 0;
            while (transform.parent != null) { count++; transform = transform.parent; }
            return count;
        };
        foreach (var bone in skin.bones.OrderBy(depth)) {
            var parent = bone.parent;
            var parentMatrix = parent == null ? Matrix4x4.identity :
                desired.ContainsKey(parent) ? desired[parent] : parent.localToWorldMatrix;
            var local = parentMatrix.inverse * desired[bone];
            bone.localPosition = local.GetColumn(3);
            bone.localRotation = local.rotation;
            bone.localScale = local.lossyScale;
        }
        CheckBindPose(skin);
    }
    public static Bounds SampleBounds(GameObject body, SkinnedMeshRenderer skin, IEnumerable<AnimationClip> clips) {
        var snapshot = new PoseSnapshot(body);
        var boundsRoot = skin.rootBone != null ? skin.rootBone : skin.transform;
        bool first = true;
        var result = new Bounds();
        try {
            foreach (var clip in clips) {
                for (int frame = 0; frame <= 8; frame++) {
                    snapshot.Restore();
                    clip.SampleAnimation(body, clip.length * frame / 8f);
                    var current = BoundsOf(WorldVertices(skin).Select(p => boundsRoot.InverseTransformPoint(p)));
                    if (first) { result = current; first = false; } else result.Encapsulate(current);
                }
            }
        } finally { snapshot.Restore(); }
        result.Expand(result.size.magnitude * .05f);
        return result;
    }
    static float[] EdgeLengths(Vector3[] vertices, int[] triangles) {
        var edges = new float[triangles.Length];
        for (int i = 0; i < triangles.Length; i += 3) {
            edges[i] = Vector3.Distance(vertices[triangles[i]], vertices[triangles[i + 1]]);
            edges[i + 1] = Vector3.Distance(vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
            edges[i + 2] = Vector3.Distance(vertices[triangles[i + 2]], vertices[triangles[i]]);
        }
        return edges;
    }
    public static void VerifyBundle(GameObject prefab, string sourceRoot, string bundlePath) {
        var instance = UnityEngine.Object.Instantiate(prefab);
        var poses = new List<PoseReport>();
        MaterialReport materialReport;
        try {
            var body = instance.GetComponent<AnimalBehavior>()._meshObject;
            if (!instance.CompareTag("Enemy") || instance.GetComponent<Rigidbody>() == null ||
                !instance.GetComponent<Rigidbody>().isKinematic ||
                instance.GetComponents<Collider>().Length != 24 ||
                instance.GetComponents<Collider>().Any(c => !c.isTrigger || !c.enabled) ||
                !instance.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Interaction_Point"))
                throw new Exception("Native enemy selection contract missing: tag=" + instance.tag +
                    ", colliders=" + instance.GetComponents<Collider>().Length + ", triggers=" +
                    instance.GetComponents<Collider>().Count(c => c.isTrigger && c.enabled) +
                    ", kinematic=" + (instance.GetComponent<Rigidbody>() != null && instance.GetComponent<Rigidbody>().isKinematic) +
                    ", anchor=" + instance.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Interaction_Point"));
            if ((body.transform.localScale - Vector3.one).sqrMagnitude > .00001f ||
                Quaternion.Angle(body.transform.localRotation, Quaternion.identity) > .01f ||
                body.transform.localPosition.sqrMagnitude > .00001f)
                throw new Exception("Client motion node must have an identity local transform.");
            var exporter = instance.GetComponent<Durango.Model.RootMotionExporter>();
            var forward = body.transform.TransformVector(exporter._rootMotionForward).normalized;
            if (Vector3.Dot(forward, instance.transform.forward) < .999f)
                throw new Exception("Client root-motion forward axis does not match animal yaw.");
            var skin = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
            CheckBindPose(skin);
            var mesh = skin.sharedMesh;
            if (mesh.uv.Length != mesh.vertexCount || mesh.tangents.Length != mesh.vertexCount)
                throw new Exception("UV0 or tangents missing from repaired Spino.");
            var material = skin.sharedMaterial;
            var texture = material.GetTexture("_SpinoAlbedo") as Texture2D;
            if (material.shader.name != ShaderName || texture == null || texture.name != AlbedoName)
                throw new Exception("Repaired shader or baked albedo missing from bundle.");
            if (ShaderUtil.ShaderHasError(material.shader)) throw new Exception("Spino shader compiler error.");
            var pixels = texture.GetPixels();
            int colored = pixels.Count(p => Mathf.Max(p.r, p.g, p.b) - Mathf.Min(p.r, p.g, p.b) > .04f);
            if (colored < pixels.Length * .1f) throw new Exception("Bundle albedo is not colorized.");
            materialReport = new MaterialReport { shader = material.shader.name,
                albedo = texture.name, baked_color_mask = true, chromatic_pixels = colored };
            var snapshot = new PoseSnapshot(body);
            var neutral = WorldVertices(skin);
            var triangles = mesh.triangles;
            var restEdges = EdgeLengths(neutral, triangles);
            var sorted = restEdges.OrderBy(v => v).ToArray();
            float threshold = sorted[sorted.Length / 4];
            var animation = body.GetComponent<Animation>();
            foreach (AnimationState state in animation) {
                float maximum = 0, p99 = 0;
                bool deformed = false;
                for (int frame = 0; frame <= 8; frame++) {
                    snapshot.Restore();
                    state.clip.SampleAnimation(body, state.length * frame / 8f);
                    var points = WorldVertices(skin);
                    var lengths = EdgeLengths(points, triangles);
                    var ratios = lengths.Select((length, i) => restEdges[i] > threshold ? length / restEdges[i] : 1f).OrderBy(v => v).ToArray();
                    maximum = Mathf.Max(maximum, ratios.Last());
                    p99 = Mathf.Max(p99, ratios[(int)(ratios.Length * .99f)]);
                    if (neutral.Zip(points, (a, b) => (a - b).sqrMagnitude).Any(delta => delta > .001f)) deformed = true;
                    var boundsRoot = skin.rootBone != null ? skin.rootBone : skin.transform;
                    if (points.Any(p => !skin.localBounds.Contains(boundsRoot.InverseTransformPoint(p))))
                        throw new Exception(state.name + " leaves the saved culling bounds.");
                    BoundsOf(points); // Reject non-finite coordinates.
                }
                if (p99 > 2.2f || maximum > 10f)
                    throw new Exception(state.name + " invalid skin deformation: max=" + maximum + ", p99=" + p99);
                if (state.name == "Spino-Move-Fwd" && !deformed) throw new Exception("Move clip does not animate the body.");
                poses.Add(new PoseReport { clip = state.name, maximumEdgeStretch = maximum, p99EdgeStretch = p99, samples = 9 });
            }
            snapshot.Restore();
            CheckBindPose(skin);
        } finally { UnityEngine.Object.DestroyImmediate(instance); }
        var scale = JsonUtility.FromJson<ScaleReport>(File.ReadAllText(Path.Combine(sourceRoot, "Converted/ActorXRepair/scale-reference.json")));
        if (Mathf.Abs(scale.sizeRatio - TargetSizeRatio) > .01f) throw new Exception("Spino/Dimetrodon scale calibration mismatch.");
        var shape = JsonUtility.FromJson<CombatShape>(File.ReadAllText(Path.Combine(sourceRoot, "Converted/ActorXRepair/combat-shape.json")));
        var report = new BundleReport {
            prefab_md5 = MD5File(bundlePath), pipeline = "ActorX PSK/PSA with Unreal additive poses baked",
            geometry_validated = true, bind_pose_restored = true, client_motion_node_validated = true, interaction_validated = true,
            SpinosaurusBody = materialReport, scale = scale, combat_shape = shape, poses = poses.ToArray()
        };
        File.WriteAllText(Path.Combine(sourceRoot, "AssetBundles/Windows/bundle-validation.json"), JsonUtility.ToJson(report, true));
        Debug.Log("[SpinoVerified] " + poses.Count + " clips, " + poses.Sum(p => p.samples) + " poses, textured and bind pose restored.");
    }
    public static string MD5File(string path) {
        using (var md5 = MD5.Create()) using (var stream = File.OpenRead(path))
            return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}

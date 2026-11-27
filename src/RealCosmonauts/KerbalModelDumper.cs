using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RealCosmonauts
{
    /// <summary>
    /// Developer tool. Writes a text report of every Kerbal model the game has loaded:
    /// transform tree, renderers, meshes, bones, materials and animations, plus which
    /// bones the animations move or scale. Optionally exports the meshes as .obj files.
    ///
    /// The output is local reference data for fitting the mod's own models to the stock
    /// skeleton. It comes from the game's assets, so it must never be committed or shipped.
    /// </summary>
    internal static class KerbalModelDumper
    {
        private const float Epsilon = 1e-4f;
        private const int SamplesPerClip = 8;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <param name="sceneLabel">Subfolder name, so dumps from different scenes do not overwrite each other.</param>
        public static void DumpAll(string sceneLabel, bool exportMeshes)
        {
            string outDir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "RealCosmonauts", "PluginData", "KerbalDump", sceneLabel);
            Directory.CreateDirectory(outDir);

            List<KeyValuePair<string, GameObject>> models = FindKerbalModels();
            Log.Info($"Dumping {models.Count} Kerbal models to {outDir}");

            var index = new StringBuilder();
            index.AppendLine($"Real Cosmonauts Kerbal model dump, KSP {Versioning.version_major}.{Versioning.version_minor}.{Versioning.Revision}, Unity {Application.unityVersion}");
            index.AppendLine($"Written {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            index.AppendLine();

            foreach (KeyValuePair<string, GameObject> entry in models)
            {
                string fileBase = Sanitize(entry.Key);
                try
                {
                    string report = DumpModel(entry.Key, entry.Value, exportMeshes ? Path.Combine(outDir, fileBase) : null);
                    File.WriteAllText(Path.Combine(outDir, fileBase + ".txt"), report);
                    index.AppendLine($"{entry.Key} -> {fileBase}.txt");
                }
                catch (Exception e)
                {
                    Log.Error($"Failed to dump {entry.Key}: {e}");
                    index.AppendLine($"{entry.Key} -> FAILED: {e.Message}");
                }
            }

            File.WriteAllText(Path.Combine(outDir, "_index.txt"), index.ToString());
            Log.Info("Kerbal model dump finished.");
        }

        /// <summary>Collects EVA Kerbal part prefabs and IVA Kerbal prefabs, keyed by a readable label.</summary>
        private static List<KeyValuePair<string, GameObject>> FindKerbalModels()
        {
            var result = new List<KeyValuePair<string, GameObject>>();
            var seen = new HashSet<int>();

            foreach (AvailablePart part in PartLoader.LoadedPartsList)
            {
                if (part?.partPrefab == null || part.partPrefab.GetComponent<KerbalEVA>() == null)
                    continue;
                GameObject go = part.partPrefab.gameObject;
                if (seen.Add(go.GetInstanceID()))
                    result.Add(new KeyValuePair<string, GameObject>("EVA_" + part.name, go));
            }

            // IVA Kerbals are not parts; their prefabs are only reachable as loaded objects.
            foreach (Kerbal kerbal in Resources.FindObjectsOfTypeAll<Kerbal>())
            {
                GameObject go = kerbal.gameObject;
                if (!seen.Add(go.GetInstanceID()))
                    continue;
                string scene = go.scene.IsValid() ? go.scene.name : "prefab";
                result.Add(new KeyValuePair<string, GameObject>($"IVA_{go.name}_{scene}_{go.GetInstanceID()}", go));
            }

            return result;
        }

        private static string DumpModel(string label, GameObject root, string meshExportBase)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"== {label} ==");
            sb.AppendLine($"GameObject: {root.name}, activeSelf={root.activeSelf}, scene={(root.scene.IsValid() ? root.scene.name : "<none>")}");
            sb.AppendLine();

            sb.AppendLine("-- Transform tree (local position / euler rotation / scale) --");
            AppendTree(sb, root.transform, 0);
            sb.AppendLine();

            sb.AppendLine("-- Renderers --");
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                AppendRenderer(sb, renderer, root.transform, meshExportBase);
            sb.AppendLine();

            sb.AppendLine("-- Animations --");
            AppendAnimations(sb, root);

            return sb.ToString();
        }

        private static void AppendTree(StringBuilder sb, Transform t, int depth)
        {
            string components = string.Join(", ", t.GetComponents<Component>()
                .Where(c => c != null && !(c is Transform))
                .Select(c => c.GetType().Name));
            sb.Append(' ', depth * 2)
              .Append(t.name)
              .Append("  pos=").Append(Fmt(t.localPosition))
              .Append(" rot=").Append(Fmt(t.localEulerAngles))
              .Append(" scale=").Append(Fmt(t.localScale));
            if (!t.gameObject.activeSelf)
                sb.Append(" [inactive]");
            if (components.Length > 0)
                sb.Append("  {").Append(components).Append('}');
            sb.AppendLine();

            for (int i = 0; i < t.childCount; i++)
                AppendTree(sb, t.GetChild(i), depth + 1);
        }

        private static void AppendRenderer(StringBuilder sb, Renderer renderer, Transform root, string meshExportBase)
        {
            string path = PathOf(renderer.transform, root);
            Mesh mesh = null;

            if (renderer is SkinnedMeshRenderer smr)
            {
                mesh = smr.sharedMesh;
                sb.AppendLine($"SkinnedMeshRenderer at '{path}'");
                sb.AppendLine($"  rootBone: {(smr.rootBone != null ? PathOf(smr.rootBone, root) : "<none>")}");
                sb.AppendLine($"  quality: {smr.quality}, updateWhenOffscreen: {smr.updateWhenOffscreen}, localBounds: {smr.localBounds}");
                Transform[] bones = smr.bones;
                Matrix4x4[] bindposes = mesh != null ? mesh.bindposes : new Matrix4x4[0];
                sb.AppendLine($"  bones ({bones.Length}):");
                for (int i = 0; i < bones.Length; i++)
                {
                    string bonePath = bones[i] != null ? PathOf(bones[i], root) : "<null>";
                    // The inverse bind pose gives the bone's position in mesh space at bind time.
                    string bindPos = i < bindposes.Length ? Fmt((Vector3)bindposes[i].inverse.GetColumn(3)) : "?";
                    sb.AppendLine($"    [{i}] {bonePath}  bindPos={bindPos}");
                }
            }
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
                sb.AppendLine($"{renderer.GetType().Name} at '{path}'");
            }

            sb.AppendLine($"  enabled: {renderer.enabled}, layer: {renderer.gameObject.layer}");

            if (mesh != null)
            {
                sb.AppendLine($"  mesh: '{mesh.name}', vertices={mesh.vertexCount}, subMeshes={mesh.subMeshCount}, readable={mesh.isReadable}, bounds={mesh.bounds}");
                if (mesh.blendShapeCount > 0)
                {
                    var shapes = new List<string>();
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                        shapes.Add(mesh.GetBlendShapeName(i));
                    sb.AppendLine($"  blendShapes ({mesh.blendShapeCount}): {string.Join(", ", shapes)}");
                }
                if (meshExportBase != null)
                {
                    // Windows paths are limited to 260 characters, so name files after the renderer
                    // only, plus a short hash of its full path to keep them unique.
                    string shortName = Sanitize(renderer.name) + "_" + ((uint)path.GetHashCode()).ToString("x8");
                    sb.AppendLine("  obj: " + ExportObj(mesh, meshExportBase + "__" + shortName));
                }
            }
            else
            {
                sb.AppendLine("  mesh: <none>");
            }

            foreach (Material material in renderer.sharedMaterials)
                AppendMaterial(sb, material);
        }

        private static void AppendMaterial(StringBuilder sb, Material material)
        {
            if (material == null)
            {
                sb.AppendLine("  material: <null>");
                return;
            }

            sb.AppendLine($"  material: '{material.name}', shader: '{(material.shader != null ? material.shader.name : "<none>")}'");
            foreach (string prop in material.GetTexturePropertyNames())
            {
                Texture tex = material.GetTexture(prop);
                if (tex != null)
                    sb.AppendLine($"    {prop}: '{tex.name}' {tex.width}x{tex.height}");
            }
            if (material.HasProperty("_Color"))
                sb.AppendLine($"    _Color: {material.GetColor("_Color")}");
        }

        /// <summary>
        /// Lists animation clips and samples each one on a disabled clone, recording which
        /// transforms change position or scale. This tells us how far the skeleton's
        /// proportions can be changed without the animations fighting us.
        /// </summary>
        private static void AppendAnimations(StringBuilder sb, GameObject root)
        {
            var sources = new List<KeyValuePair<string, AnimationClip>>();

            foreach (Animation anim in root.GetComponentsInChildren<Animation>(true))
            {
                sb.AppendLine($"Animation (legacy) at '{PathOf(anim.transform, root.transform)}', default clip: {(anim.clip != null ? anim.clip.name : "<none>")}");
                foreach (AnimationState state in anim)
                {
                    if (state.clip == null)
                        continue;
                    sb.AppendLine($"  clip '{state.clip.name}' length={state.clip.length.ToString("0.###", Inv)}s wrap={state.wrapMode}");
                    sources.Add(new KeyValuePair<string, AnimationClip>(PathOf(anim.transform, root.transform), state.clip));
                }
            }

            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                sb.AppendLine($"Animator at '{PathOf(animator.transform, root.transform)}', controller: {(controller != null ? controller.name : "<none>")}, avatar: {(animator.avatar != null ? animator.avatar.name : "<none>")}");
                if (controller == null)
                    continue;
                foreach (AnimationClip clip in controller.animationClips.Distinct())
                {
                    sb.AppendLine($"  clip '{clip.name}' length={clip.length.ToString("0.###", Inv)}s legacy={clip.legacy}");
                    sources.Add(new KeyValuePair<string, AnimationClip>(PathOf(animator.transform, root.transform), clip));
                }
            }

            if (sources.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine("-- Transforms moved or scaled by animations (max change from rest pose) --");

            // Clone under a disabled parent so no Awake/Start runs on the copy.
            var holder = new GameObject("RealCosmonauts_DumpHolder");
            holder.SetActive(false);
            try
            {
                GameObject clone = UnityEngine.Object.Instantiate(root, holder.transform);
                var moved = new Dictionary<string, float>();
                var scaled = new Dictionary<string, float>();

                foreach (KeyValuePair<string, AnimationClip> source in sources)
                {
                    Transform animRoot = source.Key.Length == 0 ? clone.transform : clone.transform.Find(source.Key);
                    if (animRoot == null)
                        continue;
                    Transform[] all = animRoot.GetComponentsInChildren<Transform>(true);
                    Vector3[] restPos = all.Select(t => t.localPosition).ToArray();
                    Vector3[] restScale = all.Select(t => t.localScale).ToArray();
                    string[] keys = all.Select(t => PathOf(t, clone.transform)).ToArray();

                    try
                    {
                        for (int s = 0; s <= SamplesPerClip; s++)
                        {
                            source.Value.SampleAnimation(animRoot.gameObject, source.Value.length * s / SamplesPerClip);
                            for (int i = 0; i < all.Length; i++)
                            {
                                Track(moved, keys[i], (all[i].localPosition - restPos[i]).magnitude);
                                Track(scaled, keys[i], (all[i].localScale - restScale[i]).magnitude);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        sb.AppendLine($"  could not sample '{source.Value.name}': {e.Message}");
                    }

                    // Reset so the next clip starts from the rest pose.
                    for (int i = 0; i < all.Length; i++)
                    {
                        all[i].localPosition = restPos[i];
                        all[i].localScale = restScale[i];
                    }
                }

                AppendChanges(sb, "position", moved);
                AppendChanges(sb, "scale", scaled);
            }
            finally
            {
                UnityEngine.Object.Destroy(holder);
            }
        }

        private static void Track(Dictionary<string, float> map, string key, float delta)
        {
            if (delta <= Epsilon)
                return;
            map.TryGetValue(key, out float current);
            if (delta > current)
                map[key] = delta;
        }

        private static void AppendChanges(StringBuilder sb, string what, Dictionary<string, float> map)
        {
            sb.AppendLine($"Animated {what} ({map.Count} transforms):");
            foreach (KeyValuePair<string, float> kv in map.OrderBy(kv => kv.Key))
                sb.AppendLine($"  {kv.Key}  max delta={kv.Value.ToString("0.#####", Inv)}");
        }

        /// <summary>Writes the mesh in its bind pose (mesh space) as a Wavefront .obj file.</summary>
        private static string ExportObj(Mesh mesh, string fileBase)
        {
            if (!mesh.isReadable)
                return "skipped (mesh not readable)";

            string file = fileBase + ".obj";
            var sb = new StringBuilder();
            sb.AppendLine($"# {mesh.name} exported by Real Cosmonauts (local reference only, do not distribute)");
            sb.AppendLine($"o {Sanitize(mesh.name)}");

            // Unity is left-handed; flip X (and winding) so the mesh imports correctly in Blender.
            foreach (Vector3 v in mesh.vertices)
                sb.AppendLine($"v {(-v.x).ToString("R", Inv)} {v.y.ToString("R", Inv)} {v.z.ToString("R", Inv)}");
            Vector3[] normals = mesh.normals;
            foreach (Vector3 n in normals)
                sb.AppendLine($"vn {(-n.x).ToString("R", Inv)} {n.y.ToString("R", Inv)} {n.z.ToString("R", Inv)}");
            Vector2[] uvs = mesh.uv;
            foreach (Vector2 uv in uvs)
                sb.AppendLine($"vt {uv.x.ToString("R", Inv)} {uv.y.ToString("R", Inv)}");

            bool hasNormals = normals.Length == mesh.vertexCount;
            bool hasUvs = uvs.Length == mesh.vertexCount;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                sb.AppendLine($"g submesh{sub}");
                int[] tris = mesh.GetTriangles(sub);
                for (int i = 0; i + 2 < tris.Length; i += 3)
                    sb.AppendLine($"f {Corner(tris[i], hasUvs, hasNormals)} {Corner(tris[i + 2], hasUvs, hasNormals)} {Corner(tris[i + 1], hasUvs, hasNormals)}");
            }

            File.WriteAllText(file, sb.ToString());
            return Path.GetFileName(file);
        }

        private static string Corner(int index, bool hasUvs, bool hasNormals)
        {
            string i = (index + 1).ToString(Inv);
            if (hasUvs && hasNormals) return $"{i}/{i}/{i}";
            if (hasNormals) return $"{i}//{i}";
            if (hasUvs) return $"{i}/{i}";
            return i;
        }

        private static string PathOf(Transform t, Transform root)
        {
            if (t == root)
                return "";
            var parts = new List<string>();
            for (Transform cur = t; cur != null && cur != root; cur = cur.parent)
                parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string Fmt(Vector3 v) =>
            $"({v.x.ToString("0.#####", Inv)}, {v.y.ToString("0.#####", Inv)}, {v.z.ToString("0.#####", Inv)})";

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "root";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }
    }
}

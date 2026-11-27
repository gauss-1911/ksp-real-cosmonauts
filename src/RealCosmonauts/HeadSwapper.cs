using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RealCosmonauts
{
    /// <summary>
    /// Puts a human head on the stock Kerbal models (prototype, milestone 3).
    ///
    /// The prefabs are patched once at the main menu, so every Kerbal spawned afterwards
    /// in IVA or on EVA gets the new head. The new head is a separate SkinnedMeshRenderer
    /// skinned to the stock head bones, so stock animations keep driving it. The stock
    /// head, eyes, teeth and tongue get an empty mesh rather than being disabled, so other
    /// mods that toggle or retexture them cannot bring them back.
    /// </summary>
    internal static class HeadSwapper
    {
        public const string HeadObjectName = "RealCosmonautsHead";

        private const string PivotBone = "bn_headPivot_a01";
        private const string SkullBone = "bn_headPivot_b01";

        private static readonly string[] HiddenParts = { "eyeball", "pupil", "teeth", "tongue" };

        private class HeadAsset
        {
            public string Name;
            public RcmMesh Mesh;
            public Texture SkinTexture;
            public Texture EyeTexture;
        }

        private static readonly Dictionary<string, HeadAsset> Cache = new Dictionary<string, HeadAsset>();
        private static Mesh emptyMesh;

        public static void PatchAllPrefabs()
        {
            int patched = 0;

            foreach (AvailablePart part in PartLoader.LoadedPartsList)
            {
                if (part?.partPrefab != null && part.partPrefab.GetComponent<KerbalEVA>() != null)
                    patched += Patch(part.partPrefab.gameObject, part.name) ? 1 : 0;
            }

            foreach (Kerbal kerbal in Resources.FindObjectsOfTypeAll<Kerbal>())
            {
                // Only prefabs: scene instances are spawned from them anyway.
                if (!kerbal.gameObject.scene.IsValid())
                    patched += Patch(kerbal.gameObject, kerbal.gameObject.name) ? 1 : 0;
            }

            Log.Info($"Human heads applied to {patched} Kerbal models.");
        }

        private static bool Patch(GameObject root, string label)
        {
            try
            {
                if (FindByName(root.transform, HeadObjectName) != null)
                    return false; // already patched

                SkinnedMeshRenderer stockHead = FindStockHead(root);
                if (stockHead == null)
                {
                    Log.Warning($"{label}: no stock head found, left unchanged.");
                    return false;
                }

                bool female = label.IndexOf("female", StringComparison.OrdinalIgnoreCase) >= 0;
                HeadAsset asset = LoadAsset(female ? Settings.FemaleHead : Settings.MaleHead);
                if (asset == null)
                    return false;

                Transform[] bones = stockHead.bones;
                Matrix4x4[] bindposes = stockHead.sharedMesh.bindposes;
                int pivotIndex = Array.FindIndex(bones, b => b != null && b.name == PivotBone);
                int skullIndex = Array.FindIndex(bones, b => b != null && b.name == SkullBone);
                if (pivotIndex < 0 || skullIndex < 0)
                {
                    Log.Warning($"{label}: stock head lacks {PivotBone} or {SkullBone}, left unchanged.");
                    return false;
                }

                // Bone position in mesh space at bind time.
                Vector3 pivot = bindposes[pivotIndex].inverse.GetColumn(3);
                Mesh mesh = asset.Mesh.ToUnityMesh($"{asset.Name}_{label}", bones, bindposes, skullIndex, pivot, Settings.HeadScale);

                var go = new GameObject(HeadObjectName) { layer = stockHead.gameObject.layer };
                go.transform.SetParent(stockHead.transform.parent, false);
                go.transform.localPosition = stockHead.transform.localPosition;
                go.transform.localRotation = stockHead.transform.localRotation;
                go.transform.localScale = stockHead.transform.localScale;

                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.bones = bones;
                renderer.rootBone = stockHead.rootBone;
                renderer.sharedMesh = mesh;
                renderer.quality = stockHead.quality;
                renderer.updateWhenOffscreen = stockHead.updateWhenOffscreen;
                renderer.localBounds = mesh.bounds;
                renderer.sharedMaterials = new[]
                {
                    MakeMaterial(stockHead.sharedMaterial, asset.SkinTexture, asset.Name + "_skin"),
                    MakeMaterial(stockHead.sharedMaterial, asset.EyeTexture, asset.Name + "_eyes"),
                };

                HideStockFace(root, stockHead);
                Log.Info($"{label}: human head '{asset.Name}' applied ({mesh.vertexCount} vertices).");
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"{label}: head swap failed: {e}");
                return false;
            }
        }

        /// <summary>The stock head is the big skinned mesh that uses the skull bone and a head texture.</summary>
        private static SkinnedMeshRenderer FindStockHead(GameObject root)
        {
            SkinnedMeshRenderer best = null;
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || smr.name == HeadObjectName || smr.sharedMesh.vertexCount < 300)
                    continue;
                if (Array.FindIndex(smr.bones, b => b != null && b.name == SkullBone) < 0)
                    continue;
                string texture = smr.sharedMaterial != null && smr.sharedMaterial.mainTexture != null
                    ? smr.sharedMaterial.mainTexture.name : "";
                bool looksLikeHead = smr.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0
                    || texture.IndexOf("kerbalHead", StringComparison.OrdinalIgnoreCase) >= 0
                    || texture.IndexOf("kerbalGirl", StringComparison.OrdinalIgnoreCase) >= 0;
                if (looksLikeHead && (best == null || smr.sharedMesh.vertexCount > best.sharedMesh.vertexCount))
                    best = smr;
            }
            return best;
        }

        private static void HideStockFace(GameObject root, SkinnedMeshRenderer stockHead)
        {
            if (emptyMesh == null)
                emptyMesh = new Mesh { name = "RealCosmonauts_empty" };

            stockHead.sharedMesh = emptyMesh;
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (string part in HiddenParts)
                {
                    if (smr.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        smr.sharedMesh = emptyMesh;
                        break;
                    }
                }
            }
        }

        private static Material MakeMaterial(Material template, Texture texture, string name)
        {
            var material = template != null ? new Material(template) : new Material(Shader.Find("KSP/Diffuse"));
            material.name = name;
            material.mainTexture = texture;
            if (material.HasProperty("_Color"))
                material.color = Color.white;
            return material;
        }

        private static HeadAsset LoadAsset(string name)
        {
            if (Cache.TryGetValue(name, out HeadAsset cached))
                return cached;

            HeadAsset asset = null;
            string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/RealCosmonauts/Models/" + name + ".rcm");
            try
            {
                asset = new HeadAsset
                {
                    Name = name,
                    Mesh = RcmMesh.Load(path),
                    SkinTexture = GameDatabase.Instance.GetTexture("RealCosmonauts/Textures/" + name + "_skin", false),
                    EyeTexture = GameDatabase.Instance.GetTexture("RealCosmonauts/Textures/eye_brown", false),
                };
                if (asset.SkinTexture == null || asset.EyeTexture == null)
                    Log.Warning($"Head '{name}': texture missing (skin: {asset.SkinTexture != null}, eyes: {asset.EyeTexture != null}).");
            }
            catch (Exception e)
            {
                Log.Error($"Could not load head '{name}' from {path}: {e.Message}");
            }

            Cache[name] = asset;
            return asset;
        }

        private static Transform FindByName(Transform t, string name)
        {
            if (t.name == name)
                return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform found = FindByName(t.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}

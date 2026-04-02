using UnityEngine;
using UnityEditor;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Shared helper methods used across multiple tool classes.
    /// </summary>
    public static class ToolUtility
    {
        /// <summary>
        /// Get the Mesh from a GameObject (SkinnedMeshRenderer or MeshFilter).
        /// </summary>
        internal static Mesh GetMesh(GameObject go)
        {
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null) return smr.sharedMesh;
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null) return mf.sharedMesh;
            return null;
        }

        /// <summary>
        /// Find the avatar root name by walking up the hierarchy.
        /// Checks for VRCAvatarDescriptor first, falls back to Animator.
        /// </summary>
        internal static string FindAvatarRootName(GameObject go)
        {
            Transform current = go.transform;
            string bestName = null;

            while (current != null)
            {
                if (current.GetComponent("VRCAvatarDescriptor") != null ||
                    current.GetComponent("VRC_AvatarDescriptor") != null)
                    return current.name;

                if (current.GetComponent<Animator>() != null)
                    bestName = current.name;

                current = current.parent;
            }

            return bestName;
        }

        /// <summary>
        /// Save a Material asset to the Generated/Materials folder.
        /// </summary>
        internal static string SaveMaterialAsset(Material mat, string avatarName)
        {
            string folderPath = $"{PackagePaths.GetGeneratedDir("Materials")}/{avatarName}";
            if (!System.IO.Directory.Exists(folderPath))
                System.IO.Directory.CreateDirectory(folderPath);

            string assetPath = $"{folderPath}/{mat.name}.mat";
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(mat, assetPath);
            return assetPath;
        }

        /// <summary>
        /// Ensure an asset folder path exists, creating intermediate folders as needed.
        /// Uses AssetDatabase API for proper Unity integration.
        /// </summary>
        public static void EnsureAssetDirectory(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class AssetTools
    {
        internal static bool IsProjectPath(string path, bool allowPackages = false) =>
            !string.IsNullOrEmpty(path) && !path.Contains("\\") &&
            (path.StartsWith("Assets/", System.StringComparison.Ordinal) ||
             (allowPackages && path.StartsWith("Packages/", System.StringComparison.Ordinal))) &&
            path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

        internal static bool TryResolveAssetPath(string requested, out string path, out string error, bool allowPackages = false)
        {
            path = null;
            error = null;
            if (IsProjectPath(requested, allowPackages))
            {
                string guid = AssetDatabase.AssetPathToGUID(requested, AssetPathToGUIDOptions.OnlyExistingAssets);
                if (!string.IsNullOrEmpty(guid)) path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) return true;
            }

            error = $"Error: Asset '{requested}' was not found. Pass a full project asset path under Assets/" +
                    (allowPackages ? " or Packages/." : ".");
            string leaf = requested ?? "";
            leaf = leaf.Substring(System.Math.Max(leaf.LastIndexOf('/'), leaf.LastIndexOf('\\')) + 1);
            var candidates = FindExactAssetPaths(leaf, allowPackages).Take(12).ToArray();
            if (candidates.Length > 0) error += " Candidate asset paths: " + string.Join(", ", candidates) + ".";
            return false;
        }

        internal static string[] FindExactAssetPaths(string name, bool allowPackages = true)
        {
            if (string.IsNullOrWhiteSpace(name)) return System.Array.Empty<string>();
            return AssetDatabase.GetAllAssetPaths()
                .Where(path => IsProjectPath(path, allowPackages) &&
                    (string.Equals(Path.GetFileName(path), name, System.StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(Path.GetFileNameWithoutExtension(path), name, System.StringComparison.OrdinalIgnoreCase)))
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .ToArray();
        }
        /// <summary>
        /// 指定されたパスにフォルダを作成します。
        /// </summary>
        /// <param name="parentPath">親フォルダのパス（例: "Assets"）</param>
        /// <param name="folderName">作成するフォルダ名</param>
        [AgentTool("指定されたパスにフォルダを作成します。")]
        public static string CreateFolder(string parentPath, string folderName)
        {
            if (!AssetDatabase.IsValidFolder(parentPath))
            {
                return $"Error: Parent path '{parentPath}' does not exist.";
            }

            string guid = AssetDatabase.CreateFolder(parentPath, folderName);
            if (string.IsNullOrEmpty(guid))
            {
                return $"Error: Failed to create folder '{folderName}' in '{parentPath}'.";
            }

            return $"Success: Folder '{folderName}' created in '{parentPath}'. Path: {AssetDatabase.GUIDToAssetPath(guid)}";
        }

        [AgentTool("Create a primitive GameObject (Cube, Sphere, Capsule, Cylinder, Plane, Quad).")]
        public static string CreatePrimitive(string type, string name = "", string parentName = "")
        {
            if (!System.Enum.TryParse(type, true, out PrimitiveType primitiveType) || !System.Enum.IsDefined(typeof(PrimitiveType), primitiveType))
            {
                return $"Error: Invalid PrimitiveType '{type}'. Supported: Cube, Sphere, Capsule, Cylinder, Plane, Quad.";
            }

            if (!SceneTools.TryResolveCreationParent(parentName, out var parent, out string error)) return error;
            var go = GameObject.CreatePrimitive(primitiveType);
            if (parent != null) go.transform.SetParent(parent.transform);
            go.name = SceneTools.UniqueSiblingName(go, string.IsNullOrEmpty(name) ? type : name);
            Undo.RegisterCreatedObjectUndo(go, "Create Primitive via Agent");
            return $"Success: Created {type} {SceneTools.DescribeTarget(go)}.";
        }

        /// <summary>
        /// アセットを別のパスに移動します。
        /// </summary>
        /// <param name="sourcePath">移動元のアセットパス（例: "Assets/Old/MyMaterial.mat"）</param>
        /// <param name="destinationPath">移動先のアセットパス（例: "Assets/New/MyMaterial.mat"）</param>
        [AgentTool("Move an asset to a different path. Both sourcePath and destinationPath must be full asset paths starting with 'Assets/' (e.g. 'Assets/Models/Old/Cube.fbx' to 'Assets/Models/New/Cube.fbx'). The destination folder must already exist.")]
        public static string MoveAsset(string sourcePath, string destinationPath)
        {
            if (!TryResolveAssetPath(sourcePath, out string source, out string error)) return error;
            if (!IsProjectPath(destinationPath))
                return $"Error: Invalid destination path '{destinationPath}'.";

            string destFolder = Path.GetDirectoryName(destinationPath)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(destFolder))
                return $"Error: Destination folder '{destFolder}' does not exist.";

            error = AssetDatabase.MoveAsset(source, destinationPath);
            if (!string.IsNullOrEmpty(error))
                return $"Error: {error}";

            return $"Success: Moved '{source}' to '{destinationPath}'.";
        }

        /// <summary>
        /// アセットをリネームします。
        /// </summary>
        /// <param name="assetPath">リネームするアセットのパス（例: "Assets/Materials/Old.mat"）</param>
        /// <param name="newName">新しいファイル名（拡張子なし）</param>
        [AgentTool("Rename an asset. assetPath is the full path (e.g. 'Assets/Materials/OldName.mat'). newName is the new filename without extension (e.g. 'NewName'). The extension is preserved automatically.")]
        public static string RenameAsset(string assetPath, string newName)
        {
            if (!TryResolveAssetPath(assetPath, out string source, out string error)) return error;
            if (string.IsNullOrWhiteSpace(newName) || newName == "." || newName == ".." || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return $"Error: Invalid new name '{newName}'. Must be a filename without path separators.";

            string guid = AssetDatabase.AssetPathToGUID(source, AssetPathToGUIDOptions.OnlyExistingAssets);
            error = AssetDatabase.RenameAsset(source, newName);
            if (!string.IsNullOrEmpty(error))
                return $"Error: {error}";

            return $"Success: Renamed '{source}' to '{AssetDatabase.GUIDToAssetPath(guid)}'.";
        }

        /// <summary>
        /// アセットをコピーします。
        /// </summary>
        /// <param name="sourcePath">コピー元のアセットパス（例: "Assets/Materials/Original.mat"）</param>
        /// <param name="destinationPath">コピー先のアセットパス（例: "Assets/Materials/Copy.mat"）</param>
        [AgentTool("Copy an asset to a new path. Both sourcePath and destinationPath must be full asset paths starting with 'Assets/' (e.g. 'Assets/Materials/Original.mat' to 'Assets/Materials/Duplicate.mat'). The destination folder must already exist. Will not overwrite an existing file.")]
        public static string CopyAsset(string sourcePath, string destinationPath)
        {
            if (!TryResolveAssetPath(sourcePath, out string source, out string error)) return error;
            if (!IsProjectPath(destinationPath))
                return $"Error: Invalid destination path '{destinationPath}'.";

            string destFolder = Path.GetDirectoryName(destinationPath)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(destFolder))
                return $"Error: Destination folder '{destFolder}' does not exist.";

            if (!AssetDatabase.CopyAsset(source, destinationPath))
                return $"Error: Failed to copy '{source}' to '{destinationPath}'.";

            return $"Success: Copied '{source}' to '{destinationPath}'.";
        }

        /// <summary>
        /// 指定されたパスのアセットまたはフォルダを削除します。
        /// </summary>
        /// <param name="path">削除するアセットのパス（例: "Assets/OldFolder"）</param>
        [AgentTool("指定されたパスのアセットまたはフォルダを削除します。Requires user confirmation when enabled.")]
        public static string DeleteAsset(string path)
        {
            if (!TryResolveAssetPath(path, out string source, out string error)) return error;
            if (!AgentSettings.RequestConfirmation(
                "アセットを削除",
                $"'{source}' を削除します。"))
                return "Cancelled: User denied the deletion.";

            if (AssetDatabase.DeleteAsset(source))
            {
                return $"Success: Deleted asset at '{source}'.";
            }
            return $"Error: Failed to delete asset at '{source}'.";
        }
    }
}

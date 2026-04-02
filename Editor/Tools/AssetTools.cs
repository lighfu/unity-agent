using System.IO;
using UnityEditor;
using UnityEngine;
using AjisaiFlow.UnityAgent.Editor;
using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class AssetTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
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
            if (!System.Enum.TryParse(type, true, out PrimitiveType primitiveType))
            {
                return $"Error: Invalid PrimitiveType '{type}'. Supported: Cube, Sphere, Capsule, Cylinder, Plane, Quad.";
            }

            GameObject go = GameObject.CreatePrimitive(primitiveType);

            // 名前を設定
            string baseName = string.IsNullOrEmpty(name) ? type : name;
            go.name = baseName;

            // 名前の一意性を保証: GameObject.Find で確実にこのオブジェクトを見つけられるようにする
            var found = GameObject.Find(go.name);
            if (found != null && found != go)
            {
                int suffix = 1;
                string uniqueName;
                do
                {
                    uniqueName = $"{baseName} ({suffix})";
                    suffix++;
                } while (GameObject.Find(uniqueName) != null);
                go.name = uniqueName;
            }

            Undo.RegisterCreatedObjectUndo(go, "Create Primitive via Agent");

            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = FindGO(parentName);
                if (parent != null)
                {
                    go.transform.SetParent(parent.transform);
                }
            }
            return $"Success: Created {type} '{go.name}'.";
        }

        /// <summary>
        /// アセットを別のパスに移動します。
        /// </summary>
        /// <param name="sourcePath">移動元のアセットパス（例: "Assets/Old/MyMaterial.mat"）</param>
        /// <param name="destinationPath">移動先のアセットパス（例: "Assets/New/MyMaterial.mat"）</param>
        [AgentTool("Move an asset to a different path. Both sourcePath and destinationPath must be full asset paths starting with 'Assets/' (e.g. 'Assets/Models/Old/Cube.fbx' to 'Assets/Models/New/Cube.fbx'). The destination folder must already exist.")]
        public static string MoveAsset(string sourcePath, string destinationPath)
        {
            if (!sourcePath.StartsWith("Assets/") || sourcePath.Contains(".."))
                return $"Error: Invalid source path '{sourcePath}'.";
            if (!destinationPath.StartsWith("Assets/") || destinationPath.Contains(".."))
                return $"Error: Invalid destination path '{destinationPath}'.";

            string destFolder = Path.GetDirectoryName(destinationPath)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(destFolder))
                return $"Error: Destination folder '{destFolder}' does not exist.";

            string error = AssetDatabase.MoveAsset(sourcePath, destinationPath);
            if (!string.IsNullOrEmpty(error))
                return $"Error: {error}";

            return $"Success: Moved '{sourcePath}' to '{destinationPath}'.";
        }

        /// <summary>
        /// アセットをリネームします。
        /// </summary>
        /// <param name="assetPath">リネームするアセットのパス（例: "Assets/Materials/Old.mat"）</param>
        /// <param name="newName">新しいファイル名（拡張子なし）</param>
        [AgentTool("Rename an asset. assetPath is the full path (e.g. 'Assets/Materials/OldName.mat'). newName is the new filename without extension (e.g. 'NewName'). The extension is preserved automatically.")]
        public static string RenameAsset(string assetPath, string newName)
        {
            if (!assetPath.StartsWith("Assets/") || assetPath.Contains(".."))
                return $"Error: Invalid asset path '{assetPath}'.";
            if (string.IsNullOrWhiteSpace(newName) || newName.Contains("/") || newName.Contains("\\"))
                return $"Error: Invalid new name '{newName}'. Must be a filename without path separators.";

            string error = AssetDatabase.RenameAsset(assetPath, newName);
            if (!string.IsNullOrEmpty(error))
                return $"Error: {error}";

            return $"Success: Renamed '{assetPath}' to '{newName}'.";
        }

        /// <summary>
        /// アセットをコピーします。
        /// </summary>
        /// <param name="sourcePath">コピー元のアセットパス（例: "Assets/Materials/Original.mat"）</param>
        /// <param name="destinationPath">コピー先のアセットパス（例: "Assets/Materials/Copy.mat"）</param>
        [AgentTool("Copy an asset to a new path. Both sourcePath and destinationPath must be full asset paths starting with 'Assets/' (e.g. 'Assets/Materials/Original.mat' to 'Assets/Materials/Duplicate.mat'). The destination folder must already exist. Will not overwrite an existing file.")]
        public static string CopyAsset(string sourcePath, string destinationPath)
        {
            if (!sourcePath.StartsWith("Assets/") || sourcePath.Contains(".."))
                return $"Error: Invalid source path '{sourcePath}'.";
            if (!destinationPath.StartsWith("Assets/") || destinationPath.Contains(".."))
                return $"Error: Invalid destination path '{destinationPath}'.";

            string destFolder = Path.GetDirectoryName(destinationPath)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(destFolder))
                return $"Error: Destination folder '{destFolder}' does not exist.";

            if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                return $"Error: Failed to copy '{sourcePath}' to '{destinationPath}'.";

            return $"Success: Copied '{sourcePath}' to '{destinationPath}'.";
        }

        /// <summary>
        /// 指定されたパスのアセットまたはフォルダを削除します。
        /// </summary>
        /// <param name="path">削除するアセットのパス（例: "Assets/OldFolder"）</param>
        [AgentTool("指定されたパスのアセットまたはフォルダを削除します。Requires user confirmation when enabled.")]
        public static string DeleteAsset(string path)
        {
            if (!AgentSettings.RequestConfirmation(
                "アセットを削除",
                $"'{path}' を削除します。"))
                return "Cancelled: User denied the deletion.";

            if (AssetDatabase.DeleteAsset(path))
            {
                return $"Success: Deleted asset at '{path}'.";
            }
            return $"Error: Failed to delete asset at '{path}'. Check if the path is correct.";
        }
    }
}

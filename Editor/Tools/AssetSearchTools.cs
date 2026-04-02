using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class AssetSearchTools
    {
        [AgentTool("Search for assets by name keyword (first arg) with optional type filter (second arg, e.g. Material, Texture2D, Prefab, Mesh, AnimationClip). Usage: SearchAssets(\"ring\") or SearchAssets(\"ring\", \"Prefab\"). Returns up to 20 results. Use ListTopFolders() first to browse the project structure if you don't know what to search for.")]
        public static string SearchAssets(string query, string typeFilter = "")
        {
            string filter = query;
            if (!string.IsNullOrEmpty(typeFilter))
                filter += $" t:{typeFilter}";

            string[] guids = AssetDatabase.FindAssets(filter);

            // If no results and query might be Japanese/specific, try searching in asset paths
            if (guids.Length == 0)
            {
                // Try broader search without type filter to give suggestions
                string[] broaderGuids = string.IsNullOrEmpty(typeFilter) ? new string[0] : AssetDatabase.FindAssets(query);
                if (broaderGuids.Length > 0)
                {
                    var sb2 = new StringBuilder();
                    sb2.AppendLine($"No '{typeFilter}' assets found matching '{query}', but found {broaderGuids.Length} other asset(s):");
                    int limit2 = Math.Min(broaderGuids.Length, 10);
                    for (int i = 0; i < limit2; i++)
                    {
                        string p = AssetDatabase.GUIDToAssetPath(broaderGuids[i]);
                        var a = AssetDatabase.LoadMainAssetAtPath(p);
                        string tn = a != null ? a.GetType().Name : "Unknown";
                        sb2.AppendLine($"  {i + 1}. [{tn}] {p}");
                    }
                    if (broaderGuids.Length > 10) sb2.AppendLine($"  ... and {broaderGuids.Length - 10} more.");
                    sb2.AppendLine("Tip: Try without typeFilter, or use ListTopFolders() / ListAssetsInFolder() to browse.");
                    return sb2.ToString().TrimEnd();
                }

                return $"No assets found matching '{query}'" + (string.IsNullOrEmpty(typeFilter) ? "" : $" (type: {typeFilter})") + ". Try a different keyword, or use ListTopFolders() to browse the project structure.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {guids.Length} asset(s) matching '{query}'" + (string.IsNullOrEmpty(typeFilter) ? "" : $" (type: {typeFilter})") + ":");

            int limit = Math.Min(guids.Length, 20);
            for (int i = 0; i < limit; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                string typeName = asset != null ? asset.GetType().Name : "Unknown";
                sb.AppendLine($"  {i + 1}. [{typeName}] {path}");
            }

            if (guids.Length > 20)
                sb.AppendLine($"  ... and {guids.Length - 20} more. Refine your search for more specific results.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("List assets in a folder. Includes subfolders by default. Set recursive=false for direct children only.")]
        public static string ListAssetsInFolder(string folderPath, bool recursive = true)
        {
            folderPath = folderPath.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(folderPath))
                return $"Error: Folder '{folderPath}' does not exist.";

            string[] guids;
            if (recursive)
            {
                guids = AssetDatabase.FindAssets("", new[] { folderPath });
            }
            else
            {
                // Non-recursive: only direct children
                var allGuids = AssetDatabase.FindAssets("", new[] { folderPath });
                guids = allGuids.Where(guid =>
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string dir = Path.GetDirectoryName(path).Replace('\\', '/');
                    return dir == folderPath;
                }).ToArray();
            }

            if (guids.Length == 0) return $"No assets found in '{folderPath}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Assets in '{folderPath}' ({guids.Length}):");

            int limit = Math.Min(guids.Length, 20);
            for (int i = 0; i < limit; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                bool isFolder = AssetDatabase.IsValidFolder(path);
                if (isFolder)
                {
                    sb.AppendLine($"  {i + 1}. [Folder] {path}");
                }
                else
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    string typeName = asset != null ? asset.GetType().Name : "Unknown";
                    sb.AppendLine($"  {i + 1}. [{typeName}] {path}");
                }
            }

            if (guids.Length > 20)
                sb.AppendLine($"  ... and {guids.Length - 20} more.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Get detailed info about an asset (type, file size, dependencies, key properties).")]
        public static string GetAssetInfo(string assetPath)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null) return $"Error: Asset not found at '{assetPath}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Asset: {assetPath}");
            sb.AppendLine($"  Name: {asset.name}");
            sb.AppendLine($"  Type: {asset.GetType().FullName}");

            // File size
            string fullPath = Path.GetFullPath(assetPath);
            if (File.Exists(fullPath))
            {
                long bytes = new FileInfo(fullPath).Length;
                sb.AppendLine($"  Size: {FormatFileSize(bytes)}");
            }

            // Dependencies
            string[] deps = AssetDatabase.GetDependencies(assetPath, false);
            if (deps.Length > 1 || (deps.Length == 1 && deps[0] != assetPath))
            {
                var filteredDeps = deps.Where(d => d != assetPath).ToArray();
                sb.AppendLine($"  Dependencies ({filteredDeps.Length}):");
                int limit = Math.Min(filteredDeps.Length, 10);
                for (int i = 0; i < limit; i++)
                    sb.AppendLine($"    - {filteredDeps[i]}");
                if (filteredDeps.Length > 10)
                    sb.AppendLine($"    ... and {filteredDeps.Length - 10} more.");
            }

            // Type-specific info
            if (asset is Texture2D tex)
            {
                sb.AppendLine($"  Resolution: {tex.width}x{tex.height}");
                sb.AppendLine($"  Format: {tex.format}");
                sb.AppendLine($"  MipMaps: {tex.mipmapCount}");
            }
            else if (asset is Mesh mesh)
            {
                sb.AppendLine($"  Vertices: {mesh.vertexCount}");
                sb.AppendLine($"  Triangles: {mesh.triangles.Length / 3}");
                sb.AppendLine($"  SubMeshes: {mesh.subMeshCount}");
                sb.AppendLine($"  BlendShapes: {mesh.blendShapeCount}");
            }
            else if (asset is Material mat)
            {
                sb.AppendLine($"  Shader: {mat.shader.name}");
                sb.AppendLine($"  RenderQueue: {mat.renderQueue}");
            }
            else if (asset is AnimationClip clip)
            {
                sb.AppendLine($"  Length: {clip.length:F2}s");
                sb.AppendLine($"  FrameRate: {clip.frameRate}");
                sb.AppendLine($"  WrapMode: {clip.wrapMode}");
                sb.AppendLine($"  Loop: {clip.isLooping}");
            }
            else if (asset is AudioClip audio)
            {
                sb.AppendLine($"  Length: {audio.length:F2}s");
                sb.AppendLine($"  Channels: {audio.channels}");
                sb.AppendLine($"  Frequency: {audio.frequency}Hz");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("List sub-assets within an asset file (e.g. meshes and animations inside an FBX).")]
        public static string ListSubAssets(string assetPath)
        {
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (allAssets == null || allAssets.Length == 0)
                return $"Error: No assets found at '{assetPath}'.";

            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            var subAssets = allAssets.Where(a => a != mainAsset && a != null).ToArray();

            if (subAssets.Length == 0) return $"No sub-assets in '{assetPath}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Sub-assets in '{assetPath}' ({subAssets.Length}):");

            int limit = Math.Min(subAssets.Length, 20);
            for (int i = 0; i < limit; i++)
            {
                var sub = subAssets[i];
                sb.AppendLine($"  {i + 1}. [{sub.GetType().Name}] {sub.name}");
            }

            if (subAssets.Length > 20)
                sb.AppendLine($"  ... and {subAssets.Length - 20} more.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("List top-level folders under Assets to understand project structure. Use this first when you need to find assets but don't know where they are.")]
        public static string ListTopFolders()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Project top-level folders:");

            string[] guids = AssetDatabase.FindAssets("", new[] { "Assets" });
            var folders = new System.Collections.Generic.HashSet<string>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Extract first-level folder under Assets
                if (path.StartsWith("Assets/"))
                {
                    string relativePath = path.Substring("Assets/".Length);
                    int slashIndex = relativePath.IndexOf('/');
                    if (slashIndex > 0)
                    {
                        folders.Add("Assets/" + relativePath.Substring(0, slashIndex));
                    }
                }
            }

            var sortedFolders = folders.OrderBy(f => f).ToArray();
            foreach (string folder in sortedFolders)
            {
                // Count items in folder
                int itemCount = AssetDatabase.FindAssets("", new[] { folder }).Length;
                sb.AppendLine($"  {folder}/ ({itemCount} items)");
            }

            sb.AppendLine($"\nTotal: {sortedFolders.Length} folders. Use ListAssetsInFolder(\"folderPath\") to explore contents.");

            return sb.ToString().TrimEnd();
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}

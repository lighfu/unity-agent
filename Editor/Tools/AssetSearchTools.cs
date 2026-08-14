using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class AssetSearchTools
    {
        /// <summary>
        /// Asset types Unity serializes as text and that can therefore hold a GUID reference.
        /// Binary formats (.fbx, .png, .unitypackage) never reference other assets by GUID.
        /// </summary>
        private static readonly string[] DefaultReferenceExtensions =
        {
            ".unity", ".prefab", ".mat", ".controller", ".overrideController",
            ".anim", ".asset", ".playable", ".mask", ".physicMaterial", ".preset",
            ".shadervariants", ".spriteatlas", ".terrainlayer", ".lighting", ".renderTexture",
            // .meta carries real references: an FBX importer's externalObjects material remap,
            // AvatarMask/Avatar sub-asset links, and other importer settings all live there.
            // Omitting them made a remapped material look unreferenced.
            ".meta",
        };

        // A single asset larger than this is a generated data blob (baked lightmaps, long motion
        // clips), not something that holds hand-authored references worth this much I/O.
        private const long MaxScanFileBytes = 96L * 1024 * 1024;

        // Unity writes references as "guid: <32 hex chars>". Scanning for the prefix once and
        // hash-checking the 32 bytes that follow costs the same whether we are looking for one
        // GUID or two hundred — important when the target is a folder.
        private static readonly byte[] GuidPrefix = Encoding.ASCII.GetBytes("guid: ");
        private const int GuidLength = 32;

        [AgentTool(@"Find which assets REFERENCE a given asset or folder — the reverse of GetAssetInfo's
dependency list. Use before deleting or moving anything: 'what breaks if this disappears'.

assetPath: an asset ('Assets/Foo/Bar.mat') or a folder ('Assets/Foo'). A folder resolves every
  asset inside it and reports references to any of them.
searchInFolder: restrict the scan to this folder (e.g. 'Assets/900_Avatars'). Empty = all of Assets.
  Narrowing this is the single biggest speedup on a large project.
extensions: ';' separated file extensions to scan. Empty = the standard text-serialized set
  (.unity .prefab .mat .controller .overrideController .anim .asset .playable .mask .preset .meta ...).
limit: maximum referencing assets to report per target (default 200).
timeoutSeconds: give up after this long and return PARTIAL results, clearly marked (default 60,
  clamped to 110 — the scan blocks the editor's main thread and the MCP transport quits at 120 s).
includePackages: also scan Packages/ (default false — package assets rarely reference project assets).

Assets/ and ProjectSettings/ are always scanned. ProjectSettings matters because Always Included
Shaders and the preloaded-assets list live only there, and .meta matters because FBX importers
remap materials through externalObjects.

Cost: including .meta roughly triples the file count. On a ~10 GB project expect ~45 s for a whole
-project scan. Narrow it with searchInFolder, or drop '.meta' from extensions, when that matters.

Scans text-serialized assets for the target GUID. On a multi-gigabyte project this is I/O bound;
the scan runs in parallel and reports exactly how much it covered. A partial result is never
presented as complete — if it timed out or skipped files, deleting on that basis is not safe.",
            Risk = ToolRisk.Safe)]
        public static string FindReferencesTo(
            string assetPath,
            string searchInFolder = "",
            string extensions = "",
            int limit = 200,
            int timeoutSeconds = 60,
            bool includePackages = false)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return "Error: assetPath is required.";
            if (limit <= 0) limit = 200;
            if (timeoutSeconds <= 0) timeoutSeconds = 60;
            // The scan runs synchronously on the main thread, so this value is how long the whole
            // editor freezes. Cap it at the transport's own deadline — a longer scan cannot report
            // anything back, it can only wedge the editor past the point where the caller is
            // still listening (and, with the watchdog, make every parallel agent bounce too).
            if (timeoutSeconds > EditorStateTools.MaxToolSeconds) timeoutSeconds = EditorStateTools.MaxToolSeconds;

            bool isFolder = AssetDatabase.IsValidFolder(assetPath);
            var targets = new Dictionary<string, string>(StringComparer.Ordinal); // guid -> path

            if (isFolder)
            {
                foreach (string guid in AssetDatabase.FindAssets("", new[] { assetPath }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(p) || AssetDatabase.IsValidFolder(p)) continue;
                    targets[guid] = p;
                }
                if (targets.Count == 0)
                    return $"Error: folder '{assetPath}' contains no assets.";
            }
            else
            {
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                    return $"Error: no asset at '{assetPath}' (or it has no GUID).";
                targets[guid] = assetPath;
            }

            var exts = ParseExtensions(extensions);
            var targetGuids = new HashSet<string>(targets.Keys, StringComparer.OrdinalIgnoreCase);

            // Exclude the target itself AND its .meta. Every .meta opens with "guid: <its own guid>",
            // so without this the tool reports an asset's own sidecar as a referrer — a false
            // positive on literally every lookup once .meta files are in the scan set.
            var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in targets.Values)
            {
                targetPaths.Add(path);
                targetPaths.Add(path + ".meta");
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

            var searchRoots = new List<string>();
            if (!string.IsNullOrWhiteSpace(searchInFolder))
            {
                string abs = Path.IsPathRooted(searchInFolder)
                    ? searchInFolder
                    : Path.Combine(projectRoot, searchInFolder);
                if (!Directory.Exists(abs))
                    return $"Error: searchInFolder '{searchInFolder}' does not exist.";
                searchRoots.Add(abs);
            }
            else
            {
                searchRoots.Add(Application.dataPath);
                // ProjectSettings holds references no asset does: Always Included Shaders in
                // GraphicsSettings, the preloaded-assets list, layer/tag collision matrices.
                // It is a few dozen small files, so there is no reason to make it opt-in.
                string projectSettings = Path.Combine(projectRoot, "ProjectSettings");
                if (Directory.Exists(projectSettings)) searchRoots.Add(projectSettings);
                if (includePackages)
                {
                    string packages = Path.Combine(projectRoot, "Packages");
                    if (Directory.Exists(packages)) searchRoots.Add(packages);
                }
            }

            // Enumerate first so the parallel pass has a fixed work list and an accurate total.
            var candidates = new List<string>();
            foreach (string root in searchRoots)
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories); }
                catch (Exception ex) { return $"Error: cannot enumerate '{root}': {ex.Message}"; }

                foreach (string file in files)
                {
                    if (!exts.Contains(Path.GetExtension(file))) continue;
                    // The target is not its own referrer; drop it here so the scanned/total
                    // counts match instead of looking like a file was silently missed.
                    if (targetPaths.Contains(ToProjectRelative(file, projectRoot))) continue;
                    candidates.Add(file);
                }
            }

            var hits = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentBag<string>>(StringComparer.Ordinal);
            int scanned = 0, skipped = 0, oversized = 0;
            long bytesRead = 0;

            var deadline = System.Diagnostics.Stopwatch.StartNew();
            bool timedOut = false;

            var options = new System.Threading.Tasks.ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1)
            };

            System.Threading.Tasks.Parallel.ForEach(candidates, options, (file, state) =>
            {
                if (deadline.Elapsed.TotalSeconds > timeoutSeconds)
                {
                    timedOut = true;
                    state.Stop();
                    return;
                }

                string rel = ToProjectRelative(file, projectRoot);

                byte[] bytes;
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length > MaxScanFileBytes)
                    {
                        System.Threading.Interlocked.Increment(ref oversized);
                        return;
                    }
                    bytes = File.ReadAllBytes(file);
                }
                catch
                {
                    System.Threading.Interlocked.Increment(ref skipped);
                    return;
                }

                System.Threading.Interlocked.Increment(ref scanned);
                System.Threading.Interlocked.Add(ref bytesRead, bytes.LongLength);

                foreach (string guid in ExtractReferencedGuids(bytes, targetGuids))
                    hits.GetOrAdd(guid, _ => new System.Collections.Concurrent.ConcurrentBag<string>()).Add(rel);
            });

            deadline.Stop();

            var sb = new StringBuilder();
            sb.AppendLine($"References to: {assetPath}{(isFolder ? $"  (folder, {targets.Count} assets)" : "")}");
            sb.AppendLine($"Scanned {scanned:N0} of {candidates.Count:N0} candidate assets " +
                          $"({bytesRead / 1024.0 / 1024.0:F0} MB) in {deadline.Elapsed.TotalSeconds:F1}s");
            if (oversized > 0) sb.AppendLine($"  {oversized} file(s) skipped as larger than {MaxScanFileBytes / 1024 / 1024} MB.");
            if (skipped > 0) sb.AppendLine($"  {skipped} file(s) unreadable.");

            bool incomplete = timedOut || oversized > 0 || skipped > 0;

            // This scan reads files on disk. A scene edited but not saved holds its references
            // only in memory, so a material dropped onto a renderer five minutes ago is invisible
            // here — exactly the case where "no references found" would be acted on destructively.
            var dirtyScenes = new List<string>();
            for (int i = 0; i < UnityEditor.SceneManagement.EditorSceneManager.sceneCount; i++)
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
                if (scene.isDirty) dirtyScenes.Add(string.IsNullOrEmpty(scene.name) ? "(untitled)" : scene.name);
            }
            if (dirtyScenes.Count > 0)
            {
                incomplete = true;
                sb.AppendLine($"  WARNING: {dirtyScenes.Count} open scene(s) have UNSAVED changes: {string.Join(", ", dirtyScenes)}");
                sb.AppendLine("  This scan reads files on disk, so references added in those scenes are NOT visible.");
                sb.AppendLine("  Save the scene(s) and re-run before treating this result as a safety check.");
            }

            if (timedOut)
            {
                sb.AppendLine($"  TIMED OUT after {timeoutSeconds}s — {candidates.Count - scanned:N0} assets were NOT scanned.");
                sb.AppendLine("  Narrow the scan with searchInFolder, or raise timeoutSeconds.");
            }

            // ForceBinary genuinely defeats the scan. Mixed is Unity's default and is
            // overwhelmingly text in practice, so flagging every Mixed project as "incomplete"
            // would make the warning meaningless — note it without poisoning the verdict.
            if (EditorSettings.serializationMode == SerializationMode.ForceBinary)
            {
                incomplete = true;
                sb.AppendLine("  WARNING: serializationMode is ForceBinary — assets are not text and CANNOT be scanned.");
                sb.AppendLine("  This result is meaningless for safety checks. Switch to Force Text to use this tool.");
            }
            else if (EditorSettings.serializationMode == SerializationMode.Mixed)
            {
                sb.AppendLine("  Note: serializationMode is Mixed (Unity's default). Almost all assets are text, but");
                sb.AppendLine("  any that Unity chose to write as binary are invisible to this scan.");
            }

            int totalReferrers = hits.Values.Sum(l => l.Count);
            if (totalReferrers == 0)
            {
                sb.Append(isFolder
                    ? "No references found to any asset in this folder."
                    : "No references found.");
                if (incomplete)
                    sb.Append("  NOTE: the scan was incomplete (see above) — this is NOT proof that deletion is safe.");
                return sb.ToString();
            }

            sb.AppendLine("---");
            int truncated = 0;
            foreach (var kv in hits.OrderByDescending(k => k.Value.Count))
            {
                string targetName = targets.TryGetValue(kv.Key, out string tn) ? tn : kv.Key;
                var referrers = kv.Value.Distinct(StringComparer.Ordinal)
                                        .OrderBy(r => r, StringComparer.Ordinal).ToList();
                sb.AppendLine($"{targetName}  ({referrers.Count} referrer(s))");
                foreach (string r in referrers.Take(limit)) sb.AppendLine($"    {r}");
                if (referrers.Count > limit)
                {
                    truncated += referrers.Count - limit;
                    sb.AppendLine($"    ... {referrers.Count - limit} more");
                }
            }

            sb.AppendLine("---");
            sb.Append($"{totalReferrers} reference(s) across {hits.Count} target asset(s).");
            if (truncated > 0) sb.Append($" {truncated} suppressed by limit={limit}.");
            if (incomplete) sb.Append("  Scan was INCOMPLETE — see notes above.");
            return sb.ToString();
        }

        /// <summary>
        /// Finds every "guid: xxxx" occurrence in a raw asset file and returns the ones present in
        /// <paramref name="wanted"/>. Operating on bytes avoids decoding gigabytes of YAML into
        /// UTF-16 strings, which is what makes a whole-project scan feasible at all.
        /// </summary>
        private static IEnumerable<string> ExtractReferencedGuids(byte[] bytes, HashSet<string> wanted)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int limit = bytes.Length - (GuidPrefix.Length + GuidLength);

            for (int i = 0; i <= limit; i++)
            {
                if (bytes[i] != GuidPrefix[0]) continue;

                bool prefixMatch = true;
                for (int p = 1; p < GuidPrefix.Length; p++)
                {
                    if (bytes[i + p] != GuidPrefix[p]) { prefixMatch = false; break; }
                }
                if (!prefixMatch) continue;

                int start = i + GuidPrefix.Length;
                string guid = Encoding.ASCII.GetString(bytes, start, GuidLength);
                if (wanted.Contains(guid)) found.Add(guid);

                // Skip past this GUID; overlapping matches are impossible.
                i = start + GuidLength - 1;
            }

            return found;
        }

        private static HashSet<string> ParseExtensions(string extensions)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(extensions))
            {
                foreach (var e in DefaultReferenceExtensions) set.Add(e);
                return set;
            }
            foreach (var raw in extensions.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string e = raw.Trim();
                if (e.Length == 0) continue;
                if (!e.StartsWith(".")) e = "." + e;
                set.Add(e);
            }
            return set;
        }

        private static string ToProjectRelative(string absolutePath, string projectRoot)
        {
            string normalized = absolutePath.Replace('\\', '/');
            string root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(root.Length)
                : normalized;
        }

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

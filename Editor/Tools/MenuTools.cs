using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class MenuTools
    {
        // Cache discovered menu paths (assembly scan is expensive)
        private static List<string> _cachedMenuPaths;
        private static HashSet<string> _cachedTopLevelCategories;

        [AgentTool("Search all Unity menu items (including custom/package menus) matching a keyword. Discovers menus from all installed packages (VRCFury, Modular Avatar, AAO, lilToon, etc.). Example: SearchMenu('avatar') to find avatar-related commands.")]
        public static string SearchMenu(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
                return "Error: keyword is required.";

            var allMenus = GetAllMenuPaths();
            string kw = keyword.ToLower();

            var matches = allMenus.Where(p => p.ToLower().Contains(kw)).ToList();

            if (matches.Count == 0)
                return $"No menu items found matching '{keyword}'. Try a broader keyword, or use ListMenuCategory to browse top-level categories.";

            var sb = new StringBuilder();
            sb.AppendLine($"Menu items matching '{keyword}' ({matches.Count}):");
            foreach (string path in matches.Take(50))
                sb.AppendLine($"  {path}");
            if (matches.Count > 50)
                sb.AppendLine($"  ... and {matches.Count - 50} more. Narrow your search keyword.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Execute a Unity menu item by its full path. Example: ExecuteMenu('File/Save Project'), ExecuteMenu('FaceEmo/New Menu'). If the exact path is not found, similar menu items will be suggested automatically.")]
        public static string ExecuteMenu(string menuPath)
        {
            if (string.IsNullOrEmpty(menuPath))
                return "Error: menuPath is required.";

            // Direct API calls for operations that ExecuteMenuItem may not reliably execute
            string directResult = TryExecuteDirect(menuPath);
            if (directResult != null)
                return directResult;

            try
            {
                bool result = EditorApplication.ExecuteMenuItem(menuPath);
                if (result)
                    return $"Success: Executed menu item '{menuPath}'.";
            }
            catch (Exception ex)
            {
                return $"Error: Failed to execute '{menuPath}': {ex.Message}";
            }

            // Menu item not found — search for similar items automatically
            return SuggestSimilarMenuItems(menuPath);
        }

        private static string SuggestSimilarMenuItems(string menuPath)
        {
            var allMenus = GetAllMenuPaths();

            // Extract keywords from the failed path for fuzzy matching
            var keywords = menuPath.Split('/')
                .SelectMany(seg => seg.Split(' ', '_', '-'))
                .Where(w => w.Length >= 2)
                .Select(w => w.ToLower())
                .ToList();

            // Score each menu item by keyword overlap
            var scored = new List<(string path, int score)>();
            foreach (string path in allMenus)
            {
                string lower = path.ToLower();
                int score = 0;
                foreach (string kw in keywords)
                {
                    if (lower.Contains(kw))
                        score += kw.Length; // Longer keyword matches score higher
                }
                if (score > 0)
                    scored.Add((path, score));
            }

            var suggestions = scored.OrderByDescending(s => s.score).Take(10).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Error: Menu item '{menuPath}' not found.");

            if (suggestions.Count > 0)
            {
                sb.AppendLine("Did you mean one of these?");
                foreach (var (path, _) in suggestions)
                    sb.AppendLine($"  {path}");
            }
            else
            {
                sb.AppendLine("No similar menu items found. Use SearchMenu to discover available items.");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Intercept known menu paths and call the underlying Unity API directly
        /// to ensure reliable execution.
        /// </summary>
        private static string TryExecuteDirect(string menuPath)
        {
            switch (menuPath)
            {
                case "File/Save Project":
                    AssetDatabase.SaveAssets();
                    EditorSceneManager.SaveOpenScenes();
                    return "Success: Project saved (assets + scenes).";

                case "File/Save":
                    EditorSceneManager.SaveOpenScenes();
                    AssetDatabase.SaveAssets();
                    return "Success: Saved (scenes + assets).";

                case "File/Save As...":
                    // Cannot automate Save As dialog — fall through to ExecuteMenuItem
                    return null;

                case "Assets/Refresh":
                    AssetDatabase.Refresh();
                    return "Success: AssetDatabase refreshed.";

                case "Assets/Reimport All":
                    AssetDatabase.ImportAsset("Assets", ImportAssetOptions.ImportRecursive);
                    return "Success: Reimporting all assets.";

                case "Edit/Play":
                    EditorApplication.isPlaying = !EditorApplication.isPlaying;
                    return EditorApplication.isPlaying
                        ? "Success: Entered Play mode."
                        : "Success: Exited Play mode.";

                case "Edit/Pause":
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    return EditorApplication.isPaused
                        ? "Success: Editor paused."
                        : "Success: Editor unpaused.";

                case "Edit/Step":
                    EditorApplication.Step();
                    return "Success: Stepped one frame.";

                case "Edit/Undo":
                    Undo.PerformUndo();
                    return "Success: Undo performed.";

                case "Edit/Redo":
                    Undo.PerformRedo();
                    return "Success: Redo performed.";

                default:
                    return null; // Not intercepted — use ExecuteMenuItem
            }
        }

        [AgentTool("List all menu items under a category. Discovers all categories automatically including custom package menus. Use without arguments or with empty string to list all top-level categories. Example: ListMenuCategory('Tools'), ListMenuCategory('VRChat SDK').")]
        public static string ListMenuCategory(string category = "")
        {
            var allMenus = GetAllMenuPaths();

            // If no category specified, list top-level categories
            if (string.IsNullOrEmpty(category))
            {
                var categories = GetTopLevelCategories();
                var sb = new StringBuilder();
                sb.AppendLine($"Available top-level menu categories ({categories.Count}):");
                foreach (string cat in categories.OrderBy(c => c))
                {
                    int count = allMenus.Count(p => p.StartsWith(cat + "/", StringComparison.OrdinalIgnoreCase));
                    sb.AppendLine($"  {cat}/ ({count} items)");
                }
                return sb.ToString().TrimEnd();
            }

            string prefix = category.EndsWith("/") ? category : category + "/";
            var matches = allMenus.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count == 0)
                return $"No menu items found under '{category}'. Use ListMenuCategory('') to see all available categories.";

            // Group by next level for readability
            var sb2 = new StringBuilder();
            sb2.AppendLine($"Menu items under '{category}' ({matches.Count}):");

            // Show sub-categories and leaf items
            var subCategories = new Dictionary<string, int>();
            var leafItems = new List<string>();

            foreach (string path in matches)
            {
                string remainder = path.Substring(prefix.Length);
                int slashIdx = remainder.IndexOf('/');
                if (slashIdx >= 0)
                {
                    string subCat = remainder.Substring(0, slashIdx);
                    string key = prefix + subCat;
                    if (!subCategories.ContainsKey(key))
                        subCategories[key] = 0;
                    subCategories[key]++;
                }
                else
                {
                    leafItems.Add(path);
                }
            }

            // Sub-categories first
            foreach (var kvp in subCategories.OrderBy(k => k.Key))
                sb2.AppendLine($"  [{kvp.Key}/] ({kvp.Value} items)");

            // Then leaf items
            foreach (string item in leafItems)
                sb2.AppendLine($"  {item}");

            return sb2.ToString().TrimEnd();
        }

        [AgentTool("Force refresh the menu item cache. Use this after installing new packages or if menu items seem outdated.")]
        public static string RefreshMenuCache()
        {
            _cachedMenuPaths = null;
            _cachedTopLevelCategories = null;
            var allMenus = GetAllMenuPaths();
            var categories = GetTopLevelCategories();
            return $"Success: Refreshed menu cache. Found {allMenus.Count} menu items across {categories.Count} top-level categories.";
        }

        // ─── Discovery ───

        private static List<string> GetAllMenuPaths()
        {
            if (_cachedMenuPaths != null)
                return _cachedMenuPaths;

            var paths = new HashSet<string>();

            // 1. Scan all assemblies for [MenuItem] attributes
            DiscoverMenuItemAttributes(paths);

            // 2. Add built-in menus that may not have [MenuItem] (Unity internal)
            AddBuiltInMenuPaths(paths);

            _cachedMenuPaths = paths.OrderBy(p => p).ToList();
            return _cachedMenuPaths;
        }

        private static HashSet<string> GetTopLevelCategories()
        {
            if (_cachedTopLevelCategories != null)
                return _cachedTopLevelCategories;

            _cachedTopLevelCategories = new HashSet<string>();
            foreach (string path in GetAllMenuPaths())
            {
                int slashIdx = path.IndexOf('/');
                if (slashIdx > 0)
                    _cachedTopLevelCategories.Add(path.Substring(0, slashIdx));
            }
            return _cachedTopLevelCategories;
        }

        private static void DiscoverMenuItemAttributes(HashSet<string> paths)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Skip non-editor assemblies for performance
                string asmName = assembly.GetName().Name;
                if (asmName.StartsWith("System") || asmName.StartsWith("mscorlib") ||
                    asmName.StartsWith("Mono.") || asmName.StartsWith("netstandard"))
                    continue;

                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            try
                            {
                                var attrs = method.GetCustomAttributes(typeof(MenuItem), false);
                                foreach (MenuItem attr in attrs)
                                {
                                    string menuItem = attr.menuItem;
                                    if (string.IsNullOrEmpty(menuItem)) continue;

                                    // Skip validation menu items (prefixed with "CONTEXT/")
                                    if (menuItem.StartsWith("CONTEXT/")) continue;

                                    // Remove hotkey suffix (e.g., " %s" for Ctrl+S)
                                    int lastSpace = menuItem.LastIndexOf(' ');
                                    if (lastSpace > 0)
                                    {
                                        string suffix = menuItem.Substring(lastSpace + 1);
                                        if (suffix.Length <= 4 && (suffix.StartsWith("%") || suffix.StartsWith("#") ||
                                            suffix.StartsWith("&") || suffix.StartsWith("_")))
                                        {
                                            menuItem = menuItem.Substring(0, lastSpace);
                                        }
                                    }

                                    paths.Add(menuItem);
                                }
                            }
                            catch { /* Skip methods that fail attribute reading */ }
                        }
                    }
                }
                catch { /* Skip assemblies that fail type enumeration */ }
            }
        }

        private static void AddBuiltInMenuPaths(HashSet<string> paths)
        {
            // Unity built-in menus that don't always have [MenuItem] attributes
            var builtIns = new[]
            {
                "File/New Scene", "File/Open Scene", "File/Save", "File/Save As...",
                "File/Save Project", "File/Build Settings...", "File/Build And Run",
                "Edit/Undo", "Edit/Redo", "Edit/Cut", "Edit/Copy", "Edit/Paste",
                "Edit/Duplicate", "Edit/Delete", "Edit/Select All", "Edit/Deselect All",
                "Edit/Play", "Edit/Pause", "Edit/Step",
                "Edit/Project Settings...", "Edit/Preferences...",
                "Assets/Refresh", "Assets/Reimport", "Assets/Reimport All",
                "Window/General/Scene", "Window/General/Game", "Window/General/Inspector",
                "Window/General/Hierarchy", "Window/General/Project", "Window/General/Console",
                "Window/Package Manager",
            };
            foreach (string path in builtIns)
                paths.Add(path);
        }
    }
}

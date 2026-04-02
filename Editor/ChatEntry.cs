using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using System;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class ResultItem
    {
        public string displayName;
        public string typeName;
        public string reference;
        public bool isAsset;

        public void SelectAndPing()
        {
            if (isAsset)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(reference);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
            else
            {
                var go = GameObject.Find(reference);
                if (go != null)
                {
                    Selection.activeGameObject = go;
                    EditorGUIUtility.PingObject(go);
                    if (SceneView.lastActiveSceneView != null)
                        SceneView.lastActiveSceneView.FrameSelected();
                }
            }
        }
    }

    public class ChatEntry
    {
        public enum EntryType { User, Agent, Info, Error, Choice }

        public EntryType type;
        public string text;
        public DateTime timestamp;
        public List<ResultItem> results;

        // Thinking text (collapsible in UI)
        public string thinkingText;
        public bool thinkingFoldout;

        // Image preview (for scene captures etc.)
        public Texture2D imagePreview;

        // Choice fields
        public string[] choiceOptions;
        public string choiceImportance; // "info", "warning", "critical"
        public int choiceSelectedIndex = -1;
        public bool isToolConfirm; // true if this choice is a tool execution confirmation

        // Batch tool confirm fields
        public bool isBatchToolConfirm;
        public List<BatchToolItem> batchItems;
        public bool batchResolved;

        // Clipboard (manual) provider fields
        public bool isClipboard;

        // Debug log (collapsible, shown only when debug mode is on)
        public List<string> debugLogs;
        public bool debugFoldout;
        public string cachedDebugRich;
        public System.TimeSpan? requestDuration;

        public void AppendDebugLog(string log)
        {
            if (debugLogs == null) debugLogs = new List<string>();
            debugLogs.Add(log);
            cachedDebugRich = null;
        }

        // Markdown→RichText cache (invalidated by setting to null)
        public string cachedRichText;
        public string cachedThinkingRich;

        // Asset search: "  1. [Material] Assets/path/to/file.mat"
        private static readonly Regex AssetPattern =
            new Regex(@"^\s+\d+\.\s+\[(\w+)\]\s+(Assets/.+)$", RegexOptions.Multiline);

        // ListRootObjects: "- ObjectName | Active:"
        private static readonly Regex RootObjectPattern =
            new Regex(@"^- (.+?) \| Active:", RegexOptions.Multiline);

        // ListChildren: "  0: ChildName | Active:"
        private static readonly Regex ChildObjectPattern =
            new Regex(@"^\s+\d+: (.+?) \| Active:", RegexOptions.Multiline);

        // FindObjectsByComponent: "- Root/Path/Object (Active)" or "- Root/Path/Object (Inactive)"
        private static readonly Regex ComponentSearchPattern =
            new Regex(@"^- (.+?) \((Active|Inactive)\)$", RegexOptions.Multiline);

        // Fallback: generic Assets/ path detection
        private static readonly Regex AssetPathPattern =
            new Regex(@"(Assets/\S+\.(?:png|jpg|mat|prefab|asset|anim|controller|fbx|mesh))",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

        public static ChatEntry CreateUser(string text)
        {
            return new ChatEntry { type = EntryType.User, text = text, timestamp = DateTime.Now };
        }

        public static ChatEntry CreateAgent(string text)
        {
            return new ChatEntry { type = EntryType.Agent, text = text, timestamp = DateTime.Now };
        }

        public static ChatEntry CreateInfo(string text)
        {
            var entry = new ChatEntry { type = EntryType.Info, text = text, timestamp = DateTime.Now };

            if (text.StartsWith("[Tool Result] "))
            {
                var toolOutput = text.Substring("[Tool Result] ".Length);
                entry.results = ParseResults(toolOutput);
            }

            return entry;
        }

        public static ChatEntry CreateError(string text)
        {
            return new ChatEntry { type = EntryType.Error, text = text, timestamp = DateTime.Now };
        }

        public static ChatEntry CreateChoice(string question, string[] options, string importance)
        {
            return new ChatEntry
            {
                type = EntryType.Choice,
                text = question,
                choiceOptions = options,
                choiceImportance = importance ?? "info",
                choiceSelectedIndex = -1,
                timestamp = DateTime.Now
            };
        }

        public static ChatEntry CreateToolConfirm(string question, string[] options, string importance)
        {
            var entry = new ChatEntry
            {
                type = EntryType.Choice,
                text = question,
                choiceOptions = options,
                choiceImportance = importance ?? "warning",
                choiceSelectedIndex = -1,
                isToolConfirm = true,
                timestamp = DateTime.Now
            };
            return entry;
        }

        public static ChatEntry CreateClipboard()
        {
            return new ChatEntry
            {
                type = EntryType.Choice,
                text = M("プロンプトをクリップボードにコピーしました"),
                choiceImportance = "info",
                choiceSelectedIndex = -1,
                isClipboard = true,
                timestamp = DateTime.Now
            };
        }

        public static ChatEntry CreateBatchToolConfirm(List<BatchToolItem> items)
        {
            return new ChatEntry
            {
                type = EntryType.Choice,
                text = string.Format(M("{0} 件のツールを一括実行します"), items.Count),
                choiceImportance = "warning",
                choiceSelectedIndex = -1,
                isBatchToolConfirm = true,
                batchItems = items,
                timestamp = DateTime.Now
            };
        }

        private static List<ResultItem> ParseResults(string text)
        {
            var items = new List<ResultItem>();

            // Try asset pattern
            foreach (Match m in AssetPattern.Matches(text))
            {
                items.Add(new ResultItem
                {
                    typeName = m.Groups[1].Value,
                    reference = m.Groups[2].Value.Trim(),
                    displayName = System.IO.Path.GetFileName(m.Groups[2].Value.Trim()),
                    isAsset = true
                });
            }
            if (items.Count > 0) return items;

            // Try component search pattern (before root/child patterns since it also starts with "- ")
            foreach (Match m in ComponentSearchPattern.Matches(text))
            {
                var path = m.Groups[1].Value;
                items.Add(new ResultItem
                {
                    displayName = path,
                    typeName = "",
                    reference = path,
                    isAsset = false
                });
            }
            if (items.Count > 0) return items;

            // Try root objects pattern
            foreach (Match m in RootObjectPattern.Matches(text))
            {
                items.Add(new ResultItem
                {
                    displayName = m.Groups[1].Value,
                    typeName = "GameObject",
                    reference = m.Groups[1].Value,
                    isAsset = false
                });
            }
            if (items.Count > 0) return items;

            // Try children pattern
            foreach (Match m in ChildObjectPattern.Matches(text))
            {
                items.Add(new ResultItem
                {
                    displayName = m.Groups[1].Value,
                    typeName = "GameObject",
                    reference = m.Groups[1].Value,
                    isAsset = false
                });
            }
            if (items.Count > 0) return items;

            // Fallback: general Assets/ path detection
            foreach (Match m in AssetPathPattern.Matches(text))
            {
                string path = m.Groups[1].Value;
                string ext = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
                items.Add(new ResultItem
                {
                    typeName = ext == "PNG" || ext == "JPG" ? "Texture2D" : ext,
                    reference = path,
                    displayName = System.IO.Path.GetFileName(path),
                    isAsset = true
                });
            }

            return items.Count > 0 ? items : null;
        }
    }
}

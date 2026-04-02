using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// General-purpose BlendShape tools for reading, writing, and animating blend shapes on any SkinnedMeshRenderer.
    /// For expression-specific workflows (search → preview → capture → register), use FaceEmo tools instead.
    /// </summary>
    public static class BlendShapeTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // ═══════════════════════════════════════════
        //  Expression BlendShape Synonym Groups
        //  同じ概念の別名をグループ化。検索時に自動展開される。
        // ═══════════════════════════════════════════
        private static readonly string[][] SynonymGroups = new[]
        {
            // 感情プリセット — 命名規則が異なるアバター間で同じ表情を検索するため
            new[] { "smile", "joy", "happy", "fun", "cheerful", "笑" },
            new[] { "angry", "anger", "irritated", "怒" },
            new[] { "surprised", "surprise", "astonished", "驚", "びっくり" },
            new[] { "sad", "sorrow", "unhappy", "悲" },
            new[] { "cry", "tear", "crying", "泣" },
            new[] { "shy", "embarrass", "blush", "照" },
            new[] { "wink", "ウインク", "ウィンク" },
            new[] { "sleep", "sleeping", "drowsy", "眠", "寝" },
            new[] { "kiss", "chu", "キス" },
            new[] { "tongue", "bero", "べろ", "舌" },
            // 顔パーツ
            new[] { "brow", "eyebrow", "mayu", "眉" },
            new[] { "cheek", "ほお", "頬" },
            new[] { "lip", "くちびる", "唇" },
            // 目の状態
            new[] { "narrow", "squint", "half", "jito", "じと", "細" },
        };

        /// <summary>
        /// 検索キーワードを同義語グループで展開する。
        /// 例: "smile" → ["smile", "joy", "happy", "fun", "cheerful", "笑"]
        /// マッチするグループがなければ元のキーワードのみ返す。
        /// </summary>
        internal static string[] ExpandSynonyms(string filter)
        {
            if (string.IsNullOrEmpty(filter)) return new[] { filter };
            string lower = filter.ToLower();
            foreach (var group in SynonymGroups)
            {
                for (int i = 0; i < group.Length; i++)
                {
                    if (group[i] == lower)
                        return group;
                }
            }
            return new[] { filter };
        }

        /// <summary>
        /// 複数キーワード (OR) でBlendShapeを検索する。エイリアス展開済みのフィルタ配列を受け取る。
        /// </summary>
        internal static string SearchBlendShapesMulti(string gameObjectName, string originalFilter, string[] expandedFilters)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return $"Error: No SkinnedMeshRenderer on '{gameObjectName}'.";

            var mesh = smr.sharedMesh;
            if (mesh == null) return "Error: No mesh assigned.";

            int count = mesh.blendShapeCount;
            if (count == 0) return $"'{gameObjectName}' has no blend shapes.";

            var lowerFilters = expandedFilters.Select(f => f.ToLower()).ToArray();
            bool wasExpanded = expandedFilters.Length > 1;

            var sb = new StringBuilder();
            sb.AppendLine($"Blend shapes on '{gameObjectName}' (total: {count}):");
            if (wasExpanded)
                sb.AppendLine($"  (synonym expansion: '{originalFilter}' → {string.Join(", ", expandedFilters)})");

            int shown = 0;
            for (int i = 0; i < count; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                string nameLower = name.ToLower();
                bool match = false;
                foreach (var f in lowerFilters)
                {
                    if (nameLower.Contains(f)) { match = true; break; }
                }
                if (!match) continue;

                float weight = smr.GetBlendShapeWeight(i);
                string weightStr = weight > 0.01f ? $" = {weight:F1}" : "";
                sb.AppendLine($"  [{i}] {name}{weightStr}");
                shown++;
            }

            if (shown == 0)
            {
                string hint = wasExpanded
                    ? $" (synonyms tried: {string.Join(", ", expandedFilters)})"
                    : "";
                return $"No blend shapes matching '{originalFilter}' on '{gameObjectName}'.{hint} Total shapes: {count}. Try a broader keyword like 'eye', 'mouth', or 'all'.";
            }

            sb.AppendLine($"  ({shown}/{count} shown, filtered by '{originalFilter}')");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("List blend shapes on a SkinnedMeshRenderer with optional keyword filter. " +
            "General-purpose tool for any blend shape inspection (clothing, body shape, etc.). " +
            "For expression creation workflow, use SearchExpressionShapes instead.")]
        public static string ListBlendShapesEx(string gameObjectName, string filter = "")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return $"Error: No SkinnedMeshRenderer on '{gameObjectName}'.";

            var mesh = smr.sharedMesh;
            if (mesh == null) return "Error: No mesh assigned.";

            int count = mesh.blendShapeCount;
            if (count == 0) return $"'{gameObjectName}' has no blend shapes.";

            string filterLower = string.IsNullOrEmpty(filter) ? "" : filter.ToLower();

            var sb = new StringBuilder();
            sb.AppendLine($"Blend shapes on '{gameObjectName}' (total: {count}):");

            int shown = 0;
            for (int i = 0; i < count; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (!string.IsNullOrEmpty(filterLower) && !name.ToLower().Contains(filterLower))
                    continue;

                float weight = smr.GetBlendShapeWeight(i);
                string weightStr = weight > 0.01f ? $" = {weight:F1}" : "";
                sb.AppendLine($"  [{i}] {name}{weightStr}");
                shown++;
            }

            if (shown == 0)
                return $"No blend shapes matching '{filter}' on '{gameObjectName}'. Total shapes: {count}";

            if (!string.IsNullOrEmpty(filterLower))
                sb.AppendLine($"  ({shown}/{count} shown, filtered by '{filter}')");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Set multiple blend shapes at once. Format: 'shapeName=value;shapeName2=value2' (values 0-100). " +
            "General-purpose tool for any blend shape editing. " +
            "For expression preview workflow, use SetExpressionPreview instead.")]
        public static string SetMultipleBlendShapes(string gameObjectName, string blendShapeData)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return $"Error: No SkinnedMeshRenderer on '{gameObjectName}'.";

            var mesh = smr.sharedMesh;
            if (mesh == null) return "Error: No mesh assigned.";

            if (string.IsNullOrEmpty(blendShapeData))
                return "Error: blendShapeData is required. Format: 'shapeName=value;shapeName2=value2'";

            Undo.RecordObject(smr, "Set Multiple BlendShapes");

            var results = new List<string>();
            var errors = new List<string>();

            foreach (string entry in blendShapeData.Split(';'))
            {
                string trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx < 0) { errors.Add($"Invalid format: '{trimmed}'"); continue; }

                string shapeName = trimmed.Substring(0, eqIdx).Trim();
                string valueStr = trimmed.Substring(eqIdx + 1).Trim();

                if (!float.TryParse(valueStr, out float weight))
                {
                    errors.Add($"Invalid value for '{shapeName}': '{valueStr}'");
                    continue;
                }

                int index = mesh.GetBlendShapeIndex(shapeName);
                if (index < 0)
                {
                    // Try partial match
                    index = FindBlendShapePartial(mesh, shapeName);
                    if (index < 0) { errors.Add($"BlendShape not found: '{shapeName}'"); continue; }
                    shapeName = mesh.GetBlendShapeName(index);
                }

                smr.SetBlendShapeWeight(index, weight);
                results.Add($"{shapeName}={weight:F0}");
            }

            var sb = new StringBuilder();
            if (results.Count > 0)
                sb.AppendLine($"Success: Set {results.Count} blend shapes: {string.Join(", ", results)}");
            if (errors.Count > 0)
                sb.AppendLine($"Errors: {string.Join("; ", errors)}");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Reset all blend shapes on a SkinnedMeshRenderer to 0. " +
            "For expression workflow cleanup, use ResetExpressionPreview instead.")]
        public static string ResetBlendShapes(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return $"Error: No SkinnedMeshRenderer on '{gameObjectName}'.";

            var mesh = smr.sharedMesh;
            if (mesh == null) return "Error: No mesh assigned.";

            Undo.RecordObject(smr, "Reset BlendShapes");

            int count = mesh.blendShapeCount;
            for (int i = 0; i < count; i++)
                smr.SetBlendShapeWeight(i, 0f);

            SceneView.RepaintAll();
            return $"Success: Reset all {count} blend shapes to 0 on '{gameObjectName}'.";
        }

        [AgentTool("Get current non-zero blend shape values on a SkinnedMeshRenderer. " +
            "For expression state capture, use GetCurrentExpressionValues instead.")]
        public static string GetActiveBlendShapes(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return $"Error: No SkinnedMeshRenderer on '{gameObjectName}'.";

            var mesh = smr.sharedMesh;
            if (mesh == null) return "Error: No mesh assigned.";

            var sb = new StringBuilder();
            sb.AppendLine($"Active blend shapes on '{gameObjectName}':");

            int active = 0;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                float weight = smr.GetBlendShapeWeight(i);
                if (weight > 0.01f)
                {
                    sb.AppendLine($"  {mesh.GetBlendShapeName(i)} = {weight:F1}");
                    active++;
                }
            }

            if (active == 0)
                return $"No active blend shapes on '{gameObjectName}' (all at 0).";

            sb.AppendLine($"  ({active} active shapes)");
            return sb.ToString().TrimEnd();
        }

        // ─── Animation Clip Tools ───

        [AgentTool("Create animation clip from current non-zero blend shape values on a SkinnedMeshRenderer. " +
            "General-purpose clip creation for any blend shape animation. " +
            "For expression creation + FaceEmo registration, use CreateAndRegisterExpression instead.")]
        public static string CreateExpressionClip(string gameObjectName, string animPath, string meshPath = "")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return $"Error: No SkinnedMeshRenderer on '{gameObjectName}'.";

            var mesh = smr.sharedMesh;
            if (mesh == null) return "Error: No mesh assigned.";

            // Determine relative path for animation binding
            if (string.IsNullOrEmpty(meshPath))
                meshPath = GetRelativePath(go);

            // Collect non-zero blend shapes
            var entries = new List<(string name, float value)>();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                float weight = smr.GetBlendShapeWeight(i);
                if (weight > 0.01f)
                    entries.Add((mesh.GetBlendShapeName(i), weight));
            }

            if (entries.Count == 0)
                return "Error: No active blend shapes to save. Set weights first with SetMultipleBlendShapes(gameObjectName, 'shape1=80;shape2=50'), then call CreateExpressionClip.";

            // Create clip
            var clip = new AnimationClip();
            clip.name = System.IO.Path.GetFileNameWithoutExtension(animPath);

            foreach (var (name, value) in entries)
            {
                var binding = new EditorCurveBinding
                {
                    path = meshPath,
                    type = typeof(SkinnedMeshRenderer),
                    propertyName = $"blendShape.{name}"
                };
                var curve = new AnimationCurve(new Keyframe(0f, value));
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            // Ensure directory exists
            string dir = System.IO.Path.GetDirectoryName(animPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(
                System.IO.Path.Combine(Application.dataPath, "..", dir)))
            {
                System.IO.Directory.CreateDirectory(
                    System.IO.Path.Combine(Application.dataPath, "..", dir));
            }

            AssetDatabase.CreateAsset(clip, animPath);
            AssetDatabase.SaveAssets();

            return $"Success: Created animation clip '{animPath}' with {entries.Count} blend shape curves.";
        }

        [AgentTool("Create animation clip from explicit blend shape data. Format: 'shapeName=value;shapeName2=value2'. " +
            "General-purpose clip creation. " +
            "For expression creation + FaceEmo registration, use CreateExpressionFromData instead.")]
        public static string CreateExpressionClipFromData(string animPath, string meshPath, string blendShapeData)
        {
            if (string.IsNullOrEmpty(animPath)) return "Error: animPath is required.";
            if (string.IsNullOrEmpty(meshPath)) return "Error: meshPath is required.";
            if (string.IsNullOrEmpty(blendShapeData)) return "Error: blendShapeData is required.";

            var entries = ParseBlendShapeData(blendShapeData);
            if (entries.Count == 0)
                return "Error: No valid blend shape entries in data.";

            var clip = new AnimationClip();
            clip.name = System.IO.Path.GetFileNameWithoutExtension(animPath);

            foreach (var (name, value) in entries)
            {
                var binding = new EditorCurveBinding
                {
                    path = meshPath,
                    type = typeof(SkinnedMeshRenderer),
                    propertyName = $"blendShape.{name}"
                };
                var curve = new AnimationCurve(new Keyframe(0f, value));
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            string dir = System.IO.Path.GetDirectoryName(animPath);
            if (!string.IsNullOrEmpty(dir))
            {
                string fullDir = System.IO.Path.Combine(Application.dataPath, "..", dir);
                if (!System.IO.Directory.Exists(fullDir))
                    System.IO.Directory.CreateDirectory(fullDir);
            }

            AssetDatabase.CreateAsset(clip, animPath);
            AssetDatabase.SaveAssets();

            return $"Success: Created '{animPath}' with {entries.Count} blend shape curves: {string.Join(", ", entries.Select(e => $"{e.name}={e.value:F0}"))}";
        }

        [AgentTool("Inspect an animation clip's blend shape curves. Shows all BlendShape keyframes with their values.")]
        public static string InspectExpressionClip(string animPath)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animPath);
            if (clip == null) return $"Error: Animation clip not found at '{animPath}'.";

            var bindings = AnimationUtility.GetCurveBindings(clip);
            var blendShapeBindings = bindings
                .Where(b => b.propertyName.StartsWith("blendShape."))
                .ToList();

            if (blendShapeBindings.Count == 0)
                return $"Animation clip '{animPath}' has no blend shape curves. ({bindings.Length} total curves)";

            var sb = new StringBuilder();
            sb.AppendLine($"Expression clip: {animPath}");
            sb.AppendLine($"  Total curves: {bindings.Length}, BlendShape curves: {blendShapeBindings.Count}");
            sb.AppendLine($"  Duration: {clip.length}s, Loop: {clip.isLooping}");
            sb.AppendLine();

            foreach (var binding in blendShapeBindings.OrderBy(b => b.propertyName))
            {
                string shapeName = binding.propertyName.Substring("blendShape.".Length);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                float value = curve.Evaluate(0f);
                sb.AppendLine($"  {shapeName} = {value:F1}  (path: {binding.path})");
            }

            // Also list non-blendshape bindings
            var otherBindings = bindings.Where(b => !b.propertyName.StartsWith("blendShape.")).ToList();
            if (otherBindings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"  Other curves ({otherBindings.Count}):");
                foreach (var b in otherBindings.Take(20))
                    sb.AppendLine($"    {b.path}/{b.propertyName}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Edit a blend shape value in an existing animation clip. Adds or updates the curve for the specified blend shape.")]
        public static string EditExpressionClip(string animPath, string meshPath, string blendShapeName, float value)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animPath);
            if (clip == null) return $"Error: Animation clip not found at '{animPath}'.";

            Undo.RecordObject(clip, "Edit Expression Clip");

            var binding = new EditorCurveBinding
            {
                path = meshPath,
                type = typeof(SkinnedMeshRenderer),
                propertyName = $"blendShape.{blendShapeName}"
            };

            if (value < 0.01f)
            {
                // Remove curve
                AnimationUtility.SetEditorCurve(clip, binding, null);
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssets();
                return $"Success: Removed blend shape '{blendShapeName}' from '{animPath}'.";
            }

            var curve = new AnimationCurve(new Keyframe(0f, value));
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            return $"Success: Set '{blendShapeName}' = {value} in '{animPath}'.";
        }

        [AgentTool("Preview an animation clip's blend shapes on a SkinnedMeshRenderer. Applies the clip's blend shape values to the mesh for visual preview without entering Play mode.")]
        public static string PreviewExpressionClip(string gameObjectName, string animPath)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return $"Error: No SkinnedMeshRenderer on '{gameObjectName}'.";

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(animPath);
            if (clip == null) return $"Error: Animation clip not found at '{animPath}'.";

            var mesh = smr.sharedMesh;
            if (mesh == null) return "Error: No mesh assigned.";

            Undo.RecordObject(smr, "Preview Expression");

            // Reset all blend shapes first
            for (int i = 0; i < mesh.blendShapeCount; i++)
                smr.SetBlendShapeWeight(i, 0f);

            // Apply clip's blend shape curves
            var bindings = AnimationUtility.GetCurveBindings(clip);
            int applied = 0;

            foreach (var binding in bindings)
            {
                if (!binding.propertyName.StartsWith("blendShape.")) continue;

                string shapeName = binding.propertyName.Substring("blendShape.".Length);
                int index = mesh.GetBlendShapeIndex(shapeName);
                if (index < 0) continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                float value = curve.Evaluate(0f);
                smr.SetBlendShapeWeight(index, value);
                applied++;
            }

            SceneView.RepaintAll();
            return $"Success: Applied {applied} blend shapes from '{animPath}' to '{gameObjectName}'.";
        }

        // ─── Camera Focus Tools ───

        /// <summary>Focus Scene camera on avatar face. Called internally by FaceEmo CaptureExpressionPreview.</summary>
        public static string FocusOnFace(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            // Find head bone
            Transform head = FindBone(go.transform, "Head");
            if (head == null)
                head = FindBone(go.transform, "head");
            if (head == null)
            {
                // Try Animator
                var animator = go.GetComponent<Animator>();
                if (animator != null)
                    head = animator.GetBoneTransform(HumanBodyBones.Head);
            }

            if (head == null)
                return $"Error: Could not find Head bone on '{avatarRootName}'.";

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return "Error: No active SceneView.";

            // Frame the head with close zoom
            Vector3 headPos = head.position;
            float distance = 0.4f; // Close-up for face

            sceneView.LookAt(headPos, sceneView.rotation, distance);
            sceneView.Repaint();

            return $"Success: Scene camera focused on face (Head bone at {headPos}).";
        }

        /// <summary>Capture face close-up from Scene view. Called internally by FaceEmo CaptureExpressionPreview.</summary>
        public static string CaptureExpressionPreview(string avatarRootName, int width = 512, int height = 512)
        {
            // Focus on face first
            string focusResult = FocusOnFace(avatarRootName);
            if (focusResult.StartsWith("Error"))
            {
                // Even if focus fails, try to capture anyway
                Debug.LogWarning($"[BlendShapeTools] {focusResult}");
            }

            // Small delay for Scene to update
            SceneView.RepaintAll();

            // Capture
            return SceneViewTools.CaptureSceneView(width, height);
        }

        // ─── Helpers ───

        private static int FindBlendShapePartial(Mesh mesh, string query)
        {
            string queryLower = query.ToLower();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (mesh.GetBlendShapeName(i).ToLower().Contains(queryLower))
                    return i;
            }
            return -1;
        }

        private static string GetRelativePath(GameObject go)
        {
            // Get path relative to the nearest avatar root (or scene root)
            var parts = new List<string>();
            Transform current = go.transform;

            while (current.parent != null)
            {
                // Check if parent has VRCAvatarDescriptor
                if (current.parent.GetComponent("VRCAvatarDescriptor") != null ||
                    current.parent.GetComponent<Animator>() != null)
                {
                    parts.Insert(0, current.name);
                    return string.Join("/", parts);
                }
                parts.Insert(0, current.name);
                current = current.parent;
            }

            return go.name;
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            if (root.name.Equals(boneName, StringComparison.OrdinalIgnoreCase))
                return root;

            foreach (Transform child in root)
            {
                var found = FindBone(child, boneName);
                if (found != null) return found;
            }
            return null;
        }

        private static List<(string name, float value)> ParseBlendShapeData(string data)
        {
            var result = new List<(string name, float value)>();
            if (string.IsNullOrEmpty(data)) return result;

            foreach (string entry in data.Split(';'))
            {
                string trimmed = entry.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx < 0) continue;

                string name = trimmed.Substring(0, eqIdx).Trim();
                string valStr = trimmed.Substring(eqIdx + 1).Trim();

                if (float.TryParse(valStr, out float val))
                    result.Add((name, val));
            }
            return result;
        }
    }
}

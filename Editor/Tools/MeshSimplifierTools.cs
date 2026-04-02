using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class MeshSimplifierTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
#if NDMF_MESH_SIMPLIFIER

        // ========== Helpers ==========

        private static Type _simplifierType;

        private static bool InitType()
        {
            if (_simplifierType != null) return true;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                _simplifierType = assembly.GetType("jp.lilxyzw.ndmfmeshsimplifier.runtime.NDMFMeshSimplifier");
                if (_simplifierType != null) return true;
            }
            return false;
        }

        private static GameObject FindAvatarRoot(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            return go;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ========== 1. AddMeshSimplifier ==========

        [AgentTool("Add NDMF Mesh Simplifier to a renderer for build-time polygon reduction. quality: 0-1 (0.5=50% triangles). Processes at build time via NDMF.")]
        public static string AddMeshSimplifier(string targetMeshName, float quality = 0.5f)
        {
            if (!InitType())
                return "Error: NDMF Mesh Simplifier type not found. Is the package installed?";

            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: GameObject '{targetMeshName}' not found.";

            var renderer = targetObj.GetComponent<Renderer>();
            if (renderer == null)
                return $"Error: '{targetMeshName}' does not have a Renderer component.";

            var existing = targetObj.GetComponent(_simplifierType);
            if (existing != null)
                return $"Error: '{targetMeshName}' already has a MeshSimplifier component. Use ConfigureMeshSimplifier to change settings.";

            if (quality < 0f || quality > 1f)
                return $"Error: quality must be between 0 and 1. Got {quality}.";

            if (!AgentSettings.RequestConfirmation(
                "NDMF Mesh Simplifier の追加",
                $"対象: {targetMeshName}\n" +
                $"品質: {quality:F2} ({quality * 100:F0}% のポリゴンを維持)"))
                return "Cancelled: User denied the operation.";

            var comp = Undo.AddComponent(targetObj, _simplifierType);
            var so = new SerializedObject(comp);
            var qualityProp = so.FindProperty("quality");
            if (qualityProp != null)
                qualityProp.floatValue = quality;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);

            return $"Success: Added MeshSimplifier to '{targetMeshName}'.\n" +
                   $"  Quality: {quality:F2} ({quality * 100:F0}% triangles retained)";
        }

        // ========== 2. ConfigureMeshSimplifier ==========

        [AgentTool("Configure mesh simplifier quality and options. Shows current values when called without parameters. quality: 0-1 ratio of triangles to keep. preserveBorderEdges/preserveUVSeamEdges/enableSmartLink: 'true'/'false'.")]
        public static string ConfigureMeshSimplifier(string targetMeshName, string quality = "",
            string preserveBorderEdges = "", string preserveUVSeamEdges = "",
            string enableSmartLink = "")
        {
            if (!InitType())
                return "Error: NDMF Mesh Simplifier type not found.";

            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: GameObject '{targetMeshName}' not found.";

            var comp = targetObj.GetComponent(_simplifierType);
            if (comp == null)
                return $"Error: '{targetMeshName}' does not have a MeshSimplifier component. Use AddMeshSimplifier first.";

            var so = new SerializedObject(comp);

            bool anySet = !string.IsNullOrEmpty(quality) ||
                          !string.IsNullOrEmpty(preserveBorderEdges) ||
                          !string.IsNullOrEmpty(preserveUVSeamEdges) ||
                          !string.IsNullOrEmpty(enableSmartLink);

            if (!anySet)
            {
                // Show current values
                var sb = new StringBuilder();
                sb.AppendLine($"MeshSimplifier settings on '{targetMeshName}':");
                var qProp = so.FindProperty("quality");
                if (qProp != null)
                    sb.AppendLine($"  quality: {qProp.floatValue:F3}");

                var optionsProp = so.FindProperty("options");
                if (optionsProp != null)
                {
                    var pbe = optionsProp.FindPropertyRelative("PreserveBorderEdges");
                    var puse = optionsProp.FindPropertyRelative("PreserveUVSeamEdges");
                    var pufe = optionsProp.FindPropertyRelative("PreserveUVFoldoverEdges");
                    var psc = optionsProp.FindPropertyRelative("PreserveSurfaceCurvature");
                    var esl = optionsProp.FindPropertyRelative("EnableSmartLink");
                    var mic = optionsProp.FindPropertyRelative("MaxIterationCount");
                    var agg = optionsProp.FindPropertyRelative("Agressiveness");

                    if (pbe != null) sb.AppendLine($"  PreserveBorderEdges: {pbe.boolValue}");
                    if (puse != null) sb.AppendLine($"  PreserveUVSeamEdges: {puse.boolValue}");
                    if (pufe != null) sb.AppendLine($"  PreserveUVFoldoverEdges: {pufe.boolValue}");
                    if (psc != null) sb.AppendLine($"  PreserveSurfaceCurvature: {psc.boolValue}");
                    if (esl != null) sb.AppendLine($"  EnableSmartLink: {esl.boolValue}");
                    if (mic != null) sb.AppendLine($"  MaxIterationCount: {mic.intValue}");
                    if (agg != null) sb.AppendLine($"  Agressiveness: {agg.doubleValue:F1}");
                }
                return sb.ToString().TrimEnd();
            }

            // Apply changes
            Undo.RecordObject(comp, "Configure MeshSimplifier");
            var changes = new List<string>();

            if (!string.IsNullOrEmpty(quality))
            {
                if (float.TryParse(quality, out float qVal))
                {
                    qVal = Mathf.Clamp01(qVal);
                    var qProp = so.FindProperty("quality");
                    if (qProp != null)
                    {
                        qProp.floatValue = qVal;
                        changes.Add($"  quality: {qVal:F3}");
                    }
                }
            }

            var optProp = so.FindProperty("options");
            if (optProp != null)
            {
                void SetOptionBool(string propName, string value)
                {
                    if (string.IsNullOrEmpty(value)) return;
                    bool bVal = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    var p = optProp.FindPropertyRelative(propName);
                    if (p != null)
                    {
                        p.boolValue = bVal;
                        changes.Add($"  {propName}: {bVal}");
                    }
                }

                SetOptionBool("PreserveBorderEdges", preserveBorderEdges);
                SetOptionBool("PreserveUVSeamEdges", preserveUVSeamEdges);
                SetOptionBool("EnableSmartLink", enableSmartLink);
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);

            return $"Success: Updated MeshSimplifier on '{targetMeshName}':\n{string.Join("\n", changes)}";
        }

        // ========== 3. ListMeshSimplifiers ==========

        [AgentTool("List all NDMF Mesh Simplifier components on an avatar and its children.")]
        public static string ListMeshSimplifiers(string avatarRootName)
        {
            if (!InitType())
                return "Error: NDMF Mesh Simplifier type not found.";

            var avatarRoot = FindAvatarRoot(avatarRootName);
            if (avatarRoot == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var components = avatarRoot.GetComponentsInChildren(_simplifierType, true);
            if (components == null || components.Length == 0)
                return $"No MeshSimplifier components found on '{avatarRootName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"MeshSimplifier components on '{avatarRootName}' ({components.Length}):");

            foreach (var comp in components)
            {
                var path = GetRelativePath(avatarRoot.transform, comp.transform);
                var so = new SerializedObject(comp);
                var qProp = so.FindProperty("quality");
                float q = qProp != null ? qProp.floatValue : -1f;

                sb.AppendLine($"  [{path}]");
                if (q >= 0f)
                    sb.AppendLine($"    Quality: {q:F3} ({q * 100:F0}% triangles)");

                var renderer = comp.GetComponent<Renderer>();
                if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                    sb.AppendLine($"    Mesh: {smr.sharedMesh.name} ({smr.sharedMesh.triangles.Length / 3} tris)");
                else if (renderer is MeshRenderer mr)
                {
                    var mf = comp.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                        sb.AppendLine($"    Mesh: {mf.sharedMesh.name} ({mf.sharedMesh.triangles.Length / 3} tris)");
                }
            }

            return sb.ToString().TrimEnd();
        }

        // ========== 4. RemoveMeshSimplifier ==========

        [AgentTool("Remove a mesh simplifier component from a renderer.")]
        public static string RemoveMeshSimplifier(string targetMeshName)
        {
            if (!InitType())
                return "Error: NDMF Mesh Simplifier type not found.";

            var targetObj = FindGO(targetMeshName);
            if (targetObj == null)
                return $"Error: GameObject '{targetMeshName}' not found.";

            var comp = targetObj.GetComponent(_simplifierType);
            if (comp == null)
                return $"Error: '{targetMeshName}' does not have a MeshSimplifier component.";

            if (!AgentSettings.RequestConfirmation(
                "Mesh Simplifier の削除",
                $"対象: {targetMeshName}\nMeshSimplifierコンポーネントを削除します。"))
                return "Cancelled: User denied the operation.";

            Undo.DestroyObjectImmediate(comp);
            EditorUtility.SetDirty(targetObj);

            return $"Success: Removed MeshSimplifier from '{targetMeshName}'.";
        }

#endif
    }
}

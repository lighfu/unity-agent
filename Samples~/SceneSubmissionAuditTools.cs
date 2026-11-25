using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/// <summary>Copy this sample into the project's Editor folder and enable it in UnityAgent.</summary>
public static class SceneSubmissionAuditTools
{
    [Serializable]
    private sealed class Finding
    {
        public string severity, rule, target, message;
    }

    [Serializable]
    private sealed class Report
    {
        public string scenePath;
        public int errors, warnings, assetPaths, assetsWithoutMeasuredSize;
        public long sourceAssetBytes;
        public int activeRenderers, activeMaterialSlots;
        public long activeMeshTriangles;
        public bool findingsTruncated;
        public Finding[] findings;
    }

    [AgentTool("Audit a loaded, saved scene for missing scripts, missing meshes and empty material slots. " +
               "Reports source asset bytes and active renderer, material-slot and mesh-triangle counts. " +
               "Provide the scene's full Assets/ path and run in Edit Mode.",
        Category = "Scene Audit", Risk = ToolRisk.Safe)]
    public static string InspectSceneSubmission(string scenePath, int maxFindings = 60)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Error: Stop Play Mode before auditing a scene.";
        if (string.IsNullOrWhiteSpace(scenePath) || !scenePath.StartsWith("Assets/", StringComparison.Ordinal) ||
            !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            return "Error: scenePath must be the full project path of a scene under Assets/.";
        if (maxFindings < 1 || maxFindings > 200) return "Error: maxFindings must be between 1 and 200.";

        var scene = SceneManager.GetSceneByPath(scenePath);
        if (!scene.IsValid() || !scene.isLoaded) return $"Error: Open scene '{scenePath}' before auditing it.";
        if (scene.isDirty) return $"Error: Save scene '{scene.path}' so the audited hierarchy and source dependencies agree.";

        var report = new Report { scenePath = scene.path };
        var findings = new List<Finding>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                if (count > 0)
                    AddFinding(findings, "error", "missing_script", transform, $"{count} missing script reference(s).");
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                report.activeRenderers++;
                var materials = renderer.sharedMaterials;
                report.activeMaterialSlots += materials.Length;
                int emptySlots = materials.Count(material => material == null);
                if (emptySlots > 0)
                    AddFinding(findings, "warning", "empty_material_slot", renderer.transform, $"{emptySlots} empty material slot(s).");

                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                {
                    AddFinding(findings, "warning", "missing_mesh", renderer.transform, "The active mesh renderer has no mesh.");
                    continue;
                }
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    if (mesh.GetTopology(submesh) == MeshTopology.Triangles)
                        report.activeMeshTriangles += mesh.GetIndexCount(submesh) / 3L;
            }
        }

        foreach (string path in AssetDatabase.GetDependencies(scene.path, true).Distinct(StringComparer.Ordinal))
        {
            if (AssetDatabase.IsValidFolder(path)) continue;
            report.assetPaths++;
            if (TryGetFileSize(path, out long size)) report.sourceAssetBytes += size;
            else report.assetsWithoutMeasuredSize++;
        }

        report.errors = findings.Count(finding => finding.severity == "error");
        report.warnings = findings.Count - report.errors;
        report.findingsTruncated = findings.Count > maxFindings;
        report.findings = findings.OrderBy(finding => finding.severity).Take(maxFindings).ToArray();
        return JsonUtility.ToJson(report, true);
    }

    private static void AddFinding(List<Finding> findings, string severity, string rule, Transform target, string message)
    {
        var names = new Stack<string>();
        for (var node = target; node != null; node = node.parent) names.Push(node.name);
        findings.Add(new Finding { severity = severity, rule = rule, target = "/" + string.Join("/", names), message = message });
    }

    private static bool TryGetFileSize(string assetPath, out long size)
    {
        size = 0;
        string filePath;
        if (assetPath.StartsWith("Assets/", StringComparison.Ordinal))
        {
            filePath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
        }
        else
        {
            var package = PackageInfo.FindForAssetPath(assetPath);
            if (package == null || !assetPath.StartsWith(package.assetPath + "/", StringComparison.Ordinal)) return false;
            filePath = Path.Combine(package.resolvedPath, assetPath.Substring(package.assetPath.Length + 1));
        }

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists) return false;
            size = info.Length;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

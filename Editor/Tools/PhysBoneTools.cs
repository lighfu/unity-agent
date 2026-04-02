using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class PhysBoneTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        private const string PhysBoneTypeName = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone";
        private const string PhysBoneColliderTypeName = "VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider";

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        private static Vector3? ParseVector3(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            if (parts.Length != 3) return null;
            if (float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y) &&
                float.TryParse(parts[2].Trim(), out float z))
                return new Vector3(x, y, z);
            return null;
        }

        // ================================================================
        // PhysBone Add / Remove
        // ================================================================

        [AgentTool("Add a VRCPhysBone component to a GameObject. rootTransformName: optional root bone name (defaults to self). Use ConfigurePhysBone or ApplyPhysBoneTemplate after adding.")]
        public static string AddPhysBone(string goName, string rootTransformName = "")
        {
            var pbType = FindType(PhysBoneTypeName);
            if (pbType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            if (go.GetComponent(pbType) != null)
                return $"Error: '{goName}' already has a VRCPhysBone component.";

            var pb = Undo.AddComponent(go, pbType);

            if (!string.IsNullOrEmpty(rootTransformName))
            {
                var rootGo = FindGO(rootTransformName);
                if (rootGo == null)
                    return $"Warning: VRCPhysBone added but rootTransform '{rootTransformName}' not found. Set it manually.";

                var so = new SerializedObject(pb);
                var rootProp = so.FindProperty("rootTransform");
                if (rootProp != null)
                {
                    rootProp.objectReferenceValue = rootGo.transform;
                    so.ApplyModifiedProperties();
                }
            }

            return $"Success: Added VRCPhysBone to '{goName}'." +
                   (string.IsNullOrEmpty(rootTransformName) ? " RootTransform: (self)." : $" RootTransform: '{rootTransformName}'.");
        }

        [AgentTool("Remove a VRCPhysBone component from a GameObject. Requires user confirmation.")]
        public static string RemovePhysBone(string goName)
        {
            var pbType = FindType(PhysBoneTypeName);
            if (pbType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var pb = go.GetComponent(pbType);
            if (pb == null) return $"Error: No VRCPhysBone found on '{goName}'.";

            if (!AgentSettings.RequestConfirmation(
                "PhysBone 削除",
                $"'{goName}' の VRCPhysBone を削除します。"))
                return "Cancelled: User denied the operation.";

            Undo.DestroyObjectImmediate(pb);
            return $"Success: Removed VRCPhysBone from '{goName}'.";
        }

        // ================================================================
        // PhysBone Collider
        // ================================================================

        [AgentTool("Add a VRCPhysBoneCollider to a GameObject. shapeType: 0=Sphere, 1=Capsule, 2=Plane. position format: 'x,y,z'. rotation format: 'x,y,z' (euler degrees).")]
        public static string AddPhysBoneCollider(string goName, int shapeType = 0, float radius = 0.05f, float height = 0f, string position = "", string rotation = "")
        {
            var colliderType = FindType(PhysBoneColliderTypeName);
            if (colliderType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var col = Undo.AddComponent(go, colliderType);
            var so = new SerializedObject(col);

            var shapeProp = so.FindProperty("shapeType");
            if (shapeProp != null) shapeProp.intValue = shapeType;

            var radiusProp = so.FindProperty("radius");
            if (radiusProp != null) radiusProp.floatValue = radius;

            var heightProp = so.FindProperty("height");
            if (heightProp != null) heightProp.floatValue = height;

            var posVec = ParseVector3(position);
            if (posVec.HasValue)
            {
                var posProp = so.FindProperty("position");
                if (posProp != null) posProp.vector3Value = posVec.Value;
            }

            var rotVec = ParseVector3(rotation);
            if (rotVec.HasValue)
            {
                var rotProp = so.FindProperty("rotation");
                if (rotProp != null) rotProp.quaternionValue = Quaternion.Euler(rotVec.Value);
            }

            so.ApplyModifiedProperties();

            string[] shapeNames = { "Sphere", "Capsule", "Plane" };
            string shapeName = shapeType >= 0 && shapeType < shapeNames.Length ? shapeNames[shapeType] : shapeType.ToString();
            return $"Success: Added VRCPhysBoneCollider ({shapeName}, radius={radius:F3}) to '{goName}'.";
        }

        [AgentTool("List all VRCPhysBoneCollider components under an avatar. Shows shape, radius, height, and which PhysBones reference them.")]
        public static string ListPhysBoneColliders(string avatarRootName)
        {
            var colliderType = FindType(PhysBoneColliderTypeName);
            if (colliderType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var colliders = go.GetComponentsInChildren(colliderType, true);
            if (colliders.Length == 0)
                return $"No PhysBoneCollider found under '{avatarRootName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"PhysBoneColliders under '{avatarRootName}' ({colliders.Length}):");

            string[] shapeNames = { "Sphere", "Capsule", "Plane" };

            int limit = Math.Min(colliders.Length, 30);
            for (int i = 0; i < limit; i++)
            {
                var col = colliders[i];
                var so = new SerializedObject(col);
                string path = GetRelativePath(go.transform, col.transform);

                var shape = so.FindProperty("shapeType");
                var radius = so.FindProperty("radius");
                var height = so.FindProperty("height");

                int shapeVal = shape != null ? shape.intValue : -1;
                string shapeName = shapeVal >= 0 && shapeVal < shapeNames.Length ? shapeNames[shapeVal] : "?";

                sb.Append($"  [{i}] {path} ({shapeName}");
                if (radius != null) sb.Append($", r={radius.floatValue:F3}");
                if (height != null && height.floatValue > 0) sb.Append($", h={height.floatValue:F3}");
                sb.AppendLine(")");
            }

            if (colliders.Length > 30)
                sb.AppendLine($"  ... and {colliders.Length - 30} more.");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Inspect a VRCPhysBoneCollider in detail. Shows shapeType, radius, height, position, rotation, insideBounds, bonesAsSpheres.")]
        public static string InspectPhysBoneCollider(string goName)
        {
            var colliderType = FindType(PhysBoneColliderTypeName);
            if (colliderType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var col = go.GetComponent(colliderType);
            if (col == null) return $"Error: No VRCPhysBoneCollider on '{goName}'.";

            var so = new SerializedObject(col);
            var sb = new StringBuilder();
            sb.AppendLine($"VRCPhysBoneCollider on '{goName}':");

            string[] shapeNames = { "Sphere", "Capsule", "Plane" };
            var shape = so.FindProperty("shapeType");
            if (shape != null)
            {
                int v = shape.intValue;
                sb.AppendLine($"  ShapeType: {(v >= 0 && v < shapeNames.Length ? shapeNames[v] : v.ToString())}");
            }

            var radius = so.FindProperty("radius");
            if (radius != null) sb.AppendLine($"  Radius: {radius.floatValue:F4}");

            var height = so.FindProperty("height");
            if (height != null) sb.AppendLine($"  Height: {height.floatValue:F4}");

            var pos = so.FindProperty("position");
            if (pos != null) sb.AppendLine($"  Position: {pos.vector3Value}");

            var rot = so.FindProperty("rotation");
            if (rot != null) sb.AppendLine($"  Rotation: {rot.quaternionValue.eulerAngles}");

            var inside = so.FindProperty("insideBounds");
            if (inside != null) sb.AppendLine($"  InsideBounds: {inside.boolValue}");

            var bonesAsSpheres = so.FindProperty("bonesAsSpheres");
            if (bonesAsSpheres != null) sb.AppendLine($"  BonesAsSpheres: {bonesAsSpheres.boolValue}");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Configure a VRCPhysBoneCollider. shapeType: 0=Sphere,1=Capsule,2=Plane (-1=unchanged). position/rotation format: 'x,y,z'. Use -999 for unchanged floats.")]
        public static string ConfigurePhysBoneCollider(string goName, int shapeType = -1, float radius = -999, float height = -999, string position = "", string rotation = "", int insideBounds = -1)
        {
            var colliderType = FindType(PhysBoneColliderTypeName);
            if (colliderType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var col = go.GetComponent(colliderType);
            if (col == null) return $"Error: No VRCPhysBoneCollider on '{goName}'.";

            Undo.RecordObject(col, "Configure PhysBoneCollider");
            var so = new SerializedObject(col);
            int changed = 0;
            var sb = new StringBuilder();
            sb.AppendLine($"Configured PhysBoneCollider on '{goName}':");

            if (shapeType >= 0)
            {
                var p = so.FindProperty("shapeType");
                if (p != null) { p.intValue = shapeType; sb.AppendLine($"  shapeType: {shapeType}"); changed++; }
            }
            if (radius != -999)
            {
                var p = so.FindProperty("radius");
                if (p != null) { p.floatValue = radius; sb.AppendLine($"  radius: {radius:F4}"); changed++; }
            }
            if (height != -999)
            {
                var p = so.FindProperty("height");
                if (p != null) { p.floatValue = height; sb.AppendLine($"  height: {height:F4}"); changed++; }
            }

            var posVec = ParseVector3(position);
            if (posVec.HasValue)
            {
                var p = so.FindProperty("position");
                if (p != null) { p.vector3Value = posVec.Value; sb.AppendLine($"  position: {posVec.Value}"); changed++; }
            }

            var rotVec = ParseVector3(rotation);
            if (rotVec.HasValue)
            {
                var p = so.FindProperty("rotation");
                if (p != null) { p.quaternionValue = Quaternion.Euler(rotVec.Value); sb.AppendLine($"  rotation: {rotVec.Value}"); changed++; }
            }

            if (insideBounds >= 0)
            {
                var p = so.FindProperty("insideBounds");
                if (p != null) { p.boolValue = insideBounds != 0; sb.AppendLine($"  insideBounds: {p.boolValue}"); changed++; }
            }

            if (changed == 0) return "No changes made (all values unchanged).";

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(col);
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Remove a VRCPhysBoneCollider from a GameObject. Requires user confirmation.")]
        public static string RemovePhysBoneCollider(string goName)
        {
            var colliderType = FindType(PhysBoneColliderTypeName);
            if (colliderType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var col = go.GetComponent(colliderType);
            if (col == null) return $"Error: No VRCPhysBoneCollider on '{goName}'.";

            if (!AgentSettings.RequestConfirmation(
                "PhysBoneCollider 削除",
                $"'{goName}' の VRCPhysBoneCollider を削除します。"))
                return "Cancelled: User denied the operation.";

            Undo.DestroyObjectImmediate(col);
            return $"Success: Removed VRCPhysBoneCollider from '{goName}'.";
        }

        // ================================================================
        // PhysBone ↔ Collider リンク
        // ================================================================

        [AgentTool("Add a VRCPhysBoneCollider reference to a VRCPhysBone's collider list. physBoneGoName: the PhysBone's GameObject, colliderGoName: the Collider's GameObject.")]
        public static string LinkColliderToPhysBone(string physBoneGoName, string colliderGoName)
        {
            var pbType = FindType(PhysBoneTypeName);
            var colType = FindType(PhysBoneColliderTypeName);
            if (pbType == null || colType == null) return "Error: VRChat SDK not found.";

            var pbGo = FindGO(physBoneGoName);
            if (pbGo == null) return $"Error: GameObject '{physBoneGoName}' not found.";
            var pb = pbGo.GetComponent(pbType);
            if (pb == null) return $"Error: No VRCPhysBone on '{physBoneGoName}'.";

            var colGo = FindGO(colliderGoName);
            if (colGo == null) return $"Error: GameObject '{colliderGoName}' not found.";
            var col = colGo.GetComponent(colType);
            if (col == null) return $"Error: No VRCPhysBoneCollider on '{colliderGoName}'.";

            Undo.RecordObject(pb, "Link Collider to PhysBone");
            var so = new SerializedObject(pb);
            var collidersProp = so.FindProperty("colliders");
            if (collidersProp == null || !collidersProp.isArray)
                return "Error: Cannot find 'colliders' property on VRCPhysBone.";

            // Check for duplicates
            for (int i = 0; i < collidersProp.arraySize; i++)
            {
                if (collidersProp.GetArrayElementAtIndex(i).objectReferenceValue == col)
                    return $"Info: '{colliderGoName}' is already linked to PhysBone on '{physBoneGoName}'.";
            }

            int idx = collidersProp.arraySize;
            collidersProp.InsertArrayElementAtIndex(idx);
            collidersProp.GetArrayElementAtIndex(idx).objectReferenceValue = col;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pb);

            return $"Success: Linked '{colliderGoName}' collider to PhysBone on '{physBoneGoName}' (total: {collidersProp.arraySize}).";
        }

        [AgentTool("Remove a VRCPhysBoneCollider reference from a VRCPhysBone's collider list.")]
        public static string UnlinkColliderFromPhysBone(string physBoneGoName, string colliderGoName)
        {
            var pbType = FindType(PhysBoneTypeName);
            var colType = FindType(PhysBoneColliderTypeName);
            if (pbType == null || colType == null) return "Error: VRChat SDK not found.";

            var pbGo = FindGO(physBoneGoName);
            if (pbGo == null) return $"Error: GameObject '{physBoneGoName}' not found.";
            var pb = pbGo.GetComponent(pbType);
            if (pb == null) return $"Error: No VRCPhysBone on '{physBoneGoName}'.";

            var colGo = FindGO(colliderGoName);
            if (colGo == null) return $"Error: GameObject '{colliderGoName}' not found.";
            var col = colGo.GetComponent(colType);
            if (col == null) return $"Error: No VRCPhysBoneCollider on '{colliderGoName}'.";

            Undo.RecordObject(pb, "Unlink Collider from PhysBone");
            var so = new SerializedObject(pb);
            var collidersProp = so.FindProperty("colliders");
            if (collidersProp == null || !collidersProp.isArray)
                return "Error: Cannot find 'colliders' property on VRCPhysBone.";

            bool found = false;
            for (int i = collidersProp.arraySize - 1; i >= 0; i--)
            {
                if (collidersProp.GetArrayElementAtIndex(i).objectReferenceValue == col)
                {
                    // Clear reference first, then delete element
                    collidersProp.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    collidersProp.DeleteArrayElementAtIndex(i);
                    found = true;
                }
            }

            if (!found)
                return $"Info: '{colliderGoName}' was not in PhysBone's collider list on '{physBoneGoName}'.";

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pb);
            return $"Success: Unlinked '{colliderGoName}' from PhysBone on '{physBoneGoName}'.";
        }

        // ================================================================
        // PhysBone Exclusions / Endpoint
        // ================================================================

        [AgentTool("Set exclusion transforms for a VRCPhysBone. exclusionNames: comma-separated list of GameObjects to exclude from PhysBone influence. Pass empty string to clear all exclusions.")]
        public static string SetPhysBoneExclusions(string goName, string exclusionNames)
        {
            var pbType = FindType(PhysBoneTypeName);
            if (pbType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var pb = go.GetComponent(pbType);
            if (pb == null) return $"Error: No VRCPhysBone on '{goName}'.";

            Undo.RecordObject(pb, "Set PhysBone Exclusions");
            var so = new SerializedObject(pb);
            var exProp = so.FindProperty("exclusions");
            if (exProp == null || !exProp.isArray)
                return "Error: Cannot find 'exclusions' property on VRCPhysBone.";

            // Clear existing
            exProp.ClearArray();

            if (string.IsNullOrWhiteSpace(exclusionNames))
            {
                so.ApplyModifiedProperties();
                return $"Success: Cleared all exclusions from PhysBone on '{goName}'.";
            }

            var names = exclusionNames.Split(',');
            var added = new List<string>();
            var notFound = new List<string>();

            foreach (var rawName in names)
            {
                string name = rawName.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                var exGo = FindGO(name);
                if (exGo == null)
                {
                    notFound.Add(name);
                    continue;
                }

                int idx = exProp.arraySize;
                exProp.InsertArrayElementAtIndex(idx);
                exProp.GetArrayElementAtIndex(idx).objectReferenceValue = exGo.transform;
                added.Add(name);
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pb);

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Set {added.Count} exclusion(s) on PhysBone '{goName}':");
            foreach (var n in added) sb.AppendLine($"  + {n}");
            if (notFound.Count > 0)
            {
                sb.AppendLine("  Not found:");
                foreach (var n in notFound) sb.AppendLine($"  ? {n}");
            }
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Set the endpoint position for a VRCPhysBone. endpointPosition format: 'x,y,z' (local offset). This defines the chain length when no child bones exist.")]
        public static string SetPhysBoneEndpoint(string goName, string endpointPosition)
        {
            var pbType = FindType(PhysBoneTypeName);
            if (pbType == null) return "Error: VRChat SDK not found.";

            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var pb = go.GetComponent(pbType);
            if (pb == null) return $"Error: No VRCPhysBone on '{goName}'.";

            var pos = ParseVector3(endpointPosition);
            if (!pos.HasValue)
                return "Error: Invalid endpointPosition format. Use 'x,y,z' (e.g., '0,0,0.1').";

            Undo.RecordObject(pb, "Set PhysBone Endpoint");
            var so = new SerializedObject(pb);
            var prop = so.FindProperty("endpointPosition");
            if (prop == null) return "Error: Cannot find 'endpointPosition' property.";

            prop.vector3Value = pos.Value;
            so.ApplyModifiedProperties();

            return $"Success: Set PhysBone endpoint on '{goName}' to {pos.Value}.";
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return root.name;
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
    }
}

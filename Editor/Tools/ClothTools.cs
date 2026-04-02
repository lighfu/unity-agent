using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Cloth コンポーネントの追加・設定・検査ツール。
    /// VRChat アバターの衣装物理シミュレーション（スカート、袖、髪など）に使用。
    /// </summary>
    public static class ClothTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        [AgentTool(@"Add a Cloth component to a SkinnedMeshRenderer GameObject.
stretchingStiffness/bendingStiffness: 0-1, resistance to stretching/bending (higher=stiffer).
damping: 0-1, motion damping (higher=less bouncy).
friction: collision friction.")]
        public static string AddCloth(string goName, float stretchingStiffness = 0.8f, float bendingStiffness = 0.8f,
            float damping = 0.1f, float friction = 0.5f)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            if (go.GetComponent<SkinnedMeshRenderer>() == null)
                return $"Error: '{goName}' has no SkinnedMeshRenderer. Cloth requires a SkinnedMeshRenderer.";

            if (go.GetComponent<Cloth>() != null)
                return $"Error: '{goName}' already has a Cloth component.";

            var cloth = Undo.AddComponent<Cloth>(go);
            cloth.stretchingStiffness = stretchingStiffness;
            cloth.bendingStiffness = bendingStiffness;
            cloth.damping = damping;
            cloth.friction = friction;

            return $"Success: Added Cloth to '{goName}' (stretch={stretchingStiffness}, bend={bendingStiffness}, damp={damping}).";
        }

        [AgentTool(@"Configure Cloth physics parameters.
stretchingStiffness/bendingStiffness: 0-1 resistance. damping: 0-1 motion damping.
friction: collision friction. collisionMassScale: mass increase on collision.
useGravity: apply gravity. useTethers: anchor constraints. useVirtualParticles: stability.
selfCollisionDistance: min self-collision distance. selfCollisionStiffness: self-collision force.
clothSolverFrequency: solver iterations/sec (higher=more accurate but slower).
worldVelocityScale/worldAccelerationScale: 0-1, influence of character movement.
externalAcceleration: constant force 'x,y,z'. randomAcceleration: random force 'x,y,z'.")]
        public static string ConfigureCloth(string goName, float stretchingStiffness = -1f, float bendingStiffness = -1f,
            float damping = -1f, float friction = -1f, float collisionMassScale = -1f,
            int useGravity = -1, int useTethers = -1, int useVirtualParticles = -1,
            float selfCollisionDistance = -1f, float selfCollisionStiffness = -1f,
            float clothSolverFrequency = -1f, float worldVelocityScale = -1f, float worldAccelerationScale = -1f,
            string externalAcceleration = "", string randomAcceleration = "")
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var cloth = go.GetComponent<Cloth>();
            if (cloth == null) return $"Error: No Cloth on '{goName}'.";

            Undo.RecordObject(cloth, "Configure Cloth");

            if (stretchingStiffness >= 0) cloth.stretchingStiffness = stretchingStiffness;
            if (bendingStiffness >= 0) cloth.bendingStiffness = bendingStiffness;
            if (damping >= 0) cloth.damping = damping;
            if (friction >= 0) cloth.friction = friction;
            if (collisionMassScale >= 0) cloth.collisionMassScale = collisionMassScale;
            if (useGravity >= 0) cloth.useGravity = useGravity != 0;
            if (useTethers >= 0) cloth.useTethers = useTethers != 0;
            if (useVirtualParticles >= 0) cloth.useVirtualParticles = useVirtualParticles;
            if (selfCollisionDistance >= 0) cloth.selfCollisionDistance = selfCollisionDistance;
            if (selfCollisionStiffness >= 0) cloth.selfCollisionStiffness = selfCollisionStiffness;
            if (clothSolverFrequency > 0) cloth.clothSolverFrequency = clothSolverFrequency;
            if (worldVelocityScale >= 0) cloth.worldVelocityScale = worldVelocityScale;
            if (worldAccelerationScale >= 0) cloth.worldAccelerationScale = worldAccelerationScale;

            if (!string.IsNullOrEmpty(externalAcceleration))
            {
                var v = ParseVector3(externalAcceleration);
                if (v.HasValue) cloth.externalAcceleration = v.Value;
            }
            if (!string.IsNullOrEmpty(randomAcceleration))
            {
                var v = ParseVector3(randomAcceleration);
                if (v.HasValue) cloth.randomAcceleration = v.Value;
            }

            EditorUtility.SetDirty(cloth);
            return $"Success: Configured Cloth on '{goName}'.";
        }

        [AgentTool("Inspect a Cloth component. Shows all physics parameters, collider count, and vertex info.")]
        public static string InspectCloth(string goName)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var cloth = go.GetComponent<Cloth>();
            if (cloth == null) return $"Error: No Cloth on '{goName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Cloth on '{goName}':");
            sb.AppendLine($"  Enabled: {cloth.enabled}");
            sb.AppendLine($"  StretchingStiffness: {cloth.stretchingStiffness:F3}");
            sb.AppendLine($"  BendingStiffness: {cloth.bendingStiffness:F3}");
            sb.AppendLine($"  Damping: {cloth.damping:F3}");
            sb.AppendLine($"  Friction: {cloth.friction:F3}");
            sb.AppendLine($"  CollisionMassScale: {cloth.collisionMassScale:F3}");
            sb.AppendLine($"  UseGravity: {cloth.useGravity}");
            sb.AppendLine($"  UseTethers: {cloth.useTethers}");
            sb.AppendLine($"  UseVirtualParticles: {cloth.useVirtualParticles}");
            sb.AppendLine($"  SelfCollisionDistance: {cloth.selfCollisionDistance:F4}");
            sb.AppendLine($"  SelfCollisionStiffness: {cloth.selfCollisionStiffness:F3}");
            sb.AppendLine($"  SolverFrequency: {cloth.clothSolverFrequency:F1}");
            sb.AppendLine($"  WorldVelocityScale: {cloth.worldVelocityScale:F3}");
            sb.AppendLine($"  WorldAccelerationScale: {cloth.worldAccelerationScale:F3}");
            sb.AppendLine($"  ExternalAcceleration: {cloth.externalAcceleration}");
            sb.AppendLine($"  RandomAcceleration: {cloth.randomAcceleration}");

            var spheres = cloth.sphereColliders;
            var capsules = cloth.capsuleColliders;
            sb.AppendLine($"  SphereColliders: {spheres.Length}");
            sb.AppendLine($"  CapsuleColliders: {capsules.Length}");

            var coeffs = cloth.coefficients;
            sb.AppendLine($"  Vertices: {coeffs.Length}");
            if (coeffs.Length > 0)
            {
                int pinned = coeffs.Count(c => c.maxDistance == 0f);
                sb.AppendLine($"  Pinned Vertices: {pinned}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"Set cloth colliders for collision with avatar body.
sphereColliderPaths: semicolon-separated GameObjects with SphereCollider. Each can be a single sphere or pair 'path1,path2'.
capsuleColliderPaths: semicolon-separated GameObjects with CapsuleCollider.")]
        public static string SetClothColliders(string goName, string sphereColliderPaths = "", string capsuleColliderPaths = "")
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var cloth = go.GetComponent<Cloth>();
            if (cloth == null) return $"Error: No Cloth on '{goName}'.";

            Undo.RecordObject(cloth, "Set Cloth Colliders");
            int count = 0;

            if (!string.IsNullOrEmpty(sphereColliderPaths))
            {
                var pairs = new System.Collections.Generic.List<ClothSphereColliderPair>();
                var entries = sphereColliderPaths.Split(';');
                foreach (var entry in entries)
                {
                    var parts = entry.Trim().Split(',');
                    SphereCollider s1 = null, s2 = null;
                    if (parts.Length >= 1)
                    {
                        var g1 = FindGO(parts[0].Trim());
                        if (g1 != null) s1 = g1.GetComponent<SphereCollider>();
                    }
                    if (parts.Length >= 2)
                    {
                        var g2 = FindGO(parts[1].Trim());
                        if (g2 != null) s2 = g2.GetComponent<SphereCollider>();
                    }
                    if (s1 != null) pairs.Add(new ClothSphereColliderPair(s1, s2));
                }
                cloth.sphereColliders = pairs.ToArray();
                count += pairs.Count;
            }

            if (!string.IsNullOrEmpty(capsuleColliderPaths))
            {
                var capsules = new System.Collections.Generic.List<CapsuleCollider>();
                var entries = capsuleColliderPaths.Split(';');
                foreach (var entry in entries)
                {
                    var g = FindGO(entry.Trim());
                    if (g != null)
                    {
                        var c = g.GetComponent<CapsuleCollider>();
                        if (c != null) capsules.Add(c);
                    }
                }
                cloth.capsuleColliders = capsules.ToArray();
                count += capsules.Count;
            }

            EditorUtility.SetDirty(cloth);
            return $"Success: Set {count} colliders on Cloth of '{goName}'.";
        }

        [AgentTool("List all Cloth components in the scene with basic info.")]
        public static string ListCloths()
        {
            var cloths = Object.FindObjectsOfType<Cloth>(true);
            if (cloths.Length == 0) return "No Cloth components in the scene.";

            var sb = new StringBuilder();
            sb.AppendLine($"Cloth components in scene ({cloths.Length}):");
            foreach (var c in cloths.OrderBy(c => c.gameObject.name))
            {
                string path = GetHierarchyPath(c.transform);
                sb.AppendLine($"  {path}: stretch={c.stretchingStiffness:F2}, bend={c.bendingStiffness:F2}, verts={c.coefficients.Length}");
            }
            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static Vector3? ParseVector3(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            if (parts.Length != 3) return null;
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var ns = System.Globalization.NumberStyles.Float;
            if (float.TryParse(parts[0].Trim(), ns, ic, out float x) &&
                float.TryParse(parts[1].Trim(), ns, ic, out float y) &&
                float.TryParse(parts[2].Trim(), ns, ic, out float z))
                return new Vector3(x, y, z);
            return null;
        }

        private static string GetHierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var current = t.parent;
            while (current != null) { sb.Insert(0, current.name + "/"); current = current.parent; }
            return sb.ToString();
        }
    }
}

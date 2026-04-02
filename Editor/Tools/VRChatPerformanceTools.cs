using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Collections.Generic;
using UnityEngine.Profiling;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class VRChatPerformanceTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // PC Performance thresholds: [Excellent, Good, Medium, Poor] — beyond Poor = Very Poor
        private static readonly int[] TriangleThresholds = { 32000, 70000, 70000, 70000 };
        private static readonly long[] TextureMemoryMBThresholds = { 40, 75, 110, 150 };
        private static readonly int[] SkinnedMeshThresholds = { 1, 2, 8, 16 };
        private static readonly int[] BasicMeshThresholds = { 4, 8, 16, 24 };
        private static readonly int[] MaterialSlotThresholds = { 4, 8, 16, 32 };
        private static readonly int[] PhysBoneThresholds = { 4, 8, 16, 32 };
        private static readonly int[] PBTransformThresholds = { 16, 64, 128, 256 };
        private static readonly int[] PBColliderThresholds = { 4, 8, 16, 32 };
        private static readonly int[] PBCollisionCheckThresholds = { 32, 128, 256, 512 };
        private static readonly int[] ContactThresholds = { 8, 16, 24, 32 };
        private static readonly int[] ConstraintThresholds = { 100, 250, 300, 350 };
        private static readonly int[] ConstraintDepthThresholds = { 20, 50, 80, 100 };
        private static readonly int[] AnimatorThresholds = { 1, 4, 16, 32 };
        private static readonly int[] BoneThresholds = { 75, 150, 256, 400 };
        private static readonly int[] LightThresholds = { 0, 0, 0, 1 };
        private static readonly int[] ParticleSystemThresholds = { 0, 4, 8, 16 };
        private static readonly int[] TotalParticlesThresholds = { 0, 300, 1000, 2500 };
        private static readonly int[] MeshParticlePolyThresholds = { 0, 1000, 2000, 5000 };
        private static readonly int[] TrailRendererThresholds = { 1, 2, 4, 8 };
        private static readonly int[] LineRendererThresholds = { 1, 2, 4, 8 };
        private static readonly int[] ClothThresholds = { 0, 1, 1, 1 };
        private static readonly int[] ClothVertexThresholds = { 0, 50, 100, 200 };
        private static readonly int[] PhysicsColliderThresholds = { 0, 1, 8, 8 };
        private static readonly int[] PhysicsRigidbodyThresholds = { 0, 1, 8, 8 };
        private static readonly int[] AudioSourceThresholds = { 1, 4, 8, 8 };

        private static readonly string[] RankNames = { "Excellent", "Good", "Medium", "Poor", "Very Poor" };

        private static string GetRank(int value, int[] thresholds)
        {
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (value <= thresholds[i]) return RankNames[i];
            }
            return RankNames[4]; // Very Poor
        }

        private static string GetRankLong(long value, long[] thresholds)
        {
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (value <= thresholds[i]) return RankNames[i];
            }
            return RankNames[4];
        }

        private static int RankToIndex(string rank)
        {
            for (int i = 0; i < RankNames.Length; i++)
            {
                if (RankNames[i] == rank) return i;
            }
            return 4;
        }

        [AgentTool("Get comprehensive avatar performance stats with all VRChat categories and accurate PC ranking. Shows triangles, textures, meshes, materials, PhysBones, contacts, constraints, particles, cloth, audio, etc.")]
        public static string GetAvatarPerformanceStats(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Performance Stats for '{avatarRootName}':");

            int worstRankIdx = 0; // Track worst rank

            // === Mesh & Triangles ===
            var skinnedRenderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshFilters = go.GetComponentsInChildren<MeshFilter>(true);
            var allRenderers = go.GetComponentsInChildren<Renderer>(true);

            int totalTriangles = 0;
            int materialSlots = 0;
            var textureSet = new HashSet<Texture>();

            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMesh != null)
                    totalTriangles += smr.sharedMesh.triangles.Length / 3;
                CollectMaterialInfo(smr.sharedMaterials, ref materialSlots, textureSet);
            }

            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                    totalTriangles += mf.sharedMesh.triangles.Length / 3;
                var r = mf.GetComponent<Renderer>();
                if (r != null)
                    CollectMaterialInfo(r.sharedMaterials, ref materialSlots, textureSet);
            }

            // Texture Memory
            long textureMemoryBytes = 0;
            foreach (var tex in textureSet)
            {
                if (tex != null)
                    textureMemoryBytes += Profiler.GetRuntimeMemorySizeLong(tex);
            }
            long textureMemoryMB = textureMemoryBytes / (1024 * 1024);

            string triRank = GetRank(totalTriangles, TriangleThresholds);
            string texMemRank = GetRankLong(textureMemoryMB, TextureMemoryMBThresholds);
            string skinnedRank = GetRank(skinnedRenderers.Length, SkinnedMeshThresholds);
            string basicRank = GetRank(meshFilters.Length, BasicMeshThresholds);
            string matSlotRank = GetRank(materialSlots, MaterialSlotThresholds);

            sb.AppendLine($"  Triangles: {totalTriangles:N0} [{triRank}]");
            sb.AppendLine($"  Texture Memory: {textureMemoryMB} MB [{texMemRank}]");
            sb.AppendLine($"  Skinned Meshes: {skinnedRenderers.Length} [{skinnedRank}]");
            sb.AppendLine($"  Basic Meshes: {meshFilters.Length} [{basicRank}]");
            sb.AppendLine($"  Material Slots: {materialSlots} [{matSlotRank}]");

            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(triRank));
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(texMemRank));
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(skinnedRank));
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(basicRank));
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(matSlotRank));

            // === Bones ===
            var allTransforms = go.GetComponentsInChildren<Transform>(true);
            // Count bones: transforms that are referenced by SkinnedMeshRenderers
            var boneSet = new HashSet<Transform>();
            foreach (var smr in skinnedRenderers)
            {
                if (smr.bones != null)
                    foreach (var bone in smr.bones)
                        if (bone != null) boneSet.Add(bone);
            }
            int boneCount = boneSet.Count > 0 ? boneSet.Count : allTransforms.Length;
            string boneRank = GetRank(boneCount, BoneThresholds);
            sb.AppendLine($"  Bones: {boneCount} [{boneRank}]");
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(boneRank));

            // === PhysBones ===
            var physBoneType = VRChatTools.FindVrcType(VRChatTools.VrcPhysBoneTypeName);
            int pbCount = 0, pbTransformCount = 0, pbColliderCount = 0, pbCollisionCheckCount = 0;
            if (physBoneType != null)
            {
                var physBones = go.GetComponentsInChildren(physBoneType, true);
                pbCount = physBones.Length;

                var pbColliderType = VRChatTools.FindVrcType("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider");
                if (pbColliderType != null)
                    pbColliderCount = go.GetComponentsInChildren(pbColliderType, true).Length;

                foreach (var pb in physBones)
                {
                    var pbSo = new SerializedObject(pb);
                    int affectedTransforms = CountAffectedTransforms(pbSo, pb);
                    pbTransformCount += affectedTransforms;

                    var collidersArr = pbSo.FindProperty("colliders");
                    int colCount = (collidersArr != null && collidersArr.isArray) ? collidersArr.arraySize : 0;
                    pbCollisionCheckCount += affectedTransforms * colCount;
                }

                string pbRank = GetRank(pbCount, PhysBoneThresholds);
                string pbtRank = GetRank(pbTransformCount, PBTransformThresholds);
                string pbcRank = GetRank(pbColliderCount, PBColliderThresholds);
                string pbccRank = GetRank(pbCollisionCheckCount, PBCollisionCheckThresholds);

                sb.AppendLine($"  PhysBones: {pbCount} [{pbRank}]");
                sb.AppendLine($"  PB Affected Transforms: {pbTransformCount} [{pbtRank}]");
                sb.AppendLine($"  PB Colliders: {pbColliderCount} [{pbcRank}]");
                sb.AppendLine($"  PB Collision Check Count: {pbCollisionCheckCount} [{pbccRank}]");

                worstRankIdx = Math.Max(worstRankIdx, RankToIndex(pbRank));
                worstRankIdx = Math.Max(worstRankIdx, RankToIndex(pbtRank));
                worstRankIdx = Math.Max(worstRankIdx, RankToIndex(pbcRank));
                worstRankIdx = Math.Max(worstRankIdx, RankToIndex(pbccRank));
            }

            // === Contacts ===
            var contactSenderType = VRChatTools.FindVrcType("VRC.SDK3.Dynamics.Contact.Components.VRCContactSender");
            var contactReceiverType = VRChatTools.FindVrcType("VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver");
            int contactCount = 0;
            if (contactSenderType != null)
                contactCount += go.GetComponentsInChildren(contactSenderType, true).Length;
            if (contactReceiverType != null)
                contactCount += go.GetComponentsInChildren(contactReceiverType, true).Length;
            string contactRank = GetRank(contactCount, ContactThresholds);
            sb.AppendLine($"  Contacts: {contactCount} [{contactRank}]");
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(contactRank));

            // === Constraints ===
            int constraintCount = 0;
            int constraintDepth = 0;

            // Unity IConstraint
            var iConstraintType = typeof(UnityEngine.Animations.IConstraint);
            foreach (var comp in go.GetComponentsInChildren<Component>(true))
            {
                if (comp != null && iConstraintType.IsAssignableFrom(comp.GetType()))
                    constraintCount++;
            }

            // VRC Constraints
            var vrcConstraintBaseType = VRChatTools.FindVrcType("VRC.SDK3.Dynamics.Constraint.Components.VRCConstraintBase");
            if (vrcConstraintBaseType != null)
            {
                var vrcConstraints = go.GetComponentsInChildren(vrcConstraintBaseType, true);
                constraintCount += vrcConstraints.Length;
            }

            constraintDepth = CalculateConstraintDepth(go);

            string constraintRank = GetRank(constraintCount, ConstraintThresholds);
            string constraintDepthRank = GetRank(constraintDepth, ConstraintDepthThresholds);
            sb.AppendLine($"  Constraints: {constraintCount} [{constraintRank}]");
            sb.AppendLine($"  Constraint Depth: {constraintDepth} [{constraintDepthRank}]");
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(constraintRank));
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(constraintDepthRank));

            // === Animators ===
            var animators = go.GetComponentsInChildren<Animator>(true);
            string animatorRank = GetRank(animators.Length, AnimatorThresholds);
            sb.AppendLine($"  Animators: {animators.Length} [{animatorRank}]");
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(animatorRank));

            // === Lights ===
            var lights = go.GetComponentsInChildren<Light>(true);
            string lightRank = GetRank(lights.Length, LightThresholds);
            sb.AppendLine($"  Lights: {lights.Length} [{lightRank}]");
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(lightRank));

            // === Particle Systems ===
            var particleSystems = go.GetComponentsInChildren<ParticleSystem>(true);
            int totalMaxParticles = 0;
            int meshParticlePolys = 0;
            bool hasParticleTrails = false;
            bool hasParticleCollision = false;

            foreach (var ps in particleSystems)
            {
                totalMaxParticles += ps.main.maxParticles;

                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer != null && renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.mesh != null)
                    meshParticlePolys += renderer.mesh.triangles.Length / 3;

                if (ps.trails.enabled) hasParticleTrails = true;
                if (ps.collision.enabled) hasParticleCollision = true;
            }

            string psRank = GetRank(particleSystems.Length, ParticleSystemThresholds);
            string tpRank = GetRank(totalMaxParticles, TotalParticlesThresholds);
            string mppRank = GetRank(meshParticlePolys, MeshParticlePolyThresholds);

            sb.AppendLine($"  Particle Systems: {particleSystems.Length} [{psRank}]");
            sb.AppendLine($"  Total Max Particles: {totalMaxParticles} [{tpRank}]");
            sb.AppendLine($"  Mesh Particle Polys: {meshParticlePolys} [{mppRank}]");
            if (hasParticleTrails) sb.AppendLine("  Particle Trails: Enabled");
            if (hasParticleCollision) sb.AppendLine("  Particle Collision: Enabled");

            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(psRank));
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(tpRank));
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(mppRank));

            // === Trail / Line Renderers ===
            var trailRenderers = go.GetComponentsInChildren<TrailRenderer>(true);
            var lineRenderers = go.GetComponentsInChildren<LineRenderer>(true);
            string trailRank = GetRank(trailRenderers.Length, TrailRendererThresholds);
            string lineRank = GetRank(lineRenderers.Length, LineRendererThresholds);
            sb.AppendLine($"  Trail Renderers: {trailRenderers.Length} [{trailRank}]");
            sb.AppendLine($"  Line Renderers: {lineRenderers.Length} [{lineRank}]");
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(trailRank));
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(lineRank));

            // === Cloth ===
            var cloths = go.GetComponentsInChildren<Cloth>(true);
            int totalClothVertices = 0;
            foreach (var cloth in cloths)
            {
                if (cloth.vertices != null)
                    totalClothVertices += cloth.vertices.Length;
            }
            string clothRank = GetRank(cloths.Length, ClothThresholds);
            string clothVertRank = GetRank(totalClothVertices, ClothVertexThresholds);
            sb.AppendLine($"  Cloths: {cloths.Length} [{clothRank}]");
            sb.AppendLine($"  Cloth Vertices: {totalClothVertices} [{clothVertRank}]");
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(clothRank));
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(clothVertRank));

            // === Physics Colliders / Rigidbodies ===
            var physicsColliders = go.GetComponentsInChildren<Collider>(true);
            var rigidbodies = go.GetComponentsInChildren<Rigidbody>(true);
            string pcRank = GetRank(physicsColliders.Length, PhysicsColliderThresholds);
            string rbRank = GetRank(rigidbodies.Length, PhysicsRigidbodyThresholds);
            sb.AppendLine($"  Physics Colliders: {physicsColliders.Length} [{pcRank}]");
            sb.AppendLine($"  Physics Rigidbodies: {rigidbodies.Length} [{rbRank}]");
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(pcRank));
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(rbRank));

            // === Audio Sources ===
            var audioSources = go.GetComponentsInChildren<AudioSource>(true);
            string audioRank = GetRank(audioSources.Length, AudioSourceThresholds);
            sb.AppendLine($"  Audio Sources: {audioSources.Length} [{audioRank}]");
            worstRankIdx = Math.Max(worstRankIdx, RankToIndex(audioRank));

            // === Bounds Size ===
            Bounds combinedBounds = new Bounds(go.transform.position, Vector3.zero);
            bool hasBounds = false;
            foreach (var r in allRenderers)
            {
                if (!hasBounds)
                {
                    combinedBounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(r.bounds);
                }
            }
            if (hasBounds)
                sb.AppendLine($"  Bounds Size: {combinedBounds.size.x:F2} x {combinedBounds.size.y:F2} x {combinedBounds.size.z:F2}");

            // === Overall Rank ===
            sb.AppendLine();
            sb.AppendLine($"  Overall Performance Rank: {RankNames[worstRankIdx]}");

            return sb.ToString().TrimEnd();
        }

        private static void CollectMaterialInfo(Material[] materials, ref int materialSlots, HashSet<Texture> textureSet)
        {
            if (materials == null) return;
            materialSlots += materials.Length;

            foreach (var mat in materials)
            {
                if (mat == null) continue;
                CollectTexturesFromMaterial(mat, textureSet);
            }
        }

        private static void CollectTexturesFromMaterial(Material mat, HashSet<Texture> textureSet)
        {
            if (mat == null || mat.shader == null) return;

            int propCount = ShaderUtil.GetPropertyCount(mat.shader);
            for (int i = 0; i < propCount; i++)
            {
                if (ShaderUtil.GetPropertyType(mat.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propName = ShaderUtil.GetPropertyName(mat.shader, i);
                    var tex = mat.GetTexture(propName);
                    if (tex != null)
                        textureSet.Add(tex);
                }
            }
        }

        private static int CountAffectedTransforms(SerializedObject pbSo, Component pb)
        {
            var rootTransformProp = pbSo.FindProperty("rootTransform");
            Transform root = (rootTransformProp != null && rootTransformProp.objectReferenceValue != null)
                ? (Transform)rootTransformProp.objectReferenceValue
                : pb.transform;

            var exclusionsProp = pbSo.FindProperty("exclusions");
            var exclusions = new HashSet<Transform>();
            if (exclusionsProp != null && exclusionsProp.isArray)
            {
                for (int i = 0; i < exclusionsProp.arraySize; i++)
                {
                    var excl = exclusionsProp.GetArrayElementAtIndex(i);
                    if (excl.objectReferenceValue != null)
                        exclusions.Add((Transform)excl.objectReferenceValue);
                }
            }

            return CountTransformsRecursive(root, exclusions) - 1; // Exclude root itself
        }

        private static int CountTransformsRecursive(Transform t, HashSet<Transform> exclusions)
        {
            if (exclusions.Contains(t)) return 0;
            int count = 1;
            for (int i = 0; i < t.childCount; i++)
                count += CountTransformsRecursive(t.GetChild(i), exclusions);
            return count;
        }

        private static int CalculateConstraintDepth(GameObject root)
        {
            // Build a map of constrained transforms and their source transforms
            var constrainedObjects = new Dictionary<Transform, List<Transform>>();

            var iConstraintType = typeof(UnityEngine.Animations.IConstraint);
            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                if (!iConstraintType.IsAssignableFrom(comp.GetType())) continue;

                var constraint = comp as UnityEngine.Animations.IConstraint;
                if (constraint == null) continue;

                var sources = new List<Transform>();
                for (int i = 0; i < constraint.sourceCount; i++)
                {
                    var source = constraint.GetSource(i);
                    if (source.sourceTransform != null)
                        sources.Add(source.sourceTransform);
                }

                if (!constrainedObjects.ContainsKey(comp.transform))
                    constrainedObjects[comp.transform] = new List<Transform>();
                constrainedObjects[comp.transform].AddRange(sources);
            }

            // VRC Constraints
            var vrcConstraintBaseType = VRChatTools.FindVrcType("VRC.SDK3.Dynamics.Constraint.Components.VRCConstraintBase");
            if (vrcConstraintBaseType != null)
            {
                foreach (var comp in root.GetComponentsInChildren(vrcConstraintBaseType, true))
                {
                    var so = new SerializedObject(comp);
                    var sources = so.FindProperty("Sources");
                    if (sources == null || !sources.isArray) continue;

                    var sourceTransforms = new List<Transform>();
                    for (int i = 0; i < sources.arraySize; i++)
                    {
                        var sourceElement = sources.GetArrayElementAtIndex(i);
                        var sourceTransform = sourceElement.FindPropertyRelative("SourceTransform");
                        if (sourceTransform != null && sourceTransform.objectReferenceValue != null)
                            sourceTransforms.Add((Transform)sourceTransform.objectReferenceValue);
                    }

                    if (!constrainedObjects.ContainsKey(comp.transform))
                        constrainedObjects[comp.transform] = new List<Transform>();
                    constrainedObjects[comp.transform].AddRange(sourceTransforms);
                }
            }

            // Calculate max depth via DFS
            int maxDepth = 0;
            var visited = new HashSet<Transform>();
            foreach (var t in constrainedObjects.Keys)
            {
                int depth = GetConstraintChainDepth(t, constrainedObjects, visited);
                if (depth > maxDepth) maxDepth = depth;
            }

            return maxDepth;
        }

        private static int GetConstraintChainDepth(Transform t, Dictionary<Transform, List<Transform>> graph, HashSet<Transform> visited)
        {
            if (visited.Contains(t)) return 0; // Prevent cycles
            visited.Add(t);

            if (!graph.ContainsKey(t)) return 1;

            int maxSourceDepth = 0;
            foreach (var source in graph[t])
            {
                int d = GetConstraintChainDepth(source, graph, visited);
                if (d > maxSourceDepth) maxSourceDepth = d;
            }

            visited.Remove(t);
            return 1 + maxSourceDepth;
        }
    }
}

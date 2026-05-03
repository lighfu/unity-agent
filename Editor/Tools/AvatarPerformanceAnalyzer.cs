using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using nadena.dev.ndmf;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Avatar performance analysis without baking. Combines:
    /// <list type="bullet">
    /// <item>VRC SDK's official <c>AvatarPerformance.CalculatePerformanceStats</c> for runtime-relevant stats
    ///   (poly count, mesh/material count, PhysBone, contacts, animators, particles, audio,
    ///   download size, uncompressed size, etc.). This is the same calculator the in-build
    ///   performance ranker uses, and AAO calls it before/after its passes.</item>
    /// <item>NDMF <c>ParameterInfo.ForUI</c> for the post-build parameter list, which projects every
    ///   parameter that NDMF / Modular Avatar / VRCFury / etc. will contribute at build time —
    ///   without actually baking. This gives the realistic 256-bit budget usage.</item>
    /// </list>
    /// VRC SDK is reached through reflection so the tool stays compatible with environments where
    /// VRC.SDKBase is absent (the call gracefully reports a single error line instead of throwing).
    /// </summary>
    public static class AvatarPerformanceAnalyzer
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        // ─────────────────────────────────────────────────────────────────
        // Public AgentTool surface
        // ─────────────────────────────────────────────────────────────────

        [AgentTool("Compute VRChat avatar performance stats with no bake required. Uses VRC SDK's official AvatarPerformance.CalculatePerformanceStats (the same calculator the build performance ranker uses) plus NDMF ParameterInfo for post-build parameter budget. Returns combined report. Prefer this over GetAvatarPerformanceStats for production analysis.")]
        public static string AnalyzeAvatarPerformance(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";
            return AnalyzeForGameObject(go, avatarRootName);
        }

        // ─────────────────────────────────────────────────────────────────
        // Internal pipeline
        // ─────────────────────────────────────────────────────────────────

        internal static string AnalyzeForGameObject(GameObject go, string displayName)
        {
            if (go == null) return $"Error: GameObject '{displayName}' is null.";

            var sb = new StringBuilder();
            sb.AppendLine($"Avatar Performance (no bake) for '{displayName}':");

            sb.AppendLine();
            sb.AppendLine("== VRC SDK Official Stats (live) ==");
            AppendVrcStats(sb, go, displayName);

            sb.AppendLine();
            sb.AppendLine("== NDMF Parameter Budget (post-build view) ==");
            AppendNdmfParameterBudget(sb, go);

            return sb.ToString().TrimEnd();
        }

        // ─── VRC SDK section (reflected) ───

        private static void AppendVrcStats(StringBuilder sb, GameObject go, string displayName)
        {
            var apType = FindType("VRC.SDKBase.Validation.Performance.AvatarPerformance");
            var apsType = FindType("VRC.SDKBase.Validation.Performance.Stats.AvatarPerformanceStats");
            if (apType == null || apsType == null)
            {
                sb.AppendLine("  Error: VRC SDK (VRC.SDKBase.Validation.Performance) not found.");
                return;
            }

            object stats;
            try
            {
                var ctor = apsType.GetConstructor(new[] { typeof(bool) });
                if (ctor == null)
                {
                    sb.AppendLine("  Error: AvatarPerformanceStats(bool) ctor not found.");
                    return;
                }
                stats = ctor.Invoke(new object[] { false });

                var calc = apType.GetMethod("CalculatePerformanceStats",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(GameObject), apsType, typeof(bool) },
                    null);
                if (calc == null)
                {
                    sb.AppendLine("  Error: CalculatePerformanceStats(string, GameObject, AvatarPerformanceStats, bool) not found.");
                    return;
                }
                calc.Invoke(null, new object[] { displayName, go, stats, false });
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  Error invoking CalculatePerformanceStats: {ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
                return;
            }

            // Top-level fields
            AppendField(sb, stats, "polyCount", "Polygons");
            AppendField(sb, stats, "textureMegabytes", "Texture Memory (MB)");
            AppendField(sb, stats, "skinnedMeshCount", "Skinned Meshes");
            AppendField(sb, stats, "meshCount", "Static Meshes");
            AppendField(sb, stats, "materialCount", "Materials");
            AppendField(sb, stats, "boneCount", "Bones");
            AppendField(sb, stats, "animatorCount", "Animators");
            AppendField(sb, stats, "lightCount", "Lights");
            AppendField(sb, stats, "particleSystemCount", "Particle Systems");
            AppendField(sb, stats, "particleTotalCount", "Particle Total");
            AppendField(sb, stats, "particleMaxMeshPolyCount", "Particle Mesh Polys");
            AppendField(sb, stats, "particleTrailsEnabled", "Particle Trails");
            AppendField(sb, stats, "particleCollisionEnabled", "Particle Collision");
            AppendField(sb, stats, "trailRendererCount", "Trail Renderers");
            AppendField(sb, stats, "lineRendererCount", "Line Renderers");
            AppendField(sb, stats, "clothCount", "Cloth");
            AppendField(sb, stats, "clothMaxVertices", "Cloth Vertices");
            AppendField(sb, stats, "physicsColliderCount", "Physics Colliders");
            AppendField(sb, stats, "physicsRigidbodyCount", "Physics Rigidbodies");
            AppendField(sb, stats, "audioSourceCount", "Audio Sources");
            AppendField(sb, stats, "constraintsCount", "Constraints");
            AppendField(sb, stats, "constraintDepth", "Constraint Depth");
            AppendField(sb, stats, "contactCount", "Contacts");
            AppendField(sb, stats, "downloadSizeBytes", "Download Size (bytes)");
            AppendField(sb, stats, "uncompressedSizeBytes", "Uncompressed Size (bytes)");

            // Nested physBone struct
            var pbField = stats.GetType().GetField("physBone");
            if (pbField != null)
            {
                var pb = pbField.GetValue(stats);
                if (pb != null)
                {
                    AppendField(sb, pb, "componentCount", "PhysBone Components");
                    AppendField(sb, pb, "transformCount", "PhysBone Transforms");
                    AppendField(sb, pb, "colliderCount", "PhysBone Colliders");
                    AppendField(sb, pb, "collisionCheckCount", "PhysBone Collision Checks");
                }
            }
        }

        private static void AppendField(StringBuilder sb, object owner, string fieldName, string label)
        {
            var f = owner.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return;

            object raw = f.GetValue(owner);
            string rendered = FormatValue(raw);
            if (rendered == null) return; // hide null nullables for noise reduction
            sb.AppendLine($"  {label}: {rendered}");
        }

        private static string FormatValue(object raw)
        {
            if (raw == null) return null;

            var t = raw.GetType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                var hasValue = (bool)(t.GetProperty("HasValue")?.GetValue(raw) ?? false);
                if (!hasValue) return null;
                return t.GetProperty("Value")?.GetValue(raw)?.ToString() ?? "(null)";
            }

            return raw.ToString();
        }

        // ─── NDMF parameter budget ───

        private static void AppendNdmfParameterBudget(StringBuilder sb, GameObject go)
        {
            try
            {
                var paramInfo = nadena.dev.ndmf.ParameterInfo.ForUI;
                int totalParams = 0;
                int syncedParams = 0;
                int totalBits = 0;
                foreach (var p in paramInfo.GetParametersForObject(go))
                {
                    totalParams++;
                    if (p.WantSynced)
                    {
                        syncedParams++;
                        totalBits += p.BitUsage;
                    }
                }
                float usagePct = totalBits * 100f / 256f;
                sb.AppendLine($"  Total parameters: {totalParams} ({syncedParams} synced)");
                sb.AppendLine($"  Synced bit cost : {totalBits} / 256 ({usagePct:F1}%)");
                if (totalBits > 256)
                    sb.AppendLine("  WARNING: synced parameter budget EXCEEDS 256 bits.");
                else if (usagePct >= 90f)
                    sb.AppendLine("  Note: synced parameter budget over 90%.");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  Error reading NDMF ParameterInfo: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ─── Helpers ───

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType(fullName); }
                catch { continue; }
                if (t != null) return t;
            }
            return null;
        }
    }
}

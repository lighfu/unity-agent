using System;
using System.Collections.Generic;
using System.Text;
using AjisaiFlow.UnityAgent.SDK;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    // Identify the Body / Face SkinnedMeshRenderer on a VRChat-style humanoid
    // avatar with a deterministic multi-tier heuristic. Algorithm ported from
    // BoundBonePro (商用, 独立実装のため BBP asmdef 参照なし).
    public static class AvatarAnatomyTools
    {
        // ─────────────────────────────────────────────
        // Public MCP tools
        // ─────────────────────────────────────────────

        [AgentTool("アバター配下の Body SkinnedMeshRenderer を誤差ゼロで特定する。Tier1: 名前マッチ (単一 'Body'), Tier2: 骨領域多様性スコア (humanoid 7 領域), Tier3: Y 軸最大範囲 (non-humanoid fallback)。JSON で採用 Tier と診断情報を返す。",
            Author = "ajisaiflow",
            Category = "AvatarAnatomy",
            Risk = ToolRisk.Safe)]
        public static string IdentifyBodySmr(string avatarRootName)
        {
            if (string.IsNullOrWhiteSpace(avatarRootName))
                return "Error: avatarRootName is empty.";

            var root = MeshAnalysisTools.FindGameObject(avatarRootName);
            if (root == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var result = FindBodySmr(root);
            return FormatBodyResult(avatarRootName, result);
        }

        [AgentTool("アバター配下の Face SkinnedMeshRenderer を誤差ゼロで特定する。Tier1: viseme/ARKit/VRM の BlendShape 数 (最強シグナル), Tier2: 名前マッチ + 実体サイズガード, Tier3: Head 参照 + 低領域多様性スコアリング。Body SMR は除外される。",
            Author = "ajisaiflow",
            Category = "AvatarAnatomy",
            Risk = ToolRisk.Safe)]
        public static string IdentifyFaceSmr(string avatarRootName)
        {
            if (string.IsNullOrWhiteSpace(avatarRootName))
                return "Error: avatarRootName is empty.";

            var root = MeshAnalysisTools.FindGameObject(avatarRootName);
            if (root == null)
                return $"Error: GameObject '{avatarRootName}' not found.";

            var bodyResult = FindBodySmr(root);
            var faceResult = FindFaceSmr(root, bodyResult.Smr);
            return FormatFaceResult(avatarRootName, faceResult, bodyResult.Smr);
        }

        // ─────────────────────────────────────────────
        // Results
        // ─────────────────────────────────────────────

        private struct BodyResult
        {
            public SkinnedMeshRenderer Smr;
            public string Tier;
            public int Diversity;
            public int HumanBoneCount;
            public int VertexCount;
            public bool IsNonBodyName;
            public float YRange;
        }

        private struct FaceResult
        {
            public SkinnedMeshRenderer Smr;
            public string Tier;
            public int VisemeCount;
            public int Diversity;
            public int VertexCount;
            public int BlendShapeCount;
            public bool HasFaceName;
            public string Reason;
        }

        // ─────────────────────────────────────────────
        // Body detection
        // ─────────────────────────────────────────────

        private static BodyResult FindBodySmr(GameObject avatarRoot)
        {
            var result = new BodyResult { Tier = "none" };
            var allSmrs = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (allSmrs.Length == 0) return result;

            // Tier 1: name match (single "Body" excluding non-body keywords)
            SkinnedMeshRenderer singleBody = null;
            int bodyNameCount = 0;
            foreach (var smr in allSmrs)
            {
                if (smr.sharedMesh == null) continue;
                if (smr.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0
                    && !IsNonBodyName(smr.name))
                {
                    bodyNameCount++;
                    if (singleBody == null) singleBody = smr;
                }
            }
            if (bodyNameCount == 1 && singleBody != null)
            {
                result.Smr = singleBody;
                result.Tier = "name_match";
                result.VertexCount = singleBody.sharedMesh.vertexCount;
                return result;
            }

            // Tier 2: bone region diversity
            var animator = avatarRoot.GetComponent<Animator>();
            var humanBoneSet = new HashSet<Transform>();
            if (animator != null && animator.isHuman)
            {
                foreach (HumanBodyBones hb in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (hb == HumanBodyBones.LastBone) continue;
                    var t = animator.GetBoneTransform(hb);
                    if (t != null) humanBoneSet.Add(t);
                }
            }

            SkinnedMeshRenderer best = null;
            int bestDiv = -1, bestHuman = -1, bestVerts = 0;
            bool bestNonBody = true;
            foreach (var smr in allSmrs)
            {
                if (smr.sharedMesh == null) continue;
                var bones = smr.bones;
                if (bones == null || bones.Length == 0) continue;

                int diversity = (animator != null && animator.isHuman)
                    ? CountBoneRegionDiversity(bones, animator) : 0;

                int humanCount = 0;
                if (humanBoneSet.Count > 0)
                {
                    foreach (var b in bones)
                        if (b != null && humanBoneSet.Contains(b)) humanCount++;
                }

                int verts = smr.sharedMesh.vertexCount;
                bool nonBody = IsNonBodyName(smr.name);

                bool better = false;
                if (diversity > bestDiv) better = true;
                else if (diversity == bestDiv)
                {
                    if (bestNonBody && !nonBody) better = true;
                    else if (nonBody == bestNonBody)
                    {
                        if (humanCount > bestHuman) better = true;
                        else if (humanCount == bestHuman && verts > bestVerts) better = true;
                    }
                }

                if (better)
                {
                    best = smr; bestDiv = diversity; bestHuman = humanCount;
                    bestVerts = verts; bestNonBody = nonBody;
                }
            }

            if (best != null)
            {
                result.Smr = best;
                result.Tier = "bone_diversity";
                result.Diversity = bestDiv;
                result.HumanBoneCount = bestHuman;
                result.VertexCount = bestVerts;
                result.IsNonBodyName = bestNonBody;
                return result;
            }

            // Tier 3: widest Y-range fallback (non-humanoid)
            float widestY = 0;
            foreach (var smr in allSmrs)
            {
                if (smr.sharedMesh == null) continue;
                var verts = smr.sharedMesh.vertices;
                float yMin = float.MaxValue, yMax = float.MinValue;
                for (int i = 0; i < verts.Length; i++)
                {
                    if (verts[i].y < yMin) yMin = verts[i].y;
                    if (verts[i].y > yMax) yMax = verts[i].y;
                }
                float range = yMax - yMin;
                if (range > widestY)
                {
                    widestY = range;
                    result.Smr = smr;
                    result.Tier = "y_range_fallback";
                    result.YRange = range;
                    result.VertexCount = smr.sharedMesh.vertexCount;
                }
            }
            return result;
        }

        // ─────────────────────────────────────────────
        // Face detection
        // ─────────────────────────────────────────────

        private static FaceResult FindFaceSmr(GameObject avatarRoot, SkinnedMeshRenderer bodySmr)
        {
            var result = new FaceResult { Tier = "none" };
            var allSmrs = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (allSmrs.Length == 0) { result.Reason = "no_smr"; return result; }

            var animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) { result.Reason = "not_humanoid"; return result; }

            var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headBone == null) { result.Reason = "no_head_bone"; return result; }

            var headArea = new HashSet<Transform> { headBone };
            var neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
            var jawBone = animator.GetBoneTransform(HumanBodyBones.Jaw);
            if (neckBone != null) headArea.Add(neckBone);
            if (jawBone != null) headArea.Add(jawBone);

            // Tier 1: viseme BlendShape (strongest)
            SkinnedMeshRenderer singleViseme = null;
            int visemeCount = 0;
            int visemeMax = 0;
            foreach (var smr in allSmrs)
            {
                if (smr.sharedMesh == null || smr == bodySmr) continue;
                int vc = CountVisemeBlendShapes(smr.sharedMesh);
                if (vc >= MinVisemeCount)
                {
                    visemeCount++;
                    if (singleViseme == null) { singleViseme = smr; visemeMax = vc; }
                }
            }
            if (visemeCount == 1 && singleViseme != null && !HasAny(singleViseme.name, NonFaceKeywords))
            {
                result.Smr = singleViseme;
                result.Tier = "viseme_match";
                result.VisemeCount = visemeMax;
                result.VertexCount = singleViseme.sharedMesh.vertexCount;
                result.BlendShapeCount = singleViseme.sharedMesh.blendShapeCount;
                result.HasFaceName = HasAny(singleViseme.name, FaceKeywords);
                return result;
            }

            // Tier 2: name match with substantive-mesh guard
            SkinnedMeshRenderer singleFace = null;
            int faceNameCount = 0;
            foreach (var smr in allSmrs)
            {
                if (smr.sharedMesh == null || smr == bodySmr) continue;
                if (!HasAny(smr.name, FaceKeywords)) continue;
                if (HasAny(smr.name, NonFaceKeywords)) continue;
                if (!BonesTouch(smr.bones, headArea)) continue;

                int verts = smr.sharedMesh.vertexCount;
                int bs = smr.sharedMesh.blendShapeCount;
                if (verts < MinFaceVertCount && bs == 0) continue;

                faceNameCount++;
                if (singleFace == null) singleFace = smr;
            }
            if (faceNameCount == 1 && singleFace != null)
            {
                result.Smr = singleFace;
                result.Tier = "name_match";
                result.VisemeCount = CountVisemeBlendShapes(singleFace.sharedMesh);
                result.VertexCount = singleFace.sharedMesh.vertexCount;
                result.BlendShapeCount = singleFace.sharedMesh.blendShapeCount;
                result.HasFaceName = true;
                return result;
            }

            // Tier 3: Head-bone referencing with low region diversity
            SkinnedMeshRenderer best = null;
            bool bestFaceName = false;
            int bestVisemes = -1;
            int bestVerts = 0;
            int bestDiv = 0;
            int bestBs = 0;
            foreach (var smr in allSmrs)
            {
                if (smr.sharedMesh == null || smr == bodySmr) continue;
                var bones = smr.bones;
                if (bones == null || bones.Length == 0) continue;
                if (!BonesTouch(bones, headArea)) continue;

                int diversity = CountBoneRegionDiversity(bones, animator);
                if (diversity > MaxFaceDiversity) continue;

                if (HasAny(smr.name, NonFaceKeywords)) continue;

                int verts = smr.sharedMesh.vertexCount;
                int bs = smr.sharedMesh.blendShapeCount;
                if (verts < MinFaceVertCount && bs == 0) continue;

                bool faceName = HasAny(smr.name, FaceKeywords);
                int visemes = CountVisemeBlendShapes(smr.sharedMesh);

                bool better = false;
                if (best == null) better = true;
                else if (faceName && !bestFaceName) better = true;
                else if (faceName == bestFaceName)
                {
                    if (visemes > bestVisemes) better = true;
                    else if (visemes == bestVisemes && verts > bestVerts) better = true;
                }

                if (better)
                {
                    best = smr;
                    bestFaceName = faceName;
                    bestVisemes = visemes;
                    bestVerts = verts;
                    bestDiv = diversity;
                    bestBs = bs;
                }
            }

            if (best != null)
            {
                result.Smr = best;
                result.Tier = "bone_heuristic";
                result.VisemeCount = bestVisemes;
                result.VertexCount = bestVerts;
                result.Diversity = bestDiv;
                result.BlendShapeCount = bestBs;
                result.HasFaceName = bestFaceName;
                return result;
            }

            result.Reason = "no_candidate";
            return result;
        }

        // ─────────────────────────────────────────────
        // Helpers (algorithm)
        // ─────────────────────────────────────────────

        private static readonly string[] NonBodyKeywords =
        {
            "Hair", "Face", "Eye", "Teeth", "Tongue", "Ear",
            "Cloth", "Costume", "Accessory", "Acc_",
        };

        private static readonly string[] FaceKeywords =
        {
            "Face", "Head", "Kao", "顔",
        };

        private static readonly string[] NonFaceKeywords =
        {
            "Hair", "Eye", "Teeth", "Tongue", "Ear",
            "Hat", "Helmet", "Glasses", "Mask", "Horn",
            "Accessory", "Acc_",
        };

        private static readonly string[] VisemePrefixes =
        {
            "vrc.v_", "Viseme_", "Fcl_",
        };

        private const int MinVisemeCount = 3;
        private const int MinFaceVertCount = 500;
        private const int MaxFaceDiversity = 2;

        private static bool IsNonBodyName(string n)
        {
            foreach (var kw in NonBodyKeywords)
                if (n.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool HasAny(string n, string[] keywords)
        {
            foreach (var kw in keywords)
                if (n.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool BonesTouch(Transform[] bones, HashSet<Transform> target)
        {
            if (bones == null) return false;
            foreach (var b in bones)
                if (b != null && target.Contains(b)) return true;
            return false;
        }

        private static int CountVisemeBlendShapes(Mesh mesh)
        {
            if (mesh == null) return 0;
            int count = 0;
            int total = mesh.blendShapeCount;
            for (int i = 0; i < total; i++)
            {
                var n = mesh.GetBlendShapeName(i);
                foreach (var p in VisemePrefixes)
                {
                    if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { count++; break; }
                }
            }
            return count;
        }

        private static int CountBoneRegionDiversity(Transform[] smrBones, Animator animator)
        {
            if (animator == null || !animator.isHuman) return 0;
            var boneToRegion = new Dictionary<Transform, int>();

            void Add(HumanBodyBones hb, int region)
            {
                var t = animator.GetBoneTransform(hb);
                if (t != null && !boneToRegion.ContainsKey(t)) boneToRegion[t] = region;
            }

            Add(HumanBodyBones.Head, 0); Add(HumanBodyBones.Neck, 0);
            Add(HumanBodyBones.Jaw, 0); Add(HumanBodyBones.LeftEye, 0); Add(HumanBodyBones.RightEye, 0);
            Add(HumanBodyBones.Spine, 1); Add(HumanBodyBones.Chest, 1); Add(HumanBodyBones.UpperChest, 1);
            Add(HumanBodyBones.Hips, 2);
            Add(HumanBodyBones.LeftShoulder, 3); Add(HumanBodyBones.LeftUpperArm, 3);
            Add(HumanBodyBones.LeftLowerArm, 3); Add(HumanBodyBones.LeftHand, 3);
            Add(HumanBodyBones.RightShoulder, 4); Add(HumanBodyBones.RightUpperArm, 4);
            Add(HumanBodyBones.RightLowerArm, 4); Add(HumanBodyBones.RightHand, 4);
            Add(HumanBodyBones.LeftUpperLeg, 5); Add(HumanBodyBones.LeftLowerLeg, 5);
            Add(HumanBodyBones.LeftFoot, 5); Add(HumanBodyBones.LeftToes, 5);
            Add(HumanBodyBones.RightUpperLeg, 6); Add(HumanBodyBones.RightLowerLeg, 6);
            Add(HumanBodyBones.RightFoot, 6); Add(HumanBodyBones.RightToes, 6);

            var covered = new HashSet<int>();
            foreach (var b in smrBones)
            {
                if (b == null) continue;
                if (boneToRegion.TryGetValue(b, out var r)) covered.Add(r);
            }
            return covered.Count;
        }

        private static string HierarchyPath(GameObject go)
        {
            if (go == null) return string.Empty;
            var stack = new Stack<string>();
            var t = go.transform;
            while (t != null) { stack.Push(t.name); t = t.parent; }
            return string.Join("/", stack);
        }

        // ─────────────────────────────────────────────
        // JSON formatting
        // ─────────────────────────────────────────────

        private static string FormatBodyResult(string avatarRoot, BodyResult r)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,");
            sb.Append("\"avatar\":\"").Append(Esc(avatarRoot)).Append("\",");
            sb.Append("\"found\":").Append(r.Smr != null ? "true" : "false").Append(',');
            if (r.Smr != null)
            {
                sb.Append("\"path\":\"").Append(Esc(HierarchyPath(r.Smr.gameObject))).Append("\",");
                sb.Append("\"name\":\"").Append(Esc(r.Smr.name)).Append("\",");
            }
            sb.Append("\"tier\":\"").Append(r.Tier).Append("\",");
            sb.Append("\"diagnostics\":{");
            sb.Append("\"diversity\":").Append(r.Diversity).Append(',');
            sb.Append("\"humanBoneCount\":").Append(r.HumanBoneCount).Append(',');
            sb.Append("\"vertexCount\":").Append(r.VertexCount).Append(',');
            sb.Append("\"isNonBodyName\":").Append(r.IsNonBodyName ? "true" : "false").Append(',');
            sb.Append("\"yRange\":").Append(r.YRange.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append("}}");
            return sb.ToString();
        }

        private static string FormatFaceResult(string avatarRoot, FaceResult r, SkinnedMeshRenderer bodySmr)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true,");
            sb.Append("\"avatar\":\"").Append(Esc(avatarRoot)).Append("\",");
            sb.Append("\"found\":").Append(r.Smr != null ? "true" : "false").Append(',');
            if (r.Smr != null)
            {
                sb.Append("\"path\":\"").Append(Esc(HierarchyPath(r.Smr.gameObject))).Append("\",");
                sb.Append("\"name\":\"").Append(Esc(r.Smr.name)).Append("\",");
            }
            sb.Append("\"tier\":\"").Append(r.Tier).Append("\",");
            if (!string.IsNullOrEmpty(r.Reason))
                sb.Append("\"reason\":\"").Append(Esc(r.Reason)).Append("\",");
            sb.Append("\"bodySmr\":");
            if (bodySmr != null)
                sb.Append('"').Append(Esc(HierarchyPath(bodySmr.gameObject))).Append('"');
            else sb.Append("null");
            sb.Append(',');
            sb.Append("\"diagnostics\":{");
            sb.Append("\"visemeCount\":").Append(r.VisemeCount).Append(',');
            sb.Append("\"diversity\":").Append(r.Diversity).Append(',');
            sb.Append("\"vertexCount\":").Append(r.VertexCount).Append(',');
            sb.Append("\"blendShapeCount\":").Append(r.BlendShapeCount).Append(',');
            sb.Append("\"hasFaceName\":").Append(r.HasFaceName ? "true" : "false");
            sb.Append("}}");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append($"\\u{(int)ch:X4}");
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using System.Text;
using System.Collections.Generic;
using System.Linq;

using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.MA;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class MeshAnalysisTools
    {
        private struct FingerCrossSectionInfo
        {
            public Vector3 center;
            public float radius;
            public int vertexCount;
        }

        [AgentTool("Analyze mesh bounds to understand its orientation. Shows extents along each axis and identifies the shortest axis (hole axis for ring-like objects).")]
        public static string AnalyzeMeshBounds(string gameObjectName)
        {
            var go = FindGameObject(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var allVerticesLocal = CollectMeshVerticesLocal(go);
            if (allVerticesLocal.Count == 0)
                return $"Error: No mesh found on '{gameObjectName}' or its children.";

            Vector3 min = allVerticesLocal[0], max = allVerticesLocal[0];
            foreach (var v in allVerticesLocal)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            Vector3 center = (min + max) / 2f;
            Vector3 extents = max - min;

            Vector3 holeAxis;
            string shortestAxis;
            FindHoleAxis(extents, out holeAxis, out shortestAxis);

            float innerRadius = MeasureRingInnerRadius(allVerticesLocal, holeAxis, ComputeMedianCenter(allVerticesLocal));

            var sb = new StringBuilder();
            sb.AppendLine($"Mesh Analysis for '{gameObjectName}':");
            sb.AppendLine($"  Vertices: {allVerticesLocal.Count}");
            sb.AppendLine($"  Bounds Min (local): ({min.x:F4}, {min.y:F4}, {min.z:F4})");
            sb.AppendLine($"  Bounds Max (local): ({max.x:F4}, {max.y:F4}, {max.z:F4})");
            sb.AppendLine($"  Center (local): ({center.x:F4}, {center.y:F4}, {center.z:F4})");
            sb.AppendLine($"  Extents: X={extents.x:F4}, Y={extents.y:F4}, Z={extents.z:F4}");
            sb.AppendLine($"  Shortest Axis: {shortestAxis} (hole axis for ring-like objects)");
            sb.AppendLine($"  Estimated Inner Radius: {innerRadius:F4}");

            if (go.transform.parent != null)
            {
                var bone = go.transform.parent;
                sb.AppendLine($"  Parent Bone: {bone.name}");

                Transform nextBone = FindNextBoneInChain(bone, go.transform);
                if (nextBone != null)
                {
                    Vector3 boneDir = bone.InverseTransformPoint(nextBone.position).normalized;
                    sb.AppendLine($"  Bone Direction (to '{nextBone.name}'): ({boneDir.x:F4}, {boneDir.y:F4}, {boneDir.z:F4})");
                }
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Align and scale a ring to fit a bone. The ring must already be in the scene (use InstantiatePrefab first) and have MA Bone Proxy set (use AttachRingWithBoneProxy first). Only takes ringName and positionAlongBone (0-1, default 0.3). Do NOT pass a bone name.")]
        public static string AlignRingToBone(string ringName, float positionAlongBone = 0.3f)
        {
            var ring = FindGameObject(ringName);
            if (ring == null) return $"Error: Ring '{ringName}' not found.";

            // Get bone from BoneProxy or parent
            Transform bone;
            var bpTarget = MAComponentFactory.GetBoneProxyTarget(ring);
            if (bpTarget != null)
                bone = bpTarget;
            else
                bone = ring.transform.parent;
            if (bone == null) return "Error: Ring has no MA Bone Proxy and no parent. Use AttachRingWithBoneProxy first.";
            bool isDirectChild = (ring.transform.parent == bone);

            // Collect ring mesh vertices in ring's local space
            var allVertices = CollectMeshVerticesLocal(ring);
            if (allVertices.Count == 0) return "Error: No mesh vertices found on ring or its children.";

            // Compute bounding box
            Vector3 min = allVertices[0], max = allVertices[0];
            foreach (var v in allVertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            Vector3 extents = max - min;

            // Compute mesh center using median (robust to asymmetric decoration)
            Vector3 meshCenter = ComputeMedianCenter(allVertices);

            // Find hole axis by measuring inner radius for each candidate
            // The true hole axis gives the largest inner radius (donut cross-section)
            float radiusX = MeasureRingInnerRadius(allVertices, Vector3.right, meshCenter);
            float radiusY = MeasureRingInnerRadius(allVertices, Vector3.up, meshCenter);
            float radiusZ = MeasureRingInnerRadius(allVertices, Vector3.forward, meshCenter);

            Vector3 holeAxis;
            string holeAxisName;
            float ringInnerRadius;
            if (radiusX >= radiusY && radiusX >= radiusZ)
            { holeAxis = Vector3.right; holeAxisName = "X"; ringInnerRadius = radiusX; }
            else if (radiusY >= radiusX && radiusY >= radiusZ)
            { holeAxis = Vector3.up; holeAxisName = "Y"; ringInnerRadius = radiusY; }
            else
            { holeAxis = Vector3.forward; holeAxisName = "Z"; ringInnerRadius = radiusZ; }

            // Find next bone in chain
            Transform nextBone = FindNextBoneInChain(bone, ring.transform);
            if (nextBone == null)
                return "Error: Could not determine bone direction (no sibling bone found in parent).";

            // Use bone local space for direction and length
            Vector3 fingerDirLocal = bone.InverseTransformPoint(nextBone.position).normalized;
            float boneLength = bone.InverseTransformPoint(nextBone.position).magnitude;

            // Analyze finger cross-section using bone weights
            FingerCrossSectionInfo fingerInfo;
            bool fingerAnalyzed = TryAnalyzeFingerCrossSection(
                bone, nextBone, ring.transform,
                positionAlongBone,
                out fingerInfo);

            // Compute scale factor
            float scaleFactor = 1.0f;
            string scaleInfo;
            if (fingerAnalyzed && ringInnerRadius > 0.0001f)
            {
                scaleFactor = (fingerInfo.radius * 0.92f) / ringInnerRadius;
                scaleFactor = Mathf.Clamp(scaleFactor, 0.1f, 10.0f);
                scaleInfo = $"Finger radius: {fingerInfo.radius:F4}, Ring inner radius: {ringInnerRadius:F4}, Scale: {scaleFactor:F3}";
            }
            else
            {
                scaleInfo = $"Could not auto-scale (finger vertices: {fingerInfo.vertexCount}, ring inner radius: {ringInnerRadius:F4}).";
            }

            // Compute alignment rotation (in bone local space)
            Vector3 upHint = Vector3.Cross(Vector3.right, fingerDirLocal);
            if (upHint.sqrMagnitude < 0.001f)
                upHint = Vector3.Cross(Vector3.forward, fingerDirLocal);
            upHint.Normalize();

            Vector3 ringUp;
            if (holeAxisName == "X")
                ringUp = extents.y >= extents.z ? Vector3.up : Vector3.forward;
            else if (holeAxisName == "Y")
                ringUp = extents.x >= extents.z ? Vector3.right : Vector3.forward;
            else
                ringUp = extents.x >= extents.y ? Vector3.right : Vector3.up;

            Quaternion alignRotation = Quaternion.LookRotation(fingerDirLocal, upHint)
                                     * Quaternion.Inverse(Quaternion.LookRotation(holeAxis, ringUp));

            // Compute position in bone local space
            Vector3 localPos = fingerDirLocal * boneLength * Mathf.Clamp01(positionAlongBone)
                             - (alignRotation * (meshCenter * scaleFactor));

            // Perpendicular adjustment using finger mesh center
            string fingerCenterInfo;
            if (fingerAnalyzed)
            {
                Vector3 fingerCenterLocal = bone.InverseTransformPoint(fingerInfo.center);
                float fcAlong = Vector3.Dot(fingerCenterLocal, fingerDirLocal);
                Vector3 fcPerp = fingerCenterLocal - fingerDirLocal * fcAlong;

                float rlAlong = Vector3.Dot(localPos, fingerDirLocal);
                Vector3 rlPerp = localPos - fingerDirLocal * rlAlong;
                Vector3 perpAdjustment = fcPerp - rlPerp;

                float maxPerpShift = boneLength * 0.3f;
                if (perpAdjustment.magnitude > maxPerpShift)
                    perpAdjustment = perpAdjustment.normalized * maxPerpShift;

                localPos += perpAdjustment;
                fingerCenterInfo = $"Finger center applied ({fingerInfo.vertexCount} bone-weighted vertices, perp shift: {perpAdjustment.magnitude:F4}).";
            }
            else
            {
                fingerCenterInfo = "Could not find finger mesh via bone weights. Used bone-centered placement.";
            }

            // Safety: clamp
            float maxLocalDist = boneLength * 1.5f;
            if (localPos.magnitude > maxLocalDist)
                localPos = localPos.normalized * maxLocalDist;

            // Apply transforms
            Undo.RecordObject(ring.transform, "Align Ring to Bone");
            ring.transform.localScale = Vector3.one * scaleFactor;
            if (isDirectChild)
            {
                ring.transform.localRotation = alignRotation;
                ring.transform.localPosition = localPos;
            }
            else
            {
                // BoneProxy case: ring is under avatar root, apply in world space
                ring.transform.rotation = bone.rotation * alignRotation;
                ring.transform.position = bone.TransformPoint(localPos);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Success: Aligned and scaled ring '{ring.name}' to bone '{bone.name}'.");
            sb.AppendLine($"  Mesh extents: X={extents.x:F4}, Y={extents.y:F4}, Z={extents.z:F4}");
            sb.AppendLine($"  Hole axis: {holeAxisName}");
            sb.AppendLine($"  Bone length (local): {boneLength:F4}, ring at {positionAlongBone:P0} along bone");
            sb.AppendLine($"  {scaleInfo}");
            sb.AppendLine($"  Applied rotation: ({ring.transform.eulerAngles.x:F1}, {ring.transform.eulerAngles.y:F1}, {ring.transform.eulerAngles.z:F1})");
            sb.AppendLine($"  Applied scale: {scaleFactor:F3}");
            sb.AppendLine($"  Applied position: ({ring.transform.position.x:F4}, {ring.transform.position.y:F4}, {ring.transform.position.z:F4})");
            sb.AppendLine($"  {fingerCenterInfo}");

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Move a ring incrementally along its parent bone direction. Positive = toward fingertip, negative = toward base. Units are in bone local space. Works with both direct parenting and MA Bone Proxy.")]
        public static string NudgeRing(string ringName, float alongBone = 0f)
        {
            var ring = FindGameObject(ringName);
            if (ring == null) return $"Error: Ring '{ringName}' not found.";

            Transform bone, nextBone;
            Vector3 fingerDirLocal;
            if (!TryGetBoneContext(ring.transform, out bone, out nextBone, out fingerDirLocal))
                return "Error: Could not determine bone context (no parent bone or MA Bone Proxy found).";

            Undo.RecordObject(ring.transform, "Nudge Ring");
            if (ring.transform.parent == bone)
                ring.transform.localPosition += fingerDirLocal * alongBone;
            else
                ring.transform.position += bone.TransformVector(fingerDirLocal * alongBone);

            var pos = ring.transform.localPosition;
            return $"Nudged '{ringName}' by {alongBone:F4} along bone '{bone.name}' direction.\n  New localPosition: ({pos.x:F4}, {pos.y:F4}, {pos.z:F4})";
        }

        [AgentTool("Multiply the current scale of a ring by a factor. 0.9 = shrink 10%, 1.1 = enlarge 10%.")]
        public static string AdjustRingScale(string ringName, float multiplier = 1.0f)
        {
            if (multiplier <= 0f) return "Error: multiplier must be positive.";

            var ring = FindGameObject(ringName);
            if (ring == null) return $"Error: Ring '{ringName}' not found.";

            Undo.RecordObject(ring.transform, "Adjust Ring Scale");
            ring.transform.localScale *= multiplier;

            float s = ring.transform.localScale.x;
            return $"Scaled '{ringName}' by {multiplier:F3}.\n  New localScale: ({s:F4}, {s:F4}, {s:F4})";
        }

        [AgentTool("Rotate a ring around its bone axis by the specified degrees. Use this to adjust which direction the decorative side faces (e.g. 90 or 180).")]
        public static string RotateRing(string ringName, float degrees = 0f)
        {
            var ring = FindGameObject(ringName);
            if (ring == null) return $"Error: Ring '{ringName}' not found.";

            Transform bone, nextBone;
            Vector3 fingerDirLocal;
            if (!TryGetBoneContext(ring.transform, out bone, out nextBone, out fingerDirLocal))
                return "Error: Could not determine bone context (no parent bone or MA Bone Proxy found).";

            Undo.RecordObject(ring.transform, "Rotate Ring");
            if (ring.transform.parent == bone)
            {
                ring.transform.localRotation *= Quaternion.AngleAxis(degrees, fingerDirLocal);
            }
            else
            {
                // BoneProxy case: rotate in world space around bone direction
                Vector3 worldAxis = (nextBone.position - bone.position).normalized;
                ring.transform.RotateAround(ring.transform.position, worldAxis, degrees);
            }

            var euler = ring.transform.eulerAngles;
            return $"Rotated '{ring.name}' by {degrees:F1}\u00b0 around bone axis.\n  New rotation: ({euler.x:F1}, {euler.y:F1}, {euler.z:F1})";
        }

        [AgentTool("Attach an existing ring GameObject to a bone using MA Bone Proxy (non-destructive). The ring must already be instantiated in the scene (use InstantiatePrefab first). The ring is placed under the avatar root and MA Bone Proxy handles the bone attachment at build time. Do NOT pass a prefab asset path. ringName = name of the ring already in scene, boneName = name or path of the target bone.")]
        public static string AttachRingWithBoneProxy(string ringName, string boneName)
        {
            var maErr = MAAvailability.CheckOrError();
            if (maErr != null) return maErr;

            var ring = FindGameObject(ringName);
            if (ring == null) return $"Error: Ring '{ringName}' not found in scene. Use InstantiatePrefab to add it first.";

            // Safety: ring must have mesh components (not an avatar root or bone)
            if (ring.GetComponentInChildren<MeshFilter>() == null
                && ring.GetComponentInChildren<MeshRenderer>() == null
                && ring.GetComponentInChildren<SkinnedMeshRenderer>() == null)
                return $"Error: '{ringName}' has no mesh. This does not appear to be a ring. Did you pass the avatar name instead of the ring name?";

            var boneGo = FindGameObject(boneName);
            if (boneGo == null) return $"Error: Bone '{boneName}' not found.";
            Transform bone = boneGo.transform;

            // Safety: prevent circular parenting
            if (bone.IsChildOf(ring.transform))
                return $"Error: '{ringName}' is an ancestor of bone '{boneName}'. You passed the avatar root or a parent object instead of the ring.";

            // Place ring under avatar root (NOT under the bone)
            // MA Bone Proxy will handle reparenting at build time
            Transform avatarRoot = bone.root;
            if (ring.transform.parent != avatarRoot)
                Undo.SetTransformParent(ring.transform, avatarRoot, "Place Ring Under Avatar Root");

            // Add or update MA Bone Proxy via SDK
            MAComponentFactory.AddBoneProxy(ring, bone);

            // Position ring at bone for visual reference
            Undo.RecordObject(ring.transform, "Position Ring at Bone");
            ring.transform.position = bone.position;
            ring.transform.rotation = bone.rotation;

            return $"Attached '{ring.name}' to bone '{bone.name}' with MA Bone Proxy.\n  Ring placed under avatar root '{avatarRoot.name}'.\n  Attachment mode: AsChildAtRoot (non-destructive build).\n  Use AlignRingToBone to position precisely.";
        }

        // ========== Object Lookup ==========

        internal static GameObject FindGameObject(string name)
        {
            // 1. Fast path: active objects (supports full paths from root)
            var go = GameObject.Find(name);
            if (go != null) return go;

            // 2. Search from scene roots using Transform.Find (finds inactive objects)
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var t = root.transform.Find(name);
                    if (t != null) return t.gameObject;
                }
            }

            // 3. Partial path: find first segment anywhere in hierarchy, then traverse rest
            int slash = name.IndexOf('/');
            if (slash >= 0)
            {
                string head = name.Substring(0, slash);
                string rest = name.Substring(slash + 1);
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded) continue;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        {
                            if (t.name == head)
                            {
                                var child = t.Find(rest);
                                if (child != null) return child.gameObject;
                            }
                        }
                    }
                }
            }

            // 4. Leaf name fallback: search all objects including inactive
            string leafName = slash >= 0 ? name.Substring(name.LastIndexOf('/') + 1) : name;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == leafName) return t.gameObject;
                    }
                }
            }

            // 5. Case-insensitive leaf name fallback
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (string.Equals(t.name, leafName, System.StringComparison.OrdinalIgnoreCase))
                            return t.gameObject;
                    }
                }
            }

            return null;
        }

        // ========== Bone Context Helper ==========

        internal static bool TryGetBoneContext(Transform ring, out Transform bone, out Transform nextBone, out Vector3 fingerDirLocal)
        {
            bone = null;
            nextBone = null;
            fingerDirLocal = Vector3.zero;

            // Try MA Bone Proxy first
            var bpTarget = MAComponentFactory.GetBoneProxyTarget(ring.gameObject);
            if (bpTarget != null)
                bone = bpTarget;
            else if (ring.parent != null)
                bone = ring.parent;

            if (bone == null) return false;

            nextBone = FindNextBoneInChain(bone, ring);
            if (nextBone == null) return false;

            Vector3 toNext = bone.InverseTransformPoint(nextBone.position);
            if (toNext.magnitude < 0.0001f) return false;

            fingerDirLocal = toNext.normalized;
            return true;
        }

        // ========== Ring Measurement ==========

        /// <summary>
        /// Measure the inner radius of a ring mesh by computing
        /// perpendicular distances from the hole axis and taking the 5th percentile.
        /// </summary>
        private static float MeasureRingInnerRadius(List<Vector3> vertices, Vector3 holeAxis, Vector3 meshCenter)
        {
            var distances = new List<float>(vertices.Count);
            foreach (var v in vertices)
            {
                Vector3 diff = v - meshCenter;
                float alongHole = Vector3.Dot(diff, holeAxis);
                Vector3 perp = diff - holeAxis * alongHole;
                distances.Add(perp.magnitude);
            }
            distances.Sort();

            // 5th percentile = inner ring edge (robust to isolated center vertices)
            int idx = Mathf.Max(0, (int)(distances.Count * 0.05f));
            return distances[idx];
        }

        // ========== Finger Measurement ==========

        /// <summary>
        /// Analyze the finger mesh cross-section at the ring position using bone weights.
        /// Phase 1: Collect vertices weighted to the target bone (most accurate).
        /// Phase 2: Fallback to spatial search if bone weights don't yield enough vertices.
        /// Returns the center and effective radius of the finger at the ring position.
        /// </summary>
        private static bool TryAnalyzeFingerCrossSection(
            Transform bone, Transform nextBone, Transform ring,
            float positionAlongBone,
            out FingerCrossSectionInfo info)
        {
            info = new FingerCrossSectionInfo { center = bone.position, radius = 0f, vertexCount = 0 };

            var avatarRoot = bone.root;
            var smrs = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>();

            Vector3 worldFingerDir = (nextBone.position - bone.position).normalized;
            float worldBoneLength = Vector3.Distance(bone.position, nextBone.position);
            Vector3 searchCenter = bone.position + worldFingerDir * worldBoneLength * positionAlongBone;

            var fingerVertices = new List<Vector3>();
            float sliceThickness = worldBoneLength * 0.25f;
            string method = "bone-weight";

            // Phase 1: Bone weight-based collection (accurate)
            foreach (var smr in smrs)
            {
                if (!smr.gameObject.activeInHierarchy) continue;
                if (smr.sharedMesh == null) continue;
                if (smr.transform.IsChildOf(ring)) continue;

                // Find the target bone's index in this SMR's bone array
                int boneIdx = FindBoneIndex(smr, bone);
                if (boneIdx < 0) continue;

                var bakedMesh = new Mesh();
                smr.BakeMesh(bakedMesh);
                var bakedVerts = bakedMesh.vertices;
                var boneWeights = smr.sharedMesh.boneWeights;

                int count = Mathf.Min(bakedVerts.Length, boneWeights.Length);
                for (int i = 0; i < count; i++)
                {
                    // Check if this vertex is weighted to our target bone
                    var bw = boneWeights[i];
                    float weight = 0f;
                    if (bw.boneIndex0 == boneIdx) weight += bw.weight0;
                    if (bw.boneIndex1 == boneIdx) weight += bw.weight1;
                    if (bw.boneIndex2 == boneIdx) weight += bw.weight2;
                    if (bw.boneIndex3 == boneIdx) weight += bw.weight3;

                    if (weight < 0.1f) continue; // Not significantly weighted to this bone

                    Vector3 worldV = smr.transform.TransformPoint(bakedVerts[i]);

                    // Filter by position along bone (keep only the slice at ring position)
                    Vector3 diff = worldV - searchCenter;
                    float distAlong = Vector3.Dot(diff, worldFingerDir);

                    if (Mathf.Abs(distAlong) < sliceThickness)
                    {
                        fingerVertices.Add(worldV);
                    }
                }

                Object.DestroyImmediate(bakedMesh);
            }

            // Phase 2: Spatial fallback if bone weights didn't find enough
            if (fingerVertices.Count < 6)
            {
                fingerVertices.Clear();
                method = "spatial-fallback";
                float spatialRadius = worldBoneLength * 0.4f;
                float spatialSlice = worldBoneLength * 0.15f;

                foreach (var smr in smrs)
                {
                    if (!smr.gameObject.activeInHierarchy) continue;
                    if (smr.sharedMesh == null) continue;
                    if (smr.transform.IsChildOf(ring)) continue;

                    var bakedMesh = new Mesh();
                    smr.BakeMesh(bakedMesh);
                    var verts = bakedMesh.vertices;

                    foreach (var v in verts)
                    {
                        Vector3 worldV = smr.transform.TransformPoint(v);
                        Vector3 diff = worldV - searchCenter;
                        float distAlong = Vector3.Dot(diff, worldFingerDir);
                        Vector3 perp = diff - worldFingerDir * distAlong;

                        if (Mathf.Abs(distAlong) < spatialSlice && perp.magnitude < spatialRadius)
                        {
                            fingerVertices.Add(worldV);
                        }
                    }

                    Object.DestroyImmediate(bakedMesh);
                }
            }

            info.vertexCount = fingerVertices.Count;
            if (fingerVertices.Count < 6) return false;

            // Compute centroid using median (robust to outliers)
            var wxs = fingerVertices.Select(v => v.x).OrderBy(x => x).ToList();
            var wys = fingerVertices.Select(v => v.y).OrderBy(y => y).ToList();
            var wzs = fingerVertices.Select(v => v.z).OrderBy(z => z).ToList();
            int mid = fingerVertices.Count / 2;
            info.center = new Vector3(wxs[mid], wys[mid], wzs[mid]);

            // Compute perpendicular distances from centroid (finger radii)
            var radii = new List<float>(fingerVertices.Count);
            foreach (var v in fingerVertices)
            {
                Vector3 diff = v - info.center;
                float along = Vector3.Dot(diff, worldFingerDir);
                Vector3 perp = diff - worldFingerDir * along;
                radii.Add(perp.magnitude);
            }
            radii.Sort();

            // Use 70th percentile as effective finger radius
            // Conservative to avoid overestimation from knuckle/joint vertices
            int idx70 = Mathf.Min((int)(radii.Count * 0.70f), radii.Count - 1);
            info.radius = radii[idx70];

            return true;
        }

        /// <summary>
        /// Find the index of a bone Transform in a SkinnedMeshRenderer's bones array.
        /// </summary>
        private static int FindBoneIndex(SkinnedMeshRenderer smr, Transform bone)
        {
            var bones = smr.bones;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == bone) return i;
            }
            return -1;
        }

        // ========== Helpers ==========

        private static void FindHoleAxis(Vector3 extents, out Vector3 holeAxis, out string holeAxisName)
        {
            if (extents.x <= extents.y && extents.x <= extents.z)
            {
                holeAxis = Vector3.right;
                holeAxisName = "X";
            }
            else if (extents.y <= extents.x && extents.y <= extents.z)
            {
                holeAxis = Vector3.up;
                holeAxisName = "Y";
            }
            else
            {
                holeAxis = Vector3.forward;
                holeAxisName = "Z";
            }
        }

        internal static List<Vector3> CollectMeshVerticesLocal(GameObject go)
        {
            var result = new List<Vector3>();

            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                    result.Add(go.transform.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }

            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;
                foreach (var v in smr.sharedMesh.vertices)
                    result.Add(go.transform.InverseTransformPoint(smr.transform.TransformPoint(v)));
            }

            return result;
        }

        internal static Vector3 ComputeMedianCenter(List<Vector3> vertices)
        {
            int mid = vertices.Count / 2;
            var xs = vertices.Select(v => v.x).OrderBy(x => x).ToList();
            var ys = vertices.Select(v => v.y).OrderBy(y => y).ToList();
            var zs = vertices.Select(v => v.z).OrderBy(z => z).ToList();
            return new Vector3(xs[mid], ys[mid], zs[mid]);
        }

        internal static Transform FindNextBoneInChain(Transform bone, Transform excludeTransform)
        {
            for (int i = 0; i < bone.childCount; i++)
            {
                var child = bone.GetChild(i);
                if (child != excludeTransform)
                    return child;
            }
            return null;
        }
    }
}

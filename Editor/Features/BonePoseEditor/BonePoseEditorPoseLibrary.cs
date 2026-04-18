using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor
{
    [System.Serializable]
    public class BoneRotationEntry
    {
        public HumanBodyBones Bone;
        public Quaternion Rotation;
    }

    [System.Serializable]
    public class PoseEntry
    {
        public string Name;
        public string Category = "";
        public string CreatedDate;
        public byte[] ThumbnailPNG;
        public List<BoneRotationEntry> Rotations = new List<BoneRotationEntry>();
    }

    [CreateAssetMenu(menuName = "\u7d2b\u967d\u82b1\u5e83\u5834/Pose Library")]
    public class PoseLibraryAsset : ScriptableObject
    {
        public List<PoseEntry> Poses = new List<PoseEntry>();
    }

    public static class PoseLibraryOperations
    {
        public static void SavePose(PoseLibraryAsset lib, string name,
            Dictionary<HumanBodyBones, Transform> bones,
            HashSet<HumanBodyBones> selectedBones = null,
            string category = "")
        {
            if (lib == null) return;

            Undo.RecordObject(lib, "Save Pose");

            var entry = new PoseEntry
            {
                Name = name,
                Category = category ?? "",
                CreatedDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                ThumbnailPNG = GenerateThumbnail(SceneView.lastActiveSceneView),
            };

            foreach (var kvp in bones)
            {
                if (selectedBones != null && !selectedBones.Contains(kvp.Key))
                    continue;

                entry.Rotations.Add(new BoneRotationEntry
                {
                    Bone = kvp.Key,
                    Rotation = kvp.Value.localRotation,
                });
            }

            lib.Poses.Add(entry);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
        }

        public static byte[] GenerateThumbnail(SceneView view, int size = 128)
        {
            if (view == null || view.camera == null) return null;

            var rt = new RenderTexture(size, size, 24);
            var prevRT = view.camera.targetTexture;

            try
            {
                view.camera.targetTexture = rt;
                view.camera.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();

                var png = tex.EncodeToPNG();
                Object.DestroyImmediate(tex);
                return png;
            }
            finally
            {
                view.camera.targetTexture = prevRT;
                RenderTexture.active = null;
                Object.DestroyImmediate(rt);
            }
        }

        public static void ApplyPose(PoseEntry entry,
            Dictionary<HumanBodyBones, Transform> bones,
            float weight = 1f, HashSet<int> groupMask = null)
        {
            if (entry == null) return;

            foreach (var rot in entry.Rotations)
            {
                if (!bones.TryGetValue(rot.Bone, out var t)) continue;
                if (groupMask != null && !IsBoneInGroupMask(rot.Bone, groupMask))
                    continue;

                Undo.RecordObject(t, "Apply Pose");
                if (weight >= 0.999f)
                    t.localRotation = rot.Rotation;
                else
                    t.localRotation = Quaternion.Slerp(
                        t.localRotation, rot.Rotation, weight);
            }
        }

        public static void ApplyPoseMirrored(PoseEntry entry,
            Dictionary<HumanBodyBones, Transform> bones,
            float weight = 1f)
        {
            if (entry == null) return;

            foreach (var rot in entry.Rotations)
            {
                // Find mirror bone
                var mirrorBone = BonePoseEditorState.GetMirrorBone(rot.Bone);
                HumanBodyBones targetBone;
                if ((int)mirrorBone >= 0)
                    targetBone = mirrorBone;
                else
                    targetBone = rot.Bone; // center bone: apply mirrored rotation

                if (!bones.TryGetValue(targetBone, out var t)) continue;

                var mirroredRot = BonePoseEditorState.ComputeMirrorRotation(rot.Rotation);

                Undo.RecordObject(t, "Apply Mirrored Pose");
                if (weight >= 0.999f)
                    t.localRotation = mirroredRot;
                else
                    t.localRotation = Quaternion.Slerp(
                        t.localRotation, mirroredRot, weight);
            }
        }

        private static bool IsBoneInGroupMask(HumanBodyBones bone, HashSet<int> groupMask)
        {
            for (int g = 0; g < BonePoseEditorState.BoneGroups.Length; g++)
            {
                if (!groupMask.Contains(g)) continue;
                foreach (var b in BonePoseEditorState.BoneGroups[g].bones)
                {
                    if (b == bone) return true;
                }
            }
            return false;
        }
    }
}

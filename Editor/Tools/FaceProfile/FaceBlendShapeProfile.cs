using System;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceProfile
{
    public enum FaceCategory
    {
        Eye,
        Mouth,
        Brow,
        Cheek,
        Tongue,
        Other,
    }

    public enum FacePreset
    {
        Smile,
        Angry,
        Surprised,
        Sad,
        Cry,
        Wink,
        Sleep,
        Kiss,
        Shy,
    }

    [Serializable]
    public class ShapeEntry
    {
        public string smrPath;
        public int index;
        public string name;
        public string category;
        public List<string> tags = new List<string>();
    }

    [Serializable]
    public class CategorySection
    {
        public string category;
        public List<ShapeEntry> shapes = new List<ShapeEntry>();
    }

    [Serializable]
    public class PresetEntry
    {
        public string smrPath;
        public string shapeName;
        public float value;
        public string slotCategory;
    }

    [Serializable]
    public class PresetCandidate
    {
        public string preset;
        public List<PresetEntry> entries = new List<PresetEntry>();
        public float confidence;
        public int matchedRequired;
        public int totalRequired;
    }

    [Serializable]
    public class FaceBlendShapeProfile
    {
        public string avatarRootPath;
        public string faceSmrPath;
        public List<string> extraFaceSmrPaths = new List<string>();
        public List<CategorySection> categories = new List<CategorySection>();
        public List<PresetCandidate> presets = new List<PresetCandidate>();
        public string cachedAtIso;
        public string avatarFingerprint;
        public int totalShapes;
        public int faceSmrShapes;
        public string faceSmrTier;
    }
}

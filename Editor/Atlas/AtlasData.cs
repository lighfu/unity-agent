using UnityEngine;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Atlas
{
    internal class AtlasSettings
    {
        public int MaxAtlasSize = 4096;
        public int Padding = 4;
        public bool IncludeMainTex = true;
        public bool IncludeNormalMap = true;
        public bool IncludeEmissionMap = true;
        public bool BakeLilToonColor = true;
        public int UniformTileSize = 0; // 0=auto, >0=force all to this size
    }

    internal class MaterialSlotInfo
    {
        public int MaterialIndex;
        public Material Material;
        public string MaterialName;
        public string ShaderName;
        public bool IsLilToon;
        public Texture2D MainTex;
        public Texture2D NormalMap;
        public Texture2D EmissionMap;
        public Color MainColor;
        public int TextureWidth;
        public int TextureHeight;
        public int SubmeshIndex;
        public int TriangleCount;
        public bool HasUVOutOfRange;
        public bool IsSharedAcrossRenderers;
    }

    internal struct AtlasRect
    {
        public int MaterialIndex;
        public int PackedX;
        public int PackedY;
        public int PackedWidth;
        public int PackedHeight;
    }

    internal class AtlasLayout
    {
        public int AtlasWidth;
        public int AtlasHeight;
        public List<AtlasRect> Rects = new List<AtlasRect>();
        public float Efficiency;
    }
}

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Texture editing utilities (replaces EasyMeshPainter dependency).
    /// </summary>
    public static class TextureUtility
    {
        public static Texture2D CreateEditableTexture(Texture2D source)
        {
            if (source == null) return null;

            string path = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(path))
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            }

            // Blit to RenderTexture to handle compressed formats.
            // Use sRGB read/write to prevent double-linearization in Linear color space projects.
            // Without this, Blit applies sRGB→Linear, and the saved texture gets Linear→sRGB→Linear
            // on re-import, resulting in much darker colors.
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, source.mipmapCount > 1);
            readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readable.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return readable;
        }

        public static string SaveTexture(Texture2D tex, string avatarName, string originalName)
        {
            byte[] bytes = tex.EncodeToPNG();
            if (bytes == null)
            {
                Debug.LogError("[TextureUtility] EncodeToPNG returned null.");
                return null;
            }

            string folderPath = Path.Combine(PackagePaths.GetGeneratedDir("Textures"), avatarName).Replace("\\", "/");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            string fileName = originalName + "_Customized.png";
            string fullPath = Path.Combine(folderPath, fileName).Replace("\\", "/");

            File.WriteAllBytes(fullPath, bytes);
            AssetDatabase.ImportAsset(fullPath);

            TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return fullPath;
        }

        public static void PaintIslands(Texture2D tex, Mesh mesh, List<UVIsland> allIslands, List<IslandColorSetting> settings)
        {
            int width = tex.width;
            int height = tex.height;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;

            foreach (var setting in settings)
            {
                if (setting.islandIndex < 0 || setting.islandIndex >= allIslands.Count) continue;

                var island = allIslands[setting.islandIndex];
                foreach (int triIdx in island.triangleIndices)
                {
                    Vector2 uv0 = uvs[triangles[triIdx * 3 + 0]];
                    Vector2 uv1 = uvs[triangles[triIdx * 3 + 1]];
                    Vector2 uv2 = uvs[triangles[triIdx * 3 + 2]];

                    PaintTriangle(tex, uv0, uv1, uv2, setting.color);
                }
            }
            tex.Apply();
        }

        private static void PaintTriangle(Texture2D tex, Vector2 uv0, Vector2 uv1, Vector2 uv2, Color color)
        {
            int w = tex.width;
            int h = tex.height;

            Vector2 p0 = new Vector2(uv0.x * w, uv0.y * h);
            Vector2 p1 = new Vector2(uv1.x * w, uv1.y * h);
            Vector2 p2 = new Vector2(uv2.x * w, uv2.y * h);

            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, w - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, w - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, h - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, h - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (IsPointInTriangle(new Vector2(x + 0.5f, y + 0.5f), p0, p1, p2))
                    {
                        tex.SetPixel(x, y, color);
                    }
                }
            }
        }

        private static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s = a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y;
            float t = a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y;

            if ((s < 0) != (t < 0)) return false;

            float area = -b.y * c.x + a.y * (c.x - b.x) + a.x * (b.y - c.y) + b.x * c.y;
            if (area < 0)
            {
                s = -s;
                t = -t;
                area = -area;
            }
            return s > 0 && t > 0 && (s + t) <= area;
        }
    }

    // --- Metadata classes ---

    [System.Serializable]
    public class MeshPaintMetadata
    {
        public string originalTextureGuid;
        public float[] originalMainColor; // Original _Color RGBA before baking into texture
        public List<IslandColorSetting> islandColors = new List<IslandColorSetting>();
        public List<MaterialTextureGuid> materialTextureGuids; // per-material-slot original texture GUIDs

        public void SetColor(int islandIndex, Color color)
        {
            if (islandColors == null) islandColors = new List<IslandColorSetting>();

            var existing = islandColors.Find(x => x.islandIndex == islandIndex);
            if (existing != null)
            {
                existing.color = color;
            }
            else
            {
                islandColors.Add(new IslandColorSetting { islandIndex = islandIndex, color = color });
            }
        }

        public void RemoveSetting(int islandIndex)
        {
            islandColors?.RemoveAll(x => x.islandIndex == islandIndex);
        }

        public string GetMaterialTextureGuid(int materialIndex)
        {
            if (materialTextureGuids != null)
            {
                var entry = materialTextureGuids.Find(x => x.materialIndex == materialIndex);
                if (entry != null) return entry.textureGuid;
            }
            // Fallback for legacy single-material data
            if (materialIndex == 0) return originalTextureGuid;
            return null;
        }

        public void SetMaterialTextureGuid(int materialIndex, string guid)
        {
            if (materialTextureGuids == null)
                materialTextureGuids = new List<MaterialTextureGuid>();

            var entry = materialTextureGuids.Find(x => x.materialIndex == materialIndex);
            if (entry != null)
                entry.textureGuid = guid;
            else
                materialTextureGuids.Add(new MaterialTextureGuid { materialIndex = materialIndex, textureGuid = guid });

            // Keep legacy field in sync for material 0
            if (materialIndex == 0)
                originalTextureGuid = guid;
        }
    }

    [System.Serializable]
    public class IslandColorSetting
    {
        public int islandIndex;
        public Color color;
    }

    [System.Serializable]
    public class MaterialTextureGuid
    {
        public int materialIndex;
        public string textureGuid;
    }

    public static class MetadataManager
    {
        private static string MetadataRoot => PackagePaths.GetGeneratedDir("Metadata");

        public static void SaveMetadata(MeshPaintMetadata data, string avatarName, string meshName)
        {
            string folderPath = Path.Combine(MetadataRoot, avatarName).Replace("\\", "/");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            string filePath = Path.Combine(folderPath, meshName + ".json").Replace("\\", "/");
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(filePath, json);
            AssetDatabase.Refresh();
        }

        public static MeshPaintMetadata LoadMetadata(string avatarName, string meshName)
        {
            string filePath = Path.Combine(MetadataRoot, avatarName, meshName + ".json").Replace("\\", "/");
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                return JsonUtility.FromJson<MeshPaintMetadata>(json);
            }

            // Fallback: try loading from old EasyMeshPainter path
            string oldPath = Path.Combine("Assets/紫陽花広場/EasyMeshPainter/Metadata", avatarName, meshName + ".json").Replace("\\", "/");
            if (File.Exists(oldPath))
            {
                string json = File.ReadAllText(oldPath);
                return JsonUtility.FromJson<MeshPaintMetadata>(json);
            }

            return null;
        }

        public static string GetOriginalTexturePath(string guid)
        {
            return AssetDatabase.GUIDToAssetPath(guid);
        }
    }
}

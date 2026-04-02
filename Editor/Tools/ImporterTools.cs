using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// TextureImporter / ModelImporter 設定ツール。
    /// アバターのテクスチャ最適化、モデルインポート設定に使用。
    /// </summary>
    public static class ImporterTools
    {
        // =================================================================
        // TextureImporter
        // =================================================================

        [AgentTool(@"Configure texture import settings. texturePath is the asset path to the texture.
textureType: 0=Default, 1=NormalMap, 2=EditorGUI, 3=Sprite, 4=Cursor, 5=Cookie, 6=Lightmap, 7=SingleChannel.
maxSize: max texture dimension (32,64,128,256,512,1024,2048,4096,8192).
compression: 0=None, 1=LowQuality, 2=NormalQuality, 3=HighQuality.
sRGB: true for color textures, false for data (normal maps, masks).
mipmaps: enable mipmap generation. isReadable: allow CPU access (increases memory).
filterMode: 0=Point, 1=Bilinear, 2=Trilinear.
wrapMode: 0=Repeat, 1=Clamp, 2=Mirror, 3=MirrorOnce.
anisoLevel: anisotropic filtering level (0-16).")]
        public static string ConfigureTextureImporter(string texturePath, int textureType = -1, int maxSize = -1,
            int compression = -1, int sRGB = -1, int mipmaps = -1, int isReadable = -1,
            int filterMode = -1, int wrapMode = -1, int anisoLevel = -1, int crunchedCompression = -1,
            int compressionQuality = -1, int alphaIsTransparency = -1)
        {
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) return $"Error: TextureImporter not found at '{texturePath}'.";

            Undo.RecordObject(importer, "Configure TextureImporter");

            if (textureType >= 0) importer.textureType = (TextureImporterType)textureType;
            if (maxSize > 0) importer.maxTextureSize = maxSize;
            if (compression >= 0) importer.textureCompression = (TextureImporterCompression)compression;
            if (sRGB >= 0) importer.sRGBTexture = sRGB != 0;
            if (mipmaps >= 0) importer.mipmapEnabled = mipmaps != 0;
            if (isReadable >= 0) importer.isReadable = isReadable != 0;
            if (filterMode >= 0) importer.filterMode = (FilterMode)filterMode;
            if (wrapMode >= 0) importer.wrapMode = (TextureWrapMode)wrapMode;
            if (anisoLevel >= 0) importer.anisoLevel = anisoLevel;
            if (crunchedCompression >= 0) importer.crunchedCompression = crunchedCompression != 0;
            if (compressionQuality >= 0) importer.compressionQuality = compressionQuality;
            if (alphaIsTransparency >= 0) importer.alphaIsTransparency = alphaIsTransparency != 0;

            importer.SaveAndReimport();
            return $"Success: Configured TextureImporter at '{texturePath}'.";
        }

        [AgentTool("Inspect texture import settings. Shows type, size, compression, mipmaps, and more.")]
        public static string InspectTextureImporter(string texturePath)
        {
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) return $"Error: TextureImporter not found at '{texturePath}'.";

            importer.GetSourceTextureWidthAndHeight(out int srcW, out int srcH);

            var sb = new StringBuilder();
            sb.AppendLine($"TextureImporter: {texturePath}");
            sb.AppendLine($"  Source Size: {srcW}x{srcH}");
            sb.AppendLine($"  TextureType: {importer.textureType}");
            sb.AppendLine($"  MaxSize: {importer.maxTextureSize}");
            sb.AppendLine($"  Compression: {importer.textureCompression}");
            sb.AppendLine($"  CrunchCompression: {importer.crunchedCompression} (quality={importer.compressionQuality})");
            sb.AppendLine($"  sRGB: {importer.sRGBTexture}");
            sb.AppendLine($"  Mipmaps: {importer.mipmapEnabled}");
            sb.AppendLine($"  IsReadable: {importer.isReadable}");
            sb.AppendLine($"  FilterMode: {importer.filterMode}");
            sb.AppendLine($"  WrapMode: {importer.wrapMode}");
            sb.AppendLine($"  AnisoLevel: {importer.anisoLevel}");
            sb.AppendLine($"  AlphaIsTransparency: {importer.alphaIsTransparency}");
            sb.AppendLine($"  AlphaSource: {importer.alphaSource}");
            sb.AppendLine($"  HasAlpha: {importer.DoesSourceTextureHaveAlpha()}");
            sb.AppendLine($"  NpotScale: {importer.npotScale}");
            sb.AppendLine($"  TextureShape: {importer.textureShape}");

            if (importer.textureType == TextureImporterType.Sprite)
            {
                sb.AppendLine($"  SpriteImportMode: {importer.spriteImportMode}");
                sb.AppendLine($"  SpritePixelsPerUnit: {importer.spritePixelsPerUnit}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"Set platform-specific texture settings. platform: 'Standalone', 'Android', 'iPhone', etc.
format: -1=Automatic. Common: 10=RGB24, 12=RGBA32, 29=ASTC_6x6, 48=ETC2_RGBA8.
maxSize/compressionQuality: override per platform.")]
        public static string SetTexturePlatformSettings(string texturePath, string platform, int maxSize = -1,
            int format = -2, int compressionQuality = -1, int overridden = 1)
        {
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) return $"Error: TextureImporter not found at '{texturePath}'.";

            var settings = importer.GetPlatformTextureSettings(platform);
            settings.overridden = overridden != 0;
            if (maxSize > 0) settings.maxTextureSize = maxSize;
            if (format > -2) settings.format = (TextureImporterFormat)format;
            if (compressionQuality >= 0) settings.compressionQuality = compressionQuality;

            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();

            return $"Success: Set {platform} texture settings for '{texturePath}' (maxSize={settings.maxTextureSize}, format={settings.format}).";
        }

        [AgentTool(@"Batch optimize textures under a folder for VRChat avatar performance.
maxSize: target max dimension. enableCrunch: use crunch compression for smaller file size.
This is useful for reducing avatar download size.")]
        public static string BatchOptimizeTextures(string folderPath, int maxSize = 1024, bool enableCrunch = true,
            int compressionQuality = 75)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            if (guids.Length == 0) return $"No textures found in '{folderPath}'.";

            int count = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool changed = false;
                if (importer.maxTextureSize > maxSize)
                {
                    importer.maxTextureSize = maxSize;
                    changed = true;
                }
                if (importer.crunchedCompression != enableCrunch)
                {
                    importer.crunchedCompression = enableCrunch;
                    changed = true;
                }
                if (enableCrunch && importer.compressionQuality != compressionQuality)
                {
                    importer.compressionQuality = compressionQuality;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    count++;
                }
            }

            return $"Success: Optimized {count}/{guids.Length} textures in '{folderPath}' (maxSize={maxSize}, crunch={enableCrunch}).";
        }

        // =================================================================
        // ModelImporter
        // =================================================================

        [AgentTool(@"Configure model import settings for an FBX/model file.
modelPath: asset path to the model.
globalScale: import scale factor (1.0 = original).
importBlendShapes: import blend shapes (essential for facial expressions).
importAnimation: import animations from file.
animationType: 0=None, 1=Legacy, 2=Generic, 3=Human.
meshCompression: 0=Off, 1=Low, 2=Medium, 3=High.
isReadable: allow CPU mesh access.
optimizeMesh: optimize mesh vertices/polygons.
importNormals: 0=Import, 1=Calculate, 2=None.
importTangents: 0=Import, 1=CalculateLegacy, 2=CalculateMikk, 3=None.
normalSmoothingAngle: angle threshold for smooth normals (0-180).")]
        public static string ConfigureModelImporter(string modelPath, float globalScale = float.NaN,
            int importBlendShapes = -1, int importAnimation = -1, int animationType = -1,
            int meshCompression = -1, int isReadable = -1, int optimizeMesh = -1,
            int importNormals = -1, int importTangents = -1, float normalSmoothingAngle = float.NaN,
            int weldVertices = -1, int maxBonesPerVertex = -1)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return $"Error: ModelImporter not found at '{modelPath}'.";

            Undo.RecordObject(importer, "Configure ModelImporter");

            if (!float.IsNaN(globalScale)) importer.globalScale = globalScale;
            if (importBlendShapes >= 0) importer.importBlendShapes = importBlendShapes != 0;
            if (importAnimation >= 0) importer.importAnimation = importAnimation != 0;
            if (animationType >= 0) importer.animationType = (ModelImporterAnimationType)animationType;
            if (meshCompression >= 0) importer.meshCompression = (ModelImporterMeshCompression)meshCompression;
            if (isReadable >= 0) importer.isReadable = isReadable != 0;
            if (optimizeMesh >= 0) importer.optimizeMeshVertices = optimizeMesh != 0;
            if (importNormals >= 0) importer.importNormals = (ModelImporterNormals)importNormals;
            if (importTangents >= 0) importer.importTangents = (ModelImporterTangents)importTangents;
            if (!float.IsNaN(normalSmoothingAngle)) importer.normalSmoothingAngle = normalSmoothingAngle;
            if (weldVertices >= 0) importer.weldVertices = weldVertices != 0;
            if (maxBonesPerVertex > 0) importer.maxBonesPerVertex = maxBonesPerVertex;

            importer.SaveAndReimport();
            return $"Success: Configured ModelImporter at '{modelPath}'.";
        }

        [AgentTool("Inspect model import settings. Shows scale, animation type, mesh settings, blend shapes, and bone configuration.")]
        public static string InspectModelImporter(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return $"Error: ModelImporter not found at '{modelPath}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"ModelImporter: {modelPath}");
            sb.AppendLine($"  GlobalScale: {importer.globalScale:F4}");
            sb.AppendLine($"  UseFileScale: {importer.useFileScale}");
            sb.AppendLine($"  AnimationType: {importer.animationType}");
            sb.AppendLine($"  ImportAnimation: {importer.importAnimation}");
            sb.AppendLine($"  ImportBlendShapes: {importer.importBlendShapes}");
            sb.AppendLine($"  MeshCompression: {importer.meshCompression}");
            sb.AppendLine($"  IsReadable: {importer.isReadable}");
            sb.AppendLine($"  OptimizeMeshVertices: {importer.optimizeMeshVertices}");
            sb.AppendLine($"  OptimizeMeshPolygons: {importer.optimizeMeshPolygons}");
            sb.AppendLine($"  WeldVertices: {importer.weldVertices}");
            sb.AppendLine($"  ImportNormals: {importer.importNormals}");
            sb.AppendLine($"  ImportTangents: {importer.importTangents}");
            sb.AppendLine($"  NormalSmoothingAngle: {importer.normalSmoothingAngle:F1}");
            sb.AppendLine($"  MaxBonesPerVertex: {importer.maxBonesPerVertex}");
            sb.AppendLine($"  ImportCameras: {importer.importCameras}");
            sb.AppendLine($"  ImportLights: {importer.importLights}");
            sb.AppendLine($"  PreserveHierarchy: {importer.preserveHierarchy}");
            sb.AppendLine($"  MaterialImportMode: {importer.materialImportMode}");

            if (importer.importAnimation)
            {
                sb.AppendLine($"  AnimationCompression: {importer.animationCompression}");
                sb.AppendLine($"  ResampleCurves: {importer.resampleCurves}");
            }

            // List clip animations if any
            var clips = importer.clipAnimations;
            if (clips.Length > 0)
            {
                sb.AppendLine($"  AnimationClips ({clips.Length}):");
                foreach (var clip in clips)
                    sb.AppendLine($"    {clip.name} ({clip.firstFrame:F0}-{clip.lastFrame:F0}, loop={clip.loopTime})");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"Configure avatar setup for a model (humanoid/generic configuration).
avatarSetup: 0=NoAvatar, 1=CreateFromThisModel, 2=CopyFromOther.
humanoidOversampling: 1-4, higher = better retargeting quality.
optimizeBones: remove non-connected bones.")]
        public static string ConfigureModelAvatar(string modelPath, int avatarSetup = -1,
            int humanoidOversampling = -1, int optimizeBones = -1, int preserveHierarchy = -1,
            int importConstraints = -1, int importVisibility = -1)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return $"Error: ModelImporter not found at '{modelPath}'.";

            Undo.RecordObject(importer, "Configure Model Avatar");

            if (avatarSetup >= 0) importer.avatarSetup = (ModelImporterAvatarSetup)avatarSetup;
            if (humanoidOversampling > 0) importer.humanoidOversampling = (ModelImporterHumanoidOversampling)humanoidOversampling;
            if (optimizeBones >= 0) importer.optimizeBones = optimizeBones != 0;
            if (preserveHierarchy >= 0) importer.preserveHierarchy = preserveHierarchy != 0;
            if (importConstraints >= 0) importer.importConstraints = importConstraints != 0;
            if (importVisibility >= 0) importer.importVisibility = importVisibility != 0;

            importer.SaveAndReimport();
            return $"Success: Configured avatar setup at '{modelPath}' (setup={importer.avatarSetup}).";
        }

        [AgentTool("Extract embedded textures from a model file into a separate folder. Useful for editing imported textures.")]
        public static string ExtractModelTextures(string modelPath, string outputFolder = "")
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return $"Error: ModelImporter not found at '{modelPath}'.";

            if (string.IsNullOrEmpty(outputFolder))
                outputFolder = System.IO.Path.GetDirectoryName(modelPath) + "/Textures";

            if (!AssetDatabase.IsValidFolder(outputFolder))
            {
                string parent = System.IO.Path.GetDirectoryName(outputFolder);
                string folderName = System.IO.Path.GetFileName(outputFolder);
                AssetDatabase.CreateFolder(parent, folderName);
            }

            bool success = importer.ExtractTextures(outputFolder);
            if (success)
            {
                AssetDatabase.Refresh();
                return $"Success: Extracted textures from '{modelPath}' to '{outputFolder}'.";
            }
            return $"Info: No textures extracted (may be already extracted or none embedded).";
        }

        [AgentTool("Configure model material import settings. Controls how materials are imported and named.")]
        public static string ConfigureModelMaterials(string modelPath, int materialImportMode = -1,
            int materialLocation = -1, int useSRGBMaterialColor = -1)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) return $"Error: ModelImporter not found at '{modelPath}'.";

            Undo.RecordObject(importer, "Configure Model Materials");

            if (materialImportMode >= 0) importer.materialImportMode = (ModelImporterMaterialImportMode)materialImportMode;
            if (materialLocation >= 0) importer.materialLocation = (ModelImporterMaterialLocation)materialLocation;
            if (useSRGBMaterialColor >= 0) importer.useSRGBMaterialColor = useSRGBMaterialColor != 0;

            importer.SaveAndReimport();
            return $"Success: Configured material settings for '{modelPath}' (mode={importer.materialImportMode}, location={importer.materialLocation}).";
        }
    }
}

using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using AjisaiFlow.UnityAgent.Editor.Tools;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    public enum BrushTool { Paint, Eraser, Blur, Smudge, Clone, Dodge, Burn, Tint, Sharpen, Noise, Saturate, Desaturate }

    public static class ScenePaintState
    {
        // Activation
        public static bool IsActive;
        public static Renderer ActiveRenderer;
        public static GameObject AvatarRoot;
        public static Mesh SharedMesh;
        public static Mesh BakedMesh;
        public static Vector3[] WorldVertices;
        public static Vector2[] UVs;
        public static int[] Triangles;

        // Tool mode
        public static BrushTool ActiveTool = BrushTool.Paint;
        public static string[] ToolLabels => new[] {
            M("ペイント"), M("消しゴム"), M("ぼかし"), M("指先"), M("クローン"), M("覆い焼き"), M("焼き込み"),
            M("色合い"), M("シャープン"), M("ノイズ"), M("彩度+"), M("彩度-")
        };

        // Brush settings (synced from MeshPainterWindow)
        public static float BrushSize = 0.02f;        // world-space radius
        public static float BrushOpacity = 1.0f;      // 0..1
        public static float BrushHardness = 0.8f;     // 0..1 (1=hard edge)
        public static Color BrushColor = Color.white;
        public static int BlendModeIndex = 0;          // 0=normal, 1=multiply, 2=screen, 3=overlay
        public static bool IslandMaskEnabled = false;
        public static HashSet<int> MaskedTriangles;    // island mask triangle set

        public static string[] BlendModeLabels => new[] { M("通常"), M("乗算"), M("スクリーン"), M("オーバーレイ") };

        // Symmetry
        public static bool SymmetryEnabled;

        // Texture buffers
        public static Texture2D DisplayTexture;        // assigned to material.mainTexture during painting
        public static Color32[] BasePixels;            // snapshot at stroke start
        public static float[] StrokeAccumulator;       // per-pixel max opacity within stroke
        public static Color32[] OriginalPixels;        // eraser: pre-edit original
        public static int TexWidth, TexHeight;

        // Blur buffer
        public static Color32[] BlurredPixels;

        // Smudge buffers
        public static Color SmudgeCarryColor;
        public static Color32[] SmudgeWorkPixels;

        // Clone stamp state
        public static bool CloneSourceSet;
        public static Vector2 CloneSourceUV;
        public static Vector2Int CloneOffset;

        // Stroke state
        public static bool IsStroking;
        public static Vector2 LastHitUV;
        public static Vector3 LastHitWorldPos;
        public static Vector3 LastHitNormal;
        public static RectInt DirtyRect;

        // Undo
        public static int UndoGroup;
        public static Material PaintMaterial;

        // Pixel undo/redo stacks (Unity Undo can't track texture pixel data)
        private static readonly List<Color32[]> _undoStack = new List<Color32[]>();
        private static readonly List<Color32[]> _redoStack = new List<Color32[]>();
        private const int MaxUndoSteps = 30;

        public static bool CanUndo => _undoStack.Count > 0;
        public static bool CanRedo => _redoStack.Count > 0;

        // Cursor display state (updated every mouse move)
        public static bool HasCursorHit;
        public static Vector3 CursorWorldPos;
        public static Vector3 CursorNormal;

        // Color picked event (for eyedropper → MeshPainterWindow sync)
        public static event Action<Color> OnColorPicked;

        public static void Activate(Renderer renderer, GameObject avatarRoot)
        {
            // Clean up previous session if still active
            if (IsActive)
                Deactivate();

            ActiveRenderer = renderer;
            AvatarRoot = avatarRoot;

            // Get shared mesh
            if (renderer is SkinnedMeshRenderer smr)
            {
                SharedMesh = smr.sharedMesh;
                BakedMesh = new Mesh();
                smr.BakeMesh(BakedMesh);
            }
            else if (renderer is MeshRenderer)
            {
                SharedMesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                BakedMesh = SharedMesh;
            }

            if (SharedMesh == null) return;

            UVs = SharedMesh.uv;
            Triangles = SharedMesh.triangles;

            // Build world vertices from baked mesh
            Matrix4x4 ltw = renderer.transform.localToWorldMatrix;
            Vector3[] localVerts = BakedMesh.vertices;
            WorldVertices = new Vector3[localVerts.Length];
            for (int i = 0; i < localVerts.Length; i++)
                WorldVertices[i] = ltw.MultiplyPoint3x4(localVerts[i]);

            // Setup material and texture
            Material mat = renderer.sharedMaterial;
            if (mat == null) return;

            string avatarName = ToolUtility.FindAvatarRootName(renderer.gameObject);
            if (string.IsNullOrEmpty(avatarName)) avatarName = avatarRoot.name;

            // Load or create metadata
            var metadata = MetadataManager.LoadMetadata(avatarName, renderer.gameObject.name);
            if (metadata == null)
            {
                metadata = new MeshPaintMetadata();
                Texture mainTex = mat.mainTexture;
                if (mainTex != null)
                    metadata.originalTextureGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mainTex));
            }

            // Get source texture (from original if available)
            Texture2D sourceTex = mat.mainTexture as Texture2D;
            Texture2D originalTex = null;
            if (!string.IsNullOrEmpty(metadata.originalTextureGuid))
            {
                string origPath = MetadataManager.GetOriginalTexturePath(metadata.originalTextureGuid);
                var origTex = AssetDatabase.LoadAssetAtPath<Texture2D>(origPath);
                if (origTex != null)
                {
                    originalTex = origTex;
                    sourceTex = origTex;
                }
            }

            if (sourceTex == null) return;

            // Create editable texture
            DisplayTexture = TextureUtility.CreateEditableTexture(sourceTex);
            if (DisplayTexture == null) return;

            // Create _Customized material if needed
            if (!mat.name.EndsWith("_Customized"))
            {
                Material newMat = new Material(mat);
                newMat.name = mat.name + "_Customized";
                string matPath = ToolUtility.SaveMaterialAsset(newMat, avatarName);
                mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                renderer.sharedMaterial = mat;
            }

            // Bake lilToon _Color if needed
            BakeMainColorIfNeeded(DisplayTexture, mat, metadata);
            NeutralizeLilToonShadowColors(mat);

            PaintMaterial = mat;
            TexWidth = DisplayTexture.width;
            TexHeight = DisplayTexture.height;

            // Load original pixels for eraser
            if (originalTex != null)
            {
                var origEditable = TextureUtility.CreateEditableTexture(originalTex);
                BakeMainColorIfNeeded(origEditable, mat, metadata);
                OriginalPixels = origEditable.GetPixels32();
                UnityEngine.Object.DestroyImmediate(origEditable);
            }
            else
            {
                OriginalPixels = DisplayTexture.GetPixels32();
            }

            // Set display texture on material
            mat.mainTexture = DisplayTexture;

            // Save metadata
            MetadataManager.SaveMetadata(metadata, avatarName, renderer.gameObject.name);

            // Init stroke state
            IsStroking = false;
            HasCursorHit = false;
            MaskedTriangles = null;

            IsActive = true;
            SceneView.RepaintAll();
        }

        public static void Deactivate()
        {
            if (IsStroking)
                EndStroke();

            // Save to disk once on deactivation
            ScenePaintEngine.SaveDisplayTexture();

            // Destroy runtime-created objects before nulling references
            if (BakedMesh != null && BakedMesh != SharedMesh)
                UnityEngine.Object.DestroyImmediate(BakedMesh);
            if (DisplayTexture != null)
                UnityEngine.Object.DestroyImmediate(DisplayTexture);

            IsActive = false;
            ActiveRenderer = null;
            AvatarRoot = null;
            SharedMesh = null;
            BakedMesh = null;
            WorldVertices = null;
            UVs = null;
            Triangles = null;
            DisplayTexture = null;
            BasePixels = null;
            StrokeAccumulator = null;
            OriginalPixels = null;
            PaintMaterial = null;
            MaskedTriangles = null;
            BlurredPixels = null;
            SmudgeWorkPixels = null;
            CloneSourceSet = false;
            IsStroking = false;
            HasCursorHit = false;
            ClearUndoStacks();

            SceneView.RepaintAll();
        }

        public static void BeginStroke()
        {
            if (DisplayTexture == null) return;

            // Push pixel snapshot for custom undo before modifying
            PushUndoSnapshot();

            Undo.IncrementCurrentGroup();
            UndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Scene Paint Stroke");

            if (PaintMaterial != null)
                Undo.RecordObject(PaintMaterial, "Scene Paint Stroke");

            BasePixels = DisplayTexture.GetPixels32();
            DirtyRect = new RectInt(TexWidth, TexHeight, 0, 0); // empty

            int pixelCount = TexWidth * TexHeight;

            if (ActiveTool == BrushTool.Smudge)
            {
                SmudgeWorkPixels = DisplayTexture.GetPixels32();
                StrokeAccumulator = null; // Smudge doesn't use accumulator
            }
            else
            {
                StrokeAccumulator = new float[pixelCount];

                if (ActiveTool == BrushTool.Blur || ActiveTool == BrushTool.Sharpen)
                    BlurredPixels = new Color32[pixelCount];
            }

            IsStroking = true;
        }

        public static void BeginSmudgeAtPixel(int centerPixelIdx)
        {
            if (SmudgeWorkPixels != null && centerPixelIdx >= 0 && centerPixelIdx < SmudgeWorkPixels.Length)
                SmudgeCarryColor = SmudgeWorkPixels[centerPixelIdx];
        }

        public static void EndStroke()
        {
            IsStroking = false;
            StrokeAccumulator = null;
            BlurredPixels = null;
            // SmudgeWorkPixels cleaned up in CommitStroke
        }

        public static void RaiseColorPicked(Color color)
        {
            BrushColor = color;
            OnColorPicked?.Invoke(color);
        }

        // --- Pixel Undo/Redo ---

        private static void PushUndoSnapshot()
        {
            if (DisplayTexture == null) return;
            _undoStack.Add(DisplayTexture.GetPixels32());
            if (_undoStack.Count > MaxUndoSteps)
                _undoStack.RemoveAt(0);
            _redoStack.Clear();
        }

        public static void PerformUndo()
        {
            if (_undoStack.Count == 0 || DisplayTexture == null) return;

            // Save current to redo
            _redoStack.Add(DisplayTexture.GetPixels32());

            // Restore from undo
            Color32[] snapshot = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);

            DisplayTexture.SetPixels32(snapshot);
            DisplayTexture.Apply();
            BasePixels = snapshot;
        }

        public static void PerformRedo()
        {
            if (_redoStack.Count == 0 || DisplayTexture == null) return;

            // Save current to undo
            _undoStack.Add(DisplayTexture.GetPixels32());

            // Restore from redo
            Color32[] snapshot = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);

            DisplayTexture.SetPixels32(snapshot);
            DisplayTexture.Apply();
            BasePixels = snapshot;
        }

        private static void ClearUndoStacks()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }

        // --- lilToon helpers (duplicated from TextureEditTools to avoid coupling) ---

        private static void BakeMainColorIfNeeded(Texture2D editableTex, Material mat, MeshPaintMetadata metadata)
        {
            if (!mat.HasProperty("_Color")) return;

            Color mainColor;
            if (metadata.originalMainColor != null && metadata.originalMainColor.Length == 4)
            {
                mainColor = new Color(
                    metadata.originalMainColor[0],
                    metadata.originalMainColor[1],
                    metadata.originalMainColor[2],
                    metadata.originalMainColor[3]);
            }
            else
            {
                mainColor = mat.GetColor("_Color");
            }

            if (mainColor.r > 0.95f && mainColor.g > 0.95f && mainColor.b > 0.95f)
                return;

            if (metadata.originalMainColor == null || metadata.originalMainColor.Length != 4)
                metadata.originalMainColor = new float[] { mainColor.r, mainColor.g, mainColor.b, mainColor.a };

            Color[] pixels = editableTex.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(
                    pixels[i].r * mainColor.r,
                    pixels[i].g * mainColor.g,
                    pixels[i].b * mainColor.b,
                    pixels[i].a);
            }
            editableTex.SetPixels(pixels);
            editableTex.Apply();

            Undo.RecordObject(mat, "Bake _Color into texture");
            mat.SetColor("_Color", Color.white);
        }

        private static void NeutralizeLilToonShadowColors(Material mat)
        {
            if (!mat.HasProperty("_ShadowColor")) return;
            string[] shadowProps = { "_ShadowColor", "_Shadow2ndColor", "_Shadow3rdColor" };
            foreach (var prop in shadowProps)
            {
                if (!mat.HasProperty(prop)) continue;
                Color c = mat.GetColor(prop);
                float gray = c.grayscale;
                mat.SetColor(prop, new Color(gray, gray, gray, c.a));
            }
        }
    }
}

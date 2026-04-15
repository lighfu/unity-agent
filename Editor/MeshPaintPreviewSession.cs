using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using AjisaiFlow.UnityAgent.Editor.Tools;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// In-memory staging layer for Mesh Painter live preview.
    ///
    /// Lifecycle:
    ///   Begin()        — create _Customized material, clone texture into RGBA32,
    ///                    bake lilToon _Color once, install preview texture on material,
    ///                    snapshot baseline pixels.
    ///   ApplyPreview() — replace preview pixels (computed by TextureEditCore) in memory only.
    ///                    Sets HasUncommittedChanges = true.
    ///   Revert()       — reset preview texture back to baseline. Clears uncommitted flag.
    ///   Commit()       — encode preview to PNG, save via TextureUtility.SaveTexture,
    ///                    update metadata, point material at saved texture, then refresh
    ///                    baseline from the new state. Clears uncommitted flag.
    ///   End()          — clean up preview texture and restore the material to the
    ///                    currently-committed state. Auto-commit if requested.
    ///
    /// Keyed to a single (Renderer, materialSlot) pair. Changing renderer requires End() + Begin().
    /// </summary>
    internal class MeshPaintPreviewSession
    {
        public Renderer ActiveRenderer { get; private set; }
        public GameObject AvatarRoot { get; private set; }
        public int MaterialSlotIndex { get; private set; }
        public Material CustomizedMat { get; private set; }
        public Texture2D PreviewTexture { get; private set; }
        public Color[] BaselinePixels { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public Mesh CachedMesh { get; private set; }
        public List<UVIsland> CachedIslands { get; private set; }
        public int[] CachedIslandGroups { get; private set; }
        public MeshPaintMetadata Metadata { get; private set; }
        public string OriginalTexGuid { get; private set; }
        public string AvatarName { get; private set; }
        public string SafeObjectName { get; private set; }
        public bool HasUncommittedChanges { get; private set; }
        public bool IsActive { get; private set; }

        Texture _savedOriginalMainTexture; // what mat.mainTexture was before we swapped in preview

        public bool Begin(Renderer renderer, GameObject avatarRoot, int slotIndex = 0)
        {
            if (IsActive)
            {
                Debug.LogWarning("[MeshPaintPreviewSession] Begin called while already active. Ignoring.");
                return false;
            }
            if (renderer == null) return false;

            // Resolve mesh
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else if (renderer is MeshRenderer) mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return false;

            // Resolve material at slot
            var sharedMats = renderer.sharedMaterials;
            if (slotIndex < 0 || slotIndex >= sharedMats.Length) slotIndex = 0;
            Material mat = sharedMats[slotIndex];
            if (mat == null) return false;

            // Resolve avatar name
            string avatarName = ToolUtility.FindAvatarRootName(renderer.gameObject);
            if (string.IsNullOrEmpty(avatarName) && avatarRoot != null)
                avatarName = avatarRoot.name;
            if (string.IsNullOrEmpty(avatarName)) return false;

            // Load / create metadata
            var metadata = MetadataManager.LoadMetadata(avatarName, renderer.gameObject.name)
                           ?? new MeshPaintMetadata();

            // Resolve source texture: prefer the pristine original if metadata knows it
            Texture2D sourceTex = mat.mainTexture as Texture2D;
            if (sourceTex == null) return false;
            string originalTexGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sourceTex));
            if (string.IsNullOrEmpty(metadata.originalTextureGuid))
                metadata.originalTextureGuid = originalTexGuid;
            else
                originalTexGuid = metadata.originalTextureGuid;

            // Create RGBA32 editable clone of the CURRENT material texture (not the pristine
            // original) — this is the baseline the user sees right now. Previous commits remain.
            Texture2D previewTex = TextureUtility.CreateEditableTexture(sourceTex);
            if (previewTex == null) return false;

            // Duplicate material to _Customized if not already
            if (!mat.name.EndsWith("_Customized"))
            {
                Material newMat = new Material(mat) { name = mat.name + "_Customized" };
                string matPath = ToolUtility.SaveMaterialAsset(newMat, avatarName);
                mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                TextureEditTools.SetMaterialAtIndex(renderer, slotIndex, mat);
            }

            // Bake lilToon _Color into the baseline ONCE per session start.
            // Subsequent preview recomputes work on the already-baked baseline.
            TextureEditTools.BakeMainColorIfNeeded(previewTex, mat, metadata);

            // Remember what the material was pointing at, then install preview
            _savedOriginalMainTexture = mat.mainTexture;
            mat.mainTexture = previewTex;

            // Snapshot baseline AFTER bake so it represents the starting point for live edits
            Color[] baseline = previewTex.GetPixels();

            // Cache island data — live preview must not re-detect islands on every slider tick
            var islands = UVIslandDetector.DetectIslands(mesh);
            var islandGroups = UVIslandDetector.BuildIslandGroups(mesh, islands);

            ActiveRenderer = renderer;
            AvatarRoot = avatarRoot;
            MaterialSlotIndex = slotIndex;
            CustomizedMat = mat;
            PreviewTexture = previewTex;
            BaselinePixels = baseline;
            Width = previewTex.width;
            Height = previewTex.height;
            CachedMesh = mesh;
            CachedIslands = islands;
            CachedIslandGroups = islandGroups;
            Metadata = metadata;
            OriginalTexGuid = originalTexGuid;
            AvatarName = avatarName;
            SafeObjectName = renderer.gameObject.name.Replace("/", "_").Replace("\\", "_");
            HasUncommittedChanges = false;
            IsActive = true;

            MetadataManager.SaveMetadata(metadata, avatarName, renderer.gameObject.name);
            SceneView.RepaintAll();
            return true;
        }

        /// <summary>
        /// Replace preview pixels with the caller-computed result.
        /// Caller is expected to have used BaselinePixels as the starting point.
        /// </summary>
        public void ApplyPreview(Color[] newPixels)
        {
            if (!IsActive || PreviewTexture == null || newPixels == null) return;
            if (newPixels.Length != BaselinePixels.Length) return;
            PreviewTexture.SetPixels(newPixels);
            // updateMipmaps=true is REQUIRED — otherwise only mip0 is updated and distant
            // camera views keep showing the stale pre-edit mipmap chain until the camera
            // zooms in enough to sample mip0.
            PreviewTexture.Apply(updateMipmaps: true);
            HasUncommittedChanges = true;
            SceneView.RepaintAll();
        }

        /// <summary>Revert the preview texture to the current baseline.</summary>
        public void Revert()
        {
            if (!IsActive || PreviewTexture == null) return;
            PreviewTexture.SetPixels(BaselinePixels);
            PreviewTexture.Apply(updateMipmaps: true);
            HasUncommittedChanges = false;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Flush preview to disk, update the material to point at the saved asset, and
        /// move the baseline forward so further edits stack on top.
        /// </summary>
        public bool Commit()
        {
            if (!IsActive || PreviewTexture == null || CustomizedMat == null) return false;
            if (!HasUncommittedChanges) return true; // nothing to do

            // NOTE: we intentionally do NOT call Undo.RecordObject here.
            // Commit is a file-write operation (PNG on disk) plus a series of
            // material mutations that Begin()/End() already perform outside Unity's
            // undo stack. Mixing in Undo.RecordObject produces an inconsistent
            // history — especially when a _Customized material is shared across
            // renderers, where a single Undo can flip mainTexture references on
            // unrelated meshes. If the user wants to revert, they should either
            // use Revert() before commit or reopen the original texture.

            // Source name to use for the saved file — reuse what the old flow produced so
            // we don't orphan files from earlier tool-based commits.
            string sourceName = (_savedOriginalMainTexture != null ? _savedOriginalMainTexture.name : "Preview");
            string texPath = TextureUtility.SaveTexture(PreviewTexture, AvatarName, sourceName + "_" + SafeObjectName);
            if (string.IsNullOrEmpty(texPath))
            {
                Debug.LogError("[MeshPaintPreviewSession] Commit failed: SaveTexture returned empty path.");
                return false;
            }

            var savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (savedTex == null)
            {
                Debug.LogError("[MeshPaintPreviewSession] Commit failed: could not load saved texture.");
                return false;
            }

            CustomizedMat.mainTexture = savedTex;
            TextureEditTools.NeutralizeLilToonShadowColors(CustomizedMat);

            if (string.IsNullOrEmpty(Metadata.originalTextureGuid))
                Metadata.originalTextureGuid = OriginalTexGuid;
            MetadataManager.SaveMetadata(Metadata, AvatarName, ActiveRenderer.gameObject.name);

            EditorUtility.SetDirty(CustomizedMat);

            // Baseline forward so next edits layer on top
            BaselinePixels = PreviewTexture.GetPixels();
            HasUncommittedChanges = false;

            // Re-point material at the in-memory preview so further slider edits remain live.
            // The saved texture is only what future sessions / external viewers see.
            _savedOriginalMainTexture = savedTex;
            CustomizedMat.mainTexture = PreviewTexture;

            SceneView.RepaintAll();
            return true;
        }

        /// <summary>
        /// Clean up the session. If <paramref name="autoCommit"/> is true and there are
        /// unsaved changes, commit them first. Otherwise discards the live preview.
        /// </summary>
        public void End(bool autoCommit)
        {
            if (!IsActive) return;

            if (autoCommit && HasUncommittedChanges)
            {
                Commit();
            }

            // Restore the material to the committed texture (not the in-memory preview
            // that we're about to destroy).
            if (CustomizedMat != null && _savedOriginalMainTexture != null)
                CustomizedMat.mainTexture = _savedOriginalMainTexture;

            if (PreviewTexture != null)
                Object.DestroyImmediate(PreviewTexture);

            ActiveRenderer = null;
            AvatarRoot = null;
            CustomizedMat = null;
            PreviewTexture = null;
            BaselinePixels = null;
            CachedMesh = null;
            CachedIslands = null;
            CachedIslandGroups = null;
            Metadata = null;
            _savedOriginalMainTexture = null;
            HasUncommittedChanges = false;
            IsActive = false;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Translate user-facing island-index list into the format TextureEditCore expects,
        /// validating indices against the cached island list.
        /// Returns null islands list when empty (meaning "all triangles").
        /// </summary>
        public bool ResolveIslandIndices(IList<int> rawIndices, out List<int> resolved)
        {
            resolved = new List<int>();
            if (CachedIslands == null) return true;
            if (rawIndices == null || rawIndices.Count == 0)
            {
                // empty → apply to every island (Gradient wants explicit list,
                // HSV/BC treats empty as "whole texture")
                for (int i = 0; i < CachedIslands.Count; i++) resolved.Add(i);
                return true;
            }
            foreach (int idx in rawIndices)
            {
                if (idx < 0 || idx >= CachedIslands.Count) return false;
                resolved.Add(idx);
            }
            return true;
        }
    }
}

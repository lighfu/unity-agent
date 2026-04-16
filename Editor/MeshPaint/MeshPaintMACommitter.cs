using UnityEngine;
using UnityEditor;
using AjisaiFlow.UnityAgent.Editor.Tools;
using AjisaiFlow.UnityAgent.Editor.MA;

namespace AjisaiFlow.UnityAgent.Editor.MeshPaint
{
    /// <summary>
    /// Non-destructive commit path: save the current preview as a PNG, wrap it in a
    /// material variant, then attach a <c>ModularAvatarMaterialSetter</c> to the target
    /// renderer's GameObject so the swap only happens at MA build time. The original
    /// texture asset and the live material are left untouched.
    /// </summary>
    internal static class MeshPaintMACommitter
    {
        public static bool Apply(MeshPaintSessionEntry entry, GameObject avatarRoot)
        {
            if (entry == null) return false;
            if (!MAAvailability.IsInstalled)
            {
                EditorUtility.DisplayDialog(
                    "Modular Avatar が見つかりません",
                    "MA 非破壊モードを使用するには Modular Avatar パッケージが必要です。\n"
                    + "パッケージをインストールするか、MA 非破壊トグルをオフにしてから再度お試しください。",
                    "OK");
                return false;
            }

            var s = entry.Session;
            if (s == null || !s.IsActive) return false;
            if (s.PreviewTexture == null || s.CustomizedMat == null) return false;
            if (entry.Renderer == null) return false;

            // 1. Save PNG.
            string texPath = TextureUtility.SaveTexture(
                s.PreviewTexture, s.AvatarName, s.SafeObjectName + "_MA");
            if (string.IsNullOrEmpty(texPath))
            {
                Debug.LogError("[MeshPaintMACommitter] SaveTexture returned empty path.");
                return false;
            }
            var savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (savedTex == null)
            {
                Debug.LogError("[MeshPaintMACommitter] Failed to load saved texture at " + texPath);
                return false;
            }

            // 2. Create material variant pointing at the saved PNG.
            var variant = new Material(s.CustomizedMat) { name = s.CustomizedMat.name + "_MA" };
            variant.mainTexture = savedTex;
            string matPath = ToolUtility.SaveMaterialAsset(variant, s.AvatarName);
            var matAsset = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (matAsset == null)
            {
                Debug.LogError("[MeshPaintMACommitter] Failed to save material variant at " + matPath);
                return false;
            }

            // 3. Attach MA MaterialSetter to the target renderer's GameObject.
            var comp = MAComponentFactory.AddOrUpdateMaterialSetter(
                entry.Renderer.gameObject, entry.Renderer, s.MaterialSlotIndex, matAsset);
            if (comp == null)
            {
                Debug.LogError("[MeshPaintMACommitter] Failed to create MaterialSetter component.");
                return false;
            }

            // 4. Keep the preview on the live material — MA swap happens at build time.
            return true;
        }
    }
}

using System;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Persistence
{
    /// <summary>
    /// Texture2D を最大 1024 px に縮小して PNG / Base64 にエンコードする。
    /// 永続化サイズを抑えるために UI プレビュー画像へ適用する。
    /// 元の Texture2D は変更しない (縮小が必要な場合はテンポラリを生成して破棄)。
    /// </summary>
    internal static class ImageDownscaler
    {
        public const int MaxDimension = 1024;

        /// <summary>
        /// Texture2D を ImageSnapshot に変換する。null や失敗時は空の ImageSnapshot を返す
        /// (HasImage が false になる)。
        /// </summary>
        public static ImageSnapshot Encode(Texture2D source)
        {
            var snap = new ImageSnapshot();
            if (source == null) return snap;

            Texture2D temp = null;
            try
            {
                Texture2D toEncode = source;

                bool needsResize = source.width > MaxDimension || source.height > MaxDimension;
                if (needsResize || !source.isReadable)
                {
                    int w, h;
                    if (needsResize)
                    {
                        float scale = Mathf.Min(
                            (float)MaxDimension / source.width,
                            (float)MaxDimension / source.height);
                        w = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
                        h = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
                    }
                    else
                    {
                        w = source.width;
                        h = source.height;
                    }
                    temp = BlitToReadable(source, w, h);
                    toEncode = temp;
                }

                byte[] png = toEncode.EncodeToPNG();
                if (png == null || png.Length == 0) return snap;

                snap.width = toEncode.width;
                snap.height = toEncode.height;
                snap.pngBase64 = Convert.ToBase64String(png);
                return snap;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] ImageDownscaler.Encode failed: {ex.Message}");
                return new ImageSnapshot();
            }
            finally
            {
                if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
            }
        }

        /// <summary>
        /// ImageSnapshot を Texture2D に復元する。失敗時は null。
        /// 呼び出し側は不要になったら DestroyImmediate で解放すること。
        /// </summary>
        public static Texture2D Decode(ImageSnapshot snap)
        {
            if (snap == null || !snap.HasImage) return null;
            try
            {
                byte[] png = Convert.FromBase64String(snap.pngBase64);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(png))
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                    return null;
                }
                return tex;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] ImageDownscaler.Decode failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 任意の Texture (read 不可でも) を指定サイズの読み取り可能 Texture2D に Blit する。
        /// </summary>
        static Texture2D BlitToReadable(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.Default);
            var prevActive = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}

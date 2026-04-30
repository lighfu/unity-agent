using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceProfile
{
    // Library/UnityAgent/face-profiles/<fingerprint>.json への永続化と読み込み。
    // Library 配下なので git に入らず、Unity プロジェクトのクリーンで自動再生成される。
    public static class FaceProfileCache
    {
        private static string CacheDir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "UnityAgent", "face-profiles"));

        public static string ComputeFingerprint(SkinnedMeshRenderer faceSmr, string avatarRootName)
        {
            if (faceSmr == null || faceSmr.sharedMesh == null)
                return string.Empty;

            var mesh = faceSmr.sharedMesh;
            int count = mesh.blendShapeCount;
            string firstShape = count > 0 ? mesh.GetBlendShapeName(0) : string.Empty;
            string lastShape = count > 0 ? mesh.GetBlendShapeName(count - 1) : string.Empty;

            string seed = string.Join("|", new[]
            {
                avatarRootName ?? string.Empty,
                faceSmr.name ?? string.Empty,
                mesh.name ?? string.Empty,
                count.ToString(),
                mesh.vertexCount.ToString(),
                firstShape,
                lastShape,
            });

            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(seed));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static FaceBlendShapeProfile TryGet(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint)) return null;
            string path = GetCachePath(fingerprint);
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var profile = JsonUtility.FromJson<FaceBlendShapeProfile>(json);
                if (profile == null) return null;
                if (profile.avatarFingerprint != fingerprint) return null;
                return profile;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FaceProfileCache] Failed to load cache '{path}': {ex.Message}");
                return null;
            }
        }

        public static void Save(FaceBlendShapeProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.avatarFingerprint)) return;

            try
            {
                EnsureCacheDir();
                string path = GetCachePath(profile.avatarFingerprint);
                string json = JsonUtility.ToJson(profile, prettyPrint: true);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FaceProfileCache] Failed to save profile: {ex.Message}");
            }
        }

        public static void Invalidate(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint)) return;
            string path = GetCachePath(fingerprint);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FaceProfileCache] Failed to invalidate '{path}': {ex.Message}");
            }
        }

        private static string GetCachePath(string fingerprint)
        {
            return Path.Combine(CacheDir, fingerprint + ".json");
        }

        private static void EnsureCacheDir()
        {
            if (!Directory.Exists(CacheDir))
                Directory.CreateDirectory(CacheDir);
        }
    }
}

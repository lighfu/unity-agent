using System.IO;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// パッケージパス解決ユーティリティ。
    /// VPM パッケージ (Packages/) と旧 Assets/ 配置の両方をサポート。
    /// </summary>
    internal static class PackagePaths
    {
        private static string _cachedPackageRoot;

        /// <summary>
        /// パッケージのルートディレクトリ（読み取り専用）。
        /// VPM: Packages/com.ajisaiflow.unityagent/
        /// Legacy: Assets/紫陽花広場/UnityAgent/
        /// </summary>
        internal static string PackageRoot
        {
            get
            {
                if (_cachedPackageRoot != null)
                    return _cachedPackageRoot;

                var info = UnityEditor.PackageManager.PackageInfo
                    .FindForAssembly(typeof(PackagePaths).Assembly);
                _cachedPackageRoot = info?.resolvedPath
                    ?? Path.Combine(Application.dataPath, "紫陽花広場", "UnityAgent");
                return _cachedPackageRoot;
            }
        }

        /// <summary>
        /// 生成アセットの出力先ルート（書き込み可能な Assets/ 配下）。
        /// </summary>
        internal const string GeneratedRoot = "Assets/UnityAgent_Generated";

        /// <summary>
        /// サブフォルダ付きの生成アセットディレクトリパスを返す。
        /// 例: GetGeneratedDir("MeshEdit") → "Assets/UnityAgent_Generated/MeshEdit"
        /// </summary>
        internal static string GetGeneratedDir(string subfolder)
        {
            return GeneratedRoot + "/" + subfolder;
        }

        /// <summary>
        /// ローカライゼーションディレクトリのパスを返す。
        /// パッケージ内の localization/ フォルダを参照。
        /// </summary>
        internal static string LocalizationDir(string category)
        {
            return Path.Combine(PackageRoot, "localization", category);
        }
    }
}

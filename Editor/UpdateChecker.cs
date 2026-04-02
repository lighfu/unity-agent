using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor
{
    [Serializable]
    public class VersionExpiration
    {
        public string version; // "1.0.0"
        public string date;    // "yyyy-MM-dd"
        public string reason;  // optional
    }

    [Serializable]
    public class VersionInfo
    {
        public string version;
        public string downloadUrl;
        public string changelog;
        public string sha256;
        public VersionExpiration[] expirations;
    }

    [InitializeOnLoad]
    public static class UpdateChecker
    {
        public static readonly string CurrentVersion = GetPackageVersion();

        private const string VersionUrl =
            "https://raw.githubusercontent.com/lighfu/versions/refs/heads/main/unity/UnityAIAgent.json";

        public const string ProductPageUrl = "https://ajisaiflow.booth.pm/items/7993112";

        private const string PrefLastCheck = "UnityAgent_LastUpdateCheck";
        private const string PrefSkipVersion = "UnityAgent_SkipVersion";
        private const string PrefExpirationDate = "UnityAgent_ExpirationDate";
        private const string PrefExpirationReason = "UnityAgent_ExpirationReason";
        private const string PrefExpirationVersion = "UnityAgent_ExpirationVersion";

        private static UnityWebRequest _versionRequest;
        private static bool _isManualCheck;
        private static Action<string> _manualCheckCallback;
        private static Action<VersionInfo> _manualUpdateCallback;

        /// <summary>Latest version info from server (null if not yet checked or failed).</summary>
        public static VersionInfo Latest { get; private set; }

        /// <summary>Fires after a version check completes (info may be null on failure).</summary>
        public static event Action<VersionInfo> Checked;

        public static bool IsExpired { get; private set; }
        public static bool IsLicenseCheckFailed { get; private set; }
        public static string ExpirationDateStr { get; private set; }
        public static string ExpirationReason { get; private set; }

        /// <summary>IsExpired or IsLicenseCheckFailed</summary>
        public static bool IsBlocked => IsExpired || IsLicenseCheckFailed;

        static UpdateChecker()
        {
            // Restore expiration from cache (offline support)
            var cachedVersion = SettingsStore.GetString(PrefExpirationVersion, "");
            if (cachedVersion == CurrentVersion)
            {
                var cachedDate = SettingsStore.GetString(PrefExpirationDate, "");
                if (DateTime.TryParseExact(cachedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var expDate))
                {
                    ExpirationDateStr = cachedDate;
                    ExpirationReason = SettingsStore.GetString(PrefExpirationReason, "");
                    IsExpired = DateTime.UtcNow.Date > expDate;
                }
            }

            EditorApplication.update += CheckOnce;
        }

        private static void CheckOnce()
        {
            EditorApplication.update -= CheckOnce;

            // Skip if checked within 24 hours
            var lastCheck = SettingsStore.GetString(PrefLastCheck, "");
            if (DateTime.TryParse(lastCheck, out var lastTime) &&
                (DateTime.UtcNow - lastTime).TotalHours < 24)
                return;

            StartRequest();
        }

        /// <summary>Manual update check (ignores 24-hour cooldown and skip version).</summary>
        /// <param name="onResult">Callback with result message when no update available or on error.</param>
        /// <param name="onUpdateAvailable">Callback with VersionInfo when an update is found.</param>
        public static void CheckNow(Action<string> onResult = null, Action<VersionInfo> onUpdateAvailable = null)
        {
            if (_versionRequest != null)
            {
                onResult?.Invoke(M("確認中です..."));
                return;
            }
            _isManualCheck = true;
            _manualCheckCallback = onResult;
            _manualUpdateCallback = onUpdateAvailable;
            StartRequest();
        }

        /// <summary>Returns true if Latest version is newer than CurrentVersion.</summary>
        public static bool IsUpdateAvailable()
        {
            if (Latest == null || string.IsNullOrEmpty(Latest.version)) return false;
            try { return new Version(Latest.version) > new Version(CurrentVersion); }
            catch { return false; }
        }

        private static void StartRequest()
        {
            _versionRequest = UnityWebRequest.Get(VersionUrl);
            _versionRequest.SendWebRequest();
            EditorApplication.update += PollVersionCheck;
        }

        private static void PollVersionCheck()
        {
            if (!_versionRequest.isDone) return;

            EditorApplication.update -= PollVersionCheck;

            if (_versionRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[UnityAgent] Update check failed: {_versionRequest.error}");
                _versionRequest.Dispose();
                _versionRequest = null;

                // No cache for this version → cannot verify license
                if (string.IsNullOrEmpty(ExpirationDateStr))
                    IsLicenseCheckFailed = true;

                var cb = _manualCheckCallback;
                _manualCheckCallback = null;
                _manualUpdateCallback = null;
                _isManualCheck = false;
                cb?.Invoke(M("更新確認に失敗しました。ネットワーク接続を確認してください。"));
                Checked?.Invoke(null);
                return;
            }

            // Network succeeded → clear failure flag
            IsLicenseCheckFailed = false;

            var json = _versionRequest.downloadHandler.text;
            _versionRequest.Dispose();
            _versionRequest = null;

            SettingsStore.SetString(PrefLastCheck, DateTime.UtcNow.ToString("o"));

            VersionInfo latestVersion;
            try
            {
                latestVersion = JsonUtility.FromJson<VersionInfo>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityAgent] Failed to parse version info: {e.Message}");
                var cb2 = _manualCheckCallback;
                _manualCheckCallback = null;
                _manualUpdateCallback = null;
                _isManualCheck = false;
                cb2?.Invoke(M("更新情報の解析に失敗しました。"));
                Checked?.Invoke(null);
                return;
            }

            Latest = latestVersion;

            // Update expiration info — find entry matching CurrentVersion
            UpdateExpiration(latestVersion);

            if (string.IsNullOrEmpty(latestVersion.version))
            {
                Debug.LogWarning("[UnityAgent] Server returned empty version.");
                var cb3 = _manualCheckCallback;
                _manualCheckCallback = null;
                _manualUpdateCallback = null;
                _isManualCheck = false;
                cb3?.Invoke(M("更新情報の解析に失敗しました。"));
                Checked?.Invoke(Latest);
                return;
            }

            Version current, latest;
            try
            {
                current = new Version(CurrentVersion);
                latest = new Version(latestVersion.version);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityAgent] Failed to parse version: {e.Message}");
                var cb3 = _manualCheckCallback;
                _manualCheckCallback = null;
                _manualUpdateCallback = null;
                _isManualCheck = false;
                cb3?.Invoke(M("更新情報の解析に失敗しました。"));
                Checked?.Invoke(Latest);
                return;
            }

            if (latest <= current)
            {
                var cb3 = _manualCheckCallback;
                _manualCheckCallback = null;
                _manualUpdateCallback = null;
                _isManualCheck = false;
                cb3?.Invoke(string.Format(M("v{0} は最新です。"), CurrentVersion));
                Checked?.Invoke(Latest);
                return;
            }

            // Manual check: skip version is ignored, use MD3 dialog via callback
            if (_isManualCheck)
            {
                _isManualCheck = false;
                var cbMsg = _manualCheckCallback;
                var cbUpdate = _manualUpdateCallback;
                _manualCheckCallback = null;
                _manualUpdateCallback = null;
                if (cbUpdate != null)
                    cbUpdate(latestVersion);
                else
                    cbMsg?.Invoke(string.Format(M("v{0} が利用可能です。"), latestVersion.version));
                Checked?.Invoke(Latest);
                return;
            }

            // Auto check: respect skip version; banner handles display
            var skipVersion = SettingsStore.GetString(PrefSkipVersion, "");
            if (skipVersion == latestVersion.version)
            {
                Checked?.Invoke(Latest);
                return;
            }

            Checked?.Invoke(Latest);
        }

        private static void UpdateExpiration(VersionInfo info)
        {
            if (info.expirations == null) return;

            foreach (var entry in info.expirations)
            {
                if (entry.version != CurrentVersion) continue;

                if (DateTime.TryParseExact(entry.date, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var expDate))
                {
                    ExpirationDateStr = entry.date;
                    ExpirationReason = entry.reason ?? "";
                    IsExpired = DateTime.UtcNow.Date > expDate;
                    SettingsStore.SetString(PrefExpirationDate, entry.date);
                    SettingsStore.SetString(PrefExpirationReason, ExpirationReason);
                    SettingsStore.SetString(PrefExpirationVersion, CurrentVersion);
                }
                return;
            }
        }

        private static string GetPackageVersion()
        {
            var info = UnityEditor.PackageManager.PackageInfo
                .FindForAssembly(typeof(UpdateChecker).Assembly);
            if (info != null) return info.version;

            // Assets/ 配置 (開発時) — package.json から読み取り
            var packageJson = System.IO.Path.Combine(PackagePaths.PackageRoot, "package.json");
            if (System.IO.File.Exists(packageJson))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    System.IO.File.ReadAllText(packageJson),
                    @"""version""\s*:\s*""([^""]+)""");
                if (match.Success) return match.Groups[1].Value;
            }
            return "0.0.0";
        }
    }
}

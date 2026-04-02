using System;
using UnityEditor;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    internal static class HttpHelper
    {
        /// <summary>
        /// HTTP URL の場合に insecureHttpOption を一時的に許可する IDisposable スコープ。
        /// HTTPS URL の場合は null を返す（using (null) は C# で安全）。
        /// ローカルアドレス (localhost, 127.*, 10.*, 172.16-31.*, 192.168.*) は常に許可。
        /// それ以外の HTTP URL は AgentSettings.AllowRemoteInsecureHttp が true の場合のみ許可。
        /// </summary>
        internal static IDisposable AllowInsecureIfNeeded(string url)
        {
            if (url == null || !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return null;

            if (IsLocalAddress(url) || AgentSettings.AllowRemoteInsecureHttp)
                return new InsecureHttpScope();

            return null;
        }

        private static bool IsLocalAddress(string url)
        {
            // "http://".Length == 7
            var hostPart = url.Substring(7);
            // ポートやパスの前のホスト部分を取得
            int colonIdx = hostPart.IndexOf(':');
            int slashIdx = hostPart.IndexOf('/');
            int end = hostPart.Length;
            if (colonIdx >= 0) end = Math.Min(end, colonIdx);
            if (slashIdx >= 0) end = Math.Min(end, slashIdx);
            var host = hostPart.Substring(0, end).ToLowerInvariant();

            if (host == "localhost") return true;
            if (host == "127.0.0.1") return true;
            if (host.StartsWith("127.")) return true;
            if (host.StartsWith("192.168.")) return true;
            if (host == "[::1]" || host == "::1") return true;

            return false;
        }

        private class InsecureHttpScope : IDisposable
        {
            private readonly InsecureHttpOption _original;

            internal InsecureHttpScope()
            {
                _original = PlayerSettings.insecureHttpOption;
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            }

            public void Dispose()
            {
                PlayerSettings.insecureHttpOption = _original;
            }
        }
    }
}

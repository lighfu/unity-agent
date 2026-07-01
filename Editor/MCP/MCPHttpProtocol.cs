using System;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    internal static class MCPHttpProtocol
    {
        public const string LatestProtocolVersion = "2025-06-18";
        public const string DefaultProtocolVersionWhenHeaderMissing = "2025-03-26";
        public const string HeaderProtocolVersion = "MCP-Protocol-Version";
        public const string HeaderSessionId = "Mcp-Session-Id";
        public const string HeaderOrigin = "Origin";

        public static string NegotiateProtocolVersion(JNode initializeParams)
        {
            string requested = initializeParams?["protocolVersion"].AsString;
            return IsSupportedProtocolVersion(requested)
                ? requested
                : LatestProtocolVersion;
        }

        public static bool IsSupportedProtocolVersion(string version)
        {
            return string.Equals(version, LatestProtocolVersion, StringComparison.Ordinal)
                || string.Equals(version, "2025-03-26", StringComparison.Ordinal)
                || string.Equals(version, "2024-11-05", StringComparison.Ordinal);
        }

        public static bool IsValidProtocolVersionHeader(string headerValue)
        {
            if (string.IsNullOrWhiteSpace(headerValue))
                return true;
            return IsSupportedProtocolVersion(headerValue.Trim());
        }

        public static bool AcceptsJsonAndEventStream(string acceptHeader)
        {
            return HeaderContainsMediaType(acceptHeader, "application/json")
                && HeaderContainsMediaType(acceptHeader, "text/event-stream");
        }

        public static bool AcceptsEventStream(string acceptHeader)
        {
            return HeaderContainsMediaType(acceptHeader, "text/event-stream");
        }

        public static bool ShouldWarnAboutPostAcceptHeader(string acceptHeader)
        {
            return !string.IsNullOrWhiteSpace(acceptHeader)
                && !AcceptsJsonAndEventStream(acceptHeader);
        }

        public static bool IsAllowedOrigin(string originHeader)
        {
            if (string.IsNullOrWhiteSpace(originHeader))
                return true;

            string origin = originHeader.Trim();
            if (string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                return false;
            if (!string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                uri.AbsolutePath != "/")
                return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            string host = uri.Host.Trim('[', ']').ToLowerInvariant();
            return host == "localhost" || host == "127.0.0.1" || host == "::1";
        }

        public static string GetCorsAllowOriginValue(string originHeader)
        {
            return string.IsNullOrWhiteSpace(originHeader) ? "*" : originHeader.Trim();
        }

        static bool HeaderContainsMediaType(string header, string mediaType)
        {
            if (string.IsNullOrWhiteSpace(header))
                return false;

            var parts = header.Split(',');
            foreach (string raw in parts)
            {
                string item = raw.Trim();
                int semicolon = item.IndexOf(';');
                if (semicolon >= 0)
                    item = item.Substring(0, semicolon).Trim();

                if (string.Equals(item, mediaType, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}

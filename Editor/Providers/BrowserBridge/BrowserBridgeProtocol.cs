using System.Text;

namespace AjisaiFlow.UnityAgent.Editor.Providers.BrowserBridge
{
    /// <summary>
    /// Browser Bridge WebSocket プロトコルの JSON メッセージヘルパー。
    /// </summary>
    internal static class BrowserBridgeProtocol
    {
        public static string BuildPromptMessage(string id, string text, bool newSession = false)
        {
            var sb = new StringBuilder(text.Length + 64);
            sb.Append("{\"type\":\"prompt\",\"id\":\"");
            sb.Append(EscapeJson(id));
            sb.Append("\",\"text\":\"");
            sb.Append(EscapeJson(text));
            sb.Append("\",\"newSession\":");
            sb.Append(newSession ? "true" : "false");
            sb.Append("}");
            return sb.ToString();
        }

        public static string BuildAbortMessage(string id)
        {
            return "{\"type\":\"abort\",\"id\":\"" + EscapeJson(id) + "\"}";
        }

        public static string BuildPingMessage()
        {
            return "{\"type\":\"ping\"}";
        }

        public static string GetMessageType(string json)
        {
            return GetField(json, "type");
        }

        public static string GetField(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string key = "\"" + field + "\"";
            int keyIdx = json.IndexOf(key);
            if (keyIdx < 0) return null;
            int colonIdx = json.IndexOf(':', keyIdx + key.Length);
            if (colonIdx < 0) return null;
            int startQuote = json.IndexOf('"', colonIdx + 1);
            if (startQuote < 0) return null;

            var sb = new StringBuilder();
            for (int i = startQuote + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    if (next == '"') { sb.Append('"'); i++; }
                    else if (next == '\\') { sb.Append('\\'); i++; }
                    else if (next == 'n') { sb.Append('\n'); i++; }
                    else if (next == 'r') { sb.Append('\r'); i++; }
                    else if (next == 't') { sb.Append('\t'); i++; }
                    else { sb.Append(c); }
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}

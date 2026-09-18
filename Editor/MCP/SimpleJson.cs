using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>Minimal recursive-descent JSON parser / builder. No external dependencies.</summary>
    internal sealed class JNode
    {
        public enum JType { Null, Bool, Number, String, Array, Object }
        const int MaxParseDepth = 128;

        public readonly JType Type;
        readonly string _s;
        readonly double _n;
        readonly bool _b;
        readonly List<JNode> _a;
        readonly Dictionary<string, JNode> _o;

        JNode(JType t, string s = null, double n = 0, bool b = false,
              List<JNode> a = null, Dictionary<string, JNode> o = null)
        { Type = t; _s = s; _n = n; _b = b; _a = a; _o = o; }

        // ─── Static factories ───

        public static readonly JNode NullNode = new JNode(JType.Null);
        public static JNode Str(string v) => new JNode(JType.String, s: v ?? "");
        public static JNode Num(double v) => new JNode(JType.Number, n: v);
        public static JNode Bool(bool v) => new JNode(JType.Bool, b: v);

        public static JNode Arr(params JNode[] items)
        {
            var list = new List<JNode>(items?.Length ?? 0);
            if (items != null)
            {
                foreach (var item in items)
                    list.Add(item ?? NullNode);
            }
            return new JNode(JType.Array, a: list);
        }

        public static JNode Obj(params (string key, JNode val)[] pairs)
        {
            var d = new Dictionary<string, JNode>();
            if (pairs != null)
            {
                foreach (var (k, v) in pairs)
                    d[k] = v ?? NullNode;
            }
            return new JNode(JType.Object, o: d);
        }

        // ─── Accessors ───

        public string AsString => _s;
        public double AsNumber => _n;
        public int AsInt => (int)_n;
        public bool AsBool => _b;
        public bool IsNull => Type == JType.Null;
        public int Count => _a?.Count ?? _o?.Count ?? 0;
        public List<JNode> AsArray => _a;
        public Dictionary<string, JNode> AsObject => _o;

        public JNode this[string key] =>
            _o != null && _o.TryGetValue(key, out var v) ? v : NullNode;
        public JNode this[int idx] =>
            _a != null && idx >= 0 && idx < _a.Count ? _a[idx] : NullNode;
        public bool Has(string key) => _o != null && _o.ContainsKey(key);

        /// <summary>Get object keys. Empty if not an object.</summary>
        public IEnumerable<string> Keys => _o != null ? (IEnumerable<string>)_o.Keys : Array.Empty<string>();

        // ─── Parse ───

        public static JNode Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return NullNode;
            int pos = 0;
            var result = ParseValue(json, ref pos, 0);
            if (result == null)
                return NullNode;

            SkipWs(json, ref pos);
            return pos == json.Length ? result : NullNode;
        }

        static JNode ParseValue(string s, ref int i, int depth)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return NullNode;
            char c = s[i];
            if (c == '"') return ParseString(s, ref i);
            if (c == '{')
            {
                if (depth >= MaxParseDepth) return null;
                return ParseObject(s, ref i, depth + 1);
            }
            if (c == '[')
            {
                if (depth >= MaxParseDepth) return null;
                return ParseArray(s, ref i, depth + 1);
            }
            if (c == 't' && MatchKeyword(s, ref i, "true")) return Bool(true);
            if (c == 'f' && MatchKeyword(s, ref i, "false")) return Bool(false);
            if (c == 'n' && MatchKeyword(s, ref i, "null")) return NullNode;
            return ParseNumber(s, ref i);
        }

        static JNode ParseString(string s, ref int i)
        {
            i++; // skip opening "
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 5 >= s.Length || !int.TryParse(s.Substring(i + 2, 4),
                                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int cp))
                                return null;
                            sb.Append((char)cp);
                            i += 4;
                            break;
                        default: return null;
                    }
                    i += 2;
                }
                else if (c == '"')
                {
                    i++; // skip closing "
                    return Str(sb.ToString());
                }
                else
                {
                    if (c < 0x20)
                        return null;
                    sb.Append(c);
                    i++;
                }
            }
            return null;
        }

        static JNode ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && s[i] == '-') i++;

            if (i >= s.Length)
                return null;

            if (s[i] == '0')
            {
                i++;
            }
            else if (s[i] >= '1' && s[i] <= '9')
            {
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            else
            {
                return null;
            }

            if (i < s.Length && s[i] == '.')
            {
                i++;
                int fractionStart = i;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                if (i == fractionStart)
                    return null;
            }

            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                int exponentStart = i;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                if (i == exponentStart)
                    return null;
            }

            if (double.TryParse(s.Substring(start, i - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double v) &&
                !double.IsInfinity(v) && !double.IsNaN(v))
                return Num(v);
            return null;
        }

        static JNode ParseObject(string s, ref int i, int depth)
        {
            i++; // skip {
            var dict = new Dictionary<string, JNode>();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return new JNode(JType.Object, o: dict); }

            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') break;
                var keyNode = ParseString(s, ref i);
                if (keyNode == null)
                    return null;
                string key = keyNode.AsString;
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    return null;
                i++;
                var val = ParseValue(s, ref i, depth);
                if (val == null)
                    return null;
                dict[key] = val;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                return null;
            }
            return i <= s.Length && i > 0 && s[i - 1] == '}'
                ? new JNode(JType.Object, o: dict)
                : null;
        }

        static JNode ParseArray(string s, ref int i, int depth)
        {
            i++; // skip [
            var list = new List<JNode>();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return new JNode(JType.Array, a: list); }

            while (i < s.Length)
            {
                var value = ParseValue(s, ref i, depth);
                if (value == null)
                    return null;
                list.Add(value);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                return null;
            }
            return i <= s.Length && i > 0 && s[i - 1] == ']'
                ? new JNode(JType.Array, a: list)
                : null;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        static bool MatchKeyword(string s, ref int i, string keyword)
        {
            if (i + keyword.Length > s.Length ||
                string.CompareOrdinal(s, i, keyword, 0, keyword.Length) != 0)
                return false;
            i += keyword.Length;
            return true;
        }

        // ─── Stringify ───

        public string ToJson()
        {
            var sb = new StringBuilder();
            WriteJson(sb, this);
            return sb.ToString();
        }

        static void WriteJson(StringBuilder sb, JNode node)
        {
            if (node == null)
            {
                sb.Append("null");
                return;
            }

            switch (node.Type)
            {
                case JType.Null: sb.Append("null"); break;
                case JType.Bool: sb.Append(node._b ? "true" : "false"); break;
                case JType.Number:
                    if (double.IsNaN(node._n) || double.IsInfinity(node._n))
                        sb.Append("null");
                    else
                        sb.Append(node._n.ToString(CultureInfo.InvariantCulture));
                    break;
                case JType.String:
                    sb.Append('"');
                    EscapeString(sb, node._s);
                    sb.Append('"');
                    break;
                case JType.Array:
                    sb.Append('[');
                    for (int i = 0; i < node._a.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        WriteJson(sb, node._a[i]);
                    }
                    sb.Append(']');
                    break;
                case JType.Object:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in node._o)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append('"');
                        EscapeString(sb, kv.Key);
                        sb.Append("\":");
                        WriteJson(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
            }
        }

        static void EscapeString(StringBuilder sb, string s)
        {
            if (s == null) return;
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
        }
    }
}

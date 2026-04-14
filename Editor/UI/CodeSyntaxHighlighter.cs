using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>
    /// コードブロックを Unity RichText のカラータグ付き文字列に変換する軽量ハイライタ。
    /// C# / JSON / JS / Shader (HLSL) / Python の最小サポート。完全な構文解析ではなく
    /// 正規表現ベースのトークナイザ。言語名は ```lang で指定する。
    /// </summary>
    internal static class CodeSyntaxHighlighter
    {
        // Dark theme colors (matches Unity pro skin)
        const string ColorKeyword = "#569CD6"; // blue
        const string ColorString  = "#CE9178"; // orange
        const string ColorComment = "#6A9955"; // green
        const string ColorNumber  = "#B5CEA8"; // light green
        const string ColorType    = "#4EC9B0"; // teal
        const string ColorDefault = "#D4D4D4"; // light gray

        static readonly HashSet<string> CSharpKeywords = new HashSet<string>
        {
            "abstract","as","async","await","base","bool","break","byte","case","catch",
            "char","checked","class","const","continue","decimal","default","delegate","do",
            "double","else","enum","event","explicit","extern","false","finally","fixed",
            "float","for","foreach","goto","if","implicit","in","int","interface","internal",
            "is","lock","long","namespace","new","null","object","operator","out","override",
            "params","private","protected","public","readonly","ref","return","sbyte","sealed",
            "short","sizeof","stackalloc","static","string","struct","switch","this","throw",
            "true","try","typeof","uint","ulong","unchecked","unsafe","ushort","using","var",
            "virtual","void","volatile","while","yield","record","init","get","set","nameof",
        };

        static readonly HashSet<string> CSharpTypes = new HashSet<string>
        {
            "String","Int32","Int64","Double","Float","Boolean","List","Dictionary","HashSet",
            "IEnumerable","IEnumerator","Task","Action","Func","Vector3","Vector2","Quaternion",
            "Transform","GameObject","Component","MonoBehaviour","EditorWindow","VisualElement",
            "Label","Color","Texture2D","AssetDatabase","Selection","Debug","Application","Mathf",
        };

        static readonly HashSet<string> PythonKeywords = new HashSet<string>
        {
            "False","None","True","and","as","assert","async","await","break","class","continue",
            "def","del","elif","else","except","finally","for","from","global","if","import","in",
            "is","lambda","nonlocal","not","or","pass","raise","return","try","while","with","yield",
        };

        static readonly HashSet<string> ShaderKeywords = new HashSet<string>
        {
            "Shader","Properties","SubShader","Pass","Tags","LOD","CGPROGRAM","ENDCG","HLSLPROGRAM",
            "ENDHLSL","Cull","ZWrite","ZTest","Blend","Stencil","ColorMask","Offset","Lighting",
            "Fog","SeparateSpecular","AlphaTest","Fixed","float","float2","float3","float4",
            "half","half2","half3","half4","int","bool","sampler2D","samplerCUBE","fixed","fixed4",
            "return","if","else","for","while","void","struct","uniform","const","in","out","inout",
        };

        static readonly HashSet<string> JsKeywords = new HashSet<string>
        {
            "var","let","const","function","return","if","else","for","while","do","switch","case",
            "break","continue","class","extends","new","this","super","static","async","await",
            "try","catch","finally","throw","typeof","instanceof","in","of","null","undefined",
            "true","false","import","export","from","default","yield",
        };

        /// <summary>
        /// コードを Unity RichText のカラータグ付き文字列へ変換する。
        /// tag 引数は fenced code block の言語ヒント ("csharp", "json", "shader", "python" 等)。
        /// </summary>
        public static string Highlight(string code, string lang)
        {
            if (string.IsNullOrEmpty(code)) return "";

            var kind = DetectLang(lang);
            switch (kind)
            {
                case LangKind.CSharp: return HighlightCLike(code, CSharpKeywords, CSharpTypes);
                case LangKind.Json: return HighlightJson(code);
                case LangKind.Python: return HighlightPython(code);
                case LangKind.Shader: return HighlightCLike(code, ShaderKeywords, null);
                case LangKind.Js: return HighlightCLike(code, JsKeywords, null);
                default: return $"<color={ColorDefault}>{EscapeRichText(code)}</color>";
            }
        }

        enum LangKind { None, CSharp, Json, Python, Shader, Js }

        static LangKind DetectLang(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return LangKind.None;
            lang = lang.Trim().ToLowerInvariant();
            switch (lang)
            {
                case "c#":
                case "cs":
                case "csharp":
                    return LangKind.CSharp;
                case "json":
                    return LangKind.Json;
                case "python":
                case "py":
                    return LangKind.Python;
                case "shader":
                case "hlsl":
                case "shaderlab":
                case "glsl":
                    return LangKind.Shader;
                case "js":
                case "javascript":
                case "ts":
                case "typescript":
                    return LangKind.Js;
                default:
                    return LangKind.None;
            }
        }

        // ── C-like tokenizer (C#, shader, js) ──

        static string HighlightCLike(string code, HashSet<string> keywords, HashSet<string> types)
        {
            var sb = new StringBuilder(code.Length + 64);
            int i = 0;
            int n = code.Length;

            while (i < n)
            {
                char c = code[i];

                // Line comment
                if (c == '/' && i + 1 < n && code[i + 1] == '/')
                {
                    int end = code.IndexOf('\n', i);
                    if (end < 0) end = n;
                    AppendColored(sb, code.Substring(i, end - i), ColorComment);
                    i = end;
                    continue;
                }

                // Block comment
                if (c == '/' && i + 1 < n && code[i + 1] == '*')
                {
                    int end = code.IndexOf("*/", i + 2);
                    if (end < 0) end = n;
                    else end += 2;
                    AppendColored(sb, code.Substring(i, end - i), ColorComment);
                    i = end;
                    continue;
                }

                // String literal (single or double)
                if (c == '"' || c == '\'' || (c == '@' && i + 1 < n && code[i + 1] == '"'))
                {
                    int start = i;
                    bool verbatim = c == '@';
                    if (verbatim) i += 2;
                    else i++;
                    char quote = verbatim ? '"' : c;
                    while (i < n)
                    {
                        if (!verbatim && code[i] == '\\' && i + 1 < n) { i += 2; continue; }
                        if (code[i] == quote)
                        {
                            if (verbatim && i + 1 < n && code[i + 1] == '"') { i += 2; continue; }
                            i++;
                            break;
                        }
                        i++;
                    }
                    AppendColored(sb, code.Substring(start, i - start), ColorString);
                    continue;
                }

                // Number
                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '.' || code[i] == '_')) i++;
                    AppendColored(sb, code.Substring(start, i - start), ColorNumber);
                    continue;
                }

                // Identifier / keyword
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                    string word = code.Substring(start, i - start);
                    if (keywords != null && keywords.Contains(word))
                        AppendColored(sb, word, ColorKeyword);
                    else if (types != null && types.Contains(word))
                        AppendColored(sb, word, ColorType);
                    else
                        sb.Append(EscapeRichText(word));
                    continue;
                }

                // Any other char
                sb.Append(EscapeRichTextChar(c));
                i++;
            }

            return sb.ToString();
        }

        // ── JSON tokenizer ──

        static string HighlightJson(string code)
        {
            var sb = new StringBuilder(code.Length + 64);
            int i = 0;
            int n = code.Length;

            while (i < n)
            {
                char c = code[i];

                if (c == '"')
                {
                    int start = i++;
                    while (i < n)
                    {
                        if (code[i] == '\\' && i + 1 < n) { i += 2; continue; }
                        if (code[i] == '"') { i++; break; }
                        i++;
                    }
                    // Is this a key? Look ahead past whitespace for ':'
                    int peek = i;
                    while (peek < n && char.IsWhiteSpace(code[peek])) peek++;
                    bool isKey = peek < n && code[peek] == ':';
                    AppendColored(sb, code.Substring(start, i - start), isKey ? ColorType : ColorString);
                    continue;
                }

                if (char.IsDigit(c) || (c == '-' && i + 1 < n && char.IsDigit(code[i + 1])))
                {
                    int start = i++;
                    while (i < n && (char.IsDigit(code[i]) || code[i] == '.' || code[i] == 'e' || code[i] == 'E' || code[i] == '+' || code[i] == '-'))
                        i++;
                    AppendColored(sb, code.Substring(start, i - start), ColorNumber);
                    continue;
                }

                if (c == 't' && i + 3 < n && code.Substring(i, 4) == "true")
                {
                    AppendColored(sb, "true", ColorKeyword); i += 4; continue;
                }
                if (c == 'f' && i + 4 < n && code.Substring(i, 5) == "false")
                {
                    AppendColored(sb, "false", ColorKeyword); i += 5; continue;
                }
                if (c == 'n' && i + 3 < n && code.Substring(i, 4) == "null")
                {
                    AppendColored(sb, "null", ColorKeyword); i += 4; continue;
                }

                sb.Append(EscapeRichTextChar(c));
                i++;
            }

            return sb.ToString();
        }

        // ── Python tokenizer ──

        static string HighlightPython(string code)
        {
            var sb = new StringBuilder(code.Length + 64);
            int i = 0;
            int n = code.Length;

            while (i < n)
            {
                char c = code[i];

                // # comment
                if (c == '#')
                {
                    int end = code.IndexOf('\n', i);
                    if (end < 0) end = n;
                    AppendColored(sb, code.Substring(i, end - i), ColorComment);
                    i = end;
                    continue;
                }

                // Triple-quoted string
                if ((c == '"' || c == '\'') && i + 2 < n && code[i + 1] == c && code[i + 2] == c)
                {
                    char q = c;
                    int start = i;
                    i += 3;
                    while (i + 2 < n && !(code[i] == q && code[i + 1] == q && code[i + 2] == q)) i++;
                    if (i + 2 < n) i += 3; else i = n;
                    AppendColored(sb, code.Substring(start, i - start), ColorString);
                    continue;
                }

                // Single-quoted / double-quoted string
                if (c == '"' || c == '\'')
                {
                    char q = c;
                    int start = i++;
                    while (i < n)
                    {
                        if (code[i] == '\\' && i + 1 < n) { i += 2; continue; }
                        if (code[i] == q) { i++; break; }
                        i++;
                    }
                    AppendColored(sb, code.Substring(start, i - start), ColorString);
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '.' || code[i] == '_')) i++;
                    AppendColored(sb, code.Substring(start, i - start), ColorNumber);
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                    string word = code.Substring(start, i - start);
                    if (PythonKeywords.Contains(word))
                        AppendColored(sb, word, ColorKeyword);
                    else
                        sb.Append(EscapeRichText(word));
                    continue;
                }

                sb.Append(EscapeRichTextChar(c));
                i++;
            }

            return sb.ToString();
        }

        // ── Helpers ──

        static void AppendColored(StringBuilder sb, string text, string color)
        {
            sb.Append("<color=").Append(color).Append('>').Append(EscapeRichText(text)).Append("</color>");
        }

        static string EscapeRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("<", "\u2039").Replace(">", "\u203A");
        }

        static string EscapeRichTextChar(char c)
        {
            if (c == '<') return "\u2039";
            if (c == '>') return "\u203A";
            return c.ToString();
        }
    }
}

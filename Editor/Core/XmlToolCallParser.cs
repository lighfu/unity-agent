using System;
using System.Collections.Generic;
using System.Text;

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// 1 件の XML スタイルツール呼び出し。
    /// <code>
    /// &lt;tool name="WriteFile"&gt;
    /// &lt;arg name="path"&gt;A.cs&lt;/arg&gt;
    /// &lt;arg name="content"&gt;line1
    /// line2&lt;/arg&gt;
    /// &lt;/tool&gt;
    /// </code>
    /// </summary>
    internal sealed class XmlToolCall
    {
        /// <summary>&lt;tool name="..."&gt; のツール名。</summary>
        public string Name;

        /// <summary>arg 名 → 生の（ルールに従いトリムした）値。XML エンティティはデコードしない。</summary>
        public Dictionary<string, string> Args;

        /// <summary>ソーステキスト内の '&lt;tool' の開始インデックス。</summary>
        public int Index;

        /// <summary>'&lt;tool' から '&lt;/tool&gt;' の末尾までの長さ。</summary>
        public int Length;
    }

    /// <summary>
    /// LLM 応答テキストから XML スタイルのツール呼び出し（&lt;tool&gt;/&lt;arg&gt;）を抽出する寛容なスキャナ。
    ///
    /// 既存のブラケット構文 [Method(args)] と共存させるための新形式。意図的に厳密な XML パーサ
    /// （System.Xml）を使わない — 引数本文には '&lt;' '&amp;' '"' '|' '$' など XML 的に不正な文字が
    /// 頻繁に含まれる（シェルスクリプト・コード・正規表現）ため。手書きの文字スキャンで以下を満たす:
    ///   - &lt;arg&gt; の中身は生テキストとして取得し、XML エンティティデコードを行わない。
    ///   - &lt;arg&gt; 直後の改行を 1 つ、&lt;/arg&gt; 直前の改行＋インデントを 1 つだけトリムする。
    ///   - 属性は name="x" / name='x' / name=x の全形式を許容する。
    ///   - 不正・未閉じ入力でも例外を投げず、取得できた分だけ返す。
    ///   - 散文中の foo() や [Tool(x)]、構造を伴わない単なる "&lt;tool&gt;" 言及は検出しない（精度優先）。
    /// </summary>
    internal static class XmlToolCallParser
    {
        /// <summary>
        /// テキストからツール呼び出しをドキュメント順で返す。1 件も無ければ空リスト（null は返さない）。
        /// 例外は投げない。
        /// </summary>
        public static List<XmlToolCall> Parse(string text)
        {
            var result = new List<XmlToolCall>();
            if (string.IsNullOrEmpty(text))
                return result;

            int len = text.Length;
            int pos = 0;

            while (pos < len)
            {
                // 次の "<tool" 出現位置を探す（タグ名は \w+ なので "<tool" の直後が単語境界外
                // —— 例えば "<toolbox" —— の場合はスキップする）。
                int toolStart = IndexOfTag(text, pos, "tool");
                if (toolStart < 0)
                    break;

                // '<tool' に続く属性領域 → '>' を見つける。
                int afterTag = toolStart + 5; // "<tool" の長さ
                int openEnd = FindTagClose(text, afterTag);
                if (openEnd < 0)
                {
                    // 開きタグが閉じない（未閉じ）。これ以上の構造は取れないので終了。
                    break;
                }

                // 開きタグ内の属性文字列。例: ' name="WriteFile"'
                string openAttrs = text.Substring(afterTag, openEnd - afterTag);
                // self-closing '<tool ... />' は引数を持たないツール呼び出しとして扱う。
                bool selfClosing = openEnd > afterTag && text[openEnd - 1] == '/';
                if (selfClosing)
                {
                    openAttrs = openAttrs.Substring(0, openAttrs.Length - 1);
                }

                string toolName = GetAttribute(openAttrs, "name");
                if (string.IsNullOrEmpty(toolName))
                {
                    // name 属性が無い <tool> は本物のツール呼び出しと見なさない。
                    // 次の文字から再スキャンする（"<tool" 自体の誤検出を避ける）。
                    pos = afterTag;
                    continue;
                }

                int contentStart = openEnd + 1; // '>' の次

                if (selfClosing)
                {
                    result.Add(new XmlToolCall
                    {
                        Name = toolName,
                        Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        Index = toolStart,
                        Length = contentStart - toolStart,
                    });
                    pos = contentStart;
                    continue;
                }

                // 対応する "</tool>" を探す。本文は生テキストとして扱うため、
                // 終端の判定は既存の寛容な走査ルールに従う。
                int closeStart = IndexOfCloseTag(text, contentStart, "tool");

                if (closeStart < 0)
                {
                    // </tool> が見つからない（未閉じ）。これは本物のツール呼び出しではなく、
                    // 散文中の "<tool ...>" 言及やストリーミング途中の断片である可能性が高い。
                    // 精度優先（クラス doc 参照）: 呼び出しとして採用せず、この開きタグを飛ばして
                    // 後続に整形式の <tool>...</tool> があれば拾えるよう走査を続ける。
                    // ※ 採用してしまうと、同一ターンの本物のブラケット呼び出しが
                    //   XML モード切替で握り潰される（レビュー指摘の回避）。
                    pos = afterTag;
                    continue;
                }

                int bodyEnd = closeStart;                       // <arg> をスキャンする範囲の終端（排他）
                int closeEnd = GetCloseTagEnd(text, closeStart, "tool");
                int toolLength = closeEnd - toolStart;          // XmlToolCall.Length

                var args = ParseArgs(text, contentStart, bodyEnd);

                result.Add(new XmlToolCall
                {
                    Name = toolName,
                    Args = args,
                    Index = toolStart,
                    Length = toolLength,
                });

                pos = toolStart + toolLength;
            }

            return result;
        }

        /// <summary>
        /// [start, end) の範囲から &lt;arg name="..."&gt;...&lt;/arg&gt; を順に抽出する。
        /// </summary>
        private static Dictionary<string, string> ParseArgs(string text, int start, int end)
        {
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int pos = start;

            while (pos < end)
            {
                int argStart = IndexOfTag(text, pos, "arg");
                if (argStart < 0 || argStart >= end)
                    break;

                int afterTag = argStart + 4; // "<arg" の長さ
                int openEnd = FindTagClose(text, afterTag);
                if (openEnd < 0 || openEnd >= end)
                {
                    // 開き <arg> が閉じない → これ以上の arg は取れない。
                    break;
                }

                string openAttrs = text.Substring(afterTag, openEnd - afterTag);
                bool selfClosing = openEnd > afterTag && text[openEnd - 1] == '/';
                if (selfClosing)
                {
                    openAttrs = openAttrs.Substring(0, openAttrs.Length - 1);
                }

                string argName = GetAttribute(openAttrs, "name");
                int contentStart = openEnd + 1;

                if (selfClosing)
                {
                    // 自己終了 <arg name="x"/> は空文字列の値。
                    if (!string.IsNullOrEmpty(argName) && !args.ContainsKey(argName))
                        args[argName] = string.Empty;
                    pos = contentStart;
                    continue;
                }

                // 最初の </arg> を探す（範囲 end を超えないよう制限）。
                int closeStart = IndexOfCloseTag(text, contentStart, "arg");

                int rawEnd;
                int nextPos;
                if (closeStart < 0 || closeStart > end)
                {
                    // </arg> が無い（未閉じ）。寛容に: 範囲末尾までを内容とする。
                    rawEnd = end;
                    nextPos = end;
                }
                else
                {
                    rawEnd = closeStart;
                    nextPos = GetCloseTagEnd(text, closeStart, "arg");
                }

                // name 属性が無い arg は寛容に無視する（内容のスキップだけ行う）。
                if (!string.IsNullOrEmpty(argName))
                {
                    string raw = text.Substring(contentStart, rawEnd - contentStart);
                    string value = DecodeEntities(TrimArgContent(raw));
                    // 同名が複数ある場合は最初を優先する。
                    if (!args.ContainsKey(argName))
                        args[argName] = value;
                }

                pos = nextPos;
            }

            return args;
        }

        /// <summary>
        /// &lt;arg&gt; の生内容に対する整形ルール:
        ///   - 先頭の改行（\r?\n）を 1 つだけ取り除く。
        ///   - 末尾の「改行 + その後ろの空白インデント」を 1 つだけ取り除く
        ///     （= 読みやすいレイアウトで &lt;/arg&gt; が独立行・インデント付きの場合に対応）。
        ///   - 内部の内容・改行・インデントは完全に保持する。
        ///   - XML エンティティのデコードは行わない（'&lt;' '&amp;' '"' はそのまま通す）。
        /// </summary>
        private static string TrimArgContent(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return raw;

            int begin = 0;
            int finish = raw.Length;

            // 先頭の改行を 1 つだけ除去（\r\n または \n）。
            if (begin < finish && raw[begin] == '\r')
                begin++;
            if (begin < finish && raw[begin] == '\n')
                begin++;

            // 末尾: "改行 + 空白インデント" を 1 つだけ除去する。
            // まず末尾から空白（スペース/タブ）を skip し、その直前が改行ならそこまで切る。
            int scan = finish;
            int ws = scan;
            while (ws > begin && (raw[ws - 1] == ' ' || raw[ws - 1] == '\t'))
                ws--;
            if (ws > begin && raw[ws - 1] == '\n')
            {
                int cut = ws - 1; // '\n' の位置
                if (cut > begin && raw[cut - 1] == '\r')
                    cut--; // 先行する '\r' も含める
                finish = cut;
            }

            if (begin == 0 && finish == raw.Length)
                return raw;
            if (finish < begin)
                return string.Empty;
            return raw.Substring(begin, finish - begin);
        }

        /// <summary>
        /// &lt;arg&gt; 内容中の標準 XML エンティティをデコードする。
        ///
        /// なぜ必要か: 多くのモデル（特に小型モデル）は「エスケープ不要」という指示に反して
        /// XML タグ内の '&lt;' '&gt;' '&amp;' をエンティティ化して出力する。デコードしないと
        /// ファイルにリテラルの "&amp;lt;" が書き込まれてしまう（実際に gemini-3.5-flash で発生）。
        ///
        /// 方針（左→右の単一パス、再帰デコードしない）:
        ///   - 名前付き: &amp;lt; &amp;gt; &amp;amp; &amp;quot; &amp;apos; を実文字へ。
        ///   - 数値: &amp;#NN; (10進) / &amp;#xHH; (16進)。
        ///   - ';' で終端しない裸の '&amp;'（シェルの &amp;&amp;、"A &amp; B"）や、未知の実体は '&amp;' のまま残す
        ///     → コード/シェル/正規表現は壊れない。
        ///   - 単一パスなので "&amp;amp;lt;" は "&amp;lt;"（リテラル4文字）に正しくなる。
        /// </summary>
        private static string DecodeEntities(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0)
                return s;

            var sb = new StringBuilder(s.Length);
            int len = s.Length;
            int i = 0;
            while (i < len)
            {
                char c = s[i];
                if (c != '&')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                // '&' の後、';' までを実体本体候補として取り出す。実体は短いので、
                // 遠くの ';' は実体とみなさない（12 文字上限）。
                int semi = s.IndexOf(';', i + 1);
                if (semi > i + 1 && semi - i <= 12)
                {
                    string body = s.Substring(i + 1, semi - i - 1);
                    string decoded = DecodeEntityBody(body);
                    if (decoded != null)
                    {
                        sb.Append(decoded);
                        i = semi + 1;
                        continue;
                    }
                }

                // 実体として認識できない '&' はそのまま残す。
                sb.Append('&');
                i++;
            }
            return sb.ToString();
        }

        /// <summary>'&amp;' と ';' の間の本体を実文字へ。認識できなければ null。</summary>
        private static string DecodeEntityBody(string body)
        {
            switch (body)
            {
                case "lt": return "<";
                case "gt": return ">";
                case "amp": return "&";
                case "quot": return "\"";
                case "apos": return "'";
            }

            // 数値実体 &#NN; / &#xHH;
            if (body.Length >= 2 && body[0] == '#')
            {
                int code;
                bool ok;
                if (body[1] == 'x' || body[1] == 'X')
                {
                    ok = int.TryParse(body.Substring(2),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out code);
                }
                else
                {
                    ok = int.TryParse(body.Substring(1),
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out code);
                }
                if (!ok || code < 0 || code > 0x10FFFF)
                    return null;
                try
                {
                    return char.ConvertFromUtf32(code);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null; // サロゲート等の不正コードポイント
                }
            }

            return null;
        }

        /// <summary>
        /// fromIndex 以降で開きタグ "&lt;{name}" の開始位置を返す。タグ名の直後が単語構成文字
        /// （\w）の場合は別タグ（例 "&lt;toolbox"）なのでスキップする。見つからなければ -1。
        /// </summary>
        private static int IndexOfTag(string text, int fromIndex, string name)
        {
            string needle = "<" + name;
            int i = fromIndex;
            while (true)
            {
                int idx = text.IndexOf(needle, i, StringComparison.Ordinal);
                if (idx < 0)
                    return -1;
                int after = idx + needle.Length;
                // タグ名の直後が単語文字なら別タグ。
                if (after < text.Length && IsWordChar(text[after]))
                {
                    i = idx + 1;
                    continue;
                }
                return idx;
            }
        }

        /// <summary>
        /// fromIndex 以降で閉じタグ "&lt;/{name}&gt;" の '&lt;' の位置を返す。見つからなければ -1。
        /// タグ名直後は '&gt;' か空白のみを許容する（"&lt;/toolbox&gt;" を誤検出しない）。
        /// </summary>
        private static int IndexOfCloseTag(string text, int fromIndex, string name)
        {
            string needle = "</" + name;
            int i = fromIndex;
            while (true)
            {
                int idx = text.IndexOf(needle, i, StringComparison.Ordinal);
                if (idx < 0)
                    return -1;
                int after = idx + needle.Length;
                // タグ名の直後が単語文字なら別タグ（例 "</toolbox>"）。
                if (after < text.Length && IsWordChar(text[after]))
                {
                    i = idx + 1;
                    continue;
                }
                // 末尾の '>' を許容（間に空白可: "</tool >"）。
                if (GetCloseTagEnd(text, idx, name) >= 0)
                    return idx;
                // '>' が見つからない場合は不正 → 次を探す。
                i = idx + 1;
            }
        }

        /// <summary>
        /// 指定位置の閉じタグの直後を返す。IndexOfCloseTag は開始位置だけを返すため、
        /// 許容されたタグ名後の空白を含めて Length/走査位置を正しく計算する。
        /// </summary>
        private static int GetCloseTagEnd(string text, int closeStart, string name)
        {
            int j = closeStart + 2 + name.Length;
            while (j < text.Length && IsWhite(text[j]))
                j++;
            return j < text.Length && text[j] == '>' ? j + 1 : -1;
        }

        /// <summary>
        /// 開きタグの '&lt;name' 直後（attrStart）から、属性領域を閉じる '&gt;' の位置を返す。
        /// 属性値の引用符内にある '&gt;' は無視する。見つからなければ -1。
        /// </summary>
        private static int FindTagClose(string text, int attrStart)
        {
            int len = text.Length;
            char quote = '\0';
            for (int i = attrStart; i < len; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    quote = c;
                    continue;
                }
                if (c == '>')
                    return i;
                // 新しいタグの開始に遭遇したら、この開きタグは閉じていない（不正）。
                if (c == '<')
                    return -1;
            }
            return -1;
        }

        /// <summary>
        /// 属性文字列から指定属性の値を取り出す。name="x" / name='x' / name=x を許容する。
        /// 見つからなければ null。属性名は \w+ として一致させる。
        /// </summary>
        private static string GetAttribute(string attrs, string attrName)
        {
            if (string.IsNullOrEmpty(attrs))
                return null;

            int len = attrs.Length;
            int i = 0;
            while (i < len)
            {
                // 属性名の先頭（単語構成文字）を探す。
                if (!IsWordChar(attrs[i]))
                {
                    i++;
                    continue;
                }
                int nameStart = i;
                while (i < len && IsWordChar(attrs[i]))
                    i++;
                string thisName = attrs.Substring(nameStart, i - nameStart);

                // 名前の後の空白を skip。
                int j = i;
                while (j < len && IsWhite(attrs[j]))
                    j++;

                if (j < len && attrs[j] == '=')
                {
                    j++; // '=' を消費
                    while (j < len && IsWhite(attrs[j]))
                        j++;

                    string value;
                    if (j < len && (attrs[j] == '"' || attrs[j] == '\''))
                    {
                        char q = attrs[j];
                        int vStart = j + 1;
                        int vEnd = attrs.IndexOf(q, vStart);
                        if (vEnd < 0)
                        {
                            // 閉じ引用符が無い → 末尾までを値とする（寛容）。
                            value = attrs.Substring(vStart);
                            j = len;
                        }
                        else
                        {
                            value = attrs.Substring(vStart, vEnd - vStart);
                            j = vEnd + 1;
                        }
                    }
                    else
                    {
                        // bare value: 次の空白まで。
                        int vStart = j;
                        while (j < len && !IsWhite(attrs[j]) && attrs[j] != '/')
                            j++;
                        value = attrs.Substring(vStart, j - vStart);
                    }

                    if (string.Equals(thisName, attrName, StringComparison.OrdinalIgnoreCase))
                        return value;

                    i = j;
                }
                else
                {
                    // '=' が無い属性 → 名前の後ろから続行。
                    i = j;
                }
            }

            return null;
        }

        private static bool IsWordChar(char c)
        {
            return c == '_' || (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static bool IsWhite(char c)
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        }
    }
}

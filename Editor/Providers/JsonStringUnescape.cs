using System.Globalization;
using System.Text;

namespace AjisaiFlow.UnityAgent.Editor.Providers
{
    /// <summary>
    /// JSON 文字列リテラル内のエスケープシーケンスを 1 個ずつデコードする共有ヘルパー。
    ///
    /// 各プロバイダは SSE / JSONL を軽量に走査するため、完全な JSON パーサを通さず
    /// 手書きで <c>"key": "value"</c> を抜き出している。その手書き実装が個別に
    /// エスケープ処理を持っていたため、<c>\uXXXX</c> を扱えない実装が複数あった
    /// (issue #5)。エスケープの解釈だけをここに集約する。
    /// </summary>
    internal static class JsonStringUnescape
    {
        /// <summary>
        /// <paramref name="json"/> の <paramref name="backslashIndex"/> にある <c>\</c> から始まる
        /// エスケープを 1 個読み、復号結果を <paramref name="sb"/> に追記する。
        /// 戻り値は消費した次の位置 (呼び出し側はこれを i に代入する)。
        ///
        /// 仕様に無いエスケープや壊れた <c>\uXXXX</c> は <b>バックスラッシュごと原文のまま</b>
        /// 追記する。バックスラッシュを捨てると <c>&lt;</c> が <c>u003c</c> という
        /// 一見もっともらしいゴミ文字列に化け、壊れていることに気付けなくなるため
        /// (issue #5 で実際に起きた壊れ方)。
        ///
        /// サロゲートペア (絵文字など) は個別に処理しない。C# の string は UTF-16 なので、
        /// 上位・下位を続けて char として追記すれば正しい 1 文字に組み上がる。
        /// </summary>
        internal static int AppendEscape(string json, int backslashIndex, StringBuilder sb)
        {
            int i = backslashIndex;

            // 末尾が単独のバックスラッシュで終わっている (壊れた入力)。そのまま出して終える。
            if (i + 1 >= json.Length)
            {
                sb.Append('\\');
                return i + 1;
            }

            char next = json[i + 1];
            switch (next)
            {
                case '"':  sb.Append('"');  return i + 2;
                case '\\': sb.Append('\\'); return i + 2;
                case '/':  sb.Append('/');  return i + 2;
                case 'b':  sb.Append('\b'); return i + 2;
                case 'f':  sb.Append('\f'); return i + 2;
                case 'n':  sb.Append('\n'); return i + 2;
                case 'r':  sb.Append('\r'); return i + 2;
                case 't':  sb.Append('\t'); return i + 2;

                case 'u':
                    // \uXXXX — 16 進 4 桁。足りない・16 進でないなら原文のまま返す。
                    if (i + 5 < json.Length
                        && int.TryParse(json.Substring(i + 2, 4), NumberStyles.AllowHexSpecifier,
                                        CultureInfo.InvariantCulture, out int code))
                    {
                        sb.Append((char)code);
                        return i + 6;
                    }
                    sb.Append('\\').Append('u');
                    return i + 2;

                default:
                    sb.Append('\\').Append(next);
                    return i + 2;
            }
        }
    }
}

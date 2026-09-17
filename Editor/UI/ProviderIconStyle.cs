using AjisaiFlow.MD3SDK.Editor;
using AjisaiFlow.UnityAgent.Editor.Providers;
using UnityEngine;
using UnityEngine.UIElements;
using static AjisaiFlow.UnityAgent.Editor.L10n;

namespace AjisaiFlow.UnityAgent.Editor.UI
{
    /// <summary>
    /// プロバイダーを見分けるためのアイコン (Material Symbols のグリフ) と色。
    ///
    /// ── なぜ各社のロゴを使わないのか ──
    /// Google / Anthropic / OpenAI / Groq のいずれも、第三者の製品でロゴを使うには
    /// 事前の許可またはライセンスが要るとブランドガイドラインに書いている。そのため
    /// 公式ロゴ画像は同梱せず、似せた図形を自作することもしない。
    /// さらに Google はブランドカラーの流用も禁じているので、配色も各社のブランド色には
    /// 寄せない。下の色は「ライト / ダークのどちらの地色でも読めること」と
    /// 「同じグリフになるプロバイダーどうしが見分けられること」だけで決めた独自の配色で、
    /// むしろ各社のブランド色とは意図的にずらしてある
    /// (Claude は Anthropic のテラコッタではなく青緑、Gemini は Google の青/赤/黄/緑を
    ///  避けて紅紫、DeepSeek は青ではなく橙、Groq は橙ではなく緑、Perplexity は青緑ではなく琥珀)。
    ///
    /// ── グリフの選び方 ──
    /// グリフは「そのプロバイダーが何であるか」= つなぎ方を表す。事業者の API を直接叩くのか、
    /// ローカルの CLI を起動するのか、ブラウザごしなのか、といった区別で、ブランドの区別ではない。
    /// つなぎ方が同じプロバイダーは同じグリフになるので、その場合は色で見分ける
    /// (同じグリフの中では必ず違う色を当てている)。
    /// </summary>
    internal static class ProviderIconStyle
    {
        // ═══════════════════════════════════════════════════════
        //  配色
        // ═══════════════════════════════════════════════════════
        //
        // 1 色につきライト用とダーク用の 2 つを持つ。1 つの色で両方をまかなうと、
        // どちらかの地色に対してコントラストが足りなくなる。ライト用はほぼ白の Surface に
        // 対して読めるよう明度を落とし、ダーク用は暗い Surface に対して読めるよう明度を上げた。
        // どちらもおおよそコントラスト比 6:1 以上になる明度に置いてある。
        // Chip / メニュー項目の地色 (Surface, SurfaceContainerLow, 選択時の SecondaryContainer)
        // はいずれもテーマの端に寄った色なので、この 2 択で足りる。

        static readonly Color MagentaLight = new Color32(0x9A, 0x2F, 0xA6, 0xFF);
        static readonly Color MagentaDark  = new Color32(0xEF, 0xB0, 0xF7, 0xFF);

        static readonly Color TealLight    = new Color32(0x00, 0x69, 0x6A, 0xFF);
        static readonly Color TealDark     = new Color32(0x4F, 0xD9, 0xDA, 0xFF);

        static readonly Color VioletLight  = new Color32(0x53, 0x43, 0xC9, 0xFF);
        static readonly Color VioletDark   = new Color32(0xBE, 0xB2, 0xFF, 0xFF);

        static readonly Color OrangeLight  = new Color32(0x9A, 0x4A, 0x00, 0xFF);
        static readonly Color OrangeDark   = new Color32(0xFF, 0xB7, 0x7C, 0xFF);

        static readonly Color RoseLight    = new Color32(0xA6, 0x2A, 0x55, 0xFF);
        static readonly Color RoseDark     = new Color32(0xFF, 0xB0, 0xC4, 0xFF);

        static readonly Color BlueLight    = new Color32(0x1F, 0x5F, 0xC8, 0xFF);
        static readonly Color BlueDark     = new Color32(0xA8, 0xC7, 0xFF, 0xFF);

        static readonly Color GreenLight   = new Color32(0x2C, 0x6B, 0x2F, 0xFF);
        static readonly Color GreenDark    = new Color32(0x92, 0xD9, 0x92, 0xFF);

        static readonly Color CyanLight    = new Color32(0x00, 0x66, 0x81, 0xFF);
        static readonly Color CyanDark     = new Color32(0x66, 0xD4, 0xF5, 0xFF);

        static readonly Color AmberLight   = new Color32(0x7C, 0x58, 0x00, 0xFF);
        static readonly Color AmberDark    = new Color32(0xF2, 0xC2, 0x47, 0xFF);

        static readonly Color BrownLight   = new Color32(0x6B, 0x52, 0x38, 0xFF);
        static readonly Color BrownDark    = new Color32(0xDC, 0xBD, 0x98, 0xFF);

        // 青灰。特定の事業者に紐づかない経路 (任意のサーバー / ブラウザ / 外部エージェント) 用。
        static readonly Color SlateLight   = new Color32(0x4F, 0x5B, 0x6B, 0xFF);
        static readonly Color SlateDark    = new Color32(0xB6, 0xC3, 0xD4, 0xFF);

        // ═══════════════════════════════════════════════════════
        //  グリフ
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// そのプロバイダーのつなぎ方を表す Material Symbols のグリフ。
        ///
        /// 新しいプロバイダーを足したらここにも足すこと。未割当のものは
        /// <see cref="MD3Icon.SmartToy"/> に落ちる (存在しない定数を書くと
        /// コンパイルは通っても atlas に無い文字として □ になるため、既定値も実在の定数にしてある)。
        /// </summary>
        public static string Glyph(LLMProviderType type)
        {
            switch (type)
            {
                // 事業者の API を直接叩く。6 つとも同じグリフなので色で見分ける。
                case LLMProviderType.Gemini:
                case LLMProviderType.Claude_API:
                case LLMProviderType.OpenAI:
                case LLMProviderType.DeepSeek:
                case LLMProviderType.xAI_Grok:
                case LLMProviderType.Mistral:
                    return MD3Icon.Api;

                // ローカルにインストールした CLI を起動する。4 つとも同じグリフなので色で見分ける。
                case LLMProviderType.Claude_CLI:
                case LLMProviderType.Gemini_CLI:
                case LLMProviderType.Codex_CLI:
                case LLMProviderType.Antigravity_CLI:
                    return MD3Icon.Terminal;

                // 自分で用意したサーバー (LM Studio など) につなぐ。Dns はサーバーラックの絵。
                case LLMProviderType.OpenAI_Compatible:
                    return MD3Icon.Dns;

                // この PC の上でモデルを動かす。
                case LLMProviderType.Ollama:
                    return MD3Icon.Computer;

                // クラウド基盤 (プロジェクトとリージョンを指定する) ごしにつなぐ。
                case LLMProviderType.Vertex_AI:
                    return MD3Icon.Cloud;

                // 推論に特化したサービス。
                case LLMProviderType.Groq:
                    return MD3Icon.Bolt;

                // Web 検索と組み合わせて答えるサービス。
                case LLMProviderType.Perplexity:
                    return MD3Icon.TravelExplore;

                // ブラウザの拡張ごしにやり取りする。
                case LLMProviderType.Gemini_Web:
                    return MD3Icon.Web;

                // API を使わず、手でコピーして貼り付ける。
                case LLMProviderType.Clipboard:
                    return MD3Icon.ContentPaste;

                // 外部のエージェントが UnityAgent につなぐ。UnityAgent 側は LLM を呼ばない。
                case LLMProviderType.MCPServer:
                    return MD3Icon.Hub;

                default:
                    return MD3Icon.SmartToy;
            }
        }

        // ═══════════════════════════════════════════════════════
        //  色
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// そのプロバイダーのアイコンの色。テーマに合わせてライト用 / ダーク用を出し分ける。
        /// theme が null のときは判断できないので、既定のアイコン色に落とす。
        /// </summary>
        public static Color Tint(LLMProviderType type, MD3Theme theme)
        {
            bool dark = theme != null && theme.IsDark;
            switch (type)
            {
                // 紅紫。Google のブランド色 (青 / 赤 / 黄 / 緑) のどれとも重ならない色を選んだ。
                case LLMProviderType.Gemini:
                case LLMProviderType.Gemini_CLI:
                case LLMProviderType.Vertex_AI:
                    return dark ? MagentaDark : MagentaLight;

                // 青緑。Anthropic のブランド色 (テラコッタ) の反対側。
                case LLMProviderType.Claude_API:
                case LLMProviderType.Claude_CLI:
                    return dark ? TealDark : TealLight;

                // 青紫。OpenAI のブランド色 (黒 / 白と緑寄りの色) のどれとも重ならない。
                case LLMProviderType.OpenAI:
                case LLMProviderType.Codex_CLI:
                    return dark ? VioletDark : VioletLight;

                // 橙。DeepSeek のブランド色は青、Antigravity (Google) のブランド色にも橙は無い。
                case LLMProviderType.DeepSeek:
                case LLMProviderType.Antigravity_CLI:
                    return dark ? OrangeDark : OrangeLight;

                // 赤紫。xAI のブランド色は黒 / 白。
                case LLMProviderType.xAI_Grok:
                    return dark ? RoseDark : RoseLight;

                // 青。Mistral のブランド色は橙から黄のグラデーション。
                case LLMProviderType.Mistral:
                    return dark ? BlueDark : BlueLight;

                // 緑。Groq のブランド色は橙から赤。
                case LLMProviderType.Groq:
                    return dark ? GreenDark : GreenLight;

                // 水。Ollama のブランド色は黒 / 白。
                case LLMProviderType.Ollama:
                    return dark ? CyanDark : CyanLight;

                // 琥珀。Perplexity のブランド色は青緑。
                case LLMProviderType.Perplexity:
                    return dark ? AmberDark : AmberLight;

                // 茶。事業者に紐づかない手動の経路なので、他とは別系統の色を当てた。
                case LLMProviderType.Clipboard:
                    return dark ? BrownDark : BrownLight;

                // 青灰。どの事業者にも紐づかない経路 (任意のサーバー / ブラウザ / 外部エージェント)。
                case LLMProviderType.OpenAI_Compatible:
                case LLMProviderType.Gemini_Web:
                case LLMProviderType.MCPServer:
                    return dark ? SlateDark : SlateLight;

                default:
                    return theme != null ? theme.OnSurfaceVariant : (dark ? SlateDark : SlateLight);
            }
        }

        // ═══════════════════════════════════════════════════════
        //  つなぎ方の説明 (tooltip)
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// グリフだけでは伝わりきらないので、Chip とメニュー項目の tooltip に出す一行。
        /// 同じグリフのプロバイダーは同じ説明になる。
        /// </summary>
        public static string RouteHint(LLMProviderType type)
        {
            switch (type)
            {
                case LLMProviderType.Gemini:
                case LLMProviderType.Claude_API:
                case LLMProviderType.OpenAI:
                case LLMProviderType.DeepSeek:
                case LLMProviderType.xAI_Grok:
                case LLMProviderType.Mistral:
                    return M("事業者の API に直接つなぐ");

                case LLMProviderType.Claude_CLI:
                case LLMProviderType.Gemini_CLI:
                case LLMProviderType.Codex_CLI:
                case LLMProviderType.Antigravity_CLI:
                    return M("ローカルの CLI を起動する");

                case LLMProviderType.OpenAI_Compatible:
                    return M("自分で用意したサーバーにつなぐ");

                case LLMProviderType.Ollama:
                    return M("この PC の上でモデルを動かす");

                case LLMProviderType.Vertex_AI:
                    return M("クラウド基盤ごしにつなぐ");

                case LLMProviderType.Groq:
                    return M("推論に特化したサービスにつなぐ");

                case LLMProviderType.Perplexity:
                    return M("Web 検索と組み合わせて答えるサービス");

                case LLMProviderType.Gemini_Web:
                    return M("ブラウザごしにやり取りする");

                case LLMProviderType.Clipboard:
                    return M("手でコピーして貼り付ける");

                case LLMProviderType.MCPServer:
                    return M("外部のエージェントが UnityAgent につなぐ");

                default:
                    return null;
            }
        }

        // ═══════════════════════════════════════════════════════
        //  生成
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// そのプロバイダーのアイコン Label を作る。
        /// MD3Icon.Create が返す Label は pickingMode = Ignore なので、Chip やメニュー項目の
        /// 先頭に差し込んでもクリックはそのまま親に届く。
        /// </summary>
        public static Label CreateIcon(LLMProviderType type, MD3Theme theme, float size = 16f)
            => MD3Icon.Create(Glyph(type), size, Tint(type, theme));
    }
}

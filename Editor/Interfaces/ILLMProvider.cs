using System;
using System.Collections;

namespace AjisaiFlow.UnityAgent.Editor.Interfaces
{
    /// <summary>
    /// 構造化ストリーミングイベントの種類。プロバイダが SSE 等で区別できる場合に利用する。
    /// </summary>
    public enum StreamEventKind
    {
        /// <summary>本文テキスト (通常の assistant 応答)。</summary>
        Text,
        /// <summary>思考過程 (Claude thinking_delta, Gemini part.thought, OpenAI reasoning_content)。</summary>
        Thinking,
        /// <summary>ツール呼び出しヒント (将来用、P3 では未使用)。</summary>
        ToolCallHint,
    }

    /// <summary>
    /// プロバイダが発火する構造化ストリーミングイベント。
    /// </summary>
    public readonly struct ChatStreamEvent
    {
        public readonly StreamEventKind Kind;
        public readonly string Chunk;

        public ChatStreamEvent(StreamEventKind kind, string chunk)
        {
            Kind = kind;
            Chunk = chunk;
        }
    }

    public interface ILLMProvider
    {
        string ProviderName { get; }

        /// <summary>
        /// LLM を呼び出す。<paramref name="onStreamEvent"/> は構造化ストリームを受け取る optional コールバックで、
        /// 対応プロバイダは <see cref="StreamEventKind.Text"/> / <see cref="StreamEventKind.Thinking"/> を
        /// 区別して発火する。未対応プロバイダは null を無視してよい。
        /// 既存の <paramref name="onPartialResponse"/> は後方互換のためそのまま残す。
        /// </summary>
        IEnumerator CallLLM(
            System.Collections.Generic.IEnumerable<Message> history,
            Action<string> onSuccess,
            Action<string> onError,
            Action<string> onStatus = null,
            Action<string> onDebugLog = null,
            Action<string> onPartialResponse = null,
            Action<ChatStreamEvent> onStreamEvent = null);

        /// <summary>進行中のLLMリクエストを中断する。</summary>
        void Abort();
    }
}

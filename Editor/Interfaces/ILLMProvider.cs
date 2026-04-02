using System;
using System.Collections;

namespace AjisaiFlow.UnityAgent.Editor.Interfaces
{
    public interface ILLMProvider
    {
        string ProviderName { get; }
        IEnumerator CallLLM(System.Collections.Generic.IEnumerable<Message> history, Action<string> onSuccess, Action<string> onError, Action<string> onStatus = null, Action<string> onDebugLog = null, Action<string> onPartialResponse = null);

        /// <summary>進行中のLLMリクエストを中断する。</summary>
        void Abort();
    }
}

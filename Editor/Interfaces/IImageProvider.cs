using System;
using System.Collections;

namespace AjisaiFlow.UnityAgent.Editor.Interfaces
{
    public interface IImageProvider
    {
        string ProviderName { get; }

        IEnumerator GenerateImage(
            string systemPrompt, string userPrompt, byte[] inputImagePng,
            Action<byte[], string> onSuccess, Action<string> onError,
            Action<string> onStatus = null, Action<string> onDebugLog = null);

        void Abort();
    }
}

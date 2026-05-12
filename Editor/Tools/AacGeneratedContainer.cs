using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// AnimatorAsCode V1 AssetContainer 用の空 ScriptableObject。
    /// AAC が生成する AnimatorController / AnimationClip / BlendTree は
    /// このアセットのサブアセットとして AssetDatabase.AddObjectToAsset で
    /// まとめて永続化される（ContainerMode.Everything 前提）。
    /// </summary>
    internal sealed class AacGeneratedContainer : ScriptableObject { }
}

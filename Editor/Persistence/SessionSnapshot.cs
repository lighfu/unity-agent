using System;

namespace AjisaiFlow.UnityAgent.Editor.Persistence
{
    /// <summary>
    /// Domain reload を跨いで UnityAgentWindow / UnityAgentCore の状態を再現するためのシリアライズ用 DTO。
    /// JsonUtility 互換のため public フィールドのみ、null を扱わずに空文字列・空配列で表現する。
    ///
    /// 注意:
    /// - JsonUtility は null reference をシリアライズしないので、入れ子オブジェクトの "存在しない" 状態は
    ///   pngBase64.Length == 0 や bytesBase64.Length == 0 で判定する。
    /// - byte[] は JSON 数値配列に展開されると肥大化するため、すべて Base64 文字列で保持する。
    /// </summary>
    [Serializable]
    public class SessionSnapshot
    {
        public int version = 2;

        /// <summary>ISO 8601 UTC. 古すぎるスナップショットの破棄判定にも使える。</summary>
        public string savedAt = "";

        /// <summary>reload 直前に LLM 呼び出しが進行中だったか (自動リトライ判定)。</summary>
        public bool wasProcessing;

        /// <summary>復元時に provider 設定が一致しているかチェック (LLMProviderType 数値)。</summary>
        public int providerType;
        public string modelName = "";

        /// <summary>UI 表示用のチャット履歴 (ChatEntry 列)。</summary>
        public ChatEntrySnapshot[] chatHistory = Array.Empty<ChatEntrySnapshot>();

        /// <summary>LLM API に送る会話コンテキスト (Message 列)。chatHistory とペアで保持する。</summary>
        public LlmMessageSnapshot[] llmHistory = Array.Empty<LlmMessageSnapshot>();

        // Window-level transient
        public string userQuery = "";
        public string currentToolStatus = "";
        public bool showHistory;
        public string[] recentQueries = Array.Empty<string>();

        public PendingAttachmentSnapshot pendingAttachment = new PendingAttachmentSnapshot();

        // Token usage continuity
        public int sessionTotalTokens;
        public int sessionInputTokens;
        public int sessionOutputTokens;
        public int lastPromptTokens;
        public int sessionUndoCount;
    }

    [Serializable]
    public class ChatEntrySnapshot
    {
        public int type;                      // ChatEntry.EntryType (cast to int)
        public string text = "";
        public string thinkingText = "";
        public bool thinkingFoldout;
        public string timestamp = "";         // ISO 8601

        public string[] choiceOptions = Array.Empty<string>();
        public string choiceImportance = "info";
        public int choiceSelectedIndex = -1;
        public bool isToolConfirm;

        public bool isBatchToolConfirm;
        public BatchToolItemSnapshot[] batchItems = Array.Empty<BatchToolItemSnapshot>();
        public bool batchResolved;

        public bool isClipboard;

        public string[] debugLogs = Array.Empty<string>();
        public bool debugFoldout;
        public long requestDurationTicks = -1; // -1 = no duration recorded

        public ImageSnapshot imagePreview = new ImageSnapshot();
        public ResultItemSnapshot[] results = Array.Empty<ResultItemSnapshot>();

        // ── ToolCall entry fields (schema v2+) ──
        public string toolCallId = "";
        public string toolName = "";
        public string toolArgsRaw = "";
        public string toolResult = "";
        public int toolStatus;                // ChatEntry.ToolCallStatus (int)
        public string toolStartedUtcIso = ""; // ISO 8601 UTC
        public long toolDurationMs;
        public string toolCategory = "";
    }

    [Serializable]
    public class BatchToolItemSnapshot
    {
        public string toolName = "";
        public string description = "";
        public string parameters = "";
        public bool approved;
    }

    [Serializable]
    public class ResultItemSnapshot
    {
        public string displayName = "";
        public string typeName = "";
        public string reference = "";
        public bool isAsset;
    }

    /// <summary>
    /// 縮小済み PNG (最大 1024 px) の Base64 表現。
    /// pngBase64 が空文字列なら "画像なし" とみなす。
    /// </summary>
    [Serializable]
    public class ImageSnapshot
    {
        public int width;
        public int height;
        public string pngBase64 = "";

        public bool HasImage => !string.IsNullOrEmpty(pngBase64) && width > 0 && height > 0;
    }

    [Serializable]
    public class LlmMessageSnapshot
    {
        public string role = "";
        public LlmPartSnapshot[] parts = Array.Empty<LlmPartSnapshot>();
    }

    [Serializable]
    public class LlmPartSnapshot
    {
        public string text = "";
        /// <summary>LLM 送信用画像。元サイズで保持 (縮小しない)。空文字列なら画像なし。</summary>
        public string imageBase64 = "";
        public string imageMimeType = "";
    }

    /// <summary>
    /// 送信前の添付ファイル。bytesBase64 は元サイズ (縮小しない)、preview は表示用に縮小したもの。
    /// 元サイズが ChatSessionPersistence.MaxAttachmentBytes を超える場合は永続化スキップ。
    /// </summary>
    [Serializable]
    public class PendingAttachmentSnapshot
    {
        public string filename = "";
        public string mimeType = "";
        public string bytesBase64 = "";
        public ImageSnapshot preview = new ImageSnapshot();

        public bool HasAttachment => !string.IsNullOrEmpty(bytesBase64);
    }
}

namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// ClipboardProvider の待機状態を管理する静的クラス。
    /// UnityAgentCore のコルーチンが IsSubmitted を監視し、
    /// UI（UnityAgentWindow）がユーザーの入力を受け取って Submit() を呼ぶ。
    /// </summary>
    public static class ClipboardProviderState
    {
        /// <summary>ユーザーの入力待ち中かどうか。</summary>
        public static bool IsPending { get; private set; }

        /// <summary>ユーザーが送信ボタンを押したかどうか（Cancel も true にする）。</summary>
        public static bool IsSubmitted { get; private set; }

        /// <summary>クリップボードにコピーされたプロンプト全文。再コピー用。</summary>
        public static string PromptText { get; private set; }

        /// <summary>UI の TextArea に入力中のテキスト（UI から直接書き込まれる）。</summary>
        public static string PendingResponse;

        /// <summary>Submit() 後に確定した応答テキスト。</summary>
        public static string Response { get; private set; }

        public static void Request(string promptText)
        {
            PromptText = promptText;
            PendingResponse = "";
            IsPending = true;
            IsSubmitted = false;
            Response = null;
        }

        /// <summary>UI の「送信」ボタンから呼び出す。</summary>
        public static void Submit()
        {
            Response = PendingResponse;
            IsSubmitted = true;
            IsPending = false;
        }

        /// <summary>UI の「キャンセル」ボタンから呼び出す。空文字列で完了させる。</summary>
        public static void Cancel()
        {
            Response = null;
            IsSubmitted = true;
            IsPending = false;
        }

        public static void Clear()
        {
            IsPending = false;
            IsSubmitted = false;
            PromptText = null;
            PendingResponse = "";
            Response = null;
        }
    }
}

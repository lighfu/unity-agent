namespace AjisaiFlow.UnityAgent.Editor
{
    /// <summary>
    /// BrowserBridgeProvider の状態を管理する静的クラス。
    /// WebSocket サーバースレッドから応答を受け取り、コルーチンが完了を監視する。
    /// </summary>
    public static class BrowserBridgeState
    {
        private static readonly object _lock = new object();

        private static bool _isConnected;
        private static bool _isPending;
        private static bool _isCompleted;
        private static bool _hasError;
        private static string _partialResponse;
        private static string _finalResponse;
        private static string _errorMessage;
        private static string _currentRequestId;
        private static string _connectedSiteUrl;

        /// <summary>Chrome 拡張機能が WebSocket で接続中かどうか。</summary>
        public static bool IsConnected { get { lock (_lock) return _isConnected; } }

        /// <summary>接続中のサイト URL (location.href)。</summary>
        public static string ConnectedSiteUrl { get { lock (_lock) return _connectedSiteUrl; } }

        /// <summary>プロンプト送信後、応答待ち中かどうか。</summary>
        public static bool IsPending { get { lock (_lock) return _isPending; } }

        /// <summary>応答が完了したかどうか。</summary>
        public static bool IsCompleted { get { lock (_lock) return _isCompleted; } }

        /// <summary>エラーが発生したかどうか。</summary>
        public static bool HasError { get { lock (_lock) return _hasError; } }

        /// <summary>ストリーミング中の部分応答テキスト。</summary>
        public static string PartialResponse { get { lock (_lock) return _partialResponse ?? ""; } }

        /// <summary>完了後の最終応答テキスト。</summary>
        public static string FinalResponse { get { lock (_lock) return _finalResponse; } }

        /// <summary>エラーメッセージ。</summary>
        public static string ErrorMessage { get { lock (_lock) return _errorMessage; } }

        /// <summary>現在のリクエスト ID。</summary>
        public static string CurrentRequestId { get { lock (_lock) return _currentRequestId; } }

        /// <summary>新しいリクエストを開始する。</summary>
        public static void Request(string requestId)
        {
            lock (_lock)
            {
                _currentRequestId = requestId;
                _isPending = true;
                _isCompleted = false;
                _hasError = false;
                _partialResponse = "";
                _finalResponse = null;
                _errorMessage = null;
            }
        }

        /// <summary>ストリーミング部分応答を更新する（サーバースレッドから呼ばれる）。</summary>
        public static void OnPartial(string requestId, string text)
        {
            lock (_lock)
            {
                if (_currentRequestId != requestId) return;
                _partialResponse = text;
            }
        }

        /// <summary>応答完了（サーバースレッドから呼ばれる）。</summary>
        public static void OnComplete(string requestId, string text)
        {
            lock (_lock)
            {
                if (_currentRequestId != requestId) return;
                _finalResponse = text;
                _isCompleted = true;
                _isPending = false;
            }
        }

        /// <summary>エラー発生（サーバースレッドから呼ばれる）。</summary>
        public static void OnError(string requestId, string message)
        {
            lock (_lock)
            {
                if (_currentRequestId != requestId) return;
                _errorMessage = message;
                _hasError = true;
                _isCompleted = true;
                _isPending = false;
            }
        }

        /// <summary>接続状態を更新する（サーバースレッドから呼ばれる）。</summary>
        public static void SetConnected(bool connected)
        {
            lock (_lock)
            {
                _isConnected = connected;
                if (!connected) _connectedSiteUrl = null;
            }
        }

        /// <summary>接続先サイト URL を設定する（サーバースレッドから呼ばれる）。</summary>
        public static void SetSiteUrl(string url)
        {
            lock (_lock) { _connectedSiteUrl = url; }
        }

        /// <summary>状態をリセットする。</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _isConnected = false;
                _isPending = false;
                _isCompleted = false;
                _hasError = false;
                _partialResponse = "";
                _finalResponse = null;
                _errorMessage = null;
                _currentRequestId = null;
                _connectedSiteUrl = null;
            }
        }
    }
}

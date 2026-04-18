using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class AgentWebStatus
    {
        public bool isProcessing;
        public string modelName;
        public int sessionTotalTokens;
        public int sessionInputTokens;
        public int sessionOutputTokens;
        public int lastPromptTokens;
        public int maxContextTokens;
        public string estimatedCost;
        public string currentTool;
    }

    public class WebMessage
    {
        public string text;
        public byte[] imageBytes;
        public string imageMimeType;
    }

    public class AgentWebServer
    {
        private HttpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;
        private readonly object _lock = new object();
        private string _cachedStatusJson = "{}";
        private string _cachedChatJson = "{\"messages\":[]}";
        private string _cachedHtml;
        private int _port;
        private string _username;
        private string _password;
        private string _basicAuthToken;
        private readonly Queue<WebMessage> _pendingMessages = new Queue<WebMessage>();
        private volatile bool _cachedIsProcessing;

        public bool IsRunning => _running;

        public void Start(int port, string username = "", string password = "")
        {
            if (_running) return;

            _port = port;
            _username = username ?? "";
            _password = password ?? "";
            _basicAuthToken = !string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password)
                ? Convert.ToBase64String(Encoding.UTF8.GetBytes(_username + ":" + _password))
                : null;
            _cachedHtml = GenerateHtml(port);

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://+:{port}/");

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                // Fallback: localhost only (no admin rights for +)
                _listener.Close();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
            }

            _running = true;
            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "AgentWebServer"
            };
            _listenerThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception) { }
            _listener = null;
            _listenerThread = null;
        }

        public WebMessage DequeueMessage()
        {
            lock (_lock)
            {
                return _pendingMessages.Count > 0 ? _pendingMessages.Dequeue() : null;
            }
        }

        public void UpdateCache(AgentWebStatus status, List<ChatEntry> chatHistory)
        {
            var statusJson = BuildStatusJson(status);
            var chatJson = BuildChatJson(chatHistory);

            lock (_lock)
            {
                _cachedStatusJson = statusJson;
                _cachedChatJson = chatJson;
            }
            _cachedIsProcessing = status.isProcessing;
        }

        private void ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch (Exception)
                {
                    break;
                }

                try
                {
                    HandleRequest(ctx);
                }
                catch (Exception) { }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url.AbsolutePath;
            var method = ctx.Request.HttpMethod;

            // Basic Authentication
            if (_basicAuthToken != null)
            {
                var authHeader = ctx.Request.Headers["Authorization"];
                bool authenticated = false;
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Basic "))
                {
                    var token = authHeader.Substring(6).Trim();
                    authenticated = token == _basicAuthToken;
                }
                if (!authenticated)
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"Unity Agent\"");
                    var body = Encoding.UTF8.GetBytes("Unauthorized");
                    ctx.Response.ContentLength64 = body.Length;
                    ctx.Response.OutputStream.Write(body, 0, body.Length);
                    ctx.Response.OutputStream.Close();
                    return;
                }
            }

            // CORS headers for local development
            ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
            ctx.Response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            ctx.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");

            // CORS preflight
            if (method == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.ContentLength64 = 0;
                ctx.Response.OutputStream.Close();
                return;
            }

            // POST /api/send
            if (path == "/api/send" && method == "POST")
            {
                HandleSendMessage(ctx);
                return;
            }

            string responseBody;
            string contentType;

            switch (path)
            {
                case "/api/status":
                    lock (_lock) { responseBody = _cachedStatusJson; }
                    contentType = "application/json; charset=utf-8";
                    break;

                case "/api/chat":
                    lock (_lock) { responseBody = _cachedChatJson; }
                    contentType = "application/json; charset=utf-8";
                    break;

                default:
                    responseBody = _cachedHtml;
                    contentType = "text/html; charset=utf-8";
                    break;
            }

            ctx.Response.ContentType = contentType;

            var bytes = Encoding.UTF8.GetBytes(responseBody);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private void HandleSendMessage(HttpListenerContext ctx)
        {
            string body;
            using (var reader = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
            {
                body = reader.ReadToEnd();
            }

            // Extract fields from JSON
            string message = ExtractJsonStringField(body, "message");
            string imageBase64 = ExtractJsonStringField(body, "imageBase64");
            string imageMimeType = ExtractJsonStringField(body, "imageMimeType");

            // Allow image-only messages (empty text with image is OK)
            bool hasText = !string.IsNullOrEmpty(message);
            bool hasImage = !string.IsNullOrEmpty(imageBase64);

            string responseBody;
            if (!hasText && !hasImage)
            {
                responseBody = "{\"ok\":false,\"error\":\"empty\"}";
            }
            else if (_cachedIsProcessing)
            {
                responseBody = "{\"ok\":false,\"error\":\"busy\"}";
            }
            else
            {
                var webMsg = new WebMessage { text = message ?? "" };

                if (hasImage)
                {
                    try
                    {
                        webMsg.imageBytes = Convert.FromBase64String(imageBase64);
                        webMsg.imageMimeType = !string.IsNullOrEmpty(imageMimeType) ? imageMimeType : "image/png";
                    }
                    catch (FormatException)
                    {
                        responseBody = "{\"ok\":false,\"error\":\"invalid_image\"}";
                        ctx.Response.ContentType = "application/json; charset=utf-8";
                        var errBytes = Encoding.UTF8.GetBytes(responseBody);
                        ctx.Response.ContentLength64 = errBytes.Length;
                        ctx.Response.OutputStream.Write(errBytes, 0, errBytes.Length);
                        ctx.Response.OutputStream.Close();
                        return;
                    }
                }

                lock (_lock)
                {
                    _pendingMessages.Enqueue(webMsg);
                }
                responseBody = "{\"ok\":true}";
            }

            ctx.Response.ContentType = "application/json; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(responseBody);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private static string ExtractJsonStringField(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string key = "\"" + field + "\"";
            int keyIdx = json.IndexOf(key);
            if (keyIdx < 0) return null;
            int colonIdx = json.IndexOf(':', keyIdx + key.Length);
            if (colonIdx < 0) return null;
            int startQuote = json.IndexOf('"', colonIdx + 1);
            if (startQuote < 0) return null;

            var sb = new StringBuilder();
            for (int i = startQuote + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    if (next == '"') { sb.Append('"'); i++; }
                    else if (next == '\\') { sb.Append('\\'); i++; }
                    else if (next == 'n') { sb.Append('\n'); i++; }
                    else if (next == 'r') { sb.Append('\r'); i++; }
                    else if (next == 't') { sb.Append('\t'); i++; }
                    else { sb.Append(c); }
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string BuildStatusJson(AgentWebStatus s)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"isProcessing\":").Append(s.isProcessing ? "true" : "false");
            sb.Append(",\"modelName\":").Append(JsonEscape(s.modelName ?? ""));
            sb.Append(",\"sessionTotalTokens\":").Append(s.sessionTotalTokens);
            sb.Append(",\"sessionInputTokens\":").Append(s.sessionInputTokens);
            sb.Append(",\"sessionOutputTokens\":").Append(s.sessionOutputTokens);
            sb.Append(",\"lastPromptTokens\":").Append(s.lastPromptTokens);
            sb.Append(",\"maxContextTokens\":").Append(s.maxContextTokens);
            sb.Append(",\"estimatedCost\":").Append(JsonEscape(s.estimatedCost ?? ""));
            sb.Append(",\"currentTool\":").Append(JsonEscape(s.currentTool ?? ""));
            sb.Append('}');
            return sb.ToString();
        }

        private static string BuildChatJson(List<ChatEntry> history)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"messages\":[");

            if (history != null)
            {
                bool first = true;
                for (int i = 0; i < history.Count; i++)
                {
                    var entry = history[i];
                    if (!first) sb.Append(',');
                    first = false;

                    sb.Append('{');
                    sb.Append("\"type\":").Append(JsonEscape(entry.type.ToString()));
                    sb.Append(",\"text\":").Append(JsonEscape(entry.text ?? ""));
                    sb.Append(",\"timestamp\":").Append(JsonEscape(
                        entry.timestamp != default(DateTime) ? entry.timestamp.ToString("HH:mm") : ""));
                    sb.Append(",\"thinkingText\":").Append(
                        string.IsNullOrEmpty(entry.thinkingText) ? "null" : JsonEscape(entry.thinkingText));
                    sb.Append('}');
                }
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string GenerateHtml(int port)
        {
            return @"<!DOCTYPE html>
<html lang=""ja"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Unity AI Agent Monitor</title>
<style>
* { margin: 0; padding: 0; box-sizing: border-box; }
body {
  background: #1a1a2e;
  color: #e0e0e0;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  height: 100vh;
  display: flex;
  flex-direction: column;
}

/* Status Bar */
.status-bar {
  background: #16213e;
  padding: 12px 20px;
  border-bottom: 1px solid #0f3460;
  display: flex;
  align-items: center;
  gap: 20px;
  flex-wrap: wrap;
}
.status-indicator {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
}
.status-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: #555;
}
.status-dot.active {
  background: #00d26a;
  box-shadow: 0 0 8px #00d26a;
  animation: pulse 1.5s infinite;
}
@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}
.status-item {
  font-size: 13px;
  color: #a0a0c0;
}
.status-item .label { color: #7070a0; }
.status-item .value { color: #e0e0f0; font-weight: 500; }

/* Context Progress */
.context-bar-container {
  flex: 1;
  min-width: 200px;
  max-width: 400px;
}
.context-bar {
  height: 8px;
  background: #0f3460;
  border-radius: 4px;
  overflow: hidden;
}
.context-bar-fill {
  height: 100%;
  background: #4a9eff;
  border-radius: 4px;
  transition: width 0.5s ease;
}
.context-bar-fill.warning { background: #f0ad4e; }
.context-bar-fill.danger { background: #d9534f; }
.context-label {
  font-size: 11px;
  color: #7070a0;
  margin-top: 2px;
}

/* Current Tool */
.current-tool {
  background: #1a1a3e;
  padding: 6px 16px;
  font-size: 12px;
  color: #80a0c0;
  border-bottom: 1px solid #0f3460;
  font-family: 'Fira Code', 'Cascadia Code', monospace;
  display: none;
}
.current-tool.visible { display: block; }

/* Chat Area */
.chat-area {
  flex: 1;
  overflow-y: auto;
  padding: 16px 20px;
}
.message {
  margin-bottom: 12px;
  max-width: 80%;
  animation: fadeIn 0.3s ease;
}
@keyframes fadeIn {
  from { opacity: 0; transform: translateY(8px); }
  to { opacity: 1; transform: translateY(0); }
}
.message.User {
  margin-left: auto;
}
.message.Agent {
  margin-right: auto;
}
.message.Info, .message.Error {
  margin: 4px auto;
  max-width: 90%;
}
.message.Choice {
  margin-right: auto;
}

.bubble {
  padding: 10px 14px;
  border-radius: 12px;
  font-size: 14px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
}
.User .bubble {
  background: #1a3a5c;
  color: #c0d8f0;
  border-bottom-right-radius: 4px;
}
.Agent .bubble {
  background: #2a2a3a;
  color: #d0d0d8;
  border-bottom-left-radius: 4px;
}
.Info .bubble {
  background: rgba(30, 60, 30, 0.4);
  color: #80b080;
  font-size: 12px;
  padding: 6px 12px;
  border-radius: 8px;
  cursor: pointer;
}
.Error .bubble {
  background: rgba(80, 30, 30, 0.5);
  color: #f0a0a0;
  font-size: 13px;
}
.Choice .bubble {
  background: #1a2a4c;
  color: #a0c0f0;
}

.msg-meta {
  font-size: 11px;
  color: #606080;
  margin-bottom: 2px;
  padding: 0 4px;
}
.User .msg-meta { text-align: right; }

.thinking-toggle {
  font-size: 12px;
  color: #7070a0;
  cursor: pointer;
  margin-bottom: 4px;
  padding: 0 4px;
}
.thinking-toggle:hover { color: #9090c0; }
.thinking-content {
  background: rgba(40, 40, 60, 0.5);
  color: #9090b0;
  font-size: 12px;
  font-style: italic;
  padding: 8px 12px;
  border-radius: 8px;
  margin-bottom: 4px;
  white-space: pre-wrap;
  display: none;
}
.thinking-content.open { display: block; }

/* Info group collapse */
.info-group {
  margin: 4px auto;
  max-width: 90%;
}
.info-group-toggle {
  font-size: 12px;
  color: #70a070;
  cursor: pointer;
  padding: 4px 8px;
  background: rgba(30, 50, 30, 0.3);
  border-radius: 6px;
  display: inline-block;
}
.info-group-toggle:hover { background: rgba(30, 50, 30, 0.5); }
.info-group-items { display: none; }
.info-group-items.open { display: block; }

/* Input Area */
.input-area {
  background: #16213e;
  padding: 10px 16px;
  border-top: 1px solid #0f3460;
}
.input-row {
  display: flex;
  gap: 8px;
  align-items: flex-end;
}
.input-left {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.input-left textarea {
  width: 100%;
  background: #1a1a2e;
  color: #e0e0e0;
  border: 1px solid #0f3460;
  border-radius: 8px;
  padding: 8px 12px;
  font-size: 14px;
  font-family: inherit;
  resize: none;
  min-height: 40px;
  max-height: 120px;
  outline: none;
}
.input-left textarea:focus {
  border-color: #4a9eff;
}
.input-left textarea:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.input-buttons {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.input-buttons button {
  border: none;
  border-radius: 8px;
  padding: 8px 16px;
  font-size: 14px;
  cursor: pointer;
  white-space: nowrap;
  height: 36px;
}
.input-buttons button:disabled {
  background: #555;
  cursor: not-allowed;
}
#sendBtn {
  background: #4a9eff;
  color: #fff;
}
#sendBtn:hover:not(:disabled) { background: #3a8eef; }
#imgBtn {
  background: #3a3a5e;
  color: #a0a0c0;
  font-size: 18px;
  padding: 4px 16px;
}
#imgBtn:hover:not(:disabled) { background: #4a4a6e; }

/* Image preview */
.img-preview {
  display: none;
  align-items: center;
  gap: 8px;
  padding: 6px 0 2px 0;
}
.img-preview.visible { display: flex; }
.img-preview img {
  max-height: 60px;
  max-width: 120px;
  border-radius: 6px;
  border: 1px solid #0f3460;
  object-fit: contain;
}
.img-preview .img-name {
  font-size: 12px;
  color: #8080a0;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.img-preview .img-remove {
  background: none;
  border: 1px solid #505070;
  color: #a0a0c0;
  border-radius: 50%;
  width: 22px;
  height: 22px;
  cursor: pointer;
  font-size: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
}
.img-preview .img-remove:hover { background: #4a2a2a; color: #f08080; }
</style>
</head>
<body>

<div class=""status-bar"">
  <div class=""status-indicator"">
    <div class=""status-dot"" id=""statusDot""></div>
    <span id=""statusText"">接続中...</span>
  </div>
  <div class=""status-item"">
    <span class=""label"">Model: </span>
    <span class=""value"" id=""modelName"">-</span>
  </div>
  <div class=""context-bar-container"">
    <div class=""context-bar"">
      <div class=""context-bar-fill"" id=""contextFill""></div>
    </div>
    <div class=""context-label"" id=""contextLabel"">コンテキスト: - / -</div>
  </div>
  <div class=""status-item"">
    <span class=""label"">Tokens: </span>
    <span class=""value"" id=""totalTokens"">0</span>
  </div>
  <div class=""status-item"">
    <span class=""label"">Cost: </span>
    <span class=""value"" id=""costDisplay"">-</span>
  </div>
</div>

<div class=""current-tool"" id=""currentTool""></div>

<div class=""chat-area"" id=""chatArea""></div>

<div class=""input-area"">
  <div class=""img-preview"" id=""imgPreview"">
    <img id=""imgThumb"" src="""" alt=""preview"">
    <span class=""img-name"" id=""imgName""></span>
    <button class=""img-remove"" onclick=""removeImage()"" title=""削除"">&times;</button>
  </div>
  <div class=""input-row"">
    <div class=""input-left"">
      <textarea id=""msgInput"" rows=""1"" placeholder=""メッセージを入力... (Enter で送信、画像も添付可)""
        onkeydown=""if(event.key==='Enter'&&!event.shiftKey){event.preventDefault();sendMessage();}""
        onpaste=""handlePaste(event)""></textarea>
    </div>
    <div class=""input-buttons"">
      <button id=""sendBtn"" onclick=""sendMessage()"">送信</button>
      <button id=""imgBtn"" onclick=""document.getElementById('fileInput').click()"" title=""画像を添付"">&#128247;</button>
    </div>
  </div>
  <input type=""file"" id=""fileInput"" accept=""image/*"" style=""display:none"" onchange=""handleFileSelect(event)"">
</div>

<script>
const BASE = location.origin;
let lastMessageCount = 0;
let autoScroll = true;

const chatArea = document.getElementById('chatArea');

chatArea.addEventListener('scroll', () => {
  const threshold = 50;
  autoScroll = (chatArea.scrollHeight - chatArea.scrollTop - chatArea.clientHeight) < threshold;
});

function formatTokens(n) {
  if (n >= 1000000) return (n / 1000000).toFixed(1) + 'M';
  if (n >= 1000) return (n / 1000).toFixed(1) + 'k';
  return n.toString();
}

function escapeHtml(s) {
  const d = document.createElement('div');
  d.textContent = s;
  return d.innerHTML;
}

async function fetchStatus() {
  try {
    const res = await fetch(BASE + '/api/status');
    const s = await res.json();

    const dot = document.getElementById('statusDot');
    const text = document.getElementById('statusText');
    isProcessing = s.isProcessing;
    if (s.isProcessing) {
      dot.classList.add('active');
      text.textContent = '処理中';
    } else {
      dot.classList.remove('active');
      text.textContent = '待機中';
    }
    updateInputState();

    document.getElementById('modelName').textContent = s.modelName || '-';
    document.getElementById('totalTokens').textContent = formatTokens(s.sessionTotalTokens);
    document.getElementById('costDisplay').textContent = s.estimatedCost || '-';

    // Context bar
    const ratio = s.maxContextTokens > 0 ? s.lastPromptTokens / s.maxContextTokens : 0;
    const fill = document.getElementById('contextFill');
    fill.style.width = (Math.min(ratio, 1) * 100) + '%';
    fill.className = 'context-bar-fill' + (ratio >= 0.9 ? ' danger' : ratio >= 0.7 ? ' warning' : '');
    document.getElementById('contextLabel').textContent =
      'コンテキスト: ' + formatTokens(s.lastPromptTokens) + ' / ' + formatTokens(s.maxContextTokens);

    // Current tool
    const toolEl = document.getElementById('currentTool');
    if (s.isProcessing && s.currentTool) {
      toolEl.textContent = s.currentTool;
      toolEl.classList.add('visible');
    } else {
      toolEl.classList.remove('visible');
    }
  } catch (e) {}
}

async function fetchChat() {
  try {
    const res = await fetch(BASE + '/api/chat');
    const data = await res.json();
    const messages = data.messages || [];

    if (messages.length === lastMessageCount) return;
    lastMessageCount = messages.length;

    // Group Info messages
    const groups = [];
    let i = 0;
    while (i < messages.length) {
      if (messages[i].type === 'Info') {
        const start = i;
        while (i < messages.length && messages[i].type === 'Info') i++;
        groups.push({ type: 'info-group', items: messages.slice(start, i) });
      } else {
        groups.push({ type: 'single', item: messages[i] });
        i++;
      }
    }

    let html = '';
    groups.forEach((g, gi) => {
      if (g.type === 'info-group' && g.items.length >= 2) {
        const isLast = gi === groups.length - 1 || (gi === groups.length - 2 && groups[groups.length - 1].type === 'single');
        html += '<div class=""info-group"">';
        html += '<div class=""info-group-toggle"" onclick=""this.nextElementSibling.classList.toggle(\'open\')"">';
        html += '\u25B6 ツール実行 (' + g.items.length + '件)</div>';
        html += '<div class=""info-group-items' + (isLast ? ' open' : '') + '"">';
        g.items.forEach(m => {
          html += renderMessage(m);
        });
        html += '</div></div>';
      } else if (g.type === 'info-group') {
        g.items.forEach(m => { html += renderMessage(m); });
      } else {
        html += renderMessage(g.item);
      }
    });

    chatArea.innerHTML = html;

    if (autoScroll) {
      chatArea.scrollTop = chatArea.scrollHeight;
    }
  } catch (e) {}
}

function renderMessage(m) {
  let html = '<div class=""message ' + escapeHtml(m.type) + '"">';

  // Meta (timestamp + role)
  const role = m.type === 'User' ? 'あなた' : m.type === 'Agent' ? 'Agent' : '';
  if (role) {
    html += '<div class=""msg-meta"">' + (m.timestamp || '') + ' ' + role + '</div>';
  }

  // Thinking
  if (m.thinkingText && m.type === 'Agent') {
    const id = 'think-' + Math.random().toString(36).substr(2, 6);
    html += '<div class=""thinking-toggle"" onclick=""document.getElementById(\'' + id + '\').classList.toggle(\'open\')"">\u2728 Thinking</div>';
    html += '<div class=""thinking-content"" id=""' + id + '"">' + escapeHtml(m.thinkingText) + '</div>';
  }

  // Bubble
  let text = m.text || '';
  if (m.type === 'User' && text.startsWith('You: ')) text = text.substring(5);

  // Info icons
  let prefix = '';
  if (m.type === 'Info') {
    if (text.startsWith('Executing Tool:')) prefix = '\u25B6 ';
    else if (text.startsWith('[Tool Result]')) prefix = '\u2714 ';
    else if (text.startsWith('[Tool Error]')) prefix = '\u26A0 ';
    else prefix = '\u2139 ';
  } else if (m.type === 'Error') {
    prefix = '\u26A0 ';
  }

  html += '<div class=""bubble"">' + prefix + escapeHtml(text) + '</div>';
  html += '</div>';
  return html;
}

let isProcessing = false;
let pendingImageBase64 = null;
let pendingImageMimeType = null;

function updateInputState() {
  const input = document.getElementById('msgInput');
  const btn = document.getElementById('sendBtn');
  const imgBtn = document.getElementById('imgBtn');
  input.disabled = isProcessing;
  btn.disabled = isProcessing;
  imgBtn.disabled = isProcessing;
}

function setImagePreview(base64, mimeType, name) {
  pendingImageBase64 = base64;
  pendingImageMimeType = mimeType;
  const preview = document.getElementById('imgPreview');
  document.getElementById('imgThumb').src = 'data:' + mimeType + ';base64,' + base64;
  document.getElementById('imgName').textContent = name || 'image';
  preview.classList.add('visible');
}

function removeImage() {
  pendingImageBase64 = null;
  pendingImageMimeType = null;
  document.getElementById('imgPreview').classList.remove('visible');
  document.getElementById('fileInput').value = '';
}

function handleFileSelect(e) {
  const file = e.target.files[0];
  if (!file || !file.type.startsWith('image/')) return;
  readImageFile(file);
}

function handlePaste(e) {
  const items = e.clipboardData && e.clipboardData.items;
  if (!items) return;
  for (let i = 0; i < items.length; i++) {
    if (items[i].type.startsWith('image/')) {
      e.preventDefault();
      readImageFile(items[i].getAsFile());
      return;
    }
  }
}

function readImageFile(file) {
  const reader = new FileReader();
  reader.onload = function() {
    const dataUrl = reader.result;
    const commaIdx = dataUrl.indexOf(',');
    const base64 = dataUrl.substring(commaIdx + 1);
    const mime = file.type || 'image/png';
    setImagePreview(base64, mime, file.name);
  };
  reader.readAsDataURL(file);
}

async function sendMessage() {
  const input = document.getElementById('msgInput');
  const msg = input.value.trim();
  const hasImage = !!pendingImageBase64;
  if ((!msg && !hasImage) || isProcessing) return;

  const btn = document.getElementById('sendBtn');
  input.disabled = true;
  btn.disabled = true;

  const payload = { message: msg };
  if (hasImage) {
    payload.imageBase64 = pendingImageBase64;
    payload.imageMimeType = pendingImageMimeType;
  }

  try {
    const res = await fetch(BASE + '/api/send', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await res.json();
    if (data.ok) {
      input.value = '';
      removeImage();
    }
  } catch (e) {}

  updateInputState();
}

// Poll every 3 seconds
fetchStatus();
fetchChat();
setInterval(fetchStatus, 3000);
setInterval(fetchChat, 3000);
</script>
</body>
</html>";
        }
    }
}

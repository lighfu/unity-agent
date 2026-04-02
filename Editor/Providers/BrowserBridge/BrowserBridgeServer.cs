using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Providers.BrowserBridge
{
    /// <summary>
    /// RFC 6455 WebSocket サーバー（TcpListener ベース）。
    /// 単一クライアント接続を管理し、JSON メッセージの送受信を行う。
    /// </summary>
    internal class BrowserBridgeServer
    {
        private TcpListener _listener;
        private TcpListener _listener6; // IPv6
        private Thread _listenThread;
        private Thread _listenThread6; // IPv6
        private Thread _clientThread;
        private volatile bool _running;
        private TcpClient _client;
        private NetworkStream _clientStream;
        private readonly object _lock = new object();
        private readonly Queue<string> _sendQueue = new Queue<string>();
        private int _port;

        public bool IsRunning => _running;

        public void Start(int port)
        {
            if (_running) return;
            _port = port;
            _running = true;

            // IPv4 listener (127.0.0.1)
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BrowserBridgeServer] IPv4 listener failed: {ex.Message}");
                _listener = null;
            }

            // IPv6 listener (::1) — Windows ではブラウザが localhost を ::1 で解決することが多い
            try
            {
                _listener6 = new TcpListener(IPAddress.IPv6Loopback, port);
                _listener6.Start();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BrowserBridgeServer] IPv6 listener failed: {ex.Message}");
                _listener6 = null;
            }

            if (_listener == null && _listener6 == null)
            {
                Debug.LogError($"[BrowserBridgeServer] Failed to start on port {port}");
                _running = false;
                return;
            }

            if (_listener != null)
            {
                _listenThread = new Thread(() => ListenLoop(_listener))
                {
                    IsBackground = true,
                    Name = "BrowserBridgeServer_Listen_v4"
                };
                _listenThread.Start();
            }

            if (_listener6 != null)
            {
                _listenThread6 = new Thread(() => ListenLoop(_listener6))
                {
                    IsBackground = true,
                    Name = "BrowserBridgeServer_Listen_v6"
                };
                _listenThread6.Start();
            }

            Debug.Log($"[BrowserBridgeServer] Started on port {port} (v4={_listener != null}, v6={_listener6 != null})");
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _listener6?.Stop(); } catch { }
            try { _client?.Close(); } catch { }
            _listener = null;
            _listener6 = null;
            _client = null;
            _clientStream = null;
            _listenThread = null;
            _listenThread6 = null;
            _clientThread = null;
            BrowserBridgeState.SetConnected(false);
            lock (_lock) _sendQueue.Clear();
            Debug.Log("[BrowserBridgeServer] Stopped");
        }

        public void Send(string json)
        {
            lock (_lock)
            {
                _sendQueue.Enqueue(json);
            }
        }

        private void ListenLoop(TcpListener listener)
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch
                {
                    break;
                }

                // Close any existing client
                try { _client?.Close(); } catch { }
                BrowserBridgeState.SetConnected(false);

                _client = client;
                _clientStream = client.GetStream();

                _clientThread = new Thread(() => HandleClient(client))
                {
                    IsBackground = true,
                    Name = "BrowserBridgeServer_Client"
                };
                _clientThread.Start();
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();

                // WebSocket handshake
                if (!PerformHandshake(stream))
                {
                    client.Close();
                    return;
                }

                BrowserBridgeState.SetConnected(true);
                Debug.Log("[BrowserBridgeServer] Client connected (WebSocket handshake complete)");

                // Read/write loop
                while (_running && client.Connected)
                {
                    // Send queued messages
                    FlushSendQueue(stream);

                    // Check for incoming data
                    if (stream.DataAvailable)
                    {
                        string message = ReadFrame(stream);
                        if (message == null)
                        {
                            // Connection closed or error
                            break;
                        }
                        if (message.Length > 0)
                        {
                            HandleMessage(message);
                        }
                    }

                    Thread.Sleep(16); // ~60Hz
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[BrowserBridgeServer] Client disconnected: {ex.Message}");
            }
            finally
            {
                BrowserBridgeState.SetConnected(false);
                try { client.Close(); } catch { }
            }
        }

        private bool PerformHandshake(NetworkStream stream)
        {
            // Read HTTP request
            var buffer = new byte[4096];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) return false;

            string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // Extract Sec-WebSocket-Key
            string key = null;
            foreach (var line in request.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    key = trimmed.Substring("Sec-WebSocket-Key:".Length).Trim();
                    break;
                }
            }

            if (key == null) return false;

            // Compute accept key
            string acceptKey = ComputeAcceptKey(key);

            // Send handshake response
            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + acceptKey + "\r\n" +
                "\r\n";

            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            stream.Write(responseBytes, 0, responseBytes.Length);
            stream.Flush();

            return true;
        }

        private static string ComputeAcceptKey(string key)
        {
            string magic = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(magic));
                return Convert.ToBase64String(hash);
            }
        }

        private string ReadFrame(NetworkStream stream)
        {
            // Read first 2 bytes
            int b0 = stream.ReadByte();
            int b1 = stream.ReadByte();
            if (b0 < 0 || b1 < 0) return null;

            int opcode = b0 & 0x0F;
            bool masked = (b1 & 0x80) != 0;
            long payloadLen = b1 & 0x7F;

            if (payloadLen == 126)
            {
                int hi = stream.ReadByte();
                int lo = stream.ReadByte();
                if (hi < 0 || lo < 0) return null;
                payloadLen = (hi << 8) | lo;
            }
            else if (payloadLen == 127)
            {
                var lenBytes = new byte[8];
                ReadExact(stream, lenBytes, 8);
                payloadLen = 0;
                for (int i = 0; i < 8; i++)
                    payloadLen = (payloadLen << 8) | lenBytes[i];
            }

            byte[] maskKey = null;
            if (masked)
            {
                maskKey = new byte[4];
                ReadExact(stream, maskKey, 4);
            }

            var payload = new byte[payloadLen];
            if (payloadLen > 0)
                ReadExact(stream, payload, (int)payloadLen);

            if (masked && maskKey != null)
            {
                for (int i = 0; i < payload.Length; i++)
                    payload[i] ^= maskKey[i % 4];
            }

            switch (opcode)
            {
                case 0x1: // Text frame
                    return Encoding.UTF8.GetString(payload);

                case 0x8: // Close
                    // Send close frame back
                    try { SendFrame(stream, 0x8, new byte[0]); } catch { }
                    return null;

                case 0x9: // Ping → send Pong
                    try { SendFrame(stream, 0xA, payload); } catch { }
                    return "";

                case 0xA: // Pong
                    return "";

                default:
                    return "";
            }
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new IOException("Connection closed during read");
                offset += read;
            }
        }

        private void SendFrame(NetworkStream stream, int opcode, byte[] payload)
        {
            // Server frames are unmasked
            int headerLen = 2;
            if (payload.Length >= 126 && payload.Length <= 65535) headerLen += 2;
            else if (payload.Length > 65535) headerLen += 8;

            var frame = new byte[headerLen + payload.Length];
            frame[0] = (byte)(0x80 | opcode); // FIN + opcode

            if (payload.Length < 126)
            {
                frame[1] = (byte)payload.Length;
            }
            else if (payload.Length <= 65535)
            {
                frame[1] = 126;
                frame[2] = (byte)(payload.Length >> 8);
                frame[3] = (byte)(payload.Length & 0xFF);
            }
            else
            {
                frame[1] = 127;
                long len = payload.Length;
                for (int i = 7; i >= 0; i--)
                {
                    frame[2 + (7 - i)] = (byte)(len >> (i * 8));
                }
            }

            Buffer.BlockCopy(payload, 0, frame, headerLen, payload.Length);
            stream.Write(frame, 0, frame.Length);
            stream.Flush();
        }

        private void SendText(NetworkStream stream, string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            SendFrame(stream, 0x1, payload);
        }

        private void FlushSendQueue(NetworkStream stream)
        {
            while (true)
            {
                string msg;
                lock (_lock)
                {
                    if (_sendQueue.Count == 0) break;
                    msg = _sendQueue.Dequeue();
                }
                try
                {
                    SendText(stream, msg);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BrowserBridgeServer] Send failed: {ex.Message}");
                    break;
                }
            }
        }

        private void HandleMessage(string json)
        {
            string type = BrowserBridgeProtocol.GetMessageType(json);
            if (type == null) return;

            string id = BrowserBridgeProtocol.GetField(json, "id");

            switch (type)
            {
                case "ready":
                    var siteUrl = BrowserBridgeProtocol.GetField(json, "geminiUrl");
                    BrowserBridgeState.SetSiteUrl(siteUrl);
                    Debug.Log("[BrowserBridgeServer] Extension ready: " + siteUrl);
                    break;

                case "partial":
                    if (id != null)
                        BrowserBridgeState.OnPartial(id, BrowserBridgeProtocol.GetField(json, "text") ?? "");
                    break;

                case "complete":
                    if (id != null)
                        BrowserBridgeState.OnComplete(id, BrowserBridgeProtocol.GetField(json, "text") ?? "");
                    break;

                case "error":
                    if (id != null)
                        BrowserBridgeState.OnError(id, BrowserBridgeProtocol.GetField(json, "message") ?? "Unknown error");
                    break;

                case "pong":
                    break;
            }
        }
    }

    /// <summary>
    /// BrowserBridgeServer のシングルトン管理。
    /// </summary>
    internal static class BrowserBridgeServerManager
    {
        private static BrowserBridgeServer _server;

        public static BrowserBridgeServer Server => _server;
        public static bool IsRunning => _server != null && _server.IsRunning;

        public static void EnsureRunning(int port)
        {
            if (_server != null && _server.IsRunning) return;
            _server?.Stop();
            _server = new BrowserBridgeServer();
            _server.Start(port);
        }

        public static void Stop()
        {
            _server?.Stop();
            _server = null;
        }
    }
}

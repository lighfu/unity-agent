using System;
using System.Reflection;
using System.Threading;
using UnityEditor;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// Keeps Bridge startup, reconnects and tool coroutines moving when the editor is idle or
    /// unfocused. Only signals Unity's native event loop; all Unity work stays on update's thread.
    /// </summary>
    internal static class MCPMainThreadWakeup
    {
        const int TickIntervalMs = 200;
        static Action _signalTick;
        static Timer _timer;
        static volatile bool _running;
        static int _failureLogged;

        // Start/Stop are main-thread-only. SignalTick is explicitly [ThreadSafe] in Unity's
        // EditorApplication bindings (2022.3), unlike QueuePlayerLoopUpdate/RepaintAllViews.
        // It is internal, so resolve it once and keep the ordinary update path as a fallback.
        internal static void Start()
        {
            if (_running) return;
            _timer?.Dispose();
            _timer = null;
            try
            {
                if (_signalTick == null)
                {
                    var method = typeof(EditorApplication).GetMethod("SignalTick",
                        BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (method == null) throw new MissingMethodException("EditorApplication.SignalTick");
                    _signalTick = (Action)Delegate.CreateDelegate(typeof(Action), method);
                }
                _running = true;
                _timer = new Timer(_ => RequestTick(), null, 0, TickIntervalMs);
            }
            catch (Exception ex)
            {
                _running = false;
                LogFailure(ex);
            }
        }

        internal static void Stop()
        {
            _running = false;
            _timer?.Dispose();
            _timer = null;
        }

        /// <summary>Safe on socket/Timer threads. Does not execute callbacks or steal focus.</summary>
        internal static void RequestTick()
        {
            if (!_running) return;
            try { _signalTick(); }
            catch (Exception ex)
            {
                // A future Unity version may remove the native entry point. Do not let an
                // exception on a Timer thread terminate the editor, or log every 200 ms.
                _running = false;
                LogFailure(ex);
            }
        }

        static void LogFailure(Exception ex)
        {
            if (Interlocked.Exchange(ref _failureLogged, 1) == 0)
                AgentLogger.Warning(LogTag.MCP,
                    $"[BridgeClient] background editor wakeup unavailable; using normal editor updates: {ex.Message}");
        }
    }
}

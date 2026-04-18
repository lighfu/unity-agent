using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.MCP
{
    /// <summary>
    /// MCP サーバーのオートスタート。
    ///
    /// モード分岐:
    /// - <see cref="MCPServerMode.InProc"/>: <see cref="AgentMCPServer.StartShared"/> を呼んで Editor 内 HTTP listener を起動 (legacy)
    /// - <see cref="MCPServerMode.Bridge"/>: 別プロセスの bridge binary を spawn し、<see cref="AgentMCPBridgeClient.Connect"/> で TCP 接続
    ///
    /// Editor ドメインがロードされた時点で起動し、<c>beforeAssemblyReload</c> / <c>quitting</c>
    /// で停止する。Bridge モードの場合、bridge プロセス自体は生存し続けるので
    /// 次の reload 後に Connect が再呼出しされて即時復帰する。
    /// </summary>
    [InitializeOnLoad]
    internal static class AgentMCPServerBootstrap
    {
        static AgentMCPServerBootstrap()
        {
            // DelayCall で domain reload 後に安定してから起動
            EditorApplication.delayCall += StartIfEnabled;

            AssemblyReloadEvents.beforeAssemblyReload += StopBeforeReload;
            EditorApplication.quitting += StopBeforeReload;
        }

        /// <summary>
        /// 有効化トグルや mode 切替からも呼び出される再入可能なエントリポイント。
        /// 内部の各層 (<see cref="AgentMCPServer"/>, <see cref="AgentMCPBridgeClient"/>) は
        /// 既に running であれば no-op なので重ね呼びしても安全。
        /// </summary>
        internal static void StartIfEnabled()
        {
            if (!AgentSettings.MCPServerEnabled) return;

            switch (AgentSettings.MCPServerMode)
            {
                case MCPServerMode.Bridge:
                    StartBridgeMode();
                    break;
                case MCPServerMode.InProc:
                default:
                    AgentMCPServer.StartShared();
                    break;
            }
        }

        static void StopBeforeReload()
        {
            var mode = AgentSettings.MCPServerMode;
            AgentLogger.Info(LogTag.MCP, $"[Bootstrap] StopBeforeReload mode={mode}");
            switch (mode)
            {
                case MCPServerMode.Bridge:
                    AgentMCPBridgeClient.Shared.Disconnect("domain_reload");
                    break;
                case MCPServerMode.InProc:
                default:
                    AgentMCPServer.StopShared();
                    break;
            }
        }

        // ─── Bridge mode ───

        static void StartBridgeMode()
        {
            if (AgentMCPBridgeClient.Shared.IsConnected) return;

            int internalPort = AgentSettings.MCPBridgeInternalPort;
            int publicPort = AgentSettings.MCPBridgePublicPort;
            string token = AgentSettings.EnsureMCPServerToken();

            AgentMCPBridgeClient.Shared.MarkStarting();

            AgentLogger.Info(LogTag.MCP, $"[Bootstrap] StartBridgeMode internal={internalPort} public={publicPort} token.len={token?.Length ?? 0}");

            try
            {
                EnsureBridgeProcessRunning(internalPort, publicPort, token);
            }
            catch (Exception ex)
            {
                AgentMCPBridgeClient.Shared.ClearStarting();
                AgentLogger.Error(LogTag.MCP, $"[Bootstrap] Bridge spawn failed: {ex.Message}");
                return;
            }

            // bridge プロセスが listen 状態になるタイミングは不定 (コールドスタート〜数百 ms)。
            // EditorApplication.update を tick にして ~4 秒までリトライする。
            ScheduleBridgeConnect(internalPort, token);
        }

        /// <summary>
        /// <see cref="AgentMCPBridgeClient.Connect"/> を成功するまで short backoff でリトライする。
        /// 上限に達したら断念してログを残す。
        /// </summary>
        static void ScheduleBridgeConnect(int internalPort, string token)
        {
            const int MaxAttempts = 25;      // ~5 秒 (200ms * 25)
            const double IntervalSec = 0.2;

            int attempts = 0;
            double nextAt = EditorApplication.timeSinceStartup;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (AgentMCPBridgeClient.Shared.IsConnected)
                {
                    EditorApplication.update -= tick;
                    return;
                }
                if (!AgentSettings.MCPServerEnabled ||
                    AgentSettings.MCPServerMode != MCPServerMode.Bridge)
                {
                    EditorApplication.update -= tick;
                    AgentMCPBridgeClient.Shared.ClearStarting();
                    return;
                }

                if (EditorApplication.timeSinceStartup < nextAt) return;
                attempts++;

                try
                {
                    AgentMCPBridgeClient.Shared.Connect(internalPort, token);
                    AgentLogger.Info(LogTag.MCP, $"[Bootstrap] Bridge connected on attempt {attempts}/{MaxAttempts}");
                    EditorApplication.update -= tick;
                    return;
                }
                catch (Exception ex)
                {
                    AgentLogger.Debug(LogTag.MCP, $"[Bootstrap] Bridge connect attempt {attempts}/{MaxAttempts} failed: {ex.Message}");
                    if (attempts >= MaxAttempts)
                    {
                        EditorApplication.update -= tick;
                        AgentMCPBridgeClient.Shared.ClearStarting();
                        AgentLogger.Error(LogTag.MCP,
                            $"[Bootstrap] Bridge client connect failed after {attempts} attempts: {ex.Message}");
                        return;
                    }
                    nextAt = EditorApplication.timeSinceStartup + IntervalSec;
                }
            };
            EditorApplication.update += tick;
        }

        /// <summary>
        /// bridge プロセスが既に動いているかをロックファイルで判定し、なければ spawn する。
        /// </summary>
        static void EnsureBridgeProcessRunning(int internalPort, int publicPort, string token)
        {
            string lockPath = GetLockFilePath();

            // 既存のロックがあれば pid を確認し、さらに internal port で実際に listening しているかを probe する
            if (File.Exists(lockPath))
            {
                try
                {
                    string content = File.ReadAllText(lockPath).Trim();
                    if (int.TryParse(content, out int existingPid))
                    {
                        try
                        {
                            var existing = Process.GetProcessById(existingPid);
                            bool listening = IsPortListening(internalPort);
                            AgentLogger.Debug(LogTag.MCP, $"[Bootstrap] lockfile pid={existingPid} hasExited={existing.HasExited} listening={listening}");
                            if (!existing.HasExited && listening)
                            {
                                AgentLogger.Info(LogTag.MCP, $"[Bootstrap] Bridge already running (pid={existingPid}), reusing.");
                                return;
                            }
                            // pid は存在するが port listen が無い → pid 再利用 or 別プロセス。上書きして spawn。
                        }
                        catch (ArgumentException)
                        {
                            AgentLogger.Debug(LogTag.MCP, $"[Bootstrap] stale lockfile pid={existingPid} no longer exists");
                            // pid no longer exists — stale lockfile, will overwrite below
                        }
                    }
                }
                catch (Exception ex)
                {
                    AgentLogger.Warning(LogTag.MCP, $"[Bootstrap] Reading bridge lockfile failed: {ex.Message}");
                }
            }

            string binaryPath = ResolveBridgeBinary();
            if (string.IsNullOrEmpty(binaryPath))
            {
                // packageRoot 解決失敗 (PackageInfo.FindForAssembly null + package.json walk-up fail)
                AgentLogger.Error(LogTag.MCP, "[Bootstrap] Could not resolve package root; bridge binary path is empty.");
                throw new FileNotFoundException(
                    "Bridge binary path could not be resolved (package root lookup failed). " +
                    "Verify UnityAgent is installed as a UPM package or switch back to InProc mode.");
            }
            if (!File.Exists(binaryPath))
            {
                AgentLogger.Error(LogTag.MCP, $"[Bootstrap] Bridge binary missing at expected path: {binaryPath}");
                throw new FileNotFoundException(
                    $"Bridge binary not found at expected path: {binaryPath}\n" +
                    "Build it via Editor/Bridge~/UnityAgentBridge/build.ps1 or switch back to InProc mode.");
            }
            AgentLogger.Debug(LogTag.MCP, $"[Bootstrap] Resolved bridge binary: {binaryPath}");

            string logPath = GetBridgeLogPath();
            string args = $"--internal-port {internalPort} --public-port {publicPort} --token {token} --log \"{logPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };
            var proc = Process.Start(psi);
            if (proc == null)
                throw new InvalidOperationException("Process.Start returned null.");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lockPath) ?? "");
                File.WriteAllText(lockPath, proc.Id.ToString());
            }
            catch (Exception ex)
            {
                AgentLogger.Warning(LogTag.MCP, $"[Bootstrap] Failed to write bridge lockfile: {ex.Message}");
            }

            AgentLogger.Info(LogTag.MCP, $"[Bootstrap] Bridge spawned (pid={proc.Id}, internal={internalPort}, public={publicPort}, binary={binaryPath})");
        }

        static string ResolveBridgeBinary()
        {
            // Editor フォルダ基準: <package>/Editor/Bridge/bin/<rid>/UnityAgentBridge[.exe]
            string packageRoot = TryGetPackageRoot();
            if (string.IsNullOrEmpty(packageRoot)) return null;

            string rid = GetCurrentRid();
            string exeName = Application.platform == RuntimePlatform.WindowsEditor
                ? "UnityAgentBridge.exe"
                : "UnityAgentBridge";
            return Path.Combine(packageRoot, "Editor", "Bridge", "bin", rid, exeName);
        }

        static string GetCurrentRid()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor: return "win-x64";
                case RuntimePlatform.OSXEditor:
                    return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                        == System.Runtime.InteropServices.Architecture.Arm64
                        ? "osx-arm64"
                        : "osx-x64";
                case RuntimePlatform.LinuxEditor: return "linux-x64";
                default: return "win-x64";
            }
        }

        static string TryGetPackageRoot()
        {
            // Resolve the path to the assembly that defines this file, walk up to the package root.
            try
            {
                var asm = typeof(AgentMCPServerBootstrap).Assembly;
                string asmPath = asm.Location;
                if (string.IsNullOrEmpty(asmPath)) return null;

                // Editor assemblies live under .../Editor/<asmname>.dll or under Library/ScriptAssemblies/
                // For source distribution, we rely on PackageInfo lookup instead.
                var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(asm);
                if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath))
                    return pkg.resolvedPath;

                // Fallback: walk up from the assembly path
                string dir = Path.GetDirectoryName(asmPath);
                while (!string.IsNullOrEmpty(dir))
                {
                    if (File.Exists(Path.Combine(dir, "package.json")))
                        return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 127.0.0.1:port が listen 中かどうかを短い timeout で probe する。
        /// Bridge.lock の pid 再利用判定に使う。
        /// </summary>
        static bool IsPortListening(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect("127.0.0.1", port, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(300));
                    if (!ok) return false;
                    try { client.EndConnect(ar); }
                    catch { return false; }
                    return client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        static string GetLockFilePath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, "Library", "UnityAgent", "Bridge.lock");
        }

        static string GetBridgeLogPath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, "Library", "UnityAgent", "Bridge.log");
        }
    }
}

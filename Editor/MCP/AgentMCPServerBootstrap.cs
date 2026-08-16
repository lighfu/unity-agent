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
    ///
    /// Bridge モードでは接続後も監視 tick (<see cref="SupervisorTick"/>) を張り続け、
    /// bridge プロセスが落ちた場合はバックオフしながら再 spawn / 再接続を繰り返す。
    /// 監視状態は静的フィールドに持つだけなのでドメインリロードで消えるが、
    /// リロード後は再び delayCall → <see cref="StartIfEnabled"/> から張り直される。
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
                    StopSupervisor();
                    AgentMCPBridgeClient.Shared.Disconnect("domain_reload");
                    break;
                case MCPServerMode.InProc:
                default:
                    AgentMCPServer.StopShared();
                    break;
            }
        }

        // ─── Bridge mode / 接続スーパーバイザ ───
        //
        // bridge プロセスは Unity とは独立に死にうる (idle 自死・手動 kill・クラッシュ)。
        // 一度きりの接続リトライだと、そこから先はドメインリロードか設定トグルまで
        // 恒久的に無応答になるので、接続後も監視を続けて落ちたら張り直す。

        const double FastRetryIntervalSec = 0.2;  // spawn 直後の listen 待ち
        const int FastRetryAttempts = 25;         // ~5 秒
        const double MinBackoffSec = 1.0;
        const double MaxBackoffSec = 30.0;

        static EditorApplication.CallbackFunction _supervisorTick;
        static int _bridgeInternalPort;
        static int _bridgePublicPort;
        static string _bridgeToken;

        /// <summary>直前の tick で接続済みだったか。true → false の遷移が「見失った」瞬間。</summary>
        static bool _wasConnected;
        static bool _fastPhase;
        static int _attemptsSinceReset;
        static double _backoffSec;
        static double _nextAttemptAt;

        /// <summary>spawn 失敗ログの連投を防ぐフラグ (最初の 1 回だけ Error、以降は Debug)。</summary>
        static bool _spawnErrorLogged;

        static void StartBridgeMode()
        {
            _bridgeInternalPort = AgentSettings.MCPBridgeInternalPort;
            _bridgePublicPort = AgentSettings.MCPBridgePublicPort;
            _bridgeToken = AgentSettings.EnsureMCPServerToken();

            if (AgentMCPBridgeClient.Shared.IsConnected)
            {
                // 既に接続済み (トグル操作などによる再入)。監視だけ張り直して戻る。
                _wasConnected = true;
                EnsureSupervisorRunning();
                return;
            }

            AgentMCPBridgeClient.Shared.MarkStarting();

            AgentLogger.Info(LogTag.MCP,
                $"[Bootstrap] StartBridgeMode internal={_bridgeInternalPort} public={_bridgePublicPort} token.len={_bridgeToken?.Length ?? 0}");

            // spawn が失敗しても監視は張る。バイナリを後から用意した場合や
            // 一時的な失敗はスーパーバイザ側の再試行で自然に復帰する。
            TryEnsureBridgeProcess();

            // bridge プロセスが listen 状態になるタイミングは不定 (コールドスタート〜数百 ms)
            // なので、最初の ~5 秒は 200ms 間隔で叩く。
            ResetRetrySchedule(fast: true);
            EnsureSupervisorRunning();
        }

        static void EnsureSupervisorRunning()
        {
            if (_supervisorTick != null) return;
            _supervisorTick = SupervisorTick;
            EditorApplication.update += _supervisorTick;
        }

        static void StopSupervisor()
        {
            if (_supervisorTick == null) return;
            EditorApplication.update -= _supervisorTick;
            _supervisorTick = null;
        }

        /// <summary>
        /// 再試行スケジュールを初期化する。
        /// <paramref name="fast"/> = true なら spawn 直後の短間隔フェーズから、
        /// false なら <see cref="MinBackoffSec"/> から指数バックオフを始める。
        /// </summary>
        static void ResetRetrySchedule(bool fast)
        {
            _fastPhase = fast;
            _attemptsSinceReset = 0;
            _backoffSec = fast ? FastRetryIntervalSec : MinBackoffSec;
            _nextAttemptAt = EditorApplication.timeSinceStartup; // 1 回目は即時
        }

        /// <summary>
        /// 毎 update 呼ばれる監視 tick。接続中は設定 2 件と volatile bool を読むだけで抜ける
        /// (設定はメモリ上の Dictionary 参照なのでコストは無視できる)。
        /// 未接続なら <see cref="_nextAttemptAt"/> に達したタイミングで再接続を試みる。
        /// </summary>
        static void SupervisorTick()
        {
            if (!AgentSettings.MCPServerEnabled ||
                AgentSettings.MCPServerMode != MCPServerMode.Bridge)
            {
                StopSupervisor();
                AgentMCPBridgeClient.Shared.ClearStarting();
                _wasConnected = false;
                return;
            }

            if (AgentMCPBridgeClient.Shared.IsConnected)
            {
                _wasConnected = true;
                return;
            }

            if (_wasConnected)
            {
                _wasConnected = false;
                AgentLogger.Warning(LogTag.MCP, "[Bootstrap] Bridge connection lost — reconnecting.");
                ResetClientAfterLoss("bridge_lost");
                AgentMCPBridgeClient.Shared.MarkStarting();
                // 落ちた直後は bridge を spawn し直す必要があるので短間隔フェーズはスキップする。
                ResetRetrySchedule(fast: false);
            }

            if (EditorApplication.timeSinceStartup < _nextAttemptAt) return;
            AttemptConnect();
        }

        /// <summary>
        /// 接続を 1 回試み、失敗したら次回の待ち時間を伸ばす。
        /// 短間隔フェーズを抜けた後は、接続前に bridge プロセスの生存確認 (必要なら再 spawn) も行う。
        /// </summary>
        static void AttemptConnect()
        {
            _attemptsSinceReset++;

            // 短間隔フェーズ中は spawn 直後なのでプロセス確認は不要。抜けた後は
            // 「bridge が死んでいる」可能性が高いので毎回確認する (port probe を含むので
            // 短間隔フェーズでやると main thread を無駄に止める)。
            if (!_fastPhase)
                TryEnsureBridgeProcess();

            try
            {
                AgentMCPBridgeClient.Shared.Connect(_bridgeInternalPort, _bridgeToken);
            }
            catch (Exception ex)
            {
                AgentLogger.Debug(LogTag.MCP,
                    $"[Bootstrap] Bridge connect attempt {_attemptsSinceReset} failed: {ex.Message}");
                AdvanceBackoff();
                return;
            }

            if (AgentMCPBridgeClient.Shared.IsConnected)
            {
                _wasConnected = true;
                _spawnErrorLogged = false;
                AgentLogger.Info(LogTag.MCP,
                    $"[Bootstrap] Bridge connected on attempt {_attemptsSinceReset} (port={_bridgeInternalPort})");
                return;
            }

            // 例外なく戻ったのに未接続 = クライアント内部が前の接続を掴んだままで
            // Connect が no-op になった状態。畳んでから次の周期で張り直す。
            AgentLogger.Debug(LogTag.MCP, "[Bootstrap] Connect returned without connecting; resetting client state.");
            ResetClientAfterLoss("stale_connect_state");
            AdvanceBackoff();
        }

        /// <summary>
        /// 次回試行までの待ち時間を進める。短間隔フェーズを使い切ったら
        /// 指数バックオフ (最大 <see cref="MaxBackoffSec"/> 秒) に移行する。
        /// </summary>
        static void AdvanceBackoff()
        {
            if (_fastPhase)
            {
                if (_attemptsSinceReset >= FastRetryAttempts)
                {
                    _fastPhase = false;
                    _backoffSec = MinBackoffSec;
                    // ここで「起動中」を降ろす。再試行自体は無限に続くが、UI に起動中を出したままだと
                    // 「あと少しで繋がる」と読めてしまう。実態は未接続で、バックオフ再試行中。
                    AgentMCPBridgeClient.Shared.ClearStarting();
                    AgentLogger.Warning(LogTag.MCP,
                        $"[Bootstrap] Bridge connect failed after {_attemptsSinceReset} fast attempts; " +
                        $"retrying in the background (backoff up to {MaxBackoffSec:0}s).");
                }
            }
            else
            {
                _backoffSec = Math.Min(_backoffSec * 2.0, MaxBackoffSec);
            }
            _nextAttemptAt = EditorApplication.timeSinceStartup + _backoffSec;
        }

        /// <summary>
        /// クライアントを未接続状態に畳む。<see cref="AgentMCPBridgeClient.Connect"/> は
        /// 内部の running フラグが立っている間 no-op で戻るため、reader スレッドだけが死んだ
        /// ケースでは Disconnect を挟まないと二度と再接続できない。
        /// </summary>
        static void ResetClientAfterLoss(string reason)
        {
            try
            {
                AgentMCPBridgeClient.Shared.Disconnect(reason);
            }
            catch (Exception ex)
            {
                // 既に壊れたソケットの後始末なので、失敗しても再接続は続行できる。
                AgentLogger.Debug(LogTag.MCP, $"[Bootstrap] Disconnect during reconnect failed: {ex.Message}");
            }
        }

        /// <summary>
        /// bridge プロセスの生存確認と (必要なら) spawn。失敗しても投げず、ログだけ残す。
        /// 同じ失敗を毎回 Error で出すと再試行のたびにコンソールが埋まるので、
        /// 2 回目以降は Debug に落とす。
        /// </summary>
        static void TryEnsureBridgeProcess()
        {
            try
            {
                EnsureBridgeProcessRunning(_bridgeInternalPort, _bridgePublicPort, _bridgeToken);
                _spawnErrorLogged = false;
            }
            catch (Exception ex)
            {
                if (!_spawnErrorLogged)
                {
                    _spawnErrorLogged = true;
                    AgentLogger.Error(LogTag.MCP, $"[Bootstrap] Bridge spawn failed: {ex.Message}");
                }
                else
                {
                    AgentLogger.Debug(LogTag.MCP, $"[Bootstrap] Bridge spawn retry failed: {ex.Message}");
                }
            }
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
                // ここで Error を出さない: 再接続は無限に続くので、出すと 30 秒ごとに赤エラーが
                // 永久に積もる。同じ内容は throw のメッセージに載っており、TryEnsureBridgeProcess が
                // 初回だけ Error・2 回目以降 Debug に落として一本化する。
                throw new FileNotFoundException(
                    "Bridge binary path could not be resolved (package root lookup failed). " +
                    "Verify UnityAgent is installed as a UPM package or legacy Assets package, or switch back to InProc mode.");
            }
            if (!File.Exists(binaryPath))
            {
                // Error を出さない理由は上の packageRoot 解決失敗と同じ (再試行のたびに積もる)。
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

                // Editor assemblies live under .../Editor/<asmname>.dll or under Library/ScriptAssemblies/
                // For source distribution, we rely on PackageInfo lookup instead.
                var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(asm);
                if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath))
                    return pkg.resolvedPath;

                // Fallback: walk up from the assembly path
                if (!string.IsNullOrEmpty(asmPath))
                {
                    string dir = Path.GetDirectoryName(asmPath);
                    while (!string.IsNullOrEmpty(dir))
                    {
                        if (File.Exists(Path.Combine(dir, "package.json")))
                            return dir;
                        dir = Path.GetDirectoryName(dir);
                    }
                }
            }
            catch { }
            try
            {
                string legacyRoot = PackagePaths.PackageRoot;
                if (!string.IsNullOrEmpty(legacyRoot) &&
                    File.Exists(Path.Combine(legacyRoot, "package.json")))
                    return legacyRoot;
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

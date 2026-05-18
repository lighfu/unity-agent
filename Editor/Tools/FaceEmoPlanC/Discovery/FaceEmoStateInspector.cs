// Editor/Tools/FaceEmoPlanC/Discovery/FaceEmoStateInspector.cs
#if FACE_EMO
using System.Linq;
using Suzuryg.FaceEmo.Components;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.Discovery
{
    /// <summary>
    /// 指定 avatar に対する FaceEmo セットアップ状態を判定。
    /// Spec Sec 5.2 の 5 状態を返す + 次のアクションヒント。
    /// NotInstalled は #if FACE_EMO 外で別途判定するため本ファイルには含めない。
    /// </summary>
    public static class FaceEmoStateInspector
    {
        public enum State
        {
            NotInstalled,
            NoLauncher,
            LauncherUnconfigured,
            Configured,
            HasModes,
        }

        public sealed class Result
        {
            public State CurrentState { get; set; }
            public FaceEmoLauncherComponent Launcher { get; set; }
            public string[] ModeNames { get; set; }
            public string NextActionHint { get; set; }
            public string AvatarRootName { get; set; }
        }

        public static Result Inspect(string avatarRootName)
        {
            var r = new Result { AvatarRootName = avatarRootName, ModeNames = System.Array.Empty<string>() };

            FaceEmoLauncherComponent launcher = null;
            if (!string.IsNullOrEmpty(avatarRootName))
                launcher = FaceEmoAPI.FindLauncherForAvatar(avatarRootName);

            if (launcher == null)
            {
                r.CurrentState = State.NoLauncher;
                r.NextActionHint = "AutoSetupFaceEmoForAvatar(avatarRootName) を呼んで launcher を作成";
                return r;
            }
            r.Launcher = launcher;

            // TargetAvatar is accessed via launcher.AV3Setting.TargetAvatar (not launcher.TargetAvatar).
            if (launcher.AV3Setting == null || launcher.AV3Setting.TargetAvatar == null)
            {
                r.CurrentState = State.LauncherUnconfigured;
                r.NextActionHint = "ConfigureTargetAvatar(launcher, avatar) を呼んで紐付け";
                return r;
            }

            var menu = FaceEmoAPI.LoadMenu(launcher);
            if (menu == null)
            {
                r.CurrentState = State.Configured;
                r.NextActionHint = "menu load 失敗。FaceEmo MainView を起動して初期化を促す";
                return r;
            }

            // GetAllExpressions walks both menu.Registered and menu.Unregistered recursively,
            // covering Modes nested inside Groups (Plan A Hotfix #4: include Unregistered).
            var allExpressions = FaceEmoAPI.GetAllExpressions(menu);
            var modeNames = allExpressions.Select(e => e.mode.DisplayName).ToArray();
            r.ModeNames = modeNames;

            if (modeNames.Length == 0)
            {
                r.CurrentState = State.Configured;
                r.NextActionHint = "Mode 0 個。新規 Mode 作成 (OpenExpressionSession editMode=new-mode)";
            }
            else
            {
                r.CurrentState = State.HasModes;
                r.NextActionHint = $"Mode {modeNames.Length} 個。AskUser で選択 OR モードが 1 個なら自動採択";
            }
            return r;
        }
    }
}
#endif

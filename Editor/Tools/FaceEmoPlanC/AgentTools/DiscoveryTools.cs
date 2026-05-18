// Editor/Tools/FaceEmoPlanC/AgentTools/DiscoveryTools.cs
#if FACE_EMO
using UnityEditor;
using UnityEngine;
using AjisaiFlow.UnityAgent.SDK;
using AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.Discovery;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.AgentTools
{
    public static class DiscoveryTools
    {
        [AgentTool(
            "発話 hint または scene 状態から target avatar を解決。返却: avatar 名 + confidence + alternatives。")]
        public static string ResolveTargetAvatar(string promptHint = "")
        {
            var r = AvatarResolver.Resolve(promptHint);
            if (r.AvatarRootName != null)
                return $"OK confidence={r.Confidence} avatar={r.AvatarRootName} reason={r.Reason}";
            if (r.Alternatives != null && r.Alternatives.Count > 0)
                return $"Ambiguous alternatives=[{string.Join(",", r.Alternatives)}] reason={r.Reason}";
            return $"None reason={r.Reason}";
        }

        [AgentTool(
            "avatar に対する FaceEmo セットアップ状態を判定。state + modeNames + 推奨 next action を返す。")]
        public static string InspectFaceEmoState(string avatarRootName)
        {
            var r = FaceEmoStateInspector.Inspect(avatarRootName);
            string modes = r.ModeNames != null && r.ModeNames.Length > 0
                ? string.Join(",", r.ModeNames) : "(none)";
            return $"state={r.CurrentState} launcher={r.Launcher?.gameObject?.name ?? "null"} " +
                   $"modes=[{modes}] next={r.NextActionHint}";
        }

        [AgentTool(
            "avatar 用 FaceEmo launcher 自動セットアップ。NoLauncher → ExecuteMenu('FaceEmo/New Menu') + TargetAvatar 設定。")]
        public static string AutoSetupFaceEmoForAvatar(string avatarRootName)
        {
            // 1. avatar GameObject 解決
            var av = GameObject.Find(avatarRootName);
            if (av == null) return $"Error: avatar '{avatarRootName}' not found in scene.";

            // 2. New Menu 実行
            Selection.activeGameObject = av;
            bool ok = EditorApplication.ExecuteMenuItem("FaceEmo/New Menu");
            if (!ok) return "Error: ExecuteMenuItem('FaceEmo/New Menu') failed.";

            // 3. 直後に出現した launcher を avatar 配下から探す
            var launcher = av.GetComponentInChildren<Suzuryg.FaceEmo.Components.FaceEmoLauncherComponent>();
            if (launcher == null) return "Error: launcher not created after New Menu.";

            // 4. TargetAvatar 設定 (AV3Setting.TargetAvatar が null の場合のみ)
            if (launcher.AV3Setting == null || launcher.AV3Setting.TargetAvatar == null)
            {
                var desc = av.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                if (desc == null) return "Error: avatar has no VRCAvatarDescriptor.";

                // Build hierarchy path for TargetAvatarPath (mirrors FaceEmoAdvancedTools pattern)
                string avatarPath = av.name;
                Transform parent = av.transform.parent;
                while (parent != null)
                {
                    avatarPath = parent.name + "/" + avatarPath;
                    parent = parent.parent;
                }

                var av3 = FaceEmoAPI.GetAV3Setting(launcher);
                if (av3 == null) return "Error: AV3Setting not found on launcher after New Menu.";

                Undo.RecordObject(av3, "Configure FaceEmo Target Avatar");
                av3.TargetAvatar = desc;
                av3.TargetAvatarPath = avatarPath;
                EditorUtility.SetDirty(av3);
                FaceEmoAPI.RefreshWindowIfOpen(launcher);
            }

            return $"OK launcher={launcher.gameObject.name} targetAvatar={launcher.AV3Setting?.TargetAvatar?.gameObject?.name ?? "(unset)"}";
        }
    }
}
#endif

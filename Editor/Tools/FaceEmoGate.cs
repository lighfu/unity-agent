// Editor/Tools/FaceEmoGate.cs
using System;
using UnityEngine;

#if FACE_EMO
using Suzuryg.FaceEmo.Components;
#endif

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// 表情変更系ツールの事前条件を一元化する static クラス。
    /// FaceEmo 必須化のゲートをここに集中させる。
    /// </summary>
    public static class FaceEmoGate
    {
        public struct Result
        {
            public bool Ok;
            public string ErrorMessage;
#if FACE_EMO
            public FaceEmoLauncherComponent Launcher;
#endif
        }

        /// <summary>
        /// FaceEmo パッケージがインストールされているか（最も軽量なチェック）。
        /// 解析系（read-only）ツールがエラーヒントに使う。
        /// </summary>
        public static bool IsFaceEmoInstalled()
        {
#if FACE_EMO
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// 表情を「変更」するツールが先頭で呼ぶ。
        /// FaceEmo インストール／launcher／TargetAvatar の 3 条件をすべて満たさないと Ok=false。
        /// </summary>
        public static Result RequireExpressionEditingReady(string gameObjectName = "")
        {
            var result = new Result();
#if !FACE_EMO
            result.Ok = false;
            result.ErrorMessage = "Error: FaceEmo (jp.suzuryg.face-emo) is not installed. " +
                "Expression editing is only available with FaceEmo. Install FaceEmo via VCC, then retry.";
            return result;
#else
            var launcher = FaceEmoAPI.FindLauncher(gameObjectName);
            if (launcher == null)
            {
                result.Ok = false;
                result.ErrorMessage = "Error: No FaceEmo launcher in scene. " +
                    "Run ExecuteMenu('FaceEmo/New Menu') to create one, then ConfigureTargetAvatar('<avatarName>').";
                return result;
            }
            if (launcher.AV3Setting == null || launcher.AV3Setting.TargetAvatar == null)
            {
                result.Ok = false;
                result.ErrorMessage = $"Error: FaceEmo launcher '{launcher.gameObject.name}' has no TargetAvatar. " +
                    "Run ConfigureTargetAvatar('<avatarName>') first.";
                return result;
            }
            result.Ok = true;
            result.Launcher = launcher;
            return result;
#endif
        }
    }
}

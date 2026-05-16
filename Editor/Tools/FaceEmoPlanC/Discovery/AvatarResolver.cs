// Editor/Tools/FaceEmoPlanC/Discovery/AvatarResolver.cs
#if FACE_EMO
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoExpressionEditor;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.Discovery
{
    /// <summary>
    /// 「どの avatar に対して操作するか」を発話文 + scene 状態から決定。
    /// Spec Sec 5.1 の優先順位ロジックを実装。
    /// </summary>
    public static class AvatarResolver
    {
        public enum Confidence { Exact, High, Medium, Low, None }

        public sealed class Result
        {
            public string AvatarRootName { get; set; }
            public Confidence Confidence { get; set; }
            public List<string> Alternatives { get; set; }
            public string Reason { get; set; }
        }

        public static Result Resolve(string promptHint)
        {
            // priority 1: promptHint name match
            if (!string.IsNullOrEmpty(promptHint))
            {
                var allAvatars = ListActiveVrcAvatars();
                var exact = allAvatars.FirstOrDefault(a => a.name == promptHint);
                if (exact != null)
                    return Ok(exact.name, Confidence.Exact, $"promptHint 厳密一致: {promptHint}");
                var partial = allAvatars.Where(a => a.name.IndexOf(promptHint, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (partial.Count == 1)
                    return Ok(partial[0].name, Confidence.High, $"promptHint 部分一致: {promptHint}");
                if (partial.Count > 1)
                    return Ambiguous(partial.Select(a => a.name).ToList(), $"promptHint '{promptHint}' に複数 hit");
            }

            // priority 2: Active session
            var active = FaceEmoExpressionSession.Active;
            if (active?.Launcher != null)
            {
                var root = active.Launcher.gameObject.transform.root.name;
                return Ok(root, Confidence.High, $"Active session の avatar: {root}");
            }

            // priority 3: scene 内 VRC avatar
            var avatars = ListActiveVrcAvatars();
            if (avatars.Count == 1)
                return Ok(avatars[0].name, Confidence.Medium, $"scene に 1 体のみ: {avatars[0].name}");
            if (avatars.Count > 1)
                return Ambiguous(avatars.Select(a => a.name).ToList(), "scene に複数 avatar");

            return new Result
            {
                AvatarRootName = null,
                Confidence = Confidence.None,
                Alternatives = new List<string>(),
                Reason = "scene に VRC avatar が見つかりません",
            };
        }

        private static List<GameObject> ListActiveVrcAvatars()
        {
            var list = new List<GameObject>();
            var descriptors = Object.FindObjectsByType<VRCAvatarDescriptor>(FindObjectsSortMode.None);
            foreach (var d in descriptors)
            {
                if (d == null || d.gameObject == null) continue;
                if (!d.gameObject.activeInHierarchy) continue;
                if (!list.Contains(d.gameObject)) list.Add(d.gameObject);
            }
            return list;
        }

        private static Result Ok(string name, Confidence conf, string reason) => new Result
        {
            AvatarRootName = name,
            Confidence = conf,
            Alternatives = new List<string>(),
            Reason = reason,
        };

        private static Result Ambiguous(List<string> alts, string reason) => new Result
        {
            AvatarRootName = null,
            Confidence = Confidence.Low,
            Alternatives = alts,
            Reason = reason,
        };
    }
}
#endif

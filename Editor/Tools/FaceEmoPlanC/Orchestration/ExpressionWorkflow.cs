#if FACE_EMO
using AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.Conventions;

namespace AjisaiFlow.UnityAgent.Editor.Tools.FaceEmoPlanC.Orchestration
{
    /// <summary>
    /// 発話文を解析し、AI が「どの step で AskUser を踏むか / skip するか」を決めるヒントを返す。
    /// 副作用なし。Discovery / Curation / Execution は呼ばない。
    /// </summary>
    public static class ExpressionWorkflow
    {
        public sealed class WorkflowPlan
        {
            public string Intent { get; set; }                       // smile / angry / ... (検出できなければ null)
            public IntentVocabulary.TopMode TopMode { get; set; }    // Auto / Interactive / Unspecified
            public string AvatarHint { get; set; }                   // 発話に含まれた avatar 名 (なければ null)
            public string ModeHint { get; set; }                     // 発話に含まれた Mode 名
            public string GestureHint { get; set; }                  // 発話に含まれた gesture (PascalCase)
            public string HandHint { get; set; }                     // 発話に含まれた Hand qualifier
            public bool ShouldAskTopMode => TopMode == IntentVocabulary.TopMode.Unspecified;
            public bool ShouldAskAvatar => string.IsNullOrEmpty(AvatarHint);
            public bool ShouldAskMode => string.IsNullOrEmpty(ModeHint);
            public bool ShouldAskGesture => string.IsNullOrEmpty(GestureHint);
            public bool ShouldAskHand => string.IsNullOrEmpty(HandHint);
        }

        // intent キーワード → 内部 intent 名 (Synonym 同様だが Workflow 用に最小限)
        private static readonly (string keyword, string intent)[] IntentKeywords =
        {
            ("笑顔",   "smile"), ("にっこり", "smile"), ("ニコニコ", "smile"),
            ("smile",   "smile"), ("happy",    "smile"),
            ("怒り",   "angry"), ("怒った",   "angry"), ("angry",   "angry"),
            ("悲しい", "sad"),    ("sad",       "sad"),
            ("驚き",   "surprise"), ("びっくり", "surprise"), ("surprise", "surprise"),
            ("ウィンク", "wink"), ("wink",     "wink"),
            ("眠い",   "sleepy"), ("sleepy",  "sleepy"),
        };

        /// <summary>発話文 1 行から WorkflowPlan を組み立てる。</summary>
        public static WorkflowPlan Parse(string utterance)
        {
            var plan = new WorkflowPlan();
            if (string.IsNullOrEmpty(utterance)) return plan;

            plan.TopMode = IntentVocabulary.DetectTopMode(utterance);

            // intent
            foreach (var (kw, intent) in IntentKeywords)
            {
                if (utterance.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    plan.Intent = intent; break;
                }
            }

            // gesture
            plan.GestureHint = IntentVocabulary.DetectHandPose(utterance);
            plan.HandHint = IntentVocabulary.DetectHandQualifier(utterance);

            // avatar / mode は scene 依存なので Discovery 層で解決 → Workflow では null のまま
            return plan;
        }
    }
}
#endif

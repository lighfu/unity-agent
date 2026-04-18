using System.Collections.Generic;

namespace AjisaiFlow.UnityAgent.Editor
{
    public class BatchToolItem
    {
        public string toolName;
        public string description;
        public string parameters;
        public bool approved = true;
    }

    public static class BatchToolConfirmState
    {
        public static bool IsPending;
        public static List<BatchToolItem> Items;
        public static bool IsResolved;
        public static HashSet<string> ApprovedTools;

        public static void Request(List<BatchToolItem> items)
        {
            Items = items;
            ApprovedTools = null;
            IsResolved = false;
            IsPending = true;
        }

        public static void Resolve(HashSet<string> approved)
        {
            ApprovedTools = approved;
            IsResolved = true;
            IsPending = false;
        }

        public static void Clear()
        {
            IsPending = false;
            Items = null;
            IsResolved = false;
            ApprovedTools = null;
        }
    }
}

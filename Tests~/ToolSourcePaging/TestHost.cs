using System.Collections.Generic;

namespace UnityEngine
{
    public static class Debug { public static void Log(object value) { } }
}

namespace UnityEditor
{
    public class EditorWindow { }
    public enum ImportAssetOptions { ForceUpdate }
    public static class AssetDatabase { public static void ImportAsset(string path, ImportAssetOptions options) { } }

    public class LogEntry
    {
        public int mode;
        public string message;
        public string file;
        public int line;
    }

    public static class LogEntries
    {
        public static readonly List<LogEntry> Rows = new List<LogEntry>();
        public static int Started, Ended;
        public static void StartGettingEntries() => Started++;
        public static void EndGettingEntries() => Ended++;
        public static int GetCount() => Rows.Count;
        public static void Clear() => Rows.Clear();
        public static bool GetEntryInternal(int index, LogEntry output)
        {
            var source = Rows[index];
            output.mode = source.mode;
            output.message = source.message;
            output.file = source.file;
            output.line = source.line;
            return true;
        }
    }
}

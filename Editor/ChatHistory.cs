using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor
{
    [Serializable]
    public class ChatRecord
    {
        public int type;
        public string text;
        public string thinkingText;
    }

    [Serializable]
    public class ChatSession
    {
        public string title;
        public string timestamp;
        public ChatRecord[] records;
    }

    public static class ChatHistoryManager
    {
        private static readonly string HistoryDir =
            Path.Combine(Application.dataPath, "..", "Library", "UnityAgent", "ChatHistory");

        public static void Save(List<ChatEntry> chatHistory)
        {
            if (chatHistory == null || chatHistory.Count == 0) return;

            Directory.CreateDirectory(HistoryDir);

            var records = new List<ChatRecord>();
            string title = null;

            foreach (var entry in chatHistory)
            {
                records.Add(new ChatRecord
                {
                    type = (int)entry.type,
                    text = entry.text,
                    thinkingText = entry.thinkingText
                });

                if (title == null && entry.type == ChatEntry.EntryType.User)
                {
                    var raw = entry.text;
                    if (raw != null && raw.StartsWith("You: "))
                        raw = raw.Substring(5);
                    if (raw != null && raw.Length > 40)
                        raw = raw.Substring(0, 40) + "...";
                    title = raw;
                }
            }

            if (title == null) title = "チャット";

            var session = new ChatSession
            {
                title = title,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                records = records.ToArray()
            };

            string json = JsonUtility.ToJson(session, true);
            string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
            File.WriteAllText(Path.Combine(HistoryDir, fileName), json);
        }

        public static List<ChatSessionHeader> ListSessions()
        {
            var headers = new List<ChatSessionHeader>();

            if (!Directory.Exists(HistoryDir)) return headers;

            var files = Directory.GetFiles(HistoryDir, "*.json");
            Array.Sort(files);
            Array.Reverse(files);

            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var session = JsonUtility.FromJson<ChatSession>(json);
                    headers.Add(new ChatSessionHeader
                    {
                        title = session.title,
                        timestamp = session.timestamp,
                        filePath = file
                    });
                }
                catch
                {
                    // Skip corrupt files
                }
            }

            return headers;
        }

        public static List<ChatEntry> Load(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            string json = File.ReadAllText(filePath);
            var session = JsonUtility.FromJson<ChatSession>(json);
            var entries = new List<ChatEntry>();

            foreach (var record in session.records)
            {
                var entry = new ChatEntry
                {
                    type = (ChatEntry.EntryType)record.type,
                    text = record.text,
                    thinkingText = record.thinkingText
                };
                entries.Add(entry);
            }

            return entries;
        }

        public static void Delete(string filePath)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    public class ChatSessionHeader
    {
        public string title;
        public string timestamp;
        public string filePath;
    }
}

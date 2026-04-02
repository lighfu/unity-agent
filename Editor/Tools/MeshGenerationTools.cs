using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.IO;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// 3D mesh generation tools using the Meshy API.
    /// Supports Text-to-3D and Image-to-3D generation with FBX import.
    /// </summary>
    internal static class MeshGenerationTools
    {
        private const string MeshyBaseUrl = "https://api.meshy.ai";
        private static string GeneratedDir => PackagePaths.GetGeneratedDir("MeshGen");
        private const float PollIntervalSec = 5f;

        // ─── Text-to-3D ───

        [AgentTool("Generate a 3D mesh from text using Meshy API. Returns the imported asset path. " +
                    "Requires Meshy API key in settings. " +
                    "topology: 'triangle' or 'quad'. " +
                    "targetPolycount: 100-300000 (default 30000). " +
                    "aiModel: 'meshy-5', 'meshy-6', or 'latest'.",
            Category = "3D Generation", Risk = ToolRisk.Caution)]
        public static IEnumerator GenerateMeshFromText(
            string prompt,
            string savePath = "Assets",
            string topology = "triangle",
            int targetPolycount = 30000,
            string aiModel = "latest")
        {
            // 1. API key
            string apiKey = GetApiKey();
            if (apiKey == null)
            {
                yield return "Error: Meshy API key not configured. Set it in the Unity AI Agent settings (Advanced tab).";
                yield break;
            }

            // 2. Create preview task
            ToolProgress.Report(0.05f, "3D メッシュ生成中...", $"プロンプト: {TruncateString(prompt, 100)}");

            var bodyJson = new StringBuilder();
            bodyJson.Append("{");
            bodyJson.Append("\"mode\":\"preview\",");
            bodyJson.Append("\"prompt\":"); bodyJson.Append(EscapeJsonString(prompt)); bodyJson.Append(",");
            bodyJson.Append("\"topology\":"); bodyJson.Append(EscapeJsonString(topology)); bodyJson.Append(",");
            bodyJson.Append("\"target_polycount\":"); bodyJson.Append(targetPolycount); bodyJson.Append(",");
            bodyJson.Append("\"ai_model\":"); bodyJson.Append(EscapeJsonString(aiModel));
            bodyJson.Append("}");

            var createReq = CreateRequest("POST", "/openapi/v2/text-to-3d", apiKey, bodyJson.ToString());
            var sendEnum = SendRequest(createReq);
            while (sendEnum.MoveNext())
                yield return sendEnum.Current;

            if (createReq.result != UnityWebRequest.Result.Success)
            {
                string err = $"{createReq.error} (Code: {createReq.responseCode})\n{createReq.downloadHandler.text}";
                createReq.Dispose();
                ToolProgress.Clear();
                yield return $"Error: Failed to create Meshy task: {err}";
                yield break;
            }

            string taskId = ParseJsonStringValue(createReq.downloadHandler.text, "result");
            createReq.Dispose();

            if (string.IsNullOrEmpty(taskId))
            {
                ToolProgress.Clear();
                yield return "Error: No task ID returned from Meshy API.";
                yield break;
            }

            // 3. Poll for completion
            string pollResult = null;
            var pollEnumerator = PollTask("/openapi/v2/text-to-3d", taskId, apiKey);
            while (pollEnumerator.MoveNext())
            {
                if (pollEnumerator.Current is string s)
                    pollResult = s;
                else
                    yield return pollEnumerator.Current;
            }
            ToolProgress.Clear();

            if (pollResult != null && pollResult.StartsWith("Error"))
            {
                yield return pollResult;
                yield break;
            }

            // pollResult is the completed task JSON
            string fbxUrl = ParseJsonStringValue(pollResult, "fbx");
            if (string.IsNullOrEmpty(fbxUrl))
            {
                yield return "Error: No FBX URL found in completed task response.";
                yield break;
            }

            // 4. Download and import
            string safeName = SanitizeFileName(prompt);
            string importResult = null;
            var dlEnumerator = DownloadAndImport(fbxUrl, savePath, safeName);
            while (dlEnumerator.MoveNext())
            {
                if (dlEnumerator.Current is string s)
                    importResult = s;
                else
                    yield return dlEnumerator.Current;
            }
            ToolProgress.Clear();

            yield return importResult;
        }

        // ─── Image-to-3D ───

        [AgentTool("Generate a 3D mesh from an image using Meshy API. " +
                    "imagePath is a project-relative path (e.g. 'Assets/ref.png'). " +
                    "Returns the imported asset path. Requires Meshy API key in settings. " +
                    "topology: 'triangle' or 'quad'. " +
                    "targetPolycount: 100-300000 (default 30000). " +
                    "aiModel: 'meshy-5', 'meshy-6', or 'latest'.",
            Category = "3D Generation", Risk = ToolRisk.Caution)]
        public static IEnumerator GenerateMeshFromImage(
            string imagePath,
            string savePath = "Assets",
            string topology = "triangle",
            int targetPolycount = 30000,
            string aiModel = "latest")
        {
            // 1. API key
            string apiKey = GetApiKey();
            if (apiKey == null)
            {
                yield return "Error: Meshy API key not configured. Set it in the Unity AI Agent settings (Advanced tab).";
                yield break;
            }

            // 2. Read image and convert to base64 data URI
            string fullPath = Path.Combine(Application.dataPath, "..", imagePath);
            if (!File.Exists(fullPath))
            {
                yield return $"Error: Image file not found: {imagePath}";
                yield break;
            }

            byte[] imageBytes = File.ReadAllBytes(fullPath);
            string ext = Path.GetExtension(imagePath).ToLowerInvariant();
            string mimeType;
            switch (ext)
            {
                case ".png": mimeType = "image/png"; break;
                case ".jpg": case ".jpeg": mimeType = "image/jpeg"; break;
                default:
                    yield return $"Error: Unsupported image format '{ext}'. Use .png or .jpg.";
                    yield break;
            }

            string dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";

            ToolProgress.Report(0.05f, "3D メッシュ生成中 (画像)...", $"画像: {Path.GetFileName(imagePath)}");

            // 3. Create task
            var bodyJson = new StringBuilder();
            bodyJson.Append("{");
            bodyJson.Append("\"image_url\":"); bodyJson.Append(EscapeJsonString(dataUri)); bodyJson.Append(",");
            bodyJson.Append("\"topology\":"); bodyJson.Append(EscapeJsonString(topology)); bodyJson.Append(",");
            bodyJson.Append("\"target_polycount\":"); bodyJson.Append(targetPolycount); bodyJson.Append(",");
            bodyJson.Append("\"ai_model\":"); bodyJson.Append(EscapeJsonString(aiModel));
            bodyJson.Append("}");

            var createReq = CreateRequest("POST", "/openapi/v1/image-to-3d", apiKey, bodyJson.ToString());
            var sendEnum = SendRequest(createReq);
            while (sendEnum.MoveNext())
                yield return sendEnum.Current;

            if (createReq.result != UnityWebRequest.Result.Success)
            {
                string err = $"{createReq.error} (Code: {createReq.responseCode})\n{createReq.downloadHandler.text}";
                createReq.Dispose();
                ToolProgress.Clear();
                yield return $"Error: Failed to create Meshy task: {err}";
                yield break;
            }

            string taskId = ParseJsonStringValue(createReq.downloadHandler.text, "result");
            createReq.Dispose();

            if (string.IsNullOrEmpty(taskId))
            {
                ToolProgress.Clear();
                yield return "Error: No task ID returned from Meshy API.";
                yield break;
            }

            // 4. Poll for completion
            string pollResult = null;
            var pollEnumerator = PollTask("/openapi/v1/image-to-3d", taskId, apiKey);
            while (pollEnumerator.MoveNext())
            {
                if (pollEnumerator.Current is string s)
                    pollResult = s;
                else
                    yield return pollEnumerator.Current;
            }
            ToolProgress.Clear();

            if (pollResult != null && pollResult.StartsWith("Error"))
            {
                yield return pollResult;
                yield break;
            }

            string fbxUrl = ParseJsonStringValue(pollResult, "fbx");
            if (string.IsNullOrEmpty(fbxUrl))
            {
                yield return "Error: No FBX URL found in completed task response.";
                yield break;
            }

            // 5. Download and import
            string safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(imagePath));
            string importResult = null;
            var dlEnumerator = DownloadAndImport(fbxUrl, savePath, safeName);
            while (dlEnumerator.MoveNext())
            {
                if (dlEnumerator.Current is string s)
                    importResult = s;
                else
                    yield return dlEnumerator.Current;
            }
            ToolProgress.Clear();

            yield return importResult;
        }

        // ─── Internal helpers ───

        private static string GetApiKey()
        {
            string key = SettingsStore.GetString("UnityAgent_MeshyApiKey", "");
            return string.IsNullOrEmpty(key) ? null : key;
        }

        private static UnityWebRequest CreateRequest(string method, string path, string apiKey, string jsonBody = null)
        {
            string url = MeshyBaseUrl + path;
            var req = new UnityWebRequest(url, method);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            req.timeout = 30;

            if (jsonBody != null)
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.SetRequestHeader("Content-Type", "application/json");
            }

            return req;
        }

        private static IEnumerator SendRequest(UnityWebRequest req)
        {
            var op = req.SendWebRequest();
            while (!op.isDone)
                yield return null;
        }

        private static IEnumerator PollTask(string apiPath, string taskId, string apiKey)
        {
            float startTime = Time.realtimeSinceStartup;

            while (true)
            {
                var getReq = CreateRequest("GET", $"{apiPath}/{taskId}", apiKey);
                var sendEnum = SendRequest(getReq);
                while (sendEnum.MoveNext())
                    yield return sendEnum.Current;

                if (getReq.result != UnityWebRequest.Result.Success)
                {
                    string err = $"{getReq.error} (Code: {getReq.responseCode})";
                    getReq.Dispose();
                    yield return $"Error: Failed to poll task status: {err}";
                    yield break;
                }

                string json = getReq.downloadHandler.text;
                getReq.Dispose();

                string status = ParseJsonStringValue(json, "status");
                int progress = ParseJsonIntValue(json, "progress");
                float elapsed = Time.realtimeSinceStartup - startTime;

                if (status == "SUCCEEDED")
                {
                    yield return json;
                    yield break;
                }

                if (status == "FAILED" || status == "CANCELED")
                {
                    string errorMsg = ParseJsonStringValue(json, "message");
                    if (string.IsNullOrEmpty(errorMsg))
                        errorMsg = status;
                    yield return $"Error: Meshy task {status}: {errorMsg}";
                    yield break;
                }

                ToolProgress.Report(
                    Mathf.Clamp01(progress / 100f),
                    $"3D メッシュ生成中... {progress}% ({elapsed:F0}s)",
                    $"ステータス: {status}");

                // Wait poll interval
                float waitUntil = Time.realtimeSinceStartup + PollIntervalSec;
                while (Time.realtimeSinceStartup < waitUntil)
                    yield return null;
            }
        }

        private static IEnumerator DownloadAndImport(string fbxUrl, string savePath, string name)
        {
            ToolProgress.Report(0.9f, "FBX ダウンロード中...");

            var dlReq = UnityWebRequest.Get(fbxUrl);
            dlReq.timeout = 120;
            var op = dlReq.SendWebRequest();
            while (!op.isDone)
                yield return null;

            if (dlReq.result != UnityWebRequest.Result.Success)
            {
                string err = $"{dlReq.error} (Code: {dlReq.responseCode})";
                dlReq.Dispose();
                yield return $"Error: Failed to download FBX: {err}";
                yield break;
            }

            byte[] fbxData = dlReq.downloadHandler.data;
            dlReq.Dispose();

            // Ensure save directory exists
            ToolUtility.EnsureAssetDirectory(savePath);

            string fileName = $"{name}.fbx";
            string assetPath = $"{savePath}/{fileName}";

            // Avoid overwriting existing files
            if (File.Exists(Path.Combine(Application.dataPath, "..", assetPath)))
            {
                string timestamp = DateTime.Now.ToString("HHmmss");
                fileName = $"{name}_{timestamp}.fbx";
                assetPath = $"{savePath}/{fileName}";
            }

            string fullPath = Path.Combine(Application.dataPath, "..", assetPath);
            File.WriteAllBytes(fullPath, fbxData);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            ToolProgress.Report(1f, "インポート完了");

            yield return $"Success: 3D mesh imported to {assetPath}";
        }

        // ─── JSON helpers ───

        private static string ParseJsonStringValue(string json, string key)
        {
            string keyPattern = "\"" + key + "\"";
            int keyIdx = json.IndexOf(keyPattern, StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            int afterKey = keyIdx + keyPattern.Length;
            int colonIdx = -1;
            for (int i = afterKey; i < json.Length; i++)
            {
                char c = json[i];
                if (c == ':') { colonIdx = i; break; }
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return null;
            }
            if (colonIdx < 0) return null;

            for (int i = colonIdx + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    int valStart = i + 1;
                    int valEnd = json.IndexOf('"', valStart);
                    if (valEnd < 0) return null;
                    return json.Substring(valStart, valEnd - valStart);
                }
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return null;
            }
            return null;
        }

        private static int ParseJsonIntValue(string json, string key)
        {
            string keyPattern = "\"" + key + "\"";
            int keyIdx = json.IndexOf(keyPattern, StringComparison.Ordinal);
            if (keyIdx < 0) return 0;

            int afterKey = keyIdx + keyPattern.Length;
            int colonIdx = -1;
            for (int i = afterKey; i < json.Length; i++)
            {
                char c = json[i];
                if (c == ':') { colonIdx = i; break; }
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return 0;
            }
            if (colonIdx < 0) return 0;

            int numStart = -1;
            for (int i = colonIdx + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c >= '0' && c <= '9') { numStart = i; break; }
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return 0;
            }
            if (numStart < 0) return 0;

            int numEnd = numStart;
            while (numEnd < json.Length && json[numEnd] >= '0' && json[numEnd] <= '9') numEnd++;
            if (int.TryParse(json.Substring(numStart, numEnd - numStart), out int val))
                return val;
            return 0;
        }

        // ─── Utility ───

        private static string EscapeJsonString(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
        }

        private static string TruncateString(string s, int maxLen)
        {
            if (s == null) return "";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "mesh";
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
                else if (c == ' ')
                    sb.Append('_');
            }
            string result = sb.ToString();
            if (result.Length > 40) result = result.Substring(0, 40);
            return string.IsNullOrEmpty(result) ? "mesh" : result;
        }
    }
}

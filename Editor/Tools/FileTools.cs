using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class FileTools
    {
        private static readonly HashSet<string> BinaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", ".psd", ".tif", ".tiff",
            ".fbx", ".obj", ".blend",
            ".asset", ".prefab", ".unity", ".mesh",
            ".anim", ".controller", ".overrideController",
            ".dll", ".so", ".exe",
            ".wav", ".mp3", ".ogg", ".aiff",
            ".mp4", ".mov", ".avi",
            ".zip", ".rar", ".7z",
            ".ttf", ".otf",
        };

        private const int MaxReadLines = 500;

        private static string ValidateAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return "Error: assetPath is empty.";

            if (!assetPath.StartsWith("Assets/"))
                return "Error: Path must start with 'Assets/'. Got: " + assetPath;

            if (assetPath.Contains(".."))
                return "Error: Path must not contain '..'. Got: " + assetPath;

            return null; // valid
        }

        private static bool IsBinaryExtension(string assetPath)
        {
            string ext = Path.GetExtension(assetPath);
            return BinaryExtensions.Contains(ext);
        }

        [AgentTool("Read a text file inside Assets/. Returns the full content (truncated at 500 lines). Usage: ReadFile(\"Assets/Scripts/MyScript.cs\")")]
        public static string ReadFile(string assetPath)
        {
            string error = ValidateAssetPath(assetPath);
            if (error != null) return error;

            if (IsBinaryExtension(assetPath))
                return $"Error: Cannot read binary file '{assetPath}'. Use GetAssetInfo() for binary assets.";

            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return $"Error: File not found: '{assetPath}'.";

            string[] lines = File.ReadAllLines(fullPath);
            if (lines.Length <= MaxReadLines)
            {
                return string.Join("\n", lines);
            }

            var sb = new StringBuilder();
            for (int i = 0; i < MaxReadLines; i++)
            {
                sb.AppendLine(lines[i]);
            }
            sb.AppendLine($"\n... [Truncated: showing {MaxReadLines} of {lines.Length} lines. Use InsertIntoFile with specific line numbers to edit the rest.]");
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Write text content to a file inside Assets/. Creates intermediate directories automatically. Overwrites existing files. Usage: WriteFile(\"Assets/Scripts/Hello.cs\", \"using UnityEngine;...\")")]
        public static string WriteFile(string assetPath, string content)
        {
            string error = ValidateAssetPath(assetPath);
            if (error != null) return error;

            if (IsBinaryExtension(assetPath))
                return $"Error: Cannot write binary file '{assetPath}'. Only text files are supported.";

            string fullPath = Path.GetFullPath(assetPath);
            string dir = Path.GetDirectoryName(fullPath);

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[UnityAgent] Created directory: {dir}");
            }

            if (File.Exists(fullPath))
            {
                string existing = File.ReadAllText(fullPath);
                Debug.Log($"[UnityAgent] Overwriting '{assetPath}' (previous length: {existing.Length} chars)");
            }

            File.WriteAllText(fullPath, content);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            long size = new FileInfo(fullPath).Length;
            return $"File written: '{assetPath}' ({size} bytes)";
        }

        [AgentTool("Insert text into an existing file at a specific line number (1-based). Use lineNumber=0 to append at the end. The file must already exist. Usage: InsertIntoFile(\"Assets/Scripts/MyScript.cs\", \"    public int health;\", 10)")]
        public static string InsertIntoFile(string assetPath, string content, int lineNumber = 0)
        {
            string error = ValidateAssetPath(assetPath);
            if (error != null) return error;

            if (IsBinaryExtension(assetPath))
                return $"Error: Cannot modify binary file '{assetPath}'.";

            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return $"Error: File not found: '{assetPath}'. Use WriteFile to create a new file.";

            var lines = new List<string>(File.ReadAllLines(fullPath));
            string[] newLines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            if (lineNumber == 0)
            {
                // Append at end
                lines.AddRange(newLines);
            }
            else if (lineNumber < 1 || lineNumber > lines.Count + 1)
            {
                return $"Error: lineNumber {lineNumber} is out of range. File has {lines.Count} lines (valid: 1-{lines.Count + 1}, or 0 for append).";
            }
            else
            {
                // Insert before the specified line (1-based → 0-based index)
                lines.InsertRange(lineNumber - 1, newLines);
            }

            File.WriteAllLines(fullPath, lines);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            return $"Inserted {newLines.Length} line(s) into '{assetPath}' at line {(lineNumber == 0 ? "end" : lineNumber.ToString())}. File now has {lines.Count} lines.";
        }

        [AgentTool("Create a C# script with boilerplate (using statements, namespace, class declaration). Provide the class body content. Usage: CreateCSharpScript(\"Assets/Scripts/MyBehaviour.cs\", \"MyBehaviour\", \"void Start() { Debug.Log(\\\"Hello\\\"); }\", \"MonoBehaviour\")")]
        public static string CreateCSharpScript(string assetPath, string className, string content, string baseClass = "MonoBehaviour")
        {
            string error = ValidateAssetPath(assetPath);
            if (error != null) return error;

            if (!assetPath.EndsWith(".cs"))
                return "Error: assetPath must end with '.cs'.";

            string fullPath = Path.GetFullPath(assetPath);
            string dir = Path.GetDirectoryName(fullPath);

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[UnityAgent] Created directory: {dir}");
            }

            if (File.Exists(fullPath))
            {
                string existing = File.ReadAllText(fullPath);
                Debug.Log($"[UnityAgent] Overwriting '{assetPath}' (previous length: {existing.Length} chars)");
            }

            // Indent the content body
            string[] bodyLines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var indentedBody = new StringBuilder();
            foreach (string line in bodyLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    indentedBody.AppendLine();
                else
                    indentedBody.AppendLine("        " + line);
            }

            string inheritance = string.IsNullOrEmpty(baseClass) ? "" : $" : {baseClass}";

            var sb = new StringBuilder();
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using System.Collections;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine($"public class {className}{inheritance}");
            sb.AppendLine("{");
            sb.Append(indentedBody);
            sb.AppendLine("}");

            File.WriteAllText(fullPath, sb.ToString());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            long size = new FileInfo(fullPath).Length;
            return $"C# script created: '{assetPath}' (class {className}{inheritance}, {size} bytes)";
        }

        [AgentTool("Create a shader file. If content starts with 'Shader' it is written as-is (complete shader code). Otherwise it is wrapped in Shader \"shaderName\" { content }. Usage: CreateShader(\"Assets/Shaders/MyShader.shader\", \"Custom/MyShader\", \"SubShader { Pass { ... } }\")")]
        public static string CreateShader(string assetPath, string shaderName, string content)
        {
            string error = ValidateAssetPath(assetPath);
            if (error != null) return error;

            if (!assetPath.EndsWith(".shader"))
                return "Error: assetPath must end with '.shader'.";

            string fullPath = Path.GetFullPath(assetPath);
            string dir = Path.GetDirectoryName(fullPath);

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[UnityAgent] Created directory: {dir}");
            }

            if (File.Exists(fullPath))
            {
                string existing = File.ReadAllText(fullPath);
                Debug.Log($"[UnityAgent] Overwriting '{assetPath}' (previous length: {existing.Length} chars)");
            }

            string shaderCode;
            if (content.TrimStart().StartsWith("Shader"))
            {
                // Complete shader code provided
                shaderCode = content;
            }
            else
            {
                // Wrap in Shader block
                shaderCode = $"Shader \"{shaderName}\" {{\n{content}\n}}";
            }

            File.WriteAllText(fullPath, shaderCode);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            long size = new FileInfo(fullPath).Length;
            return $"Shader created: '{assetPath}' (name: \"{shaderName}\", {size} bytes)";
        }
    }
}

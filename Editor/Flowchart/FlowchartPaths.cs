using System.IO;
using AjisaiFlow.UnityAgent.Editor.Tools;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart
{
    /// <summary>
    /// Centralized path resolution for flowchart sources and their compiled skill artifacts.
    /// Flowchart graphs sit beside the user-skill directory so a saved flow appears in
    /// SkillManagementWindow without extra plumbing.
    /// </summary>
    public static class FlowchartPaths
    {
        /// <summary>User-writable root for .flow.json source-of-truth files.</summary>
        public static readonly string FlowchartsDir = Path.Combine(
            Application.dataPath, "紫陽花広場", "UnityAgent", "Editor", "Flowcharts");

        /// <summary>Compiled .md output sits in the existing user skill directory.</summary>
        public static string SkillsDir => SkillTools.UserSkillsPath;

        public static string FlowFile(string id) => Path.Combine(FlowchartsDir, id + ".flow.json");
        public static string SkillFile(string id) => Path.Combine(SkillsDir, id + ".md");

        public static void EnsureDirs()
        {
            Directory.CreateDirectory(FlowchartsDir);
            Directory.CreateDirectory(SkillsDir);
        }
    }
}

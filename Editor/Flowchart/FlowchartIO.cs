using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Flowchart
{
    /// <summary>
    /// Disk persistence for FlowchartGraph. JsonUtility-based; pretty-printed output
    /// so users can diff and (carefully) hand-edit the .flow.json if needed.
    /// </summary>
    public static class FlowchartIO
    {
        public static FlowchartGraph Load(string path)
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<FlowchartGraph>(json);
        }

        public static FlowchartGraph LoadById(string id) => Load(FlowchartPaths.FlowFile(id));

        public static void Save(FlowchartGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (string.IsNullOrEmpty(graph.id))
                throw new InvalidOperationException("FlowchartGraph.id must be set before saving.");

            FlowchartPaths.EnsureDirs();
            string path = FlowchartPaths.FlowFile(graph.id);
            string json = JsonUtility.ToJson(graph, prettyPrint: true);
            File.WriteAllText(path, json);
        }

        public static IEnumerable<string> ListIds()
        {
            if (!Directory.Exists(FlowchartPaths.FlowchartsDir)) yield break;
            foreach (var file in Directory.GetFiles(FlowchartPaths.FlowchartsDir, "*.flow.json"))
            {
                string name = Path.GetFileName(file);
                // strip ".flow.json"
                if (name.EndsWith(".flow.json", StringComparison.Ordinal))
                    yield return name.Substring(0, name.Length - ".flow.json".Length);
            }
        }

        public static bool Delete(string id)
        {
            string flow = FlowchartPaths.FlowFile(id);
            string md = FlowchartPaths.SkillFile(id);
            bool any = false;
            if (File.Exists(flow)) { File.Delete(flow); any = true; }
            if (File.Exists(md)) { File.Delete(md); any = true; }
            return any;
        }
    }
}

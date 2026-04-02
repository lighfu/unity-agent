using UnityEngine;
using System.Collections.Generic;
using System.Linq;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class InteractionTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool("Open the Mesh Painter window targeting a specific mesh for manual texture editing. Use this when the user wants to manually adjust colors or paint specific UV islands. The window allows Scene-click island selection, UV preview, and per-island color painting.")]
        public static string OpenMeshPainter(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer found on '{gameObjectName}'.";

            MeshPainterWindow.OpenForRenderer(renderer);
            return $"Success: Mesh Painter opened for '{gameObjectName}'.";
        }

        [AgentTool("Ask the user to choose from options in chat UI. " +
            "option1 and option2 are REQUIRED (minimum 2). Up to 4 options. " +
            "importance: info, warning, critical. " +
            "The user can also type a free-text response instead of selecting an option. " +
            "NEVER use this to ask for technical info you can inspect yourself (object names, errors, etc.).")]
        public static string AskUser(string question, string option1, string option2,
            string option3 = "", string option4 = "", string importance = "info")
        {
            var options = new List<string> { option1, option2 };
            if (!string.IsNullOrEmpty(option3)) options.Add(option3);
            if (!string.IsNullOrEmpty(option4)) options.Add(option4);

            UserChoiceState.Request(question, options.ToArray(), importance);
            return "__WAITING_USER_CHOICE__";
        }
    }
}

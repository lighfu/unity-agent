using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class SceneManagementTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        [AgentTool("List all scenes in the Build Settings with their index, path, and enabled state.")]
        public static string ListBuildScenes()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0) return "No scenes in Build Settings.";

            var sb = new StringBuilder();
            sb.AppendLine($"Build Settings Scenes ({scenes.Length}):");

            for (int i = 0; i < scenes.Length; i++)
            {
                var scene = scenes[i];
                string enabled = scene.enabled ? "ON" : "OFF";
                sb.AppendLine($"  [{i}] {scene.path} [{enabled}]");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("List all currently loaded scenes with details: name, path, root object count, dirty state, and active state.")]
        public static string ListLoadedScenes()
        {
            int count = SceneManager.sceneCount;
            if (count == 0) return "No scenes loaded.";

            var activeScene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.AppendLine($"Loaded Scenes ({count}):");

            for (int i = 0; i < count; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                bool isActive = scene == activeScene;
                string dirty = scene.isDirty ? " [unsaved]" : "";
                string active = isActive ? " [ACTIVE]" : "";
                sb.AppendLine($"  [{i}] {scene.name}{active}{dirty}");
                sb.AppendLine($"      Path: {scene.path}");
                sb.AppendLine($"      Root objects: {scene.rootCount}");
                sb.AppendLine($"      Loaded: {scene.isLoaded}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Save the currently active scene. If it's a new unsaved scene, use SaveSceneAs instead.")]
        public static string SaveCurrentScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
                return "Error: Active scene has no path. Use SaveSceneAs to save it to a specific location.";

            bool result = EditorSceneManager.SaveScene(scene);
            return result
                ? $"Success: Saved scene '{scene.name}' at '{scene.path}'."
                : $"Error: Failed to save scene '{scene.name}'.";
        }

        [AgentTool("Save the active scene to a new path. savePath: e.g. 'Assets/Scenes/MyScene.unity'.")]
        public static string SaveSceneAs(string savePath)
        {
            if (!savePath.EndsWith(".unity"))
                savePath += ".unity";

            // Ensure directory exists
            string dir = System.IO.Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                string[] folders = dir.Replace('\\', '/').Split('/');
                string current = folders[0];
                for (int i = 1; i < folders.Length; i++)
                {
                    string next = current + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, folders[i]);
                    current = next;
                }
            }

            var scene = SceneManager.GetActiveScene();
            bool result = EditorSceneManager.SaveScene(scene, savePath);
            return result
                ? $"Success: Saved scene as '{savePath}'."
                : $"Error: Failed to save scene to '{savePath}'.";
        }

        [AgentTool("Load a scene by path. mode: 'single' replaces all scenes, 'additive' adds alongside current scenes. Saves current scene first if dirty.")]
        public static string LoadScene(string scenePath, string mode = "single")
        {
            if (!System.IO.File.Exists(scenePath) && !scenePath.StartsWith("Assets/"))
                return $"Error: Scene not found at '{scenePath}'.";

            // Save current if dirty
            var currentScene = SceneManager.GetActiveScene();
            if (currentScene.isDirty)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return "Cancelled: User cancelled the save prompt.";
            }

            OpenSceneMode openMode = mode.ToLower() == "additive"
                ? OpenSceneMode.Additive
                : OpenSceneMode.Single;

            var newScene = EditorSceneManager.OpenScene(scenePath, openMode);
            return newScene.IsValid()
                ? $"Success: Loaded scene '{newScene.name}' ({mode})."
                : $"Error: Failed to load scene '{scenePath}'.";
        }

        [AgentTool("Create a new empty scene. mode: 'single' replaces all, 'additive' adds alongside current.")]
        public static string CreateNewScene(string mode = "single")
        {
            // Save current if dirty
            var currentScene = SceneManager.GetActiveScene();
            if (currentScene.isDirty)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return "Cancelled: User cancelled the save prompt.";
            }

            NewSceneMode newMode = mode.ToLower() == "additive"
                ? NewSceneMode.Additive
                : NewSceneMode.Single;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, newMode);
            return $"Success: Created new scene '{scene.name}'.";
        }

        [AgentTool("Set which loaded scene is the active scene (used for new object placement). sceneName: name of a loaded scene.")]
        public static string SetActiveScene(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.name == sceneName)
                {
                    if (!scene.isLoaded)
                        return $"Error: Scene '{sceneName}' is not loaded.";

                    SceneManager.SetActiveScene(scene);
                    return $"Success: Set '{sceneName}' as the active scene.";
                }
            }

            return $"Error: Scene '{sceneName}' not found among loaded scenes.";
        }

        [AgentTool("Unload an additively loaded scene. Cannot unload the last remaining scene.")]
        public static string UnloadScene(string sceneName)
        {
            if (SceneManager.sceneCount <= 1)
                return "Error: Cannot unload the only loaded scene.";

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.name == sceneName)
                {
                    if (scene.isDirty)
                    {
                        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                            return "Cancelled: User cancelled the save prompt.";
                    }

                    EditorSceneManager.CloseScene(scene, true);
                    return $"Success: Unloaded and removed scene '{sceneName}'.";
                }
            }

            return $"Error: Scene '{sceneName}' not found among loaded scenes.";
        }

        [AgentTool("Get detailed info about the active scene: path, root objects, dirty state, lighting settings.")]
        public static string GetSceneInfo()
        {
            var scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.AppendLine($"Active Scene: {scene.name}");
            sb.AppendLine($"  Path: {(string.IsNullOrEmpty(scene.path) ? "(unsaved)" : scene.path)}");
            sb.AppendLine($"  Dirty: {scene.isDirty}");
            sb.AppendLine($"  Root objects: {scene.rootCount}");
            sb.AppendLine($"  Build index: {scene.buildIndex}");

            // List root objects
            var roots = scene.GetRootGameObjects();
            if (roots.Length > 0)
            {
                sb.AppendLine($"  Root GameObjects:");
                foreach (var root in roots.Take(20))
                {
                    int childCount = root.transform.childCount;
                    bool active = root.activeSelf;
                    sb.AppendLine($"    - {root.name} (children={childCount}, active={active})");
                }
                if (roots.Length > 20)
                    sb.AppendLine($"    ... +{roots.Length - 20} more");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Move a root GameObject from one loaded scene to another. Useful for organizing objects across additive scenes.")]
        public static string MoveToScene(string gameObjectName, string targetSceneName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            if (go.transform.parent != null)
                return $"Error: '{gameObjectName}' is not a root object. Only root objects can be moved between scenes.";

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.name == targetSceneName)
                {
                    if (!scene.isLoaded) return $"Error: Scene '{targetSceneName}' is not loaded.";

                    Undo.RecordObject(go, "Move to Scene");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    return $"Success: Moved '{gameObjectName}' to scene '{targetSceneName}'.";
                }
            }

            return $"Error: Scene '{targetSceneName}' not found among loaded scenes.";
        }

        [AgentTool("Mark the active scene as dirty (needs saving). Useful after programmatic changes.")]
        public static string MarkSceneDirty()
        {
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            return $"Success: Marked scene '{scene.name}' as dirty.";
        }
    }
}

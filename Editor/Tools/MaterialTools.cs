using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class MaterialTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        private static Shader FindDefaultShader()
        {
            // lilToon を優先、見つからなければ Standard にフォールバック
            var shader = Shader.Find("lilToon");
            if (shader != null) return shader;

            shader = Shader.Find("Standard");
            if (shader != null) return shader;

            return Shader.Find("Unlit/Color");
        }

        [AgentTool("Create a new Material with a specific color. Color values are 0-1. Optional shaderName parameter (default: lilToon).")]
        public static string CreateMaterial(string name, float r, float g, float b, float a = 1.0f, string shaderName = "")
        {
            string folderPath = "Assets/Materials";
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            string path = $"{folderPath}/{name}.mat";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            Shader shader;
            if (!string.IsNullOrEmpty(shaderName))
            {
                shader = Shader.Find(shaderName);
                if (shader == null)
                    return $"Error: Shader '{shaderName}' not found.";
            }
            else
            {
                shader = FindDefaultShader();
                if (shader == null)
                    return "Error: No suitable shader found (tried lilToon, Standard, Unlit/Color).";
            }

            Material mat = new Material(shader);
            mat.color = new Color(r, g, b, a);

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();

            Undo.RegisterCreatedObjectUndo(mat, "Create Material via Agent");

            return $"Success: Created material '{name}' (shader: {shader.name}) at '{path}'.";
        }

        [AgentTool("Set the color of a GameObject's material. Color values are 0-1. If the object uses the default material, a new material is created automatically.")]
        public static string SetMaterialColor(string gameObjectName, float r, float g, float b, float a = 1.0f)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: GameObject '{gameObjectName}' has no Renderer.";

            var mat = renderer.sharedMaterial;

            // デフォルトマテリアル（アセットとして保存されていない）の場合は新規作成
            if (mat == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mat)))
            {
                var shader = FindDefaultShader();
                if (shader == null)
                    return "Error: No suitable shader found.";

                mat = new Material(shader);
                mat.name = $"{go.name}_Material";

                string folderPath = "Assets/Materials";
                if (!AssetDatabase.IsValidFolder(folderPath))
                    AssetDatabase.CreateFolder("Assets", "Materials");

                string matPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{mat.name}.mat");
                AssetDatabase.CreateAsset(mat, matPath);

                // SerializedObject 経由でマテリアルを割り当て
                var so = new SerializedObject(renderer);
                var materialsProp = so.FindProperty("m_Materials");
                materialsProp.arraySize = 1;
                materialsProp.GetArrayElementAtIndex(0).objectReferenceValue = mat;
                so.ApplyModifiedProperties();
            }

            Undo.RecordObject(mat, "Set Material Color via Agent");
            mat.color = new Color(r, g, b, a);

            EditorUtility.SetDirty(mat);
            EditorUtility.SetDirty(renderer);
            EditorSceneManager.MarkSceneDirty(go.scene);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();

            return $"Success: Set color of '{gameObjectName}' to ({r}, {g}, {b}, {a}). Shader: {mat.shader.name}.";
        }

        [AgentTool("Assign an existing material to a GameObject by material name or asset path.")]
        public static string AssignMaterial(string gameObjectName, string materialName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: GameObject '{gameObjectName}' has no Renderer.";

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialName);

            if (mat == null)
            {
                string searchName = Path.GetFileNameWithoutExtension(materialName);
                string[] guids = AssetDatabase.FindAssets($"{searchName} t:Material");

                if (guids.Length == 0)
                    return $"Error: Material '{materialName}' not found in project.";

                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            }

            if (mat == null)
                return $"Error: Could not load material '{materialName}'.";

            // SerializedObject 経由で確実にシリアライズする
            Undo.RegisterCompleteObjectUndo(renderer, "Assign Material via Agent");
            var so = new SerializedObject(renderer);
            so.Update();
            var materialsProp = so.FindProperty("m_Materials");
            materialsProp.arraySize = 1;
            materialsProp.GetArrayElementAtIndex(0).objectReferenceValue = mat;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(renderer);
            EditorSceneManager.MarkSceneDirty(go.scene);
            SceneView.RepaintAll();

            return $"Success: Assigned material '{mat.name}' (path: {AssetDatabase.GetAssetPath(mat)}, shader: {mat.shader.name}) to '{gameObjectName}'.";
        }
    }
}

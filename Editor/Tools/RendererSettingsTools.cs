using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System.Linq;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Renderer 設定（影・ライトプローブ・バウンド）、Light、TrailRenderer、LineRenderer ツール。
    /// VRChat アバターの描画最適化・視覚エフェクトに使用。
    /// </summary>
    public static class RendererSettingsTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);
        // =================================================================
        // Renderer Settings
        // =================================================================

        [AgentTool(@"Configure rendering settings on a Renderer component. Useful for VRChat avatar optimization.
shadowCasting: 0=Off, 1=On, 2=TwoSided, 3=ShadowsOnly.
lightProbes: 0=Off, 1=BlendProbes, 2=UseProxyVolume, 4=CustomProvided.
reflectionProbes: 0=Off, 1=BlendProbes, 2=BlendProbesAndSkybox, 3=Simple.")]
        public static string ConfigureRendererSettings(string goName, int shadowCasting = -1, int receiveShadows = -1,
            int lightProbes = -1, int reflectionProbes = -1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer on '{goName}'.";

            Undo.RecordObject(renderer, "Configure Renderer Settings");

            if (shadowCasting >= 0) renderer.shadowCastingMode = (ShadowCastingMode)shadowCasting;
            if (receiveShadows >= 0) renderer.receiveShadows = receiveShadows != 0;
            if (lightProbes >= 0) renderer.lightProbeUsage = (LightProbeUsage)lightProbes;
            if (reflectionProbes >= 0) renderer.reflectionProbeUsage = (ReflectionProbeUsage)reflectionProbes;

            EditorUtility.SetDirty(renderer);
            return $"Success: Configured renderer settings on '{goName}' (shadow={renderer.shadowCastingMode}, recv={renderer.receiveShadows}, lightProbes={renderer.lightProbeUsage}).";
        }

        [AgentTool("Inspect Renderer settings on a GameObject. Shows materials, shadow, probe, bounds, and sorting info.")]
        public static string InspectRendererSettings(string goName)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return $"Error: No Renderer on '{goName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Renderer on '{goName}' ({renderer.GetType().Name}):");
            sb.AppendLine($"  Enabled: {renderer.enabled}");
            sb.AppendLine($"  Shadow Casting: {renderer.shadowCastingMode}");
            sb.AppendLine($"  Receive Shadows: {renderer.receiveShadows}");
            sb.AppendLine($"  Light Probes: {renderer.lightProbeUsage}");
            sb.AppendLine($"  Reflection Probes: {renderer.reflectionProbeUsage}");
            sb.AppendLine($"  Sorting Layer: {renderer.sortingLayerName} (order={renderer.sortingOrder})");
            sb.AppendLine($"  Bounds: center={renderer.bounds.center}, size={renderer.bounds.size}");

            // Materials
            var mats = renderer.sharedMaterials;
            sb.AppendLine($"  Materials ({mats.Length}):");
            for (int i = 0; i < mats.Length; i++)
            {
                string matName = mats[i] != null ? mats[i].name : "null";
                string shader = mats[i] != null ? mats[i].shader.name : "N/A";
                sb.AppendLine($"    [{i}] {matName} (shader={shader})");
            }

            // SkinnedMeshRenderer specifics
            if (renderer is SkinnedMeshRenderer smr)
            {
                sb.AppendLine($"  [SkinnedMeshRenderer]");
                sb.AppendLine($"    Quality: {smr.quality}");
                sb.AppendLine($"    UpdateWhenOffscreen: {smr.updateWhenOffscreen}");
                sb.AppendLine($"    RootBone: {(smr.rootBone != null ? smr.rootBone.name : "none")}");
                sb.AppendLine($"    LocalBounds: center={smr.localBounds.center}, size={smr.localBounds.size}");
                sb.AppendLine($"    BlendShapes: {(smr.sharedMesh != null ? smr.sharedMesh.blendShapeCount : 0)}");
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Set the local bounds of a SkinnedMeshRenderer. Useful for fixing culling issues. center/size format: 'x,y,z'.")]
        public static string SetSkinnedMeshBounds(string goName, string center, string size)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) return $"Error: No SkinnedMeshRenderer on '{goName}'.";

            var c = ParseVector3(center);
            var s = ParseVector3(size);
            if (!c.HasValue || !s.HasValue) return "Error: Invalid center or size format. Use 'x,y,z'.";

            Undo.RecordObject(smr, "Set SkinnedMesh Bounds");
            smr.localBounds = new Bounds(c.Value, s.Value);

            EditorUtility.SetDirty(smr);
            return $"Success: Set bounds of '{goName}' to center={c.Value}, size={s.Value}.";
        }

        [AgentTool("Batch configure shadow settings on all renderers under a GameObject (recursive). Useful for avatar-wide optimization.")]
        public static string BatchConfigureShadows(string rootGoName, int shadowCasting = 1, int receiveShadows = 1)
        {
            var go = FindGO(rootGoName);
            if (go == null) return $"Error: GameObject '{rootGoName}' not found.";

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return $"No renderers under '{rootGoName}'.";

            int count = 0;
            foreach (var r in renderers)
            {
                Undo.RecordObject(r, "Batch Configure Shadows");
                r.shadowCastingMode = (ShadowCastingMode)shadowCasting;
                r.receiveShadows = receiveShadows != 0;
                EditorUtility.SetDirty(r);
                count++;
            }

            return $"Success: Configured shadows on {count} renderers under '{rootGoName}' (cast={((ShadowCastingMode)shadowCasting)}, receive={receiveShadows != 0}).";
        }

        // =================================================================
        // Light
        // =================================================================

        [AgentTool("Add a Light component to a GameObject. type: Directional, Point, Spot, Area. color is hex (e.g. '#FFFFFF').")]
        public static string AddLight(string goName, string type = "Point", string color = "#FFFFFF", float intensity = 1f, float range = 10f, float spotAngle = 30f)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            LightType lightType;
            switch (type.ToLower())
            {
                case "directional": lightType = LightType.Directional; break;
                case "point": lightType = LightType.Point; break;
                case "spot": lightType = LightType.Spot; break;
                case "area": lightType = LightType.Area; break;
                default: return $"Error: Unknown light type '{type}'. Valid: Directional, Point, Spot, Area.";
            }

            var light = Undo.AddComponent<Light>(go);
            light.type = lightType;
            light.intensity = intensity;
            light.range = range;
            if (lightType == LightType.Spot) light.spotAngle = spotAngle;
            if (ColorUtility.TryParseHtmlString(color, out Color c)) light.color = c;

            return $"Success: Added {type} Light to '{goName}' (intensity={intensity}, range={range}).";
        }

        [AgentTool("Configure an existing Light component. Use -1 for unchanged float values.")]
        public static string ConfigureLight(string goName, float intensity = -1f, float range = -1f, float spotAngle = -1f,
            string color = "", int shadows = -1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var light = go.GetComponent<Light>();
            if (light == null) return $"Error: No Light on '{goName}'.";

            Undo.RecordObject(light, "Configure Light via Agent");

            if (intensity >= 0) light.intensity = intensity;
            if (range >= 0) light.range = range;
            if (spotAngle >= 0) light.spotAngle = spotAngle;
            if (!string.IsNullOrEmpty(color) && ColorUtility.TryParseHtmlString(color, out Color c)) light.color = c;
            if (shadows >= 0) light.shadows = (LightShadows)shadows;

            EditorUtility.SetDirty(light);
            return $"Success: Configured Light on '{goName}'.";
        }

        [AgentTool("List all Light components in the scene with their type, color, intensity, and range.")]
        public static string ListLights()
        {
            var lights = UnityEngine.Object.FindObjectsOfType<Light>(true);
            if (lights.Length == 0) return "No Light components in the scene.";

            var sb = new StringBuilder();
            sb.AppendLine($"Lights in scene ({lights.Length}):");
            foreach (var l in lights.OrderBy(l => l.gameObject.name))
            {
                string path = GetHierarchyPath(l.transform);
                sb.AppendLine($"  {path}: {l.type}, color={l.color}, intensity={l.intensity:F2}, range={l.range:F1}, shadows={l.shadows}");
            }
            return sb.ToString().TrimEnd();
        }

        // =================================================================
        // TrailRenderer
        // =================================================================

        [AgentTool("Add a TrailRenderer to a GameObject. Commonly used for VRChat avatar visual effects. color is hex, materialPath is optional.")]
        public static string AddTrailRenderer(string goName, float time = 1f, float startWidth = 0.1f, float endWidth = 0f,
            string color = "#FFFFFF", string materialPath = "")
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var trail = Undo.AddComponent<TrailRenderer>(go);
            trail.time = time;
            trail.startWidth = startWidth;
            trail.endWidth = endWidth;

            if (ColorUtility.TryParseHtmlString(color, out Color c))
            {
                trail.startColor = c;
                trail.endColor = new Color(c.r, c.g, c.b, 0f);
            }

            if (!string.IsNullOrEmpty(materialPath))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (mat != null) trail.sharedMaterial = mat;
            }

            return $"Success: Added TrailRenderer to '{goName}' (time={time}s, width={startWidth}→{endWidth}).";
        }

        [AgentTool("Configure an existing TrailRenderer. Use -1 for unchanged float values.")]
        public static string ConfigureTrailRenderer(string goName, float time = -1f, float startWidth = -1f, float endWidth = -1f,
            string startColor = "", string endColor = "", int castShadows = -1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var trail = go.GetComponent<TrailRenderer>();
            if (trail == null) return $"Error: No TrailRenderer on '{goName}'.";

            Undo.RecordObject(trail, "Configure TrailRenderer via Agent");

            if (time >= 0) trail.time = time;
            if (startWidth >= 0) trail.startWidth = startWidth;
            if (endWidth >= 0) trail.endWidth = endWidth;
            if (!string.IsNullOrEmpty(startColor) && ColorUtility.TryParseHtmlString(startColor, out Color sc)) trail.startColor = sc;
            if (!string.IsNullOrEmpty(endColor) && ColorUtility.TryParseHtmlString(endColor, out Color ec)) trail.endColor = ec;
            if (castShadows >= 0) trail.shadowCastingMode = (ShadowCastingMode)castShadows;

            EditorUtility.SetDirty(trail);
            return $"Success: Configured TrailRenderer on '{goName}'.";
        }

        // =================================================================
        // LineRenderer
        // =================================================================

        [AgentTool("Add a LineRenderer to a GameObject. positions is semicolon-separated 'x,y,z' points (e.g. '0,0,0;1,1,0;2,0,0').")]
        public static string AddLineRenderer(string goName, string positions = "", float startWidth = 0.1f, float endWidth = 0.1f,
            string color = "#FFFFFF", string materialPath = "")
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var line = Undo.AddComponent<LineRenderer>(go);
            line.startWidth = startWidth;
            line.endWidth = endWidth;
            line.useWorldSpace = false;

            if (ColorUtility.TryParseHtmlString(color, out Color c))
            {
                line.startColor = c;
                line.endColor = c;
            }

            if (!string.IsNullOrEmpty(positions))
            {
                var points = positions.Split(';')
                    .Select(p => ParseVector3(p.Trim()))
                    .Where(v => v.HasValue)
                    .Select(v => v.Value)
                    .ToArray();

                if (points.Length > 0)
                {
                    line.positionCount = points.Length;
                    line.SetPositions(points);
                }
            }

            if (!string.IsNullOrEmpty(materialPath))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (mat != null) line.sharedMaterial = mat;
            }

            return $"Success: Added LineRenderer to '{goName}' (width={startWidth}→{endWidth}, points={line.positionCount}).";
        }

        [AgentTool("Set positions on a LineRenderer. positions is semicolon-separated 'x,y,z' points.")]
        public static string SetLineRendererPositions(string goName, string positions)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var line = go.GetComponent<LineRenderer>();
            if (line == null) return $"Error: No LineRenderer on '{goName}'.";

            var points = positions.Split(';')
                .Select(p => ParseVector3(p.Trim()))
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .ToArray();

            if (points.Length == 0) return "Error: No valid positions parsed.";

            Undo.RecordObject(line, "Set LineRenderer Positions");
            line.positionCount = points.Length;
            line.SetPositions(points);

            EditorUtility.SetDirty(line);
            return $"Success: Set {points.Length} positions on LineRenderer of '{goName}'.";
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static Vector3? ParseVector3(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(',');
            if (parts.Length != 3) return null;
            if (float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y) &&
                float.TryParse(parts[2].Trim(), out float z))
                return new Vector3(x, y, z);
            return null;
        }

        private static string GetHierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var current = t.parent;
            while (current != null)
            {
                sb.Insert(0, current.name + "/");
                current = current.parent;
            }
            return sb.ToString();
        }
    }
}

using UnityEngine;
using UnityEditor;
using System;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// ParticleSystem の追加・検査・設定ツール。
    /// VRChat アバターのエフェクト設定に使用。
    /// </summary>
    public static class ParticleTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        [AgentTool("Add a ParticleSystem to a GameObject. Creates with sensible defaults for VRChat avatar effects.")]
        public static string AddParticleSystem(string goName)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            if (go.GetComponent<ParticleSystem>() != null)
                return $"Error: '{goName}' already has a ParticleSystem.";

            Undo.AddComponent<ParticleSystem>(go);
            return $"Success: Added ParticleSystem to '{goName}'. Use ConfigureParticle* tools to adjust settings.";
        }

        [AgentTool("Inspect a ParticleSystem and show all main module settings (duration, looping, start values, max particles, simulation space, etc).")]
        public static string InspectParticleSystem(string goName)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var main = ps.main;
            var emission = ps.emission;
            var shape = ps.shape;
            var renderer = go.GetComponent<ParticleSystemRenderer>();

            var sb = new StringBuilder();
            sb.AppendLine($"ParticleSystem on '{goName}':");
            sb.AppendLine($"  [Main Module]");
            sb.AppendLine($"    Duration: {main.duration}s");
            sb.AppendLine($"    Looping: {main.loop}");
            sb.AppendLine($"    Prewarm: {main.prewarm}");
            sb.AppendLine($"    StartLifetime: {FormatMinMaxCurve(main.startLifetime)}");
            sb.AppendLine($"    StartSpeed: {FormatMinMaxCurve(main.startSpeed)}");
            sb.AppendLine($"    StartSize: {FormatMinMaxCurve(main.startSize)}");
            sb.AppendLine($"    StartColor: {main.startColor.color}");
            sb.AppendLine($"    GravityModifier: {FormatMinMaxCurve(main.gravityModifier)}");
            sb.AppendLine($"    SimulationSpace: {main.simulationSpace}");
            sb.AppendLine($"    MaxParticles: {main.maxParticles}");
            sb.AppendLine($"    PlayOnAwake: {main.playOnAwake}");

            sb.AppendLine($"  [Emission]");
            sb.AppendLine($"    Enabled: {emission.enabled}");
            sb.AppendLine($"    RateOverTime: {FormatMinMaxCurve(emission.rateOverTime)}");
            sb.AppendLine($"    RateOverDistance: {FormatMinMaxCurve(emission.rateOverDistance)}");
            sb.AppendLine($"    Bursts: {emission.burstCount}");

            sb.AppendLine($"  [Shape]");
            sb.AppendLine($"    Enabled: {shape.enabled}");
            sb.AppendLine($"    ShapeType: {shape.shapeType}");
            sb.AppendLine($"    Radius: {shape.radius}");
            sb.AppendLine($"    Angle: {shape.angle}");

            if (renderer != null)
            {
                sb.AppendLine($"  [Renderer]");
                sb.AppendLine($"    RenderMode: {renderer.renderMode}");
                sb.AppendLine($"    Material: {(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "none")}");
                sb.AppendLine($"    TrailMaterial: {(renderer.trailMaterial != null ? renderer.trailMaterial.name : "none")}");
                sb.AppendLine($"    SortingOrder: {renderer.sortingOrder}");
            }

            // Check other enabled modules
            sb.AppendLine($"  [Enabled Modules]");
            if (ps.colorOverLifetime.enabled) sb.AppendLine($"    ColorOverLifetime: enabled");
            if (ps.sizeOverLifetime.enabled) sb.AppendLine($"    SizeOverLifetime: enabled");
            if (ps.velocityOverLifetime.enabled) sb.AppendLine($"    VelocityOverLifetime: enabled");
            if (ps.rotationOverLifetime.enabled) sb.AppendLine($"    RotationOverLifetime: enabled");
            if (ps.noise.enabled) sb.AppendLine($"    Noise: enabled");
            if (ps.trails.enabled) sb.AppendLine($"    Trails: enabled");
            if (ps.collision.enabled) sb.AppendLine($"    Collision: enabled");
            if (ps.subEmitters.enabled) sb.AppendLine($"    SubEmitters: enabled");
            if (ps.textureSheetAnimation.enabled) sb.AppendLine($"    TextureSheetAnimation: enabled");
            if (ps.lights.enabled) sb.AppendLine($"    Lights: enabled");

            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"Configure the Main module of a ParticleSystem. Use -1 for unchanged float values. simulationSpace: 0=Local, 1=World, 2=Custom.")]
        public static string ConfigureParticleMain(string goName, float duration = -1f, int looping = -1, float startLifetime = -1f,
            float startSpeed = -1f, float startSize = -1f, string startColor = "", float gravityModifier = -999f,
            int simulationSpace = -1, int maxParticles = -1, int playOnAwake = -1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            // ParticleSystem requires SerializedObject for proper editing
            var so = new SerializedObject(ps);

            if (duration >= 0) so.FindProperty("lengthInSec").floatValue = duration;
            if (looping >= 0) so.FindProperty("looping").boolValue = looping != 0;
            if (startLifetime >= 0) SetMinMaxCurveConstant(so, "InitialModule.startLifetime", startLifetime);
            if (startSpeed >= 0) SetMinMaxCurveConstant(so, "InitialModule.startSpeed", startSpeed);
            if (startSize >= 0) SetMinMaxCurveConstant(so, "InitialModule.startSize", startSize);
            if (gravityModifier > -999f) SetMinMaxCurveConstant(so, "InitialModule.gravityModifier", gravityModifier);
            if (simulationSpace >= 0 && simulationSpace <= 2) so.FindProperty("moveWithTransform").intValue = simulationSpace;
            if (maxParticles >= 0) so.FindProperty("InitialModule.maxNumParticles").intValue = maxParticles;
            if (playOnAwake >= 0) so.FindProperty("playOnAwake").boolValue = playOnAwake != 0;

            if (!string.IsNullOrEmpty(startColor))
            {
                if (ColorUtility.TryParseHtmlString(startColor, out Color color))
                {
                    var colorProp = so.FindProperty("InitialModule.startColor.maxColor");
                    if (colorProp != null) colorProp.colorValue = color;
                }
            }

            so.ApplyModifiedProperties();
            return $"Success: Configured ParticleSystem main module on '{goName}'.";
        }

        [AgentTool("Configure the Emission module of a ParticleSystem. Set rateOverTime and/or rateOverDistance.")]
        public static string ConfigureParticleEmission(string goName, float rateOverTime = -1f, float rateOverDistance = -1f, int enabled = 1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var so = new SerializedObject(ps);

            so.FindProperty("EmissionModule.enabled").boolValue = enabled != 0;
            if (rateOverTime >= 0) SetMinMaxCurveConstant(so, "EmissionModule.rateOverTime", rateOverTime);
            if (rateOverDistance >= 0) SetMinMaxCurveConstant(so, "EmissionModule.rateOverDistance", rateOverDistance);

            so.ApplyModifiedProperties();
            return $"Success: Configured emission module (rate={rateOverTime}, enabled={enabled != 0}).";
        }

        [AgentTool("Configure the Shape module of a ParticleSystem. shapeType: 0=Sphere, 1=Hemisphere, 2=Cone, 3=Box, 5=Circle, 6=Edge, 12=Rectangle.")]
        public static string ConfigureParticleShape(string goName, int shapeType = -1, float radius = -1f, float angle = -1f, int enabled = 1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var so = new SerializedObject(ps);

            so.FindProperty("ShapeModule.enabled").boolValue = enabled != 0;
            if (shapeType >= 0) so.FindProperty("ShapeModule.type").intValue = shapeType;
            if (radius >= 0) so.FindProperty("ShapeModule.radius.value").floatValue = radius;
            if (angle >= 0) so.FindProperty("ShapeModule.angle").floatValue = angle;

            so.ApplyModifiedProperties();
            return $"Success: Configured shape module on '{goName}'.";
        }

        [AgentTool("Configure the Renderer of a ParticleSystem. renderMode: 0=Billboard, 1=Stretch, 2=HorizontalBillboard, 3=VerticalBillboard, 4=Mesh. materialPath is an asset path.")]
        public static string ConfigureParticleRenderer(string goName, int renderMode = -1, string materialPath = "", int sortingOrder = -999)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return $"Error: No ParticleSystemRenderer on '{goName}'.";

            Undo.RecordObject(renderer, "Configure ParticleSystemRenderer");

            if (renderMode >= 0) renderer.renderMode = (ParticleSystemRenderMode)renderMode;
            if (sortingOrder > -999) renderer.sortingOrder = sortingOrder;

            if (!string.IsNullOrEmpty(materialPath))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (mat != null) renderer.sharedMaterial = mat;
                else return $"Warning: Configured renderer but material not found at '{materialPath}'.";
            }

            EditorUtility.SetDirty(renderer);
            return $"Success: Configured ParticleSystemRenderer on '{goName}'.";
        }

        [AgentTool("Add a burst to the Emission module. time is in seconds, count is number of particles.")]
        public static string AddParticleBurst(string goName, float time = 0f, int count = 10, int cycles = 1, float interval = 0.01f)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";

            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var emission = ps.emission;
            var burst = new ParticleSystem.Burst(time, (short)count, (short)count, cycles, interval);

            // Need to rebuild burst array
            int existingCount = emission.burstCount;
            var bursts = new ParticleSystem.Burst[existingCount + 1];
            for (int i = 0; i < existingCount; i++)
                bursts[i] = emission.GetBurst(i);
            bursts[existingCount] = burst;

            var so = new SerializedObject(ps);
            // Set burst count and data via emission module
            emission.SetBursts(bursts);
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(ps);
            return $"Success: Added burst at t={time}s, count={count}, cycles={cycles} to '{goName}'.";
        }

        // =================================================================
        // ColorOverLifetime Module
        // =================================================================

        [AgentTool(@"Configure the ColorOverLifetime module. Sets a gradient with color and alpha keys.
colorKeys: semicolon-separated 'time:r,g,b' (0-1 each). Example: '0:1,1,1;0.5:1,0,0;1:0,0,1'
alphaKeys: semicolon-separated 'time:alpha' (0-1). Example: '0:1;0.8:1;1:0'")]
        public static string ConfigureParticleColorOverLifetime(string goName, string colorKeys, string alphaKeys = "0:1;1:1", int enabled = 1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var gColorKeys = ParseGradientColorKeys(colorKeys);
            var gAlphaKeys = ParseGradientAlphaKeys(alphaKeys);
            if (gColorKeys == null) return "Error: Invalid colorKeys format. Use 'time:r,g,b;time:r,g,b'.";
            if (gAlphaKeys == null) return "Error: Invalid alphaKeys format. Use 'time:alpha;time:alpha'.";

            var gradient = new Gradient();
            gradient.SetKeys(gColorKeys, gAlphaKeys);

            var so = new SerializedObject(ps);
            so.FindProperty("ColorModule.enabled").boolValue = enabled != 0;

            // Set gradient mode
            var gradProp = so.FindProperty("ColorModule.gradient");
            if (gradProp != null)
            {
                gradProp.FindPropertyRelative("minMaxState").intValue = 1; // Gradient mode
                var maxGrad = gradProp.FindPropertyRelative("maxGradient");
                SetSerializedGradient(maxGrad, gradient);
            }

            so.ApplyModifiedProperties();
            return $"Success: Configured ColorOverLifetime ({gColorKeys.Length} color keys, {gAlphaKeys.Length} alpha keys).";
        }

        // =================================================================
        // SizeOverLifetime Module
        // =================================================================

        [AgentTool(@"Configure the SizeOverLifetime module with a curve.
curve: comma-separated 'time:value' pairs (value is multiplier, 1.0=original size). Example: '0:0, 0.2:1, 0.8:1, 1:0'
Or set a constant multiplier with a single value.")]
        public static string ConfigureParticleSizeOverLifetime(string goName, string curve, int enabled = 1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var so = new SerializedObject(ps);
            so.FindProperty("SizeModule.enabled").boolValue = enabled != 0;

            if (float.TryParse(curve.Trim(), out float constant))
            {
                SetMinMaxCurveConstant(so, "SizeModule.curve", constant);
            }
            else
            {
                var keys = ParseKeyframes(curve);
                if (keys == null) return "Error: Invalid curve format. Use 'time:value, time:value'.";
                SetMinMaxCurveCurve(so, "SizeModule.curve", keys);
            }

            so.ApplyModifiedProperties();
            return $"Success: Configured SizeOverLifetime on '{goName}'.";
        }

        // =================================================================
        // VelocityOverLifetime Module
        // =================================================================

        [AgentTool(@"Configure the VelocityOverLifetime module. x, y, z are constant velocity values.
space: 0=Local, 1=World. Use orbital/radial for curved motion.")]
        public static string ConfigureParticleVelocityOverLifetime(string goName, float x = 0f, float y = 0f, float z = 0f,
            float orbitalX = 0f, float orbitalY = 0f, float orbitalZ = 0f, float radial = 0f, int space = 0, int enabled = 1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var so = new SerializedObject(ps);
            so.FindProperty("VelocityModule.enabled").boolValue = enabled != 0;
            so.FindProperty("VelocityModule.inWorldSpace").boolValue = space != 0;

            SetMinMaxCurveConstant(so, "VelocityModule.x", x);
            SetMinMaxCurveConstant(so, "VelocityModule.y", y);
            SetMinMaxCurveConstant(so, "VelocityModule.z", z);
            SetMinMaxCurveConstant(so, "VelocityModule.orbitalX", orbitalX);
            SetMinMaxCurveConstant(so, "VelocityModule.orbitalY", orbitalY);
            SetMinMaxCurveConstant(so, "VelocityModule.orbitalZ", orbitalZ);
            SetMinMaxCurveConstant(so, "VelocityModule.radial", radial);

            so.ApplyModifiedProperties();
            return $"Success: Configured VelocityOverLifetime on '{goName}' (linear=[{x},{y},{z}], orbital=[{orbitalX},{orbitalY},{orbitalZ}], radial={radial}).";
        }

        // =================================================================
        // RotationOverLifetime Module
        // =================================================================

        [AgentTool("Configure the RotationOverLifetime module. angularVelocity is in degrees/second. Use separate axes for 3D rotation.")]
        public static string ConfigureParticleRotationOverLifetime(string goName, float angularVelocity = 0f,
            int separateAxes = 0, float x = 0f, float y = 0f, float z = 0f, int enabled = 1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var so = new SerializedObject(ps);
            so.FindProperty("RotationModule.enabled").boolValue = enabled != 0;
            so.FindProperty("RotationModule.separateAxes").boolValue = separateAxes != 0;

            if (separateAxes != 0)
            {
                SetMinMaxCurveConstant(so, "RotationModule.x", x * Mathf.Deg2Rad);
                SetMinMaxCurveConstant(so, "RotationModule.y", y * Mathf.Deg2Rad);
                SetMinMaxCurveConstant(so, "RotationModule.curve", z * Mathf.Deg2Rad);
            }
            else
            {
                SetMinMaxCurveConstant(so, "RotationModule.curve", angularVelocity * Mathf.Deg2Rad);
            }

            so.ApplyModifiedProperties();
            return $"Success: Configured RotationOverLifetime on '{goName}' ({(separateAxes != 0 ? $"xyz=[{x},{y},{z}]" : $"angular={angularVelocity}")}).";
        }

        // =================================================================
        // Noise Module
        // =================================================================

        [AgentTool("Configure the Noise module for turbulence effects. strength controls displacement, frequency controls pattern scale.")]
        public static string ConfigureParticleNoise(string goName, float strength = 1f, float frequency = 0.5f,
            float scrollSpeed = 0f, int damping = 1, int octaves = 1,
            float strengthX = -999f, float strengthY = -999f, float strengthZ = -999f,
            int separateAxes = 0, int enabled = 1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var so = new SerializedObject(ps);
            so.FindProperty("NoiseModule.enabled").boolValue = enabled != 0;
            so.FindProperty("NoiseModule.frequency").floatValue = frequency;
            so.FindProperty("NoiseModule.scrollSpeed").floatValue = scrollSpeed;
            so.FindProperty("NoiseModule.damping").boolValue = damping != 0;
            so.FindProperty("NoiseModule.octaveCount").intValue = Mathf.Clamp(octaves, 1, 4);
            so.FindProperty("NoiseModule.separateAxes").boolValue = separateAxes != 0;

            if (separateAxes != 0)
            {
                if (strengthX > -999f) SetMinMaxCurveConstant(so, "NoiseModule.strengthX", strengthX);
                if (strengthY > -999f) SetMinMaxCurveConstant(so, "NoiseModule.strengthY", strengthY);
                if (strengthZ > -999f) SetMinMaxCurveConstant(so, "NoiseModule.strengthZ", strengthZ);
            }
            else
            {
                SetMinMaxCurveConstant(so, "NoiseModule.strength", strength);
            }

            so.ApplyModifiedProperties();
            return $"Success: Configured Noise on '{goName}' (strength={strength}, freq={frequency}, octaves={octaves}).";
        }

        // =================================================================
        // Collision Module
        // =================================================================

        [AgentTool("Configure the Collision module. type: 0=Planes, 1=World. mode: 0=3D, 1=2D.")]
        public static string ConfigureParticleCollision(string goName, int type = 1, int mode = 0,
            float dampen = 0f, float bounce = 1f, float lifetimeLoss = 0f, float minKillSpeed = 0f,
            int sendCollisionMessages = 0, int enabled = 1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var so = new SerializedObject(ps);
            so.FindProperty("CollisionModule.enabled").boolValue = enabled != 0;
            so.FindProperty("CollisionModule.type").intValue = type;
            so.FindProperty("CollisionModule.collisionMode").intValue = mode;
            SetMinMaxCurveConstant(so, "CollisionModule.dampen", dampen);
            SetMinMaxCurveConstant(so, "CollisionModule.bounce", bounce);
            SetMinMaxCurveConstant(so, "CollisionModule.energyLossOnCollision", lifetimeLoss);
            so.FindProperty("CollisionModule.minKillSpeed").floatValue = minKillSpeed;
            so.FindProperty("CollisionModule.collisionMessages").boolValue = sendCollisionMessages != 0;

            so.ApplyModifiedProperties();
            return $"Success: Configured Collision on '{goName}' (type={(type == 0 ? "Planes" : "World")}, dampen={dampen}, bounce={bounce}).";
        }

        // =================================================================
        // Trails Module
        // =================================================================

        [AgentTool("Configure the Trails module for particle trail effects. ratio: fraction of particles with trails (0-1).")]
        public static string ConfigureParticleTrails(string goName, float ratio = 1f, float lifetime = 1f,
            float minVertexDistance = 0.2f, int worldSpace = 0, int dieWithParticles = 1,
            int textureMode = 0, float widthOverTrail = 1f, int enabled = 1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var so = new SerializedObject(ps);
            so.FindProperty("TrailModule.enabled").boolValue = enabled != 0;
            so.FindProperty("TrailModule.ratio").floatValue = ratio;
            so.FindProperty("TrailModule.minVertexDistance").floatValue = minVertexDistance;
            so.FindProperty("TrailModule.worldSpace").boolValue = worldSpace != 0;
            so.FindProperty("TrailModule.dieWithParticles").boolValue = dieWithParticles != 0;
            so.FindProperty("TrailModule.textureMode").intValue = textureMode;
            SetMinMaxCurveConstant(so, "TrailModule.lifetime", lifetime);
            SetMinMaxCurveConstant(so, "TrailModule.widthOverTrail", widthOverTrail);

            so.ApplyModifiedProperties();
            return $"Success: Configured Trails on '{goName}' (ratio={ratio}, lifetime={lifetime}).";
        }

        // =================================================================
        // TextureSheetAnimation Module
        // =================================================================

        [AgentTool("Configure the TextureSheetAnimation module for sprite sheet playback. mode: 0=Grid, 1=Sprites. animation: 0=WholeSheet, 1=SingleRow.")]
        public static string ConfigureParticleTextureSheet(string goName, int tilesX = 1, int tilesY = 1,
            int mode = 0, int animation = 0, int frameOverTime = -1, float cycleCount = 1f, int enabled = 1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var so = new SerializedObject(ps);
            so.FindProperty("UVModule.enabled").boolValue = enabled != 0;
            so.FindProperty("UVModule.mode").intValue = mode;
            so.FindProperty("UVModule.tilesX").intValue = tilesX;
            so.FindProperty("UVModule.tilesY").intValue = tilesY;
            so.FindProperty("UVModule.animationType").intValue = animation;
            so.FindProperty("UVModule.cycles").floatValue = cycleCount;

            so.ApplyModifiedProperties();
            return $"Success: Configured TextureSheetAnimation on '{goName}' (tiles={tilesX}x{tilesY}, animation={animation}).";
        }

        // =================================================================
        // ForceOverLifetime Module
        // =================================================================

        [AgentTool("Configure the ForceOverLifetime module. Applies constant force to particles. space: 0=Local, 1=World.")]
        public static string ConfigureParticleForceOverLifetime(string goName, float x = 0f, float y = 0f, float z = 0f,
            int space = 0, int enabled = 1)
        {
            var go = FindGO(goName);
            if (go == null) return $"Error: GameObject '{goName}' not found.";
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return $"Error: No ParticleSystem on '{goName}'.";

            var so = new SerializedObject(ps);
            so.FindProperty("ForceModule.enabled").boolValue = enabled != 0;
            so.FindProperty("ForceModule.inWorldSpace").boolValue = space != 0;
            SetMinMaxCurveConstant(so, "ForceModule.x", x);
            SetMinMaxCurveConstant(so, "ForceModule.y", y);
            SetMinMaxCurveConstant(so, "ForceModule.z", z);

            so.ApplyModifiedProperties();
            return $"Success: Configured ForceOverLifetime on '{goName}' (force=[{x},{y},{z}], space={((space == 0) ? "Local" : "World")}).";
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static GradientColorKey[] ParseGradientColorKeys(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            var entries = input.Split(';');
            var keys = new System.Collections.Generic.List<GradientColorKey>();

            foreach (var entry in entries)
            {
                string trimmed = entry.Trim();
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0) return null;

                if (!float.TryParse(trimmed.Substring(0, colonIdx).Trim(),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float time))
                    return null;

                var rgb = trimmed.Substring(colonIdx + 1).Split(',');
                if (rgb.Length != 3) return null;
                if (!float.TryParse(rgb[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r) ||
                    !float.TryParse(rgb[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float g) ||
                    !float.TryParse(rgb[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float b))
                    return null;

                keys.Add(new GradientColorKey(new Color(r, g, b), time));
            }
            return keys.Count > 0 ? keys.ToArray() : null;
        }

        private static GradientAlphaKey[] ParseGradientAlphaKeys(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            var entries = input.Split(';');
            var keys = new System.Collections.Generic.List<GradientAlphaKey>();

            foreach (var entry in entries)
            {
                string trimmed = entry.Trim();
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0) return null;

                if (!float.TryParse(trimmed.Substring(0, colonIdx).Trim(),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float time) ||
                    !float.TryParse(trimmed.Substring(colonIdx + 1).Trim(),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float alpha))
                    return null;

                keys.Add(new GradientAlphaKey(alpha, time));
            }
            return keys.Count > 0 ? keys.ToArray() : null;
        }

        private static Keyframe[] ParseKeyframes(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            var parts = input.Split(',');
            var keys = new System.Collections.Generic.List<Keyframe>();

            foreach (var part in parts)
            {
                string trimmed = part.Trim();
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0) return null;

                if (!float.TryParse(trimmed.Substring(0, colonIdx).Trim(),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float time) ||
                    !float.TryParse(trimmed.Substring(colonIdx + 1).Trim(),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
                    return null;

                keys.Add(new Keyframe(time, value));
            }
            return keys.Count > 0 ? keys.ToArray() : null;
        }

        private static void SetSerializedGradient(SerializedProperty gradProp, Gradient gradient)
        {
            if (gradProp == null) return;

            // Color keys
            var colorKeys = gradProp.FindPropertyRelative("key0");
            if (colorKeys != null && gradient.colorKeys.Length > 0)
            {
                for (int i = 0; i < Mathf.Min(gradient.colorKeys.Length, 8); i++)
                {
                    var keyProp = gradProp.FindPropertyRelative($"key{i}");
                    if (keyProp != null) keyProp.colorValue = gradient.colorKeys[Mathf.Min(i, gradient.colorKeys.Length - 1)].color;

                    var ctimeProp = gradProp.FindPropertyRelative($"ctime{i}");
                    if (ctimeProp != null) ctimeProp.intValue = (int)(gradient.colorKeys[Mathf.Min(i, gradient.colorKeys.Length - 1)].time * 65535);
                }
            }

            // Alpha keys
            for (int i = 0; i < Mathf.Min(gradient.alphaKeys.Length, 8); i++)
            {
                var atimeProp = gradProp.FindPropertyRelative($"atime{i}");
                if (atimeProp != null) atimeProp.intValue = (int)(gradient.alphaKeys[i].time * 65535);
            }

            var numColorKeys = gradProp.FindPropertyRelative("m_NumColorKeys");
            if (numColorKeys != null) numColorKeys.intValue = gradient.colorKeys.Length;
            var numAlphaKeys = gradProp.FindPropertyRelative("m_NumAlphaKeys");
            if (numAlphaKeys != null) numAlphaKeys.intValue = gradient.alphaKeys.Length;
        }

        private static void SetMinMaxCurveCurve(SerializedObject so, string path, Keyframe[] keys)
        {
            var mode = so.FindProperty(path + ".minMaxState");
            if (mode != null) mode.intValue = 1; // Curve mode

            var curveProp = so.FindProperty(path + ".maxCurve");
            if (curveProp != null) curveProp.animationCurveValue = new AnimationCurve(keys);

            var scalar = so.FindProperty(path + ".scalar");
            if (scalar != null) scalar.floatValue = 1f;
        }

        private static string FormatMinMaxCurve(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return $"{curve.constant:F2}";
                case ParticleSystemCurveMode.TwoConstants:
                    return $"[{curve.constantMin:F2} ~ {curve.constantMax:F2}]";
                default:
                    return $"({curve.mode})";
            }
        }

        private static void SetMinMaxCurveConstant(SerializedObject so, string path, float value)
        {
            var scalar = so.FindProperty(path + ".scalar");
            if (scalar != null) scalar.floatValue = value;
            var mode = so.FindProperty(path + ".minMaxState");
            if (mode != null) mode.intValue = 0; // Constant mode
        }
    }
}

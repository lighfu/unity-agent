using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Numeric image comparison, and the A/B render loop it exists to serve.
    ///
    /// The manual version of this — spin up a temp camera, CopyFrom(SceneView.camera), render to a
    /// RenderTexture, encode a PNG, then diff the two files with an external image library — costs
    /// about ten tool calls per comparison. Flipping one material property and asking "did anything
    /// change, and by how much" is the single most repeated verification in shader work, so it gets
    /// to be one call.
    /// </summary>
    public static class ImageDiffTools
    {
        [AgentTool(@"Compare two image files numerically. Reports differing pixel count, percentage,
max channel difference and mean channel difference, and can write a visualized diff image.

pathA / pathB: absolute or project-relative paths to PNG/JPG files.
threshold: per-channel difference (0-255) at or below which a pixel counts as identical (default 0).
diffOutputPath: optional path for a grayscale diff image (black = identical, white = max difference).
compareAlpha: include the alpha channel in the comparison (default false — RGB only).
maskRegion: 'x,y,w,h' in pixels to restrict the comparison to one rectangle, origin BOTTOM-LEFT
  (Unity's convention). Use it to ignore parts of the frame that are expected to move — a gizmo,
  an overlay, a second object — so the number reported is about the thing you are actually testing.

Always reports magentaPixels for each image: Unity draws a missing or failed-to-compile shader in
magenta, so a nonzero count means the render is broken and any diff computed from it is
meaningless. Check that before believing the percentages.

Sizes must match exactly; a mismatch is an error rather than a guess at how to align them.",
            Category = "ImageDiff", Risk = ToolRisk.Caution)]
        public static string DiffImages(
            string pathA,
            string pathB,
            int threshold = 0,
            string diffOutputPath = "",
            bool compareAlpha = false,
            string maskRegion = "")
        {
            if (string.IsNullOrWhiteSpace(pathA) || string.IsNullOrWhiteSpace(pathB))
                return "Error: pathA and pathB are both required.";

            Texture2D a = null, b = null;
            try
            {
                a = LoadImage(pathA, out string errA);
                if (a == null) return $"Error: {errA}";
                b = LoadImage(pathB, out string errB);
                if (b == null) return $"Error: {errB}";

                if (a.width != b.width || a.height != b.height)
                    return $"Error: size mismatch — A is {a.width}x{a.height}, B is {b.width}x{b.height}. " +
                           "Re-render both at the same resolution; this tool will not rescale for you.";

                return CompareTextures(a, b, threshold, diffOutputPath, compareAlpha,
                    $"A: {pathA}\nB: {pathB}", maskRegion);
            }
            finally
            {
                if (a != null) UnityEngine.Object.DestroyImmediate(a);
                if (b != null) UnityEngine.Object.DestroyImmediate(b);
            }
        }

        [AgentTool(@"Render the SceneView twice with one material property set to two different values,
then diff the results. Collapses the standard 'does this property actually do anything, and where'
check into a single call.

materialPath: asset path to the .mat.
property: shader property name (e.g. '_NRSShadowStrength').
valueA / valueB: values to set. Format follows the property's declared type:
  Float/Range -> '0.05'   Int -> '2'   Color -> '#RRGGBB' or 'r,g,b[,a]'   Vector -> 'x,y,z,w'
width / height: render resolution (default 1024).
outputDir: where to write a.png / b.png / diff.png (default: system temp).
threshold: per-channel difference tolerated before a pixel counts as changed (default 0).

Frame the subject in the SceneView first — this renders through the SceneView camera.
The material's ENTIRE serialized state is snapshotted and restored afterwards, including on error,
so a property that had no serialized entry does not gain one.
The property name is resolved to the shader's own spelling, and Integer-declared properties are
written with SetInteger — Unity silently ignores SetFloat on those.",
            Category = "ImageDiff", Risk = ToolRisk.Caution)]
        public static string RenderMaterialAB(
            string materialPath,
            string property,
            string valueA,
            string valueB,
            int width = 1024,
            int height = 1024,
            string outputDir = "",
            int threshold = 0)
        {
            if (string.IsNullOrWhiteSpace(property))
                return "Error: property is required.";
            if (width <= 0 || height <= 0)
                return "Error: width and height must be positive.";

            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null) return $"Error: Material not found at '{materialPath}'.";

            var shader = mat.shader;
            if (shader == null) return $"Error: Material '{mat.name}' has no shader.";

            int propIndex = FindPropertyIndex(shader, property);
            if (propIndex < 0)
            {
                var names = Enumerable.Range(0, shader.GetPropertyCount())
                    .Select(i => shader.GetPropertyName(i))
                    .Where(n => n.IndexOf(property, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Take(10).ToArray();
                string hint = names.Length > 0
                    ? $" Similar: {string.Join(", ", names)}"
                    : " Use InspectMaterial or DumpMaterial to list properties.";
                return $"Error: Shader '{shader.name}' has no property '{property}'.{hint}";
            }

            // Use the shader's own spelling from here on. FindPropertyIndex falls back to a
            // case-insensitive match, but Material.Get*/Set* are case-SENSITIVE: writing with the
            // caller's casing would create an orphan property the shader never reads, both renders
            // would be identical, and the tool would confidently answer "this property does
            // nothing" — the exact wrong answer it exists to prevent.
            string resolvedProperty = shader.GetPropertyName(propIndex);
            if (!string.Equals(resolvedProperty, property, StringComparison.Ordinal))
                property = resolvedProperty;

            var propType = shader.GetPropertyType(propIndex);
            if (!TryParseValue(propType, valueA, out object parsedA, out string parseErrA))
                return $"Error: valueA — {parseErrA}";
            if (!TryParseValue(propType, valueB, out object parsedB, out string parseErrB))
                return $"Error: valueB — {parseErrB}";

            string dir = string.IsNullOrWhiteSpace(outputDir) ? Path.GetTempPath() : outputDir;
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                return $"Error: cannot create outputDir '{dir}': {ex.Message}";
            }

            string safeProp = property.TrimStart('_');
            string pathA = Path.Combine(dir, $"ab-{safeProp}-A.png");
            string pathB = Path.Combine(dir, $"ab-{safeProp}-B.png");
            string pathDiff = Path.Combine(dir, $"ab-{safeProp}-diff.png");

            // Snapshot the whole serialized material rather than just this property's value.
            // Writing the value back would CREATE a serialized entry for a property that had none,
            // turning an "AT DEFAULT" property into an explicitly-stored one and destroying the
            // very signal DumpMaterial reports. Overwriting from JSON restores the exact prior
            // serialization, entries included.
            string originalJson = EditorJsonUtility.ToJson(mat);
            Texture2D texA = null, texB = null;
            try
            {
                WriteValue(mat, propType, property, parsedA);
                texA = SceneViewTools.RenderSceneViewToTexture(width, height, out string errA);
                if (texA == null) return $"Error: {errA}";

                WriteValue(mat, propType, property, parsedB);
                texB = SceneViewTools.RenderSceneViewToTexture(width, height, out string errB);
                if (texB == null) return $"Error: {errB}";

                if (!TryWritePng(texA, pathA, out string saveErrA)) return $"Error: {saveErrA}";
                if (!TryWritePng(texB, pathB, out string saveErrB)) return $"Error: {saveErrB}";

                string header =
                    $"Material: {mat.name}  ({materialPath})\n" +
                    $"Property: {property} [{propType}]  A={valueA}  B={valueB}\n" +
                    $"A: {pathA}\nB: {pathB}";

                return CompareTextures(texA, texB, threshold, pathDiff, compareAlpha: false, header: header);
            }
            finally
            {
                // Restore before anything else can observe the material — including on exception.
                try { EditorJsonUtility.FromJsonOverwrite(originalJson, mat); }
                catch (Exception ex)
                {
                    Debug.LogError($"[UnityAgent] RenderMaterialAB failed to restore '{mat.name}' after testing {property}: {ex.Message}");
                }
                if (texA != null) UnityEngine.Object.DestroyImmediate(texA);
                if (texB != null) UnityEngine.Object.DestroyImmediate(texB);
            }
        }

        // ── comparison core ──────────────────────────────────────────────────

        private static string CompareTextures(
            Texture2D a, Texture2D b, int threshold, string diffOutputPath, bool compareAlpha, string header,
            string maskRegion = "")
        {
            if (threshold < 0) threshold = 0;

            int width = a.width, height = a.height;
            if (!TryParseMask(maskRegion, width, height, out int mx, out int my, out int mw, out int mh, out string maskErr))
                return $"Error: {maskErr}";
            bool masked = mw != width || mh != height || mx != 0 || my != 0;

            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            int count = Math.Min(pa.Length, pb.Length);

            long differing = 0;
            long compared = 0;
            long magentaA = 0, magentaB = 0;
            int maxDiff = 0;
            double sumDiff = 0;
            int channels = compareAlpha ? 4 : 3;

            // Sized to the full image even when masked: the diff image stays aligned with the
            // source, so a masked-out region reads as "no difference" rather than shifting
            // everything left and making the picture lie about where the change is.
            byte[] diffMap = string.IsNullOrWhiteSpace(diffOutputPath) ? null : new byte[count];

            for (int i = 0; i < count; i++)
            {
                if (masked)
                {
                    // GetPixels32 is row-major from the bottom-left, which is also how maskRegion
                    // is documented, so no flip is needed here.
                    int x = i % width;
                    int y = i / width;
                    if (x < mx || x >= mx + mw || y < my || y >= my + mh) continue;
                }

                compared++;
                if (IsErrorMagenta(pa[i])) magentaA++;
                if (IsErrorMagenta(pb[i])) magentaB++;

                int dr = Math.Abs(pa[i].r - pb[i].r);
                int dg = Math.Abs(pa[i].g - pb[i].g);
                int db = Math.Abs(pa[i].b - pb[i].b);
                int da = compareAlpha ? Math.Abs(pa[i].a - pb[i].a) : 0;

                int pixelMax = Math.Max(Math.Max(dr, dg), Math.Max(db, da));
                if (pixelMax > maxDiff) maxDiff = pixelMax;
                sumDiff += (dr + dg + db + da) / (double)channels;
                if (pixelMax > threshold) differing++;

                if (diffMap != null) diffMap[i] = (byte)pixelMax;
            }

            double pct = compared == 0 ? 0 : differing * 100.0 / compared;
            double mean = compared == 0 ? 0 : sumDiff / compared;
            var ic = CultureInfo.InvariantCulture;

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(header)) sb.AppendLine(header);
            sb.AppendLine($"Size: {width}x{height} ({count} pixels)  channels={(compareAlpha ? "RGBA" : "RGB")}  threshold={threshold}");
            if (masked)
                sb.AppendLine($"maskRegion: x={mx} y={my} w={mw} h={mh} (bottom-left origin) — {compared:N0} pixels compared");
            sb.AppendLine($"Differing pixels: {differing:N0} / {compared:N0} ({pct.ToString("F3", ic)}%)");
            sb.AppendLine($"Max channel diff: {maxDiff}");
            sb.AppendLine($"Mean channel diff: {mean.ToString("F4", ic)}");
            sb.AppendLine($"magentaPixels: A={magentaA:N0}  B={magentaB:N0}");
            if (magentaA > 0 || magentaB > 0)
                sb.AppendLine("  WARNING: magenta present — a shader is missing or failed to compile. " +
                              "The numbers above describe a broken render, not a material change.");

            if (diffMap != null)
            {
                if (TryWriteDiffImage(diffMap, a.width, a.height, diffOutputPath, out string diffErr))
                    sb.AppendLine($"Diff image: {diffOutputPath}");
                else
                    sb.AppendLine($"Diff image: FAILED ({diffErr})");
            }

            sb.Append(differing == 0
                ? "IDENTICAL — no pixel differs beyond the threshold."
                : maxDiff <= threshold
                    ? "Within threshold."
                    : "DIFFERENT.");
            return sb.ToString();
        }

        [AgentTool(@"Render a mask showing exactly which pixels a given material draws: white where it
is visible, black everywhere else.

Removes the guesswork from ""is this the material responsible for what I am looking at"". Reading
a colour at a hand-picked coordinate and reasoning about which material owns it is how a wrong
material gets blamed — a mask answers it directly, and reports the coverage as a number.

materialPath: asset path to the .mat to isolate.
width / height: render resolution (default 1024).
outputPath: where to write the mask PNG (default: system temp). The image is also attached.
pivot / rotation / orthoSize: pin the framing, same meaning and same all-or-nothing rule as
  CaptureSceneView. Use the SAME values you used for the capture you are trying to explain,
  otherwise the mask describes a different view than the picture you are holding.

How it works: every renderer in the scene is temporarily reassigned to a flat unlit material —
white for slots holding the target, black for everything else — then the SceneView camera is
rendered and the original assignments are put back, including if anything throws.

Two consequences worth knowing. The scene's dirty flag may end up set even though the contents are
byte-identical afterwards. And because the swap is per material SLOT, a renderer that uses the
target in one slot and something else in another shows only the target's own triangles, which is
the honest answer rather than the whole mesh.",
            Category = "ImageDiff", Risk = ToolRisk.Caution)]
        public static string RenderMaterialMask(
            string materialPath,
            int width = 1024,
            int height = 1024,
            string outputPath = "",
            string pivot = "",
            string rotation = "",
            float orthoSize = 0f)
        {
            if (string.IsNullOrWhiteSpace(materialPath))
                return "Error: materialPath is required.";

            var target = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (target == null) return $"Error: Material not found at '{materialPath}'.";

            if (!SceneViewTools.TryResolvePose(pivot, rotation, orthoSize, out bool overridePose,
                                               out Vector3 pivotV, out Quaternion rotationQ, out string poseError))
                return $"Error: {poseError}";

            var unlit = Shader.Find("Unlit/Color");
            if (unlit == null)
                return "Error: the built-in 'Unlit/Color' shader is not available in this project, " +
                       "so the mask cannot be drawn. Add it to Always Included Shaders or install the built-in shaders.";

            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            if (renderers.Length == 0)
                return "Error: no renderers in the active scene.";

            Material white = null, black = null;
            Texture2D tex = null;
            var saved = new List<KeyValuePair<Renderer, Material[]>>(renderers.Length);
            int matchedRenderers = 0, matchedSlots = 0;

            try
            {
                white = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
                white.SetColor("_Color", Color.white);
                black = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
                black.SetColor("_Color", Color.black);

                foreach (var r in renderers)
                {
                    var originals = r.sharedMaterials;
                    saved.Add(new KeyValuePair<Renderer, Material[]>(r, originals));

                    var replacement = new Material[originals.Length];
                    bool hit = false;
                    for (int i = 0; i < originals.Length; i++)
                    {
                        bool isTarget = originals[i] == target;
                        if (isTarget) { hit = true; matchedSlots++; }
                        replacement[i] = isTarget ? white : black;
                    }
                    if (hit) matchedRenderers++;
                    r.sharedMaterials = replacement;
                }

                tex = SceneViewTools.RenderSceneViewToTexture(width, height, out string renderError,
                                                              overridePose, pivotV, rotationQ, orthoSize);
                if (tex == null) return $"Error: {renderError}";
            }
            finally
            {
                foreach (var kv in saved)
                    if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
                if (white != null) UnityEngine.Object.DestroyImmediate(white);
                if (black != null) UnityEngine.Object.DestroyImmediate(black);
            }

            try
            {
                long lit = 0;
                var pixels = tex.GetPixels32();
                foreach (var p in pixels)
                    if (p.r > 127) lit++;   // flat white against flat black — a single channel decides it

                byte[] png = tex.EncodeToPNG();
                if (png == null || png.Length == 0) return "Error: failed to encode the mask image.";

                string path = string.IsNullOrWhiteSpace(outputPath)
                    ? Path.Combine(Path.GetTempPath(), $"materialmask-{SanitizeFileName(target.name)}.png")
                    : outputPath;
                string saveMsg;
                if (SceneViewTools.TrySaveToPath(png, path, out string saveErr))
                    saveMsg = saveErr != null ? $"Mask image: FAILED to save ({saveErr})" : $"Mask image: {path}";
                else
                    saveMsg = "Mask image: not saved";

                SceneViewTools.SetPendingImage(png, "image/png");

                double pct = pixels.Length == 0 ? 0 : lit * 100.0 / pixels.Length;
                var sb = new StringBuilder();
                sb.AppendLine($"Material: {target.name}  ({materialPath})");
                sb.AppendLine($"Renderers using it: {matchedRenderers} / {renderers.Length}  (material slots: {matchedSlots})");
                sb.AppendLine($"Visible pixels: {lit:N0} / {pixels.Length:N0} ({pct.ToString("F3", CultureInfo.InvariantCulture)}%)");
                sb.AppendLine(saveMsg);
                if (matchedSlots == 0)
                    sb.AppendLine("WARNING: no renderer in the scene references this material, so the mask is entirely black. " +
                                  "Check that you passed the asset the scene actually uses — a duplicated or variant material looks identical by name.");
                else if (lit == 0)
                    sb.AppendLine("WARNING: the material is assigned but draws nothing in this view — occluded, off-camera, " +
                                  "or on a disabled renderer. The framing, not the material, is what to check first.");
                sb.Append("The mask has been attached for your review.");
                return sb.ToString();
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static string SanitizeFileName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "material" : sb.ToString();
        }

        /// <summary>
        /// Unity's "shader missing / failed to compile" fill. The exact value is (255,0,255), but
        /// it arrives here through a render, an encode and possibly a JPEG, so the test is a
        /// neighbourhood rather than equality. Kept tight enough that ordinary saturated pinks in
        /// artwork do not register — a real error fill is a flat expanse, so a handful of stray
        /// matches would not change the verdict anyway.
        /// </summary>
        private static bool IsErrorMagenta(Color32 c)
            => c.r > 200 && c.b > 200 && c.g < 60;

        private static bool TryParseMask(string maskRegion, int width, int height,
                                         out int x, out int y, out int w, out int h, out string error)
        {
            x = 0; y = 0; w = width; h = height; error = null;
            if (string.IsNullOrWhiteSpace(maskRegion)) return true;

            var parts = maskRegion.Split(',');
            if (parts.Length != 4)
            {
                error = $"maskRegion '{maskRegion}' must be 'x,y,w,h' in pixels.";
                return false;
            }
            if (!int.TryParse(parts[0].Trim(), out x) || !int.TryParse(parts[1].Trim(), out y) ||
                !int.TryParse(parts[2].Trim(), out w) || !int.TryParse(parts[3].Trim(), out h))
            {
                error = $"maskRegion '{maskRegion}' must be four integers.";
                return false;
            }
            if (w <= 0 || h <= 0)
            {
                error = $"maskRegion width and height must be positive (got {w}x{h}).";
                return false;
            }
            // Clamping instead of erroring would silently compare a different area than asked for,
            // and the caller would read the resulting percentage as if it covered their rectangle.
            if (x < 0 || y < 0 || x + w > width || y + h > height)
            {
                error = $"maskRegion x={x} y={y} w={w} h={h} does not fit inside the {width}x{height} image.";
                return false;
            }
            return true;
        }

        private static Texture2D LoadImage(string path, out string error)
        {
            error = null;
            string full = ResolvePath(path);
            if (!File.Exists(full))
            {
                error = $"file not found: '{path}' (resolved to '{full}')";
                return null;
            }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(full); }
            catch (Exception ex) { error = $"cannot read '{path}': {ex.Message}"; return null; }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.DestroyImmediate(tex);
                error = $"'{path}' is not a decodable PNG/JPG";
                return null;
            }
            return tex;
        }

        /// <summary>Absolute paths pass through; anything else resolves against the project root.</summary>
        private static string ResolvePath(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.GetFullPath(Path.Combine(projectRoot, path));
        }

        private static bool TryWritePng(Texture2D tex, string path, out string error)
        {
            error = null;
            try
            {
                byte[] png = tex.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    error = $"failed to encode PNG for '{path}'";
                    return false;
                }
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, png);
                return true;
            }
            catch (Exception ex)
            {
                error = $"cannot write '{path}': {ex.Message}";
                return false;
            }
        }

        private static bool TryWriteDiffImage(byte[] diffMap, int width, int height, string path, out string error)
        {
            Texture2D tex = null;
            try
            {
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                var pixels = new Color32[diffMap.Length];
                for (int i = 0; i < diffMap.Length; i++)
                {
                    byte v = diffMap[i];
                    pixels[i] = new Color32(v, v, v, 255);
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                return TryWritePng(tex, ResolvePath(path), out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // ── material property plumbing ───────────────────────────────────────

        private static int FindPropertyIndex(Shader shader, string propertyName)
        {
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
                if (string.Equals(shader.GetPropertyName(i), propertyName, StringComparison.Ordinal))
                    return i;
            // Second pass: case-insensitive, so a casing slip produces a render rather than an error.
            for (int i = 0; i < count; i++)
                if (string.Equals(shader.GetPropertyName(i), propertyName, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static object ReadValue(Material mat, UnityEngine.Rendering.ShaderPropertyType type, string name)
        {
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color: return mat.GetColor(name);
                case UnityEngine.Rendering.ShaderPropertyType.Vector: return mat.GetVector(name);
#if UNITY_2021_1_OR_NEWER
                case UnityEngine.Rendering.ShaderPropertyType.Int: return mat.GetInteger(name);
#endif
                default: return mat.GetFloat(name);
            }
        }

        private static void WriteValue(Material mat, UnityEngine.Rendering.ShaderPropertyType type, string name, object value)
        {
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    mat.SetColor(name, (Color)value); break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    mat.SetVector(name, (Vector4)value); break;
#if UNITY_2021_1_OR_NEWER
                case UnityEngine.Rendering.ShaderPropertyType.Int:
                    // SetFloat here would be silently dropped on save — the exact bug this toolset
                    // is meant to catch, so the tool must not commit it itself.
                    mat.SetInteger(name, Convert.ToInt32(value, CultureInfo.InvariantCulture)); break;
#endif
                default:
                    mat.SetFloat(name, Convert.ToSingle(value, CultureInfo.InvariantCulture)); break;
            }
        }

        private static bool TryParseValue(
            UnityEngine.Rendering.ShaderPropertyType type, string raw, out object value, out string error)
        {
            value = null;
            error = null;
            string s = (raw ?? "").Trim();
            var ic = CultureInfo.InvariantCulture;

            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                {
                    if (!TryParseColor(s, out Color c))
                    {
                        error = $"'{raw}' is not a color. Use '#RRGGBB', '#RRGGBBAA' or 'r,g,b[,a]' with 0-1 floats.";
                        return false;
                    }
                    value = c;
                    return true;
                }
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                {
                    var parts = s.Split(',');
                    if (parts.Length < 2 || parts.Length > 4)
                    {
                        error = $"'{raw}' is not a vector. Use 'x,y' .. 'x,y,z,w'.";
                        return false;
                    }
                    var v = Vector4.zero;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, ic, out float f))
                        {
                            error = $"'{parts[i].Trim()}' in '{raw}' is not a number.";
                            return false;
                        }
                        v[i] = f;
                    }
                    value = v;
                    return true;
                }
#if UNITY_2021_1_OR_NEWER
                case UnityEngine.Rendering.ShaderPropertyType.Int:
                {
                    if (!int.TryParse(s, NumberStyles.Integer, ic, out int i))
                    {
                        error = $"'{raw}' is not an integer (this property is declared Int).";
                        return false;
                    }
                    value = i;
                    return true;
                }
#endif
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    error = "texture properties are not supported by RenderMaterialAB.";
                    return false;
                default:
                {
                    if (!float.TryParse(s, NumberStyles.Float, ic, out float f))
                    {
                        error = $"'{raw}' is not a number.";
                        return false;
                    }
                    value = f;
                    return true;
                }
            }
        }

        private static bool TryParseColor(string input, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(input)) return false;
            if (input.StartsWith("#")) return ColorUtility.TryParseHtmlString(input, out color);

            var parts = input.Split(',');
            if (parts.Length < 3) return false;
            var ic = CultureInfo.InvariantCulture;
            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, ic, out float r)) return false;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, ic, out float g)) return false;
            if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, ic, out float b)) return false;
            float alpha = 1f;
            if (parts.Length > 3) float.TryParse(parts[3].Trim(), NumberStyles.Float, ic, out alpha);
            color = new Color(r, g, b, alpha);
            return true;
        }
    }
}

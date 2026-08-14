using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Real shader-variant compilation, via the public <c>ShaderData.Pass.CompileVariant</c> API.
    ///
    /// Why this exists: <c>ShaderUtil.GetShaderMessages</c> / <c>ShaderHasError</c> only report
    /// what the editor happened to compile already. A variant that no material in the project
    /// currently requests is never compiled, so those APIs return zero errors for a shader that
    /// is genuinely broken — the failure surfaces later as a magenta object in-game. Compiling
    /// the exact variant on demand turns that into an immediate, specific error message.
    ///
    /// Bytecode size is reported for every pass because it is the strongest available evidence
    /// that a keyword actually reached the intended pass: flipping one keyword should change the
    /// size of exactly the passes that consume it, and leave the others byte-identical.
    /// </summary>
    public static class ShaderVariantTools
    {
        // CompileVariant spawns the real shader compiler. Passes are cheap individually but a
        // large uber-shader has dozens; cap the default so a single call cannot blow the MCP
        // request timeout with no output at all.
        private const int DefaultMaxPasses = 16;

        [AgentTool(@"Compile specific shader variants for real and report per-pass success, errors and BYTECODE SIZE.
Use this instead of ShaderUtil.GetShaderMessages / ShaderHasError when verifying a shader edit:
those only report variants the editor already happened to compile, so they return 'no errors'
for a shader that is actually broken (the failure then shows up as a magenta material in-game).

shaderPath: asset path ('Assets/Foo.shader') or shader name ('Sunao Shader/Standard').
keywords: ';' separated shader keywords to enable (e.g. 'SPOT;SHADOWS_DEPTH;SHADOWS_NATIVE'). Empty = no keywords.
passes: ';' separated pass names or 0-based indices. Empty = all passes (capped by maxPasses).
platform: d3d11 (default) | vulkan | metal | glcore | gles3 | switch | ps4 | gamecore.
shaderType: fragment (default) | vertex | geometry | hull | domain.
subshaderIndex: -1 (default) uses the active subshader.
maxPasses: safety cap on how many passes to compile (default 16).

BYTECODE SIZE IS THE POINT: compile the same passes with and without a keyword and diff the sizes.
If only the intended pass grew, the keyword reached only that pass — stronger evidence than
counting pixels in a screenshot.",
            Category = "ShaderVariant", Risk = ToolRisk.Safe)]
        public static string CompileShaderVariants(
            string shaderPath,
            string keywords = "",
            string passes = "",
            string platform = "d3d11",
            string shaderType = "fragment",
            int subshaderIndex = -1,
            int maxPasses = DefaultMaxPasses)
        {
            if (!TryResolveShader(shaderPath, out Shader shader, out string shaderErr))
                return shaderErr;
            if (!TryParsePlatform(platform, out ShaderCompilerPlatform compilerPlatform, out string platErr))
                return platErr;
            if (!TryParseShaderType(shaderType, out ShaderType stage, out string stageErr))
                return stageErr;

            string[] kw = ParseKeywords(keywords);
            if (maxPasses <= 0) maxPasses = DefaultMaxPasses;

            ShaderData data;
            try { data = ShaderUtil.GetShaderData(shader); }
            catch (Exception ex) { return $"Error: ShaderUtil.GetShaderData failed for '{shader.name}': {ex.Message}"; }
            if (data == null) return $"Error: No ShaderData for '{shader.name}'.";

            if (!TryResolveSubshader(data, subshaderIndex, out ShaderData.Subshader subshader, out int usedSubshader, out string subErr))
                return subErr;

            var selected = ResolvePasses(subshader, passes, out string passErr);
            if (passErr != null) return passErr;
            if (selected.Count == 0) return $"Error: Subshader {usedSubshader} of '{shader.name}' has no passes.";

            bool capped = selected.Count > maxPasses;
            if (capped) selected = selected.Take(maxPasses).ToList();

            var target = EditorUserBuildSettings.activeBuildTarget;

            var sb = new StringBuilder();
            sb.AppendLine($"Shader: {shader.name}  (subshader {usedSubshader}, {subshader.PassCount} passes)");
            sb.AppendLine($"Keywords: {(kw.Length == 0 ? "(none)" : string.Join(";", kw))}");
            sb.AppendLine($"Platform: {compilerPlatform}  Stage: {stage}  BuildTarget: {target}");
            sb.AppendLine("---");

            int failed = 0;
            long totalBytes = 0;
            foreach (var (index, pass, name) in selected)
            {
                if (!pass.HasShaderStage(stage))
                {
                    sb.AppendLine($"[{index}] {name,-16} SKIP  (no {stage} stage in this pass)");
                    continue;
                }

                ShaderData.VariantCompileInfo info;
                try
                {
                    info = pass.CompileVariant(stage, kw, compilerPlatform, target);
                }
                catch (Exception ex)
                {
                    failed++;
                    sb.AppendLine($"[{index}] {name,-16} ERROR (CompileVariant threw: {ex.Message})");
                    continue;
                }

                int size = info.ShaderData?.Length ?? 0;
                if (info.Success)
                {
                    totalBytes += size;
                    sb.AppendLine($"[{index}] {name,-16} OK    {size} bytes");
                    AppendMessages(sb, info.Messages, onlyWarnings: true);
                }
                else
                {
                    failed++;
                    sb.AppendLine($"[{index}] {name,-16} FAIL");
                    AppendMessages(sb, info.Messages, onlyWarnings: false);
                }
            }

            sb.AppendLine("---");
            sb.Append(failed == 0
                ? $"All {selected.Count} compiled pass(es) succeeded. Total bytecode: {totalBytes} bytes."
                : $"{failed} of {selected.Count} pass(es) FAILED.");
            if (capped)
                sb.Append($"  NOTE: only the first {maxPasses} passes were compiled; raise maxPasses to cover the rest.");

            return sb.ToString();
        }

        [AgentTool(@"Return the PREPROCESSED HLSL for one shader variant, so you can verify that an
injected code block actually reached the variant. Compiling successfully is not proof: code that
was never included compiles fine and simply does nothing.

shaderPath / keywords / platform / shaderType: same as CompileShaderVariants.
pass: pass name or 0-based index (default '0').
matchPattern: .NET regex. Only matching lines (plus contextLines around each) are returned.
  Empty = return the first maxLines lines plus the total line count.
contextLines: lines of context around each match (default 3).
maxLines: cap on returned lines (default 200) — preprocessed uber-shaders are enormous.",
            Category = "ShaderVariant", Risk = ToolRisk.Safe)]
        public static string PreprocessShaderVariant(
            string shaderPath,
            string keywords = "",
            string pass = "0",
            string platform = "d3d11",
            string shaderType = "fragment",
            string matchPattern = "",
            int contextLines = 3,
            int maxLines = 200)
        {
            if (!TryResolveShader(shaderPath, out Shader shader, out string shaderErr))
                return shaderErr;
            if (!TryParsePlatform(platform, out ShaderCompilerPlatform compilerPlatform, out string platErr))
                return platErr;
            if (!TryParseShaderType(shaderType, out ShaderType stage, out string stageErr))
                return stageErr;

            if (contextLines < 0) contextLines = 0;
            if (maxLines <= 0) maxLines = 200;

            Regex regex = null;
            if (!string.IsNullOrWhiteSpace(matchPattern))
            {
                try { regex = new Regex(matchPattern, RegexOptions.IgnoreCase); }
                catch (ArgumentException ex) { return $"Error: invalid matchPattern regex: {ex.Message}"; }
            }

            ShaderData data;
            try { data = ShaderUtil.GetShaderData(shader); }
            catch (Exception ex) { return $"Error: ShaderUtil.GetShaderData failed for '{shader.name}': {ex.Message}"; }
            if (data == null) return $"Error: No ShaderData for '{shader.name}'.";

            if (!TryResolveSubshader(data, -1, out ShaderData.Subshader subshader, out int usedSubshader, out string subErr))
                return subErr;

            var selected = ResolvePasses(subshader, pass, out string passErr);
            if (passErr != null) return passErr;
            if (selected.Count == 0) return $"Error: No pass matched '{pass}'.";

            var (index, shaderPass, passName) = selected[0];
            string[] kw = ParseKeywords(keywords);

            ShaderData.PreprocessedVariant pre;
            try
            {
                pre = shaderPass.PreprocessVariant(stage, kw, compilerPlatform,
                    EditorUserBuildSettings.activeBuildTarget, stripLineDirectives: true);
            }
            catch (Exception ex)
            {
                return $"Error: PreprocessVariant threw for pass [{index}] {passName}: {ex.Message}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Shader: {shader.name}  subshader {usedSubshader}  pass [{index}] {passName}");
            sb.AppendLine($"Keywords: {(kw.Length == 0 ? "(none)" : string.Join(";", kw))}  Platform: {compilerPlatform}  Stage: {stage}");

            if (!pre.Success)
            {
                sb.AppendLine("Preprocess FAILED:");
                AppendMessages(sb, pre.Messages, onlyWarnings: false);
                return sb.ToString().TrimEnd();
            }

            string code = pre.PreprocessedCode ?? "";
            if (code.Length == 0) return sb.Append("Preprocess succeeded but returned empty code.").ToString();

            var lines = code.Replace("\r\n", "\n").Split('\n');
            sb.AppendLine($"Preprocessed: {lines.Length} lines, {code.Length} chars");
            sb.AppendLine("---");

            if (regex == null)
            {
                int show = Math.Min(maxLines, lines.Length);
                for (int i = 0; i < show; i++)
                    sb.AppendLine($"{i + 1,6}: {lines[i]}");
                if (show < lines.Length)
                    sb.AppendLine($"... {lines.Length - show} more lines. Pass matchPattern to search instead of dumping.");
                return sb.ToString().TrimEnd();
            }

            // Collect matching line numbers, expand by context, merge overlapping ranges.
            var keep = new SortedSet<int>();
            int matchCount = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!regex.IsMatch(lines[i])) continue;
                matchCount++;
                for (int j = Math.Max(0, i - contextLines); j <= Math.Min(lines.Length - 1, i + contextLines); j++)
                    keep.Add(j);
            }

            if (matchCount == 0)
            {
                sb.Append($"NO MATCH for /{matchPattern}/ in the preprocessed output. ");
                sb.Append("The code you expected is NOT in this variant — check the keyword set and the #if guards.");
                return sb.ToString();
            }

            sb.AppendLine($"{matchCount} matching line(s) for /{matchPattern}/:");
            int emitted = 0, prev = -2;
            foreach (int i in keep)
            {
                if (emitted >= maxLines)
                {
                    sb.AppendLine($"... output capped at {maxLines} lines; raise maxLines or narrow matchPattern.");
                    break;
                }
                if (i != prev + 1 && prev >= 0) sb.AppendLine("       ...");
                sb.AppendLine($"{i + 1,6}: {lines[i]}");
                prev = i;
                emitted++;
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool(@"Report how many variants a shader compiles into, plus its keyword space.
Use before/after a shader edit to quantify variant explosion (e.g. 710 -> 1046, +47.3%).

usedBySceneOnly: count only variants the current scene actually uses (default false = all).

The exact count comes from ShaderUtil.GetVariantCount, which is internal Unity API accessed by
reflection. If a future Unity version renames it, the keyword breakdown is still reported and the
count is marked unavailable rather than guessed.",
            Category = "ShaderVariant", Risk = ToolRisk.Safe)]
        public static string GetShaderVariantCount(string shaderPath, bool usedBySceneOnly = false)
        {
            if (!TryResolveShader(shaderPath, out Shader shader, out string shaderErr))
                return shaderErr;

            var sb = new StringBuilder();
            sb.AppendLine($"Shader: {shader.name}");

            if (TryGetVariantCount(shader, usedBySceneOnly, out ulong count, out string countErr))
                sb.AppendLine($"  Variants: {count:N0}  (usedBySceneOnly={usedBySceneOnly})");
            else
                sb.AppendLine($"  Variants: unavailable ({countErr})");

            try
            {
                var space = shader.keywordSpace;
                var names = space.keywordNames ?? Array.Empty<string>();
                sb.AppendLine($"  Keywords: {names.Length}");
                foreach (var n in names.OrderBy(n => n, StringComparer.Ordinal))
                    sb.AppendLine($"    {n}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  Keywords: unavailable ({ex.Message})");
            }

            try
            {
                var data = ShaderUtil.GetShaderData(shader);
                if (data != null)
                {
                    sb.AppendLine($"  Subshaders: {data.SubshaderCount} (active={data.ActiveSubshaderIndex})");
                    var active = data.ActiveSubshader;
                    if (active != null) sb.AppendLine($"  Passes in active subshader: {active.PassCount}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  ShaderData: unavailable ({ex.Message})");
            }

            return sb.ToString().TrimEnd();
        }

        // ── internals ────────────────────────────────────────────────────────

        private static bool TryResolveShader(string shaderPath, out Shader shader, out string error)
        {
            shader = null;
            error = null;

            if (string.IsNullOrWhiteSpace(shaderPath))
            {
                error = "Error: shaderPath is empty.";
                return false;
            }

            if (shaderPath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                || shaderPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
            {
                shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                if (shader == null)
                {
                    error = $"Error: No shader asset at '{shaderPath}'.";
                    return false;
                }
                return true;
            }

            shader = Shader.Find(shaderPath);
            if (shader == null)
            {
                error = $"Error: Shader '{shaderPath}' not found (tried asset path and Shader.Find). Use FindShader to search by name.";
                return false;
            }
            return true;
        }

        private static bool TryParsePlatform(string platform, out ShaderCompilerPlatform result, out string error)
        {
            error = null;
            switch ((platform ?? "d3d11").Trim().ToLowerInvariant())
            {
                case "":
                case "d3d":
                case "d3d11":
                case "dx11":
                case "directx": result = ShaderCompilerPlatform.D3D; return true;
                case "vulkan": result = ShaderCompilerPlatform.Vulkan; return true;
                case "metal": result = ShaderCompilerPlatform.Metal; return true;
                case "glcore":
                case "opengl":
                case "openglcore": result = ShaderCompilerPlatform.OpenGLCore; return true;
                case "gles3":
                case "gles3x": result = ShaderCompilerPlatform.GLES3x; return true;
                case "gles2":
                case "gles20": result = ShaderCompilerPlatform.GLES20; return true;
                case "switch": result = ShaderCompilerPlatform.Switch; return true;
                case "ps4": result = ShaderCompilerPlatform.PS4; return true;
                case "gamecore": result = ShaderCompilerPlatform.GameCoreXboxOne; return true;
                default:
                    result = ShaderCompilerPlatform.None;
                    error = $"Error: unknown platform '{platform}'. Use d3d11 | vulkan | metal | glcore | gles3 | gles2 | switch | ps4 | gamecore.";
                    return false;
            }
        }

        private static bool TryParseShaderType(string shaderType, out ShaderType result, out string error)
        {
            error = null;
            switch ((shaderType ?? "fragment").Trim().ToLowerInvariant())
            {
                case "":
                case "fragment":
                case "frag":
                case "pixel": result = ShaderType.Fragment; return true;
                case "vertex":
                case "vert": result = ShaderType.Vertex; return true;
                case "geometry":
                case "geom": result = ShaderType.Geometry; return true;
                case "hull": result = ShaderType.Hull; return true;
                case "domain": result = ShaderType.Domain; return true;
                case "surface": result = ShaderType.Surface; return true;
                default:
                    result = ShaderType.Fragment;
                    error = $"Error: unknown shaderType '{shaderType}'. Use fragment | vertex | geometry | hull | domain | surface.";
                    return false;
            }
        }

        private static string[] ParseKeywords(string keywords)
        {
            if (string.IsNullOrWhiteSpace(keywords)) return Array.Empty<string>();
            return keywords
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToArray();
        }

        private static bool TryResolveSubshader(
            ShaderData data, int subshaderIndex,
            out ShaderData.Subshader subshader, out int usedIndex, out string error)
        {
            error = null;
            if (subshaderIndex < 0)
            {
                usedIndex = data.ActiveSubshaderIndex;
                subshader = data.ActiveSubshader;
            }
            else
            {
                if (subshaderIndex >= data.SubshaderCount)
                {
                    subshader = null;
                    usedIndex = subshaderIndex;
                    error = $"Error: subshaderIndex {subshaderIndex} out of range (SubshaderCount={data.SubshaderCount}).";
                    return false;
                }
                usedIndex = subshaderIndex;
                subshader = data.GetSubshader(subshaderIndex);
            }

            if (subshader == null)
            {
                error = $"Error: Subshader {usedIndex} could not be read.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Resolves a ';' separated pass selector into (index, pass, displayName) tuples.
        /// Empty selector = every pass. Unnamed passes are addressable by index only.
        /// </summary>
        private static List<(int index, ShaderData.Pass pass, string name)> ResolvePasses(
            ShaderData.Subshader subshader, string selector, out string error)
        {
            error = null;
            var all = new List<(int, ShaderData.Pass, string)>();
            for (int i = 0; i < subshader.PassCount; i++)
            {
                var p = subshader.GetPass(i);
                string n = p?.Name;
                all.Add((i, p, string.IsNullOrEmpty(n) ? "(unnamed)" : n));
            }

            if (string.IsNullOrWhiteSpace(selector)) return all;

            var wanted = selector.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim())
                                 .Where(s => s.Length > 0);

            var result = new List<(int, ShaderData.Pass, string)>();
            foreach (var w in wanted)
            {
                if (int.TryParse(w, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                {
                    if (idx < 0 || idx >= all.Count)
                    {
                        error = $"Error: pass index {idx} out of range (PassCount={all.Count}).";
                        return result;
                    }
                    result.Add(all[idx]);
                    continue;
                }

                var hit = all.Where(t => string.Equals(t.Item3, w, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hit.Count == 0)
                {
                    string available = string.Join(", ", all.Select(t => $"[{t.Item1}] {t.Item3}"));
                    error = $"Error: no pass named '{w}'. Available: {available}";
                    return result;
                }
                result.AddRange(hit);
            }
            return result;
        }

        private static void AppendMessages(StringBuilder sb, ShaderMessage[] messages, bool onlyWarnings)
        {
            if (messages == null || messages.Length == 0)
            {
                if (!onlyWarnings) sb.AppendLine("      (compiler reported no message — check keywords / pass selection)");
                return;
            }

            foreach (var m in messages)
            {
                bool isError = m.severity == ShaderCompilerMessageSeverity.Error;
                if (onlyWarnings && isError) continue;

                string where = !string.IsNullOrEmpty(m.file) && m.line > 0
                    ? $"{ShortenPath(m.file)}:{m.line} "
                    : "";
                sb.AppendLine($"      {(isError ? "error" : "warning")}: {where}{m.message}");
                if (!string.IsNullOrEmpty(m.messageDetails))
                {
                    foreach (var detail in m.messageDetails.Replace("\r\n", "\n").Split('\n'))
                    {
                        string line = detail.TrimEnd();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        // Unity appends the full platform-define and disabled-keyword dump to every
                        // message. That is dozens of lines of boilerplate identical across messages,
                        // and the caller already knows which keywords they asked for.
                        string t = line.TrimStart();
                        if (t.StartsWith("Disabled keywords:", StringComparison.Ordinal)) continue;
                        if (t.StartsWith("Platform defines:", StringComparison.Ordinal)) continue;
                        sb.AppendLine($"        {line}");
                    }
                }
            }
        }

        private static string ShortenPath(string file)
        {
            if (string.IsNullOrEmpty(file)) return "";
            int idx = file.LastIndexOfAny(new[] { '/', '\\' });
            return idx >= 0 && idx < file.Length - 1 ? file.Substring(idx + 1) : file;
        }

        /// <summary>
        /// ShaderUtil.GetVariantCount is internal. Same reflection approach as ConsoleTools uses
        /// for UnityEditor.LogEntries: if the shape ever changes we report it, never guess.
        /// </summary>
        private static bool TryGetVariantCount(Shader shader, bool usedBySceneOnly, out ulong count, out string error)
        {
            count = 0;
            error = null;
            try
            {
                // Bind by explicit signature: a name-only lookup throws AmbiguousMatchException the
                // moment Unity ships a second overload, and the catch below would turn a perfectly
                // callable API into "unavailable".
                var method = typeof(ShaderUtil).GetMethod(
                    "GetVariantCount",
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(Shader), typeof(bool) },
                    modifiers: null);

                if (method == null)
                {
                    error = "ShaderUtil.GetVariantCount not found in this Unity version";
                    return false;
                }

                object raw = method.Invoke(null, new object[] { shader, usedBySceneOnly });
                if (raw == null)
                {
                    error = "GetVariantCount returned null";
                    return false;
                }
                count = Convert.ToUInt64(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = $"reflection failed: {ex.GetBaseException().Message}";
                return false;
            }
        }
    }
}

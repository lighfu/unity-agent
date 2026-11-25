using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.SDK;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Assembly = System.Reflection.Assembly;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static partial class ScriptCompilationTools
    {
        private const int MaxVerificationAssets = 16;
        private const string Observed = "observed";
        private const string MatchesExpectations = "matches_expectations";
        private const string Mismatch = "mismatch";
        private const string Missing = "missing";
        private const string Unavailable = "unavailable";

        [AgentTool(@"Inspect the package, loaded assembly and imported assets in the current Unity Editor before testing.
packageName: exact UPM package ID. Provide expectedPackagePath (absolute or relative to the Unity project)
and/or expectedPackageVersion to compare the registered package against the intended checkout.
assemblyName: simple assembly name, optionally ending in .dll. expectedMvid compares the module GUID
against the assembly already loaded in Unity's current domain.
assetChecks: JSON array of up to 16 exact Assets/ or Packages/ paths or objects with path and optional
guid, mainType (full type name), and scriptClass (full managed type name). A registered GUID alone
does not count as a successful import. For a C# script, inspect scriptClass or supply an expected class.
At least one package, assembly or asset is required.
The JSON verdict is matches_expectations only for the expectations you supply. Observed evidence by
itself does not establish which build you intended. editor_busy means wait for compilation/import to
settle. This tool does not refresh assets or load assemblies from disk.", Risk = ToolRisk.Safe)]
        public static string VerifyLoadedVersion(string packageName = "", string assemblyName = "", string assetChecks = "",
            string expectedPackagePath = "", string expectedPackageVersion = "", string expectedMvid = "")
        {
            packageName = (packageName ?? "").Trim();
            assemblyName = (assemblyName ?? "").Trim();
            if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);
            expectedPackagePath = (expectedPackagePath ?? "").Trim();
            expectedPackageVersion = (expectedPackageVersion ?? "").Trim();
            expectedMvid = (expectedMvid ?? "").Trim();

            if (packageName.Length == 0 && (expectedPackagePath.Length > 0 || expectedPackageVersion.Length > 0))
                return "Error: packageName is required with expectedPackagePath or expectedPackageVersion.";
            if (expectedMvid.Length > 0 && (assemblyName.Length == 0 || !Guid.TryParse(expectedMvid, out _)))
                return "Error: expectedMvid requires assemblyName and must be a valid GUID.";
            if (expectedPackagePath.Length > 0 && !TryNormalizePackagePath(expectedPackagePath, out expectedPackagePath))
                return "Error: expectedPackagePath is not a valid filesystem path.";
            if (!TryParseAssetChecks(assetChecks, out var requestedAssets, out string assetError))
                return $"Error: {assetError}";
            if (packageName.Length == 0 && assemblyName.Length == 0 && requestedAssets.Count == 0)
                return "Error: Specify packageName, assemblyName or at least one asset in assetChecks.";

            bool compiling = EditorApplication.isCompiling;
            bool updating = EditorApplication.isUpdating;
            var report = JNode.Obj(("editor", UnityAgentInfoTools.DescribeEditorIdentity()),
                ("isCompiling", JNode.Bool(compiling)), ("isUpdating", JNode.Bool(updating)));
            if (compiling || updating)
            {
                report.AsObject["verdict"] = JNode.Str("editor_busy");
                return report.ToJson();
            }

            var evidence = new List<JNode>();
            if (packageName.Length != 0)
            {
                var package = InspectRegisteredPackage(packageName, expectedPackagePath, expectedPackageVersion);
                report.AsObject["package"] = package;
                evidence.Add(package);
            }
            if (assemblyName.Length != 0)
            {
                var assembly = InspectLoadedAssembly(assemblyName, expectedMvid);
                report.AsObject["assembly"] = assembly;
                evidence.Add(assembly);
            }
            if (requestedAssets.Count != 0)
            {
                var assets = requestedAssets.Select(InspectImportedAsset).ToArray();
                report.AsObject["assets"] = JNode.Arr(assets);
                evidence.AddRange(assets);
            }

            // Failed checks take precedence over observations or positive matches.
            string verdict = new[] { Unavailable, Missing, Mismatch, MatchesExpectations }
                .FirstOrDefault(state => evidence.Any(item => item["status"].AsString == state)) ?? Observed;
            report.AsObject["verdict"] = JNode.Str(verdict);
            return report.ToJson();
        }

        private static JNode InspectRegisteredPackage(string name, string expectedPath, string expectedVersion)
        {
            var evidence = JNode.Obj(("name", JNode.Str(name)));
            try
            {
                var packages = PackageInfo.GetAllRegisteredPackages();
                if (packages == null)
                    return FinishEvidence(evidence, Unavailable, "The registered UPM package list is unavailable.");
                var package = packages.FirstOrDefault(p => p != null && string.Equals(p.name, name, StringComparison.Ordinal));
                if (package == null)
                    return FinishEvidence(evidence, Missing, "UPM has not registered this package.");

                evidence.AsObject["source"] = JNode.Str(package.source.ToString());
                evidence.AsObject["assetPath"] = JNode.Str(package.assetPath);
                evidence.AsObject["resolvedPath"] = JNode.Str(package.resolvedPath);
                evidence.AsObject["version"] = JNode.Str(package.version);
                bool pathMatches = expectedPath.Length == 0 ||
                    (TryNormalizePackagePath(package.resolvedPath, out var resolvedPath) &&
                     FilePathComparer.Equals(resolvedPath, expectedPath));
                return FinishEvidence(evidence, checks: new[]
                {
                    ("resolvedPath", expectedPath, pathMatches),
                    ("version", expectedVersion, string.Equals(package.version, expectedVersion, StringComparison.Ordinal)),
                });
            }
            catch (Exception ex)
            {
                return FinishEvidence(evidence, Unavailable, $"Could not read UPM package data: {ex.GetType().Name}.");
            }
        }

        private static JNode InspectLoadedAssembly(string name, string expectedMvid)
        {
            var evidence = JNode.Obj(("name", JNode.Str(name)));
            var matches = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0)
                return FinishEvidence(evidence, Missing, "This assembly is not loaded in the current Editor domain.");

            if (matches.Length > 1)
            {
                evidence.AsObject["candidates"] = JNode.Arr(matches.Select(LoadedAssemblyIdentity).ToArray());
                return FinishEvidence(evidence, Unavailable, "Several loaded assemblies have this simple name.");
            }

            var loaded = matches[0];
            foreach (var pair in LoadedAssemblyIdentity(loaded).AsObject)
                evidence.AsObject[pair.Key] = pair.Value;
            string mvid = evidence["mvid"].AsString;
            if (string.IsNullOrEmpty(mvid))
                return FinishEvidence(evidence, Unavailable, "The loaded module did not expose an MVID.");
            bool matchesMvid = Guid.TryParse(expectedMvid, out var expected) && new Guid(mvid) == expected;
            return FinishEvidence(evidence, checks: new[] { ("mvid", expectedMvid, matchesMvid) });
        }

        private static JNode LoadedAssemblyIdentity(Assembly assembly)
        {
            string location = null;
            string mvid = null;
            string definitionPath = null;
            string packageName = null;
            if (!assembly.IsDynamic)
            {
                location = assembly.Location;
                mvid = assembly.ManifestModule.ModuleVersionId.ToString("D");
                // The compilation pipeline and UPM association are supplemental. A package
                // lookup failure does not invalidate an identity read from the loaded module.
                try
                {
                    definitionPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.GetName().Name);
                    packageName = (PackageInfo.FindForAssembly(assembly) ??
                        (string.IsNullOrEmpty(definitionPath) ? null : PackageInfo.FindForAssetPath(definitionPath)))?.name;
                }
                catch (Exception ex)
                {
                    AgentLogger.Warning(LogTag.Tool, $"Could not resolve the package for {assembly.GetName().Name}: {ex.GetType().Name}.");
                }
            }

            return JNode.Obj(
                ("fullName", JNode.Str(assembly.FullName)),
                ("mvid", mvid == null ? JNode.NullNode : JNode.Str(mvid)),
                ("location", TextOrNull(location)),
                ("assemblyDefinitionPath", TextOrNull(definitionPath)),
                ("packageName", TextOrNull(packageName)));
        }

        private static JNode InspectImportedAsset((string Path, string Guid, string MainType, string ScriptClass) request)
        {
            string guid = AssetDatabase.AssetPathToGUID(request.Path, AssetPathToGUIDOptions.OnlyExistingAssets);
            string importedPath = string.IsNullOrEmpty(guid) ? "" : AssetDatabase.GUIDToAssetPath(guid);
            var evidence = JNode.Obj(
                ("path", JNode.Str(request.Path)),
                ("guid", TextOrNull(guid)),
                ("importedPath", TextOrNull(importedPath)));

            if (!string.Equals(importedPath, request.Path, StringComparison.Ordinal))
                return FinishEvidence(evidence, Missing, "The asset database has no live entry for this exact path.");
            Type mainType = AssetDatabase.GetMainAssetTypeAtPath(importedPath);
            AssetImporter importer = AssetImporter.GetAtPath(importedPath);
            evidence.AsObject["mainType"] = TextOrNull(mainType?.FullName);
            evidence.AsObject["importerType"] = TextOrNull(importer?.GetType().FullName);
            if (mainType == null || importer == null)
                return FinishEvidence(evidence, Unavailable, "The asset has a GUID but Unity has no recognizable imported asset or importer.");

            Type scriptClass = null;
            if (typeof(MonoScript).IsAssignableFrom(mainType))
            {
                scriptClass = AssetDatabase.LoadAssetAtPath<MonoScript>(importedPath)?.GetClass();
                evidence.AsObject["scriptClass"] = TextOrNull(scriptClass?.FullName);
                evidence.AsObject["scriptAssembly"] = TextOrNull(scriptClass?.Assembly.GetName().Name);
                evidence.AsObject["scriptAssemblyMvid"] = TextOrNull(scriptClass?.Module.ModuleVersionId.ToString("D"));
            }

            return FinishEvidence(evidence, checks: new[]
            {
                ("guid", request.Guid, string.Equals(guid, request.Guid, StringComparison.OrdinalIgnoreCase)),
                ("mainType", request.MainType, string.Equals(mainType.FullName, request.MainType, StringComparison.Ordinal)),
                ("scriptClass", request.ScriptClass, string.Equals(scriptClass?.FullName, request.ScriptClass, StringComparison.Ordinal)),
            });
        }

        private static JNode FinishEvidence(JNode evidence, string status = Observed, string reason = null,
            (string Field, string Expected, bool Matches)[] checks = null)
        {
            if (checks != null)
            {
                var requested = checks.Where(check => !string.IsNullOrEmpty(check.Expected)).ToArray();
                if (requested.Length > 0)
                {
                    evidence.AsObject["checks"] = JNode.Arr(requested.Select(check => JNode.Obj(
                        ("field", JNode.Str(check.Field)), ("expected", JNode.Str(check.Expected)),
                        ("matches", JNode.Bool(check.Matches)))).ToArray());
                    status = requested.All(check => check.Matches) ? MatchesExpectations : Mismatch;
                }
            }
            if (reason != null) evidence.AsObject["reason"] = JNode.Str(reason);
            evidence.AsObject["status"] = JNode.Str(status);
            return evidence;
        }

        private static bool TryParseAssetChecks(string input,
            out List<(string Path, string Guid, string MainType, string ScriptClass)> assets, out string error)
        {
            assets = new List<(string, string, string, string)>();
            error = null;
            if (string.IsNullOrWhiteSpace(input)) return true;
            var entries = JNode.Parse(input);
            if (entries.Type != JNode.JType.Array || entries.Count > MaxVerificationAssets)
            {
                error = $"assetChecks must be a JSON array of up to {MaxVerificationAssets} entries.";
                return false;
            }

            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries.AsArray)
            {
                if (entry.Type != JNode.JType.String && (entry.Type != JNode.JType.Object ||
                    entry.AsObject.Any(pair => pair.Value.Type != JNode.JType.String ||
                        (pair.Key != "path" && pair.Key != "guid" && pair.Key != "mainType" && pair.Key != "scriptClass"))))
                {
                    error = "assetChecks entries must be paths or objects with string fields path, guid, mainType, scriptClass.";
                    return false;
                }

                string path = ((entry.Type == JNode.JType.String ? entry.AsString : entry["path"].AsString) ?? "").Replace('\\', '/');
                if ((!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal)) ||
                    path.Split('/').Any(part => part.Length == 0 || part == "." || part == "..") ||
                    path.Any(char.IsControl) || !seenPaths.Add(path))
                {
                    error = "assetChecks must name distinct exact asset paths under Assets/ or Packages/ without traversal segments.";
                    return false;
                }

                string guid = entry["guid"].AsString;
                if (!string.IsNullOrEmpty(guid))
                {
                    if (!Guid.TryParse(guid, out Guid parsed) || parsed == Guid.Empty)
                    {
                        error = $"Invalid expected GUID for asset '{path}'.";
                        return false;
                    }
                    guid = parsed.ToString("N");
                }
                assets.Add((path, guid, entry["mainType"].AsString, entry["scriptClass"].AsString));
            }
            return true;
        }

        private static StringComparer FilePathComparer => Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private static bool TryNormalizePackagePath(string path, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(projectRoot, path));
                string root = Path.GetPathRoot(fullPath);
                normalized = fullPath.Length == root.Length ? root :
                    fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (IOException) { return false; }
        }

        private static JNode TextOrNull(string text) => string.IsNullOrEmpty(text) ? JNode.NullNode : JNode.Str(text);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    /// <summary>
    /// Shared helper methods used across multiple tool classes.
    /// </summary>
    public static class ToolUtility
    {
        /// <summary>
        /// Get the Mesh from a GameObject (SkinnedMeshRenderer or MeshFilter).
        /// </summary>
        internal static Mesh GetMesh(GameObject go)
        {
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null) return smr.sharedMesh;
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null) return mf.sharedMesh;
            return null;
        }

        /// <summary>
        /// Find the avatar root name by walking up the hierarchy.
        /// Checks for VRCAvatarDescriptor first, falls back to Animator.
        /// </summary>
        internal static string FindAvatarRootName(GameObject go)
        {
            Transform current = go.transform;
            string bestName = null;

            while (current != null)
            {
                if (current.GetComponent("VRCAvatarDescriptor") != null ||
                    current.GetComponent("VRC_AvatarDescriptor") != null)
                    return current.name;

                if (current.GetComponent<Animator>() != null)
                    bestName = current.name;

                current = current.parent;
            }

            return bestName;
        }

        /// <summary>
        /// Save a Material asset to the Generated/Materials folder.
        /// </summary>
        internal static string SaveMaterialAsset(Material mat, string avatarName)
        {
            string folderPath = $"{PackagePaths.GetGeneratedDir("Materials")}/{avatarName}";
            if (!System.IO.Directory.Exists(folderPath))
                System.IO.Directory.CreateDirectory(folderPath);

            string assetPath = $"{folderPath}/{mat.name}.mat";
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(mat, assetPath);
            return assetPath;
        }

        /// <summary>
        /// Ensure an asset folder path exists, creating intermediate folders as needed.
        /// Uses AssetDatabase API for proper Unity integration.
        /// </summary>
        public static void EnsureAssetDirectory(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>
        /// Parse a string into a bool. Truthy: true/1/on/yes/enabled. Anything else (including null/empty/invalid) is false.
        /// Use when the parameter is optional and "unspecified" should be treated as false.
        /// </summary>
        public static bool ParseBool(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "on":
                case "yes":
                case "enabled":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Try to parse a string into a bool, distinguishing "explicit false" from "invalid/unspecified".
        /// Truthy: true/1/on/yes/enabled. Falsy: false/0/off/no/disabled. Anything else returns false (the method, not the value).
        /// Use when the caller needs to validate the input or skip when unspecified.
        /// </summary>
        public static bool TryParseBool(string s, out bool result)
        {
            result = false;
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "on":
                case "yes":
                case "enabled":
                    result = true;
                    return true;
                case "false":
                case "0":
                case "off":
                case "no":
                case "disabled":
                    result = false;
                    return true;
                default:
                    return false;
            }
        }

        // ===== Runtime C# compilation reference whitelist =====
        // Tools that compile user-supplied C# at runtime (RunEditorScript, AacExecuteScript)
        // pass referenced assemblies to the Mono compiler as /reference: command-line args.
        // The Unity Editor loads 200-400+ assemblies; passing all of them overflows the
        // Windows CreateProcess 32767-char command-line limit and mono.exe fails to start
        // ("ファイル名または拡張子が長すぎます"). Both tools filter the AppDomain assembly
        // list through IsScriptReference so the command line stays well under the limit.

        private static readonly HashSet<string> s_ScriptReferenceExact =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // .NET core — reference ONLY "netstandard". The project is on the .NET Standard
            // 2.1 API compat level, where netstandard.dll is the canonical reference assembly
            // (surfaces System.Object .. AppDomain). Adding mscorlib/System/System.Core/
            // System.Xml alongside it makes mcs fail with "type X is defined multiple times"
            // because the implementation assemblies redefine the same BCL types. At runtime
            // netstandard's type-forwards resolve to the already-loaded implementation.
            "netstandard",
            // UnityAgent
            "AjisaiFlow.UnityAgent.SDK",
            "AjisaiFlow.UnityAgent.Editor",
            // AnimatorAsCode (AacExecuteScript)
            "AnimatorAsCode.V1",
            "AnimatorAsCode.V1.VRChat",
            "AnimatorAsCode.V1.ModularAvatar",
            // NDMF
            "nadena.dev.ndmf",
            "nadena.dev.ndmf.runtime",
            "nadena.dev.ndmf.vrchat",
        };

        // Prefix matches — pick up whole assembly families without enumerating every module.
        // Third-party avatar tooling names are best-effort: multiple likely spellings are
        // listed so prefix matching catches version/packaging variations. A miss only
        // produces a clear "type not found" compile error, never a crash.
        private static readonly string[] s_ScriptReferencePrefixes = new[]
        {
            "UnityEngine",                  // CoreModule / AnimationModule / IMGUIModule / ...
            "UnityEditor",                  // CoreModule / GraphViewModule / ...
            "VRC.SDK3",                     // SDK3 / SDK3A / SDK3A.Editor / SDK3.Avatars / ...
            "VRC.SDKBase",                  // SDKBase / SDKBase.Editor
            "VRC.Dynamics",                 // PhysBone / Contact runtime
            "VRCSDK",                       // VRCSDK3 / VRCSDK3-Editor (legacy naming)
            "nadena.dev.modular-avatar.",   // .core / .core.editor
            "com.vrcfury",                  // VRCFury (package-style assembly naming)
            "VRCFury",                      // VRCFury (alt assembly naming)
            "lilToon",                      // lilToon shader / editor
            "jp.lilxyzw",                   // lilToon (package-style naming)
            "Thry",                         // Thry Editor (common shader GUI framework)
            "net.rs64",                     // TexTransTool
            "TexTransTool",
            "TexTransCore",
        };

        /// <summary>
        /// Whether an assembly should be passed as a /reference: to the runtime C# compiler.
        /// Filters the AppDomain assembly list down to a whitelist so the Mono compiler
        /// command line stays under the Windows 32767-char CreateProcess limit.
        /// Pass the simple assembly name (e.g. asm.GetName().Name).
        /// </summary>
        public static bool IsScriptReference(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return false;
            if (s_ScriptReferenceExact.Contains(assemblyName)) return true;
            foreach (var prefix in s_ScriptReferencePrefixes)
                if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}

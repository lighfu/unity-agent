using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace AjisaiFlow.UnityAgent.Editor.MA
{
    /// <summary>
    /// Helper for configuring ModularAvatarParameters entries.
    /// </summary>
    internal static class MAParameterBuilder
    {
#if MODULAR_AVATAR

        public static void AddBoolParam(Component maParams, string name,
            bool defaultValue = false, bool saved = true, bool localOnly = false)
        {
            AddParam(maParams, name, ParameterSyncType.Bool, defaultValue ? 1f : 0f, saved, localOnly);
        }

        public static void AddIntParam(Component maParams, string name,
            int defaultValue = 0, bool saved = true, bool localOnly = false)
        {
            AddParam(maParams, name, ParameterSyncType.Int, defaultValue, saved, localOnly);
        }

        public static void AddFloatParam(Component maParams, string name,
            float defaultValue = 0f, bool saved = true, bool localOnly = false)
        {
            AddParam(maParams, name, ParameterSyncType.Float, defaultValue, saved, localOnly);
        }

        /// <summary>
        /// Add a parameter with a string-based syncType.
        /// Returns false if syncType is invalid or parameter already exists.
        /// </summary>
        public static bool AddParam(Component maParams, string name, string syncType,
            float defaultValue = 0f, bool saved = true, bool localOnly = false)
        {
            if (!MAComponentFactory.TryParseSyncType(syncType, out var parsedType))
                return false;
            return AddParam(maParams, name, parsedType, defaultValue, saved, localOnly);
        }

        public static bool HasParameter(Component maParams, string name)
        {
            var p = (ModularAvatarParameters)maParams;
            return p.parameters != null && p.parameters.Any(x => x.nameOrPrefix == name);
        }

        /// <summary>
        /// Get all parameter names from a MAParameters component.
        /// </summary>
        public static List<string> GetParameterNames(Component maParams)
        {
            var p = (ModularAvatarParameters)maParams;
            if (p.parameters == null) return new List<string>();
            return p.parameters.Select(x => x.nameOrPrefix).ToList();
        }

        private static bool AddParam(Component maParams, string name,
            ParameterSyncType syncType, float defaultValue, bool saved, bool localOnly)
        {
            var p = (ModularAvatarParameters)maParams;
            Undo.RecordObject(p, "Add MA Parameter");

            if (p.parameters == null)
                p.parameters = new List<ParameterConfig>();

            // Duplicate check
            if (p.parameters.Any(x => x.nameOrPrefix == name))
                return false;

            p.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = name,
                syncType = syncType,
                defaultValue = defaultValue,
                saved = saved,
                localOnly = localOnly
            });
            EditorUtility.SetDirty(p);
            return true;
        }

#else
        public static void AddBoolParam(Component p, string n, bool d = false, bool s = true, bool l = false) { }
        public static void AddIntParam(Component p, string n, int d = 0, bool s = true, bool l = false) { }
        public static void AddFloatParam(Component p, string n, float d = 0f, bool s = true, bool l = false) { }
        public static bool AddParam(Component p, string n, string t, float d = 0f, bool s = true, bool l = false) => false;
        public static bool HasParameter(Component p, string n) => false;
        public static List<string> GetParameterNames(Component p) => new List<string>();
#endif
    }
}

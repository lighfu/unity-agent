#if GESTURE_MANAGER
using System;
using System.Collections.Generic;
using BlackStartX.GestureManager.Editor.Modules.Vrc3;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace UnityEngine
{
    public class Object
    {
        private static int _id;
        private readonly int _instanceId = ++_id;
        public string name;
        public int GetInstanceID() => _instanceId;
    }
    public class Texture2D : Object { }
    public class GameObject : Object
    {
        public VRCAvatarDescriptor Descriptor;
        public T GetComponent<T>() where T : class => Descriptor as T;
    }
    public enum AnimatorControllerParameterType { Float, Int, Bool, Trigger }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 ClampMagnitude(Vector2 v, float max)
        {
            float magnitude = (float)Math.Sqrt(v.x * v.x + v.y * v.y);
            return magnitude <= max ? v : new Vector2(v.x * max / magnitude, v.y * max / magnitude);
        }
    }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
}
namespace UnityEditor
{
    public static class AssetDatabase
    {
        public static string GetAssetPath(UnityEngine.Object obj) => obj == null ? "" : "Assets/" + obj.name + ".asset";
        public static string AssetPathToGUID(string path) => path;
    }
}
namespace VRC.SDK3.Avatars.Components
{
    public class VRCAvatarDescriptor
    {
        public GameObject gameObject;
        public VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu expressionsMenu;
        public VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters expressionParameters;
    }
}
namespace VRC.SDK3.Avatars.ScriptableObjects
{
    public class VRCExpressionsMenu : UnityEngine.Object
    {
        public List<Control> controls = new List<Control>();
        public class Control
        {
            public enum ControlType { Button, Toggle, SubMenu, TwoAxisPuppet, FourAxisPuppet, RadialPuppet }
            public class Parameter { public string name; }
            public struct Label { public string name; public Texture2D icon; }
            public string name;
            public ControlType type;
            public Texture2D icon;
            public float value;
            public Parameter parameter;
            public Parameter[] subParameters;
            public Label[] labels;
            public VRCExpressionsMenu subMenu;
        }
    }
    public class VRCExpressionParameters
    {
        public enum ValueType { Int, Float, Bool }
        public Parameter[] parameters;
        public class Parameter
        {
            public string name;
            public ValueType valueType;
            public float defaultValue;
            public bool saved;
            public bool networkSynced;
        }
    }
}
namespace BlackStartX.GestureManager.Editor.Modules.Vrc3.Params
{
    public class Vrc3Param
    {
        public readonly string Name;
        public readonly AnimatorControllerParameterType Type;
        public readonly List<float> Writes = new List<float>();
        private float _value;
        public Vrc3Param(string name, AnimatorControllerParameterType type) { Name = name; Type = type; }
        public float FloatValue() => _value;
        public int IntValue() => (int)_value;
        public bool BoolValue() => _value != 0;
        public void Set(ModuleVrc3 module, float value) { Writes.Add(value); _value = value; }
    }
}
namespace BlackStartX.GestureManager.Editor.Modules.Vrc3
{
    public class ModuleVrc3
    {
        public bool Active = true;
        public VRCAvatarDescriptor AvatarDescriptor;
        public readonly Dictionary<string, Params.Vrc3Param> Params = new Dictionary<string, Params.Vrc3Param>();
    }
}
namespace BlackStartX.GestureManager
{
    public class GestureManager { public object Module; }
}
namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static partial class GestureManagerTools
    {
        internal static VRCAvatarDescriptor TestAvatar;
        internal static BlackStartX.GestureManager.GestureManager TestManager;
        private static GameObject FindGO(string name) => TestAvatar?.gameObject.name == name ? TestAvatar.gameObject : null;
        private static BlackStartX.GestureManager.GestureManager FindInstance() => TestManager;
        private static bool TryGetVrc3(out BlackStartX.GestureManager.GestureManager gm, out ModuleVrc3 module, out string error)
        {
            gm = TestManager;
            module = gm?.Module as ModuleVrc3;
            error = module == null ? "Error: No active VRC3 preview." : null;
            return module != null;
        }
    }
}
#endif

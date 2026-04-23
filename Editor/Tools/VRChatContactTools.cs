using UnityEngine;
using UnityEditor;
using System;
using System.Text;

using AjisaiFlow.UnityAgent.SDK;

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static class VRChatContactTools
    {
        private static GameObject FindGO(string name) => MeshAnalysisTools.FindGameObject(name);

        private const string VrcContactSenderTypeName = "VRC.SDK3.Dynamics.Contact.Components.VRCContactSender";
        private const string VrcContactReceiverTypeName = "VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver";

        [AgentTool("List all VRCContactSender and VRCContactReceiver components under an avatar with their collision tags, shape, and parameters.")]
        public static string ListContacts(string avatarRootName)
        {
            var go = FindGO(avatarRootName);
            if (go == null) return $"Error: GameObject '{avatarRootName}' not found.";

            var senderType = VRChatTools.FindVrcType(VrcContactSenderTypeName);
            var receiverType = VRChatTools.FindVrcType(VrcContactReceiverTypeName);

            if (senderType == null && receiverType == null)
                return "Error: VRChat SDK Contact types not found. Ensure VRChat Avatar SDK is installed.";

            var sb = new StringBuilder();
            sb.AppendLine($"Contacts under '{avatarRootName}':");

            // Senders
            if (senderType != null)
            {
                var senders = go.GetComponentsInChildren(senderType, true);
                sb.AppendLine($"\n  Contact Senders ({senders.Length}):");

                for (int i = 0; i < senders.Length; i++)
                {
                    var sender = senders[i];
                    var so = new SerializedObject(sender);
                    string path = VRChatTools.GetRelativePath(go.transform, sender.transform);
                    sb.AppendLine($"    [{i}] {path}");

                    // Shape type
                    var shapeType = so.FindProperty("shapeType");
                    if (shapeType != null)
                    {
                        string[] shapeNames = { "Sphere", "Capsule" };
                        int stVal = shapeType.intValue;
                        sb.AppendLine($"      Shape: {(stVal >= 0 && stVal < shapeNames.Length ? shapeNames[stVal] : stVal.ToString())}");
                    }

                    // Radius
                    var radius = so.FindProperty("radius");
                    if (radius != null) sb.AppendLine($"      Radius: {radius.floatValue:F3}");

                    // Height (for capsule)
                    var height = so.FindProperty("height");
                    if (height != null && shapeType != null && shapeType.intValue == 1)
                        sb.AppendLine($"      Height: {height.floatValue:F3}");

                    // Collision tags
                    AppendCollisionTags(sb, so);
                }
            }

            // Receivers
            if (receiverType != null)
            {
                var receivers = go.GetComponentsInChildren(receiverType, true);
                sb.AppendLine($"\n  Contact Receivers ({receivers.Length}):");

                for (int i = 0; i < receivers.Length; i++)
                {
                    var receiver = receivers[i];
                    var so = new SerializedObject(receiver);
                    string path = VRChatTools.GetRelativePath(go.transform, receiver.transform);
                    sb.AppendLine($"    [{i}] {path}");

                    // Shape type
                    var shapeType = so.FindProperty("shapeType");
                    if (shapeType != null)
                    {
                        string[] shapeNames = { "Sphere", "Capsule" };
                        int stVal = shapeType.intValue;
                        sb.AppendLine($"      Shape: {(stVal >= 0 && stVal < shapeNames.Length ? shapeNames[stVal] : stVal.ToString())}");
                    }

                    // Radius
                    var radius = so.FindProperty("radius");
                    if (radius != null) sb.AppendLine($"      Radius: {radius.floatValue:F3}");

                    // Receiver type
                    var receiverTypeProp = so.FindProperty("receiverType");
                    if (receiverTypeProp != null)
                    {
                        string[] receiverTypeNames = { "Constant", "OnEnter", "Proximity" };
                        int rtVal = receiverTypeProp.intValue;
                        sb.AppendLine($"      ReceiverType: {(rtVal >= 0 && rtVal < receiverTypeNames.Length ? receiverTypeNames[rtVal] : rtVal.ToString())}");
                    }

                    // Parameter
                    var parameter = so.FindProperty("parameter");
                    if (parameter != null && !string.IsNullOrEmpty(parameter.stringValue))
                        sb.AppendLine($"      Parameter: {parameter.stringValue}");

                    // Collision tags
                    AppendCollisionTags(sb, so);
                }
            }

            return sb.ToString().TrimEnd();
        }

        [AgentTool("Add a VRCContactReceiver to a GameObject. shapeType: 0=Sphere, 1=Capsule. receiverType: 0=Constant, 1=OnEnter, 2=Proximity. collisionTags: comma-separated tags.")]
        public static string AddContactReceiver(string gameObjectName, string parameter, string collisionTags = "Head",
            int shapeType = 0, float radius = 0.1f, int receiverType = 1)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var type = VRChatTools.FindVrcType(VrcContactReceiverTypeName);
            if (type == null) return "Error: VRChat SDK Contact types not found.";

            var comp = Undo.AddComponent(go, type);
            var so = new SerializedObject(comp);

            SetPropInt(so, "shapeType", shapeType);
            SetPropFloat(so, "radius", radius);
            SetPropInt(so, "receiverType", receiverType);
            SetPropString(so, "parameter", parameter);

            SetCollisionTags(so, collisionTags);
            so.ApplyModifiedProperties();

            return $"Success: Added VRCContactReceiver to '{gameObjectName}' (param='{parameter}', tags=[{collisionTags}]).";
        }

        [AgentTool("Add a VRCContactSender to a GameObject. shapeType: 0=Sphere, 1=Capsule. collisionTags: comma-separated tags.")]
        public static string AddContactSender(string gameObjectName, string collisionTags = "Head",
            int shapeType = 0, float radius = 0.1f)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var type = VRChatTools.FindVrcType(VrcContactSenderTypeName);
            if (type == null) return "Error: VRChat SDK Contact types not found.";

            var comp = Undo.AddComponent(go, type);
            var so = new SerializedObject(comp);

            SetPropInt(so, "shapeType", shapeType);
            SetPropFloat(so, "radius", radius);

            SetCollisionTags(so, collisionTags);
            so.ApplyModifiedProperties();

            return $"Success: Added VRCContactSender to '{gameObjectName}' (tags=[{collisionTags}]).";
        }

        [AgentTool("Configure an existing VRCContactReceiver. Use -999 for unchanged floats, -1 for unchanged ints. collisionTags: comma-separated (empty=keep current).")]
        public static string ConfigureContactReceiver(string gameObjectName, int receiverType = -1,
            string parameter = "", float radius = -999f, float height = -999f,
            int shapeType = -1, string collisionTags = "", string position = "", string rotation = "")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var type = VRChatTools.FindVrcType(VrcContactReceiverTypeName);
            if (type == null) return "Error: VRChat SDK Contact types not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: No VRCContactReceiver on '{gameObjectName}'.";

            var so = new SerializedObject(comp);
            Undo.RecordObject(comp, "Configure ContactReceiver");

            if (receiverType >= 0) SetPropInt(so, "receiverType", receiverType);
            if (!string.IsNullOrEmpty(parameter)) SetPropString(so, "parameter", parameter);
            if (radius > -999f) SetPropFloat(so, "radius", radius);
            if (height > -999f) SetPropFloat(so, "height", height);
            if (shapeType >= 0) SetPropInt(so, "shapeType", shapeType);
            if (!string.IsNullOrEmpty(collisionTags)) SetCollisionTags(so, collisionTags);

            if (!string.IsNullOrEmpty(position))
            {
                var pos = ParseVector3(position);
                if (pos.HasValue) SetPropVector3(so, "position", pos.Value);
            }
            if (!string.IsNullOrEmpty(rotation))
            {
                var rot = ParseVector3(rotation);
                if (rot.HasValue) SetPropVector3(so, "rotation", Quaternion.Euler(rot.Value));
            }

            so.ApplyModifiedProperties();
            return $"Success: Configured VRCContactReceiver on '{gameObjectName}'.";
        }

        [AgentTool("Configure an existing VRCContactSender. Use -999 for unchanged floats, -1 for unchanged ints.")]
        public static string ConfigureContactSender(string gameObjectName, float radius = -999f,
            float height = -999f, int shapeType = -1, string collisionTags = "",
            string position = "", string rotation = "")
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var type = VRChatTools.FindVrcType(VrcContactSenderTypeName);
            if (type == null) return "Error: VRChat SDK Contact types not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: No VRCContactSender on '{gameObjectName}'.";

            var so = new SerializedObject(comp);
            Undo.RecordObject(comp, "Configure ContactSender");

            if (radius > -999f) SetPropFloat(so, "radius", radius);
            if (height > -999f) SetPropFloat(so, "height", height);
            if (shapeType >= 0) SetPropInt(so, "shapeType", shapeType);
            if (!string.IsNullOrEmpty(collisionTags)) SetCollisionTags(so, collisionTags);

            if (!string.IsNullOrEmpty(position))
            {
                var pos = ParseVector3(position);
                if (pos.HasValue) SetPropVector3(so, "position", pos.Value);
            }
            if (!string.IsNullOrEmpty(rotation))
            {
                var rot = ParseVector3(rotation);
                if (rot.HasValue) SetPropVector3(so, "rotation", Quaternion.Euler(rot.Value));
            }

            so.ApplyModifiedProperties();
            return $"Success: Configured VRCContactSender on '{gameObjectName}'.";
        }

        [AgentTool("Inspect a VRCContactReceiver or VRCContactSender on a GameObject in detail.")]
        public static string InspectContact(string gameObjectName)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var senderType = VRChatTools.FindVrcType(VrcContactSenderTypeName);
            var receiverType = VRChatTools.FindVrcType(VrcContactReceiverTypeName);

            var sb = new StringBuilder();
            bool found = false;

            if (receiverType != null)
            {
                var receiver = go.GetComponent(receiverType);
                if (receiver != null)
                {
                    found = true;
                    var so = new SerializedObject(receiver);
                    sb.AppendLine($"VRCContactReceiver on '{gameObjectName}':");
                    AppendContactDetails(sb, so, true);
                }
            }

            if (senderType != null)
            {
                var sender = go.GetComponent(senderType);
                if (sender != null)
                {
                    found = true;
                    var so = new SerializedObject(sender);
                    sb.AppendLine($"VRCContactSender on '{gameObjectName}':");
                    AppendContactDetails(sb, so, false);
                }
            }

            if (!found) return $"Error: No VRCContactReceiver or VRCContactSender found on '{gameObjectName}'.";
            return sb.ToString().TrimEnd();
        }

        [AgentTool("Remove a VRCContactReceiver or VRCContactSender from a GameObject. contactType: 'receiver' or 'sender'. Requires confirmation.")]
        public static string RemoveContact(string gameObjectName, string contactType)
        {
            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            string typeName;
            if (contactType.ToLower() == "receiver")
                typeName = VrcContactReceiverTypeName;
            else if (contactType.ToLower() == "sender")
                typeName = VrcContactSenderTypeName;
            else
                return "Error: contactType must be 'receiver' or 'sender'.";

            var type = VRChatTools.FindVrcType(typeName);
            if (type == null) return "Error: VRChat SDK Contact types not found.";

            var comp = go.GetComponent(type);
            if (comp == null) return $"Error: No VRCContact{(contactType.ToLower() == "receiver" ? "Receiver" : "Sender")} on '{gameObjectName}'.";

            if (!AgentSettings.RequestConfirmation(
                "Contact削除",
                $"'{gameObjectName}' から VRCContact{(contactType.ToLower() == "receiver" ? "Receiver" : "Sender")} を削除します。"))
                return "Cancelled: User denied the removal.";

            Undo.DestroyObjectImmediate(comp);
            return $"Success: Removed VRCContact{(contactType.ToLower() == "receiver" ? "Receiver" : "Sender")} from '{gameObjectName}'.";
        }

        // ===== Helpers =====

        private static void AppendContactDetails(StringBuilder sb, SerializedObject so, bool isReceiver)
        {
            string[] shapeNames = { "Sphere", "Capsule" };
            var shapeType = so.FindProperty("shapeType");
            if (shapeType != null)
            {
                int val = shapeType.intValue;
                sb.AppendLine($"  Shape: {(val >= 0 && val < shapeNames.Length ? shapeNames[val] : val.ToString())}");
            }

            var radius = so.FindProperty("radius");
            if (radius != null) sb.AppendLine($"  Radius: {radius.floatValue:F3}");

            var height = so.FindProperty("height");
            if (height != null && shapeType != null && shapeType.intValue == 1)
                sb.AppendLine($"  Height: {height.floatValue:F3}");

            var posProp = so.FindProperty("position");
            if (posProp != null)
                sb.AppendLine($"  Position: ({posProp.vector3Value.x:F3}, {posProp.vector3Value.y:F3}, {posProp.vector3Value.z:F3})");

            if (isReceiver)
            {
                string[] receiverTypeNames = { "Constant", "OnEnter", "Proximity" };
                var receiverTypeProp = so.FindProperty("receiverType");
                if (receiverTypeProp != null)
                {
                    int val = receiverTypeProp.intValue;
                    sb.AppendLine($"  ReceiverType: {(val >= 0 && val < receiverTypeNames.Length ? receiverTypeNames[val] : val.ToString())}");
                }

                var parameter = so.FindProperty("parameter");
                if (parameter != null)
                    sb.AppendLine($"  Parameter: {parameter.stringValue}");

                var minVel = so.FindProperty("minVelocity");
                if (minVel != null) sb.AppendLine($"  MinVelocity: {minVel.floatValue:F3}");
            }

            AppendCollisionTags(sb, so);
        }

        private static void AppendCollisionTags(StringBuilder sb, SerializedObject so)
        {
            var collisionTags = so.FindProperty("collisionTags");
            if (collisionTags != null && collisionTags.isArray && collisionTags.arraySize > 0)
            {
                var tags = new StringBuilder();
                for (int j = 0; j < collisionTags.arraySize; j++)
                {
                    if (j > 0) tags.Append(", ");
                    tags.Append(collisionTags.GetArrayElementAtIndex(j).stringValue);
                }
                sb.AppendLine($"  CollisionTags: [{tags}]");
            }
        }

        private static void SetCollisionTags(SerializedObject so, string tagsCsv)
        {
            var tagsProp = so.FindProperty("collisionTags");
            if (tagsProp == null || !tagsProp.isArray) return;

            var tags = tagsCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            tagsProp.arraySize = tags.Length;
            for (int i = 0; i < tags.Length; i++)
                tagsProp.GetArrayElementAtIndex(i).stringValue = tags[i].Trim();
        }

        private static void SetPropInt(SerializedObject so, string name, int value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.intValue = value;
        }

        private static void SetPropFloat(SerializedObject so, string name, float value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.floatValue = value;
        }

        private static void SetPropString(SerializedObject so, string name, string value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.stringValue = value;
        }

        private static void SetPropVector3(SerializedObject so, string name, Vector3 value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.vector3Value = value;
        }

        private static void SetPropVector3(SerializedObject so, string name, Quaternion value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.quaternionValue = value;
        }

        [AgentTool(@"Read the LIVE runtime proximity value of a VRCContactReceiver during Play mode (or GestureManager preview).
Returns: paramAccess (current receiver value 0-1 for Proximity, 0/1 for OnEnter/Constant), parameter name,
receiver type, collision tags, allowSelf/allowOthers flags, and whether the receiver is firing.
Use to distinguish 'Contact not firing' vs 'Contact firing but Animator not writing to material'.")]
        public static string GetContactReceiverRuntimeValue(string gameObjectName)
        {
            if (!EditorApplication.isPlaying)
                return "Error: Requires Play mode (or GestureManager preview) to observe runtime values.";

            var go = FindGO(gameObjectName);
            if (go == null) return $"Error: GameObject '{gameObjectName}' not found.";

            var receiverType = VRChatTools.FindVrcType(VrcContactReceiverTypeName);
            if (receiverType == null)
                return "Error: VRChat SDK Contact type not found.";

            var receiver = go.GetComponent(receiverType) as Behaviour;
            if (receiver == null)
                return $"Error: No VRCContactReceiver on '{gameObjectName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"VRCContactReceiver on '{gameObjectName}':");
            sb.AppendLine($"  enabled: {receiver.enabled}");
            sb.AppendLine($"  isActiveAndEnabled: {receiver.isActiveAndEnabled}");

            // Reflection: read paramAccess (float), parameter (string), receiverType (enum), allowSelf, allowOthers,
            // localOnly, collisionTags, minVelocity.
            var t = receiver.GetType();
            string paramName = TryReadField<string>(t, receiver, "parameter") ?? "(unknown)";
            sb.AppendLine($"  parameter: '{paramName}'");

            object recTypeVal = TryReadFieldObject(t, receiver, "receiverType");
            sb.AppendLine($"  receiverType: {recTypeVal ?? "<unknown>"}");

            // paramAccess is of type IAnimParameterAccess (class), NOT float.
            // Under normal runtime, VRC SDK assigns AnimParameterAccessAvatarSDK (wraps Animator).
            // Under GM preview, GestureManager assigns AnimParameterAccessAvatarGmg (wraps Vrc3Param).
            // Both expose .floatVal / .boolVal / .intVal properties.
            object paramAccessObj = TryReadFieldObject(t, receiver, "paramAccess");
            float paramAccess = 0f;
            string paramAccessImpl = "<null>";
            if (paramAccessObj != null)
            {
                paramAccessImpl = paramAccessObj.GetType().Name;
                var pt = paramAccessObj.GetType();
                var floatValProp = pt.GetProperty("floatVal", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (floatValProp != null)
                {
                    try { paramAccess = (float)floatValProp.GetValue(paramAccessObj); }
                    catch { /* leave 0 */ }
                }
            }
            sb.AppendLine($"  paramAccess (runtime value): {paramAccess.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}  [impl={paramAccessImpl}]");

            bool firing = paramAccess > 0f;
            sb.AppendLine($"  firing: {firing}");

            float minVel = TryReadField<float>(t, receiver, "minVelocity");
            sb.AppendLine($"  minVelocity: {minVel.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}");

            sb.AppendLine($"  allowSelf: {TryReadField<bool>(t, receiver, "allowSelf")}");
            sb.AppendLine($"  allowOthers: {TryReadField<bool>(t, receiver, "allowOthers")}");
            sb.AppendLine($"  localOnly: {TryReadField<bool>(t, receiver, "localOnly")}");

            // collisionTags (List<string>)
            var tagsField = t.GetField("collisionTags", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (tagsField != null)
            {
                var tagsObj = tagsField.GetValue(receiver) as System.Collections.IEnumerable;
                if (tagsObj != null)
                {
                    var tags = new System.Collections.Generic.List<string>();
                    foreach (var tag in tagsObj) tags.Add(tag?.ToString() ?? "<null>");
                    sb.AppendLine($"  collisionTags: [{string.Join(", ", tags)}]");
                }
            }

            // radius/height
            float radius = TryReadField<float>(t, receiver, "radius");
            sb.AppendLine($"  radius: {radius.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}");

            // World position for easy raycast setup
            sb.AppendLine($"  world position: ({receiver.transform.position.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, {receiver.transform.position.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, {receiver.transform.position.z.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)})");

            return sb.ToString().TrimEnd();
        }

        private static T TryReadField<T>(System.Type t, object instance, string name)
        {
            var f = t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (f == null) return default(T);
            try { return (T)f.GetValue(instance); } catch { return default(T); }
        }

        private static object TryReadFieldObject(System.Type t, object instance, string name)
        {
            var f = t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(instance);
        }

        private static Vector3? ParseVector3(string s)
        {
            var parts = s.Split(',');
            if (parts.Length != 3) return null;
            if (float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y) &&
                float.TryParse(parts[2].Trim(), out float z))
                return new Vector3(x, y, z);
            return null;
        }
    }
}

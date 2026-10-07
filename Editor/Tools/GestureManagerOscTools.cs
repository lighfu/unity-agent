using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.SDK;

#if GESTURE_MANAGER
using BlackStartX.GestureManager.Editor.Modules.Vrc3;
using BlackStartX.GestureManager.Editor.Modules.Vrc3.Cache;
using BlackStartX.GestureManager.Editor.Modules.Vrc3.OpenSoundControl;
using UnityEngine;
#endif

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static partial class GestureManagerTools
    {
        [AgentTool(@"Read Gesture Manager's live OSC emulator state, UDP endpoints, loaded settings,
input/output addresses, and received messages. Requires an active VRC3 GM preview.
section is all, mappings, received, or summary. offset/limit (default 50, max 200) independently
page each selected list. mappings sort by parameter/input/output address; received sorts by address.
Each list includes page metadata (total, returned, hasMore, nextOffset). Zero is not an unlimited limit.
argumentOffset/argumentLimit (default 16, max 200) page each received message's arguments independently.
Received string arguments over 256 characters return a valuePreview with length/truncated metadata.
Pages reflect the live emulator, so incoming messages or settings changes may change subsequent pages.
This reports the GM emulator, separate from the VRChat OSC configuration tools.", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetOscState(int offset = 0, int limit = GestureManagerPaging.DefaultLimit,
            string section = "all", int argumentOffset = 0, int argumentLimit = 16)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            var pagingError = GestureManagerPaging.Validate(offset, limit);
            if (pagingError != null) return pagingError;
            pagingError = GestureManagerPaging.Validate(argumentOffset, argumentLimit);
            if (pagingError != null) return pagingError.Replace("offset", "argumentOffset").Replace("limit", "argumentLimit");
            var key = (section ?? "").Trim().ToLowerInvariant();
            if (key != "all" && key != "mappings" && key != "received" && key != "summary")
                return "Error: section must be all, mappings, received, or summary.";
            try { return OscState(module, offset, limit, key, argumentOffset, argumentLimit).ToJson(); }
            catch (Exception ex) { return OscError(ex); }
#endif
        }

        [AgentTool(@"Start Gesture Manager's UDP OSC emulator, using its receive handlers and parameter outputs.
Defaults match GM's 'Start on VRChat ports': listen on 9000, send to 127.0.0.1:9001.
For custom ports pass listenPort/sendPort (1..65535) and a numeric IPv4 sendAddress.
Load settings first to use custom address mappings; otherwise all GM parameters are mapped.
Returns errors for occupied ports or dummy/pose modes. Stop before changing endpoints.
Returned state uses default paging; continue each list with GestureManagerGetOscState.")]
        public static string GestureManagerStartOsc(int listenPort = 9000, string sendAddress = "127.0.0.1", int sendPort = 9001)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            if (listenPort < 1 || listenPort > 65535 || sendPort < 1 || sendPort > 65535)
                return "Error: listenPort and sendPort must be between 1 and 65535.";
            if (!IPAddress.TryParse(sendAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                return "Error: sendAddress must be a numeric IPv4 address, for example 127.0.0.1.";

            UdpListener listener = null;
            UdpSender sender = null;
            OscModule osc = null;
            OscSettings settings = null;
            object previousData = null;
            bool changedData = false;
            try
            {
                osc = OscController(module);
                if (OscField(module, "DummyMode") != null)
                    return "Error: Gesture Manager OSC is unavailable while a dummy/pose/test-animation mode is active.";
                if (OscEnabled(osc))
                    return "Error: Gesture Manager OSC is already running. Call GestureManagerStopOsc before changing ports.";
                // GM's private Start() shows modal dialogs on failure and can leave one socket open.
                // Use its own UDP classes, settings and queue callback, with transactional cleanup.
                if (OscField(osc, "_listener") != null || OscField(osc, "_sender") != null)
                    OscCall(osc, "Stop", new[] { typeof(bool) }, true);
                settings = (OscSettings)OscField(osc, "_settings");
                previousData = OscField(settings, "_data");
                changedData = true;
                PrepareOscSettings(settings);
                var callback = (Action<byte[]>)Delegate.CreateDelegate(typeof(Action<byte[]>), osc,
                    OscMethod(osc, "AddQueue", new[] { typeof(byte[]) }));
                listener = new UdpListener(listenPort, callback);
                sender = new UdpSender(address.ToString(), sendPort);
                OscSetField(osc, "_listener", listener);
                OscSetField(osc, "_sender", sender);
                OscSetField(osc, "_customSelection", false);
                return JNode.Obj(("success", JNode.Bool(true)), ("state", OscState(module))).ToJson();
            }
            catch (Exception ex)
            {
                listener?.Close();
                sender?.Close();
                if (changedData) OscSetField(settings, "_data", previousData);
                if (listener != null) OscSetField(osc, "_listener", null);
                if (sender != null) OscSetField(osc, "_sender", null);
                return OscError(ex);
            }
#endif
        }

        [AgentTool(@"Stop Gesture Manager's OSC emulator. Closes both UDP sockets and clears received messages,
using GM's own Stop operation. Keeps loaded OSC settings. Requires a VRC3 GM preview.")]
        public static string GestureManagerStopOsc()
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            try
            {
                OscCall(OscController(module), "Stop", new[] { typeof(bool) }, true);
                return "Success: Gesture Manager OSC stopped; UDP sockets closed and receive history cleared.";
            }
            catch (Exception ex) { return OscError(ex); }
#endif
        }

        [AgentTool(@"Load Gesture Manager OSC address mappings from a VRChat avatar configuration JSON.
configPath is an absolute file path. With no path, use GM's 'Load Settings' lookup for the
current avatar blueprint and selected VRChat user. useDefault=true clears custom settings
and maps every GM parameter at /avatar/parameters/<name>. Stop OSC before changing settings.
Invalid JSON, unknown types and duplicate endpoints return errors without changing settings.
Returned state uses default paging; continue each list with GestureManagerGetOscState.")]
        public static string GestureManagerLoadOscSettings(string configPath = "", bool useDefault = false)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            if (useDefault && !string.IsNullOrWhiteSpace(configPath))
                return "Error: Specify either configPath or useDefault=true.";
            OscSettings settings = null;
            Dictionary<FieldInfo, object> previous = null;
            try
            {
                var osc = OscController(module);
                if (OscEnabled(osc)) return "Error: Stop Gesture Manager OSC before changing settings.";
                settings = (OscSettings)OscField(osc, "_settings");
                previous = settings.GetType().GetFields(OscBinding).Where(f => !f.IsInitOnly)
                    .ToDictionary(f => f, f => f.GetValue(settings));
                if (useDefault) settings.Clean();
                else
                {
                    if (string.IsNullOrWhiteSpace(configPath))
                    {
                        settings.Load();
                        var problem = (string)OscField(settings, "_userProblem") ?? (string)OscField(settings, "_fileProblem");
                        if (problem != null) throw new ArgumentException(problem);
                        configPath = (string)OscField(settings, "_filePathString");
                    }
                    if (!Path.IsPathRooted(configPath)) throw new ArgumentException("configPath must be an absolute file path.");
                    configPath = Path.GetFullPath(configPath);
                    ReadOscFile(configPath);
                    OscSetField(settings, "_filePathString", configPath);
                    OscSetField(settings, "_fileNameString", Path.GetFileName(configPath));
                    OscSetField(settings, "_fileExist", true); // The inspector normally updates this flag.
                    OscSetField(settings, "_userProblem", null);
                    OscSetField(settings, "_fileProblem", null);
                    settings.Loaded = true;
                }
                PrepareOscSettings(settings);
                return JNode.Obj(("success", JNode.Bool(true)), ("state", OscState(module))).ToJson();
            }
            catch (Exception ex)
            {
                if (previous != null) foreach (var pair in previous) pair.Key.SetValue(settings, pair.Value);
                return OscError(ex);
            }
#endif
        }

        [AgentTool(@"Inject an OSC message into Gesture Manager's own receive handler without opening UDP ports.
Uses loaded OSC input mappings, invokes parameter OnChange handlers and updates GM receive history.
argumentsJson is a JSON scalar or argument array, e.g. 0.8, true, [1], or [{""type"":""Float"",""value"":1}].
Supported numeric input types: Bool, Int and Float. GM converts only the first argument to a
parameter value; further arguments are recorded. Unsupported/unmapped addresses return an error.
GM does not implement VRChat /input/* or tracking OSC endpoints unless mapped to a parameter.")]
        public static string GestureManagerInjectOscMessage(string address, string argumentsJson)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            try
            {
                ValidateOscAddress(address);
                var args = OscArguments(argumentsJson);
                if (args.Count == 0 || !(args[0] is bool || args[0] is int || args[0] is float))
                    throw new ArgumentException("GM parameter input requires a Bool, Int or Float as the first argument.");
                if (OscField(module, "DummyMode") != null)
                    throw new ArgumentException("GM OSC input is unavailable while a dummy/pose/test-animation mode is active.");
                var osc = OscController(module);
                var settings = (OscSettings)OscField(osc, "_settings");
                if (!OscEnabled(osc)) PrepareOscSettings(settings);
                var input = (IDictionary)OscField(OscField(settings, "_data"), "Input");
                if (!input.Contains(address))
                    throw new ArgumentException($"OSC address '{address}' has no GM input mapping. Use GestureManagerGetOscState to inspect addresses.");
                var inputType = (AnimatorControllerParameterType)OscField(input[address], "Item1");
                if (inputType == AnimatorControllerParameterType.Int && args[0] is float number &&
                    ((double)number < int.MinValue || (double)number > int.MaxValue))
                    throw new ArgumentException("OSC Float is outside the destination parameter's 32-bit integer range.");
                var name = OscParameterName(module, settings, address);
                if (name == null || !module.Params.ContainsKey(name))
                    throw new ArgumentException($"OSC address '{address}' maps to a parameter not present in the current GM preview.");
                var before = module.Params[name].FloatValue();
                OscCall(osc, "OnMessage", new[] { typeof(OscPacket.Message) }, new OscPacket.Message(address, args));
                return JNode.Obj(("success", JNode.Bool(true)), ("address", JNode.Str(address)),
                    ("parameter", JNode.Str(name)), ("before", OscValueNode(before)),
                    ("after", OscValueNode(module.Params[name].FloatValue())), ("argumentCount", JNode.Num(args.Count))).ToJson();
            }
            catch (Exception ex) { return OscError(ex); }
#endif
        }

        [AgentTool(@"Send one OSC message through Gesture Manager's currently running UDP sender.
Requires GestureManagerStartOsc. Sends to its configured destination; GM parameters are changed
only if the destination sends data back. argumentsJson is a JSON scalar or array: [true, 2, 0.5, ""text""].
Whole numbers use OSC Int, other numbers use Float. To force a type use {""type"":""Float"",""value"":1}.
Explicit types are Bool, Int, Float, String and Char. Arguments and addresses must fit OSC ASCII encoding.")]
        public static string GestureManagerSendOscMessage(string address, string argumentsJson)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            try
            {
                ValidateOscAddress(address);
                var args = OscArguments(argumentsJson);
                var osc = OscController(module);
                if (!OscEnabled(osc)) return "Error: Start Gesture Manager OSC before sending UDP messages.";
                var bytes = new OscPacket.Message(address, args).GetBytes();
                if (bytes.Length > 65507) throw new ArgumentException("OSC message exceeds the UDP payload limit (65507 bytes).");
                ((UdpSender)OscField(osc, "_sender")).Send(bytes);
                return $"Success: Sent OSC message '{address}' with {args.Count} arguments ({bytes.Length} bytes).";
            }
            catch (Exception ex) { return OscError(ex); }
#endif
        }

        [AgentTool(@"Send an OSC bundle using Gesture Manager's running UDP sender, matching its Send Bundle UI.
messagesJson is an array: [{""address"":""/avatar/parameters/A"",""arguments"":[1]}, ...].
Arguments support the same scalar types and explicit type objects as GestureManagerSendOscMessage.
timeTag is the OSC unsigned 64-bit NTP timetag as a decimal string; '1' means immediately.
All messages are validated before sending one UDP packet.")]
        public static string GestureManagerSendOscBundle(string messagesJson, string timeTag = "1")
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out _, out var module, out var error)) return error;
            try
            {
                if (!ulong.TryParse(timeTag, NumberStyles.None, CultureInfo.InvariantCulture, out var tag))
                    throw new ArgumentException("timeTag must be an unsigned 64-bit decimal integer string.");
                var json = JNode.Parse(messagesJson);
                if (json.Type != JNode.JType.Array || json.Count == 0 || json.Count > 256)
                    throw new ArgumentException("messagesJson must contain 1..256 messages.");
                var messages = new List<OscPacket.Message>();
                foreach (var item in json.AsArray)
                {
                    var address = item["address"].AsString;
                    ValidateOscAddress(address);
                    if (!item.Has("arguments")) throw new ArgumentException("Each message must have an arguments field.");
                    messages.Add(new OscPacket.Message(address, OscArguments(item["arguments"].ToJson())));
                }
                var osc = OscController(module);
                if (!OscEnabled(osc)) return "Error: Start Gesture Manager OSC before sending UDP bundles.";
                var packet = (OscPacket)Activator.CreateInstance(typeof(OscPacket), OscBinding, null,
                    new object[] { tag, messages }, CultureInfo.InvariantCulture);
                var bytes = packet.GetBytes();
                if (bytes.Length > 65507) throw new ArgumentException("OSC bundle exceeds the UDP payload limit (65507 bytes).");
                ((UdpSender)OscField(osc, "_sender")).Send(bytes);
                return $"Success: Sent OSC bundle with {messages.Count} messages ({bytes.Length} bytes, timeTag={tag}).";
            }
            catch (Exception ex) { return OscError(ex); }
#endif
        }

#if GESTURE_MANAGER
        private const BindingFlags OscBinding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        // Keep the mapping associated with GM's current data object, so changing/deleting a
        // config file cannot change the meaning of messages on an already running receiver.
        private static readonly ConditionalWeakTable<object, OscFile> OscLoadedFiles = new ConditionalWeakTable<object, OscFile>();

        private static FieldInfo OscFieldInfo(object target, string name) =>
            target?.GetType().GetField(name, OscBinding) ?? throw new NotSupportedException($"GM OSC field '{name}' is unavailable in this package version.");
        private static object OscField(object target, string name) => OscFieldInfo(target, name).GetValue(target);
        private static void OscSetField(object target, string name, object value) => OscFieldInfo(target, name).SetValue(target, value);
        private static MethodInfo OscMethod(object target, string name, Type[] args) =>
            target?.GetType().GetMethod(name, OscBinding, null, args, null) ?? throw new NotSupportedException($"GM OSC method '{name}' is unavailable in this package version.");
        private static object OscCall(object target, string name, Type[] types, params object[] args) => OscMethod(target, name, types).Invoke(target, args);
        private static OscModule OscController(ModuleVrc3 module) => (OscModule)OscField(module, "OscModule");
        private static bool OscEnabled(OscModule osc) => OscField(osc, "_listener") != null && OscField(osc, "_sender") != null;
        private static string OscError(Exception ex)
        {
            while (ex is TargetInvocationException invocation && invocation.InnerException != null) ex = invocation.InnerException;
            return $"Error: Gesture Manager OSC: {ex.Message}";
        }

        private static void PrepareOscSettings(OscSettings settings)
        {
            if (!settings.Loaded) { settings.Setup(); return; }
            // GM's file-based Setup() shows a modal error. Parse and validate before invoking
            // its typed overload so malformed files never block a remote caller with a dialog.
            var file = ReadOscFile((string)OscField(settings, "_filePathString"));
            var dataType = OscFieldInfo(settings, "_data").FieldType;
            var previous = OscField(settings, "_data");
            try
            {
                OscSetField(settings, "_data", Activator.CreateInstance(dataType, true));
                OscCall(settings, "CustomSetup", new[] { typeof(OscFile) }, file);
                OscLoadedFiles.Add(OscField(settings, "_data"), file);
            }
            catch { OscSetField(settings, "_data", previous); throw; }
        }

        private static OscFile ReadOscFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("No OSC configuration file selected.");
            var text = File.ReadAllText(path);
            var root = JNode.Parse(text);
            if (root.Type != JNode.JType.Object || root["parameters"].Type != JNode.JType.Array)
                throw new ArgumentException("OSC settings must be valid JSON with a parameters array.");
            var file = new OscFile { parameters = new Parameter[root["parameters"].Count] };
            var inputAddresses = new HashSet<string>(StringComparer.Ordinal);
            var outputNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < file.parameters.Length; i++)
            {
                var item = root["parameters"][i];
                var name = item["name"].AsString;
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException($"OSC parameter [{i}] has no name.");
                var input = OscEndpoint(item["input"]);
                var output = OscEndpoint(item["output"]);
                if (input.address != null && !inputAddresses.Add(input.address))
                    throw new ArgumentException($"Duplicate OSC input address '{input.address}'.");
                if (output.address != null && !outputNames.Add(name))
                    throw new ArgumentException($"Duplicate OSC output parameter '{name}'.");
                file.parameters[i] = new Parameter { name = name, input = input, output = output };
            }
            return file;
        }

        private static Data OscEndpoint(JNode endpoint)
        {
            if (endpoint.IsNull) return default;
            if (endpoint.Type != JNode.JType.Object) throw new ArgumentException("OSC input/output must be an object or null.");
            if (endpoint["address"].IsNull) return default;
            var address = endpoint["address"].AsString;
            ValidateOscAddress(address);
            var type = endpoint["type"].AsString;
            if (type != "Bool" && type != "Int" && type != "Float")
                throw new ArgumentException($"OSC endpoint '{address}' type must be Bool, Int or Float.");
            return new Data { address = address, type = type };
        }

        private static OscFile OscConfiguredFile(OscSettings settings)
        {
            var data = OscField(settings, "_data");
            return data != null && OscLoadedFiles.TryGetValue(data, out var file)
                ? file : ReadOscFile((string)OscField(settings, "_filePathString"));
        }

        private static string OscParameterName(ModuleVrc3 module, OscSettings settings, string address)
        {
            if (settings.Loaded)
                return OscConfiguredFile(settings).parameters
                    .FirstOrDefault(p => p.input.address == address).name;
            const string prefix = "/avatar/parameters/";
            return address.StartsWith(prefix, StringComparison.Ordinal) ? address.Substring(prefix.Length) : null;
        }

        private static JNode OscState(ModuleVrc3 module, int offset = 0, int limit = GestureManagerPaging.DefaultLimit,
            string section = "all", int argumentOffset = 0, int argumentLimit = 16)
        {
            var osc = OscController(module);
            var settings = (OscSettings)OscField(osc, "_settings");
            var listener = OscField(osc, "_listener");
            var sender = (UdpSender)OscField(osc, "_sender");
            var mappings = new List<JNode>();
            string settingsError = null;
            if (settings.Loaded)
            {
                try { ReadOscFile((string)OscField(settings, "_filePathString")); }
                catch (Exception ex) { settingsError = ex.Message; }
                OscFile file = null;
                try { file = OscConfiguredFile(settings); }
                catch (Exception ex) { settingsError = ex.Message; }
                if (file != null && (section == "all" || section == "mappings")) foreach (var p in file.parameters
                    .OrderBy(p => p.name, StringComparer.Ordinal).ThenBy(p => p.input.address, StringComparer.Ordinal)
                    .ThenBy(p => p.output.address, StringComparer.Ordinal))
                    mappings.Add(JNode.Obj(("parameter", JNode.Str(p.name)), ("available", JNode.Bool(module.Params.ContainsKey(p.name))),
                        ("inputAddress", OscNullableString(p.input.address)), ("inputType", OscNullableString(p.input.type)),
                        ("outputAddress", OscNullableString(p.output.address)), ("outputType", OscNullableString(p.output.type))));
            }
            else if (section == "all" || section == "mappings") foreach (var pair in module.Params.OrderBy(p => p.Key, StringComparer.Ordinal))
                mappings.Add(JNode.Obj(("parameter", JNode.Str(pair.Key)), ("available", JNode.Bool(true)),
                    ("inputAddress", JNode.Str("/avatar/parameters/" + pair.Key)), ("inputType", JNode.Str(pair.Value.Type.ToString())),
                    ("outputAddress", JNode.Str("/avatar/parameters/" + pair.Key)), ("outputType", JNode.Str(pair.Value.Type.ToString()))));
            var port = listener == null ? JNode.NullNode : JNode.Num((int)(listener.GetType().GetProperty("Port", OscBinding)?.GetValue(listener) ?? 0));
            var state = JNode.Obj(("avatar", JNode.Str(module.Name)), ("enabled", JNode.Bool(OscEnabled(osc))),
                ("listenPort", port), ("sendAddress", OscNullableString(sender?.Address)),
                ("sendPort", sender == null ? JNode.NullNode : JNode.Num(sender.Port)),
                ("settingsLoaded", JNode.Bool(settings.Loaded)), ("settingsStable", JNode.Bool(settings.Stable && settingsError == null)),
                ("settingsError", OscNullableString(settingsError)),
                ("settingsPath", settings.Loaded ? OscNullableString((string)OscField(settings, "_filePathString")) : JNode.NullNode),
                ("section", JNode.Str(section)));
            if (section == "all" || section == "mappings")
            {
                var page = GestureManagerPaging.Page(mappings, offset, limit);
                state.AsObject["mappings"] = page["items"];
                state.AsObject["mappingsPage"] = page["page"];
            }
            if (section == "all" || section == "received")
            {
                // GM moves an endpoint to the beginning of its chronological list on every
                // message. Address order gives stable pages while existing endpoints update.
                var controls = ((IEnumerable)OscField(osc, "_chronological")).Cast<object>()
                    .Select((control, index) => new { Control = control, SourceIndex = index,
                        Address = (string)OscField(control, "HorizontalEndpoint") })
                    .OrderBy(entry => entry.Address, StringComparer.Ordinal).ToArray();
                var received = new List<JNode>();
                foreach (var entry in controls.Skip(offset).Take(limit))
                {
                    var values = ((IEnumerable)OscField(entry.Control, "Values")).Cast<object>().ToArray();
                    var arguments = values.Skip(argumentOffset).Take(argumentLimit)
                        .Select(tuple => OscReceivedValueNode(OscField(tuple, "Item2"))).ToArray();
                    received.Add(JNode.Obj(("address", JNode.Str(entry.Address)), ("sourceIndex", JNode.Num(entry.SourceIndex)),
                        ("arguments", JNode.Arr(arguments)), ("argumentsPage", GestureManagerPaging.Metadata(
                            values.Length, argumentOffset, argumentLimit, arguments.Length))));
                }
                state.AsObject["received"] = JNode.Arr(received.ToArray());
                state.AsObject["receivedPage"] = GestureManagerPaging.Metadata(controls.Length, offset, limit, received.Count);
            }
            return state;
        }

        private static JNode OscNullableString(string value) => value == null ? JNode.NullNode : JNode.Str(value);
        private static JNode OscReceivedValueNode(object value)
        {
            const int maxPreviewLength = 256;
            if (value is string text && text.Length > maxPreviewLength)
                return JNode.Obj(("valuePreview", JNode.Str(text.Substring(0, maxPreviewLength))),
                    ("length", JNode.Num(text.Length)), ("truncated", JNode.Bool(true)));
            return OscValueNode(value);
        }
        private static JNode OscValueNode(object value)
        {
            if (value == null) return JNode.NullNode;
            if (value is bool b) return JNode.Bool(b);
            if (value is float f) return float.IsNaN(f) || float.IsInfinity(f) ? JNode.Str(f.ToString(CultureInfo.InvariantCulture)) : JNode.Num(f);
            if (value is int i) return JNode.Num(i);
            return JNode.Str(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
#endif

        // Kept independent of GM so JSON/packet validation can be checked without the package.
        private static void ValidateOscAddress(string address)
        {
            if (string.IsNullOrEmpty(address) || address[0] != '/' || address.Length > 4096 ||
                address.Any(c => c < 33 || c > 126 || "#*,?[]{}".IndexOf(c) >= 0))
                throw new ArgumentException("OSC address must be an ASCII path beginning with '/', without spaces, wildcards or control characters (max 4096 characters).");
        }

        private static List<object> OscArguments(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65507)
                throw new ArgumentException("argumentsJson must be nonempty JSON (max 65507 characters).");
            var root = JNode.Parse(json);
            if (root.IsNull) throw new ArgumentException("argumentsJson is invalid or null; use [] for no arguments.");
            var nodes = root.Type == JNode.JType.Array ? root.AsArray : new List<JNode> { root };
            if (nodes.Count > 256) throw new ArgumentException("An OSC message supports up to 256 arguments.");
            return nodes.Select(OscArgument).ToList();
        }

        private static object OscArgument(JNode node)
        {
            string type = null;
            if (node.Type == JNode.JType.Object)
            {
                type = node["type"].AsString;
                if (type == null || !node.Has("value")) throw new ArgumentException("Typed OSC arguments require type and value.");
                node = node["value"];
            }
            if ((type == null || type == "Bool") && node.Type == JNode.JType.Bool) return node.AsBool;
            if ((type == null || type == "String" || type == "Char") && node.Type == JNode.JType.String)
            {
                var text = node.AsString;
                if (text.Any(c => c == '\0' || c > 127)) throw new ArgumentException("GM's OSC serializer requires ASCII string/char arguments without NUL.");
                if (type != "Char") return text;
                if (text.Length == 1) return text[0];
                throw new ArgumentException("OSC Char requires one ASCII character.");
            }
            if ((type == null || type == "Int" || type == "Float") && node.Type == JNode.JType.Number)
            {
                var number = node.AsNumber;
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new ArgumentException("OSC numbers must be finite.");
                if (type == "Int" || type == null && number == Math.Truncate(number) && number >= int.MinValue && number <= int.MaxValue)
                {
                    if (number != Math.Truncate(number) || number < int.MinValue || number > int.MaxValue)
                        throw new ArgumentException("OSC Int requires a 32-bit integer.");
                    return (int)number;
                }
                var value = (float)number;
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("OSC Float is outside the finite 32-bit float range.");
                return value;
            }
            throw new ArgumentException("OSC arguments must be Bool, Int, Float, String or Char; the explicit type must match the JSON value.");
        }
    }
}

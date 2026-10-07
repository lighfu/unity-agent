using AjisaiFlow.UnityAgent.SDK;

#if GESTURE_MANAGER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AjisaiFlow.UnityAgent.Editor.MCP;
using BlackStartX.GestureManager.Editor.Modules.Vrc3;
using BlackStartX.GestureManager.Editor.Modules.Vrc3.Params;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using MenuControl = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;
#endif

namespace AjisaiFlow.UnityAgent.Editor.Tools
{
    public static partial class GestureManagerTools
    {
        [AgentTool(@"Read one bounded page of the ExpressionMenu graph as JSON, including submenu links, controls, icons, labels and parameter bindings.
avatarName: scene avatar name/path; empty uses the active GestureManager VRC3 preview. An explicit avatar can be inspected without starting a preview.
menuPath: optional path to a SubMenu control. Paths are slash-separated zero-based control indexes (e.g. '0/2'); exact names also work unless duplicated. Use name:123 to select a numeric name. Index paths work for names containing '/'.
The menus array is a graph: submenuId references another menu node. Shared menus and cycles are preserved without infinite recursion. firstPath/controlPath give one canonical route from the avatar root, not every route.
offset/limit: offset is a zero-based structural record offset, limit defaults to 50 and must be 1..200. Records are ordered breadth-first by menu, then menu header, each control header, its subParameter slots and labels. Even empty menus and null controls have headers. Continue with page.nextOffset until page.hasMore is false.
menus/controls can be fragments across pages: merge by menu id/control index, headerIncluded identifies the one header record, subParameters.slot and labels.index identify array entries. Primary parameter and parent identity are constant-size context on each fragment.
maxMenus/maxControls: optional additional caps on menu/control fragments in this page; 0 disables that extra cap, never the limit cap. totalMenus/totalControls remain exact. Runtime values are included only for the active preview. Keep arguments unchanged while paging and restart from offset 0 if the menu graph changes.
This reads the avatar's Expressions asset, not GM's built-in Options/Looks/Clones/Tools wheel.", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetExpressionMenu(string avatarName = "", string menuPath = "", int maxMenus = 0, int maxControls = 0, int offset = 0, int limit = GestureManagerPaging.DefaultLimit)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            var pagingError = GestureManagerPaging.Validate(offset, limit);
            if (pagingError != null) return pagingError;
            if (maxMenus < 0 || maxControls < 0) return "Error: maxMenus and maxControls must be zero (no extra cap) or positive.";
            if (!TryGetMenuAvatar(avatarName, out var avatar, out var module, out var error)) return error;
            var root = avatar.expressionsMenu;
            string prefix = "";
            if (!string.IsNullOrEmpty(menuPath))
            {
                if (!TryResolveMenuControl(root, menuPath, out var selected, out prefix, out error)) return error;
                if (selected.type != MenuControl.ControlType.SubMenu || selected.subMenu == null)
                    return $"Error: '{prefix}' is not a SubMenu with a menu asset.";
                root = selected.subMenu;
            }

            var graph = CollectExpressionMenus(root, prefix);
            int totalControls = graph.Sum(entry => entry.Menu.controls?.Count ?? 0);
            var records = CollectExpressionMenuRecords(graph);
            var menus = ExpressionMenuPage(avatar, module, records, offset, limit, maxMenus, maxControls, out var returned);
            int includedMenus = menus.AsArray.Count(menu => menu["headerIncluded"].AsBool);
            int includedControls = menus.AsArray.Sum(menu => menu["controls"].AsArray.Count(control => control["headerIncluded"].AsBool));
            return JNode.Obj(
                ("avatar", JNode.Str(avatar.gameObject.name)),
                ("rootMenuId", JNode.Str(ExpressionMenuId(root))),
                ("runtimeAvailable", JNode.Bool(module != null)),
                ("totalMenus", JNode.Num(graph.Count)),
                ("totalControls", JNode.Num(totalControls)),
                ("omittedMenus", JNode.Num(graph.Count - includedMenus)),
                ("omittedControls", JNode.Num(totalControls - includedControls)),
                ("truncated", JNode.Bool(offset > 0 || returned < records.Count)),
                ("menus", menus),
                ("page", GestureManagerPaging.Metadata(records.Count, offset, limit, returned))).ToJson();
#endif
        }

        [AgentTool(@"Read the parameter names used by ExpressionMenu controls as JSON, with ExpressionParameters definitions and current GestureManager values when available.
controlPath empty: unique parameters are sorted by ordinal name; each parameter header and then each control/slot usage is one paging record. Shared/cyclic menu assets are visited once. Parameter fragments repeat identity/definition context; merge by name, headerIncluded and usages.usageIndex. count is the exact unique parameter count, totalUsages is exact for each parameter.
controlPath specified: one control header, followed by its subParameter slots and labels, is the record order. Includes empty/unbound slots; merge by headerIncluded, subParameters.slot and labels.index.
offset is the zero-based record offset; limit defaults to 50 and must be 1..200. page.total counts records, not unique parameters. Continue with page.nextOffset until page.hasMore is false. Keep arguments unchanged and restart at offset 0 after menu changes.
Paths use zero-based control indexes separated by '/' (e.g. '0/2') or exact unique names. avatarName empty uses the active VRC3 preview; an explicit scene avatar may be read without a preview.", Risk = ToolRisk.Safe)]
        public static string GestureManagerGetExpressionMenuParameters(string controlPath = "", string avatarName = "", int offset = 0, int limit = GestureManagerPaging.DefaultLimit)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            var pagingError = GestureManagerPaging.Validate(offset, limit);
            if (pagingError != null) return pagingError;
            if (!TryGetMenuAvatar(avatarName, out var avatar, out var module, out var error)) return error;
            if (!string.IsNullOrEmpty(controlPath))
            {
                if (!TryResolveMenuControl(avatar.expressionsMenu, controlPath, out var control, out var canonicalPath, out error)) return error;
                int index = int.Parse(canonicalPath.Substring(canonicalPath.LastIndexOf('/') + 1), CultureInfo.InvariantCulture);
                var records = new List<ExpressionMenuRecord>();
                AddExpressionControlRecords(records, null, control, index);
                var result = ExpressionMenuControlJson(avatar, module, control, index, canonicalPath);
                int returned = offset >= records.Count ? 0 : Math.Min(limit, records.Count - offset);
                for (int i = 0; i < returned; i++) ApplyExpressionControlRecord(result, avatar, module, records[offset + i]);
                result.AsObject["omittedSubParameters"] = JNode.Num(result["totalSubParameters"].AsInt - result["subParameters"].Count);
                result.AsObject["omittedLabels"] = JNode.Num(result["totalLabels"].AsInt - result["labels"].Count);
                result.AsObject["page"] = GestureManagerPaging.Metadata(records.Count, offset, limit, returned);
                return result.ToJson();
            }

            var parameters = new Dictionary<string, JNode>(StringComparer.Ordinal);
            foreach (var entry in CollectExpressionMenus(avatar.expressionsMenu, ""))
            {
                if (entry.Menu.controls == null) continue;
                for (int i = 0; i < entry.Menu.controls.Count; i++)
                {
                    var control = entry.Menu.controls[i];
                    if (control == null) continue;
                    var path = MenuControlPath(entry.Path, i);
                    AddMenuParameterUsage(parameters, avatar, module, control.parameter?.name, path, control, "primary", -1);
                    if (control.subParameters == null) continue;
                    for (int s = 0; s < control.subParameters.Length; s++)
                        AddMenuParameterUsage(parameters, avatar, module, control.subParameters[s]?.name, path, control, "subParameter", s);
                }
            }
            var sorted = parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();
            int totalRecords = sorted.Sum(parameter => 1 + parameter["usages"].Count);
            var pageParameters = ExpressionParameterPage(sorted, offset, limit, out var parameterRecordsReturned);
            return JNode.Obj(
                ("avatar", JNode.Str(avatar.gameObject.name)),
                ("runtimeAvailable", JNode.Bool(module != null)),
                ("count", JNode.Num(parameters.Count)),
                ("returnedParameters", JNode.Num(pageParameters.Count)),
                ("parameters", pageParameters),
                ("page", GestureManagerPaging.Metadata(totalRecords, offset, limit, parameterRecordsReturned))).ToJson();
#endif
        }

        [AgentTool(@"Emulate an ExpressionMenu control on the active GestureManager VRC3 preview using GM's parameter Set API (including change handlers and OSC output).
controlPath: zero-based index path such as '0/2', or exact unique names separated by '/'. Path resolution does not activate ancestor SubMenus; call their enter/exit actions explicitly when their gate parameters matter.
Button: activate/press holds control.value; release writes 0. Hold across editor updates to let the animator observe the press, then call release.
Toggle: activate/toggle flips between control.value and 0; on/off explicitly selects a state.
SubMenu: activate/enter writes its primary gate parameter; exit writes 0. These emulate parameter effects and do not navigate an Inspector wheel.
RadialPuppet: activate/open sets its primary gate; set writes value [0,1] to subParameters[0]; release/close clears only the gate.
TwoAxisPuppet/FourAxisPuppet: set takes x/y [-1,1], normalized to the unit circle. TwoAxis writes X,Y; FourAxis writes Up,Right,Down,Left. set also opens the gate; release/close clears it and preserves subparameter values.
All referenced runtime parameters and input values are validated before changing any parameter. Empty optional parameter slots are ignored. Menu assets are not modified.", Risk = ToolRisk.Caution)]
        public static string GestureManagerSetExpressionMenuControl(string controlPath, string action = "activate", float value = 0, float x = 0, float y = 0)
        {
#if !GESTURE_MANAGER
            return "Error: Gesture Manager package not installed.";
#else
            if (!TryGetVrc3(out var gm, out var module, out var error)) return error;
            var avatar = module.AvatarDescriptor;
            if (avatar.expressionsMenu == null) return "Error: The active avatar has no ExpressionMenu asset.";
            if (!TryResolveMenuControl(avatar.expressionsMenu, controlPath, out var control, out var canonicalPath, out error)) return error;
            action = (action ?? "activate").ToLowerInvariant();
            var writes = new List<KeyValuePair<Vrc3Param, float>>();
            float mainValue;
            var type = control.type;
            switch (type)
            {
                case MenuControl.ControlType.Button:
                    if (action != "activate" && action != "press" && action != "release") return "Error: Button action must be activate, press or release.";
                    mainValue = action == "release" ? 0 : control.value;
                    if (!AddMenuWrite(writes, module, control.parameter?.name, mainValue, true, out error)) return error;
                    break;
                case MenuControl.ControlType.Toggle:
                    if (action != "activate" && action != "toggle" && action != "on" && action != "off") return "Error: Toggle action must be activate, toggle, on or off.";
                    if (!AddMenuWrite(writes, module, control.parameter?.name, 0, true, out error)) return error;
                    mainValue = action == "off" ? 0 : action == "on" ? control.value : writes[0].Key.FloatValue() == control.value ? 0 : control.value;
                    if (!MenuFinite(mainValue)) return "Error: The menu control value must be finite.";
                    writes[0] = new KeyValuePair<Vrc3Param, float>(writes[0].Key, mainValue);
                    break;
                case MenuControl.ControlType.SubMenu:
                    if (action != "activate" && action != "enter" && action != "exit") return "Error: SubMenu action must be activate, enter or exit.";
                    if (control.subMenu == null) return "Error: SubMenu control has no submenu asset.";
                    mainValue = action == "exit" ? 0 : control.value;
                    if (!AddMenuWrite(writes, module, control.parameter?.name, mainValue, false, out error)) return error;
                    break;
                case MenuControl.ControlType.RadialPuppet:
                case MenuControl.ControlType.TwoAxisPuppet:
                case MenuControl.ControlType.FourAxisPuppet:
                    if (action != "activate" && action != "open" && action != "set" && action != "release" && action != "close")
                        return "Error: Puppet action must be activate, open, set, release or close.";
                    mainValue = action == "release" || action == "close" ? 0 : control.value;
                    if (!AddMenuWrite(writes, module, control.parameter?.name, mainValue, false, out error)) return error;
                    if (action == "set")
                    {
                        float[] subValues;
                        if (type == MenuControl.ControlType.RadialPuppet)
                        {
                            if (!MenuFinite(value) || value < 0 || value > 1) return "Error: RadialPuppet value must be finite and in [0,1].";
                            subValues = new[] { value };
                        }
                        else
                        {
                            if (!MenuFinite(x) || !MenuFinite(y) || x < -1 || x > 1 || y < -1 || y > 1)
                                return "Error: Axis x and y must be finite and in [-1,1].";
                            var axis = Vector2.ClampMagnitude(new Vector2(x, y), 1);
                            subValues = type == MenuControl.ControlType.TwoAxisPuppet
                                ? new[] { axis.x, axis.y }
                                : new[] { Mathf.Max(axis.y, 0), Mathf.Max(axis.x, 0), Mathf.Max(-axis.y, 0), Mathf.Max(-axis.x, 0) };
                        }
                        if (control.subParameters == null || control.subParameters.Length < subValues.Length)
                            return $"Error: {type} needs {subValues.Length} subParameter slots; this menu has {control.subParameters?.Length ?? 0}.";
                        bool bound = false;
                        for (int i = 0; i < subValues.Length; i++)
                        {
                            var name = control.subParameters[i]?.name;
                            bound |= !string.IsNullOrEmpty(name);
                            if (!AddMenuWrite(writes, module, name, subValues[i], false, out error)) return error;
                        }
                        if (!bound) return "Error: Puppet has no bound subParameters.";
                    }
                    break;
                default: return $"Error: Unsupported ExpressionMenu control type '{type}'.";
            }

            var changes = JNode.Arr();
            foreach (var write in writes)
            {
                var oldValue = MenuRuntimeValue(write.Key);
                write.Key.Set(module, write.Value);
                changes.AsArray.Add(JNode.Obj(("name", JNode.Str(write.Key.Name)), ("before", oldValue), ("after", MenuRuntimeValue(write.Key))));
            }
            return JNode.Obj(
                ("success", JNode.Bool(true)),
                ("controlPath", JNode.Str(canonicalPath)),
                ("type", JNode.Str(type.ToString())),
                ("action", JNode.Str(action)),
                ("changes", changes)).ToJson();
#endif
        }

#if GESTURE_MANAGER
        private sealed class ExpressionMenuEntry
        {
            internal VRCExpressionsMenu Menu;
            internal string Path;
        }

        private enum ExpressionMenuRecordKind { Menu, Control, SubParameter, Label }

        private sealed class ExpressionMenuRecord
        {
            internal ExpressionMenuEntry Menu;
            internal MenuControl Control;
            internal int ControlIndex;
            internal ExpressionMenuRecordKind Kind;
            internal int Slot;
        }

        // Page records include collection entries, rather than just their parents, so a
        // single large menu/control cannot hide an unbounded array in one result item.
        private static List<ExpressionMenuRecord> CollectExpressionMenuRecords(List<ExpressionMenuEntry> graph)
        {
            var records = new List<ExpressionMenuRecord>();
            foreach (var entry in graph)
            {
                records.Add(new ExpressionMenuRecord { Menu = entry, Kind = ExpressionMenuRecordKind.Menu });
                if (entry.Menu.controls == null) continue;
                for (int i = 0; i < entry.Menu.controls.Count; i++)
                    AddExpressionControlRecords(records, entry, entry.Menu.controls[i], i);
            }
            return records;
        }

        private static void AddExpressionControlRecords(List<ExpressionMenuRecord> records, ExpressionMenuEntry menu, MenuControl control, int index)
        {
            records.Add(new ExpressionMenuRecord { Menu = menu, Control = control, ControlIndex = index, Kind = ExpressionMenuRecordKind.Control });
            if (control == null) return;
            for (int s = 0; s < (control.subParameters?.Length ?? 0); s++)
                records.Add(new ExpressionMenuRecord { Menu = menu, Control = control, ControlIndex = index, Kind = ExpressionMenuRecordKind.SubParameter, Slot = s });
            for (int l = 0; l < (control.labels?.Length ?? 0); l++)
                records.Add(new ExpressionMenuRecord { Menu = menu, Control = control, ControlIndex = index, Kind = ExpressionMenuRecordKind.Label, Slot = l });
        }

        private static JNode ExpressionMenuPage(VRCAvatarDescriptor avatar, ModuleVrc3 module, List<ExpressionMenuRecord> records, int offset, int limit, int maxMenus, int maxControls, out int returned)
        {
            var menus = JNode.Arr();
            returned = 0;
            if (offset >= records.Count) return menus;
            var available = Math.Min(limit, records.Count - offset);
            ExpressionMenuEntry previousMenu = null;
            JNode menu = null;
            JNode control = null;
            int previousControl = -1;
            int controlFragments = 0;
            for (int i = 0; i < available; i++)
            {
                var record = records[offset + i];
                bool newMenu = previousMenu != record.Menu;
                bool hasControl = record.Kind != ExpressionMenuRecordKind.Menu;
                bool newControl = hasControl && (newMenu || previousControl != record.ControlIndex);
                if (newMenu && maxMenus > 0 && menus.Count >= maxMenus) break;
                if (newControl && maxControls > 0 && controlFragments >= maxControls) break;
                if (newMenu)
                {
                    previousMenu = record.Menu;
                    previousControl = -1;
                    menu = JNode.Obj(
                        ("id", JNode.Str(ExpressionMenuId(record.Menu.Menu))),
                        ("name", JNode.Str(record.Menu.Menu.name)),
                        ("assetPath", JNode.Str(AssetDatabase.GetAssetPath(record.Menu.Menu))),
                        ("firstPath", JNode.Str(record.Menu.Path)),
                        ("headerIncluded", JNode.Bool(false)),
                        ("totalControls", JNode.Num(record.Menu.Menu.controls?.Count ?? 0)),
                        ("controls", JNode.Arr()));
                    menus.AsArray.Add(menu);
                }
                if (record.Kind == ExpressionMenuRecordKind.Menu) menu.AsObject["headerIncluded"] = JNode.Bool(true);
                else
                {
                    if (newControl)
                    {
                        previousControl = record.ControlIndex;
                        control = ExpressionMenuControlJson(avatar, module, record.Control, record.ControlIndex, MenuControlPath(record.Menu.Path, record.ControlIndex));
                        menu["controls"].AsArray.Add(control);
                        controlFragments++;
                    }
                    ApplyExpressionControlRecord(control, avatar, module, record);
                }
                returned++;
            }
            foreach (var fragment in menus.AsArray)
            {
                fragment.AsObject["omittedControls"] = JNode.Num(fragment["totalControls"].AsInt - fragment["controls"].AsArray.Count(item => item["headerIncluded"].AsBool));
                foreach (var item in fragment["controls"].AsArray)
                {
                    item.AsObject["omittedSubParameters"] = JNode.Num(item["totalSubParameters"].AsInt - item["subParameters"].Count);
                    item.AsObject["omittedLabels"] = JNode.Num(item["totalLabels"].AsInt - item["labels"].Count);
                }
            }
            return menus;
        }

        private static void ApplyExpressionControlRecord(JNode result, VRCAvatarDescriptor avatar, ModuleVrc3 module, ExpressionMenuRecord record)
        {
            if (record.Kind == ExpressionMenuRecordKind.Control) result.AsObject["headerIncluded"] = JNode.Bool(true);
            else if (record.Kind == ExpressionMenuRecordKind.SubParameter)
            {
                var slot = MenuParameterJson(avatar, module, record.Control.subParameters[record.Slot]?.name);
                slot.AsObject["slot"] = JNode.Num(record.Slot);
                result["subParameters"].AsArray.Add(slot);
            }
            else if (record.Kind == ExpressionMenuRecordKind.Label)
            {
                var label = record.Control.labels[record.Slot];
                result["labels"].AsArray.Add(JNode.Obj(
                    ("index", JNode.Num(record.Slot)),
                    ("name", JNode.Str(label.name)),
                    ("icon", JNode.Str(label.icon == null ? "" : AssetDatabase.GetAssetPath(label.icon)))));
            }
        }

        private static JNode ExpressionParameterPage(List<JNode> parameters, int offset, int limit, out int returned)
        {
            var result = JNode.Arr();
            returned = 0;
            int recordOffset = 0;
            foreach (var parameter in parameters)
            {
                int recordCount = 1 + parameter["usages"].Count;
                if (offset >= recordOffset && offset - recordOffset >= recordCount)
                {
                    recordOffset += recordCount;
                    continue;
                }
                if (returned >= limit) break;
                var fragment = JNode.Obj(parameter.AsObject.Where(kv => kv.Key != "usages").Select(kv => (kv.Key, kv.Value)).ToArray());
                fragment.AsObject["headerIncluded"] = JNode.Bool(false);
                fragment.AsObject["totalUsages"] = JNode.Num(parameter["usages"].Count);
                fragment.AsObject["usages"] = JNode.Arr();
                int start = Math.Max(0, offset - recordOffset);
                for (int i = start; i < recordCount && returned < limit; i++)
                {
                    if (i == 0) fragment.AsObject["headerIncluded"] = JNode.Bool(true);
                    else fragment["usages"].AsArray.Add(parameter["usages"][i - 1]);
                    returned++;
                }
                fragment.AsObject["omittedUsages"] = JNode.Num(parameter["usages"].Count - fragment["usages"].Count);
                result.AsArray.Add(fragment);
                recordOffset += recordCount;
            }
            return result;
        }

        private static bool TryGetMenuAvatar(string avatarName, out VRCAvatarDescriptor avatar, out ModuleVrc3 module, out string error)
        {
            avatar = null;
            module = null;
            error = null;
            if (string.IsNullOrEmpty(avatarName))
            {
                if (!TryGetVrc3(out var gm, out module, out error)) return false;
                avatar = module.AvatarDescriptor;
            }
            else
            {
                var go = FindGO(avatarName);
                if (go == null) { error = $"Error: Avatar '{avatarName}' not found."; return false; }
                avatar = go.GetComponent<VRCAvatarDescriptor>();
                if (avatar == null) { error = $"Error: '{avatarName}' has no VRC3 AvatarDescriptor."; return false; }
                var active = FindInstance()?.Module as ModuleVrc3;
                if (active != null && active.Active && active.AvatarDescriptor == avatar) module = active;
            }
            if (avatar == null || avatar.expressionsMenu == null) { error = "Error: Avatar has no ExpressionMenu asset."; return false; }
            return true;
        }

        // Iterative breadth-first traversal preserves shared/cyclic asset references and avoids a stack-depth limit.
        private static List<ExpressionMenuEntry> CollectExpressionMenus(VRCExpressionsMenu root, string rootPath)
        {
            var result = new List<ExpressionMenuEntry>();
            var seen = new HashSet<VRCExpressionsMenu>();
            result.Add(new ExpressionMenuEntry { Menu = root, Path = rootPath });
            seen.Add(root);
            for (int m = 0; m < result.Count; m++)
            {
                var entry = result[m];
                if (entry.Menu.controls == null) continue;
                for (int i = 0; i < entry.Menu.controls.Count; i++)
                {
                    var control = entry.Menu.controls[i];
                    if (control == null || control.type != MenuControl.ControlType.SubMenu || control.subMenu == null || !seen.Add(control.subMenu)) continue;
                    result.Add(new ExpressionMenuEntry { Menu = control.subMenu, Path = MenuControlPath(entry.Path, i) });
                }
            }
            return result;
        }

        private static string MenuControlPath(string prefix, int index) => string.IsNullOrEmpty(prefix) ? index.ToString(CultureInfo.InvariantCulture) : prefix + "/" + index.ToString(CultureInfo.InvariantCulture);

        private static bool TryResolveMenuControl(VRCExpressionsMenu root, string path, out MenuControl control, out string canonicalPath, out string error)
        {
            control = null;
            canonicalPath = "";
            error = null;
            if (string.IsNullOrWhiteSpace(path) || path.Trim('/').Length == 0) { error = "Error: controlPath must identify a menu control, e.g. '0' or '0/2'."; return false; }
            var segments = path.Trim('/').Split('/');
            var menu = root;
            for (int p = 0; p < segments.Length; p++)
            {
                if (menu == null || menu.controls == null) { error = $"Error: Menu at '{canonicalPath}' has no controls."; return false; }
                var segment = segments[p];
                if (segment.Length == 0) { error = "Error: Menu path contains an empty segment."; return false; }
                int index;
                if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out index))
                {
                    var name = segment.StartsWith("name:", StringComparison.Ordinal) ? segment.Substring(5) : segment;
                    var matches = Enumerable.Range(0, menu.controls.Count).Where(i => menu.controls[i] != null && string.Equals(menu.controls[i].name, name, StringComparison.Ordinal)).ToArray();
                    if (matches.Length != 1)
                    {
                        var parentPath = canonicalPath;
                        const int candidateLimit = 10;
                        error = matches.Length == 0 ? $"Error: Control named '{name}' not found at '{canonicalPath}'. Names are case-sensitive."
                            : $"Error: Control name '{name}' is ambiguous ({matches.Length} matches). Use an index path; first {Math.Min(matches.Length, candidateLimit)} candidates: {string.Join(", ", matches.Take(candidateLimit).Select(i => MenuControlPath(parentPath, i)))}{(matches.Length > candidateLimit ? ", ..." : ".")}";
                        return false;
                    }
                    index = matches[0];
                }
                if (index < 0 || index >= menu.controls.Count) { error = $"Error: Control index {index} is outside menu '{canonicalPath}' (count={menu.controls.Count})."; return false; }
                control = menu.controls[index];
                canonicalPath = MenuControlPath(canonicalPath, index);
                if (control == null) { error = $"Error: Control '{canonicalPath}' is null."; return false; }
                if (p == segments.Length - 1) return true;
                if (control.type != MenuControl.ControlType.SubMenu || control.subMenu == null) { error = $"Error: Control '{canonicalPath}' is not a SubMenu with a menu asset."; return false; }
                menu = control.subMenu;
            }
            return false;
        }

        private static string ExpressionMenuId(VRCExpressionsMenu menu)
        {
            var assetPath = AssetDatabase.GetAssetPath(menu);
            var guid = string.IsNullOrEmpty(assetPath) ? "" : AssetDatabase.AssetPathToGUID(assetPath);
            // Include instance id: multiple subassets can share the same file GUID.
            return (string.IsNullOrEmpty(guid) ? "instance" : guid) + ":" + menu.GetInstanceID().ToString(CultureInfo.InvariantCulture);
        }

        private static JNode ExpressionMenuControlJson(VRCAvatarDescriptor avatar, ModuleVrc3 module, MenuControl control, int index, string path)
        {
            if (control == null) return JNode.Obj(
                ("index", JNode.Num(index)), ("controlPath", JNode.Str(path)), ("nullControl", JNode.Bool(true)),
                ("headerIncluded", JNode.Bool(false)), ("totalSubParameters", JNode.Num(0)), ("totalLabels", JNode.Num(0)),
                ("subParameters", JNode.Arr()), ("labels", JNode.Arr()));
            return JNode.Obj(
                ("index", JNode.Num(index)),
                ("controlPath", JNode.Str(path)),
                ("headerIncluded", JNode.Bool(false)),
                ("name", JNode.Str(control.name)),
                ("type", JNode.Str(control.type.ToString())),
                ("value", JNode.Num(control.value)),
                ("icon", JNode.Str(control.icon == null ? "" : AssetDatabase.GetAssetPath(control.icon))),
                ("parameter", MenuParameterJson(avatar, module, control.parameter?.name)),
                ("totalSubParameters", JNode.Num(control.subParameters?.Length ?? 0)),
                ("totalLabels", JNode.Num(control.labels?.Length ?? 0)),
                ("subParameters", JNode.Arr()),
                ("labels", JNode.Arr()),
                ("submenuId", control.subMenu == null ? JNode.NullNode : JNode.Str(ExpressionMenuId(control.subMenu))));
        }

        private static JNode MenuParameterJson(VRCAvatarDescriptor avatar, ModuleVrc3 module, string name)
        {
            name = name ?? "";
            var definition = avatar.expressionParameters?.parameters?.FirstOrDefault(p => p != null && p.name == name && name.Length > 0);
            Vrc3Param runtime = null;
            if (name.Length > 0 && module != null) module.Params.TryGetValue(name, out runtime);
            return JNode.Obj(
                ("name", JNode.Str(name)),
                ("bound", JNode.Bool(name.Length > 0)),
                ("defined", JNode.Bool(definition != null)),
                ("expressionType", definition == null ? JNode.NullNode : JNode.Str(definition.valueType.ToString())),
                ("defaultValue", definition == null ? JNode.NullNode : JNode.Num(definition.defaultValue)),
                ("saved", definition == null ? JNode.NullNode : JNode.Bool(definition.saved)),
                ("networkSynced", definition == null ? JNode.NullNode : JNode.Bool(definition.networkSynced)),
                ("runtimeAvailable", JNode.Bool(runtime != null)),
                ("runtimeType", runtime == null ? JNode.NullNode : JNode.Str(runtime.Type.ToString())),
                ("runtimeValue", runtime == null ? JNode.NullNode : MenuRuntimeValue(runtime)));
        }

        private static JNode MenuRuntimeValue(Vrc3Param param)
        {
            switch (param.Type)
            {
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger: return JNode.Bool(param.BoolValue());
                case AnimatorControllerParameterType.Int: return JNode.Num(param.IntValue());
                default: return JNode.Num(param.FloatValue());
            }
        }

        private static void AddMenuParameterUsage(Dictionary<string, JNode> parameters, VRCAvatarDescriptor avatar, ModuleVrc3 module, string name, string path, MenuControl control, string role, int slot)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!parameters.TryGetValue(name, out var parameter))
            {
                parameter = MenuParameterJson(avatar, module, name);
                parameter.AsObject["usages"] = JNode.Arr();
                parameters.Add(name, parameter);
            }
            parameter["usages"].AsArray.Add(JNode.Obj(
                ("usageIndex", JNode.Num(parameter["usages"].Count)),
                ("controlPath", JNode.Str(path)), ("controlName", JNode.Str(control.name)),
                ("controlType", JNode.Str(control.type.ToString())), ("role", JNode.Str(role)), ("slot", JNode.Num(slot))));
        }

        private static bool MenuFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool AddMenuWrite(List<KeyValuePair<Vrc3Param, float>> writes, ModuleVrc3 module, string name, float value, bool required, out string error)
        {
            error = null;
            if (!MenuFinite(value)) { error = "Error: The menu control value must be finite."; return false; }
            if (string.IsNullOrEmpty(name))
            {
                if (!required) return true;
                error = "Error: This control has no primary parameter binding.";
                return false;
            }
            if (!module.Params.TryGetValue(name, out var param) || param == null) { error = $"Error: Menu parameter '{name}' is missing from the active GestureManager preview."; return false; }
            writes.Add(new KeyValuePair<Vrc3Param, float>(param, value));
            return true;
        }
#endif
    }
}

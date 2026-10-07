using System;
using System.Collections.Generic;
using System.Linq;
using AjisaiFlow.UnityAgent.Editor.MCP;
using AjisaiFlow.UnityAgent.Editor.Tools;
#if GESTURE_MANAGER
using BlackStartX.GestureManager.Editor.Modules.Vrc3;
using BlackStartX.GestureManager.Editor.Modules.Vrc3.Params;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Control = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;
#endif

internal static class Program
{
    private static int _checks;
    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }
    private static void Main()
    {
#if !GESTURE_MANAGER
        Assert(GestureManagerTools.GestureManagerGetExpressionMenu().Contains("not installed"), "Menu read must support missing GM.");
        Assert(GestureManagerTools.GestureManagerGetExpressionMenuParameters().Contains("not installed"), "Parameter read must support missing GM.");
        Assert(GestureManagerTools.GestureManagerSetExpressionMenuControl("0").Contains("not installed"), "Control action must support missing GM.");
#else
        var root = Menu("Root");
        var shared = Menu("Shared");
        root.controls.Add(SubMenu("Duplicate", shared, "Gate"));
        root.controls.Add(SubMenu("Duplicate", shared));
        root.controls.Add(ControlOf("Button / escaped\"", Control.ControlType.Button, "Button", 3));
        root.controls.Add(ControlOf("123", Control.ControlType.Toggle, "Toggle", 2));
        root.controls.Add(Puppet("Axis", Control.ControlType.FourAxisPuppet, "Gate", "Up", "Right", "Down", "Left"));
        root.controls.Add(Puppet("Radial", Control.ControlType.RadialPuppet, "Gate", "Radial"));
        root.controls.Add(Puppet("Missing", Control.ControlType.TwoAxisPuppet, "Gate", "Radial", "Missing"));
        shared.controls.Add(SubMenu("Cycle", root));
        shared.controls.Add(Puppet("Unbound", Control.ControlType.TwoAxisPuppet, "", "Radial", ""));
        var avatar = new VRCAvatarDescriptor { expressionsMenu = root, gameObject = new GameObject { name = "Avatar" } };
        avatar.gameObject.Descriptor = avatar;
        avatar.expressionParameters = new VRCExpressionParameters { parameters = new[] {
            new VRCExpressionParameters.Parameter { name = "Radial", valueType = VRCExpressionParameters.ValueType.Float, defaultValue = 0.25f, saved = true, networkSynced = true }
        }};
        GestureManagerTools.TestAvatar = avatar;
        GestureManagerTools.TestManager = null;
        var full = Parse(GestureManagerTools.GestureManagerGetExpressionMenu("Avatar"));
        Assert(full["totalMenus"].AsInt == 2 && full["totalControls"].AsInt == 9, "Shared/cyclic graph must include each asset once and every control.");
        Assert(!full["truncated"].AsBool && !full["runtimeAvailable"].AsBool, "Static graph must be complete without a preview.");
        Assert(full["menus"][0]["controls"][0]["submenuId"].AsString == full["menus"][0]["controls"][1]["submenuId"].AsString, "Shared edges must reference the same node.");
        Assert(full["menus"][1]["controls"][0]["submenuId"].AsString == full["rootMenuId"].AsString, "Cycle edge must point back to root.");
        Assert(full["menus"][0]["controls"][2]["name"].AsString == "Button / escaped\"", "Names must round-trip JSON escaping.");
        var fullRecords = MenuRecordKeys(full);
        Assert(fullRecords.Count == full["page"]["total"].AsInt, "Full structural record count must include headers and all array entries.");
        foreach (int pageSize in new[] { 1, 2, 7 })
        {
            var joined = ReadAllPages((offset, limit) => GestureManagerTools.GestureManagerGetExpressionMenu("Avatar", offset: offset, limit: limit), pageSize, MenuRecordKeys);
            Assert(joined.SetEquals(fullRecords), "Joining all menu pages must match the complete shared/cyclic graph.");
        }
        var capped = ReadAllPages((offset, limit) => GestureManagerTools.GestureManagerGetExpressionMenu("Avatar", maxMenus: 1, maxControls: 2, offset: offset, limit: limit), 7, MenuRecordKeys);
        Assert(capped.SetEquals(fullRecords), "Legacy menu/control caps must page every structural record without skipped entries.");
        var limited = Parse(GestureManagerTools.GestureManagerGetExpressionMenu("Avatar", maxMenus: 1, maxControls: 2));
        Assert(limited["totalControls"].AsInt == 9 && limited["omittedControls"].AsInt == 7 && limited["omittedMenus"].AsInt == 1 && limited["truncated"].AsBool, "Output limits must not truncate graph counting.");
        Assert(GestureManagerTools.GestureManagerGetExpressionMenu("Avatar", "Duplicate").Contains("ambiguous"), "Duplicate names must error instead of selecting arbitrarily.");
        var nested = Parse(GestureManagerTools.GestureManagerGetExpressionMenu("Avatar", "1"));
        Assert(nested["menus"][0]["firstPath"].AsString == "1", "Selected subtree must keep paths relative to avatar root.");
        var slots = Parse(GestureManagerTools.GestureManagerGetExpressionMenuParameters("1/1", "Avatar"));
        Assert(slots["subParameters"].Count == 2 && !slots["subParameters"][1]["bound"].AsBool, "Unbound subparameter slots must remain visible.");
        Assert(slots["subParameters"][0]["defaultValue"].AsNumber == 0.25 && slots["subParameters"][0]["saved"].AsBool, "ExpressionParameter definitions must remain distinct from runtime values.");
        var numericName = Parse(GestureManagerTools.GestureManagerGetExpressionMenuParameters("name:123", "Avatar"));
        Assert(numericName["controlPath"].AsString == "3" && numericName["index"].AsInt == 3, "Numeric names must have an unambiguous escape and report their real index.");
        Assert(GestureManagerTools.GestureManagerGetExpressionMenuParameters("0//1", "Avatar").Contains("empty segment"), "Malformed paths must error.");
        var module = new ModuleVrc3 { AvatarDescriptor = avatar };
        foreach (var name in new[] { "Gate", "Button", "Toggle", "Up", "Right", "Down", "Left", "Radial" })
            module.Params.Add(name, new Vrc3Param(name, AnimatorControllerParameterType.Float));
        GestureManagerTools.TestManager = new BlackStartX.GestureManager.GestureManager { Module = module };
        Parse(GestureManagerTools.GestureManagerSetExpressionMenuControl("2", "press"));
        Assert(module.Params["Button"].FloatValue() == 3 && module.Params["Button"].Writes.Count == 1, "Button press must remain held for the next Editor update.");
        Parse(GestureManagerTools.GestureManagerSetExpressionMenuControl("2", "release"));
        Assert(module.Params["Button"].FloatValue() == 0, "Button release must reset to zero.");
        Parse(GestureManagerTools.GestureManagerSetExpressionMenuControl("3"));
        Assert(module.Params["Toggle"].FloatValue() == 2, "Toggle activation must select control.value.");
        Parse(GestureManagerTools.GestureManagerSetExpressionMenuControl("3"));
        Assert(module.Params["Toggle"].FloatValue() == 0, "Repeated toggle must return to zero.");
        Parse(GestureManagerTools.GestureManagerSetExpressionMenuControl("4", "set", x: 1, y: -1));
        Assert(Math.Abs(module.Params["Right"].FloatValue() - 0.70710677f) < 0.00001 && Math.Abs(module.Params["Down"].FloatValue() - 0.70710677f) < 0.00001, "Four-axis mapping must normalize diagonal inputs and use Up/Right/Down/Left order.");
        Assert(module.Params["Up"].FloatValue() == 0 && module.Params["Left"].FloatValue() == 0 && module.Params["Gate"].FloatValue() == 1, "Axis set must clear opposing axes and open the gate.");
        Parse(GestureManagerTools.GestureManagerSetExpressionMenuControl("4", "close"));
        Assert(module.Params["Gate"].FloatValue() == 0 && module.Params["Right"].FloatValue() > 0, "Puppet close must clear only its gate and preserve axis values.");
        int gateWrites = module.Params["Gate"].Writes.Count;
        int radialWrites = module.Params["Radial"].Writes.Count;
        Assert(GestureManagerTools.GestureManagerSetExpressionMenuControl("6", "set", x: 0.5f).Contains("Missing"), "Missing subparameter must error.");
        Assert(module.Params["Gate"].Writes.Count == gateWrites && module.Params["Radial"].Writes.Count == radialWrites, "Missing subparameter validation must happen before gate/earlier-axis writes.");
        Assert(GestureManagerTools.GestureManagerSetExpressionMenuControl("5", "set", value: float.NaN).Contains("finite"), "Nonfinite radial inputs must error.");
        Assert(module.Params["Gate"].Writes.Count == gateWrites, "Invalid value must not open the puppet gate.");
        Parse(GestureManagerTools.GestureManagerSetExpressionMenuControl("5", "set", value: 0.75f));
        var runtime = Parse(GestureManagerTools.GestureManagerGetExpressionMenuParameters("5"));
        Assert(runtime["subParameters"][0]["runtimeValue"].AsNumber == 0.75 && runtime["subParameters"][0]["runtimeAvailable"].AsBool, "Parameter inspection must return the active runtime value.");
        var all = Parse(GestureManagerTools.GestureManagerGetExpressionMenuParameters());
        Assert(all["count"].AsInt == 9, "All parameter query must deduplicate names across shared/cyclic menus.");
        int radialUsages = 0;
        foreach (var parameter in all["parameters"].AsArray) if (parameter["name"].AsString == "Radial") radialUsages = parameter["usages"].Count;
        Assert(radialUsages == 3, "Parameter usage must retain every bound slot in unique menu assets.");
        var allParameterRecords = ParameterRecordKeys(all);
        Assert(allParameterRecords.Count == all["page"]["total"].AsInt, "Aggregate parameter total must include every unique header and usage.");
        foreach (int pageSize in new[] { 1, 3, 7 })
        {
            var joined = ReadAllPages((offset, limit) => GestureManagerTools.GestureManagerGetExpressionMenuParameters(offset: offset, limit: limit), pageSize, ParameterRecordKeys);
            Assert(joined.SetEquals(allParameterRecords), "Joining all parameter pages must preserve every bound usage exactly once.");
        }
        PagingBoundaries(full["page"]["total"].AsInt, all["page"]["total"].AsInt);

        module.Active = false;
        var inactive = Parse(GestureManagerTools.GestureManagerGetExpressionMenu("Avatar"));
        Assert(!inactive["runtimeAvailable"].AsBool, "Inactive modules must not supply live runtime values for static avatar inspection.");
        module.Active = true;

        var deepRoot = Menu("DeepRoot");
        var current = deepRoot;
        for (int i = 0; i < 300; i++) { var next = Menu("Deep" + i); current.controls.Add(SubMenu("Next", next)); current = next; }
        avatar.expressionsMenu = deepRoot;
        var deep = Parse(GestureManagerTools.GestureManagerGetExpressionMenu());
        Assert(deep["totalMenus"].AsInt == 301 && deep["truncated"].AsBool && deep["page"]["returned"].AsInt == 50, "Deep menu counts must remain exact while default output is bounded.");
        var deepRecords = ReadAllPages((offset, limit) => GestureManagerTools.GestureManagerGetExpressionMenu(offset: offset, limit: limit), 200, MenuRecordKeys);
        Assert(deepRecords.Count == 601 && deepRecords.Count(key => key.StartsWith("menu:")) == 301, "Deep menu paging must visit every header and control without a recursion cutoff.");
        PagingLargeCollections(avatar);
#endif
        Console.WriteLine($"GestureManagerMenu: {_checks} checks passed.");
    }
#if GESTURE_MANAGER
    private static void PagingBoundaries(int menuTotal, int parameterTotal)
    {
        foreach (int limit in new[] { -1, 0, 201, int.MaxValue })
        {
            Assert(GestureManagerTools.GestureManagerGetExpressionMenu(limit: limit).StartsWith("Error:"), "Menu invalid limits must fail.");
            Assert(GestureManagerTools.GestureManagerGetExpressionMenuParameters(limit: limit).StartsWith("Error:"), "Aggregate parameter invalid limits must fail.");
            Assert(GestureManagerTools.GestureManagerGetExpressionMenuParameters("5", limit: limit).StartsWith("Error:"), "Control-specific invalid limits must fail.");
        }
        Assert(GestureManagerTools.GestureManagerGetExpressionMenu(offset: -1).StartsWith("Error:"), "Menu negative offset must fail.");
        Assert(GestureManagerTools.GestureManagerGetExpressionMenuParameters(offset: -1).StartsWith("Error:"), "Parameter negative offset must fail.");
        Assert(GestureManagerTools.GestureManagerGetExpressionMenu(maxMenus: -1).StartsWith("Error:"), "Legacy negative menu cap must fail.");
        Assert(GestureManagerTools.GestureManagerGetExpressionMenu(maxControls: -1).StartsWith("Error:"), "Legacy negative control cap must fail.");
        foreach (int offset in new[] { menuTotal, menuTotal + 3, int.MaxValue })
        {
            var terminal = Parse(GestureManagerTools.GestureManagerGetExpressionMenu(offset: offset));
            CheckPage(terminal, offset, 50, 0);
            Assert(terminal["menus"].Count == 0, "Out-of-range menu offset must return an empty terminal page.");
        }
        foreach (int offset in new[] { parameterTotal, parameterTotal + 3, int.MaxValue })
        {
            var terminal = Parse(GestureManagerTools.GestureManagerGetExpressionMenuParameters(offset: offset));
            CheckPage(terminal, offset, 50, 0);
            Assert(terminal["parameters"].Count == 0, "Out-of-range parameter offset must return an empty terminal page.");
        }
        var terminalControl = Parse(GestureManagerTools.GestureManagerGetExpressionMenuParameters("5", offset: int.MaxValue));
        CheckPage(terminalControl, int.MaxValue, 50, 0);
        Assert(!terminalControl["headerIncluded"].AsBool && terminalControl["subParameters"].Count == 0 && terminalControl["labels"].Count == 0, "Out-of-range control slots must be empty and not repeat the header record.");
    }

    private static void PagingLargeCollections(VRCAvatarDescriptor avatar)
    {
        var root = Menu("Large");
        var empty = Menu("Empty");
        root.controls.Add(SubMenu("Empty", empty));
        for (int i = 0; i < 400; i++) root.controls.Add(ControlOf("Repeated" + i, Control.ControlType.Button, "Repeated"));
        root.controls.Add(null);
        avatar.expressionsMenu = root;
        var first = Parse(GestureManagerTools.GestureManagerGetExpressionMenu());
        Assert(first["page"]["returned"].AsInt == 50 && first["page"]["hasMore"].AsBool, "A large single menu must obey the default output cap.");
        var largeRecords = ReadAllPages((offset, limit) => GestureManagerTools.GestureManagerGetExpressionMenu(offset: offset, limit: limit), 37, MenuRecordKeys);
        Assert(largeRecords.Count == 404 && largeRecords.Count(key => key.StartsWith("menu:")) == 2, "Empty submenu and null control headers must survive large-menu paging.");
        var cappedRecords = ReadAllPages((offset, limit) => GestureManagerTools.GestureManagerGetExpressionMenu(maxMenus: 1, maxControls: 1, offset: offset, limit: limit), 200, MenuRecordKeys);
        Assert(cappedRecords.SetEquals(largeRecords), "One-control cap must eventually return the empty final submenu.");
        var repeatedFirst = Parse(GestureManagerTools.GestureManagerGetExpressionMenuParameters());
        Assert(repeatedFirst["count"].AsInt == 1 && repeatedFirst["parameters"][0]["totalUsages"].AsInt == 400 && repeatedFirst["parameters"][0]["usages"].Count == 49, "One parameter's usages must be bounded independently from unique parameter count.");
        var usages = ReadAllPages((offset, limit) => GestureManagerTools.GestureManagerGetExpressionMenuParameters(offset: offset, limit: limit), 31, ParameterRecordKeys);
        Assert(usages.Count == 401, "Every repeated usage and its single parameter header must be retrievable.");

        var longControl = Puppet("Long", Control.ControlType.TwoAxisPuppet, "Main", Enumerable.Repeat("Repeated", 300).ToArray());
        longControl.labels = Enumerable.Range(0, 240).Select(i => new Control.Label { name = "Label" + i }).ToArray();
        root.controls.Clear();
        root.controls.Add(longControl);
        var longFirst = Parse(GestureManagerTools.GestureManagerGetExpressionMenu());
        Assert(longFirst["menus"][0]["controls"][0]["subParameters"].Count == 48 && longFirst["menus"][0]["controls"][0]["labels"].Count == 0, "Nested subparameters and labels must share the structural-record page budget.");
        var longRecords = ReadAllPages((offset, limit) => GestureManagerTools.GestureManagerGetExpressionMenu(offset: offset, limit: limit), 29, MenuRecordKeys);
        Assert(longRecords.Count == 542 && longRecords.Count(key => key.Contains(":slot:")) == 300 && longRecords.Count(key => key.Contains(":label:")) == 240, "Long control nested arrays must join completely without hidden unbounded output.");
        var slotRecords = ReadAllPages((offset, limit) => GestureManagerTools.GestureManagerGetExpressionMenuParameters("0", offset: offset, limit: limit), 23, ControlRecordKeys);
        Assert(slotRecords.Count == 541, "Control-specific paging must preserve its header, every slot and every label.");
        var parameterRecords = ReadAllPages((offset, limit) => GestureManagerTools.GestureManagerGetExpressionMenuParameters(offset: offset, limit: limit), 200, ParameterRecordKeys);
        Assert(parameterRecords.Count == 303, "Long-control aggregate parameter paging must retain repeated slot usages.");

        root.controls.Clear();
        var noControls = Parse(GestureManagerTools.GestureManagerGetExpressionMenu());
        CheckPage(noControls, 0, 50, 1);
        Assert(noControls["menus"].Count == 1 && noControls["menus"][0]["headerIncluded"].AsBool, "An empty root menu must still have one pageable header.");
        var noParameters = Parse(GestureManagerTools.GestureManagerGetExpressionMenuParameters());
        CheckPage(noParameters, 0, 50, 0);
        Assert(noParameters["parameters"].Count == 0 && noParameters["count"].AsInt == 0, "A parameter-free graph must yield an empty terminal parameter page.");

        foreach (string name in new[] { "z", "Z", "a", "A" }) root.controls.Add(ControlOf(name, Control.ControlType.Button, name));
        var ordered = Parse(GestureManagerTools.GestureManagerGetExpressionMenuParameters());
        Assert(ordered["parameters"].AsArray.Select(parameter => parameter["name"].AsString).SequenceEqual(new[] { "A", "Z", "a", "z" }), "Parameter order must use ordinal comparison across names and cases.");

        root.controls.Clear();
        for (int i = 0; i < 400; i++) root.controls.Add(ControlOf("Duplicate", Control.ControlType.Button, "Repeated"));
        var ambiguous = GestureManagerTools.GestureManagerGetExpressionMenuParameters("Duplicate");
        Assert(ambiguous.Contains("ambiguous (400 matches)") && ambiguous.Contains("Use an index path"), "Ambiguous paths must report the total candidate count and an index-path hint.");
        Assert(ambiguous.EndsWith("first 10 candidates: 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, ...", StringComparison.Ordinal), "Ambiguous path errors must list only the first ten candidates, even with hundreds of matches.");
        Assert(ambiguous.Length < 250, "Large ambiguous-name errors must remain bounded.");
        Assert(GestureManagerTools.GestureManagerGetExpressionMenu("", "Duplicate").Contains("first 10 candidates"), "Menu-path errors must use the same bounded candidate preview.");
        Assert(GestureManagerTools.GestureManagerSetExpressionMenuControl("Duplicate").Contains("first 10 candidates"), "Menu-control writes must use the same bounded candidate preview before mutation.");
    }

    private static HashSet<string> ReadAllPages(Func<int, int, string> query, int limit, Func<JNode, HashSet<string>> recordKeys)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        int offset = 0;
        int total = -1;
        while (true)
        {
            var response = Parse(query(offset, limit));
            var keys = recordKeys(response);
            CheckPage(response, offset, limit, keys.Count);
            if (total < 0) total = response["page"]["total"].AsInt;
            Assert(response["page"]["total"].AsInt == total, "Unchanged fixtures must have stable paging totals.");
            foreach (var key in keys) Assert(all.Add(key), "Paged record repeated: " + key);
            if (!response["page"]["hasMore"].AsBool) break;
            Assert(keys.Count > 0, "Every nonterminal page must advance.");
            offset = response["page"]["nextOffset"].AsInt;
        }
        Assert(all.Count == total, "Joining all pages must return the exact structural-record total.");
        return all;
    }

    private static void CheckPage(JNode response, int offset, int limit, int emitted)
    {
        var page = response["page"];
        Assert(page.Type == JNode.JType.Object, "Every paged getter must expose page metadata.");
        Assert(page["offset"].AsInt == offset && page["limit"].AsInt == limit, "Page metadata must preserve request arguments.");
        Assert(page["returned"].AsInt == emitted && emitted <= limit, "Returned count must match bounded emitted records.");
        bool hasMore = (long)offset + emitted < page["total"].AsInt;
        Assert(page["hasMore"].AsBool == hasMore, "hasMore must reflect the first unreturned record.");
        Assert(hasMore ? page["nextOffset"].AsInt == offset + emitted : page["nextOffset"].IsNull, "Next offset must advance by actual records, or be null at the end.");
    }

    private static HashSet<string> MenuRecordKeys(JNode response)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var menu in response["menus"].AsArray)
        {
            string id = menu["id"].AsString;
            if (menu["headerIncluded"].AsBool) Assert(keys.Add("menu:" + id), "Menu header must appear once per page.");
            foreach (var control in menu["controls"].AsArray)
                foreach (var key in ControlRecordKeys(control)) Assert(keys.Add(id + ":" + key), "Control record must appear once per page.");
        }
        return keys;
    }

    private static HashSet<string> ControlRecordKeys(JNode response)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string id = "control:" + response["index"].AsInt;
        if (response["headerIncluded"].AsBool) Assert(keys.Add(id), "Control header must appear once per page.");
        foreach (var slot in response["subParameters"].AsArray) Assert(keys.Add(id + ":slot:" + slot["slot"].AsInt), "Subparameter slot must appear once per page.");
        foreach (var label in response["labels"].AsArray) Assert(keys.Add(id + ":label:" + label["index"].AsInt), "Label must appear once per page.");
        return keys;
    }

    private static HashSet<string> ParameterRecordKeys(JNode response)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in response["parameters"].AsArray)
        {
            string id = "parameter:" + parameter["name"].AsString;
            if (parameter["headerIncluded"].AsBool) Assert(keys.Add(id), "Parameter header must appear once per page.");
            foreach (var usage in parameter["usages"].AsArray) Assert(keys.Add(id + ":usage:" + usage["usageIndex"].AsInt), "Parameter usage must appear once per page.");
        }
        return keys;
    }

    private static JNode Parse(string json)
    {
        Assert(!json.StartsWith("Error:"), json);
        var result = JNode.Parse(json);
        Assert(result.Type == JNode.JType.Object, "Tool must return a valid JSON object: " + json);
        return result;
    }
    private static VRCExpressionsMenu Menu(string name) => new VRCExpressionsMenu { name = name };
    private static Control ControlOf(string name, Control.ControlType type, string parameter, float value = 1) => new Control { name = name, type = type, parameter = new Control.Parameter { name = parameter }, value = value };
    private static Control SubMenu(string name, VRCExpressionsMenu menu, string parameter = "")
    {
        var control = ControlOf(name, Control.ControlType.SubMenu, parameter);
        control.subMenu = menu;
        return control;
    }
    private static Control Puppet(string name, Control.ControlType type, string parameter, params string[] subs)
    {
        var control = ControlOf(name, type, parameter);
        control.subParameters = Array.ConvertAll(subs, sub => new Control.Parameter { name = sub });
        return control;
    }
#endif
}

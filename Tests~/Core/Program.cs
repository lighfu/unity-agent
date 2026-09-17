using System;
using System.Collections.Generic;
using AjisaiFlow.UnityAgent.Editor;
using AjisaiFlow.UnityAgent.Editor.MCP;

internal static class Program
{
    private static int Main()
    {
        try
        {
            TestJsonParserRejectsMalformedInput();
            TestJsonParserDepthLimit();
            TestJsonFactoriesAndSerialization();
            TestXmlCloseTagLengthAndWhitespace();
            TestXmlUnclosedArgumentRecovery();
            Console.WriteLine("Core parser tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void TestJsonParserRejectsMalformedInput()
    {
        var value = JNode.Parse("{\"message\":\"A\\u0041\\n\",\"number\":-1.25e2,\"ok\":true,\"none\":null}");
        Assert(value.Type == JNode.JType.Object, "valid JSON object should parse");
        Assert(value["message"].AsString == "AA\n", "escaped string should decode");
        Assert(value["number"].AsNumber == -125, "JSON number should parse");
        Assert(value["ok"].AsBool, "JSON boolean should parse");
        Assert(value["none"].IsNull, "JSON null should parse");

        string[] malformed =
        {
            "{\"x\":1} trailing",
            "[1,]",
            "{\"x\" 1}",
            "{\"x\":}",
            "\"\\u  41\"",
            "\"unterminated",
            "trueish",
            "1.",
            "01",
            "[1\u00a02]"
        };

        foreach (var json in malformed)
            Assert(JNode.Parse(json).IsNull, "malformed JSON should return NullNode: " + json);
    }

    private static void TestJsonFactoriesAndSerialization()
    {
        Assert(JNode.Arr((JNode)null).ToJson() == "[null]", "null array items should serialize as null");
        Assert(JNode.Obj(("value", (JNode)null)).ToJson() == "{\"value\":null}",
            "null object values should serialize as null");
        Assert(JNode.Num(double.NaN).ToJson() == "null", "NaN is not valid JSON");
        Assert(JNode.Num(double.PositiveInfinity).ToJson() == "null", "Infinity is not valid JSON");
    }

    private static void TestJsonParserDepthLimit()
    {
        string moderate = "0";
        for (int i = 0; i < 64; i++)
            moderate = "[" + moderate + "]";
        Assert(!JNode.Parse(moderate).IsNull, "moderately nested JSON should parse");

        string oversized = "0";
        for (int i = 0; i < 256; i++)
            oversized = "[" + oversized + "]";
        Assert(JNode.Parse(oversized).IsNull, "excessively nested JSON should be rejected");
    }

    private static void TestXmlCloseTagLengthAndWhitespace()
    {
        const string text = "prefix<tool name='first'><arg name='value'>ok</arg  ></tool \n >suffix<tool name='second'/>";
        List<XmlToolCall> calls = XmlToolCallParser.Parse(text);
        Assert(calls.Count == 2, "both XML tool calls should be found");
        Assert(calls[0].Name == "first" && calls[0].Args["value"] == "ok", "first XML call should parse");
        Assert(text.Substring(calls[0].Index, calls[0].Length) ==
            "<tool name='first'><arg name='value'>ok</arg  ></tool \n >",
            "Length should include whitespace before the closing angle bracket");
        Assert(text[calls[0].Index + calls[0].Length] == 's', "scan should resume after the complete closing tag");
        Assert(calls[1].Name == "second", "self-closing XML call should parse after the first call");
    }

    private static void TestXmlUnclosedArgumentRecovery()
    {
        const string text = "<tool name='first'><arg name='value'>unfinished</tool><tool name='second'/>";
        List<XmlToolCall> calls = XmlToolCallParser.Parse(text);
        Assert(calls.Count == 2, "an unclosed argument should not swallow a later tool call");
        Assert(calls[0].Args["value"] == "unfinished", "unclosed argument content should remain recoverable");
        Assert(calls[1].Name == "second", "later tool call should remain discoverable");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

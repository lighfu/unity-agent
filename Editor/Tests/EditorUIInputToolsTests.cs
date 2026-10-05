using System.Collections.Generic;
using AjisaiFlow.UnityAgent.Editor.Tools;
using NUnit.Framework;
using UnityEngine;

namespace AjisaiFlow.UnityAgent.Editor.Tests
{
    public sealed class EditorUIInputToolsTests
    {
        [Test]
        public void DoubleClickHasBalancedPressesAndIncrementingClickCount()
        {
            Assert.IsTrue(EditorUIInputTools.TryBuildEvents("doubleClick", 20, 30, 0, 0, 0,
                "", "", "Ctrl+Shift", "", 8, out var events, out bool positioned, out string error), error);
            Assert.IsTrue(positioned);
            Assert.AreEqual(4, events.Count);
            Assert.AreEqual(EventType.MouseDown, events[0].type);
            Assert.AreEqual(EventType.MouseUp, events[1].type);
            Assert.AreEqual(EventType.MouseDown, events[2].type);
            Assert.AreEqual(EventType.MouseUp, events[3].type);
            Assert.AreEqual(1, events[0].clickCount);
            Assert.AreEqual(2, events[2].clickCount);
            Assert.AreEqual(EventModifiers.Control | EventModifiers.Shift, events[3].modifiers);
            Assert.AreEqual(new Vector2(20, 30), events[3].mousePosition);
        }

        [Test]
        public void DragEndsAtRequestedPointAndReleasesMouse()
        {
            Assert.IsTrue(EditorUIInputTools.TryBuildEvents("drag", 10, 20, 0, 40, 20,
                "", "", "", "", 4, out var events, out _, out string error), error);
            Assert.AreEqual(6, events.Count);
            Assert.AreEqual(EventType.MouseDown, events[0].type);
            Assert.AreEqual(EventType.MouseDrag, events[4].type);
            Assert.AreEqual(new Vector2(50, 40), events[4].mousePosition);
            Assert.AreEqual(new Vector2(10, 5), events[4].delta);
            Assert.AreEqual(EventType.MouseUp, events[5].type);
            Assert.AreEqual(new Vector2(50, 40), events[5].mousePosition);
            Assert.IsFalse(EditorUIInputTools.ValidatePositions(events, new Vector2(50, 100), out _),
                "A drag endpoint on the right edge lies outside the drawing area.");
        }

        [Test]
        public void NonFiniteInputNeverProducesQueuedEvents()
        {
            Assert.IsFalse(EditorUIInputTools.TryBuildEvents("scroll", 10, 20, 0, 0, float.NaN,
                "", "", "", "", 8, out _, out _, out _));
            Assert.IsFalse(EditorUIInputTools.TryBuildEvents("click", float.PositiveInfinity, 20, 0, 0, 0,
                "", "", "", "", 8, out _, out _, out _));
            var outside = new List<Event> { new Event { mousePosition = new Vector2(-1, 20) } };
            Assert.IsFalse(EditorUIInputTools.ValidatePositions(outside, new Vector2(100, 100), out _));
        }

        [Test]
        public void InvalidKeysAndModifiersAreRejectedInsteadOfBecomingDifferentInput()
        {
            Assert.IsFalse(EditorUIInputTools.TryParseKey("99999", "", out _, out _, out _));
            Assert.IsFalse(EditorUIInputTools.TryParseKey("NoSuchKey", "", out _, out _, out _));
            Assert.IsFalse(EditorUIInputTools.TryParseKey("", "ab", out _, out _, out _));
            Assert.IsFalse(EditorUIInputTools.TryParseKey("", "\uD800", out _, out _, out _));
            Assert.IsFalse(EditorUIInputTools.TryParseModifiers("Ctrl+Shfit", out _, out _));
            Assert.IsTrue(EditorUIInputTools.TryParseKey("Enter", "", out var key, out char character, out _));
            Assert.AreEqual(KeyCode.Return, key);
            Assert.AreEqual('\n', character);
        }

        [Test]
        public void KeyboardAndCommandsDoNotRequireMouseCoordinates()
        {
            Assert.IsTrue(EditorUIInputTools.TryBuildEvents("key", 0, 0, 0, 0, 0,
                "A", "", "Ctrl", "", 8, out var keys, out bool positioned, out string error), error);
            Assert.IsFalse(positioned);
            Assert.AreEqual(EventType.KeyDown, keys[0].type);
            Assert.AreEqual(EventType.KeyUp, keys[1].type);
            Assert.IsTrue(EditorUIInputTools.TryBuildEvents("command", 0, 0, 0, 0, 0,
                "", "", "", "Paste", 8, out var commands, out positioned, out error), error);
            Assert.IsFalse(positioned);
            Assert.AreEqual(EventType.ExecuteCommand, commands[0].type);
            Assert.AreEqual("Paste", commands[0].commandName);
        }

        [Test]
        public void UnicodeTypingPreservesPairsAndNormalizesLineEndings()
        {
            Assert.IsTrue(EditorUIInputTools.TryBuildTextEvents("日\uD83D\uDE00\r\n本\t", out var events, out string error), error);
            Assert.AreEqual(12, events.Count, "The CRLF must yield one key pair, and the emoji must retain both UTF-16 units.");
            Assert.AreEqual('日', events[0].character);
            Assert.AreEqual('\uD83D', events[2].character);
            Assert.AreEqual('\uDE00', events[4].character);
            Assert.AreEqual(KeyCode.Return, events[6].keyCode);
            Assert.AreEqual('\n', events[6].character);
            Assert.AreEqual('本', events[8].character);
            Assert.AreEqual(KeyCode.Tab, events[10].keyCode);
        }

        [Test]
        public void TextValidationRejectsMalformedUnicodeAndUnexpectedControlInput()
        {
            Assert.IsFalse(EditorUIInputTools.TryBuildTextEvents("\uD800", out _, out _));
            Assert.IsFalse(EditorUIInputTools.TryBuildTextEvents("\uDC00", out _, out _));
            Assert.IsFalse(EditorUIInputTools.TryBuildTextEvents("a\0b", out _, out _));
            Assert.IsFalse(EditorUIInputTools.TryBuildTextEvents("a\bb", out _, out _));
            Assert.IsFalse(EditorUIInputTools.TryBuildTextEvents(new string('a', 4097), out _, out _));
        }
    }
}

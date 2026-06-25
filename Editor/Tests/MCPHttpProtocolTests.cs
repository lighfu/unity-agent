using AjisaiFlow.UnityAgent.Editor.MCP;
using NUnit.Framework;

namespace AjisaiFlow.UnityAgent.Editor.Tests
{
    public sealed class MCPHttpProtocolTests
    {
        [Test]
        public void NegotiatesLatestProtocolVersion()
        {
            var result = MCPHttpProtocol.NegotiateProtocolVersion(JNode.Obj(
                ("protocolVersion", JNode.Str("2025-06-18"))));

            Assert.AreEqual("2025-06-18", result);
        }

        [Test]
        public void FallsBackToLatestWhenRequestedVersionIsUnsupported()
        {
            var result = MCPHttpProtocol.NegotiateProtocolVersion(JNode.Obj(
                ("protocolVersion", JNode.Str("2099-01-01"))));

            Assert.AreEqual("2025-06-18", result);
        }

        [Test]
        public void PostAcceptHeaderMustIncludeJsonAndEventStream()
        {
            Assert.IsTrue(MCPHttpProtocol.AcceptsJsonAndEventStream("application/json, text/event-stream"));
            Assert.IsTrue(MCPHttpProtocol.AcceptsJsonAndEventStream("application/json; charset=utf-8, text/event-stream"));
            Assert.IsFalse(MCPHttpProtocol.AcceptsJsonAndEventStream("application/json"));
            Assert.IsFalse(MCPHttpProtocol.AcceptsJsonAndEventStream("text/event-stream"));
            Assert.IsFalse(MCPHttpProtocol.AcceptsJsonAndEventStream("*/*"));
            Assert.IsFalse(MCPHttpProtocol.AcceptsJsonAndEventStream("application/json, */*"));
        }

        [Test]
        public void MissingAcceptHeaderIsCompatibilityPath()
        {
            Assert.IsFalse(MCPHttpProtocol.ShouldWarnAboutPostAcceptHeader(null));
            Assert.IsFalse(MCPHttpProtocol.ShouldWarnAboutPostAcceptHeader(""));
        }

        [Test]
        public void ExplicitNonCompliantAcceptHeaderIsWarningPath()
        {
            Assert.IsTrue(MCPHttpProtocol.ShouldWarnAboutPostAcceptHeader("application/json"));
            Assert.IsTrue(MCPHttpProtocol.ShouldWarnAboutPostAcceptHeader("*/*"));
            Assert.IsFalse(MCPHttpProtocol.ShouldWarnAboutPostAcceptHeader("application/json, text/event-stream"));
        }

        [Test]
        public void ProtocolVersionHeaderAllowsMissingOrSupportedVersionsOnly()
        {
            Assert.IsTrue(MCPHttpProtocol.IsValidProtocolVersionHeader(null));
            Assert.IsTrue(MCPHttpProtocol.IsValidProtocolVersionHeader(""));
            Assert.IsTrue(MCPHttpProtocol.IsValidProtocolVersionHeader("2025-06-18"));
            Assert.IsTrue(MCPHttpProtocol.IsValidProtocolVersionHeader("2025-03-26"));
            Assert.IsFalse(MCPHttpProtocol.IsValidProtocolVersionHeader("not-a-date"));
        }
    }
}

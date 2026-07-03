using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AjisaiFlow.UnityAgent.Editor.MCP;
using NUnit.Framework;

namespace AjisaiFlow.UnityAgent.Editor.Tests
{
    public sealed class AgentMCPServerHttpTests
    {
        AgentMCPServer _server;
        int _port;
        const string Token = "test-token";

        [SetUp]
        public void SetUp()
        {
            _port = GetFreePort();
            _server = new AgentMCPServer();
            _server.Start(_port, Token);
        }

        [TearDown]
        public void TearDown()
        {
            _server?.Stop();
            _server = null;
        }

        [Test]
        public void InitializeReturnsLatestProtocolAndResponseHeader()
        {
            var response = PostMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}",
                "application/json, text/event-stream");

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual("2025-06-18", response.Headers[MCPHttpProtocol.HeaderProtocolVersion]);
            StringAssert.Contains("\"protocolVersion\":\"2025-06-18\"", response.Body);
        }

        [Test]
        public void MissingAcceptHeaderStaysCompatible()
        {
            var response = PostMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}",
                null);

            Assert.AreEqual(200, response.StatusCode);
            StringAssert.Contains("\"protocolVersion\":\"2025-06-18\"", response.Body);
        }

        [Test]
        public void InitializeOlderProtocolKeepsResponseHeaderAndBodyAligned()
        {
            var response = PostMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\"}}",
                "application/json, text/event-stream");

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual("2025-03-26", response.Headers[MCPHttpProtocol.HeaderProtocolVersion]);
            StringAssert.Contains("\"protocolVersion\":\"2025-03-26\"", response.Body);
        }

        [Test]
        public void MissingProtocolHeaderUsesCompatibilityDefaultForNonInitializeRequest()
        {
            var response = PostMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}",
                "application/json, text/event-stream");

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual("2025-03-26", response.Headers[MCPHttpProtocol.HeaderProtocolVersion]);
        }

        [Test]
        public void InvalidProtocolVersionHeaderReturns400()
        {
            var response = PostMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}",
                "application/json, text/event-stream",
                "not-a-date");

            Assert.AreEqual(400, response.StatusCode);
        }

        [Test]
        public void NonLocalOriginReturns403()
        {
            var response = PostMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}",
                "application/json, text/event-stream",
                origin: "https://example.com");

            Assert.AreEqual(403, response.StatusCode);
        }

        [Test]
        public void LocalOriginIsAllowedAndEchoedForCors()
        {
            const string Origin = "http://localhost:3000";
            var response = PostMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}",
                "application/json, text/event-stream",
                origin: Origin);

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(Origin, response.Headers["Access-Control-Allow-Origin"]);
        }

        [Test]
        public void NotificationReturns202()
        {
            var response = PostMcp(
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}",
                "application/json, text/event-stream");

            Assert.AreEqual(202, response.StatusCode);
            Assert.AreEqual("", response.Body);
        }

        [Test]
        public void JsonRpcResponseReturns202()
        {
            var response = PostMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}",
                "application/json, text/event-stream");

            Assert.AreEqual(202, response.StatusCode);
            Assert.AreEqual("", response.Body);
        }

        [Test]
        public void MissingMethodRequestReturnsInvalidRequest()
        {
            var response = PostMcp(
                "{\"jsonrpc\":\"2.0\",\"id\":1}",
                "application/json, text/event-stream");

            Assert.AreEqual(200, response.StatusCode);
            StringAssert.Contains("\"code\":-32600", response.Body);
        }

        [Test]
        public void JsonRpcResponseWithoutIdReturnsInvalidRequest()
        {
            var response = PostMcp(
                "{\"jsonrpc\":\"2.0\",\"result\":{}}",
                "application/json, text/event-stream");

            Assert.AreEqual(200, response.StatusCode);
            StringAssert.Contains("\"code\":-32600", response.Body);
        }

        ServerResponse PostMcp(string json, string accept, string protocolVersion = null, string origin = null)
        {
            var req = (HttpWebRequest)WebRequest.Create($"http://localhost:{_port}/mcp");
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Headers["Authorization"] = "Bearer " + Token;
            if (!string.IsNullOrEmpty(accept))
                req.Accept = accept;
            if (!string.IsNullOrEmpty(protocolVersion))
                req.Headers[MCPHttpProtocol.HeaderProtocolVersion] = protocolVersion;
            if (!string.IsNullOrEmpty(origin))
                req.Headers[MCPHttpProtocol.HeaderOrigin] = origin;

            byte[] body = Encoding.UTF8.GetBytes(json);
            req.ContentLength = body.Length;
            using (var stream = req.GetRequestStream())
                stream.Write(body, 0, body.Length);

            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                    return ReadResponse(resp);
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse resp)
            {
                using (resp)
                    return ReadResponse(resp);
            }
        }

        static ServerResponse ReadResponse(HttpWebResponse resp)
        {
            string text = "";
            using (var stream = resp.GetResponseStream())
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        text = reader.ReadToEnd();
                }
            }
            return new ServerResponse
            {
                StatusCode = (int)resp.StatusCode,
                Body = text,
                Headers = resp.Headers,
            };
        }

        static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        struct ServerResponse
        {
            public int StatusCode;
            public string Body;
            public WebHeaderCollection Headers;
        }
    }
}

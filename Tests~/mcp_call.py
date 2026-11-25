"""Call the local UnityAgent MCP endpoint and return a distinct exit status per failure layer."""

import argparse
import json
import os
import sys
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, Request, build_opener


EXIT_HTTP = 10
EXIT_JSON_RPC = 11
EXIT_TOOL = 12
EXIT_TARGET = 13
EXIT_VERSION = 14


class Failure(Exception):
    def __init__(self, exit_code, kind, tool, reason, suggestions, **fields):
        super().__init__(reason)
        self.exit_code = exit_code
        self.report = dict(kind=kind, tool=tool, reason=reason, suggestions=suggestions, **fields)


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class UnityMCP:
    def __init__(self, endpoint, token, timeout, lease_token=None):
        self.endpoint = endpoint
        self.timeout = timeout
        self.opener = build_opener(NoRedirect())
        self.headers = {
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        }
        if lease_token:
            self.headers["X-UnityAgent-Lease"] = lease_token
        self.next_id = 0

    def rpc(self, method, params=None, *, notification=False, tool=None):
        self.next_id += 1
        payload = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            payload["params"] = params
        if not notification:
            payload["id"] = self.next_id
        try:
            request = Request(self.endpoint, json.dumps(payload).encode("utf-8"), self.headers)
            with self.opener.open(request, timeout=self.timeout) as response:
                session_id = response.headers.get("Mcp-Session-Id")
                if session_id:
                    self.headers["Mcp-Session-Id"] = session_id
                if notification:
                    return None
                body = response.read()
        except HTTPError as error:
            raise Failure(EXIT_HTTP, "http", tool, str(error.reason), ["Check the MCP endpoint and HTTP request."], httpStatus=error.code) from error
        except (URLError, OSError, ValueError) as error:
            raise Failure(EXIT_HTTP, "http", tool, str(error), ["Check the MCP endpoint and whether its server is running."]) from error

        try:
            reply = json.loads(body)
        except (ValueError, UnicodeError) as error:
            raise Failure(EXIT_JSON_RPC, "json_rpc", tool, "The endpoint did not return JSON-RPC JSON.", ["Check that the URL ends in /mcp."]) from error
        if not isinstance(reply, dict) or reply.get("jsonrpc") != "2.0":
            raise Failure(EXIT_JSON_RPC, "json_rpc", tool, "Invalid JSON-RPC envelope.", ["Check the endpoint and MCP protocol version."])
        error = reply.get("error")
        if isinstance(error, dict):
            data = error.get("data")
            fields = data if isinstance(data, dict) else {}
            suggestion = "Check the Bearer token and client settings." if error.get("message") == "Unauthorized" else "Check the endpoint and request parameters."
            raise Failure(EXIT_JSON_RPC, "json_rpc", fields.get("tool") or tool,
                          fields.get("reason") or error.get("message", "JSON-RPC failure."),
                          fields.get("suggestions") or [suggestion], rpcCode=error.get("code"),
                          detail=fields.get("detail", data if isinstance(data, str) else None))
        if reply.get("id") != self.next_id or not isinstance(reply.get("result"), dict):
            raise Failure(EXIT_JSON_RPC, "json_rpc", tool, "Missing or mismatched JSON-RPC result.", ["Check the endpoint and MCP protocol version."])
        return reply["result"]

    def initialize(self):
        name = "unityagent-cli-example"
        result = self.rpc("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                         "clientInfo": {"name": name, "version": "1.0"}})
        protocol_version = result.get("protocolVersion")
        if not isinstance(protocol_version, str) or not protocol_version:
            raise Failure(EXIT_JSON_RPC, "json_rpc", None, "Initialize did not negotiate an MCP protocol version.", ["Check that the endpoint is an MCP server."])
        self.headers["MCP-Protocol-Version"] = protocol_version
        self.rpc("notifications/initialized", notification=True)
        return dict(name=name, protocolVersion=protocol_version, initialized=True, authenticated=True)

    def call(self, tool, arguments):
        target = arguments.get("name", tool) if tool == "ExecuteUnityTool" else tool
        result = self.rpc("tools/call", {"name": tool, "arguments": arguments}, tool=target)
        if result.get("isError") is True:
            data = result.get("structuredContent")
            data = data if isinstance(data, dict) else {}
            raise Failure(EXIT_TOOL, "tool", data.get("tool") or target,
                          data.get("reason") or "The Unity tool reported an execution failure.",
                          data.get("suggestions") or ["Read the tool result and verify the requested target."],
                          detail=data.get("detail"), content=result.get("content", []))
        return result


def connection_info(client, expected_project, expected_scene):
    tool = "GetUnityAgentInfo"
    result = client.call(tool, {"detail": "connection"})
    try:
        identity = json.loads(result["content"][0]["text"])
        if not isinstance(identity, dict) or not identity["projectPath"]:
            raise ValueError("missing project identity")
    except (IndexError, KeyError, TypeError, ValueError) as error:
        raise Failure(EXIT_TARGET, "target", tool, "Unity did not report its project identity.",
                      ["Check the endpoint and UnityAgent version."]) from error

    if expected_project and os.path.normcase(os.path.realpath(expected_project)) != os.path.normcase(os.path.realpath(identity["projectPath"])):
        raise Failure(EXIT_TARGET, "target", tool, "The connected Unity project differs from --expect-project.",
                      ["Choose the MCP port assigned to the intended project."], identity=identity)
    if expected_scene is not None and expected_scene.replace("\\", "/") != identity.get("scenePath"):
        raise Failure(EXIT_TARGET, "target", tool, "The active scene differs from --expect-scene.",
                      ["Check which scene is active in the intended project."], identity=identity)
    return identity


def require_loaded_version(client, arguments):
    tool = "VerifyLoadedVersion"
    result = client.call("ExecuteUnityTool", {"name": tool, "arguments": arguments,
                                            "resultLimit": 200, "resultMaxChars": 32768})
    try:
        report = json.loads(result["content"][0]["text"])
        reader = "ReadUnityToolResultPage"
        if report.get("nextTool") == reader:
            result_id = report["resultId"]
            offset = 0
            fragments = [report["text"]]
            for _ in range(64):
                if not report["page"]["hasMore"]:
                    report = json.loads("".join(fragments))
                    break
                next_offset = report["page"]["nextOffset"]
                if type(next_offset) is not int or next_offset <= offset:
                    raise ValueError("verification continuation did not advance")
                page = client.call("ExecuteUnityTool", {"name": reader, "arguments": {
                    "resultId": result_id, "offset": next_offset, "limit": 200, "maxChars": 32768}})
                report = json.loads(page["content"][0]["text"])
                if report["resultId"] != result_id or report["page"]["offset"] != next_offset:
                    raise ValueError("verification continuation changed")
                fragments.append(report["text"])
                offset = next_offset
            else:
                raise ValueError("verification result exceeds the CLI's pagination limit")
        verdict = report["verdict"]
    except (AttributeError, IndexError, KeyError, TypeError, ValueError) as error:
        raise Failure(EXIT_VERSION, "version", tool, "Unity did not return a loaded-version verdict.",
                      ["Check the UnityAgent version and VerifyLoadedVersion arguments."]) from error
    if verdict != "matches_expectations":
        raise Failure(EXIT_VERSION, "version", tool, f"Loaded-version verification returned {verdict}.",
                      ["Compare the reported package, loaded assembly, and imports with the intended build."],
                      verification=report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--endpoint", default="http://127.0.0.1:17800/mcp")
    parser.add_argument("--timeout", type=float, default=125)
    parser.add_argument("--expect-project")
    parser.add_argument("--expect-scene")
    parser.add_argument("--require-loaded-version", metavar="JSON",
                        help="Require VerifyLoadedVersion to match these expectations before calling the tool.")
    parser.add_argument("tool", nargs="?", help="Top-level MCP tool. Omit to check connection identity.")
    parser.add_argument("arguments", nargs="?", default="{}", help="JSON object of tool arguments, or - to read JSON from stdin.")
    options = parser.parse_args()
    token = os.environ.get("UNITY_AGENT_MCP_TOKEN")
    if not token:
        parser.error("Set UNITY_AGENT_MCP_TOKEN to the bearer token for this endpoint.")
    try:
        arguments = json.load(sys.stdin) if options.arguments == "-" else json.loads(options.arguments)
        if not isinstance(arguments, dict):
            raise ValueError("expected a JSON object")
        version_args = json.loads(options.require_loaded_version) if options.require_loaded_version is not None else None
        if options.require_loaded_version is not None and not isinstance(version_args, dict):
            raise ValueError("--require-loaded-version expects a JSON object")
    except ValueError as error:
        parser.error(f"Invalid arguments: {error}")

    client = UnityMCP(options.endpoint, token, options.timeout, os.environ.get("UNITY_AGENT_MCP_LEASE_TOKEN"))
    try:
        client_info = client.initialize()
        if not options.tool or options.expect_project is not None or options.expect_scene is not None:
            identity = connection_info(client, options.expect_project, options.expect_scene)
        if version_args is not None:
            verification = require_loaded_version(client, version_args)
        if options.tool:
            output = client.call(options.tool, arguments)
        else:
            output = {"client": client_info, "unity": identity}
            if version_args is not None:
                output["verification"] = verification
    except Failure as error:
        print(json.dumps(error.report, ensure_ascii=True), file=sys.stderr)
        return error.exit_code
    print(json.dumps(output, ensure_ascii=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())

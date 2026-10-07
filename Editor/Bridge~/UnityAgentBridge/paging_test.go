package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"reflect"
	"strings"
	"testing"
	"time"
)

func TestPagingEnvelopeAndSnapshotTextSurviveBridge(t *testing.T) {
	bridge := newBridge("secret")
	unity, reader := startFakeUnity(t, bridge)
	for _, envelope := range []string{
		`{"name":"SetFixture","arguments":{"offset":7,"limit":9},"resultOffset":0,"resultLimit":3,"resultMaxChars":2048}`,
		`{"name":"ReadUnityToolResultPage","arguments":{"resultId":"snapshot","offset":3,"limit":2,"maxChars":1024}}`,
		`{"name":"SetFixture","arguments":{},"resultLimit":"invalid"}`,
	} {
		request := httptest.NewRequest(http.MethodPost, "/mcp", strings.NewReader(`{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ExecuteUnityTool","arguments":`+envelope+`}}`))
		request.Header.Set("Authorization", "Bearer secret")
		request.Header.Set("Accept", "application/json, text/event-stream")
		recorder := httptest.NewRecorder()
		done := make(chan struct{})
		go func() { bridge.handleMCP(recorder, request); close(done) }()
		_ = unity.SetReadDeadline(time.Now().Add(2 * time.Second))
		line, err := reader.ReadString('\n')
		if err != nil {
			t.Fatalf("read call: %v", err)
		}
		var call wireMsg
		if err := json.Unmarshal([]byte(line), &call); err != nil {
			t.Fatalf("parse call: %v", err)
		}
		var expected, actual any
		_ = json.Unmarshal([]byte(envelope), &expected)
		_ = json.Unmarshal(call.Args, &actual)
		if call.Tool != "ExecuteUnityTool" || !reflect.DeepEqual(expected, actual) {
			t.Fatalf("paging envelope changed in transit: %s", call.Args)
		}
		// The bridge must treat the snapshot JSON as text; quotes, Unicode and nested
		// metadata survive without being interpreted, split, or executed again.
		text := `{"resultId":"snapshot","toolName":"SetFixture","text":"line\\n日本語😀","page":{"offset":0,"returned":3,"hasMore":true,"nextOffset":3},"nextTool":"ReadUnityToolResultPage"}`
		response, _ := json.Marshal(wireMsg{Type: "result", ID: call.ID, OK: true, Text: text})
		if _, err := unity.Write(append(response, '\n')); err != nil {
			t.Fatalf("write result: %v", err)
		}
		select {
		case <-done:
		case <-time.After(2 * time.Second):
			t.Fatal("HTTP result timed out")
		}
		var body struct {
			Result struct {
				Content []struct{ Type, Text string }
				IsError bool `json:"isError"`
			}
		}
		if err := json.Unmarshal(recorder.Body.Bytes(), &body); err != nil {
			t.Fatalf("parse HTTP result: %v", err)
		}
		if recorder.Code != http.StatusOK || body.Result.IsError || len(body.Result.Content) != 1 || body.Result.Content[0].Text != text {
			t.Fatalf("snapshot result changed: %s", recorder.Body.String())
		}
	}
}

func TestPagingSchemasKeepFourToolSurfaceAndEnvelopeDefaults(t *testing.T) {
	tools := newBridge("secret").handleToolsList()["tools"].([]any)
	if len(tools) != 4 {
		t.Fatalf("expected four top-level tools, got %d", len(tools))
	}
	for _, raw := range tools {
		tool := raw.(map[string]any)
		properties := tool["inputSchema"].(map[string]any)["properties"].(map[string]any)
		if tool["name"] == "ExecuteUnityTool" {
			for key, expected := range map[string][]int{"resultOffset": {0, 0}, "resultLimit": {50, 1, 200}, "resultMaxChars": {8192, 1024, 32768}} {
				property := properties[key].(map[string]any)
				if property["type"] != "integer" || property["default"] != expected[0] || property["minimum"] != expected[1] {
					t.Fatalf("wrong schema for %s: %#v", key, property)
				}
				if len(expected) == 3 && property["maximum"] != expected[2] {
					t.Fatalf("wrong maximum for %s", key)
				}
			}
			if properties["arguments"].(map[string]any)["additionalProperties"] != true {
				t.Fatal("native tool arguments stopped being arbitrary objects")
			}
		}
		if tool["name"] == "SearchUnityTool" {
			if properties["offset"].(map[string]any)["default"] != 0 || properties["limit"].(map[string]any)["maximum"] != 200 {
				t.Fatal("Search schema paging mismatch")
			}
		}
	}
}

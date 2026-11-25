package main

import (
	"encoding/json"
	"net/http/httptest"
	"testing"
)

func TestToolResponsesDistinguishRPCAndExecutionFailures(t *testing.T) {
	target := effectiveToolName("ExecuteUnityTool", json.RawMessage(`{"name":"ListRootObject","arguments":{}}`))
	recorder := httptest.NewRecorder()
	writeToolCallResult(recorder, json.RawMessage(`1`), callResult{
		Error: "Tool not found.", Reason: "There is no tool named ListRootObject.", ErrCode: -32601, Data: "Did you mean: ListRootObjects",
	}, target)
	var reply struct {
		Error *struct {
			Code int
			Data toolFailure
		}
		Result *struct {
			IsError           bool
			StructuredContent toolFailure
		}
	}
	if err := json.Unmarshal(recorder.Body.Bytes(), &reply); err != nil {
		t.Fatal(err)
	}
	if reply.Result != nil || reply.Error == nil || reply.Error.Code != -32601 || reply.Error.Data.Kind != "json_rpc" ||
		reply.Error.Data.Tool != target || len(reply.Error.Data.Suggestions) != 2 || reply.Error.Data.Suggestions[1] != "ListRootObjects" {
		t.Fatalf("expected tool-scoped JSON-RPC error, got %s", recorder.Body.String())
	}

	recorder = httptest.NewRecorder()
	writeToolCallResult(recorder, json.RawMessage(`2`), callResult{
		Error: "Object missing.", ErrCode: -32000,
		Failure: &toolFailure{Kind: "tool", Tool: "SetPosition", Reason: "Object missing.", Suggestions: []string{"Use the exact hierarchy path."}},
	}, "SetPosition")
	reply.Error, reply.Result = nil, nil
	if err := json.Unmarshal(recorder.Body.Bytes(), &reply); err != nil {
		t.Fatal(err)
	}
	if reply.Error != nil || reply.Result == nil || !reply.Result.IsError || reply.Result.StructuredContent.Tool != "SetPosition" {
		t.Fatalf("expected a tool execution failure, got %s", recorder.Body.String())
	}
}

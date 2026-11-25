package main

import (
	"encoding/json"
	"strings"
)

type toolFailure struct {
	Kind        string            `json:"kind"`
	Tool        string            `json:"tool"`
	Reason      string            `json:"reason"`
	Suggestions []string          `json:"suggestions"`
	Detail      string            `json:"detail,omitempty"`
	Reservation *unityLeaseStatus `json:"reservation,omitempty"`
}

func effectiveToolName(tool string, arguments json.RawMessage) string {
	if tool == "ExecuteUnityTool" {
		var envelope struct {
			Name string `json:"name"`
		}
		if err := json.Unmarshal(arguments, &envelope); err == nil && envelope.Name != "" {
			return envelope.Name
		}
	}
	return tool
}

func protocolToolFailure(tool string, res callResult) toolFailure {
	reason := res.Reason
	if reason == "" {
		reason = res.Error
	}
	suggestion := "Check Unity's state and Console before retrying."
	switch res.ErrCode {
	case -32601:
		suggestion = "Find the correct tool with SearchUnityTool."
	case -32602:
		suggestion = "Check the required arguments and their types with DescribeUnityTool."
	case -32002:
		suggestion = "Inspect editor and dialog state with GetEditorState and AnswerModalDialog with dryRun=true."
	case -32001, errCodeInterrupted:
		suggestion = "Check GetUnityConnectionStatus and verify whether the original operation completed before retrying."
	case errCodeLeaseConflict:
		suggestion = "Check GetUnityLeaseStatus. Supply your current credential or wait for the reservation and outstanding calls to finish."
	}
	reason, _, _ = strings.Cut(reason, "\n")
	suggestions := []string{suggestion}
	if matches, ok := strings.CutPrefix(res.Data, "Did you mean: "); ok {
		for _, name := range strings.Split(matches, ",") {
			suggestions = append(suggestions, strings.TrimSpace(name))
		}
	}
	return toolFailure{Kind: "json_rpc", Tool: tool, Reason: strings.TrimSpace(reason), Suggestions: suggestions, Detail: res.Data, Reservation: res.LeaseStatus}
}

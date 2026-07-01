---
title: Skill Name
description: Short description of what this skill does (1 line, under 50 characters)
tags: tag1, tag2, tag3
---

# Skill Name

## Overview

Explain what this skill does in 2-3 sentences.
- When to use it
- What is ultimately achieved

## Prerequisites

- Required packages (package name `com.example.package`)
- Required components or configuration state
- Things the user should have completed beforehand

## Decision Flow

<!-- If there are multiple approaches, describe criteria for choosing -->
<!-- Delete this section for single-flow skills -->

| Condition | Approach |
|-----------|----------|
| Condition A (e.g., compatible item) | → Simple procedure (recommended) |
| Condition B (e.g., incompatible item) | → Detailed procedure |

## Tools Used

<!-- Brief summary of tool names, parameters, and return values used in this skill -->
<!-- Reference for AI to call tools with accurate signatures -->

| Tool Name | Parameters | Description |
|-----------|-----------|-------------|
| `ToolA` | `(param1, param2)` | What the tool does |
| `ToolB` | `(param1, optionalParam='default')` | What the tool does |

## Procedure

### Step 1: Check Current State

First, understand the current state:
```
<tool name="ToolA">
<arg name="param1">parameter</arg>
</tool>
```
← Check for "XX" in the output.

### Step 2: Main Operation

```
<tool name="ToolB">
<arg name="param1">param1</arg>
<arg name="param2">param2</arg>
</tool>
```
**Important**: Highlight critical points in bold.

### Step 2.5: Conditional Operation (Only When Needed)

<!-- Conditional step. Delete if not needed -->

**When needed**: Only when Step 2 result shows XX

```
<tool name="ToolC">
<arg name="param1">parameter</arg>
</tool>
```

### Step 3: Verify Result

```
<tool name="ToolD">
<arg name="param1">parameter</arg>
</tool>
```
← Success if "XX" is displayed.

### User Confirmation Points

<!-- Explicitly mark where user judgment or action is needed -->

- **After Step N**: "Please verify the result. Shall we continue?"
- **Scene view action**: Have the user click XX in the Scene view

## Tool Call Examples

<!-- Reproduce actual AI ↔ user conversation flows -->
<!-- Multiple patterns improve AI accuracy -->

### Example 1: Basic Usage
```
User: "Do XX"

AI:
1. <tool name="ToolA">
<arg name="param1">avatarName</arg>
</tool> → Check result
2. <tool name="ToolB">
<arg name="param1">avatarName</arg>
<arg name="param2">param</arg>
</tool>
3. "Done. XX has been configured."
```

### Example 2: Advanced (With Conditional Branching)
```
User: "Set up XX under YY conditions"

AI:
1. <tool name="ToolA">
<arg name="param1">avatarName</arg>
</tool> → Confirm condition B applies
2. "Since it's in YY state, proceeding with manual steps."
3. <tool name="ToolE">
<arg name="param1">avatarName</arg>
<arg name="param2">param</arg>
</tool>
4. <tool name="ToolF">
<arg name="param1">avatarName</arg>
<arg name="param2">param</arg>
</tool>
```

### Example 3: Error Recovery
```
AI:
1. <tool name="ToolA">
<arg name="param1">avatarName</arg>
</tool> → Error: XX not found
2. "XX doesn't appear to be configured. Setting up YY first."
3. <tool name="ToolG">
<arg name="param1">avatarName</arg>
</tool>
4. (Restart from Step 1)
```

## Parameter Guide

<!-- Detail key parameters. Delete if parameters are few -->

### paramName
- Type: string / int / float
- Format: `"value1;value2;value3"` (semicolon-separated)
- Default: `"default"`
- How to check: Value shown in `<tool name="InspectTool"></tool>` output

## Common Mistakes

<!-- Bold the mistake pattern → contrast with correct method -->

1. **Doing YY without XX first** → Check state with XX before doing YY
2. **Passing ZZ as parameter** → Correct format is "XX"
3. **Forgetting WW** → Always run WW after YY

## Notes

- Safety notes (non-destructive, undo support)
- Performance impact
- Platform-specific limitations (PC/Quest)
- IMPORTANT: Rules the AI must never violate

## Related Skills

<!-- References to other skills. Delete if not needed -->

- `related-skill-name`: Related operation (e.g., for toggle setup see `object-toggle`)
- `another-skill`: Prerequisite operation

## Troubleshooting

<!-- Error message/symptom → cause → fix -->

- **Error message / symptom**: Cause explanation → Fix with `<tool name="FixTool"></tool>`, or check XX
- **Unexpected result**: Possibly caused by XX → Try YY

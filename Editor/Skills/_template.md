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
[ToolA('parameter')]
```
← Check for "XX" in the output.

### Step 2: Main Operation

```
[ToolB('param1', 'param2')]
```
**Important**: Highlight critical points in bold.

### Step 2.5: Conditional Operation (Only When Needed)

<!-- Conditional step. Delete if not needed -->

**When needed**: Only when Step 2 result shows XX

```
[ToolC('parameter')]
```

### Step 3: Verify Result

```
[ToolD('parameter')]
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
1. [ToolA('avatarName')] → Check result
2. [ToolB('avatarName', 'param')]
3. "Done. XX has been configured."
```

### Example 2: Advanced (With Conditional Branching)
```
User: "Set up XX under YY conditions"

AI:
1. [ToolA('avatarName')] → Confirm condition B applies
2. "Since it's in YY state, proceeding with manual steps."
3. [ToolE('avatarName', 'param')]
4. [ToolF('avatarName', 'param')]
```

### Example 3: Error Recovery
```
AI:
1. [ToolA('avatarName')] → Error: XX not found
2. "XX doesn't appear to be configured. Setting up YY first."
3. [ToolG('avatarName')]
4. (Restart from Step 1)
```

## Parameter Guide

<!-- Detail key parameters. Delete if parameters are few -->

### paramName
- Type: string / int / float
- Format: `"value1;value2;value3"` (semicolon-separated)
- Default: `"default"`
- How to check: Value shown in `[InspectTool()]` output

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

- **Error message / symptom**: Cause explanation → Fix with `[FixTool()]`, or check XX
- **Unexpected result**: Possibly caused by XX → Try YY

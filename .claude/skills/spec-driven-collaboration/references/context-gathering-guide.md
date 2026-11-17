# Context Gathering Guide

> **Purpose:** Detailed guidance for Phase 01 interactive context gathering.
> Loaded by Phase 01 before gathering issue details from the user.

---

## File Discovery Strategies

When identifying affected files for the collaboration document, use these strategies in order:

### Strategy 1: Keyword Search

Extract keywords from the ISSUE_DESCRIPTION and search for matching files:

```
keywords = extract_nouns_and_technical_terms(ISSUE_DESCRIPTION)
FOR keyword in keywords:
    Grep(pattern=keyword, output_mode="files_with_matches", head_limit=10)
```

Prioritize files that match multiple keywords.

### Strategy 2: Git Status

Check recent modifications that may relate to the issue:

```
# Look at conversation context for git status information
# Recent commits and modified files often indicate areas of active work
```

### Strategy 3: Path Pattern Inference

Infer likely file locations from the issue description:

| Issue mentions... | Search pattern |
|-------------------|----------------|
| "test" or "failing test" | `tests/**/*`, `**/*.test.*`, `**/*.spec.*` |
| "skill" or "command" | `src/claude/skills/**/*.md`, `src/claude/commands/*.md` |
| "context file" or "constitution" | `devforgeai/specs/context/*.md` |
| "story" or "STORY-NNN" | `devforgeai/specs/Stories/STORY-*.story.md` |
| "subagent" or "agent" | `.claude/agents/*.md` |
| "hook" | `.claude/hooks/*` |
| "phase" or "workflow" | `src/claude/skills/*/phases/*.md` |
| "CLI" or "validate" | `.claude/scripts/devforgeai_cli/**/*.py` |

### Strategy 4: User Confirmation

Always present findings to the user for confirmation. They may know about files the search missed.

---

## Gathering Attempts

When the user reports prior attempts, gather structured information:

### For Each Attempt, Capture:

1. **What was done** — Specific actions taken, files modified, commands run
   - Ask for file paths where changes were made
   - Ask for the specific code changes (if small enough to describe)

2. **Result observed** — Exact output, error messages, unexpected behavior
   - Request exact error text (not paraphrased)
   - Ask about any partial success

3. **Why it failed** — User's understanding of the failure
   - If user says "I don't know," that's valuable information too
   - If user has a hypothesis, capture it

### Common Attempt Patterns

| User says... | Follow-up |
|-------------|-----------|
| "I tried changing X" | "What was the exact change? What error did you get after?" |
| "I tried restarting" | "Was this a service restart, session restart, or system restart?" |
| "I searched online" | "What solutions did you find? Why didn't they apply here?" |
| "Nothing yet" | Acceptable — document as "No prior attempts" |

---

## Priority Calibration

Help users calibrate priority accurately:

| Priority | Description | Examples |
|----------|-------------|---------|
| **Critical** | All development blocked | Test suite completely broken, CLI crashes on startup |
| **High** | Current story blocked, workaround exists | Specific feature fails, can continue other work |
| **Medium** | Important but not urgent | Performance issue, intermittent failure, design question |
| **Low** | Enhancement or optimization | Better approach desired, code quality improvement |

---

## Extra Constraints

Common extra constraints to watch for:

- **WSL/Linux-specific:** File path differences, line ending issues, permission quirks
- **Backward compatibility:** Cannot change existing APIs, file formats, or interfaces
- **Performance:** Solution must not regress performance metrics
- **Security:** Solution involves auth, secrets, or access control

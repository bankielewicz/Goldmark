---
description: Generate cross-AI collaboration document for sharing issues with external LLMs
argument-hint: [issue-description] [target-ai]
model: opus
allowed-tools: Read, Skill, AskUserQuestion, Glob, Grep
---

# /collaborate - Cross-AI Collaboration

Generate a self-contained collaboration document to share with an external AI (Gemini, ChatGPT, etc.) for joint problem-solving. Claude interactively gathers context, reads actual code files, and produces a complete package ready to paste into the target LLM.

## Lean Orchestration Enforcement

**DO NOT (before skill invocation):**
- ❌ DO NOT read codebase files or constitution docs (skill handles this)
- ❌ DO NOT generate the collaboration document (skill handles this)
- ❌ DO NOT ask detailed technical questions (skill handles interactive gathering)
- ❌ DO NOT read or copy affected files (skill handles code collection)

**DO (command responsibilities only):**
- ✅ MUST capture issue description (from arg or AskUserQuestion)
- ✅ MUST capture target AI name (default: Gemini)
- ✅ MUST set context markers for skill
- ✅ MUST invoke skill immediately after validation

---

## Phase 0: Argument Validation

```
ARG = $ARGUMENTS

IF ARG is empty:
    AskUserQuestion:
        Question: "What issue should we collaborate on with an external AI?"
        Header: "Issue"
        Options:
            - "Test failures I can't resolve"
            - "Architecture decision needing fresh perspective"
            - "Implementation approach — want a second opinion"
            - "Bug that persists after multiple fix attempts"
        multiSelect: false
    ISSUE_DESCRIPTION = user response
    IF cancelled: Display "Collaboration cancelled." → EXIT
ELSE:
    ISSUE_DESCRIPTION = ARG

# Target AI detection (optional second argument or flag)
IF "--target=" flag present in ARGUMENTS:
    TARGET_AI = extracted value (e.g., "Gemini", "ChatGPT", "Copilot")
ELSE:
    TARGET_AI = "Gemini"

Display: "✓ Issue: ${ISSUE_DESCRIPTION}"
Display: "✓ Target AI: ${TARGET_AI}"
```

---

## Phase 1: Invoke Skill

**Issue Description:** ${ISSUE_DESCRIPTION}
**Target AI:** ${TARGET_AI}
**Command:** collaborate

```
Skill(command="spec-driven-collaboration")
```

**Skill handles ALL workflow** including interactive context gathering, constitution file reading, affected file collection, analysis, template population, document generation, and completion report.

---

## Error Handling

| Error | Resolution |
|-------|------------|
| No issue description | AskUserQuestion prompts for description |
| Skill not found | Verify `.claude/skills/spec-driven-collaboration/SKILL.md` exists |
| No affected files identified | Skill asks user to specify file paths |
| Write fails | Verify `tmp/` directory exists at project root |

---

## References

- Skill: `.claude/skills/spec-driven-collaboration/SKILL.md`
- Template: `.claude/skills/spec-driven-collaboration/references/collaboration-prompt-template.md`
- Pattern: `devforgeai/protocols/lean-orchestration-pattern.md`

**Command follows lean orchestration: Validate → Set markers → Invoke skill**

# Collaboration Document Output Template

> **Purpose:** Defines the exact output structure for cross-AI collaboration documents.
> This template is loaded by the skill during Phase 04 and used to structure the output in Phase 05.
> The skill populates each section with gathered context, actual code, and analysis.

---

## Document Header

```markdown
# Collaboration Request: {ISSUE_TITLE}

**From:** Claude Code (Anthropic) — DevForgeAI Framework
**To:** {TARGET_AI} ({TARGET_AI_ORG})
**Date:** {TODAY_DATE}
**Topic:** {TOPIC_SUMMARY}
**Priority:** {PRIORITY}
```

---

## Required Sections (All 10 Mandatory)

### Section 1: Executive Summary

3-5 sentences. State what is broken, the impact, and what input is needed.
No vague language. Be concrete and specific.

### Section 2: Project Context

Brief description of DevForgeAI framework relevant to this issue.
Include ONLY context the target AI needs to understand the problem space.
Reference constitutional constraints that bound the solution.
Mention the dual-path architecture (`src/` vs `.claude/`) if relevant.
State the tech stack: Markdown framework, Claude Code Terminal, Python CLI.

### Section 3: The Specific Problem

Three subsections required:

- **3.1 Current Behavior** — Exact error messages, test output, or unexpected behavior. File paths and line numbers for every reference.
- **3.2 Expected Behavior** — Expected output format, return values, state transitions, test pass criteria.
- **3.3 Impact** — What is blocked or degraded. Story IDs, sprint goals, downstream dependencies.

### Section 4: Code Artifacts

Include ALL relevant code with file paths and line numbers.
Use fenced code blocks with language annotations.
Include: test code, implementation code, configuration files, framework files.

**The target AI has NO access to our filesystem.** Everything they need MUST be in this section.

Format per artifact:

```markdown
### 4.N [Component/File Name]
**File:** `path/to/file.ext` (lines X-Y)
```language
// actual code — not pseudocode, not summaries
`` `
```

Add as many subsections as needed for complete context.

### Section 5: What We've Tried

Document every approach attempted. For each, explain what was done, the result, and WHY it failed. This prevents the target AI from suggesting approaches already known to not work.

Format per attempt:

```markdown
### Attempt N: [Approach Name]
- **What:** [Specific changes made, with file paths]
- **Result:** [Exact output or behavior observed]
- **Why it failed:** [Root cause analysis — not just "it didn't work"]
```

### Section 6: Our Analysis

Three subsections required:

- **6.1 Hypotheses** — Ranked list of suspected root causes with confidence levels. Format: `"1. [Hypothesis] — Confidence: High/Medium/Low — Evidence: [what supports this]"`
- **6.2 Constitutional Constraints** — Specific constraints from the 6 context files that MUST be respected. Quote exact text. Format: `"**[File]:** '[quoted text]' (lines X-Y)"`
- **6.3 What We Think the Solution Might Involve** — Claude's current thinking shared openly. Include specific file paths and code changes being considered.

### Section 7: Specific Questions for Target AI

Numbered list of specific, answerable questions. NOT vague requests like "any thoughts?" — targeted questions that leverage a fresh perspective:

1. Specific technical question about an approach
2. Question about a pattern or technique that might apply
3. Request for review of a specific code section (reference Section 4.N)
4. Question about edge cases or failure modes we may have missed
5. Question about alternative architectural approaches within our constraints

### Section 8: Proposed Plan (For Review)

Phased implementation plan with checkpoints. Each phase independently verifiable.

Format:

```markdown
### Phase N: [Phase Name]
- [ ] Step N.1: [Concrete action with file path]
- [ ] Step N.2: [Concrete action with file path]
- **Checkpoint:** [Specific command or test to verify this phase succeeded]

### Success Criteria
[Test commands, expected output, metrics, existing tests that must continue passing]
```

### Section 9: Files Reference

Quick-reference table of all files mentioned in the document:

```markdown
| File | Purpose | Lines of Interest |
|------|---------|-------------------|
| `path/to/file` | Brief description | Lines X-Y |
```

### Section 10: Constitutional Compliance Checklist

Both Claude and the target AI should verify proposed solutions against:

```markdown
- [ ] Solution does not introduce technologies outside `tech-stack.md`
- [ ] Solution respects `source-tree/` dual-path architecture (src/ vs .claude/)
- [ ] Solution stays within `dependencies.md` boundaries (no new packages for core framework)
- [ ] Solution follows `coding-standards.md` formatting and naming conventions
- [ ] Solution respects `architecture-constraints.md` layer rules (Commands → Skills → Subagents)
- [ ] Solution avoids all 11 patterns in `anti-patterns.md`
- [ ] Solution is non-aspirational (concrete, implementable, testable today)
- [ ] No technical debt introduced
- [ ] No regression in existing code, features, or functionality
- [ ] All proposed changes include test coverage
```

---

## Quality Rules

These rules MUST be followed when populating the template:

1. **Non-aspirational:** Every recommendation must be implementable with specific file paths and code changes. No "we should consider" or "it would be nice to" language.
2. **Actual code:** Include real code from the codebase, not pseudocode or summaries. The target AI needs to see the actual implementation.
3. **Constitution-compliant:** Cross-reference all 6 context files. Quote constraints verbatim when relevant.
4. **Complete context:** The target AI has NO access to our filesystem. Everything they need must be IN this document.
5. **Collaborative tone:** Frame as peer-to-peer problem-solving, not as a help request. Share analysis and hypotheses openly — there is power in collaboration rather than working in isolation.
6. **No regression:** Explicitly note what existing functionality must NOT break. List specific tests that must continue passing.
7. **Phased plan with checkpoints:** Include verification steps after each phase so progress can be independently confirmed.
8. **Targeted questions:** Ask the target AI specific, answerable questions — not vague "what do you think?" prompts.

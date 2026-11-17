# Phase 04: Analysis & Template Population

Load the output template, reason through the issue, and populate all 10 sections.

---

## Pre-Flight: Verify Phase 03

```
VERIFY Phase 03 completed:
  - CODE_ARTIFACTS[] has ≥1 entry
  - All artifacts have {file_path, content, line_range, type}
  - Error output captured

IF any missing: HALT — "Phase 03 incomplete. Return to Phase 03."
```

---

## Reference Loading

Load BOTH reference files fresh. Do NOT rely on memory of previous reads.

```
EXECUTE:
  Read(file_path=".claude/skills/spec-driven-collaboration/references/collaboration-prompt-template.md")
  Read(file_path=".claude/skills/spec-driven-collaboration/references/analysis-reasoning-guide.md")

VERIFY: Both files loaded successfully (non-empty content)
IF either Read fails: HALT — "Phase 04 reference files not loaded"
```

---

## Step 04.1: Load Template

```
EXECUTE:
  Read the collaboration-prompt-template.md reference file FULLY.
  Do NOT use offset or limit — read the complete template.

VERIFY:
  - Template content loaded
  - Template contains all 10 section headers
  - Template contains quality rules section

RECORD: "Step 04.1 complete — Template loaded with all 10 sections"
```

---

## Step 04.2: Reason Through the Issue

Before populating sections, work through these reasoning steps systematically. This reasoning informs the quality of every section that follows.

```
EXECUTE:
  Reason through each question and document your analysis:

  1. ROOT CAUSE: What is the most likely root cause?
     - Rank hypotheses by confidence (High/Medium/Low)
     - Cite evidence for each hypothesis

  2. ATTEMPT ANALYSIS: For each entry in ATTEMPTS[]:
     - Why specifically did this approach fail?
     - What does the failure tell us about the root cause?

  3. CONSTITUTIONAL RELEVANCE: For each entry in RELEVANT_CONSTRAINTS[]:
     - How tightly does this constraint bind the solution space?
     - Which constraints the target AI is most likely to violate unknowingly?

  4. EXTERNAL CONTEXT GAP: What does the target AI need to know that they
     could NOT infer from our code alone?
     - Framework conventions (dual-path architecture, spec-driven workflow)
     - Environment specifics (WSL quirks, Claude Code Terminal constraints)
     - Project-specific patterns or decisions

  5. TARGETED QUESTIONS: What specific questions would leverage the target
     AI's fresh perspective most effectively?
     - Each question must be answerable from the information in the document
     - No vague "any thoughts?" — specific, bounded questions

  6. PROPOSED PLAN: What phased approach would we attempt next?
     - Each phase independently verifiable
     - Checkpoints after each phase

VERIFY:
  - All 6 reasoning areas documented
  - Hypotheses ranked with confidence levels
  - At least 4 targeted questions drafted
  - Proposed plan has ≥2 phases with checkpoints

RECORD: "Step 04.2 complete — Issue reasoning documented"
```

---

## Step 04.3: Populate All 10 Sections

Using the template structure, populate every section with gathered data.

```
EXECUTE:
  Populate each section using the specified source data:

  | Section | Source Data | Key Requirements |
  |---------|------------|------------------|
  | 1. Executive Summary | ISSUE_DESCRIPTION + PRIORITY + impact | 3-5 sentences, concrete, no vague language |
  | 2. Project Context | Framework description + CONSTITUTION_SUMMARY | Only context target AI needs |
  | 3. The Specific Problem | Current/Expected behavior + error output | 3 subsections: Current, Expected, Impact |
  | 4. Code Artifacts | CODE_ARTIFACTS[] | Actual code with paths and line numbers |
  | 5. What We've Tried | ATTEMPTS[] | Each: What, Result, Why it failed |
  | 6. Our Analysis | Hypotheses + RELEVANT_CONSTRAINTS[] + ideas | 3 subsections: Hypotheses, Constraints, Solutions |
  | 7. Questions for Target AI | Targeted questions from Step 04.2 | 4-6 specific, answerable questions |
  | 8. Proposed Plan | Phased plan from Step 04.2 | Checkpoints per phase, success criteria |
  | 9. Files Reference | All files mentioned in document | Table: File, Purpose, Lines of Interest |
  | 10. Compliance Checklist | 10 constitutional compliance checks | Checkboxes for both Claude and target AI |

  Quality rules (from template):
  - NON-ASPIRATIONAL: Every recommendation implementable with specific file paths
  - ACTUAL CODE: Real code from codebase, not pseudocode or summaries
  - CONSTITUTION-COMPLIANT: Quote constraints verbatim with line numbers
  - COMPLETE CONTEXT: Target AI has NO filesystem access — everything needed is IN the document
  - COLLABORATIVE TONE: Peer-to-peer problem-solving, not a help request
  - NO REGRESSION: Note what existing functionality must NOT break
  - PHASED PLAN: Include verification steps after each phase
  - TARGETED QUESTIONS: Specific, answerable — not vague "what do you think?"

VERIFY:
  - All 10 sections populated
  - No empty sections or placeholder text
  - Section 4 contains actual code in fenced blocks with language annotations
  - Section 6.2 contains verbatim constitution quotes with line numbers
  - Section 7 has 4-6 specific questions
  - Section 8 has phased plan with checkpoints
  - Section 10 has 10 compliance checkboxes

RECORD: "Step 04.3 complete — All 10 sections populated"
```

---

## Step 04.4: Compose Document Header

```
EXECUTE:
  Create the document header with metadata:

  ```markdown
  # Collaboration Request: {ISSUE_TITLE}

  **From:** Claude Code (Anthropic) — DevForgeAI Framework
  **To:** {TARGET_AI} ({TARGET_AI_ORG})
  **Date:** {TODAY_DATE}
  **Topic:** {TOPIC_SUMMARY}
  **Priority:** {PRIORITY}
  ```

  Where:
  - ISSUE_TITLE = concise title derived from ISSUE_DESCRIPTION
  - TARGET_AI_ORG = inferred (Gemini→Google, ChatGPT→OpenAI, Copilot→Microsoft)
  - TODAY_DATE = current date
  - TOPIC_SUMMARY = 1-sentence summary of the collaboration topic

VERIFY:
  - Header has all 5 metadata fields populated
  - No placeholder tokens remaining

RECORD: "Step 04.4 complete — Document header composed"
```

---

## Phase 04 Gate

```
VERIFY:
  - [ ] Template loaded fresh from reference file
  - [ ] All 6 reasoning areas documented
  - [ ] All 10 sections populated (no empty sections, no placeholders)
  - [ ] Code artifacts contain actual code (not summaries)
  - [ ] Constitutional constraints quoted verbatim with line numbers
  - [ ] 4-6 targeted, answerable questions crafted
  - [ ] Phased plan has checkpoints per phase
  - [ ] Document header composed with all metadata

IF any section is empty or contains placeholder text: HALT — populate it
IF code artifacts are summaries instead of actual code: HALT — include real code
IF all checks pass: Phase 04 PASSED — proceed to Phase 05
```

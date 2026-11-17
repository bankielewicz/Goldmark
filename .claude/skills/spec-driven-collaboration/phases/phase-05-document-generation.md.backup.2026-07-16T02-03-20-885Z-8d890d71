# Phase 05: Document Generation

Write the collaboration document to `tmp/` and verify the write.

---

## Reference Loading

```
# No required references for this phase. Self-contained — assembles the
# document from in-context Phase 02-04 outputs and writes to tmp/. The
# Step 05.3 Read of OUTPUT_PATH is a write-back verification, not a
# reference load.
```

---

## Pre-Flight: Verify Phase 04

```
VERIFY Phase 04 completed:
  - All 10 sections populated
  - Document header composed
  - No empty sections or placeholder text

IF any missing: HALT — "Phase 04 incomplete. Return to Phase 04."
```

---

## Step 05.1: Generate Filename and Path

```
EXECUTE:
  Generate the output filename:
    - Format: collaborate-{target_ai}-{issue_slug}-{date}.md
    - target_ai: lowercase TARGET_AI (e.g., "gemini", "chatgpt", "copilot")
    - issue_slug: slugified ISSUE_TITLE, max 40 characters, lowercase, hyphens
    - date: TODAY_DATE in YYYY-MM-DD format

  Example: collaborate-gemini-phase-state-tracking-2026-03-19.md

  OUTPUT_PATH = "tmp/" + FILENAME

  Verify tmp/ directory exists:
    Glob(pattern="tmp/", path=".")
    IF not found: Note that Write() will create it

VERIFY:
  - FILENAME follows the format pattern
  - OUTPUT_PATH starts with "tmp/"
  - No invalid characters in filename

RECORD: "Step 05.1 complete — Output path: {OUTPUT_PATH}"
```

---

## Step 05.2: Write Document

Assemble and write the complete collaboration document.

```
EXECUTE:
  Assemble the document in this order:
    1. Document header (From/To/Date/Topic/Priority)
    2. Section 1: Executive Summary
    3. Section 2: Project Context
    4. Section 3: The Specific Problem (3 subsections)
    5. Section 4: Code Artifacts (multiple subsections with fenced code)
    6. Section 5: What We've Tried (per attempt)
    7. Section 6: Our Analysis (3 subsections)
    8. Section 7: Specific Questions for {TARGET_AI}
    9. Section 8: Proposed Plan (phased with checkpoints)
    10. Section 9: Files Reference (table)
    11. Section 10: Constitutional Compliance Checklist

  Write(file_path=OUTPUT_PATH, content=assembled_document)

VERIFY:
  - Write() completed without error
  - No error message returned

RECORD: "Step 05.2 complete — Document written to {OUTPUT_PATH}"
```

**Document requirements:**
- Actual code in fenced blocks with language annotations (```python, ```typescript, etc.)
- Constitutional constraints quoted verbatim with (Source: file, lines X-Y) citations
- All file paths use project-relative format
- Compliance checklist uses markdown checkboxes `- [ ]`
- Collaborative tone throughout (peer-to-peer, not help request)

---

## Step 05.3: Verify Write

Read back the written document and verify completeness.

```
EXECUTE:
  Read(file_path=OUTPUT_PATH)

  Check the document content for:
    1. All 10 section headers present
    2. At least 1 fenced code block (``` delimiter)
    3. Document length ≥ 100 lines
    4. No placeholder tokens ({VARIABLE_NAME} patterns that weren't replaced)
    5. Compliance checklist present with 10 items
    6. Header metadata complete (From, To, Date, Topic, Priority)

VERIFY:
  - [ ] File exists and is readable
  - [ ] Contains all 10 section headers
  - [ ] Contains ≥1 fenced code block
  - [ ] File is ≥100 lines
  - [ ] No unreplaced placeholder tokens
  - [ ] Compliance checklist has 10 items

RECORD: "Step 05.3 complete — Document verified: {line_count} lines, {section_count}/10 sections"
```

---

## Phase 05 Gate

```
VERIFY:
  - [ ] Document written to tmp/ directory
  - [ ] File verified via read-back
  - [ ] All 10 section headers present
  - [ ] At least 1 fenced code block
  - [ ] File ≥ 100 lines
  - [ ] No placeholder tokens remaining

IF file verification fails: HALT — "Document generation incomplete"
IF file < 50 lines: HALT — "Document suspiciously short, likely missing sections"
IF placeholder tokens found: HALT — "Unreplaced variables in document"
IF all checks pass: Phase 05 PASSED — proceed to Phase 06
```

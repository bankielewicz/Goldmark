# Phase 06: Completion Report

Display summary and next steps to the user.

---

## Reference Loading

```
# No required references for this phase. Display-only — operates on
# in-context statistics gathered from prior phases. No file Reads.
```

---

## Pre-Flight: Verify Phase 05

```
VERIFY Phase 05 completed:
  - Document written to OUTPUT_PATH
  - Document verified (≥100 lines, all 10 sections, no placeholders)

IF any missing: HALT — "Phase 05 incomplete. Return to Phase 05."
```

---

## Step 06.1: Compile Summary Statistics

```
EXECUTE:
  Gather statistics from the completed workflow:
    - SECTION_COUNT: Number of populated sections (should be 10/10)
    - CODE_ARTIFACT_COUNT: Number of entries in CODE_ARTIFACTS[]
    - QUESTION_COUNT: Number of questions in Section 7
    - CONSTRAINT_COUNT: Number of entries in RELEVANT_CONSTRAINTS[]
    - ATTEMPT_COUNT: Number of entries in ATTEMPTS[]
    - LINE_COUNT: Total lines in the output document

VERIFY:
  - All statistics are positive integers
  - SECTION_COUNT = 10

RECORD: "Step 06.1 complete — Statistics compiled"
```

---

## Step 06.2: Display Completion Report

Display the following report to the user. This is the final output of the skill.

```
EXECUTE:
  Display to user:

  ---

  ## Collaboration Document Generated

  **Output file:** `{OUTPUT_PATH}`
  **Target AI:** {TARGET_AI}
  **Priority:** {PRIORITY}
  **Document size:** {LINE_COUNT} lines

  ### Document Summary
  - **Sections:** {SECTION_COUNT}/10 complete
  - **Code files included:** {CODE_ARTIFACT_COUNT}
  - **Questions for {TARGET_AI}:** {QUESTION_COUNT}
  - **Constitutional constraints cited:** {CONSTRAINT_COUNT}
  - **Attempts documented:** {ATTEMPT_COUNT}

  ### Next Steps
  1. Open `{OUTPUT_PATH}` and review the document
  2. Copy the full contents
  3. Paste into {TARGET_AI} for collaborative review
  4. Share {TARGET_AI}'s response back here for integration

  > There is power in collaboration rather than working in isolation.

  ---

VERIFY:
  - Report displayed with all statistics filled
  - OUTPUT_PATH shown to user
  - Next steps include all 4 items

RECORD: "Step 06.2 complete — Completion report displayed"
```

---

## Phase 06 Gate (Final)

```
VERIFY:
  - [ ] Summary statistics compiled and accurate
  - [ ] Completion report displayed to user
  - [ ] Output file path clearly communicated
  - [ ] Next steps provided

IF all checks pass: Phase 06 PASSED — WORKFLOW COMPLETE

WORKFLOW VALIDATION:
  - Phases completed: 6/6
  - Document generated: {OUTPUT_PATH}
  - Status: SUCCESS
```

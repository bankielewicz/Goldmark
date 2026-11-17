# Phase 02: Constitution Loading

Read all 6 constitutional context files and extract constraints relevant to the issue.

---

## Reference Loading

```
Read(file_path="devforgeai/specs/context/tech-stack.md")
Read(file_path="devforgeai/specs/context/source-tree/governance.json")
Read(file_path="devforgeai/specs/context/dependencies.md")
Read(file_path="devforgeai/specs/context/coding-standards.md")
Read(file_path="devforgeai/specs/context/architecture-constraints.md")
Read(file_path="devforgeai/specs/context/anti-patterns.md")
```

IF any Read fails: HALT -- "Phase 02 constitution files not loaded — collaboration document cannot ground its constraints section."

---

## Pre-Flight: Verify Phase 01

```
VERIFY Phase 01 completed:
  - ISSUE_DESCRIPTION is non-empty
  - TARGET_AI is set
  - AFFECTED_FILES has ≥1 file
  - ATTEMPTS captured
  - PRIORITY set

IF any missing: HALT — "Phase 01 incomplete. Return to Phase 01."
```

---

## Step 02.1: Load All Constitution Files

Load all 6 constitutional context files. Read them in parallel where possible.

```
EXECUTE:
  Read(file_path="devforgeai/specs/context/tech-stack.md")
  Read(file_path="devforgeai/specs/context/source-tree/governance.json")
  Read(file_path="devforgeai/specs/context/dependencies.md")
  Read(file_path="devforgeai/specs/context/coding-standards.md")
  Read(file_path="devforgeai/specs/context/architecture-constraints.md")
  Read(file_path="devforgeai/specs/context/anti-patterns.md")

VERIFY:
  - All 6 files loaded successfully (non-empty content)
  - Count of successfully loaded files = 6

RECORD: "Step 02.1 complete — All 6 constitution files loaded"
```

IF any file fails to load: **HALT** — constitution files are required. Report which file(s) failed and verify file paths.

---

## Step 02.2: Extract Relevant Constraints

For each loaded constitution file, identify constraints that apply to the current issue.

```
EXECUTE:
  For each of the 6 files, scan content and identify constraints that:
    1. Directly relate to ISSUE_DESCRIPTION keywords or concepts
    2. Apply to any file in AFFECTED_FILES (matching paths, layers, or patterns)
    3. Would restrict potential solutions the target AI might suggest

  For each relevant constraint found, capture:
    - file: which constitution file (e.g., "tech-stack.md")
    - constraint_text: the exact quoted text of the constraint
    - line_numbers: the line range where the constraint appears

VERIFY:
  - RELEVANT_CONSTRAINTS[] has at least 1 entry
  - Each entry has {file, constraint_text, line_numbers}
  - constraint_text is a verbatim quote (not paraphrased)

RECORD: "Step 02.2 complete — {count} relevant constraints extracted"
```

Store: `RELEVANT_CONSTRAINTS[]` — list of {file, constraint_text, line_numbers}

**Why verbatim quotes matter:** The target AI needs exact constraint text to avoid suggesting solutions that violate constitutional rules. Paraphrased constraints lose precision and can lead to ambiguous compliance.

---

## Step 02.3: Summarize Constitution Context

Create a brief summary of the constitutional landscape for the collaboration document.

```
EXECUTE:
  Compose a 3-5 sentence summary of:
    - Which constitution files are most relevant to this issue
    - The most binding constraints (those that most restrict solution space)
    - Any constitution-level conflicts or tensions relevant to the issue

VERIFY:
  - Summary is 3-5 sentences
  - References specific constitution files by name
  - Identifies at least 1 binding constraint

RECORD: "Step 02.3 complete — Constitution summary composed"
```

Store: `CONSTITUTION_SUMMARY` — text summary for Section 2 of the document

---

## Phase 02 Gate

```
VERIFY:
  - [ ] All 6 constitution files loaded successfully
  - [ ] At least 1 relevant constraint identified with verbatim quote and line numbers
  - [ ] Constitution summary composed (3-5 sentences)

IF any file failed to load: HALT — constitution files are required
IF no constraints found: HALT — review ISSUE_DESCRIPTION for broader keyword matching
IF all checks pass: Phase 02 PASSED — proceed to Phase 03
```

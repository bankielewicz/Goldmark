# Phase 03: Code Collection

Read each affected file and capture actual source code for the collaboration document.

---

## Pre-Flight: Verify Phase 02

```
VERIFY Phase 02 completed:
  - All 6 constitution files loaded
  - RELEVANT_CONSTRAINTS[] has ≥1 entry
  - CONSTITUTION_SUMMARY composed

IF any missing: HALT — "Phase 02 incomplete. Return to Phase 02."
```

---

## Reference Loading

```
EXECUTE: Read(file_path=".claude/skills/spec-driven-collaboration/references/code-collection-patterns.md")
VERIFY: File content loaded successfully (non-empty)
IF Read fails: HALT — "Phase 03 reference file not loaded"
Do NOT rely on memory of previous reads. Load reference fresh.
```

---

## Step 03.1: Read Affected Files

Read each file in AFFECTED_FILES and capture relevant source code.

```
EXECUTE:
  FOR each file in AFFECTED_FILES:
    content = Read(file_path=file)

    IF file length > 200 lines:
      # Capture the most relevant sections
      relevant_sections = Grep(pattern=<issue keywords>, path=file, output_mode="content", -C=25)
      Store: {file_path, relevant_sections with line numbers, type: "source"}

    ELSE:
      # Store full content for short files
      Store: {file_path, full content with line numbers, type: "source"}

VERIFY:
  - Each file in AFFECTED_FILES was read successfully
  - For each file: content captured with line numbers
  - Content is ACTUAL source code, not summaries or descriptions

RECORD: "Step 03.1 complete — {count} source files read"
```

---

## Step 03.2: Collect Related Test Files

Find and read test files associated with affected files.

```
EXECUTE:
  FOR each file in AFFECTED_FILES:
    # Determine test file patterns based on file type and conventions
    # Examples:
    #   src/module.py → tests/test_module.py, tests/module_test.py
    #   src/Component.tsx → src/__tests__/Component.test.tsx
    #   src/service.ts → tests/service.spec.ts

    test_candidates = Glob(pattern matching test naming conventions for this file)

    IF test files found:
      FOR each test_file in test_candidates:
        Read(file_path=test_file)
        Store: {file_path: test_file, content with line numbers, type: "test"}

VERIFY:
  - Test file search executed for each affected file
  - Found test files read and content captured
  - Test content includes line numbers

RECORD: "Step 03.2 complete — {count} test files collected"
```

Note: It is acceptable to find 0 test files if the affected files are configuration or documentation files.

---

## Step 03.3: Collect Error Output

Capture any error messages, stack traces, or unexpected output mentioned in the issue.

```
EXECUTE:
  IF error messages or stack traces were mentioned in ISSUE_DESCRIPTION or ATTEMPTS:
    - Extract exact error text from conversation context
    - Include full stack traces (not summaries)
    - Include any relevant log output
    Store: {type: "error", content: exact error text}

  IF no errors mentioned:
    Store: {type: "error", content: "No error output reported — issue may be behavioral"}

VERIFY:
  - Error output section populated (even if "no errors reported")
  - If errors exist: full text captured, not truncated

RECORD: "Step 03.3 complete — Error output captured"
```

---

## Step 03.4: Assemble Code Artifacts

Compile all collected code into the CODE_ARTIFACTS list.

```
EXECUTE:
  Assemble CODE_ARTIFACTS[] from:
    - Source files (Step 03.1)
    - Test files (Step 03.2)
    - Error output (Step 03.3)

  Each artifact must have:
    - file_path: absolute or project-relative path
    - content: actual code with line numbers
    - line_range: which lines are included (e.g., "1-150" or "42-98")
    - type: "source" | "test" | "config" | "error"

VERIFY:
  - CODE_ARTIFACTS[] has at least 1 entry
  - All entries have {file_path, content, line_range, type}
  - Content is actual code/text, not summaries

RECORD: "Step 03.4 complete — {count} code artifacts assembled"
```

Store: `CODE_ARTIFACTS[]` — list of {file_path, content, line_range, type}

---

## Phase 03 Gate

```
VERIFY:
  - [ ] All AFFECTED_FILES read successfully (or warned and skipped)
  - [ ] At least 1 code artifact captured in CODE_ARTIFACTS[]
  - [ ] All artifacts include file paths and line numbers
  - [ ] Code is ACTUAL source, not summaries
  - [ ] Error output captured (even if "none reported")

IF any affected file failed to read: Warn user, continue with available files
IF CODE_ARTIFACTS is empty: HALT — no code collected, cannot generate useful document
IF all checks pass: Phase 03 PASSED — proceed to Phase 04
```

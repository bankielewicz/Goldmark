# Analysis & Reasoning Guide

> **Purpose:** Guidance for Phase 04 reasoning — hypothesis ranking, constraint analysis, question crafting, and plan development.
> Loaded by Phase 04 before reasoning through the issue and populating template sections.

---

## Hypothesis Ranking

When generating root cause hypotheses, follow this structured approach:

### Confidence Levels

| Level | Criteria |
|-------|----------|
| **High** | Direct evidence supports this hypothesis. Error messages, stack traces, or code inspection point here. Multiple independent signals converge. |
| **Medium** | Circumstantial evidence. The hypothesis is consistent with observed behavior but could also be explained by other causes. Some signals support it. |
| **Low** | Speculative but worth exploring. Based on pattern matching or experience rather than direct evidence. Only one weak signal. |

### Hypothesis Format

```markdown
1. [Root cause description] — Confidence: High
   - **Evidence:** [Specific error message, code line, or behavior that supports this]
   - **Would explain:** [Which symptoms this hypothesis accounts for]
   - **Would NOT explain:** [Which symptoms remain unexplained]
```

### Common Hypothesis Categories

| Category | Signs | Example |
|----------|-------|---------|
| **Logic error** | Wrong output, test assertion mismatch | Off-by-one, wrong conditional, missing edge case |
| **Configuration** | Works in one env, fails in another | Missing env var, wrong path, version mismatch |
| **State management** | Intermittent failures, order-dependent | Race condition, stale cache, leaked state |
| **Integration** | Works in isolation, fails together | API contract mismatch, schema drift, wrong format |
| **Environment** | WSL-specific, platform-specific | Line endings, path separators, permissions |

---

## Constraint Analysis

When analyzing constitutional constraints, categorize them by impact on the solution:

### Constraint Impact Classification

| Impact | Description | Action |
|--------|-------------|--------|
| **Binding** | Directly eliminates solution approaches | Quote verbatim. Explain WHY it eliminates options. |
| **Guiding** | Shapes the solution without eliminating approaches | Quote verbatim. Note how it influences the direction. |
| **Contextual** | Provides background the target AI needs | Summarize in Section 2 (Project Context). |

### Format for Section 6.2

```markdown
**tech-stack.md:** 'Python 3.11+ for CLI tools, Node.js for test framework' (lines 12-14)
→ Impact: Solution MUST use Python for any CLI changes, not Ruby/Go/Rust.

**anti-patterns.md:** 'Category 1: Bash(command="cat file.txt") — Use Read() instead' (lines 8-12)
→ Impact: Solution cannot use Bash for file operations.
```

---

## Question Crafting

The questions in Section 7 are the most valuable part of the document. They direct the target AI's fresh perspective toward specific, useful areas.

### Good Questions vs. Bad Questions

| Bad (vague) | Good (targeted) |
|-------------|-----------------|
| "Any thoughts on this?" | "Does the order of operations in `phase_check()` (Section 4.2, lines 45-60) handle the case where `phase-state.json` doesn't exist yet?" |
| "What would you do?" | "Given the constraint that we can't add new npm packages (Section 6.2), what alternative approach to file watching would you recommend for the hook system?" |
| "Is this approach correct?" | "We're considering extracting `validate_section()` into a shared utility (Section 6.3). Would this create a circular dependency with the `phase_commands` module that imports it?" |
| "How can we fix this?" | "The stack trace (Section 3.1) shows the error originates in `extract_section()` at line 87, but the regex pattern looks correct. Is there a Unicode or encoding edge case we're missing?" |

### Question Categories

Aim for 4-6 questions spread across these categories:

1. **Root cause verification** — "Does our top hypothesis (Section 6.1) seem correct based on the code in Section 4.N?"
2. **Blind spot detection** — "What edge cases or failure modes are we likely missing in [specific function]?"
3. **Alternative approach** — "Within our constraints (Section 6.2), what alternative pattern would you suggest for [specific problem]?"
4. **Code review** — "Review the implementation in Section 4.N — do you see any logic errors or anti-patterns?"
5. **Architecture** — "Would [proposed change] violate the architectural constraints in Section 6.2?"
6. **Experience-based** — "Have you seen this pattern of [specific behavior] before? What was the root cause?"

---

## Plan Development

The proposed plan in Section 8 should be actionable and verifiable.

### Plan Structure

Each phase should have:
1. **Clear scope** — What this phase accomplishes
2. **Concrete steps** — Specific file paths and code changes
3. **Checkpoint** — How to verify this phase succeeded before moving on
4. **Rollback** — How to undo this phase if it fails (optional but valuable)

### Plan Template

```markdown
### Phase 1: [Diagnostic]
- [ ] Run specific test: `pytest tests/test_module.py -v`
- [ ] Add logging to `src/module.py` line 45 to capture state
- [ ] Reproduce the error with logging enabled
- **Checkpoint:** Error reproduced with additional diagnostic output

### Phase 2: [Fix]
- [ ] Modify `src/module.py` function `process()` to handle edge case
- [ ] Update test assertions in `tests/test_module.py`
- **Checkpoint:** All existing tests pass + new test for edge case passes

### Phase 3: [Verify]
- [ ] Run full test suite: `npm test`
- [ ] Verify no regressions in related modules
- **Checkpoint:** 100% test pass rate, no new warnings

### Success Criteria
- Original error no longer reproducible
- All existing tests continue to pass
- New test covers the discovered edge case
- No constitution file violations
```

### Plan Quality Checks

Before including the plan in Section 8:
- [ ] Every step references a specific file path
- [ ] Every phase has a checkpoint
- [ ] Success criteria are measurable (test commands, expected output)
- [ ] Plan respects constitutional constraints (no forbidden technologies, no anti-patterns)
- [ ] Steps are ordered logically (diagnose before fix, fix before verify)

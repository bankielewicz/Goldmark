# Code Collection Patterns

> **Purpose:** Patterns for discovering related files, associated tests, and error output during Phase 03.
> Loaded by Phase 03 before reading affected files.

---

## Test File Discovery

Given a source file, find its associated test files using these naming conventions:

### Python

| Source File | Test File Patterns |
|-------------|-------------------|
| `src/module.py` | `tests/test_module.py`, `tests/module_test.py` |
| `src/package/module.py` | `tests/package/test_module.py`, `tests/test_package_module.py` |
| `.claude/scripts/devforgeai_cli/commands/phase_commands.py` | `.claude/scripts/devforgeai_cli/tests/test_phase_commands.py` |

Glob patterns:
```
Glob(pattern="**/test_{basename}.py")
Glob(pattern="**/{basename}_test.py")
Glob(pattern="tests/**/test_{basename}.py")
```

### JavaScript / TypeScript

| Source File | Test File Patterns |
|-------------|-------------------|
| `src/Component.tsx` | `src/__tests__/Component.test.tsx`, `src/Component.spec.tsx` |
| `src/utils/helper.ts` | `tests/utils/helper.test.ts`, `src/utils/helper.spec.ts` |

Glob patterns:
```
Glob(pattern="**/{basename}.test.{ext}")
Glob(pattern="**/{basename}.spec.{ext}")
Glob(pattern="**/__tests__/{basename}.test.{ext}")
```

### Shell / Bash

| Source File | Test File Patterns |
|-------------|-------------------|
| `scripts/build.sh` | `tests/test-build.sh`, `devforgeai/tests/*/test-*.sh` |

Glob patterns:
```
Glob(pattern="**/test-{basename}.sh")
Glob(pattern="devforgeai/tests/**/test-*.sh")
```

### Markdown (Skills / Commands)

Skills and commands don't have traditional test files. Related files include:
- Phase files: `phases/phase-*.md`
- Reference files: `references/*.md`
- Associated command: `.claude/commands/{command-name}.md`

---

## File Reading Strategy

### Short Files (≤200 lines)
Read the entire file. Include all content with line numbers.

### Long Files (>200 lines)
Use targeted extraction:

1. **Keyword search:** Grep for issue-related keywords with context
   ```
   Grep(pattern=<keyword>, path=<file>, output_mode="content", -C=25)
   ```

2. **Function/class extraction:** If the issue is about a specific function or class:
   ```
   Grep(pattern="def {function_name}|class {class_name}", path=<file>, output_mode="content", -A=50)
   ```

3. **Error line extraction:** If a stack trace points to specific lines:
   ```
   Read(file_path=<file>, offset=<error_line - 20>, limit=40)
   ```

### Configuration Files
Always read configuration files in full, regardless of length:
- `package.json`, `tsconfig.json`, `pyproject.toml`
- `.claude/settings.json`, `.claude/settings.local.json`
- `devforgeai/specs/context/*.md` (already loaded in Phase 02)

---

## Error Output Capture

### What to Capture

| Error Type | What to Include |
|-----------|----------------|
| **Stack trace** | Full trace, not truncated. Include file paths and line numbers. |
| **Test failure** | Test name, assertion message, expected vs actual values |
| **CLI error** | Full command that was run, exit code, stderr output |
| **Build error** | Compiler/linter message, file path, line number |
| **Runtime error** | Error message, context (what action triggered it) |

### What NOT to Do

- Do NOT summarize error messages — include the exact text
- Do NOT truncate stack traces — the bottom frames often contain the root cause
- Do NOT paraphrase test assertions — include expected and actual values
- Do NOT omit file paths from error output — the target AI needs them

---

## Artifact Type Classification

| Type | When to Use | Examples |
|------|-------------|---------|
| `source` | Production code being investigated | `.py`, `.ts`, `.tsx`, `.sh` files |
| `test` | Test files that exercise the affected code | `test_*.py`, `*.test.ts`, `*.spec.ts` |
| `config` | Configuration that may affect behavior | `package.json`, `tsconfig.json`, `.env.example` |
| `error` | Error output, stack traces, log entries | Terminal output, test failure messages |

#!/usr/bin/env python3
"""Validate a DevForgeAI brainstorm (BRN) document against references/output-rules.md.

Usage: python3 validate_brn.py docs/specs/brainstorm/BRN-NNN.md

Prints one line per problem ("line N: message") and exits 1, or prints "OK: <path>" and
exits 0. Uses only the standard library; if PyYAML is installed it also checks YAML syntax.
Every other check, dates against the calendar included, gives the same verdict with or without it.
It cannot check whether the user confirmed a disposition or convergence: that is the skill's job.
"""
import datetime
import os
import re
import sys

FRONTMATTER_KEYS = [
    "id", "type", "title", "status", "version", "created", "updated", "owner", "authors",
    "generated_by", "reviewed_by", "approved_by", "approved_on", "upstream", "supersedes",
    "superseded_by", "blocked_by", "participants", "sources",
]
SECTIONS = [
    "## 1. Context", "## 2. Problems", "## 3. Target users", "## 4. Ideas",
    "## 5. Evaluation method", "## 6. Convergence", "## 7. Assumptions and open questions",
    "## 8. Candidate success signals", "## Change Log",
]
COMMON = {"id", "status", "superseded_by", "upstream"}
COLLECTIONS = {
    "problems": {
        "prefix": "PRB",
        "required": ["statement", "who", "evidence", "severity"],
        "quoted": {"statement", "who", "evidence"},
        "enums": {"severity": {"high", "medium", "low"}},
    },
    "ideas": {
        "prefix": "IDEA",
        "required": ["idea", "addresses", "value", "effort", "risk", "score", "disposition", "reason"],
        "quoted": {"idea"},
        "enums": {"disposition": {"open", "promoted", "parked", "rejected"}},
    },
    "assumptions": {
        "prefix": "ASM",
        "required": ["statement", "validation", "state"],
        "quoted": {"statement", "validation"},
        "enums": {"state": {"open", "validated", "invalidated"}},
    },
}
DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")
NUMBER = re.compile(r"^-?\d+(\.\d+)?$")
PLACEHOLDER = re.compile(r"<[a-z][^<>\n]*>")
INLINE_HTML = re.compile(r"</?(br|b|i|em|strong|code|kbd|sub|sup|s|u|mark|small)\s*/?>")
GENERATED_BY_KEYS = ("tool", "model", "session")


def closing_quote(v):
    """Index of the quote that closes a double-quoted value starting at v[0], or None."""
    end = 1
    while end < len(v):
        if v[end] == "\\":
            end += 2
            continue
        if v[end] == '"':
            return end
        end += 1
    return None


def value_of(raw):
    """Strip a trailing YAML comment from a scalar value, respecting double quotes.

    Text after the closing quote that isn't a comment is kept, so quoted() rejects the value.
    """
    v = raw.strip()
    if v.startswith('"'):
        end = closing_quote(v)
        rest = v[end + 1:].strip() if end is not None else None
        if rest == "" or (rest and rest.startswith("#")):
            return v[: end + 1]
        return v
    return v.split(" #", 1)[0].strip()


def quoted(v):
    return len(v) >= 2 and v.startswith('"') and closing_quote(v) == len(v) - 1


def flow_items(v):
    """The raw items of a one-line flow list such as ["a", "b"], or None if v isn't one."""
    if not (v.startswith("[") and v.endswith("]")):
        return None
    items, cur, in_quote, escaped = [], "", False, False
    for ch in v[1:-1]:
        if in_quote:
            cur += ch
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == '"':
                in_quote = False
        elif ch == '"':
            in_quote = True
            cur += ch
        elif ch == ",":
            items.append(cur.strip())
            cur = ""
        else:
            cur += ch
    if cur.strip() or items:
        items.append(cur.strip())
    return items


def check(path):
    errors = []

    def err(line, msg):
        errors.append((line, msg))

    name = os.path.basename(path)
    stem = name[:-3] if name.endswith(".md") else name
    if not re.fullmatch(r"BRN-\d{3}\.md", name):
        err(0, f"file name {name!r} must be BRN-NNN.md")
    if not os.path.dirname(os.path.abspath(path)).replace("\\", "/").endswith("docs/specs/brainstorm"):
        err(0, "file must be in docs/specs/brainstorm/")

    try:
        with open(path, encoding="utf-8") as f:
            lines = f.read().replace("\r\n", "\n").split("\n")
    except UnicodeDecodeError as e:
        err(0, f"file is not UTF-8 text (byte {e.start}: {e.reason}); save it as UTF-8")
        return errors

    # --- frontmatter -------------------------------------------------------------------
    if not lines or lines[0] != "---":
        err(1, "frontmatter must start with a '---' line")
        return errors
    try:
        fm_end = lines.index("---", 1)
    except ValueError:
        err(1, "frontmatter has no closing '---' line")
        return errors

    keys, fm, nested = [], {}, {}
    current = None
    for i in range(1, fm_end):
        line = lines[i]
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        m = re.match(r"^([A-Za-z_][\w-]*):(.*)$", line)
        if m:
            current = m.group(1)
            keys.append(current)
            fm[current] = (i + 1, value_of(m.group(2)))
            nested[current] = []
        elif current:
            nested[current].append((i + 1, line))

    for k in keys:
        if k not in FRONTMATTER_KEYS:
            err(fm[k][0], f"unknown frontmatter key {k!r}")
    for k in FRONTMATTER_KEYS:
        if k not in fm:
            err(fm_end + 1, f"missing frontmatter key {k!r}")
    known = [k for k in keys if k in FRONTMATTER_KEYS]
    if known != [k for k in FRONTMATTER_KEYS if k in known]:
        err(2, "frontmatter keys are out of order (see output-rules.md)")

    def fv(k):
        return fm.get(k, (0, None))

    ln, v = fv("id")
    if v is not None and v != stem:
        err(ln, f"id {v!r} must equal the file name {stem!r}")
    ln, v = fv("type")
    if v is not None and v != "brainstorm":
        err(ln, "type must be brainstorm")
    ln, v = fv("title")
    if v is not None and (not quoted(v) or v == '""'):
        err(ln, "title must be a non-empty quoted string")
    ln, v = fv("status")
    if v is not None and v not in {"draft", "converged", "archived"}:
        err(ln, "status must be draft, converged or archived")
    ln, v = fv("version")
    if v is not None and not re.fullmatch(r"[1-9]\d*", v):
        err(ln, "version must be a positive integer")
    bad_date = False
    for k in ("created", "updated"):
        ln, v = fv(k)
        if v is not None and not DATE.match(v):
            err(ln, f"{k} must be an unquoted YYYY-MM-DD date")
        elif v is not None:
            try:
                datetime.date.fromisoformat(v)
            except ValueError:
                bad_date = True
                err(ln, f"{k} {v} is not a real calendar date")
    ln, v = fv("owner")
    if v is not None and (not quoted(v) or v == '""'):
        err(ln, 'owner must be a quoted name, or "[NEEDS CLARIFICATION: owner]" when it is unknown')

    def list_of(k):
        """(line, raw item) pairs for a frontmatter list in flow or block form, or None."""
        ln, v = fm[k]
        if v:
            items = flow_items(v)
            return None if items is None else [(ln, x) for x in items]
        rows = [(l, s.strip()) for l, s in nested[k] if s.strip()]
        if rows and all(s.startswith("- ") for _, s in rows):
            return [(l, value_of(s[2:])) for l, s in rows]
        return None

    def strings(items):
        return items is not None and all(quoted(x) and x != '""' for _, x in items)

    authors = list_of("authors") if "authors" in fm else None
    if "authors" in fm and not (authors and strings(authors)):
        err(fm["authors"][0],
            'authors must be a non-empty list of quoted names, e.g. ["Dana", "claude-code"]')
    for k in ("participants", "sources"):
        if k in fm and not strings(list_of(k)):
            err(fm[k][0], f"{k} must be [] or a list of quoted strings")
    if "upstream" in fm:
        links = list_of("upstream")
        if links is None or not all(x.startswith("{") and x.endswith("}") and re.search(r"[{,]\s*id:", x)
                                    and re.search(r"[{,]\s*relation:", x) for _, x in links):
            err(fm["upstream"][0], "upstream must be [] or a list of link records, one per line, such as "
                "- {id: BRN-002, relation: informed_by, version: 1, hash: null}")
    for k, want in (("supersedes", "[]"), ("superseded_by", "null"), ("blocked_by", "[]")):
        ln, v = fv(k)
        if v is not None and v != want:
            err(ln, f"{k} must be {want} in a brainstorm")
    ln, v = fv("reviewed_by")
    if v is not None and v != "[]":
        err(ln, "reviewed_by must be [] (only humans who reviewed it fill this)")
    ln, v = fv("approved_by")
    if v is not None and v != '""':
        err(ln, 'approved_by must be ""')
    ln, v = fv("approved_on")
    if v is not None and v != "null":
        err(ln, "approved_on must be null")
    if "generated_by" in fm:
        sub = {}
        for ln, line in nested["generated_by"]:
            m = re.match(r"^\s+([A-Za-z_][\w-]*):(.*)$", line)
            if m:
                sub[m.group(1)] = (ln, value_of(m.group(2)))
                if m.group(1) not in GENERATED_BY_KEYS:
                    err(ln, f"generated_by.{m.group(1)} is not allowed: generated_by holds only tool, "
                        "model and session")
        for k in GENERATED_BY_KEYS:
            ln, v = sub.get(k, (fm["generated_by"][0], None))
            if v is None or not quoted(v) or v == '""' or "${" in v:
                err(ln, f"generated_by.{k} must be a non-empty quoted string")

    # --- whole-document checks ---------------------------------------------------------
    for i, line in enumerate(lines, 1):
        for m in re.finditer(r"\bhash:\s*([^\s,}]+)", line):
            if m.group(1) != "null":
                err(i, "every hash must be null")
        if "<!--" in line:
            err(i, "leftover HTML comment")
        for token, what in (("BRN-000", "the allocated ID"), ("YYYY-MM-DD", "today's date")):
            if token in line:
                err(i, f"leftover template placeholder {token}: replace it with {what}")
        if i > fm_end:
            for m in PLACEHOLDER.finditer(line):
                if not INLINE_HTML.fullmatch(m.group(0)):
                    err(i, f"leftover template placeholder {m.group(0)!r}: replace it with content or "
                        "[NEEDS CLARIFICATION: question]")

    pos = fm_end
    for heading in SECTIONS:
        try:
            pos = lines.index(heading, pos) + 1
        except ValueError:
            err(0, f"missing or out-of-order section heading {heading!r}")

    # --- Change Log: the newest row names its session ------------------------------------
    try:
        log_start = lines.index("## Change Log", fm_end)
    except ValueError:
        log_start = None
    if log_start is not None:
        rows = []
        for i in range(log_start + 1, len(lines)):
            line = lines[i].strip()
            if line.startswith("## "):
                break
            if line.startswith("|") and not re.match(r"^\|\s*(Version|-+)\s*\|", line):
                rows.append((i + 1, [c.strip() for c in line.strip("|").split("|")]))
        if not rows:
            err(log_start + 1, "Change Log has no rows")
        else:
            ln, cells = rows[-1]
            author = cells[2] if len(cells) > 2 else ""
            if not author:
                err(ln, "last Change Log row has no author: write 'claude-code (session ID)' with the ID "
                    "from generated_by.session, or the name of the person who made the change")
            elif author.startswith("claude-code"):
                m = re.fullmatch(r"claude-code \(session ([A-Za-z0-9-]+)\)", author)
                session = (fm.get("generated_by") and next(
                    (value_of(l.split(":", 1)[1]) for _, l in nested["generated_by"]
                     if l.strip().startswith("session:")), None))
                if not m:
                    err(ln, "last Change Log row: author must be 'claude-code (session <ID>)'")
                elif session and m.group(1) != session.strip('"'):
                    err(ln, "last Change Log row: session must match generated_by.session")

    # --- item blocks -------------------------------------------------------------------
    items = {c: [] for c in COLLECTIONS}  # collection -> list of (line, id, fields)
    i = fm_end
    while i < len(lines):
        if lines[i].rstrip() != "```yaml items":
            i += 1
            continue
        start = i + 1
        try:
            end = next(j for j in range(start, len(lines)) if lines[j].startswith("```"))
        except StopIteration:
            err(start, "unclosed yaml items fence")
            break
        block = lines[start:end]
        collection, item = None, None
        reported = content = unindented = False
        for off, line in enumerate(block):
            ln = start + off + 1
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            if "<!--" in line:
                continue  # already reported
            content = True
            if not line[0].isspace() and not (collection and line.startswith("- ")):
                key = line.split(":", 1)[0]
                if collection is None:
                    if key not in COLLECTIONS or line.rstrip() != key + ":":
                        err(ln, f"top-level key {key!r} is not problems, ideas or assumptions")
                        reported = True
                        break
                    collection = key
                else:
                    err(ln, f"second top-level key {key!r} in one item block: put each collection in its "
                        "own yaml items fence")
                continue
            if not line[0].isspace() and not unindented:
                err(ln, f"list items must be indented under '{collection}:' (write '  - id: ...', as the "
                    "template does)")
                unindented = True
            m = re.match(r"^(\s*)- id:(.*)$", line)
            if m:
                item = {"line": ln, "id": value_of(m.group(2)), "indent": len(m.group(1)) + 2, "fields": {}}
                items.setdefault(collection, []).append(item)
                continue
            m = re.match(r"^(\s+)([A-Za-z_][\w-]*):(.*)$", line)
            if item and m and len(m.group(1)) == item["indent"]:
                item["fields"][m.group(2)] = (ln, value_of(m.group(3)), [])
                item["last"] = m.group(2)
                continue
            if item and item.get("last"):
                item["fields"][item["last"]][2].append((ln, line.strip()))
        if collection is None and not reported:
            err(start, "item block has no top-level key: start it with problems:, ideas: or assumptions:"
                if content else "empty yaml items block")
        i = end + 1

    prb_ids = {it["id"] for it in items["problems"]}
    for collection, spec in COLLECTIONS.items():
        seen = []
        for it in items[collection]:
            ln, iid, fields = it["line"], it["id"], it["fields"]
            if not re.fullmatch(spec["prefix"] + r"-\d{2}", iid):
                err(ln, f"{iid!r} does not match {spec['prefix']}-NN")
            if iid in seen:
                err(ln, f"duplicate id {iid}")
            seen.append(iid)
            allowed = COMMON | set(spec["required"])
            for f, (fln, _, _) in fields.items():
                if f not in allowed:
                    err(fln, f"field {f!r} is not allowed in {collection}")
            st = fields.get("status")
            if not st or st[1] not in {"active", "deprecated"}:
                err(ln, f"{iid}: status must be active or deprecated")
            for f in spec["required"]:
                if f not in fields:
                    err(ln, f"{iid}: missing field {f!r}")
            for f in spec["quoted"]:
                if f in fields and not quoted(fields[f][1]):
                    err(fields[f][0], f"{iid}: {f} must be a quoted string")
            for f, allowed_values in spec["enums"].items():
                if f in fields and fields[f][1] not in allowed_values:
                    err(fields[f][0], f"{iid}: {f} must be one of {sorted(allowed_values)}")
            if collection == "ideas":
                if "addresses" in fields:
                    fln, inline, sub = fields["addresses"]
                    if inline:
                        err(fln, f"{iid}: addresses must be a block list (- PRB-NN), not {inline!r}")
                    refs = [value_of(s[1][2:]) for s in sub if s[1].startswith("- ")]
                    if not refs and not inline:
                        err(fln, f"{iid}: addresses must name at least one problem")
                    for r in refs:
                        if r not in prb_ids:
                            err(fln, f"{iid}: addresses {r!r}, which is not a defined problem")
                for f in ("value", "effort", "risk"):
                    if f in fields:
                        v = fields[f][1]
                        if not (v == "null" or quoted(v) or NUMBER.match(v)):
                            err(fields[f][0], f"{iid}: {f} must be null, a quoted rating or a number")
                if "score" in fields:
                    v = fields["score"][1]
                    if not (v == "null" or NUMBER.match(v)):
                        err(fields["score"][0], f"{iid}: score must be null or a number")
                if "disposition" in fields and "reason" in fields:
                    d, r = fields["disposition"][1], fields["reason"][1]
                    if d == "open" and r != "null":
                        err(fields["reason"][0], f"{iid}: reason must be null while disposition is open")
                    if d != "open" and not quoted(r):
                        err(fields["reason"][0], f"{iid}: a decided disposition needs a quoted reason")
        nums = [int(x.split("-")[1]) for x in seen if re.fullmatch(spec["prefix"] + r"-\d{2}", x)]
        if nums != list(range(1, len(nums) + 1)):
            err(0, f"{collection} IDs must be numbered from 01 in order, got {seen}")
    if not items["problems"]:
        err(0, "no problems recorded")
    if not items["ideas"]:
        err(0, "no ideas recorded")

    # --- optional YAML syntax check ----------------------------------------------------
    try:
        import yaml  # type: ignore
    except ImportError:
        yaml = None
    if yaml is not None:
        # safe_load also raises plain exceptions, such as ValueError for 2026-13-45; report them all,
        # on one line, except a date error the calendar check above already reported.
        try:
            yaml.safe_load("\n".join(lines[1:fm_end]))
        except Exception as e:
            if not (bad_date and not isinstance(e, yaml.YAMLError)):
                err(2, f"frontmatter YAML does not parse: {' '.join(str(e).split())}")
        for j, line in enumerate(lines):
            if line.rstrip() == "```yaml items":
                end = next((k for k in range(j + 1, len(lines)) if lines[k].startswith("```")), len(lines))
                try:
                    yaml.safe_load("\n".join(lines[j + 1:end]))
                except Exception as e:
                    err(j + 2, f"item block YAML does not parse: {' '.join(str(e).split())}")
    return errors


def main(argv):
    if len(argv) != 2:
        print(__doc__.strip().splitlines()[2])
        return 2
    path = argv[1]
    if not os.path.isfile(path):
        print(f"line 0: file not found: {path}")
        return 1
    errors = check(path)
    for line, msg in sorted(set(errors)):
        print(f"line {line}: {msg}")
    if errors:
        print(f"{len(set(errors))} problem(s) in {path}")
        return 1
    print(f"OK: {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

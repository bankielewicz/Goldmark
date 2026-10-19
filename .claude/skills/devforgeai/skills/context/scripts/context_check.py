#!/usr/bin/env python3
"""Snapshot, check and restore a DevForgeAI project's context documents (SPEC-011 §5; BEH-17, BEH-18, ERR-08).

Usage, from the project root:
    python3 context_check.py snapshot new         copy the context documents and ambiguity logs aside
    python3 context_check.py snapshot <folder>    the same, into a folder that must not exist yet
    python3 context_check.py check [--snapshot <folder>]
    python3 context_check.py restore <folder> <file>...

snapshot copies every regular file under docs/specs/context/ and every docs/specs/ambiguities/AMB-*.md,
byte for byte, into the folder, with MANIFEST.sha256 beside them. "new" makes the folder
${TMPDIR:-/tmp}/devforgeai-context-<UTC time>-<8 hex digits>/start. Snapshots are never deleted.
Output: "snapshot: <N> files in <folder>".

check reads the documents with one of the 12 context names, the detail files they link, and the
ambiguity logs, and applies these rules (the label each error line ends with):
- schema: each document against references/schemas/context.schema.json (frontmatter and every
  `yaml items` block) and its file name; each ambiguity log against ambiguities.schema.json; with a
  format checker, so 2026-13-45 is an error. A YAML .nan is an error too.
- statement: a Decision names its source, which has a constrains link (in the parent's frontmatter for a
  detail file); an Observed statement names a path and a date; a Proposed statement holds a marker; a
  DevForgeAI rule names no document ID. Tables' Basis cells follow the same kinds, and a POL-NNN#SET-NN
  Source cell needs its link.
- approval: an approved document, its detail files included, holds no [NEEDS CLARIFICATION or [NEEDS ADR
  marker, no Proposed statement or Basis cell, and no active proposed item.
- items: item IDs are unique; with --snapshot, every item ID of the snapshot's copy is still there.
- size: index.md at most 100 lines, every other file at most 500, with a "## Contents" list past 100.
- detail: a detail file starts with its parent line, has no frontmatter and no items, is linked from its
  parent one level deep, and links no detail file.
- ambiguities: with --snapshot, every field of every entry equals the snapshot's, except resolution, and
  no entry is added or removed.
With --snapshot, a file byte-identical to its snapshot copy whose errors involve no changed file is
printed once as "<file>: unchanged, invalid: <first error> (ERR-10)" and counts in neither the summary
nor the exit code. Output: one line per error, "<file>: <part>: <field>: <message> (<rule>)", then
"OK: <N> files checked" or "INVALID: <N> error(s) in <M> file(s)".

restore copies each named file back from the snapshot after checking the copy against MANIFEST.sha256,
then checks the restored file's SHA-256: "restored <file>", or "NOT RESTORED <file>: <reason>".

Every subcommand first loads PyYAML, jsonschema and the four schema copies (context, ambiguities, common,
policy). When one is missing, or the subcommand can't otherwise run, it prints "Cannot run: <reason>."
and exits 2. Exit codes: snapshot 0 done; check 0 valid, 1 invalid; restore 0 all restored, 1 any not
restored; 2 can't run. It writes only snapshot folders and the files restore restores. It handles
jsonschema 4.10 (no referencing module) and 4.26, as validate_policy.py does.
"""
import hashlib
import json
import re
import secrets
import shutil
import sys
import warnings
from datetime import date, datetime, timezone
from os import environ
from pathlib import Path

SCHEMAS = Path(__file__).resolve().parent.parent / "references" / "schemas"
SCHEMA_NAMES = ("context", "ambiguities", "common", "policy")
CONTEXT = Path("docs/specs/context")
AMBIGUITIES = Path("docs/specs/ambiguities")
MANIFEST = "MANIFEST.sha256"
IDS = {"index": "CTX-001", "architecture": "CTX-002", "tech-stack": "CTX-003", "source-tree": "CTX-004",
       "testing": "CTX-005", "front-end": "CTX-011", "middle-tier": "CTX-012", "back-end": "CTX-013",
       "api": "CTX-014", "rdbms": "CTX-015", "datastore": "CTX-016", "ui-mockups": "CTX-017"}
FRONTMATTER = re.compile(r"\A---[ \t]*\n(.*?)\n---[ \t]*(?:\n|\Z)", re.S)
ITEM_BLOCK = re.compile(r"^```yaml items[ \t]*\n(.*?)^```[ \t]*$", re.S | re.M)
MARKER = re.compile(r"\[NEEDS (?:CLARIFICATION|ADR)")
FULL_MARKER = re.compile(r"\[NEEDS (?:CLARIFICATION|ADR): [^\]\n]+\]")
DOC_ID = re.compile(r"\b(?:BRN|PRD|ARCH|EPIC|SPR|STORY|SPEC|ADR|SKL|POL|TASK|TEST|CTX|AMB)-\d{3}\b")
SOURCE = re.compile(r"\b(ADR|POL|ARCH)-(\d{3})(?:#([A-Z]+-\d{2}))?\b")
LINK = re.compile(r"\[[^\]\n]*\]\(([^)\s]+)\)")
BULLET = re.compile(r"^([ \t]*)[-*+][ \t]+(.*)$")
PARENT_LINE = re.compile(r"\A> Part of (CTX-\d{3}) \(([a-z-]+)\.md\), version \d+\.")
COLLECTIONS = {"technologies": "TEC", "roots": "SRC", "entries": "ENT"}
MAX_MESSAGE = 160


class CannotRun(Exception):
    pass


# --- Libraries and schemas -------------------------------------------------------------------------


def load():
    try:
        import yaml
    except ImportError:
        raise CannotRun("PyYAML is not installed")
    try:
        import jsonschema
    except ImportError:
        raise CannotRun("jsonschema is not installed")
    schemas = {}
    for name in SCHEMA_NAMES:
        try:
            schemas[name] = json.loads((SCHEMAS / f"{name}.schema.json").read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            raise CannotRun(f"the schema copy {name}.schema.json is missing or unreadable ({type(exc).__name__})")
    return yaml, jsonschema, schemas


def make_validator(jsonschema, schemas, name):
    cls, common = jsonschema.Draft202012Validator, schemas["common"]
    try:
        from referencing import Registry, Resource  # jsonschema 4.18 and later
    except ImportError:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", DeprecationWarning)
            resolver = jsonschema.RefResolver.from_schema(schemas[name], store={common["$id"]: common})
            return cls(schemas[name], resolver=resolver, format_checker=jsonschema.FormatChecker())
    registry = Registry().with_resource(common["$id"], Resource.from_contents(common))
    return cls(schemas[name], registry=registry, format_checker=jsonschema.FormatChecker())


def yaml_loader(yaml):
    class Loader(yaml.SafeLoader):
        """Keeps dates as strings, as the schemas expect."""

    Loader.yaml_implicit_resolvers = {k: [r for r in v if r[0] != "tag:yaml.org,2002:timestamp"]
                                      for k, v in yaml.SafeLoader.yaml_implicit_resolvers.items()}
    return Loader


# --- Files ----------------------------------------------------------------------------------------


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def snapshot_files():
    files = []
    if CONTEXT.is_dir():
        files += [p for p in CONTEXT.rglob("*") if p.is_file() and not p.is_symlink()]
    if AMBIGUITIES.is_dir():
        files += [p for p in AMBIGUITIES.glob("AMB-*.md") if p.is_file() and not p.is_symlink()]
    return sorted(files)


def read_manifest(folder):
    try:
        lines = (Path(folder) / MANIFEST).read_text(encoding="utf-8").splitlines()
    except OSError:
        raise CannotRun(f"{folder} is not a snapshot (no readable {MANIFEST})")
    manifest = {}
    for line in lines:
        digest, _, rel = line.partition("  ")
        if rel:
            manifest[rel] = digest
    return manifest


# --- Parsing --------------------------------------------------------------------------------------


def parse(yaml, loader, text):
    """The document as the schema sees it: {"frontmatter": {...}, "<collection>": [...]}."""
    m = FRONTMATTER.match(text)
    if not m:
        raise ValueError("no YAML frontmatter between --- lines at the top of the file")
    doc = {"frontmatter": yaml.load(m.group(1), Loader=loader)}
    for block in ITEM_BLOCK.findall(text):
        data = yaml.load(block, Loader=loader)
        if not isinstance(data, dict):
            raise ValueError("an item block must hold one collection key")
        for key, value in data.items():
            if isinstance(doc.get(key), list) and isinstance(value, list):
                doc[key].extend(value)
            else:
                doc[key] = value
    return doc


def body_start(text):
    m = FRONTMATTER.match(text)
    return text[: m.end()].count("\n") if m else 0


def masked(text, start=0, code=True):
    """The file's lines with HTML comments (and, when code is true, fenced blocks) blanked out, and the
    first `start` lines (the frontmatter) blanked too. Line numbers are kept."""
    out, in_code, in_comment = [], False, False
    for i, line in enumerate(text.split("\n")):
        if i < start:
            out.append("")
            continue
        if code and not in_comment and (in_code or line.lstrip().startswith("```")):
            if line.lstrip().startswith("```"):
                in_code = not in_code
            out.append("")
            continue
        kept, rest = "", line
        while rest:
            if in_comment:
                j = rest.find("-->")
                if j < 0:
                    rest = ""
                else:
                    rest, in_comment = rest[j + 3:], False
            else:
                j = rest.find("<!--")
                if j < 0:
                    kept, rest = kept + rest, ""
                else:
                    kept, rest, in_comment = kept + rest[:j], rest[j + 4:], True
        out.append(kept)
    return out


def statements(lines):
    """Yields (line number, label, first line, whole text) for each labelled statement bullet."""
    i = 0
    while i < len(lines):
        m = BULLET.match(lines[i])
        label = re.match(r"\*\*(Decision|Convention|Observed|Proposed|DevForgeAI rule)\b", m.group(2)) if m else None
        if not label:
            i += 1
            continue
        indent, first, text, j = len(m.group(1).expandtabs()), m.group(2), [m.group(2)], i + 1
        while j < len(lines):
            line = lines[j]
            b = BULLET.match(line)
            if not line.strip() or line.lstrip().startswith(("#", "|")) or (b and len(b.group(1).expandtabs()) <= indent):
                break
            text.append(line.strip())
            j += 1
        yield i + 1, label.group(1), first, " ".join(text)
        i = j


def tables(lines):
    """Yields (header cells, [(line number, cells)]) for each Markdown table."""
    i = 0
    while i + 1 < len(lines):
        if lines[i].lstrip().startswith("|") and re.match(r"^\s*\|[\s:|-]+\|\s*$", lines[i + 1]) and "-" in lines[i + 1]:
            header = cells(lines[i])
            rows, j = [], i + 2
            while j < len(lines) and lines[j].lstrip().startswith("|"):
                rows.append((j + 1, cells(lines[j])))
                j += 1
            yield header, rows
            i = j
        else:
            i += 1


def cells(line):
    return [c.strip() for c in line.strip().strip("|").split("|")]


def valid_date(text):
    try:
        date.fromisoformat(text)
        return True
    except ValueError:
        return False


# --- Checking -------------------------------------------------------------------------------------


class Checker:
    def __init__(self, yaml, jsonschema, schemas, snapshot=None):
        self.yaml, self.loader = yaml, yaml_loader(yaml)
        self.context_v = make_validator(jsonschema, schemas, "context")
        self.amb_v = make_validator(jsonschema, schemas, "ambiguities")
        self.snapshot = Path(snapshot) if snapshot else None
        self.manifest = read_manifest(snapshot) if snapshot else {}
        self.errors = []  # (file, part, field, message, rule, involved files)
        self.checked = 0

    def add(self, file, part, field, message, rule, involved=()):
        message = message if len(message) <= MAX_MESSAGE else message[: MAX_MESSAGE - 3] + "..."
        self.errors.append((file, part, field, message, rule, {file, *involved}))

    def load_doc(self, path, text):
        try:
            return parse(self.yaml, self.loader, text)
        except (self.yaml.YAMLError, ValueError) as exc:
            self.add(path, "document", "YAML", " ".join(str(exc).split()), "schema")
            return None

    def old_text(self, path):
        if self.snapshot is None or path not in self.manifest:
            return None
        try:
            return (self.snapshot / path).read_text(encoding="utf-8")
        except OSError:
            return None

    def old_doc(self, path):
        text = self.old_text(path)
        if text is None:
            return None
        try:
            return parse(self.yaml, self.loader, text)
        except (self.yaml.YAMLError, ValueError):
            return None

    # schema --------------------------------------------------------------------------------------

    def schema(self, path, doc, validator):
        found = sorted(validator.iter_errors(doc), key=lambda e: [str(p) for p in e.absolute_path])
        for e in found:
            where = list(e.absolute_path)
            part, rest = locate(doc, where)
            message = e.message
            if e.validator == "required":
                m = re.match(r"^'(.+)' is a required property$", e.message)
                if m:
                    rest, message = rest + [m.group(1)], "missing required field"
            elif e.validator == "additionalProperties" and isinstance(e.instance, dict):
                extra = sorted(set(e.instance) - set(e.schema.get("properties", {})))
                message = "unexpected field " + ", ".join(repr(x) for x in extra)
            elif e.validator == "not" and isinstance(e.validator_value, dict) and "required" in e.validator_value:
                message = "field not allowed here: " + ", ".join(e.validator_value["required"])
            elif e.validator == "oneOf" and where[-1:] == ["approved_on"]:
                formats = [c for c in e.context if c.validator == "format"]
                if formats:
                    message = formats[0].message
            self.add(path, part, field_path(rest) or "(whole)", message, "schema")
        reported = {tuple(e.absolute_path) for e in found}
        for where in nan_paths(doc):
            if where not in reported:
                part, rest = locate(doc, list(where))
                self.add(path, part, field_path(rest) or "(whole)", "nan is not a number", "schema")

    # statement and approval -----------------------------------------------------------------------

    def links(self, doc):
        fm = doc.get("frontmatter") if isinstance(doc, dict) else None
        ups = fm.get("upstream") if isinstance(fm, dict) else None
        return [u for u in ups if isinstance(u, dict)] if isinstance(ups, list) else []

    def has_link(self, links, kind, num, item):
        target = f"{kind}-{num}"
        return any(u.get("id") == target and u.get("relation") == "constrains" and (item is None or u.get("item") == item)
                   for u in links)

    def sources_missing(self, text, links):
        found = SOURCE.findall(text)
        return found, [f"{k}-{n}" + (f"#{i}" if i else "") for k, n, i in found if not self.has_link(links, k, n, i or None)]

    def statement_rules(self, path, text, links, *, parent=None, approved=False, approval_file=None, prefix=""):
        """Statement rules on a document or detail file; approval findings go to approval_file."""
        involved = {parent} if parent else set()
        start = 0 if parent else body_start(text)
        lines = masked(text, start)
        where = f"the frontmatter of {parent}" if parent else "the frontmatter"
        for n, label, first, whole in statements(lines):
            if label == "Decision":
                m = re.match(r"\*\*Decision(?:\*\*[ \t]*\(([^)]*)\)[ \t]*:|[ \t]*\(([^)]*)\)[ \t]*:\*\*)", first)
                found, missing = self.sources_missing((m.group(1) or m.group(2)) if m else "", links)
                if not m or not found:
                    self.add(path, f"line {n}", "Decision", "names no source: write **Decision** (ADR-NNN, "
                             "POL-NNN#SET-NN or ARCH-NNN#CMP-NN):", "statement", involved)
                for s in missing:
                    self.add(path, f"line {n}", "Decision", f"{s} has no constrains link in {where}", "statement",
                             involved)
            elif label == "Observed":
                m = re.match(r"\*\*Observed(?:\*\*[ \t]*\(([^)]*)\)[ \t]*:|[ \t]*\(([^)]*)\)[ \t]*:\*\*)", first)
                inner = re.fullmatch(r"(.+?),[ \t]*(\d{4}-\d{2}-\d{2})", ((m.group(1) or m.group(2)) if m else "").strip())
                if not inner or not valid_date(inner.group(2)):
                    self.add(path, f"line {n}", "Observed", "names no path and date: write **Observed** (<path>, "
                             "YYYY-MM-DD):", "statement", involved)
            elif label == "Proposed":
                if not FULL_MARKER.search(whole):
                    self.add(path, f"line {n}", "Proposed", "holds no [NEEDS CLARIFICATION: …] marker", "statement",
                             involved)
                if approved:
                    self.add(approval_file, f"{prefix}line {n}", "status", "approved, but this line holds a Proposed "
                             "statement", "approval", {path} | involved)
            elif label == "DevForgeAI rule":
                ids = sorted(set(DOC_ID.findall(whole)))
                if ids:
                    self.add(path, f"line {n}", "DevForgeAI rule", "names a document ID: " + ", ".join(ids),
                             "statement", involved)
        for header, rows in tables(lines):
            lower = [h.lower() for h in header]
            for col, kind in ((lower.index("basis") if "basis" in lower else None, "Basis"),
                              (lower.index("source") if "source" in lower else None, "Source")):
                if col is None:
                    continue
                for n, row in rows:
                    cell = row[col] if col < len(row) else ""
                    self.cell_rules(path, n, kind, cell, links, involved, where)
                    if kind == "Basis" and approved and cell.lower().startswith("proposed"):
                        self.add(approval_file, f"{prefix}line {n}", "status", "approved, but this row's basis is "
                                 "Proposed", "approval", {path} | involved)
        if approved:
            for n, line in enumerate(masked(text, 0, code=False), start=1):
                m = MARKER.search(line)
                if m:
                    self.add(approval_file, f"{prefix}line {n}", "status", f"approved, but this line holds a "
                             f"{m.group(0)} marker", "approval", {path} | involved)

    def cell_rules(self, path, n, kind, cell, links, involved, where):
        if kind == "Source":
            for k, num, item in SOURCE.findall(cell):
                if k == "POL" and not self.has_link(links, k, num, item or None):
                    self.add(path, f"line {n}", "Source", f"POL-{num}#{item} has no constrains link in {where}",
                             "statement", involved)
            return
        low = cell.lower()
        if low.startswith("convention"):
            return
        if low.startswith("observed"):
            m = re.match(r"observed[ \t]*\((.+?),[ \t]*(\d{4}-\d{2}-\d{2})\)", cell, re.I)
            if not m or not valid_date(m.group(2)):
                self.add(path, f"line {n}", "Basis", "Observed names no path and date: write Observed (<path>, "
                         "YYYY-MM-DD)", "statement", involved)
            return
        if low.startswith("proposed"):
            if not FULL_MARKER.search(cell):
                self.add(path, f"line {n}", "Basis", "Proposed holds no [NEEDS CLARIFICATION: …] marker", "statement",
                         involved)
            return
        found, missing = self.sources_missing(cell, links)
        if not found:
            self.add(path, f"line {n}", "Basis", f"'{cell}' isn't a basis: Convention, Observed (<path>, <date>), "
                     "Proposed with its marker, or a Decision's source", "statement", involved)
        for s in missing:
            self.add(path, f"line {n}", "Basis", f"{s} has no constrains link in {where}", "statement", involved)

    # items ---------------------------------------------------------------------------------------

    def item_rules(self, path, doc, approved):
        for key, prefix in COLLECTIONS.items():
            items = doc.get(key)
            if not isinstance(items, list):
                continue
            counts = {}
            for it in items:
                if isinstance(it, dict) and isinstance(it.get("id"), str):
                    counts[it["id"]] = counts.get(it["id"], 0) + 1
            reported = set()
            for i, it in enumerate(items):
                if not isinstance(it, dict):
                    continue
                if counts.get(it.get("id"), 0) > 1 and it["id"] not in reported:
                    reported.add(it["id"])
                    self.add(path, label(doc, key, i), "id", f"used by {counts[it['id']]} items", "items")
                if approved and it.get("status") == "active" and it.get("basis") == "proposed":
                    self.add(path, label(doc, key, i), "status", "approved, but this item is an active proposal",
                             "approval")
            old = self.old_doc(path)
            if isinstance(old, dict) and isinstance(old.get(key), list):
                for it in old[key]:
                    if isinstance(it, dict) and isinstance(it.get("id"), str) and it["id"] not in counts:
                        self.add(path, it["id"], "id", "is in the snapshot but no longer in the document: deprecate "
                                 "an item, never delete or renumber it", "items")

    # size ----------------------------------------------------------------------------------------

    def size_rules(self, path, text, limit, owner=None):
        n = text.count("\n") + (1 if text and not text.endswith("\n") else 0)
        if n > limit:
            self.add(path, "document", "lines", f"{n} lines; this file may have at most {limit}", "size")
        elif n > 100 and not re.search(r"^##[ \t]+Contents\b", text, re.M):
            self.add(path, "document", "lines", f"{n} lines, past 100, with no ## Contents list", "size")

    # detail --------------------------------------------------------------------------------------

    def detail_links(self, path, name, text):
        """The detail files a document links, after reporting bad links."""
        found = []
        for n, line in enumerate(masked(text, body_start(text)), start=1):
            for target in LINK.findall(line):
                rel = relative_target(Path(path).parent, target)
                if rel is None or len(rel.parts) < 2:
                    continue
                if len(rel.parts) > 2:
                    self.add(path, f"line {n}", "link", f"links {target}: detail files are one level deep", "detail")
                elif rel.parts[0] != name:
                    continue
                elif not (CONTEXT / rel).is_file():
                    self.add(path, f"line {n}", "link", f"links {target}, which doesn't exist", "detail")
                elif (CONTEXT / rel).as_posix() not in found:
                    found.append((CONTEXT / rel).as_posix())
        return found

    def detail_rules(self, path, text, parent, name):
        m = PARENT_LINE.match(text)
        if not m or m.group(1) != IDS[name] or m.group(2) != name:
            self.add(path, "document", "parent line", f"doesn't start with '> Part of {IDS[name]} ({name}.md), "
                     "version N.'", "detail", {parent})
        if text.startswith("---"):
            self.add(path, "document", "frontmatter", "a detail file has no frontmatter", "detail")
        if re.search(r"^```yaml items", text, re.M):
            self.add(path, "document", "items", "a detail file holds no items", "detail")
        for n, line in enumerate(masked(text), start=1):
            for target in LINK.findall(line):
                rel = relative_target(Path(path).parent, target)
                if rel is not None and len(rel.parts) >= 2:
                    self.add(path, f"line {n}", "link", f"links {target}: a detail file never links a detail file",
                             "detail")

    # ambiguities ---------------------------------------------------------------------------------

    def ambiguity_rules(self, path, doc):
        if self.snapshot is None:
            return
        old = self.old_doc(path)
        if path in self.manifest and not isinstance(old, dict):
            return
        old_entries = entries(old) if isinstance(old, dict) else {}
        new_entries = entries(doc) if isinstance(doc, dict) else {}
        for eid in sorted(set(old_entries) - set(new_entries)):
            self.add(path, eid, "id", "removed since the snapshot", "ambiguities")
        for eid in sorted(set(new_entries) - set(old_entries)):
            self.add(path, eid, "id", "added since the snapshot: the context step never writes an entry", "ambiguities")
        for eid in sorted(set(old_entries) & set(new_entries)):
            a, b = old_entries[eid], new_entries[eid]
            for key in sorted(set(a) | set(b)):
                if key != "resolution" and a.get(key) != b.get(key):
                    self.add(path, eid, key, "changed since the snapshot; only resolution may change", "ambiguities")

    # run -----------------------------------------------------------------------------------------

    def run(self):
        for name in IDS:
            p = CONTEXT / f"{name}.md"
            if not p.is_file():
                continue
            path = p.as_posix()
            text = p.read_text(encoding="utf-8")
            self.checked += 1
            doc = self.load_doc(path, text)
            fm = doc.get("frontmatter") if isinstance(doc, dict) else None
            approved = isinstance(fm, dict) and fm.get("status") == "approved"
            if doc is not None:
                self.schema(path, doc, self.context_v)
                if isinstance(fm, dict) and fm.get("document") != name:
                    self.add(path, "frontmatter", "document", f"'{fm.get('document')}' doesn't match the file name "
                             f"{name}.md", "schema")
                self.item_rules(path, doc, approved)
            links = self.links(doc)
            self.statement_rules(path, text, links, approved=approved, approval_file=path)
            self.size_rules(path, text, 100 if name == "index" else 500)
            for dpath in self.detail_links(path, name, text):
                dtext = Path(dpath).read_text(encoding="utf-8")
                self.checked += 1
                self.detail_rules(dpath, dtext, path, name)
                self.statement_rules(dpath, dtext, links, parent=path, approved=approved, approval_file=path,
                                     prefix=f"{Path(dpath).relative_to(CONTEXT).as_posix()} ")
                self.size_rules(dpath, dtext, 500)
        amb_paths = sorted({p.as_posix() for p in AMBIGUITIES.glob("AMB-*.md") if p.is_file()} if AMBIGUITIES.is_dir()
                           else set())
        gone = sorted(p for p in self.manifest if p.startswith(f"{AMBIGUITIES.as_posix()}/") and p not in amb_paths)
        for path in amb_paths:
            self.checked += 1
            doc = self.load_doc(path, Path(path).read_text(encoding="utf-8"))
            if doc is not None:
                self.schema(path, doc, self.amb_v)
                self.ambiguity_rules(path, doc)
        for path in gone:
            self.ambiguity_rules(path, {})

    def unchanged(self, path):
        if path not in self.manifest:
            return False
        try:
            return sha256(Path(path).read_bytes()) == self.manifest[path]
        except OSError:
            return False

    def report(self):
        counted, quiet = [], {}
        for file, part, field, message, rule, involved in self.errors:
            if self.snapshot is not None and all(self.unchanged(f) for f in involved):
                quiet.setdefault(file, f"{part}: {field}: {message} ({rule})")
            else:
                counted.append((file, part, field, message, rule))
        for file, part, field, message, rule in counted:
            print(f"{file}: {part}: {field}: {message} ({rule})")
        for file, first in quiet.items():
            print(f"{file}: unchanged, invalid: {first} (ERR-10)")
        if counted:
            print(f"INVALID: {len(counted)} error(s) in {len({c[0] for c in counted})} file(s)")
            return 1
        print(f"OK: {self.checked} files checked")
        return 0


def locate(doc, path):
    if path[:1] == ["frontmatter"]:
        return "frontmatter", path[1:]
    if path[:1] and path[0] in COLLECTIONS and len(path) > 1 and isinstance(path[1], int):
        return label(doc, path[0], path[1]), path[2:]
    return "document", path


def label(doc, key, index):
    items = doc.get(key)
    it = items[index] if isinstance(items, list) and index < len(items) else None
    if not isinstance(it, dict) or not isinstance(it.get("id"), str):
        return f"{key}[{index}]"
    extra = it.get("name") if key == "technologies" else it.get("path") if key == "roots" else None
    return f"{it['id']} ({extra})" if isinstance(extra, str) and extra else it["id"]


def field_path(parts):
    out = ""
    for p in parts:
        out += f"[{p}]" if isinstance(p, int) else (f".{p}" if out else str(p))
    return out


def nan_paths(value, path=()):
    if isinstance(value, float) and value != value:
        yield path
    elif isinstance(value, dict):
        for k, v in value.items():
            yield from nan_paths(v, path + (k,))
    elif isinstance(value, list):
        for i, v in enumerate(value):
            yield from nan_paths(v, path + (i,))


def entries(doc):
    items = doc.get("entries") if isinstance(doc, dict) else None
    return {e["id"]: e for e in items if isinstance(e, dict) and isinstance(e.get("id"), str)} if isinstance(items, list) else {}


def relative_target(base, target):
    """A link's target as a path relative to docs/specs/context/, or None when it points elsewhere."""
    if re.match(r"^[a-z][a-z0-9+.-]*:", target, re.I) or target.startswith(("/", "#")):
        return None
    target = target.split("#", 1)[0]
    if not target.endswith(".md"):
        return None
    parts = []
    for p in (Path(base) / target).parts:
        if p == "..":
            if not parts:
                return None
            parts.pop()
        elif p != ".":
            parts.append(p)
    resolved = Path(*parts) if parts else Path(".")
    try:
        return resolved.relative_to(CONTEXT)
    except ValueError:
        return None


# --- Subcommands ----------------------------------------------------------------------------------


def snapshot(arg):
    if arg == "new":
        stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S")
        folder = Path(environ.get("TMPDIR") or "/tmp") / f"devforgeai-context-{stamp}-{secrets.token_hex(4)}" / "start"
    else:
        folder = Path(arg)
    if folder.exists():
        raise CannotRun(f"{folder} already exists")
    try:
        folder.mkdir(parents=True)
    except OSError as exc:
        raise CannotRun(f"can't create {folder}: {exc.strerror or exc}")
    lines = []
    for p in snapshot_files():
        rel = p.as_posix()
        try:
            data = p.read_bytes()
            (folder / rel).parent.mkdir(parents=True, exist_ok=True)
            (folder / rel).write_bytes(data)
            if sha256((folder / rel).read_bytes()) != sha256(data):
                raise OSError("the copy differs from the file")
        except OSError as exc:
            raise CannotRun(f"copying {rel} into {folder} failed: {getattr(exc, 'strerror', None) or exc}")
        lines.append(f"{sha256(data)}  {rel}")
    try:
        (folder / MANIFEST).write_text("".join(f"{l}\n" for l in lines), encoding="utf-8")
    except OSError as exc:
        raise CannotRun(f"writing {folder / MANIFEST} failed: {exc.strerror or exc}")
    print(f"snapshot: {len(lines)} files in {folder}")
    return 0


def restore(folder, files):
    manifest, failed = read_manifest(folder), 0
    for arg in files:
        rel = Path(arg)
        key = rel.as_posix()[2:] if rel.as_posix().startswith("./") else rel.as_posix()
        reason = None
        if rel.is_absolute() or ".." in rel.parts:
            reason = "outside the snapshot"
        elif key not in manifest:
            reason = "not in the snapshot"
        else:
            source = Path(folder) / key
            try:
                if sha256(source.read_bytes()) != manifest[key]:
                    reason = f"the snapshot copy doesn't match {MANIFEST}"
                else:
                    Path(key).parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(source, key)
                    if sha256(Path(key).read_bytes()) != manifest[key]:
                        reason = f"its SHA-256 after copying doesn't match {MANIFEST}"
            except OSError as exc:
                reason = exc.strerror or str(exc)
        if reason:
            failed += 1
            print(f"NOT RESTORED {arg}: {reason}")
        else:
            print(f"restored {arg}")
    return 1 if failed else 0


USAGE = ("usage: context_check.py snapshot new|<folder> | check [--snapshot <folder>] | "
         "restore <folder> <file> [<file> ...]")


def main(argv):
    args = argv[1:]
    try:
        if not args or args[0] not in ("snapshot", "check", "restore"):
            raise CannotRun(USAGE)
        cmd = args[0]
        if (cmd == "snapshot" and len(args) != 2) or (cmd == "restore" and len(args) < 3) or \
                (cmd == "check" and args[1:] and (len(args) != 3 or args[1] != "--snapshot")):
            raise CannotRun(USAGE)
        yaml, jsonschema, schemas = load()
        if cmd == "snapshot":
            return snapshot(args[1])
        if cmd == "restore":
            return restore(args[1], args[2:])
        checker = Checker(yaml, jsonschema, schemas, snapshot=args[2] if len(args) == 3 else None)
        checker.run()
        return checker.report()
    except CannotRun as exc:
        print(f"Cannot run: {exc}.")
        return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))

#!/usr/bin/env python3
"""Search a project's docs/specs/ and print citations (SPEC-014 IF-01, BEH-07). Standard library only.

    python3 find_spec.py [--root DIR] QUERY...

The words of QUERY form one query, classified in this order:
- a qualified item ID, `DOC ITEM` or `DOC#ITEM`, where DOC is a scanned file's frontmatter id: the item's
  definition in DOC, then every line naming it with its document;
- a document ID, equal to a scanned file's frontmatter id: every line naming it;
- a bare item ID defined in some ```yaml items block: each definition, then every other line naming it;
- else a term: every line holding all its words, case-insensitively, as literal text.

Each hit prints as `<path>:<line>  <DOC> v<version> <status>  [<ITEM> <item status>]  "<excerpt>"`, in path then
line order (an item's definitions first), at most 50, then a coverage line. Exit 0 with a hit, 1 with none or no
docs/specs/ folder, 2 when it can't run. It reads files and never writes one.
"""
import argparse
import re
import sys
from pathlib import Path

LIMIT = 50
EXCERPT = 100
FRONTMATTER = re.compile(r'^(id|title|status|version|superseded_by):\s*"?([^"#]*?)"?\s*(#.*)?$')
ITEM_ID = re.compile(r'^\s*- id:\s*"?([A-Z]+-\d{2,3})"?\s*$')
ITEM_STATUS = re.compile(r'^\s*status:\s*"?([^"#\s]+)"?')
ITEM_TEXT = re.compile(r"^\s*(rule|obligation|statement|handling):\s*(.*?)\s*$")
STATUS_LINE = re.compile(r"\*\*Status:\*\*\s*(.*)")
BARE_ITEM = re.compile(r"[A-Z]+-\d{2,3}")
QUALIFIED = re.compile(r"(\S+?)(?:\s+|#)([A-Z]+-\d{2,3})")
BEFORE, AFTER = r"(?<![A-Za-z0-9_-])", r"(?![A-Za-z0-9_])"


class Item:
    def __init__(self, item_id, line):
        self.id, self.line, self.status, self.text = item_id, line, "unknown", None


class Doc:
    """One Markdown file: its metadata, its lines, and the items its ```yaml items blocks define."""

    def __init__(self, path, lines):
        self.path, self.lines = path, lines
        meta = self._frontmatter()
        self.id = meta.get("id") or path
        self.version = meta.get("version") or "?"
        self.status = meta.get("status") or self._status_line()
        superseded_by = meta.get("superseded_by")
        if superseded_by and superseded_by != "null":
            self.status += f", superseded by {superseded_by}"
        self.has_id = bool(meta.get("id"))
        self.items, self.enclosing = [], {}
        self._items()

    def _frontmatter(self):
        if not self.lines or self.lines[0].strip() != "---":
            return {}
        meta = {}
        for line in self.lines[1:]:
            if line.strip() == "---":
                return meta
            match = FRONTMATTER.match(line)
            if match and match.group(1) not in meta:
                meta[match.group(1)] = match.group(2).strip()
        return {}  # no closing line: frontmatter the line reader can't use

    def _status_line(self):
        for line in self.lines[:10]:
            match = STATUS_LINE.search(line)
            if match:
                return match.group(1).split(".")[0].strip() or "unknown"
        return "unknown"

    def _items(self):
        in_block, item = False, None
        for number, line in enumerate(self.lines, 1):
            stripped = line.strip()
            if not in_block:
                in_block = stripped == "```yaml items"
                continue
            if stripped == "```":
                in_block, item = False, None
                continue
            match = ITEM_ID.match(line)
            if match:
                item = Item(match.group(1), number)
                self.items.append(item)
            elif item is not None:
                status, text = ITEM_STATUS.match(line), ITEM_TEXT.match(line)
                if status and item.status == "unknown":
                    item.status = status.group(1)
                elif text and item.text is None:
                    value = text.group(2)
                    item.text = value[1:-1] if len(value) > 1 and value[0] == value[-1] == '"' else value
            if item is not None:
                self.enclosing[number] = item


def excerpt(text):
    text = text.strip()
    return text if len(text) <= EXCERPT else text[:EXCERPT - 1] + "…"


def hit_line(doc, number, item, text):
    label = f"  {item.id} {item.status}" if item else ""
    return f'{doc.path}:{number}  {doc.id} v{doc.version} {doc.status}{label}  "{excerpt(text)}"'


def line_hits(docs, pattern, skip=()):
    hits = []
    for doc in docs:
        for number, line in enumerate(doc.lines, 1):
            if (doc.path, number) not in skip and pattern(line):
                hits.append(hit_line(doc, number, doc.enclosing.get(number), line))
    return hits


def definition_hits(docs, item_id):
    hits, seen = [], set()
    for doc in docs:
        for item in doc.items:
            if item.id == item_id:
                hits.append(hit_line(doc, item.line, item, item.text or doc.lines[item.line - 1]))
                seen.add((doc.path, item.line))
    return hits, seen


def names(token):
    regex = re.compile(BEFORE + token + AFTER)
    return lambda line: regex.search(line) is not None


def search(docs, query):
    ids = {doc.id for doc in docs if doc.has_id}
    qualified = QUALIFIED.fullmatch(query)
    if qualified and qualified.group(1) in ids:
        doc_id, item_id = qualified.groups()
        hits, seen = definition_hits([d for d in docs if d.id == doc_id], item_id)
        both = names(re.escape(doc_id) + r"(?:\s+|#)" + re.escape(item_id))
        return hits + line_hits(docs, both, seen)
    if query in ids:
        return line_hits(docs, names(re.escape(query)))
    if BARE_ITEM.fullmatch(query) and any(item.id == query for doc in docs for item in doc.items):
        hits, seen = definition_hits(docs, query)
        return hits + line_hits(docs, names(re.escape(query)), seen)
    words = query.lower().split()
    return line_hits(docs, lambda line: all(word in line.lower() for word in words))


def main(argv=None):
    parser = argparse.ArgumentParser(description="Search docs/specs/ and print path:line citations.")
    parser.add_argument("--root", default=".", help="the project folder that holds docs/specs/ (default: .)")
    parser.add_argument("query", nargs="+", help="a document ID, an item ID (qualified or bare) or a term")
    args = parser.parse_args(argv)
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass
    query = " ".join(args.query).strip()
    if not query:
        print("find_spec.py: the query is empty", file=sys.stderr)
        return 2
    root = Path(args.root)
    if not root.is_dir():
        print(f"find_spec.py: --root {args.root} is not a folder", file=sys.stderr)
        return 2
    specs = root / "docs" / "specs"
    if not specs.is_dir():
        print(f"no docs/specs/ folder under {args.root}: nothing is specified yet")
        return 1

    docs, skipped = [], []
    for path in sorted((p for p in specs.rglob("*.md") if p.is_file()), key=lambda p: p.relative_to(specs).as_posix()):
        rel = "docs/specs/" + path.relative_to(specs).as_posix()
        try:
            docs.append(Doc(rel, path.read_bytes().decode("utf-8").splitlines()))
        except (OSError, UnicodeDecodeError):
            skipped.append(rel)
    folders = sorted({p.relative_to(specs).parts[0] for p in specs.rglob("*.md") if len(p.relative_to(specs).parts) > 1})
    where = f"{len(docs)} files under docs/specs/" + (f" ({', '.join(folders)})" if folders else "")
    if skipped:
        where += f"; skipped {len(skipped)} unreadable: {', '.join(skipped)}"

    hits = search(docs, query)
    if not hits:
        print(f'no match: "{query}" is in none of {where}')
        return 1
    for line in hits[:LIMIT]:
        print(line)
    if len(hits) > LIMIT:
        print(f"{len(hits) - LIMIT} more hits: narrow the query")
    print(f"{len(hits)} hits; searched {where}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

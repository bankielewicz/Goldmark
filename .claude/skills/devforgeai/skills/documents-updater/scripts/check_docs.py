#!/usr/bin/env python3
"""Check Markdown documents against the documents-updater output rules.

Usage: python3 check_docs.py FILE.md [FILE.md ...]

Checks each file for one H1, skipped heading levels, unclosed code fences, empty sections,
leftover template placeholders and guide comments, and relative links, images and anchors
that don't resolve. Files named CHANGELOG*.md also get changelog checks. Prints
"path:line: error|warning: message" per finding and exits 1 if any error was found.
Standard library only.
"""

import re
import sys
import unicodedata
from pathlib import Path
from urllib.parse import unquote

KEEP_A_CHANGELOG = ["Added", "Changed", "Deprecated", "Removed", "Fixed", "Security"]

FENCE = re.compile(r"^ {0,3}(`{3,}|~{3,})(.*)$")
ATX = re.compile(r"^ {0,3}(#{1,6})(?:[ \t]+(.*?))?[ \t]*#*[ \t]*$")
SETEXT = re.compile(r"^ {0,3}(=+|-+)[ \t]*$")
HTML_HEADING = re.compile(r"<h([1-6])\b[^>]*>(.*?)</h\1>", re.I | re.S)
HTML_ID = re.compile(r"<[a-z][^>]*\b(?:id|name)\s*=\s*[\"']([^\"']+)[\"']", re.I)
INLINE_CODE = re.compile(r"(`+)(?:(?!\1).)+?\1")
PLACEHOLDER = re.compile(r"(?<!\$)\{\{[^{}\n]*\}\}")
# Inside code, only template-style placeholders ({{words with spaces}}) count, so Jinja,
# Helm, Go and Actions templates such as {{ name }} or {{.Name}} are left alone.
CODE_PLACEHOLDER = re.compile(r"(?<!\$)\{\{[A-Za-z][^{}\n]*\s[^{}\n]*\}\}")
GUIDE = re.compile(r"<!--\s*guide:", re.I)
INLINE_LINK = re.compile(r"(!?)\[((?:[^\[\]]|\[[^\]]*\])*)\]\(\s*(<[^>]*>|[^()\s]*(?:\([^()\s]*\)[^()\s]*)*)(?:\s+[\"'(][^)]*)?\s*\)")
# A footnote definition ([^1]: text) is not a link reference definition.
REF_DEF = re.compile(r"^ {0,3}\[(?!\^)([^\]]+)\]:\s*(<[^>]*>|\S+)")
BULLET = re.compile(r"^\s*[-*+]\s+(.*)$")
# A leading --- opens front matter only when a key follows; otherwise it is a horizontal rule.
FRONT_MATTER_KEY = re.compile(r"[A-Za-z_][\w.-]*\s*:(?:\s|$)")
UNRELEASED = re.compile(r"\[?unreleased\]?", re.I)
CHANGELOG_NAME = re.compile(r"changelog.*\.md$", re.I)
EXTERNAL = re.compile(r"^(?:[a-z][a-z0-9+.-]*:|//)", re.I)


class Doc:
    """A parsed Markdown file: its lines, which lines are code, and its headings."""

    def __init__(self, path):
        self.path = Path(path)
        self.lines = self.path.read_text(encoding="utf-8").split("\n")
        self.front_matter_title = False
        self.code = [False] * len(self.lines)
        self.fence_errors = []
        # (line_no, level, text, first index, index after the heading), indexes 0-based
        self.headings = []
        self._parse()

    def _parse(self):
        start = 0
        if len(self.lines) > 1 and self.lines[0].strip() == "---" and FRONT_MATTER_KEY.match(self.lines[1]):
            for i in range(1, len(self.lines)):
                if self.lines[i].strip() in ("---", "..."):
                    block = self.lines[1:i]
                    self.front_matter_title = any(re.match(r"title\s*:", l) for l in block)
                    for j in range(i + 1):
                        self.code[j] = True
                    start = i + 1
                    break
        fence = None  # (char, length, line_no)
        in_comment = False
        for i in range(start, len(self.lines)):
            line = self.lines[i]
            m = FENCE.match(line)
            if fence:
                self.code[i] = True
                if m and m.group(1)[0] == fence[0] and len(m.group(1)) >= fence[1] and not m.group(2).strip():
                    fence = None
                continue
            if m and not in_comment and not (m.group(1)[0] == "`" and "`" in m.group(2)):
                fence = (m.group(1)[0], len(m.group(1)), i + 1)
                self.code[i] = True
                continue
            # Headings inside an HTML comment are not headings.
            if in_comment:
                if "-->" in line:
                    in_comment = False
                continue
            # An HTML comment block starts only at the beginning of a line (CommonMark).
            if re.match(r" {0,3}<!--", line) and "-->" not in line.split("<!--", 1)[1]:
                in_comment = True
            h = ATX.match(line)
            if h:
                self.headings.append((i + 1, len(h.group(1)), (h.group(2) or "").strip(), i, i + 1))
                continue
            s = SETEXT.match(line)
            prev = self.lines[i - 1] if i > start else ""
            if s and prev.strip() and not self.code[i - 1] and not ATX.match(prev) \
                    and not BULLET.match(prev) and "|" not in prev and not prev.lstrip().startswith("<"):
                level = 1 if s.group(1)[0] == "=" else 2
                self.headings.append((i, level, prev.strip(), i - 1, i + 1))
                continue
            for hm in HTML_HEADING.finditer(line):
                text = re.sub(r"<[^>]+>", "", hm.group(2)).strip()
                self.headings.append((i + 1, int(hm.group(1)), text, i, i + 1))
        if fence:
            self.fence_errors.append(fence[2])

    def prose(self, i):
        """Line i with inline code spans removed, or '' when the line is code."""
        return "" if self.code[i] else INLINE_CODE.sub("", self.lines[i])


def slug(text):
    """GitHub's heading anchor for text."""
    text = re.sub(r"!\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"<[^>]+>", "", text)
    text = text.replace("`", "").lower()
    out = []
    for ch in text:
        cat = unicodedata.category(ch)
        if ch in "-_" or cat[0] in "LN" or cat == "Mn":
            out.append(ch)
        elif ch == " ":
            out.append("-")
    return "".join(out)


_anchor_cache = {}


def anchors(path):
    path = Path(path).resolve()
    if path not in _anchor_cache:
        found = set()
        try:
            doc = Doc(path)
        except (OSError, UnicodeDecodeError):
            _anchor_cache[path] = None
            return None
        counts = {}
        for _, _, text, *_ in doc.headings:
            base = slug(text)
            n = counts.get(base, 0)
            counts[base] = n + 1
            found.add(base if n == 0 else f"{base}-{n}")
        for i, line in enumerate(doc.lines):
            if not doc.code[i]:
                found.update(m.group(1) for m in HTML_ID.finditer(line))
        _anchor_cache[path] = found
    return _anchor_cache[path]


def check(path):
    findings = []

    def add(line, severity, message):
        findings.append((line, severity, message))

    doc = Doc(path)
    for line in doc.fence_errors:
        add(line, "error", "code fence is never closed")

    # Title and heading levels.
    h1 = [h for h in doc.headings if h[1] == 1]
    if not h1 and not doc.front_matter_title:
        add(1, "error", "no H1 title")
    for line, *_ in h1[1:]:
        add(line, "error", "second H1 title; a document has one title")
    prev = 1 if doc.front_matter_title and not h1 else 0
    for line, level, text, *_ in doc.headings:
        if prev and level > prev + 1:
            add(line, "error", f"heading level skips from H{prev} to H{level}: {text!r}")
        prev = level
        if not text:
            add(line, "error", "empty heading")

    # Empty sections: a heading followed only by blank lines and comments, then a heading of
    # the same or a higher level, or the end of the file. A changelog's empty Unreleased section
    # is a valid Keep a Changelog state.
    is_changelog = CHANGELOG_NAME.match(doc.path.name)
    for idx, (line, level, text, _, after) in enumerate(doc.headings):
        if is_changelog and level == 2 and UNRELEASED.fullmatch(text.strip()):
            continue
        nxt = doc.headings[idx + 1] if idx + 1 < len(doc.headings) else None
        body = "\n".join(doc.lines[after:nxt[3] if nxt else len(doc.lines)])
        body = re.sub(r"<!--.*?-->", "", body, flags=re.S).strip()
        if not body and (nxt is None or nxt[1] <= level):
            add(line, "error", f"empty section: {text!r}")

    # Leftover template markers.
    for i, raw in enumerate(doc.lines):
        if doc.code[i]:
            if i > 0 and not FENCE.match(raw) and CODE_PLACEHOLDER.search(raw):
                add(i + 1, "error", f"template placeholder left in code: {CODE_PLACEHOLDER.search(raw).group(0)}")
            continue
        if GUIDE.search(doc.prose(i)):
            add(i + 1, "error", "template guide comment left in the document")
        for m in PLACEHOLDER.finditer(doc.prose(i)):
            add(i + 1, "error", f"template placeholder left: {m.group(0)}")
        for span in INLINE_CODE.finditer(raw):
            for m in CODE_PLACEHOLDER.finditer(span.group(0)):
                add(i + 1, "error", f"template placeholder left in code: {m.group(0)}")

    # Links and images.
    base = doc.path.parent
    for i in range(len(doc.lines)):
        text = doc.prose(i)
        targets = [(m.group(1) == "!", m.group(2), m.group(3)) for m in INLINE_LINK.finditer(text)]
        rd = REF_DEF.match(text)
        if rd:
            targets.append((False, rd.group(1), rd.group(2)))
        for is_image, label, target in targets:
            target = target.strip("<>")
            if is_image and not label.strip():
                add(i + 1, "warning", f"image has no alt text: {target}")
            if not target or EXTERNAL.match(target) or target.startswith("/") or PLACEHOLDER.search(target):
                continue
            file_part, _, anchor = target.partition("#")
            file_part = unquote(file_part.split("?", 1)[0])
            dest = (base / file_part) if file_part else doc.path
            if not dest.exists():
                add(i + 1, "error", f"broken link: {target} (no such file)")
                continue
            if anchor and dest.is_file() and dest.suffix.lower() in (".md", ".markdown"):
                found = anchors(dest)
                if found is not None and unquote(anchor).lower() not in {a.lower() for a in found}:
                    add(i + 1, "error", f"broken anchor: {target}")

    if is_changelog:
        findings.extend(check_changelog(doc))
    return sorted(findings)


def check_changelog(doc):
    findings = []
    releases = [h for h in doc.headings if h[1] == 2]
    unreleased = [h for h in releases if UNRELEASED.fullmatch(h[2].strip())]
    for line, *_ in unreleased[1:]:
        findings.append((line, "error", "second Unreleased section; merge it into the first"))
    if unreleased and releases and releases[0] != unreleased[0]:
        findings.append((unreleased[0][0], "error", "the Unreleased section must come before every release"))
    if unreleased and unreleased[0][2].startswith("[") and not any(
            re.match(r"^ {0,3}\[unreleased\]:", l, re.I) for l in doc.lines):
        findings.append((unreleased[0][0], "warning", "[Unreleased] has no link definition; write 'Unreleased' without brackets"))

    for line, _, text, *_ in releases:
        # A section ends at the next heading of level 2 or higher (some changelogs use H1 releases).
        end = next((h[0] for h in doc.headings if h[0] > line and h[1] <= 2), len(doc.lines) + 1)
        cats = [h for h in doc.headings if h[1] == 3 and line < h[0] < end]
        seen = set()
        for cline, _, ctext, *_ in cats:
            key = ctext.strip().lower()
            if key in seen:
                findings.append((cline, "error", f"duplicate category {ctext!r} in {text!r}"))
            seen.add(key)
            if unreleased and line == unreleased[0][0] and ctext.strip() not in KEEP_A_CHANGELOG:
                findings.append((cline, "warning", f"category {ctext!r} is not a Keep a Changelog category"))
        if unreleased and line == unreleased[0][0]:
            bullets = []
            for i in range(line, end - 1):
                m = BULLET.match(doc.lines[i]) if not doc.code[i] else None
                if m:
                    indent = len(re.match(r"\s*", doc.lines[i].expandtabs(4)).group(0))
                    bullets.append((indent, i, m.group(1)))
            # Entries are the section's least-indented bullets; deeper bullets are sub-items.
            top = min((b[0] for b in bullets), default=0)
            entries = {}
            for indent, i, item in bullets:
                if indent != top:
                    continue
                norm = re.sub(r"\s+", " ", item).strip().rstrip(".").lower()
                if norm in entries:
                    findings.append((i + 1, "error", f"duplicate Unreleased entry (also line {entries[norm]})"))
                else:
                    entries[norm] = i + 1
    return findings


def main(argv):
    if not argv or argv[0] in ("-h", "--help"):
        print(__doc__.strip())
        return 0 if argv else 2
    errors = 0
    for name in argv:
        path = Path(name)
        if not path.is_file():
            print(f"{name}:1: error: no such file")
            errors += 1
            continue
        try:
            findings = check(path)
        except UnicodeDecodeError:
            print(f"{name}:1: error: not UTF-8 text")
            errors += 1
            continue
        for line, severity, message in findings:
            print(f"{name}:{line}: {severity}: {message}")
            errors += severity == "error"
    if errors == 0:
        print(f"OK: {len(argv)} file(s), no errors")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

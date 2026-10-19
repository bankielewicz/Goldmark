#!/usr/bin/env python3
"""Check a precompact handoff folder: START-HERE.md, RESUME-PROMPT.md and TASKS.md.

Usage:
    python3 check_handoff.py [--root DIR] HANDOFF_DIR

HANDOFF_DIR is relative to DIR (the project root, `.` by default) unless absolute. The script reads and never
writes, and reads only the three files (not TASKS-archive.md or any other file in the folder). It checks:
- the three files exist; START-HERE.md and TASKS.md have their `##` headings first and in order (more `##` headings
  may follow the last required one; `###` headings may appear anywhere);
- RESUME-PROMPT.md has at most 40 lines and names START-HERE.md by its absolute path;
- no `[[fill: ...]]` placeholder is left outside code (a code span or a fenced block quotes the form);
- START-HERE section 2: each top-level bullet's own text (before any nested list) ends
  `[checked: <command> -> <result>]` or `(unverified...)`; section 3, under `### Decided`: each bullet holds a
  YYYY-MM-DD date and a quote in double quotes, or is `- None.`; section 5: each numbered item holds `Next:` and
  `Waits for:`, or a line of the section starts `Nothing outstanding`;
- references exist: each Markdown link target that isn't a URL, an anchor or `mailto:`, and the first backticked
  token of each START-HERE section 4 item, after removing a trailing `:LINE`, `:LINE-LINE`, `:LINE:COL` or `#Lnn` and
  expanding `~/`; a relative one under DIR or beside the file that names it. A reference whose line or the line
  above says `(not yet created)` or `(session-only)` isn't checked, nor a section 4 item that says so on any of its
  lines, nor a path holding a variable that isn't set. No other backticked text is checked;
- DIR/devforgeai/handoff/.gitignore holds the line `*`.

Prints `<file>:<line>: <problem>` and `<file>:<line>: warning: <text>` (START-HERE over 250 lines; today,
yesterday, tomorrow or recently outside quotes and code), then `handoff: clean`, or
`handoff: <n> problems, <m> warnings`. Line 0 is the file as a whole. Exit 0 with no problem (warnings allowed),
1 with problems, 2 when it can't run (HANDOFF_DIR missing or unreadable). Standard library only: it runs under
`python3 -S`.
"""
import argparse
import os
import re
import sys
from urllib.parse import unquote

START_HEADINGS = [
    "## 1. What this is",
    "## 2. Verified state",
    "## 3. Decisions",
    "## 4. Read first",
    "## 5. Outstanding",
    "## 6. Rules, traps and learnings",
    "## 7. Before acting",
]
TASKS_HEADINGS = ["## Done", "## Next"]
FILES = ["START-HERE.md", "RESUME-PROMPT.md", "TASKS.md"]
RESUME_MAX_LINES = 40
START_WARN_LINES = 250
MARKERS = ("(not yet created)", "(session-only)")

FENCE = re.compile(r"^\s*(`{3,}|~{3,})")
CODE_SPAN = re.compile(r"(`+)(.+?)\1")
H2 = re.compile(r"^##(?!#)\s")
H3 = re.compile(r"^###(?!#)\s+(.*?)\s*$")
BULLET = re.compile(r"^[-*+]\s")
ITEM = re.compile(r"^\d+[.)]\s")
NESTED = re.compile(r"^\s+([-*+]|\d+[.)])\s")
LINK = re.compile(r"(?<![\w\]])!?\[[^\]]*\]\(\s*(<[^>]*>|[^)\s]+)(?:\s+[\"'][^)]*[\"'])?\s*\)")
LINE_SUFFIX = re.compile(r"(:\d+(-\d+)?(:\d+)?|#L\d+(-L?\d+)?)$")
SCHEME = re.compile(r"^[A-Za-z][A-Za-z0-9+.-]+:")
DATE = re.compile(r"(?<!\d)\d{4}-\d{2}-\d{2}(?!\d)")
QUOTE = re.compile(r"\"[^\"]+\"|“[^”]+”")
RELATIVE_DATE = re.compile(r"\b(today|yesterday|tomorrow|recently)\b", re.I)
EMPHASIS = "*_ "


class Doc:
    """One Markdown file: its lines, which of them are in fenced blocks, and its `##` sections."""

    def __init__(self, path, text):
        self.path = path
        self.lines = text.splitlines()
        self.fenced = []
        fence = None
        for line in self.lines:
            m = FENCE.match(line)
            if fence is None:
                if m:
                    fence = m.group(1)
                self.fenced.append(m is not None)
            else:
                self.fenced.append(True)
                if m and m.group(1)[0] == fence[0] and len(m.group(1)) >= len(fence) \
                        and not line.strip().lstrip(fence[0]):
                    fence = None
        self.h2 = [(i, self.lines[i].rstrip()) for i in range(len(self.lines))
                   if not self.fenced[i] and H2.match(self.lines[i])]

    def text_lines(self, start=0, end=None):
        """(index, line) for lines outside fences in [start, end)."""
        end = len(self.lines) if end is None else end
        return [(i, self.lines[i]) for i in range(start, end) if not self.fenced[i]]

    def section(self, heading):
        """The line range [start, end) of the `##` section with this heading, or None."""
        for n, (i, h) in enumerate(self.h2):
            if h == heading:
                end = self.h2[n + 1][0] if n + 1 < len(self.h2) else len(self.lines)
                return i, end
        return None

    def blocks(self, start, end, opener):
        """List items (bullets or numbered) starting at column 0 in [start, end), each with the indexes of its lines:
        indented lines, lazy continuation lines and fenced lines belong to it, across blank lines; a heading, another
        item at column 0 or unindented text after a blank line ends it."""
        out, current, blank = [], None, False
        for i in range(start, end):
            line = self.lines[i]
            if self.fenced[i]:
                if current is not None:
                    current.append(i)
                continue
            if opener.match(line):
                current, blank = [i], False
                out.append(current)
            elif current is None:
                continue
            elif not line.strip():
                blank = True
            elif H2.match(line) or H3.match(line) or BULLET.match(line) or ITEM.match(line):
                current = None
            elif line[0] in " \t" or not blank:
                current.append(i)
                blank = False
            else:
                current = None
        return out

    def joined(self, indexes):
        return " ".join(self.lines[i].strip() for i in indexes if not self.fenced[i])


def no_code(line):
    return CODE_SPAN.sub("", line)


def display(path, root):
    rel = os.path.relpath(path, root)
    return (path if rel.startswith("..") else rel).replace(os.sep, "/")


class Checker:
    def __init__(self, root, handoff):
        self.root = root
        self.handoff = handoff
        self.out = []  # (file order, line, kind, text)
        self.problems = 0
        self.warnings = 0

    def problem(self, order, path, line, text):
        self.problems += 1
        self.out.append((order, line, 0, f"{display(path, self.root)}:{line}: {text}"))

    def warning(self, order, path, line, text):
        self.warnings += 1
        self.out.append((order, line, 1, f"{display(path, self.root)}:{line}: warning: {text}"))

    def exists(self, target, holder, link):
        if os.path.isabs(target):
            # A link starting with / may be written from the project root.
            return os.path.exists(target) or (link and os.path.exists(os.path.join(self.root, target.lstrip("/"))))
        return os.path.exists(os.path.join(self.root, target)) \
            or os.path.exists(os.path.join(os.path.dirname(holder), target))

    def reference(self, order, doc, i, target, link):
        """Check one reference named on line i of doc."""
        above = doc.lines[i - 1] if i > 0 else ""
        if any(m in doc.lines[i] or m in above for m in MARKERS):
            return
        target = target.strip()
        if target.startswith("<") and target.endswith(">"):
            target = target[1:-1].strip()
        if not target or target.startswith("#") or "://" in target or target.lower().startswith("mailto:"):
            return
        if SCHEME.match(target) and not re.match(r"^[A-Za-z]:[\\/]", target):
            return
        target = LINE_SUFFIX.sub("", target)
        bare = target.split("#", 1)[0].split("?", 1)[0] if link else target
        if link:
            bare = unquote(bare)
        if not bare:
            return
        if bare.startswith("~/") or bare == "~":
            bare = os.path.expanduser(bare)
        bare = os.path.expandvars(bare)
        if "$" in bare:
            return  # a variable that isn't set here, such as one only the session substitutes
        if not self.exists(bare, doc.path, link):
            self.problem(order, doc.path, i + 1, f"`{target}` doesn't exist (mark it (not yet created) or "
                                                 f"(session-only) if that is meant)")

    def common(self, order, doc):
        """Placeholders, links and relative dates: every file."""
        for i, line in doc.text_lines():
            plain = no_code(line)
            if "[[fill:" in plain:
                self.problem(order, doc.path, i + 1, "a [[fill: ...]] placeholder is left: fill it or remove the line")
            for m in LINK.finditer(plain):
                self.reference(order, doc, i, m.group(1), link=True)
            words = RELATIVE_DATE.findall(QUOTE.sub("", plain))
            if words:
                said = ", ".join(f'"{w}"' for w in words)
                self.warning(order, doc.path, i + 1, f"relative date {said}: write the date (YYYY-MM-DD)")

    def headings(self, order, doc, required):
        found = doc.h2
        for n, want in enumerate(required):
            if n >= len(found):
                self.problem(order, doc.path, len(doc.lines), f"missing heading '{want}' (the headings, in order: "
                                                               + "; ".join(required) + ")")
                return
            line, have = found[n]
            if have != want:
                self.problem(order, doc.path, line + 1, f"expected heading '{want}' here, found '{have}'")
                return

    def start_here(self, order, doc):
        self.headings(order, doc, START_HEADINGS)
        if len(doc.lines) > START_WARN_LINES:
            self.warning(order, doc.path, START_WARN_LINES + 1,
                         f"START-HERE.md has {len(doc.lines)} lines: keep it within about {START_WARN_LINES}, "
                         "pointing to files instead of copying them")
        s2 = doc.section(START_HEADINGS[1])
        if s2:
            for block in doc.blocks(s2[0] + 1, s2[1], BULLET):
                own = [block[0]] + [j for j in block[1:] if not doc.fenced[j]]
                cut = next((k for k, j in enumerate(own) if k and NESTED.match(doc.lines[j])), len(own))
                text = doc.joined(own[:cut]).rstrip().rstrip(EMPHASIS)
                text = text[:-1].rstrip(EMPHASIS) if text.endswith(".") else text
                checked = text.rfind("[checked:")
                ok_checked = checked >= 0 and text.endswith("]") and (
                    "->" in text[checked:] or "→" in text[checked:])
                ok_unverified = text.rfind("(unverified") >= 0 and text.endswith(")")
                if not (ok_checked or ok_unverified):
                    self.problem(order, doc.path, block[0] + 1, "a verified-state bullet must end "
                                 "[checked: <command> -> <what it showed>] or (unverified: <where from>, <date>)")
        s3 = doc.section(START_HEADINGS[2])
        if s3:
            subs = [(i, H3.match(l).group(1)) for i, l in doc.text_lines(s3[0] + 1, s3[1]) if H3.match(l)]
            for n, (i, name) in enumerate(subs):
                if name != "Decided":
                    continue
                end = subs[n + 1][0] if n + 1 < len(subs) else s3[1]
                for block in doc.blocks(i + 1, end, BULLET):
                    text = doc.joined(block)
                    if re.fullmatch(r"[-*+]\s+None\.?", text):
                        continue
                    if not DATE.search(text):
                        self.problem(order, doc.path, block[0] + 1, "a decision needs its date as YYYY-MM-DD")
                    if not QUOTE.search(text):
                        self.problem(order, doc.path, block[0] + 1,
                                     "a decision needs a quote of the decider's words, in double quotes")
        s4 = doc.section(START_HEADINGS[3])
        if s4:
            for block in doc.blocks(s4[0] + 1, s4[1], ITEM):
                above = doc.lines[block[0] - 1] if block[0] > 0 else ""
                if any(m in doc.lines[j] or m in above for j in block for m in MARKERS):
                    continue
                for j in block:
                    m = None if doc.fenced[j] else CODE_SPAN.search(doc.lines[j])
                    if m:
                        self.reference(order, doc, j, m.group(2), link=False)
                        break
        s5 = doc.section(START_HEADINGS[4])
        if s5:
            items = doc.blocks(s5[0] + 1, s5[1], ITEM)
            for block in items:
                text = doc.joined(block)
                for word in ("Next:", "Waits for:"):
                    if word not in text:
                        self.problem(order, doc.path, block[0] + 1, f"an outstanding item needs '{word}' "
                                     "(N. <what>. Next: <action>. Waits for: <what, or nothing>.)")
            nothing = any(l.strip().lstrip(EMPHASIS).startswith("Nothing outstanding")
                          for _, l in doc.text_lines(s5[0] + 1, s5[1]))
            if not items and not nothing:
                self.problem(order, doc.path, s5[0] + 1, "section 5 needs at least one item "
                             "(N. <what>. Next: .... Waits for: ....) or the line 'Nothing outstanding.'")

    def resume(self, order, doc):
        if len(doc.lines) > RESUME_MAX_LINES:
            self.problem(order, doc.path, RESUME_MAX_LINES + 1,
                         f"RESUME-PROMPT.md has {len(doc.lines)} lines: keep it within {RESUME_MAX_LINES} lines "
                         "(about 15)")
        start = os.path.join(self.handoff, "START-HERE.md")
        forms = {os.path.abspath(start), os.path.realpath(start)}
        text = "\n".join(doc.lines)
        # The path itself, not a longer one holding it (START-HERE.md.bak, /other/<path>); a full stop may follow.
        if not any(re.search(r"(?<![\w./-])" + re.escape(f) + r"(?![\w/-]|\.\w)", text) for f in forms):
            self.problem(order, doc.path, 0, f"doesn't name START-HERE.md by its absolute path "
                         f"({os.path.abspath(start)})")

    def run(self):
        for order, name in enumerate(FILES):
            path = os.path.join(self.handoff, name)
            if not os.path.isfile(path):
                self.problem(order, path, 0, "missing file")
                continue
            try:
                with open(path, encoding="utf-8", errors="replace") as f:
                    doc = Doc(path, f.read())
            except OSError as e:
                self.problem(order, path, 0, f"can't be read: {e.strerror or e}")
                continue
            if name == "START-HERE.md":
                self.start_here(order, doc)
            elif name == "TASKS.md":
                self.headings(order, doc, TASKS_HEADINGS)
            elif name == "RESUME-PROMPT.md":
                self.resume(order, doc)
            self.common(order, doc)
        ignore = os.path.join(self.root, "devforgeai", "handoff", ".gitignore")
        order = len(FILES)
        try:
            with open(ignore, encoding="utf-8", errors="replace") as f:
                if "*" not in [l.strip() for l in f.read().splitlines()]:
                    self.problem(order, ignore, 0, "doesn't hold the line *")
        except FileNotFoundError:
            self.problem(order, ignore, 0, "missing: write it with the line *")
        except OSError as e:
            self.problem(order, ignore, 0, f"can't be read: {e.strerror or e}")
        for _, _, _, line in sorted(self.out, key=lambda x: (x[0], x[1], x[2])):
            print(line)
        if self.problems == 0 and self.warnings == 0:
            print("handoff: clean")
        else:
            print(f"handoff: {self.problems} problems, {self.warnings} warnings")
        return 1 if self.problems else 0


def main(argv):
    parser = argparse.ArgumentParser(description="Check a precompact handoff folder.")
    parser.add_argument("--root", default=".", help="the project root (default: .)")
    parser.add_argument("handoff_dir", help="the handoff folder, relative to the root unless absolute")
    args = parser.parse_args(argv)
    root = os.path.abspath(args.root)
    handoff = os.path.normpath(os.path.join(root, args.handoff_dir))
    if not os.path.isdir(root):
        print(f"check_handoff: no folder {args.root}", file=sys.stderr)
        return 2
    if not os.path.isdir(handoff):
        print(f"check_handoff: no handoff folder {display(handoff, root)}: write the files first", file=sys.stderr)
        return 2
    try:
        os.listdir(handoff)
    except OSError as e:
        print(f"check_handoff: can't read the handoff folder {display(handoff, root)}: {e.strerror or e}",
              file=sys.stderr)
        return 2
    return Checker(root, handoff).run()


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

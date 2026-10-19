#!/usr/bin/env python3
"""The chain listing of a DevForgeAI project (SPEC-012 version 17, IF-03, BEH-26, ERR-13, ERR-14).

Run from anywhere:
    python3 chain_state.py --root DIR

Lists the Markdown documents under DIR/docs/specs/ as JSON (DM-05, devforgeai-chain/1) on stdout: for each document with
a frontmatter `id`, its id, type, status, version, updated, path and the IDs of its `upstream` entries. It reads each
file only as far as the line that closes its frontmatter, and the frontmatter by the text of its top-level keys, not as
YAML (SPEC-012 section 4). It judges nothing: it reads no body, checks no status, version or link, and does not say which
phase a project is in.

Exit 0 whenever it prints, an empty `documents` list when there is no docs/specs/; exit 2, with nothing on stdout and
one line on stderr, when --root is missing, isn't a folder or can't be read, or an argument is unknown. It follows no
symbolic link, writes no file, opens no network connection and runs no other program (QR-06). Standard library only; the
same files under --root always give the same bytes (QR-05).
"""
import json
import os
import re
import stat
import sys

FORMAT = "devforgeai-chain/1"
VALUE = re.compile(r"[ \t]*[\"']?([^\s\"'#,}]+)")  # after `key:`: an optional quote, then up to whitespace, a quote, #, , or }
# A list line gives its ID from `{id: <ID>` or `- id: <ID>` at the start of the line (BEH-26), after an optional `-` for
# the flow form: never from the text of a note, a comment or a key that isn't first in its entry.
UPSTREAM_ID = re.compile(r"\s*(?:-\s+id:|(?:-\s*)?\{\s*id:)\s*[\"']?([A-Za-z0-9-]+)")


class Fail(Exception):
    """A reason the script can't run: exit 2, with the message on stderr."""


def parse(argv):
    """The --root value; --root DIR or --root=DIR, and nothing else is accepted."""
    root, args = None, list(argv)
    while args:
        arg = args.pop(0)
        if arg == "--root" or arg.startswith("--root="):
            if root is not None:
                raise Fail("--root is given twice")
            if arg == "--root":
                if not args:
                    raise Fail("--root needs a folder")
                root = args.pop(0)
            else:
                root = arg[len("--root="):]
        else:
            raise Fail("unknown argument %s" % arg)
    if root is None:
        raise Fail("--root is required")
    return root


def frontmatter(path):
    """The lines between the file's first two --- lines (the first line of the file being the first of them), or None
    when the file has none: its first line isn't --- or it never closes. Read in binary and decoded line by line, so
    nothing after the closing line is ever decoded (QR-06). OSError or UnicodeDecodeError when it can't be read."""
    with open(path, "rb") as handle:
        if handle.readline().decode("utf-8").rstrip() != "---":
            return None
        lines = []
        for raw in handle:
            line = raw.decode("utf-8")
            if line.rstrip() == "---":
                return lines
            lines.append(line.rstrip("\r\n"))
    return None


def top_level(lines, key):
    """The value of a column-0 `key:` line (the first such line), as BEH-26 reads it, or None."""
    prefix = key + ":"
    for line in lines:
        if line.startswith(prefix):
            found = VALUE.match(line, len(prefix))
            return found.group(1) if found else None
    return None


def upstream_ids(lines):
    """The IDs of the `upstream` list, each once in file order: the lines after a column-0 `upstream:` up to the next
    column-0 line that isn't blank, a comment or a list item beginning with `-` (BEH-26)."""
    ids, inside = [], False
    for line in lines:
        if not inside:
            inside = line.startswith("upstream:")
            continue
        if line.strip() and not line[0].isspace() and line[0] not in "#-":
            break
        found = UPSTREAM_ID.match(line)
        if found and found.group(1) not in ids:
            ids.append(found.group(1))
    return ids


def document(lines, path):
    """DM-05's entry for a document's frontmatter, or None when it has no id (ERR-13)."""
    ident = top_level(lines, "id")
    if ident is None:
        return None
    version = top_level(lines, "version")
    if version is not None and re.fullmatch(r"[0-9]+", version):
        try:
            version = int(version)
        except ValueError:  # all digits, but more than int() takes (4,300 under Python's default): the text as written
            pass
    return {"id": ident, "type": top_level(lines, "type"), "status": top_level(lines, "status"), "version": version,
            "updated": top_level(lines, "updated"), "path": path, "upstream": upstream_ids(lines)}


def is_folder(path):
    """Whether path is a folder and not a link to one: nothing here follows a symbolic link."""
    try:
        return stat.S_ISDIR(os.lstat(path).st_mode)
    except OSError:
        return False


def listing(root):
    """(the documents, the number of files left out) under root/docs/specs/ (BEH-26, ERR-13)."""
    if not os.path.isdir(root):
        raise Fail("%s: not a folder" % root)
    if not os.access(root, os.R_OK | os.X_OK):
        raise Fail("%s: can't be read" % root)
    documents, skipped = [], 0
    specs = os.path.join(root, "docs", "specs")
    if not (is_folder(os.path.join(root, "docs")) and is_folder(specs)):
        return documents, skipped
    pending = [(specs, "docs/specs")]
    while pending:
        folder, rel = pending.pop()
        try:
            entries = list(os.scandir(folder))
        except OSError:
            skipped += 1
            continue
        for entry in entries:
            name = entry.name
            try:
                name.encode("utf-8")
            except UnicodeEncodeError:  # a name that isn't UTF-8 can't be a path in the JSON
                if entry.is_dir(follow_symlinks=False) or name.endswith(".md"):
                    skipped += 1
                continue
            if entry.is_symlink():  # a link to a file or a folder: left out, never followed
                skipped += 1
            elif entry.is_dir(follow_symlinks=False):
                pending.append((entry.path, rel + "/" + name))
            elif name.endswith(".md") and entry.is_file(follow_symlinks=False):
                try:
                    lines = frontmatter(entry.path)
                except (OSError, UnicodeDecodeError):
                    skipped += 1
                    continue
                found = document(lines, rel + "/" + name) if lines is not None else None
                if found is None:
                    skipped += 1
                else:
                    documents.append(found)
    # By type (null first), then id, then path, in the byte order of the text: never the order of a folder listing.
    documents.sort(key=lambda d: (d["type"] is not None, (d["type"] or "").encode("utf-8"), d["id"].encode("utf-8"),
                                  d["path"].encode("utf-8")))
    return documents, skipped


def main(argv=None):
    try:
        documents, skipped = listing(parse(sys.argv[1:] if argv is None else argv))
    except Fail as why:
        print("chain_state: %s" % why, file=sys.stderr)
        return 2
    text = json.dumps({"format": FORMAT, "documents": documents, "skipped": skipped}, sort_keys=True, indent=2,
                      ensure_ascii=False) + "\n"
    sys.stdout.buffer.write(text.encode("utf-8"))
    sys.stdout.buffer.flush()
    return 0


if __name__ == "__main__":
    sys.exit(main())

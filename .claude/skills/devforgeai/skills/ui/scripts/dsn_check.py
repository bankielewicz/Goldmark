#!/usr/bin/env python3
"""Check a DevForgeAI design document (DSN) and the boards folder it records (SPEC-017 IF-01 to IF-04).

Usage:
    python3 dsn_check.py next [--root DIR]
    python3 dsn_check.py boards [--root DIR] DSN-NNN
    python3 dsn_check.py check [--before-amend] [--root DIR] DSN-NNN
    python3 dsn_check.py head [--root DIR] DSN-NNN [--] FILE
    python3 dsn_check.py place [--root DIR] DSN-NNN --sha-file PATH

DIR is the project root (`.` by default). Output is on standard output. Exit 0 is success, 1 a problem the script
reports, 2 that it cannot run: no or wrong arguments, a root it cannot read, a DSN file or a BRN it cannot read. A run
that cannot run prints one line, `Cannot run: <reason>.`, and nothing else. A boards folder that fails DM-04 is a problem
it reports, in every subcommand that reads it: `ERR-03`, `ERR-04`, `ERR-05` or `ERR-12` (the first that fails), or an
`ERR-06` line for every board that fails; `check` (without --before-amend) reports it as one `boards` error.

- next: prints `next: DSN-NNN` (one more than the highest DSN-NNN.md in docs/specs/design/, DSN-001 with none) and
  `pending boards folders: ...`. It reads only the names in that folder.
- boards: checks docs/specs/design/DSN-NNN/boards/ against the canvas.json contract (DM-04) and prints
  `canvas.json: v<N>, <n> boards`, a `board <k> <file> <bytes> <lines> <sha256>` line for each board in canvas order
  and `boards: ok`, with `canvas.json sha256 <hex>` after the first line and `<x> <y>` (numbers, or `-`) at the end of each
  board line; or ERR-03, ERR-04, ERR-05 or ERR-12 (the first that fails), or an ERR-06 line for every board that fails,
  then `boards: <n> problem(s)`.
- check: applies the rules of the DSN (frontmatter, boards, mapping, links, coverage, approval, changelog, placeholder)
  and prints `<file>:<line>: <part>: <message> (<rule>)` for each error, `warning: ...` for each suspect reference,
  then `OK <file>` or `INVALID: <n> error(s) in <file>`. With --before-amend it applies the structural rules only and
  prints what it did not enforce as `fact: ...` lines (the facts an amend run starts from), then
  `OK <file> (before amend)` or `INVALID: ...`.
- head: prints at most 150 lines and 16 KB of one board file that canvas.json names, a line over 500 characters cut to
  500 and marked `[cut]`, then `head: <shown> of <lines> lines, <bytes shown> of <bytes> bytes, <n> lines cut`: what was
  left out is the difference between the "of" numbers, and <n> is the number of lines that were cut short.

- place: moves an import from docs/specs/design/DSN-NNN/project/ into boards/, after checking canvas.json and every board it
  names as `boards` does and each staged file against the `<hex>  <file>` lines of the file PATH (written by the skill from
  the tool's results; the file names are read from that file and are never command-line words). It moves nothing on any
  problem. It is the only subcommand that writes, and it writes only inside that DSN's folder: it makes boards/ if it is
  absent, replaces the files of the same names there, removes project/ when it is then empty, and does nothing else. A
  PATH that is not a regular file, a line that is not `<64 hex digits>  <file>`, and a line naming a file canvas.json does
  not name are `Cannot run`. A move that fails midway stops, names the files placed and not placed, and moves nothing back.

Standard library only: it runs under `python3 -S`. It opens no network connection, starts no process, follows no symbolic
link and reads only the paths each subcommand names; every subcommand but `place` writes no file. It reads the frontmatter and the `boards:`
block with a fixed-shape reader (flat mappings, flow sequences, double-quoted scalars with escapes, integers, dates and
null); PyYAML, when installed, is only a syntax cross-check and gives the same verdict.
"""
import datetime
import hashlib
import json
import os
import re
import stat
import sys

# --- Constants (each one is a number the spec fixes, or a bound with its reason) ---------------------------------------
SUPPORTED_V = 3                       # DM-04: the only canvas.json `v` the spec supports (ERR-05)
MAX_BOARDS = 99                       # DM-02, ERR-12: BRD-NN has two digits, so 99 items
HEAD_MAX_LINES = 150                  # DM-04, IF-04: enough for a board's heading and structure
OUTPUT_MAX_BYTES = 16 * 1024          # DM-04, IF-04: the whole of `head`'s output stays within 16 KB
TRAILER_RESERVE = 200                 # room kept for head's trailer line inside OUTPUT_MAX_BYTES (it is under 100)
LINE_MAX_CHARS = 500                  # DM-04, IF-04: a longer line is cut to 500 characters and marked [cut]
LINE_PREFIX_BYTES = 4 * LINE_MAX_CHARS + 4   # 500 characters are at most 2000 bytes (UTF-8); a few more to see the 501st
READ_CHUNK = 64 * 1024                # a board is read in chunks, so a minified board of one 2 MB line costs no more memory
MAX_DSN_NUMBER = 999                  # DSN-NNN has three digits (design.schema.json)

STATUSES = ("draft", "in-review", "approved", "superseded", "deprecated")
SURFACES = ("web", "desktop", "mobile", "terminal")
COVERAGE_STATUSES = ("designed", "not a screen", "no board yet", "withdrawn")
FM_KEYS = ["id", "type", "title", "status", "version", "created", "updated", "owner", "authors", "generated_by",
           "reviewed_by", "approved_by", "approved_on", "upstream", "supersedes", "superseded_by", "blocked_by",
           "canvas", "canvas_version", "canvas_format", "boards_root", "considered"]
NULLABLE_KEYS = {"approved_on", "superseded_by", "canvas", "canvas_version"}   # keys whose value may be null
GENERATED_BY_KEYS = ("tool", "model", "session")
BOARD_REQUIRED = ["status", "file", "title", "flow", "surface", "ideas", "answers", "sha256"]   # and `id`, which starts an item
BOARD_OPTIONAL = ["superseded_by", "notes"]
LINK_KEYS = {"id", "item", "relation", "version", "hash", "note"}
SECTION_HEADINGS = {"boards": "## 2. Boards", "coverage": "## 3. Idea coverage", "questions": "## 5. Open questions",
                    "changelog": "## Change Log"}
CHANGELOG_HEADER = ["Version", "Date", "Author", "Change", "Items affected"]
RULE_ORDER = {"frontmatter": 0, "boards": 1, "mapping": 2, "links": 3, "coverage": 4, "approval": 5, "changelog": 6,
              "placeholder": 7}

RE_DSN = re.compile(r"DSN-\d{3}")
RE_BRN = re.compile(r"BRN-\d{3}")
RE_BRD = re.compile(r"BRD-\d{2}")
RE_IDEA = re.compile(r"IDEA-\d{2}")
RE_SLUG = re.compile(r"[a-z0-9]+(-[a-z0-9]+)*")
RE_SHA = re.compile(r"[0-9a-f]{64}")
RE_ANSWER = re.compile(r"PRD-\d{3}#N?FR-\d{3}|ADR-\d{3}")
RE_CONSIDERED = re.compile(r"(PRD|ADR)-\d{3}@\d+|declined:(PRD-\d{3}#N?FR-\d{3}|ADR-\d{3})")
RE_BLOCKED = re.compile(r"(BRN|PRD|ARCH|EPIC|SPR|STORY|SPEC|ADR|SKL|POL|TASK|TEST|CTX|AMB|DSN)-\d{3}(#[A-Z]+-\d{2,3})?")
RE_DOC_ID = re.compile(r"[A-Z]+-\d{3}")
RE_ITEM_ID = re.compile(r"[A-Z]+-\d{2,3}")
RE_DATE = re.compile(r"\d{4}-\d{2}-\d{2}")
RE_KEY_LINE = re.compile(r"^([A-Za-z_][A-Za-z0-9_-]*):(?:[ \t]+(.*))?$")
RE_FENCE_OPEN = re.compile(r"^```(.*)$")
RE_BACKTICKS = re.compile(r"`+")
MARKER_OPEN = "[NEEDS CLARIFICATION"
MAX_NESTING = 8                       # a field of the DSN holds a list or a mapping, two deep at most; this stops a hostile line
                                      # from exhausting the stack


class CannotRun(Exception):
    """The script cannot run: the message is the reason, without a final full stop."""


class Unreadable(Exception):
    """A file that must be a readable regular file is not: `reason` is a clause that stands alone."""

    def __init__(self, kind, reason):
        super().__init__(reason)
        self.kind = kind
        self.reason = reason


class FolderProblem(Exception):
    """`check --before-amend` met a boards folder that fails DM-04: `lines` are the ERR-NN lines IF-02 would print."""

    def __init__(self, lines):
        super().__init__(lines[0])
        self.lines = lines


class BoardsProblem(Exception):
    """The boards folder fails ERR-03, ERR-04, ERR-05 or ERR-12 (`code` is the number)."""

    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


# --- Files: regular files only, never through a symbolic link -----------------------------------------------------
def _os_reason(e):
    return e.strerror or str(e)


def regular_file(path):
    """lstat a path and require a regular file; Unreadable says why not. Nothing is opened."""
    try:
        st = os.lstat(path)
    except (FileNotFoundError, NotADirectoryError):
        raise Unreadable("absent", "the file is absent")
    except (OSError, ValueError) as e:      # ValueError: a name with a NUL or a lone surrogate cannot be a path
        raise Unreadable("unreadable", f"the file cannot be read ({_os_reason(e)})")
    if stat.S_ISLNK(st.st_mode):
        raise Unreadable("link", "the file is a symbolic link, which is never followed")
    if not stat.S_ISREG(st.st_mode):
        raise Unreadable("notfile", "the file is not a regular file")
    return st


def open_regular(path):
    """A read-only file descriptor on a regular file that is not a symbolic link (O_NOFOLLOW where it exists)."""
    regular_file(path)
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0)
    try:
        return os.open(path, flags)
    except (OSError, ValueError) as e:
        raise Unreadable("unreadable", f"the file cannot be read ({_os_reason(e)})")


def read_file(path):
    fd = open_regular(path)
    try:
        with os.fdopen(fd, "rb") as f:
            return f.read()
    except OSError as e:
        raise Unreadable("unreadable", f"the file cannot be read ({_os_reason(e)})")


class Scan:
    """One pass over a board file: size, line count, SHA-256 and the first lines kept (each cut to a bounded prefix)."""

    def __init__(self, size, lines, sha, head):
        self.size = size
        self.lines = lines
        self.sha = sha
        self.head = head      # [(prefix bytes, byte length without the newline, ended with a newline)]


def scan_file(path, keep=0):
    fd = open_regular(path)
    sha = hashlib.sha256()
    size = lines = 0
    head = []
    cur = bytearray()
    cur_len = 0
    try:
        with os.fdopen(fd, "rb") as f:
            while True:
                chunk = f.read(READ_CHUNK)
                if not chunk:
                    break
                sha.update(chunk)
                size += len(chunk)
                start = 0
                while True:
                    j = chunk.find(b"\n", start)
                    piece = chunk[start:] if j < 0 else chunk[start:j]
                    if len(head) < keep:
                        room = LINE_PREFIX_BYTES - len(cur)
                        if room > 0:
                            cur += piece[:room]
                    cur_len += len(piece)
                    if j < 0:
                        break
                    lines += 1
                    if len(head) < keep:
                        head.append((bytes(cur), cur_len, True))
                    cur = bytearray()
                    cur_len = 0
                    start = j + 1
    except OSError as e:
        raise Unreadable("unreadable", f"the file cannot be read ({_os_reason(e)})")
    if cur_len > 0:                     # a last line with no newline
        lines += 1
        if len(head) < keep:
            head.append((bytes(cur), cur_len, False))
    return Scan(size, lines, sha.hexdigest(), head)


# A name that holds one of these would forge or split a line of this script's output (`board <k> <file> ...`, `fact: ...`):
# C0 and C1 controls (newline, carriage return, tab, NUL, DEL, NEL), the Unicode line and paragraph separators, and the
# surrogates, which no file system can encode.
RE_UNSAFE_NAME = re.compile("[\x00-\x1f\x7f-\x9f\u2028\u2029\ud800-\udfff]")


def name_problem(name):
    """Why a board name is not a plain file name (ERR-06), or None."""
    if name in ("", ".") or "/" in name or "\\" in name or ".." in name or RE_UNSAFE_NAME.search(name):
        return ('the name is not a plain file name (it must not be empty or ".", or hold /, \\ or .., '
                'or hold a control character or a line separator)')
    try:
        os.fsencode(name)
    except (UnicodeError, ValueError):
        return "the name is not a plain file name (the file system cannot encode it)"
    return None


def jstr(value):
    """A name as JSON text: quoted, with escapes, so that an empty name or a backslash shows plainly. Control characters
    are escaped by JSON; DEL, the C1 controls, the line separators and surrogates are escaped here, so the text is one
    printable line."""
    text = json.dumps(value, ensure_ascii=False)
    return re.sub("[\x7f-\x9f\u2028\u2029\ud800-\udfff]", lambda m: "\\u%04x" % ord(m.group(0)), text)


# --- canvas.json (DM-04) ------------------------------------------------------------------------------------------
class RawNumber(str):
    """A JSON number with a fraction or exponent, kept as written so ERR-05 can name it."""


class JsonObject(dict):
    dups = ()


def _json_pairs(pairs):
    obj = JsonObject()
    dups = []
    for k, v in pairs:
        if k in obj:
            dups.append(k)
        obj[k] = v
    obj.dups = dups
    return obj


def _json_constant(name):
    raise ValueError(f"{name} is not valid JSON")


def shown_json(value):
    if isinstance(value, RawNumber):
        return str(value)
    try:
        text = json.dumps(value, ensure_ascii=False)
    except (TypeError, ValueError):
        text = repr(value)
    return text if len(text) <= 40 else text[:37] + "..."


class Canvas:
    def __init__(self, names, v, sha, positions):
        self.names = names
        self.v = v
        self.sha = sha              # the SHA-256 of canvas.json's bytes (BEH-26 compares it with the tool's)
        self.positions = positions  # [(x, y)] as text, one for each board: a number as written, or "-"


def number_text(value):
    """A board's x or y for IF-02: the number as the file writes it, else a dash (a boolean is not a number)."""
    if isinstance(value, bool):
        return "-"
    if isinstance(value, (int, RawNumber)):
        return str(value)
    return "-"


def boards_rel(dsn, sub="boards"):
    return f"docs/specs/design/{dsn}/{sub}/"


def boards_path(root, dsn, *more, sub="boards"):
    return os.path.join(root, "docs", "specs", "design", dsn, sub, *more)


def read_canvas(root, dsn, sub="boards"):
    """Read and validate canvas.json in the boards folder (or, for `place`, the staging folder `project`): the first
    failing of ERR-03, ERR-04, ERR-05 and ERR-12 raises BoardsProblem."""
    rel = boards_rel(dsn, sub)
    what = "boards folder" if sub == "boards" else "staging folder"
    copy_hint = ("copy canvas.json and the board files it names there, or import the canvas with the skill" if sub == "boards"
                 else "import the canvas again: the Artifact tool saves its files there")
    design = os.path.join(root, "docs", "specs", "design")
    for path in (os.path.join(design, dsn), boards_path(root, dsn, sub=sub)):
        try:
            st = os.lstat(path)
        except (FileNotFoundError, NotADirectoryError):
            raise BoardsProblem("03", f"the {what} {rel} does not exist; {copy_hint}")
        except OSError as e:
            raise BoardsProblem("03", f"the {what} {rel} cannot be read ({_os_reason(e)})")
        if stat.S_ISLNK(st.st_mode):
            raise BoardsProblem("03", f"the {what} {rel} is, or lies in, a symbolic link, which is never followed")
        if not stat.S_ISDIR(st.st_mode):
            raise BoardsProblem("03", f"{rel} is not a folder; {copy_hint}")
    try:
        entries = os.listdir(boards_path(root, dsn, sub=sub))
    except OSError as e:
        raise BoardsProblem("03", f"the {what} {rel} cannot be listed ({_os_reason(e)})")
    if not entries:
        raise BoardsProblem("03", f"the {what} {rel} is empty; {copy_hint}")
    canvas_path = boards_path(root, dsn, "canvas.json", sub=sub)
    try:
        raw = read_file(canvas_path)
    except Unreadable as u:
        if u.kind == "absent":
            raise BoardsProblem("03", f"the {what} {rel} has no canvas.json; {copy_hint}")
        if u.kind in ("link", "notfile"):
            raise BoardsProblem("03", f"{rel}canvas.json is not a regular file ({u.reason}); {copy_hint}")
        raise BoardsProblem("04", f"{rel}canvas.json cannot be read ({u.reason}); copy it again")
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as e:
        raise BoardsProblem("04", f"{rel}canvas.json is not valid UTF-8 (byte {e.start}); the copy may be damaged, copy it again")
    try:
        data = json.loads(text, object_pairs_hook=_json_pairs, parse_float=RawNumber, parse_constant=_json_constant)
    except (ValueError, RecursionError) as e:
        raise BoardsProblem("04", f"{rel}canvas.json is not valid JSON ({' '.join(str(e).split())}); the copy may be damaged or from another tool, copy it again")
    if not isinstance(data, JsonObject):
        raise BoardsProblem("04", f"{rel}canvas.json must be a JSON object, not {shown_json(data)}; the copy may be damaged, copy it again")
    if data.dups:
        raise BoardsProblem("04", f"{rel}canvas.json repeats the member {jstr(data.dups[0])}; the copy may be damaged, copy it again")
    if "boards" not in data:
        raise BoardsProblem("04", f"{rel}canvas.json has no boards member; the copy may be damaged or from another tool, copy it again")
    boards = data["boards"]
    if not isinstance(boards, JsonObject):
        raise BoardsProblem("04", f"{rel}canvas.json boards must be an object whose keys are the board file names, not {shown_json(boards)}; copy it again")
    if boards.dups:
        raise BoardsProblem("04", f"{rel}canvas.json boards repeats the key {jstr(boards.dups[0])} (a duplicate key); the copy may be damaged, copy it again")
    if not boards:
        raise BoardsProblem("03", f"{rel}canvas.json names no board; {copy_hint}")
    if "v" not in data:
        raise BoardsProblem("05", f"{rel}canvas.json has no v member; supported: v {SUPPORTED_V}. Claude Design's format is not documented, so the skill does not guess what another version means; a spec change must be approved for it")
    v = data["v"]
    if isinstance(v, bool) or not isinstance(v, int) or v != SUPPORTED_V:
        raise BoardsProblem("05", f"{rel}canvas.json v is {shown_json(v)}; supported: v {SUPPORTED_V} only. Claude Design's format is not documented, so the skill does not guess what another version means; a spec change must be approved for it")
    if len(boards) > MAX_BOARDS:
        raise BoardsProblem("12", f"{rel}canvas.json names {len(boards)} boards; the DSN's board IDs (BRD-NN) hold {MAX_BOARDS}; split the canvas or ask the owner to change the spec")
    positions = [(number_text(b.get("x")), number_text(b.get("y"))) if isinstance(b, dict) else ("-", "-")
                 for b in boards.values()]
    return Canvas(list(boards.keys()), v, hashlib.sha256(raw).hexdigest(), positions)


def board_issue(root, dsn, name, sub="boards"):
    """(reason, Scan) for a board canvas.json names: the reason is None when the board file is usable."""
    why = name_problem(name)
    if why:
        return why, None
    try:
        return None, scan_file(boards_path(root, dsn, name, sub=sub))
    except Unreadable as u:
        return u.reason, None


# --- Output -------------------------------------------------------------------------------------------------------
def cmd_next(root):
    design = os.path.join(root, "docs", "specs", "design")
    numbers, folders = set(), set()
    try:
        with os.scandir(design) as it:
            for entry in it:
                m = re.fullmatch(r"DSN-(\d{3})\.md", entry.name)
                if m:
                    numbers.add(int(m.group(1)))
                    continue
                m = re.fullmatch(r"DSN-(\d{3})", entry.name)
                if m:
                    try:
                        if entry.is_dir(follow_symlinks=False):
                            folders.add(int(m.group(1)))
                    except OSError:
                        pass
    except (FileNotFoundError, NotADirectoryError):
        pass
    except OSError as e:
        raise CannotRun(f"docs/specs/design/ cannot be listed ({_os_reason(e)})")
    nxt = max(numbers) + 1 if numbers else 1
    if nxt > MAX_DSN_NUMBER:
        raise CannotRun(f"the DSN numbers are used up (DSN-{MAX_DSN_NUMBER} exists)")
    pending = sorted(folders - numbers - {nxt})      # the folder at the next number is where the boards belong: not pending
    listed = ", ".join(f"DSN-{n:03d}" for n in pending) if pending else "none"
    return [f"next: DSN-{nxt:03d}", f"pending boards folders: {listed}"], 0


def cmd_boards(root, dsn):
    try:
        canvas = read_canvas(root, dsn)
    except BoardsProblem as p:
        return [f"ERR-{p.code}: {p.message}", "boards: 1 problem(s)"], 1
    lines, problems = [], []
    for k, name in enumerate(canvas.names, 1):
        why, scan = board_issue(root, dsn, name)
        if why:
            problems.append(f"ERR-06: board {k} {jstr(name)}: {why}")
        else:
            x, y = canvas.positions[k - 1]
            lines.append(f"board {k} {name} {scan.size} {scan.lines} {scan.sha} {x} {y}")
    if problems:
        return problems + [f"boards: {len(problems)} problem(s)"], 1
    return ([f"canvas.json: v{canvas.v}, {len(canvas.names)} boards", f"canvas.json sha256 {canvas.sha}"] + lines
            + ["boards: ok"]), 0


def cmd_head(root, dsn, name):
    try:
        canvas = read_canvas(root, dsn)
    except BoardsProblem as p:
        return [f"ERR-{p.code}: {p.message}"], 1
    why = name_problem(name)
    if not why and name not in canvas.names:
        why = f"the file is not named by canvas.json, so it is not a board"
    if not why:
        try:
            scan = scan_file(boards_path(root, dsn, name), keep=HEAD_MAX_LINES)
        except Unreadable as u:
            why = u.reason
    if why:
        return [f"ERR-06: board {jstr(name)}: {why}"], 1
    budget = OUTPUT_MAX_BYTES - TRAILER_RESERVE
    out, used, shown, bytes_shown, cut = [], 0, 0, 0, 0
    for prefix, length, ended in scan.head:
        text = prefix.decode("utf-8", "surrogateescape")
        if length == len(prefix) and text.endswith("\r"):
            text = text[:-1]                              # a CRLF line prints without its carriage return
        if len(text) > LINE_MAX_CHARS:
            text = text[:LINE_MAX_CHARS]
            counted = len(text.encode("utf-8", "surrogateescape"))
            marker = " [cut]"
            cut_line = True
        else:
            counted = length + (1 if ended else 0)
            marker = ""
            cut_line = False
        printable = text.encode("utf-8", "surrogateescape").decode("utf-8", "replace") + marker
        size = len(printable.encode("utf-8")) + 1
        if used + size > budget:
            break
        out.append(printable)
        used += size
        shown += 1
        bytes_shown += counted
        cut += cut_line
    out.append(f"head: {shown} of {scan.lines} lines, {bytes_shown} of {scan.size} bytes, {cut} lines cut")
    return out, 0


RE_DIGEST_LINE = re.compile(r"([0-9a-f]{64})  (.+)")      # IF-05: 64 lowercase hex digits, two spaces, the name to the line's end


def read_digest_file(path):
    """{file name: digest} from the file PATH of `place`. Anything that is not a plain run of '<hex>  <file>' lines is
    CannotRun: the skill writes this file, so a line that does not parse is a bug to stop on, not data to guess at."""
    try:
        raw = read_file(path)
    except Unreadable as u:
        raise CannotRun(f"the digest file {path!r} cannot be used: {u.reason}")
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        raise CannotRun(f"the digest file {path!r} is not UTF-8 text")
    lines = text.split("\n")
    if lines and lines[-1] == "":
        lines.pop()                                  # the newline that ends the last line
    digests = {}
    for n, line in enumerate(lines, 1):
        m = RE_DIGEST_LINE.fullmatch(line[:-1] if line.endswith("\r") else line)
        if not m:
            raise CannotRun(f"line {n} of the digest file is not '<sha256>  <file>' (64 lowercase hex digits, two spaces, the file name)")
        if m.group(2) in digests:
            raise CannotRun(f"line {n} of the digest file repeats the file {jstr(m.group(2))}")
        digests[m.group(2)] = m.group(1)
    return digests


def names_list(names):
    """File names for one line of output: plain names as they are, a name with a control character or a separator quoted."""
    return ", ".join(jstr(n) if RE_UNSAFE_NAME.search(n) else n for n in names) if names else "none"


def cmd_place(root, dsn, sha_path):
    digests = read_digest_file(sha_path)
    try:
        canvas = read_canvas(root, dsn, "project")
    except BoardsProblem as p:
        return [f"ERR-{p.code}: {p.message}", "place: 1 problem(s)"], 1
    boards_dir = boards_path(root, dsn)
    try:
        st = os.lstat(boards_dir)
    except (FileNotFoundError, NotADirectoryError):
        st = None
    except OSError as e:
        return [f"ERR-03: the boards folder {boards_rel(dsn)} cannot be read ({_os_reason(e)})", "place: 1 problem(s)"], 1
    if st is not None and (stat.S_ISLNK(st.st_mode) or not stat.S_ISDIR(st.st_mode)):
        what = "is a symbolic link, which is never followed" if stat.S_ISLNK(st.st_mode) else "is not a folder"
        return [f"ERR-03: the boards folder {boards_rel(dsn)} {what}; nothing is placed into it", "place: 1 problem(s)"], 1
    for name in digests:
        if name != "canvas.json" and name not in canvas.names:
            raise CannotRun(f"the digest file names {jstr(name)}, which canvas.json does not name")
    problems, scans = [], {}
    for k, name in enumerate(canvas.names, 1):
        why, scan = board_issue(root, dsn, name, "project")
        if why:
            problems.append(f"ERR-06: board {k} {jstr(name)}: {why}")
        else:
            scans[name] = scan.sha
    scans["canvas.json"] = canvas.sha
    for name in ["canvas.json"] + canvas.names:
        if name in digests and name in scans and scans[name] != digests[name]:
            problems.append(f"ERR-21: {name}: the staged digest {scans[name]} differs from the tool's {digests[name]}")
    if problems:
        return problems + [f"place: {len(problems)} problem(s)"], 1
    moving = ["canvas.json"] + canvas.names
    placed = []

    def stopped(what, why):
        return [f"ERR-21: {what}: {why}", f"files placed: {names_list(placed)}",
                f"files not placed: {names_list([n for n in moving if n not in placed])}", "place: 1 problem(s)"], 1

    if st is None:
        try:
            os.mkdir(boards_dir)
        except OSError as e:
            return stopped("boards/", f"the folder could not be made ({_os_reason(e)})")
    project_dir = boards_path(root, dsn, sub="project")
    for name in moving:
        try:
            os.replace(os.path.join(project_dir, name), os.path.join(boards_dir, name))
        except OSError as e:
            return stopped(name, f"the move failed ({_os_reason(e)})")
        placed.append(name)
    left = sorted(os.listdir(project_dir))
    if not left:
        try:
            os.rmdir(project_dir)
        except OSError:
            pass                                    # an empty staging folder that stays is harmless
    out = [f"placed: canvas.json and {len(canvas.names)} boards into {boards_rel(dsn)}"]
    if left:
        out.append(f"left in project/: {names_list(left)}")
    return out + ["place: ok"], 0


# --- The fixed-shape YAML reader -----------------------------------------------------------------------------------
class Syntax(Exception):
    """A value the fixed-shape reader does not take: the message says what to write instead."""


class Date(str):
    """An unquoted YYYY-MM-DD (YAML reads it as a date, so a string field must not hold one)."""


class Number(str):
    """An unquoted number that is not a plain integer (YAML reads it as a float, hex, octal or time)."""


class Empty:
    """A `key:` with nothing after it."""

    def __repr__(self):
        return "<empty>"


EMPTY = Empty()
BAD = object()       # a value that did not parse; its error is already reported

ESCAPES = {"0": "\0", "a": "\a", "b": "\b", "t": "\t", "\t": "\t", "n": "\n", "v": "\v", "f": "\f", "r": "\r",
           "e": "\x1b", " ": " ", '"': '"', "/": "/", "\\": "\\", "N": "\x85", "_": "\xa0", "L": " ",
           "P": " "}
HEX_ESCAPES = {"x": 2, "u": 4, "U": 8}
BOOLS = {"true": True, "yes": True, "on": True, "false": False, "no": False, "off": False}
RE_INT = re.compile(r"[-+]?(0|[1-9][0-9]*)")
RE_NUMBERISH = re.compile(r"[-+]?(\.[0-9]+|[0-9][0-9_]*(\.[0-9_]*)?)([eE][-+]?[0-9]+)?|[-+]?\.(inf|Inf|INF)|\.(nan|NaN|NAN)|"
                          r"0[xob][0-9a-fA-F_]+|[0-9]+(:[0-9]+)+(\.[0-9_]*)?|[0-9]{4}-[0-9]{1,2}-[0-9]{1,2}([Tt ].*)?")


def strip_comment(raw):
    """Remove a trailing ` # comment`, leaving every # inside a double-quoted value alone."""
    in_quote = False
    i = 0
    while i < len(raw):
        c = raw[i]
        if in_quote:
            if c == "\\":
                i += 2
                continue
            if c == '"':
                in_quote = False
        elif c == '"' and (i == 0 or raw[i - 1] in " \t[{,"):
            in_quote = True
        elif c == "#" and (i == 0 or raw[i - 1] in " \t"):
            return raw[:i]
        i += 1
    return raw


def parse_quoted(s, i):
    """s[i] is the opening double quote: (value, index after the closing quote)."""
    out = []
    i += 1
    while i < len(s):
        c = s[i]
        if c == '"':
            return "".join(out), i + 1
        if c == "\\":
            i += 1
            if i >= len(s):
                break
            e = s[i]
            if e in ESCAPES:
                out.append(ESCAPES[e])
                i += 1
            elif e in HEX_ESCAPES:
                n = HEX_ESCAPES[e]
                digits = s[i + 1:i + 1 + n]
                if len(digits) != n or any(ch not in "0123456789abcdefABCDEF" for ch in digits):
                    raise Syntax(f"the escape \\{e} in a quoted value needs {n} hexadecimal digits")
                try:
                    out.append(chr(int(digits, 16)))
                except (ValueError, OverflowError):
                    raise Syntax(f"the escape \\{e}{digits} is not a character")
                i += 1 + n
            else:
                raise Syntax(f"unknown escape \\{e} in a quoted value")
            continue
        out.append(c)
        i += 1
    raise Syntax("a quoted value is not closed: add the closing double quote")


def resolve_plain(tok):
    if tok == "null":
        return None
    if tok == "~":
        raise Syntax("write null, not ~")
    low = tok.lower()
    if low in BOOLS:
        return BOOLS[low]
    if RE_INT.fullmatch(tok):
        return int(tok)
    if RE_DATE.fullmatch(tok):
        return Date(tok)
    if RE_NUMBERISH.fullmatch(tok):
        return Number(tok)
    return tok


def parse_plain(s, i, flow, key=False):
    """A plain (unquoted) token. In a flow list or mapping it ends at , ] or }; a key ends at its colon."""
    if s[i] in "&*!|>'%@`#,[]{}" or (s[i] in "-?:" and (i + 1 >= len(s) or s[i + 1] in " \t")):
        raise Syntax(f"a value cannot start with {s[i]!r}; put it in double quotes")
    j = i
    while j < len(s):
        c = s[j]
        if flow and c in ",[]{}":           # the flow indicators end a plain token inside a list or a mapping
            break
        if c == ":" and (j + 1 >= len(s) or s[j + 1] in " \t" or (flow and s[j + 1] in ",[]{}")):
            if key:
                break
            raise Syntax("a value that holds ': ' must be in double quotes")
        j += 1
    tok = s[i:j].rstrip()
    if key:
        return tok, j
    return resolve_plain(tok), j


def _skip(s, i):
    while i < len(s) and s[i] in " \t":
        i += 1
    return i


def parse_value(s, i, flow, depth=0):
    i = _skip(s, i)
    if i >= len(s):
        raise Syntax("a value is missing")
    c = s[i]
    if c == '"':
        return parse_quoted(s, i)
    if c in "[{" and depth >= MAX_NESTING:
        raise Syntax(f"lists and mappings nested more than {MAX_NESTING} levels deep are not taken")
    if c == "[":
        return parse_flow_list(s, i, depth + 1)
    if c == "{":
        return parse_flow_map(s, i, depth + 1)
    return parse_plain(s, i, flow)


def parse_flow_list(s, i, depth):
    items = []
    i = _skip(s, i + 1)
    if i < len(s) and s[i] == "]":
        return items, i + 1
    while True:
        v, i = parse_value(s, i, True, depth)
        items.append(v)
        i = _skip(s, i)
        if i >= len(s):
            raise Syntax("a list is not closed: add the closing ]")
        if s[i] == ",":
            i = _skip(s, i + 1)
            if i < len(s) and s[i] == "]":
                raise Syntax("a list ends with a comma: remove it")
            continue
        if s[i] == "]":
            return items, i + 1
        raise Syntax(f"unexpected {s[i]!r} in a list: put a value that holds {s[i]!r} in double quotes")


def parse_flow_map(s, i, depth):
    out = {}
    i = _skip(s, i + 1)
    if i < len(s) and s[i] == "}":
        return out, i + 1
    while True:
        i = _skip(s, i)
        if i >= len(s):
            raise Syntax("a mapping is not closed: add the closing }")
        key, i = parse_plain(s, i, True, key=True)
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_-]*", key) or i >= len(s) or s[i] != ":":
            raise Syntax("a mapping entry must look like key: value")
        if key in out:
            raise Syntax(f"the key {key} appears twice in one mapping")
        v, i = parse_value(s, i + 1, True, depth)
        out[key] = v
        i = _skip(s, i)
        if i >= len(s):
            raise Syntax("a mapping is not closed: add the closing }")
        if s[i] == ",":
            i += 1
            continue
        if s[i] == "}":
            return out, i + 1
        raise Syntax(f"unexpected {s[i]!r} in a mapping: put a value that holds {s[i]!r} in double quotes")


def parse_node(raw):
    """The value of one `key: value` line (comment included): EMPTY, or the parsed value; Syntax when unreadable."""
    text = strip_comment(raw).strip(" ")        # YAML spaces only: a no-break space is part of a plain value
    if not text:
        return EMPTY
    v, i = parse_value(text, 0, False)
    i = _skip(text, i)
    if i != len(text):
        raise Syntax(f"unexpected text {text[i:i + 24]!r} after the value")
    return v


RE_QUOTED = re.compile(r'"(?:[^"\\]|\\.)*"')
# What YAML counts as printable (and PyYAML therefore accepts): tab, line feed, carriage return and the characters
# from space up, minus the C1 controls, surrogates and the two non-characters at the top of the basic plane.
RE_UNPRINTABLE = re.compile("[^\t\n\r\x20-\x7e\x85\xa0-\ud7ff\ue000-\ufffd\U00010000-\U0010ffff]")


# YAML ends a line at these as well as at a line feed; this reader splits at the line feed only, so a line holding one
# would hide a second line from it and not from PyYAML.
RE_LINE_BREAK = re.compile("[\r\x85\u2028\u2029]")


def yaml_line_problem(line):
    """Why a line of YAML is refused whatever it says: a control character or an extra line break, or a tab used as
    indentation or spacing."""
    m = RE_LINE_BREAK.search(line)
    if m:
        return f"the line holds the line break character U+{ord(m.group(0)):04X}, which would hide a line from this reader"
    m = RE_UNPRINTABLE.search(line)
    if m:
        return f"the line holds the control character U+{ord(m.group(0)):04X}, which YAML does not take"
    if "\t" in line or not line.isascii():
        bare = RE_QUOTED.sub("", line)
        if "\t" in bare:
            return "the line holds a tab outside a quoted value: indent and space with spaces"
        odd = next((c for c in bare if c.isspace() and c != " "), None)
        if odd:
            return f"the line holds the space character U+{ord(odd):04X} outside a quoted value: use plain spaces"
    return None


def python_yaml():
    try:
        import yaml  # type: ignore
        return yaml
    except Exception:
        return None


def yaml_problem(text):
    """PyYAML's complaint about a block of YAML, or None; None as well when PyYAML is not installed."""
    yaml = python_yaml()
    if yaml is None:
        return None
    try:
        yaml.safe_load(text)
    except Exception as e:
        return " ".join(str(e).split())
    return None


# --- The checker ---------------------------------------------------------------------------------------------------
class Entry:
    def __init__(self, line, value, items=None):
        self.line = line
        self.value = value
        self.items = items       # [(line, value)] for a list


class Item:
    """One board item of the boards block."""

    def __init__(self, line, ident):
        self.line = line
        self.id = ident
        self.fields = {}         # name -> (line, value)
        self.bad = set()         # field names whose value did not parse

    def get(self, name):
        return self.fields[name][1] if name in self.fields and name not in self.bad else None

    def line_of(self, name):
        return self.fields[name][0] if name in self.fields else self.line

    @property
    def active(self):
        return self.get("status") == "active"

    @property
    def part(self):
        return self.id if isinstance(self.id, str) and self.id else f"item at line {self.line}"


class Row:
    """One row of the idea coverage table."""

    def __init__(self, line, idea, boards, status):
        self.line = line
        self.idea = idea
        self.boards = boards     # a set of BRD-NN, or None when the cell is not one
        self.status = status


class Brn:
    def __init__(self, ident, version, promoted):
        self.id = ident
        self.version = version
        self.promoted = promoted


def code_free(line):
    """A line with its inline code spans blanked out (a marker or a placeholder in a code span is quoted text). A span
    opens with a run of backticks and closes at the next run of the same length, as in CommonMark; a run with no such
    partner is literal text. Linear in the length of the line."""
    if "`" not in line:
        return line
    runs = [(m.start(), m.end()) for m in RE_BACKTICKS.finditer(line)]
    partner, last = [None] * len(runs), {}
    for i in range(len(runs) - 1, -1, -1):          # the next run of the same length, found right to left
        size = runs[i][1] - runs[i][0]
        partner[i] = last.get(size)
        last[size] = i
    out, pos, i = [], 0, 0
    while i < len(runs):
        j = partner[i]
        if j is None:
            i += 1
            continue
        start, end = runs[i][0], runs[j][1]
        out.append(line[pos:start])
        out.append(" " * (end - start))
        pos = end
        i = j + 1
    out.append(line[pos:])
    return "".join(out)


def markers_in(text):
    """The [NEEDS CLARIFICATION ...] markers of a text: from the opening words to the first closing bracket. Linear: once
    no closing bracket is left, no later marker can close either."""
    found, pos = [], 0
    while True:
        a = text.find(MARKER_OPEN, pos)
        if a < 0:
            break
        b = text.find("]", a)
        if b < 0:
            break
        found.append(text[a:b + 1])
        pos = b + 1
    return found


def split_cells(line):
    inner = line.strip()
    if inner.startswith("|"):
        inner = inner[1:]
    if inner.endswith("|") and not inner.endswith("\\|"):
        inner = inner[:-1]
    return [c.strip() for c in re.split(r"(?<!\\)\|", inner)]


def valid_date(text):
    try:
        datetime.date.fromisoformat(text)
        return True
    except ValueError:
        return False


class Check:
    def __init__(self, root, dsn, before):
        self.root = root
        self.dsn = dsn
        self.before = before
        self.rel = f"docs/specs/design/{dsn}.md"
        self.errors = []
        self.warnings = []
        self.facts = []
        self.lines = []
        self.fm_end = 0
        self.fm = {}
        self.syntax_fm = False
        self.items = None            # the board items, or None when the block did not parse
        self.syntax_boards = False
        self.sections = {}
        self.fences = []
        self.canvas = None
        self.canvas_problem = None
        self.brn = None
        self.prd_cache = {}
        self.adr_cache = {}
        self.scans = {}

    # --- reporting --------------------------------------------------------------------------------------------
    def err(self, line, part, message, rule):
        self.errors.append((line, RULE_ORDER[rule], " ".join(str(part).split()), " ".join(message.split()), rule))

    def warn(self, line, part, message, rule):
        self.warnings.append((line, RULE_ORDER[rule], " ".join(str(part).split()), " ".join(message.split()), rule))

    def render(self, entry, prefix=""):
        line, _, part, message, rule = entry
        return f"{prefix}{self.rel}:{line}: {part}: {message} ({rule})"

    def result(self):
        errors = sorted(set(self.errors))
        warnings = sorted(set(self.warnings))
        out = [self.render(e) for e in errors] + [self.render(w, "warning: ") for w in warnings] + self.facts
        if errors:
            out.append(f"INVALID: {len(errors)} error(s) in {self.rel}")
            return out, 1
        out.append(f"OK {self.rel}" + (" (before amend)" if self.before else ""))
        return out, 0

    # --- the run -----------------------------------------------------------------------------------------------
    def run(self):
        try:
            return self.steps()
        except FolderProblem as f:
            return f.lines + [f"INVALID: {len(f.lines)} error(s) in {self.rel}"], 1

    def steps(self):
        if not self.load():
            return self.result()
        if not self.read_frontmatter():
            return self.result()
        self.check_frontmatter()
        self.read_structure()
        self.read_boards()
        self.check_links()
        self.read_brn_and_canvas()
        self.check_items()
        self.check_membership()
        self.check_coverage()
        self.check_ideas_against_the_brn()
        self.check_facts()
        self.check_approval()
        self.check_changelog()
        self.check_placeholders()
        self.crosscheck()
        return self.result()

    def load(self):
        try:
            raw = read_file(os.path.join(self.root, "docs", "specs", "design", f"{self.dsn}.md"))
        except Unreadable as u:
            raise CannotRun(f"{self.rel}: {u.reason}")
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError as e:
            self.err(0, "document", f"the file is not UTF-8 text (byte {e.start}: {e.reason}); save it as UTF-8", "frontmatter")
            return False
        self.lines = text.replace("\r\n", "\n").split("\n")
        return True

    def read_frontmatter(self):
        lines = self.lines
        if not lines or lines[0].rstrip() != "---":
            self.err(1, "document", "the frontmatter must start with a '---' line", "frontmatter")
            return False
        end = next((i for i in range(1, len(lines)) if lines[i].rstrip() == "---"), None)
        if end is None:
            self.err(1, "document", "the frontmatter has no closing '---' line", "frontmatter")
            return False
        self.fm_end = end
        for n in range(1, end):
            problem = yaml_line_problem(lines[n])
            if problem:
                self.err(n + 1, "document", problem, "frontmatter")
                self.syntax_fm = True
        i = 1
        while i < end:
            raw = lines[i]
            ln = i + 1
            if not raw.strip() or raw.strip().startswith("#"):
                i += 1
                continue
            m = RE_KEY_LINE.match(raw)
            if not m:
                self.err(ln, "document", "cannot read this frontmatter line: write 'key: value' at the start of a line", "frontmatter")
                self.syntax_fm = True
                i += 1
                continue
            key = m.group(1)
            rest = m.group(2) or ""
            children = []
            j = i + 1
            while j < end:
                t = lines[j]
                if not t.strip() or t.strip().startswith("#"):
                    j += 1
                    continue
                if t[0] in " \t" or (t.startswith("- ") and not strip_comment(rest).strip()):
                    children.append((j + 1, t))
                    j += 1
                    continue
                break
            i = j
            if key in self.fm:
                self.err(ln, key, f"the key {key} appears twice", "frontmatter")
                continue
            self.fm[key] = self.parse_entry(key, ln, rest, children)
        return True

    def parse_entry(self, key, ln, rest, children):
        try:
            value = parse_node(rest)
        except Syntax as s:
            self.err(ln, key, str(s), "frontmatter")
            self.syntax_fm = True
            return Entry(ln, BAD)
        if value is not EMPTY:
            if children:
                self.err(children[0][0], key, f"unexpected indented lines after the value of {key}", "frontmatter")
                self.syntax_fm = True
            items = [(ln, v) for v in value] if isinstance(value, list) else None
            return Entry(ln, value, items)
        if not children:
            return Entry(ln, EMPTY)
        try:
            if all(t.lstrip().startswith("- ") or t.strip() == "-" for _, t in children):
                indents = {len(t) - len(t.lstrip()) for _, t in children}
                if len(indents) != 1:
                    raise Syntax("the items of a list must be indented alike")
                items = []
                for cl, t in children:
                    text = t.strip()[1:].strip()
                    try:
                        items.append((cl, parse_node(text)))
                    except Syntax as s:
                        self.err(cl, key, str(s), "frontmatter")
                        self.syntax_fm = True
                        items.append((cl, BAD))
                return Entry(ln, [v for _, v in items], items)
            mapping = {}
            indents = {len(t) - len(t.lstrip()) for _, t in children}
            if len(indents) != 1:
                raise Syntax("the lines under a key must be indented alike")
            for cl, t in children:
                m = RE_KEY_LINE.match(t.strip())
                if not m:
                    raise Syntax(f"cannot read the line {t.strip()!r}: write 'key: value'")
                sub = m.group(1)
                if sub in mapping:
                    raise Syntax(f"the key {sub} appears twice")
                try:
                    v = parse_node(m.group(2) or "")
                except Syntax as s:
                    self.err(cl, key, f"{sub}: {s}", "frontmatter")
                    self.syntax_fm = True
                    v = BAD
                mapping[sub] = v
            return Entry(ln, mapping)
        except Syntax as s:
            self.err(ln, key, str(s), "frontmatter")
            self.syntax_fm = True
            return Entry(ln, BAD)

    # --- frontmatter ---------------------------------------------------------------------------------------------
    def value(self, key):
        """The parsed value of a frontmatter key, None when it is absent or did not parse."""
        e = self.fm.get(key)
        if e is None or e.value is BAD:
            return None
        return None if e.value is EMPTY else e.value

    def line(self, key):
        e = self.fm.get(key)
        return e.line if e else self.fm_end + 1

    def check_frontmatter(self):
        fm = self.fm
        for key in fm:
            if key not in FM_KEYS:
                self.err(fm[key].line, key, f"unknown frontmatter key {key!r}; the keys are {', '.join(FM_KEYS)}", "frontmatter")
        for key in FM_KEYS:
            if key not in fm:
                self.err(self.fm_end + 1, key, f"missing frontmatter key {key!r}", "frontmatter")
        present = [k for k in fm if k in FM_KEYS]
        wanted = [k for k in FM_KEYS if k in fm]
        for have, want in zip(present, wanted):
            if have != want:
                self.err(fm[have].line, have, f"frontmatter key {have!r} is out of order; the order is {', '.join(FM_KEYS)}", "frontmatter")
                break
        for key, e in fm.items():
            if e.value is EMPTY and key in NULLABLE_KEYS:
                self.err(e.line, key, f"{key} has no value: write null", "frontmatter")
                e.value = BAD

        def text(key, what):
            """A non-empty string (quoted or a plain word); an error otherwise."""
            v = self.value(key)
            if key in fm and fm[key].value is not BAD and not (isinstance(v, str) and not isinstance(v, (Date, Number)) and v != ""):
                self.err(fm[key].line, key, f"{key} must be {what}", "frontmatter")
            return v if isinstance(v, str) else None

        v = self.value("id")
        if "id" in fm and fm["id"].value is not BAD and v != self.dsn:
            self.err(fm["id"].line, "id", f"id must equal the file name {self.dsn}, not {v!r}", "frontmatter")
        if "type" in fm and fm["type"].value is not BAD and self.value("type") != "design":
            self.err(fm["type"].line, "type", "type must be design", "frontmatter")
        text("title", "a non-empty string")
        if "status" in fm and fm["status"].value is not BAD and self.value("status") not in STATUSES:
            self.err(fm["status"].line, "status", f"status must be one of {', '.join(STATUSES)}", "frontmatter")
        v = self.value("version")
        if "version" in fm and fm["version"].value is not BAD and (isinstance(v, bool) or not isinstance(v, int) or v < 1):
            self.err(fm["version"].line, "version", "version must be an integer of 1 or more", "frontmatter")
        dates = {}
        for key in ("created", "updated"):
            v = self.value(key)
            if key in fm and fm[key].value is not BAD:
                if isinstance(v, Date) and valid_date(v):
                    dates[key] = datetime.date.fromisoformat(v)
                else:
                    self.err(fm[key].line, key, f"{key} must be an unquoted date written YYYY-MM-DD that exists in the calendar", "frontmatter")
        if "created" in dates and "updated" in dates and dates["updated"] < dates["created"]:
            self.err(fm["updated"].line, "updated", f"updated {self.value('updated')} is before created {self.value('created')}", "frontmatter")
        text("owner", "a non-empty string")
        self.string_list("authors", fm)
        gb = fm.get("generated_by")
        if gb is not None and gb.value is not BAD:
            if not isinstance(gb.value, dict):
                self.err(gb.line, "generated_by", "generated_by must be a mapping of tool, model and session", "frontmatter")
            else:
                for sub, sv in gb.value.items():
                    if sub not in GENERATED_BY_KEYS:
                        self.err(gb.line, "generated_by", f"generated_by.{sub} is not allowed: it holds only tool, model and session", "frontmatter")
                    elif sv is not BAD and not (isinstance(sv, str) and not isinstance(sv, (Date, Number))):
                        self.err(gb.line, "generated_by", f"generated_by.{sub} must be a string", "frontmatter")
        self.string_list("reviewed_by", fm)
        up = fm.get("upstream")
        if up is not None and up.value is not BAD and not isinstance(up.value, list):
            self.err(up.line, "upstream", "upstream must be a list of link records (write [] for none)", "frontmatter")
        self.string_list("supersedes", fm, RE_DSN, "a DSN-NNN ID")
        v = self.value("superseded_by")
        if "superseded_by" in fm and fm["superseded_by"].value is not BAD and v is not None and not (isinstance(v, str) and RE_DSN.fullmatch(v)):
            self.err(fm["superseded_by"].line, "superseded_by", "superseded_by must be a DSN-NNN ID or null", "frontmatter")
        self.string_list("blocked_by", fm, RE_BLOCKED, "a document or item ID such as PRD-001 or PRD-001#FR-002")
        v = self.value("canvas")
        if "canvas" in fm and fm["canvas"].value is not BAD and v is not None and not (isinstance(v, str) and re.fullmatch(r"https://\S+", v)):
            self.err(fm["canvas"].line, "canvas", "canvas must be an https:// URL (no spaces) or null", "frontmatter")
        v = self.value("canvas_version")
        if "canvas_version" in fm and fm["canvas_version"].value is not BAD and v is not None and not (
                isinstance(v, str) and not isinstance(v, (Date, Number)) and v != ""):
            self.err(fm["canvas_version"].line, "canvas_version", "canvas_version must be a non-empty quoted string or null", "frontmatter")
        v = self.value("canvas_format")
        if "canvas_format" in fm and fm["canvas_format"].value is not BAD and (isinstance(v, bool) or not isinstance(v, int) or v < 1):
            self.err(fm["canvas_format"].line, "canvas_format", "canvas_format must be an integer of 1 or more (the v of canvas.json)", "frontmatter")
        expected = boards_rel(self.dsn)
        v = self.value("boards_root")
        if "boards_root" in fm and fm["boards_root"].value is not BAD and v != expected:
            self.err(fm["boards_root"].line, "boards_root", f"boards_root must equal {expected}", "frontmatter")
        self.check_considered()

    def string_list(self, key, fm, pattern=None, what="a string"):
        e = fm.get(key)
        if e is None or e.value is BAD:
            return
        if not isinstance(e.value, list):
            self.err(e.line, key, f"{key} must be a list (write [] for none)", "frontmatter")
            return
        for ln, v in e.items:
            if v is BAD:
                continue
            ok = isinstance(v, str) and not isinstance(v, (Date, Number)) and v != ""
            if ok and pattern is not None:
                ok = pattern.fullmatch(v) is not None
            if not ok:
                self.err(ln, key, f"{key} entries must be {what}, not {v!r}", "frontmatter")

    def check_considered(self):
        e = self.fm.get("considered")
        if e is None or e.value is BAD:
            return
        if not isinstance(e.value, list):
            self.err(e.line, "considered", "considered must be a list (write [] for none)", "frontmatter")
            return
        seen = set()
        self.declined = []
        for ln, v in e.items:
            if v is BAD:
                continue
            if not (isinstance(v, str) and RE_CONSIDERED.fullmatch(v)):
                self.err(ln, "considered", f"considered entry {v!r} does not have the form PRD-NNN@N, ADR-NNN@N, "
                         "declined:PRD-NNN#FR-NNN, declined:PRD-NNN#NFR-NNN or declined:ADR-NNN", "frontmatter")
                continue
            if v in seen:
                self.err(ln, "considered", f"considered entry {v!r} appears twice", "frontmatter")
            seen.add(v)
            if v.startswith("declined:"):
                self.declined.append((ln, v[len("declined:"):]))

    declined = ()

    def crosscheck(self):
        """PyYAML's syntax cross-check of the frontmatter and the boards block, run last and only on a region where the
        fixed-shape reader and the rules reported nothing, so that the output does not depend on PyYAML being installed
        (a value both would reject, such as the date 2026-13-45, is reported once, by this script)."""
        regions = [(1, self.fm_end + 1, "the frontmatter", "frontmatter", self.lines[1:self.fm_end])]
        if self.block_span:
            first, end = self.block_span
            regions.append((first + 1, end + 1, "the boards block", "boards", self.lines[first + 1:end]))
        for low, high, what, rule, chunk in regions:
            if any(low <= e[0] <= high for e in self.errors):
                continue
            problem = yaml_problem("\n".join(chunk))
            if problem:
                self.err(low, "document", f"{what} does not parse as YAML: {problem}", rule)

    # --- the body: headings and fenced blocks ---------------------------------------------------------------------
    def read_structure(self):
        lines = self.lines
        fence = None
        headings = []
        for idx in range(self.fm_end + 1, len(lines)):
            line = lines[idx]
            if fence is None:
                m = RE_FENCE_OPEN.match(line)
                if m:
                    fence = (m.group(1).strip(), idx)
                elif line.startswith("## "):
                    headings.append((line.rstrip(), idx))
            elif line.startswith("```"):
                self.fences.append((fence[0], fence[1], idx))
                fence = None
        if fence is not None:
            self.fences.append((fence[0], fence[1], None))
        for name, heading in SECTION_HEADINGS.items():
            for pos, (text, idx) in enumerate(headings):
                if text == heading:
                    end = headings[pos + 1][1] if pos + 1 < len(headings) else len(lines)
                    self.sections[name] = (idx, end)
                    break

    def section_lines(self, name):
        """[(line number, text)] of a section's body, fenced blocks included."""
        if name not in self.sections:
            return []
        start, end = self.sections[name]
        return [(i + 1, self.lines[i]) for i in range(start + 1, end)]

    # --- the boards block -----------------------------------------------------------------------------------------
    def read_boards(self):
        lines = self.lines
        blocks = []
        for info, start, end in self.fences:
            if info != "yaml items":
                continue
            if end is None:
                self.err(start + 1, "boards", "the yaml items fence is never closed: end it with a line holding only ```", "boards")
                self.syntax_boards = True
                continue
            first = next((lines[i] for i in range(start + 1, end) if lines[i].strip() and not lines[i].strip().startswith("#")), None)
            if first is not None and re.match(r"^boards:(\s+#.*|\s*)$", first):
                blocks.append((start, end))
        if "boards" not in self.sections:
            self.err(self.fm_end + 1, "boards", f"missing the section {SECTION_HEADINGS['boards']!r}", "boards")
        if not blocks:
            if not self.syntax_boards:
                self.err(self.sections["boards"][0] + 1 if "boards" in self.sections else 1, "boards",
                         "no ```yaml items block with the boards: collection was found", "boards")
            return
        if len(blocks) > 1:
            self.err(blocks[1][0] + 1, "boards", "more than one yaml items block holds boards:; keep one", "boards")
        start, end = blocks[0]
        self.block_start = start
        self.block_span = (start, end)
        items = self.parse_items(start + 1, end)
        if self.syntax_boards:
            return
        self.items = items

    block_start = 0
    block_span = None

    def parse_items(self, first, end):
        lines = self.lines
        items = []
        cur = None
        top = False
        for i in range(first, end):
            raw = lines[i]
            ln = i + 1
            bad_line = yaml_line_problem(raw)
            if bad_line:
                self.err(ln, cur.part if cur is not None else "boards", bad_line, "boards")
                self.syntax_boards = True
                continue
            if not raw.strip() or raw.strip().startswith("#"):
                continue
            if not top:
                top = True
                continue                       # the boards: line, already recognised
            m = re.match(r"^(\s+)- id:(?:[ \t]+(.*))?$", raw)
            if m:
                cur = Item(ln, None)
                cur.indent = len(m.group(1)) + 2
                try:
                    v = parse_node(m.group(2) or "")
                    cur.id = BAD if v is EMPTY else v
                    if v is EMPTY:
                        raise Syntax("an item's id has no value")
                except Syntax as s:
                    self.err(ln, "boards", f"id: {s}", "boards")
                    self.syntax_boards = True
                    cur.id = BAD
                items.append(cur)
                continue
            m = re.match(r"^(\s+)([A-Za-z_][A-Za-z0-9_-]*):(?:[ \t]+(.*))?$", raw)
            if cur is not None and m and len(m.group(1)) == cur.indent:
                name = m.group(2)
                if name in cur.fields:
                    self.err(ln, cur.part, f"field {name!r} appears twice", "boards")
                    self.syntax_boards = True
                    continue
                try:
                    v = parse_node(m.group(3) or "")
                    if v is EMPTY:
                        raise Syntax("no value on its line: write it on one line (a list as [\"IDEA-01\"] or [], text in double quotes, null for none)")
                    cur.fields[name] = (ln, v)
                except Syntax as s:
                    self.err(ln, cur.part, f"{name}: {s}", "boards")
                    self.syntax_boards = True
                    cur.fields[name] = (ln, BAD)
                    cur.bad.add(name)
                continue
            self.err(ln, cur.part if cur is not None else "boards",
                     "this line is not in the shape of the boards block: each item starts with '- id:' and holds one 'field: value' per line, lists on one line", "boards")
            self.syntax_boards = True
        if not items and not self.syntax_boards:
            self.err(first, "boards", "the boards block holds no item", "boards")
            self.syntax_boards = True
        return items

    # --- the BRN and the boards folder ------------------------------------------------------------------------------
    def read_brn_and_canvas(self):
        if self.brn_id:
            self.brn = self.read_brn(self.brn_id)
        try:
            self.canvas = read_canvas(self.root, self.dsn)
        except BoardsProblem as p:
            if self.before:      # the skill ran `boards` first, so this is rare; the folder's own ERR line says what to fix
                raise FolderProblem([f"ERR-{p.code}: {p.message}"])
            self.canvas_problem = p
            self.err(self.line("boards_root"), "boards",
                     f"the boards folder cannot be used (ERR-{p.code}: {p.message.rstrip('.')}); run dsn_check.py boards {self.dsn}", "boards")

        if self.before:
            failing = []
            for k, name in enumerate(self.canvas.names, 1):
                why, _ = self.digest_of(name)
                if why:
                    failing.append(f"ERR-06: board {k} {jstr(name)}: {why}")
            if failing:
                raise FolderProblem(failing)

    brn_id = None

    def read_brn(self, ident):
        rel = f"docs/specs/brainstorm/{ident}.md"
        try:
            raw = read_file(os.path.join(self.root, "docs", "specs", "brainstorm", f"{ident}.md"))
        except Unreadable as u:
            raise CannotRun(f"{rel} cannot be read ({u.reason})")
        try:
            lines = raw.decode("utf-8").replace("\r\n", "\n").split("\n")
        except UnicodeDecodeError:
            raise CannotRun(f"{rel} cannot be read (it is not UTF-8 text)")
        if not lines or lines[0].rstrip() != "---":
            raise CannotRun(f"{rel} cannot be read (it has no frontmatter)")
        end = next((i for i in range(1, len(lines)) if lines[i].rstrip() == "---"), None)
        if end is None:
            raise CannotRun(f"{rel} cannot be read (its frontmatter is not closed)")
        version = None
        for i in range(1, end):
            m = re.match(r"^version:[ \t]+(.*)$", lines[i])
            if m:
                try:
                    v = parse_node(m.group(1))
                except Syntax:
                    v = None
                version = v if isinstance(v, int) and not isinstance(v, bool) and v >= 1 else None
                break
        if version is None:
            raise CannotRun(f"{rel} cannot be read (its frontmatter has no integer version)")
        block = None
        fence = None
        for i in range(end + 1, len(lines)):
            if fence is None:
                if lines[i].rstrip() == "```yaml items":
                    fence = i
            elif lines[i].startswith("```"):
                first = next((lines[j] for j in range(fence + 1, i) if lines[j].strip() and not lines[j].strip().startswith("#")), "")
                if re.match(r"^ideas:(\s+#.*|\s*)$", first):
                    block = (fence + 1, i)
                    break
                fence = None
        if block is None:
            raise CannotRun(f"{rel} cannot be read (it has no ideas item block)")
        ideas = []
        for i in range(block[0], block[1]):
            m = re.match(r"^(\s+)- id:[ \t]+(.*)$", lines[i])
            if m:
                ideas.append({"indent": len(m.group(1)) + 2, "line": i + 1, "id": strip_comment(m.group(2)).strip().strip('"')})
                continue
            m = re.match(r"^(\s+)(status|disposition):[ \t]+(.*)$", lines[i])
            if m and ideas and len(m.group(1)) == ideas[-1]["indent"]:
                try:
                    v = parse_node(m.group(3))
                except Syntax:
                    v = None
                ideas[-1].setdefault(m.group(2), v)
        if not ideas:
            raise CannotRun(f"{rel} cannot be read (its ideas block holds no idea)")
        promoted = set()
        for idea in ideas:
            if not RE_IDEA.fullmatch(idea["id"]) or not isinstance(idea.get("status"), str) or not isinstance(idea.get("disposition"), str):
                raise CannotRun(f"{rel} cannot be read (the idea item at line {idea['line']} lacks an id, a status or a disposition)")
            if idea["status"] == "active" and idea["disposition"] == "promoted":
                promoted.add(idea["id"])
        return Brn(ident, version, promoted)

    # --- the items -------------------------------------------------------------------------------------------------
    def check_items(self):
        items = self.items
        if items is None:
            return
        seen = {}
        for it in items:
            ident = it.id
            if ident is BAD:
                continue
            if not (isinstance(ident, str) and RE_BRD.fullmatch(ident)):
                self.err(it.line, it.part, f"item id {ident!r} must be BRD-NN: BRD, a hyphen and two digits", "boards")
            elif ident in seen:
                self.err(it.line, ident, f"duplicate id {ident}", "boards")
            seen[ident] = True
            for name in it.fields:
                if name not in BOARD_REQUIRED and name not in BOARD_OPTIONAL:
                    hint = "; the links to the BRN belong in the document's upstream" if name == "upstream" else ""
                    self.err(it.line_of(name), it.part, f"field {name!r} is not allowed in a board item (the fields are id, {', '.join(BOARD_REQUIRED + BOARD_OPTIONAL)}){hint}", "boards")
            for name in BOARD_REQUIRED:
                if name not in it.fields:
                    self.err(it.line, it.part, f"missing field {name!r}", "boards")
            self.check_item_shapes(it)
        self.check_declined()
        self.check_active_files()

    def check_item_shapes(self, it):
        part = it.part
        v = it.get("status")
        if "status" in it.fields and "status" not in it.bad and v not in ("active", "deprecated"):
            self.err(it.line_of("status"), part, "status must be active or deprecated", "boards")
        v = it.get("file")
        if "file" in it.fields and "file" not in it.bad:
            if not isinstance(v, str) or isinstance(v, (Date, Number)):
                self.err(it.line_of("file"), part, "file must be the board's file name as a string", "boards")
            elif name_problem(v):
                self.err(it.line_of("file"), part, f"file {v!r}: {name_problem(v)}", "boards")
        v = it.get("title")
        if "title" in it.fields and "title" not in it.bad and not (isinstance(v, str) and not isinstance(v, (Date, Number)) and v != ""):
            self.err(it.line_of("title"), part, "title must be a non-empty string", "boards")
        v = it.get("sha256")
        if "sha256" in it.fields and "sha256" not in it.bad and not (isinstance(v, str) and RE_SHA.fullmatch(v)):
            self.err(it.line_of("sha256"), part, "sha256 must be 64 lowercase hexadecimal digits, in double quotes", "boards")
        v = it.get("superseded_by")
        if "superseded_by" in it.fields and "superseded_by" not in it.bad and v is not None and not (isinstance(v, str) and RE_BRD.fullmatch(v)):
            self.err(it.line_of("superseded_by"), part, "superseded_by must be a BRD-NN ID or null", "boards")
        v = it.get("notes")
        if "notes" in it.fields and "notes" not in it.bad and v is not None and not (isinstance(v, str) and not isinstance(v, (Date, Number))):
            self.err(it.line_of("notes"), part, "notes must be a string in double quotes, or null", "boards")
        notes = it.get("notes")
        marked = isinstance(notes, str) and "[NEEDS CLARIFICATION" in notes
        # mapping: flow, surface, ideas, answers
        if "flow" in it.fields and "flow" not in it.bad:
            v = it.get("flow")
            if v is None:
                if not marked:
                    self.err(it.line_of("flow"), part, "flow is null but notes holds no [NEEDS CLARIFICATION: ...] marker for it", "mapping")
            elif not (isinstance(v, str) and not isinstance(v, (Date, Number)) and RE_SLUG.fullmatch(v)):
                self.err(it.line_of("flow"), part, f"flow {v!r} must be a lowercase slug such as day-to-day (a-z, 0-9 and single hyphens), or null", "mapping")
        if "surface" in it.fields and "surface" not in it.bad:
            v = it.get("surface")
            if v is None:
                if not marked:
                    self.err(it.line_of("surface"), part, "surface is null but notes holds no [NEEDS CLARIFICATION: ...] marker for it", "mapping")
            elif v not in SURFACES:
                self.err(it.line_of("surface"), part, f"surface must be one of {', '.join(SURFACES)} or null, not {v!r}", "mapping")
        if "ideas" in it.fields and "ideas" not in it.bad:
            v = it.get("ideas")
            if v is None:
                if not marked:
                    self.err(it.line_of("ideas"), part, "ideas is null but notes holds no [NEEDS CLARIFICATION: ...] marker for it", "mapping")
            elif not (isinstance(v, list) and all(isinstance(x, str) and RE_IDEA.fullmatch(x) for x in v) and len(set(v)) == len(v)):
                self.err(it.line_of("ideas"), part, "ideas must be a list of IDEA-NN without repeats, [] for none, or null", "mapping")
        if "answers" in it.fields and "answers" not in it.bad:
            v = it.get("answers")
            if not isinstance(v, list):
                self.err(it.line_of("answers"), part, "answers must be a list of PRD-NNN#FR-NNN, PRD-NNN#NFR-NNN or ADR-NNN entries, [] when none", "mapping")
            else:
                for x in v:
                    if not (isinstance(x, str) and RE_ANSWER.fullmatch(x)):
                        self.err(it.line_of("answers"), part, f"answers entry {x!r} must be PRD-NNN#FR-NNN, PRD-NNN#NFR-NNN or ADR-NNN", "mapping")
                if len(set(map(repr, v))) != len(v):
                    self.err(it.line_of("answers"), part, "answers repeats an entry", "mapping")
                if it.active and not self.before:       # --before-amend holds shapes only: no PRD or ADR is looked up
                    for x in v:
                        if isinstance(x, str) and RE_ANSWER.fullmatch(x):
                            self.check_reference(it, x)

    def check_declined(self):
        answered = set()
        for it in self.items:
            if it.active and isinstance(it.get("answers"), list):
                answered.update(x for x in it.get("answers") if isinstance(x, str))
        for ln, ref in self.declined:
            if ref in answered:
                self.err(ln, "considered", f"considered holds declined:{ref}, but an active board's answers holds {ref}: a reference is either answered or declined", "frontmatter")

    def check_active_files(self):
        by_file = {}
        for it in self.items:
            f = it.get("file")
            if it.active and isinstance(f, str):
                by_file.setdefault(f, []).append(it)
        for f, group in by_file.items():
            if len(group) > 1:
                ids = ", ".join(str(g.id) for g in group)
                self.err(group[1].line_of("file"), group[1].part, f"file {f!r} has two active items ({ids}); only one item per file may be active, deprecate the other", "boards")

    # --- answers: the PRD items and the ADRs they name ----------------------------------------------------------------
    def check_reference(self, it, ref):
        line = it.line_of("answers")
        if ref.startswith("ADR-"):
            info = self.adr(ref)
            if info is None:
                self.err(line, it.part, f"answers entry {ref!r}: docs/specs/adr/{ref}.md does not exist", "mapping")
                return
            status, superseded = info
            why = []
            if status != "accepted":
                why.append(f"its status is {status or 'unknown'}, not accepted")
            if superseded not in (None, "null"):
                why.append(f"it is superseded by {superseded}")
            if why:
                self.warn(line, it.part, f"answers entry {ref!r} names {ref}, a suspect reference: {' and '.join(why)}", "mapping")
            return
        prd_id, item = ref.split("#")
        prd = self.prd(prd_id)
        if prd is None:
            self.err(line, it.part, f"answers entry {ref!r}: docs/specs/prd/{prd_id}.md does not exist", "mapping")
            return
        if item not in prd:
            self.err(line, it.part, f"answers entry {ref!r} names no requirement {item} in docs/specs/prd/{prd_id}.md", "mapping")
        elif prd[item] == "deprecated":
            self.warn(line, it.part, f"answers entry {ref!r} names {item}, a suspect reference: it is deprecated in docs/specs/prd/{prd_id}.md", "mapping")

    def prd(self, prd_id):
        """{item id: status} of the FR and NFR items of a PRD, or None when the PRD cannot be read."""
        if prd_id not in self.prd_cache:
            try:
                text = read_file(os.path.join(self.root, "docs", "specs", "prd", f"{prd_id}.md")).decode("utf-8", "replace")
            except Unreadable:
                self.prd_cache[prd_id] = None
                return None
            items, cur = {}, None
            for line in text.replace("\r\n", "\n").split("\n"):
                m = re.match(r"^\s*- id:\s*\"?(N?FR-\d{3})\"?\s*(#.*)?$", line)
                if m:
                    cur = m.group(1)
                    items[cur] = ""
                    continue
                if re.match(r"^\s*- id:", line):
                    cur = None
                    continue
                m = re.match(r"^\s+status:\s*\"?([A-Za-z_-]+)\"?", line)
                if m and cur and items[cur] == "":
                    items[cur] = m.group(1)
            self.prd_cache[prd_id] = items
        return self.prd_cache[prd_id]

    def adr(self, adr_id):
        """(status, superseded_by) from an ADR's frontmatter, or None when the ADR cannot be read."""
        if adr_id not in self.adr_cache:
            try:
                text = read_file(os.path.join(self.root, "docs", "specs", "adr", f"{adr_id}.md")).decode("utf-8", "replace")
            except Unreadable:
                self.adr_cache[adr_id] = None
                return None
            lines = text.replace("\r\n", "\n").split("\n")
            status, superseded = "", None
            if lines and lines[0].rstrip() == "---":
                for line in lines[1:]:
                    if line.rstrip() == "---":
                        break
                    m = re.match(r"^status:\s*\"?([A-Za-z_-]+)\"?", line)
                    if m:
                        status = m.group(1)
                    m = re.match(r"^superseded_by:\s*\"?([A-Za-z0-9_-]+)\"?", line)
                    if m:
                        superseded = m.group(1)
            self.adr_cache[adr_id] = (status, superseded)
        return self.adr_cache[adr_id]

    # --- links ----------------------------------------------------------------------------------------------------
    def named_ideas(self):
        """The distinct ideas an active board names, as a sorted list."""
        out = set()
        for it in self.items or []:
            if it.active and isinstance(it.get("ideas"), list):
                out.update(x for x in it.get("ideas") if isinstance(x, str))
        return sorted(out)

    def check_links(self):
        up = self.fm.get("upstream")
        if up is None or up.value is BAD or not isinstance(up.value, list):
            return
        links = []
        for ln, l in up.items:
            if l is BAD:
                continue
            if not isinstance(l, dict):
                self.err(ln, "upstream", "upstream entries must be link records such as {id: BRN-001, relation: derives, version: 1, hash: null}", "links")
                continue
            bad = False
            extra = set(l) - LINK_KEYS
            if extra:
                self.err(ln, "upstream", f"a link holds the unknown key {sorted(extra)[0]!r}; its keys are {', '.join(sorted(LINK_KEYS))}", "links")
                bad = True
            if not (isinstance(l.get("id"), str) and RE_DOC_ID.fullmatch(l["id"])):
                self.err(ln, "upstream", f"a link's id must be a document ID such as BRN-001, not {l.get('id')!r}", "links")
                bad = True
            v = l.get("version")
            if isinstance(v, bool) or not isinstance(v, int) or v < 1:
                self.err(ln, "upstream", f"a link's version must be an integer of 1 or more, not {v!r}", "links")
                bad = True
            if "hash" in l and l["hash"] is not None:
                self.err(ln, "upstream", f"every hash must be null, not {l['hash']!r}", "links")
                bad = True
            if "item" in l and not (isinstance(l["item"], str) and RE_ITEM_ID.fullmatch(l["item"])):
                self.err(ln, "upstream", f"a link's item must be an item ID such as IDEA-01, not {l['item']!r}", "links")
                bad = True
            if not bad:
                links.append((ln, l))
        doc = [(ln, l) for ln, l in links if "item" not in l]
        good = [(ln, l) for ln, l in doc if l.get("relation") == "derives" and RE_BRN.fullmatch(l["id"])]
        for ln, l in doc:
            if (ln, l) not in good:
                self.err(ln, "upstream", f"the link to {l['id']} without an item has relation {l.get('relation')!r}; upstream holds only derives links, one to a BRN", "links")
        if not good:
            self.err(up.line, "upstream", "upstream has no derives link without an item to a BRN; write {id: BRN-NNN, relation: derives, version: N, hash: null}", "links")
            return
        if len(good) > 1:
            self.err(good[1][0], "upstream", f"upstream has more than one derives link without an item ({', '.join(l['id'] for _, l in good)}); exactly one is allowed", "links")
        doc_line, doc_link = good[0]
        brn = doc_link["id"]
        self.brn_id = brn
        self.brn_link = (doc_line, doc_link["version"])
        linked, seen = set(), set()
        for ln, l in links:
            if "item" not in l:
                continue
            if l.get("relation") != "derives" or l["id"] != brn or not RE_IDEA.fullmatch(l["item"]):
                self.err(ln, "upstream", f"a link with an item must be a derives link to {brn} for an IDEA-NN item, not {l['id']}#{l['item']} ({l.get('relation')})", "links")
                continue
            if l["item"] in seen:
                self.err(ln, "upstream", f"upstream links {l['item']} twice", "links")
            seen.add(l["item"])
            linked.add(l["item"])
        if self.items is not None:
            named = set(self.named_ideas())
            for idea in sorted(named - linked):
                self.err(up.line, "upstream", f"upstream has no derives link for {idea}, which an active board names", "links")
            for idea in sorted(linked - named):
                self.err(up.line, "upstream", f"upstream links {idea}, which no active board names", "links")
        versions = sorted({l["version"] for _, l in links})
        if len(versions) > 1:
            self.err(up.line, "upstream", f"the links cite different versions ({', '.join(map(str, versions))}); all must cite one version", "links")

    brn_link = None

    def check_brn_version(self):
        """The suspect-link comparison with the BRN's current version (a warning, or an error above it)."""
        if self.brn is None or self.brn_link is None:
            return
        ln, version = self.brn_link
        if version < self.brn.version:
            self.warn(ln, "upstream", f"{self.brn.id} is cited at version {version} but the BRN is at version {self.brn.version}: the links are suspect until the next amend", "links")
            if self.before:
                self.facts.append(f"fact: links: {self.brn.id} at version {version}, the BRN is at {self.brn.version}")
        elif version > self.brn.version:
            message = f"upstream cites {self.brn.id} at version {version}, but the BRN is at version {self.brn.version}"
            if self.before:      # the version comparison is a warning in the pre-check
                self.warn(ln, "upstream", message, "links")
            else:
                self.err(ln, "upstream", message, "links")

    # --- membership and digests (the full check) ----------------------------------------------------------------------
    def digest_of(self, name):
        if name not in self.scans:
            self.scans[name] = board_issue(self.root, self.dsn, name)
        return self.scans[name]

    def check_membership(self):
        if self.canvas is None or self.before:
            return
        v = self.value("canvas_format")
        if ("canvas_format" in self.fm and self.fm["canvas_format"].value is not BAD
                and isinstance(v, int) and not isinstance(v, bool) and v != self.canvas.v):
            self.err(self.fm["canvas_format"].line, "canvas_format", f"canvas_format is {v} but canvas.json has v {self.canvas.v}", "frontmatter")
        if self.items is None:
            return
        names = self.canvas.names
        active = [it for it in self.items if it.active and isinstance(it.get("file"), str)]
        by_file = {}
        for it in active:
            by_file.setdefault(it.get("file"), []).append(it)
        boards_line = self.block_start + 1
        for name in names:
            group = by_file.get(name, [])
            if not group:
                self.err(boards_line, "canvas.json", f"{name!r} is named by canvas.json but has no active board item", "boards")
                continue
            why, scan = self.digest_of(name)
            if why:
                self.err(boards_line, "canvas.json", f"the board file {name!r} named by canvas.json cannot be used (ERR-06): {why}", "boards")
                continue
            for it in group:
                recorded = it.get("sha256")
                if isinstance(recorded, str) and RE_SHA.fullmatch(recorded) and recorded != scan.sha:
                    self.err(it.line_of("sha256"), it.part, f"sha256 does not match the file {name!r} (the file's digest is {scan.sha[:12]}..., the item records {recorded[:12]}...): "
                             "the boards were copied again, so this DSN needs an amend run", "boards")
        for it in active:
            if it.get("file") not in names:
                self.err(it.line_of("file"), it.part, f"file {it.get('file')!r} is not named by canvas.json", "boards")

    # --- idea coverage ---------------------------------------------------------------------------------------------
    def parse_coverage(self):
        """(rows, header_line) of section 3's table; the form errors are reported here."""
        rows = []
        if "coverage" not in self.sections:
            self.err(self.fm_end + 1, "section 3", f"missing the section {SECTION_HEADINGS['coverage']!r}", "coverage")
            return None
        table = [(ln, t) for ln, t in self.section_lines("coverage") if t.lstrip().startswith("|")]
        if not table:
            self.err(self.sections["coverage"][0] + 1, "section 3", "section 3 holds no table: write | Idea | Boards | Status |", "coverage")
            return None
        header_line, header = table[0]
        if [c for c in split_cells(header)] != ["Idea", "Boards", "Status"]:
            self.err(header_line, "section 3", "the table header must be | Idea | Boards | Status |", "coverage")
        for ln, t in table[1:]:
            cells = split_cells(t)
            if all(re.fullmatch(r":?-+:?", c) for c in cells):
                continue
            if len(cells) != 3:
                self.err(ln, "section 3", "a row must have three cells: | Idea | Boards | Status |", "coverage")
                continue
            m = re.match(r"(IDEA-\d{2})\b", cells[0])
            if not m:
                self.err(ln, "section 3", f"the first cell {cells[0]!r} must start with an idea ID such as IDEA-01", "coverage")
                continue
            idea = m.group(1)
            boards = None
            if cells[1] == "none":
                boards = set()
            else:
                parts = [p.strip() for p in cells[1].split(",")]
                if all(RE_BRD.fullmatch(p) for p in parts):
                    boards = set(parts)
                else:
                    self.err(ln, "section 3", f"{idea}: the Boards cell {cells[1]!r} must be none or a list of BRD-NN items", "coverage")
            if cells[2] not in COVERAGE_STATUSES:
                self.err(ln, "section 3", f"{idea}: Status {cells[2]!r} must be one of {', '.join(COVERAGE_STATUSES)}", "coverage")
                continue
            rows.append(Row(ln, idea, boards, cells[2]))
        return rows

    def marker_text(self):
        """Section 5 without its code spans."""
        return "\n".join(code_free(t) for _, t in self.section_lines("questions"))

    def check_coverage(self):
        if "questions" not in self.sections and not self.before:
            self.err(self.fm_end + 1, "section 5", f"missing the section {SECTION_HEADINGS['questions']!r}", "coverage")
        rows = self.parse_coverage()
        self.rows = rows
        if rows is None or self.before:
            return
        seen = {}
        for r in rows:
            if r.idea in seen:
                self.err(r.line, "section 3", f"{r.idea} appears twice in the table; keep one row", "coverage")
            seen.setdefault(r.idea, r)
        if [r.idea for r in rows] != sorted(r.idea for r in rows):
            self.err(rows[0].line, "section 3", "the rows are not in idea ID order", "coverage")
        if self.brn is None or self.items is None:
            return
        promoted = self.brn.promoted
        for idea in sorted(promoted):
            if idea not in seen:
                self.err(self.sections["coverage"][0] + 1, "section 3", f"{idea} is promoted in the BRN but section 3 has no row for it", "coverage")
        markers = markers_in(self.marker_text())
        for idea, r in seen.items():
            named = {it.id for it in self.items if it.active and isinstance(it.get("ideas"), list) and idea in it.get("ideas")}
            if idea not in promoted:
                if r.status != "withdrawn":
                    self.err(r.line, "section 3", f"{idea} is not promoted in the BRN, so its row must have Status withdrawn", "coverage")
                elif r.boards is not None and r.boards != named:
                    self.err(r.line, "section 3", f"{idea}: a withdrawn row's Boards cell lists the active boards that still name it ({self.cell(named)}), not {self.cell(r.boards)}", "coverage")
                continue
            if r.status == "withdrawn":
                self.err(r.line, "section 3", f"{idea} has a withdrawn row but the BRN still promotes it; Status must say designed, not a screen or no board yet", "coverage")
                continue
            if r.status == "designed":
                if not named:
                    self.err(r.line, "section 3", f"{idea} is designed but no active board names it", "coverage")
                elif r.boards is not None and r.boards != named:
                    self.err(r.line, "section 3", f"{idea}: the Boards cell lists {self.cell(r.boards)} but the active boards that name it are {self.cell(named)}", "coverage")
                continue
            if named:
                self.err(r.line, "section 3", f"{idea} is {r.status!r} but the active boards {self.cell(named)} name it; its Status must be designed", "coverage")
                continue
            if r.boards:
                self.err(r.line, "section 3", f"{idea}: the Boards cell must be none for Status {r.status}, not {self.cell(r.boards)}", "coverage")
            if r.status == "no board yet" and not any(idea in m for m in markers):
                self.err(r.line, "section 3", f"{idea} is no board yet but section 5 holds no [NEEDS CLARIFICATION: ...] marker that names {idea}", "coverage")

    rows = None

    @staticmethod
    def cell(ids):
        return ", ".join(sorted(ids)) if ids else "none"

    # --- the facts of --before-amend ---------------------------------------------------------------------------------
    def check_facts(self):
        self.check_brn_version()
        if not self.before:
            return
        facts = []
        if self.items is not None and self.canvas is not None:
            active = [it for it in self.items if it.active and isinstance(it.get("file"), str)]
            by_file = {}
            for it in active:
                by_file.setdefault(it.get("file"), []).append(it)
            for name in self.canvas.names:
                group = by_file.get(name)
                if not group:
                    facts.append(f"fact: board {name}: new")
                    continue
                why, scan = self.digest_of(name)
                if why:
                    continue            # unreachable: read_brn_and_canvas stopped on any board that fails ERR-06
                if any(isinstance(it.get("sha256"), str) and it.get("sha256") != scan.sha for it in group):
                    facts.append(f"fact: board {name}: changed")
            for it in active:
                if it.get("file") not in self.canvas.names and not name_problem(it.get("file")):
                    facts.append(f"fact: board {it.get('file')}: removed")      # a name that is no file name is already an error
            v = self.value("canvas_format")
            if isinstance(v, int) and not isinstance(v, bool) and v != self.canvas.v:
                facts.append(f"fact: canvas_format: the DSN records {v}, canvas.json has {self.canvas.v}")
        if self.brn is not None and self.rows is not None:
            row_ideas = {r.idea for r in self.rows}
            withdrawn = {r.idea for r in self.rows if r.status == "withdrawn"}
            for idea in sorted(self.brn.promoted - row_ideas):
                facts.append(f"fact: idea {idea}: no row")
            held = {r.idea for r in self.rows if r.status != "withdrawn"} | set(self.named_ideas())
            for idea in sorted(held - self.brn.promoted - withdrawn):
                facts.append(f"fact: idea {idea}: no longer promoted")
        # the links fact was added by check_brn_version, ahead of these; keep the documented order
        links = [f for f in self.facts if f.startswith("fact: links:")]
        self.facts = facts + links

    # --- the rules that need the BRN and the boards: the ideas a board names -------------------------------------------
    def check_ideas_against_the_brn(self):
        if self.before or self.brn is None or self.items is None or self.rows is None:
            return
        withdrawn = {r.idea for r in self.rows if r.status == "withdrawn"}
        for it in self.items:
            if not it.active or not isinstance(it.get("ideas"), list):
                continue
            for idea in it.get("ideas"):
                if isinstance(idea, str) and idea not in self.brn.promoted and idea not in withdrawn:
                    self.err(it.line_of("ideas"), it.part, f"it names {idea}, which the BRN does not promote and section 3 has no withdrawn row for", "mapping")

    # --- approval ------------------------------------------------------------------------------------------------
    def check_approval(self):
        status = self.value("status")
        if "status" not in self.fm or status not in STATUSES:
            return
        by, on = self.fm.get("approved_by"), self.fm.get("approved_on")
        has_by, has_on = by is not None and by.value is not BAD, on is not None and on.value is not BAD
        by_value = None if not has_by or by.value is EMPTY else by.value
        on_value = None if not has_on or on.value is EMPTY else on.value
        if status == "approved":
            if has_by and not (isinstance(by_value, str) and not isinstance(by_value, (Date, Number)) and by_value.strip()):
                self.err(by.line, "approved_by", "status is approved but approved_by is empty or not a name: it must name the approver", "approval")
            if has_on and not (isinstance(on_value, Date) and valid_date(on_value)):
                self.err(on.line, "approved_on", "status is approved but approved_on is not a date: write the approval date as an unquoted YYYY-MM-DD", "approval")
            marked = [i + 1 for i, line in enumerate(self.lines) if "[NEEDS CLARIFICATION" in code_free(line)]
            if marked:
                more = f" (and {len(marked) - 1} more line(s))" if len(marked) > 1 else ""
                self.err(marked[0], "marker", f"status is approved but the document still holds a [NEEDS CLARIFICATION marker{more}: resolve every marker before approval", "approval")
        elif status in ("draft", "in-review"):
            if has_by and not (isinstance(by_value, str) and by_value == ""):
                self.err(by.line, "approved_by", f"status is {status} but approved_by is {by_value!r}: it must be \"\" until the document is approved", "approval")
            if has_on and on_value is not None:
                self.err(on.line, "approved_on", f"status is {status} but approved_on is {on_value!r}: it must be null until the document is approved", "approval")

    # --- change log ----------------------------------------------------------------------------------------------
    def check_changelog(self):
        if "changelog" not in self.sections:
            self.err(self.fm_end + 1, "Change Log", f"missing the section {SECTION_HEADINGS['changelog']!r}", "changelog")
            return
        head_line = self.sections["changelog"][0] + 1
        table = [(ln, t) for ln, t in self.section_lines("changelog") if t.lstrip().startswith("|")]
        if not table:
            self.err(head_line, "Change Log", "the Change Log holds no table: write | Version | Date | Author | Change | Items affected |", "changelog")
            return
        if split_cells(table[0][1]) != CHANGELOG_HEADER:
            self.err(table[0][0], "Change Log", f"the table header must be | {' | '.join(CHANGELOG_HEADER)} |", "changelog")
        version = self.value("version")
        updated = self.value("updated")
        updated_date = datetime.date.fromisoformat(updated) if isinstance(updated, Date) and valid_date(updated) else None
        versions = []
        for ln, t in table[1:]:
            cells = split_cells(t)
            if all(re.fullmatch(r":?-+:?", c) for c in cells):
                continue
            if len(cells) != len(CHANGELOG_HEADER):
                self.err(ln, "Change Log", f"a row must have five cells: | {' | '.join(CHANGELOG_HEADER)} |", "changelog")
                continue
            if not re.fullmatch(r"[1-9][0-9]*", cells[0]):
                self.err(ln, "Change Log", f"the version cell {cells[0]!r} must be an integer of 1 or more", "changelog")
                continue
            versions.append((ln, int(cells[0])))
            if not (RE_DATE.fullmatch(cells[1]) and valid_date(cells[1])):
                self.err(ln, "Change Log", f"the date cell {cells[1]!r} must be a real date written YYYY-MM-DD", "changelog")
            elif updated_date is not None and datetime.date.fromisoformat(cells[1]) > updated_date:
                self.err(ln, "Change Log", f"the row dated {cells[1]} is after updated {updated}", "changelog")
        for (_, a), (ln, b) in zip(versions, versions[1:]):
            if b < a:
                self.err(ln, "Change Log", f"the rows are not in version order (version {b} follows version {a})", "changelog")
                break
        if isinstance(version, int) and not isinstance(version, bool) and version >= 1 and version not in [v for _, v in versions]:
            self.err(head_line, "Change Log", f"no row for version {version}", "changelog")

    # --- placeholders ---------------------------------------------------------------------------------------------
    def check_placeholders(self):
        for i, line in enumerate(self.lines):
            bare = code_free(line)
            if "<!--" in bare:
                self.err(i + 1, "document", "an HTML comment is left: delete every author comment", "placeholder")
            if "[[fill:" in bare:
                self.err(i + 1, "document", "a [[fill: ...]] placeholder is left: replace it with content", "placeholder")


# --- Arguments and main ----------------------------------------------------------------------------------------------
def parse_args(argv):
    if not argv:
        raise CannotRun("no subcommand given: use next, boards, check, head or place")
    sub = argv[0]
    if sub not in ("next", "boards", "check", "head", "place"):
        raise CannotRun(f"unknown subcommand {sub!r}: use next, boards, check, head or place")
    root, before, positional, sha_file = None, False, [], None
    i = 1
    only_positional = False
    while i < len(argv):
        a = argv[i]
        if only_positional:
            positional.append(a)
        elif a == "--":
            only_positional = True
        elif a == "--root":
            if root is not None:
                raise CannotRun("--root was given twice")
            i += 1
            if i >= len(argv) or argv[i].startswith("--"):
                raise CannotRun("--root needs a folder")
            root = argv[i]
        elif a == "--before-amend":
            if sub != "check":
                raise CannotRun("--before-amend belongs to the check subcommand")
            before = True
        elif a == "--sha-file":
            if sub != "place":
                raise CannotRun("--sha-file belongs to the place subcommand")
            if sha_file is not None:
                raise CannotRun("--sha-file was given twice")
            i += 1
            if i >= len(argv) or argv[i].startswith("--"):
                raise CannotRun("--sha-file needs the path of the digest file")
            sha_file = argv[i]
        elif a.startswith("-") and a != "-":
            raise CannotRun(f"unknown option {a!r}")
        else:
            positional.append(a)
        i += 1
    root = "." if root is None else root
    if not os.path.isdir(root) or not os.access(root, os.R_OK | os.X_OK):
        raise CannotRun(f"the project root {root!r} is not a readable folder")
    wanted = {"next": 0, "boards": 1, "check": 1, "head": 2, "place": 1}[sub]
    if len(positional) != wanted or (sub == "place" and sha_file is None):
        usage = {"next": "next [--root DIR]", "boards": "boards [--root DIR] DSN-NNN",
                 "check": "check [--before-amend] [--root DIR] DSN-NNN", "head": "head [--root DIR] DSN-NNN [--] FILE",
                 "place": "place [--root DIR] DSN-NNN --sha-file PATH"}[sub]
        raise CannotRun(f"wrong arguments: use {usage}")
    if wanted and not RE_DSN.fullmatch(positional[0]):
        raise CannotRun(f"{positional[0]!r} is not a DSN ID such as DSN-001")
    return sub, root, before, positional, sha_file


def main(argv):
    try:
        sub, root, before, positional, sha_file = parse_args(argv)
        if sub == "next":
            lines, code = cmd_next(root)
        elif sub == "boards":
            lines, code = cmd_boards(root, positional[0])
        elif sub == "head":
            lines, code = cmd_head(root, positional[0], positional[1])
        elif sub == "place":
            lines, code = cmd_place(root, positional[0], sha_file)
        else:
            lines, code = Check(root, positional[0], before).run()
    except CannotRun as e:
        lines, code = [f"Cannot run: {str(e).rstrip('.')}."], 2
    except Exception as e:          # never a traceback: a bug in this script is reported as a run that cannot run
        lines, code = [f"Cannot run: internal error ({type(e).__name__}: {' '.join(str(e).split())})."], 2
    data = ("\n".join(lines) + "\n").encode("utf-8", "replace")
    sys.stdout.flush()
    sys.stdout.buffer.write(data)
    sys.stdout.buffer.flush()
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

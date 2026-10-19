#!/usr/bin/env python3
"""Pre-publish scan of the staged content for the devforgeai git skill (SPEC-007 BEH-08).

Usage:
    python3 scan_staged.py [-C PATH]

Scans what `git commit` would record: the added lines of `git diff --cached` (which, for an added
file, is its full content) plus the size, type and attributes of every staged file. Prints one JSON
object: {"blocked": [...], "warnings": [...], "files_scanned": N}. Each finding names the check,
the path and, when it applies, the line; a secret's value is never printed. Credentials in a URL to
a local or example host (or whose user equals the password), and literal credential assignments in
test and fixture paths, are warnings. Password assignments in explicitly named local/example
Compose files also warn; other credentials block (SPEC-007 BEH-08). Paths and content
need not be UTF-8. Exit status: 0 when nothing blocks, 1 when a finding blocks the commit, 2 when
the scan didn't run (not a git repository, or any other error) - never a finding.
It never writes and never uses the network.
"""
import argparse
import json
import os
import re
import subprocess
import sys

# Pathspec modes set by the caller would change what the :(literal) pathspecs below match (with
# GIT_LITERAL_PATHSPECS=1 they match nothing, and the scan would pass everything), so drop them.
PATHSPEC_MODES = ("GIT_LITERAL_PATHSPECS", "GIT_GLOB_PATHSPECS", "GIT_NOGLOB_PATHSPECS", "GIT_ICASE_PATHSPECS")
ENV = dict({k: v for k, v in os.environ.items() if k not in PATHSPEC_MODES},
           GIT_OPTIONAL_LOCKS="0", LC_ALL="C", GIT_TERMINAL_PROMPT="0")
MB = 1024 * 1024
BLOCK_SIZE = 100 * MB
WARN_SIZE = 50 * MB

# (check, message, pattern) applied to each added line. Values are never reported.
SECRET_PATTERNS = [
    ("private_key", "private key", re.compile(r"-----BEGIN (?:[A-Z]+ )*PRIVATE KEY(?: BLOCK)?-----")),
    ("aws_access_key", "AWS access key ID", re.compile(r"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")),
    ("aws_secret_key", "AWS secret access key",
     re.compile(r"(?i)aws_?secret_?access_?key\W{0,4}[=:]\s*[\"']?[A-Za-z0-9/+=]{40}\b")),
    ("github_token", "GitHub token", re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{22,})\b")),
    ("slack_token", "Slack token", re.compile(r"\bxox[abposr]-[A-Za-z0-9-]{10,}")),
    ("google_api_key", "Google API key", re.compile(r"\bAIza[0-9A-Za-z_\-]{35}\b")),
    ("stripe_key", "Stripe live secret key", re.compile(r"\b[rs]k_live_[0-9A-Za-z]{20,}\b")),
    ("anthropic_key", "Anthropic API key", re.compile(r"\bsk-ant-[A-Za-z0-9_\-]{20,}")),
    # Legacy sk-<48 alphanumerics>, and project, service-account and admin keys whose bodies hold _ and -.
    ("openai_key", "OpenAI API key",
     re.compile(r"\bsk-(?:(?:proj|svcacct|admin)-[A-Za-z0-9_\-]{20,}|[A-Za-z0-9]{32,}\b)")),
]
# user:password@host in a URL. A password that names its source ($VAR, {var}, <var>, ***) isn't one.
URL_CREDENTIALS = re.compile(
    r"\b[a-z][a-z0-9+.\-]*://(?P<user>[^/\s:@\"']+):(?!\$|\{|<|\*{3})(?P<password>[^/\s:@\"']{3,})"
    r"@(?P<host>\[[^\]\s]+\]|[^\s/:\"'?#@]+)")
LOOPBACK = re.compile(r"^(?:127(?:\.\d{1,3}){3}|::1|localhost)$")
EXAMPLE_HOST = re.compile(r"(?:^|\.)(?:example\.(?:com|org|net)|example|test|invalid|localhost)$")
# Test and fixture paths, where a literal credential is usually test data: a directory with one of
# these names, or a file named test_*, conftest.py, or with a _test., .test., _spec. or .spec. part.
TEST_DIRS = {"test", "tests", "__tests__", "spec", "testdata", "fixture", "fixtures", "__fixtures__"}
TEST_FILE = re.compile(r"^(?:test_.*|conftest\.py|.*[._](?:test|spec)\.[^/]+)$", re.I)
# A filename marker is explicit; directories, comments and service names do not opt a file in.
COMPOSE_FILE = re.compile(r"^(?:docker-)?compose(?:\.[a-z0-9_-]+)*\.ya?ml$", re.I)
EXAMPLE_COMPOSE_FILE = re.compile(r"^(?:docker-)?compose\.(?:local|example)\.ya?ml$", re.I)
PASSWORD_KEY = re.compile(r"(?:password|passwd|pwd)$", re.I)
ASSIGNMENT = re.compile(
    r"(?i)\b([a-z0-9_.\-]*(?:password|passwd|pwd|secret|api[_\-]?key|apikey|access[_\-]?token|auth[_\-]?token"
    r"|client[_\-]?secret|private[_\-]?key|credentials?))\b[\"']?\s*(?::|=|:=|=>)\s*([\"'])([^\"'\n]{4,})\2")
# The same keywords with an unquoted value, in configuration-style files only (.env, ini, YAML, TOML,
# shell, files without an extension), where code such as `password = read_password()` doesn't occur.
CONFIG_FILE = re.compile(
    r"(?:^|/)(?:\.env[^/]*|[^/]+\.(?:ini|cfg|conf|properties|ya?ml|toml|sh|bash|zsh|env|envrc)|[^/.]+)$", re.I)
UNQUOTED_ASSIGNMENT = re.compile(
    r"(?i)^\s*(?:export\s+)?([a-z0-9_.\-]*(?:password|passwd|pwd|secret|api[_\-]?key|apikey|access[_\-]?token"
    r"|auth[_\-]?token|client[_\-]?secret|private[_\-]?key))\s*[:=]\s*([^\s\"'#|>&][^\s#]{3,})\s*(?:#.*)?$")
# Values that name where a secret comes from, or stand in for one, rather than hold it.
PLACEHOLDER = re.compile(
    r"^(?:\$|\{\{|<|%\(|!|ENC\[|vault:|your[_\-]|replace[_\-]?me|os\.environ|process\.env)"
    r"|^(?:\*+|x+|\.\.\.|~|true|false|changeme|change_me|example|dummy|fake|sample|none|null|placeholder"
    r"|password|secret)$",
    re.I)

HOME_PATH = re.compile(r"(?<![\w.])(?:/home/|/Users/)[A-Za-z0-9._\-]+|\b[A-Za-z]:[\\/]{1,2}Users[\\/]{1,2}[A-Za-z0-9._\-]+")
EMAIL = re.compile(r"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b")
EXAMPLE_DOMAIN = re.compile(r"(?:^|\.)(?:example\.(?:com|org|net)|example|test|invalid|localhost)$", re.I)
KEYSTORE_NAMES = re.compile(r"(?:^|/)(?:id_(?:rsa|dsa|ecdsa|ed25519)|[^/]+\.(?:p12|pfx|jks|keystore|bks|kdbx))$", re.I)
ENV_FILE = re.compile(r"(?:^|/)\.env(?:\.[^/]+)?$")
ENV_TEMPLATE = re.compile(r"\.(?:example|sample|template|dist|defaults?)$", re.I)
SAVED_PAGE = re.compile(r"\.(?:mhtml?|webarchive)$", re.I)
DOC_EXT = re.compile(r"\.(?:md|markdown|rst|txt|html?|pdf|adoc)$", re.I)
VENDOR_DIRS = {"vendor", "vendored", "third_party", "third-party", "thirdparty", "external", "extern"}
HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")


def git(args, cwd, raw=False):
    """(returncode, stdout). Text is UTF-8 with surrogateescape, so a path that isn't UTF-8 round-trips
    back to git; raw=True returns bytes, for content."""
    if raw:
        r = subprocess.run(["git", *args], cwd=cwd, env=ENV, capture_output=True)
    else:
        r = subprocess.run(["git", *args], cwd=cwd, env=ENV, capture_output=True, encoding="utf-8",
                           errors="surrogateescape")
    return r.returncode, r.stdout


def is_test_path(path):
    parts = path.split("/")
    return bool({p.lower() for p in parts[:-1]} & TEST_DIRS) or bool(TEST_FILE.match(parts[-1]))


def literal_warning(path, key):
    """Return why a literal may be accepted by the user, or None when it must block."""
    if is_test_path(path):
        return " in a test or fixture file"
    if EXAMPLE_COMPOSE_FILE.fullmatch(path.rsplit("/", 1)[-1]) and PASSWORD_KEY.search(key):
        return " in an explicitly named local/example Compose file"
    return None


def assignment_line(path, text):
    """Unwrap Compose list entries, quoted KEY=value scalars and quoted mapping keys.

    This only changes assignment detection, never the text scanned for tokens and private keys.
    Both marked and unmarked Compose files use it, so the latter still block literal passwords.
    """
    if COMPOSE_FILE.fullmatch(path.rsplit("/", 1)[-1]):
        item = re.match(r"^\s*-\s+(.+?)\s*$", text)
        if item:
            text = item.group(1)
            quoted = re.fullmatch(r"([\"'])(.*?)\1(?:\s+#.*)?", text)
            if quoted:
                text = quoted.group(2)
        text = re.sub(r"^(\s*)([\"'])([a-zA-Z0-9_.-]+)\2(\s*:)", r"\1\3\4", text)
    return text


def local_or_example(host):
    """localhost, a loopback address, an example or reserved test domain, or a single-label name such
    as a compose service (db)."""
    h = host.strip("[]").lower().rstrip(".")
    return bool(LOOPBACK.match(h) or h.endswith(".localhost") or EXAMPLE_HOST.search(h)
                or ("." not in h and ":" not in h))


def staged_files(root):
    """[(status, path)] for staged changes, renames split into delete + add."""
    rc, o = git(["diff", "--cached", "--name-status", "-z", "--no-renames"], root)
    parts = o.split("\0") if rc == 0 else []
    res = []
    for i in range(0, len(parts) - 1, 2):
        if parts[i]:
            res.append((parts[i][0], parts[i + 1]))
    return res


def index_blobs(root, paths):
    """{path: (mode, blob, size)} for staged versions."""
    res = {}
    for chunk in range(0, len(paths), 200):
        rc, o = git(["ls-files", "-s", "-z", "--", *[f":(literal){p}" for p in paths[chunk:chunk + 200]]], root)
        for rec in o.split("\0") if rc == 0 else ():
            if not rec:
                continue
            meta, path = rec.split("\t", 1)
            mode, blob, _stage = meta.split()
            res[path] = [mode, blob, None]
    if res:
        r = subprocess.run(["git", "cat-file", "--batch-check=%(objectname) %(objectsize)"], cwd=root, env=ENV,
                           input="\n".join(v[1] for v in res.values()) + "\n", capture_output=True,
                           encoding="utf-8", errors="surrogateescape")
        sizes = dict(line.split() for line in r.stdout.splitlines() if len(line.split()) == 2)
        for v in res.values():
            v[2] = int(sizes.get(v[1], 0))
    return {k: tuple(v) for k, v in res.items()}


def added_lines(root, path):
    """(binary, [(line_no, text)]) for the lines the staged version of path adds."""
    rc, o = git(["diff", "--cached", "-U0", "--no-color", "--no-ext-diff", "--no-renames", "--",
                 f":(literal){path}"], root, raw=True)
    text = o.decode("utf-8", "replace") if rc == 0 else ""
    lines, n, binary, in_hunk = [], 0, False, False
    for raw in text.split("\n"):
        if not in_hunk and (raw.startswith("Binary files ") or raw.startswith("GIT binary patch")):
            binary = True
        m = HUNK.match(raw)
        if m:
            n, in_hunk = int(m.group(1)), True
            continue
        if in_hunk and raw.startswith("+"):   # inside a hunk, so an added "++x" line isn't a +++ header
            lines.append((n, raw[1:]))
            n += 1
    return binary, lines


def known_emails(root):
    rc, o = git(["log", "--all", "--format=%ae%n%ce", "-n", "10000"], root)
    emails = {e.strip().lower() for e in o.splitlines() if e.strip()} if rc == 0 else set()
    rc, o = git(["config", "--get", "user.email"], root)
    if rc == 0 and o.strip():
        emails.add(o.strip().lower())
    for key in ("GIT_AUTHOR_EMAIL", "GIT_COMMITTER_EMAIL"):
        if os.environ.get(key):
            emails.add(os.environ[key].lower())
    return emails


def check_attr(root, attr, path):
    rc, o = git(["check-attr", "--cached", attr, "--", path], root)
    return o.rsplit(": ", 1)[-1].strip() if rc == 0 and o else "unspecified"


def repo_uses_lfs(root):
    rc, o = git(["grep", "--cached", "-l", "-I", "filter=lfs", "--", ".gitattributes", "*/.gitattributes"], root)
    return rc == 0 and bool(o.strip())


def has_license_nearby(root, path, staged_paths):
    parts = path.split("/")
    for i in range(len(parts) - 1, 0, -1):
        d = "/".join(parts[:i])
        for cand in staged_paths:
            if cand.startswith(d + "/") and "/" not in cand[len(d) + 1:] and re.match(
                    r"(?i)(?:licen[cs]e|copying|notice)(?:\.|$)", cand[len(d) + 1:]):
                return True
        try:
            if any(re.match(r"(?i)(?:licen[cs]e|copying|notice)(?:\.|$)", n) for n in os.listdir(os.path.join(root, d))):
                return True
        except OSError:
            pass
    return False


def scan(root):
    blocked, warnings = [], []

    def add(bucket, check, path, message, line=None, **extra):
        f = {"check": check, "path": path, "message": message}
        if line is not None:
            f["line"] = line
        f.update(extra)
        bucket.append(f)

    files = [(s, p) for s, p in staged_files(root) if s != "D"]
    paths = [p for _, p in files]
    blobs = index_blobs(root, paths)
    emails = known_emails(root)
    uses_lfs = repo_uses_lfs(root)

    for status, path in files:
        mode, blob, size = blobs.get(path, ("100644", None, 0))
        base = path.rsplit("/", 1)[-1]
        if mode == "160000":
            continue  # a submodule pointer carries no content
        if ENV_FILE.search(path) and not ENV_TEMPLATE.search(base):
            add(blocked, "env_file", path, ".env file: keep it local and list it in an ignore file")
        if KEYSTORE_NAMES.search(path):
            add(blocked, "key_store", path, "private key or key store file")
        if size > BLOCK_SIZE:
            add(blocked, "file_over_100mb", path, f"file of {size // MB} MB: GitHub rejects files over 100 MB")
            continue
        lfs = check_attr(root, "filter", path) == "lfs"
        if size > WARN_SIZE:
            add(warnings, "file_over_50mb", path,
                f"file of {size // MB} MB" + ("" if lfs else " without an LFS rule"))
        if base.lower().endswith(".pdf"):
            add(warnings, "third_party_document", path, "PDF: check that it may be published")
        elif SAVED_PAGE.search(base):
            add(warnings, "third_party_document", path, "saved web page: check that it may be published")
        segs = {s.lower() for s in path.split("/")[:-1]}
        if segs & VENDOR_DIRS and DOC_EXT.search(base) and not has_license_nearby(root, path, paths):
            add(warnings, "third_party_document", path, "vendored documentation without a license file")

        binary, lines = added_lines(root, path)
        if binary:
            if uses_lfs and not lfs and size > MB:
                add(warnings, "binary_without_lfs", path, "binary file not covered by the repository's LFS rules")
            rc, o = git(["cat-file", "blob", blob], root, raw=True) if blob and size <= WARN_SIZE else (1, b"")
            if rc == 0 and re.search(rb"-----BEGIN (?:[A-Z]+ )*PRIVATE KEY", o):
                add(blocked, "private_key", path, "private key")
            continue

        if base.lower().endswith((".html", ".htm")) and any("saved from url=" in t for _, t in lines[:5]):
            add(warnings, "third_party_document", path, "saved web page: check that it may be published")
        crlf_ok = check_attr(root, "eol", path) == "crlf"
        if not crlf_ok and status == "M":
            rc, o = git(["cat-file", "-p", f"HEAD:{path}"], root, raw=True)
            crlf_ok = rc == 0 and b"\r\n" in o[:65536]
        crlf_lines = []
        for n, t in lines:
            for check, message, pat in SECRET_PATTERNS:
                if pat.search(t):
                    add(blocked, check, path, message, n)
            url_spans = []
            for um in URL_CREDENTIALS.finditer(t):
                url_spans.append(um.span())
                if local_or_example(um.group("host")):
                    add(warnings, "url_credentials", path, "credentials in a URL to a local or example host", n)
                elif um.group("user") == um.group("password"):
                    add(warnings, "url_credentials", path, "credentials in a URL whose user name equals its password", n)
                else:
                    add(blocked, "url_credentials", path, "credentials embedded in a URL", n)
            assignment = assignment_line(path, t)
            m = ASSIGNMENT.search(assignment)
            if m and not PLACEHOLDER.match(m.group(3).strip()):
                note = literal_warning(path, m.group(1))
                add(warnings if note else blocked, "literal_credential", path,
                    f"literal value assigned to {m.group(1)}{note or ''}", n)
            elif CONFIG_FILE.search(path):
                m = UNQUOTED_ASSIGNMENT.match(assignment)
                if m and not PLACEHOLDER.match(m.group(2)):
                    note = literal_warning(path, m.group(1))
                    add(warnings if note else blocked, "literal_credential", path,
                        f"literal value assigned to {m.group(1)}{note or ''}", n)
            for hm in HOME_PATH.finditer(t):
                add(warnings, "home_path", path, "absolute home-directory path", n, match=hm.group(0))
            for em in EMAIL.finditer(t):
                if any(s < em.end() and em.start() < e for s, e in url_spans):
                    continue  # password@host inside a URL credential: reporting it would print the password
                addr = em.group(0)
                domain = addr.rsplit("@", 1)[1]
                if addr.lower().startswith("git@"):
                    continue  # an SSH remote such as git@github.com:owner/repo.git
                if addr.lower() not in emails and not EXAMPLE_DOMAIN.search(domain):
                    add(warnings, "email_address", path, "e-mail address not used by the repository's commit authors",
                        n, match=addr)
            if t.endswith("\r") and not crlf_ok:
                crlf_lines.append(n)
        if crlf_lines:
            add(warnings, "crlf_line_endings", path,
                f"CRLF line endings on {len(crlf_lines)} added line(s) in a repository that uses LF", crlf_lines[0])

    rc, o = git(["diff", "--cached", "--check", "--no-color"], root)
    for line in o.splitlines() if rc != 0 else ():
        m = re.match(r"^(.+?):(\d+): (.+?)\.?$", line)
        if m and not line.startswith("+"):
            add(warnings, "whitespace", m.group(1), m.group(3), int(m.group(2)))

    # One finding per check, path and line.
    def dedupe(items):
        seen, res = set(), []
        for f in items:
            k = (f["check"], f["path"], f.get("line"))
            if k not in seen:
                seen.add(k)
                res.append(f)
        return res

    return {"blocked": dedupe(blocked), "warnings": dedupe(warnings), "files_scanned": len(files)}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("-C", dest="cwd", default=".", help="run as if started in this directory")
    a = ap.parse_args(argv)
    try:
        rc, root = git(["rev-parse", "--show-toplevel"], os.path.abspath(a.cwd))
        if rc != 0:
            print(json.dumps({"error": "not_a_git_repository"}))
            return 2
        report = scan(root.strip())
    except Exception as e:  # the scan didn't run: exit 2, which is never a blocked finding
        print(json.dumps({"error": f"{type(e).__name__}: {e}"}))
        return 2
    print(json.dumps(report, indent=2))
    return 1 if report["blocked"] else 0


if __name__ == "__main__":
    sys.exit(main())

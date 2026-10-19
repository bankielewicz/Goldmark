#!/usr/bin/env python3
"""Validate DevForgeAI policy documents (configuration contract v1, ADR-003 A4 step R1).

Usage: python3 validate_policy.py [POLICY_DIR]      (default: docs/specs/policy)

Reads every POL-*.md in the folder. A document whose status is not approved takes no part: it is
reported as "ignored <file> (status <status>)" and not checked (SV-06). Each approved document is
validated in full against references/schemas/policy.schema.json and common.schema.json (field types,
date patterns, authors and link records) with the jsonschema library and a format checker, so the
schema's date format checks created, updated and a non-null approved_on against the calendar
(2026-13-45 fails as a schema error). A YAML .nan is a schema error too: JSON has no NaN, and the
schema's minimum and maximum can't reject it. Then the semantic rules apply:
SV-01 unique setting IDs, SV-02 one approved document per scope, SV-03 one active interview.max_calls
per document, SV-04 one active mandated platform per capability per document and no project override
the organization setting forbids, SV-05 deprecated settings take no part (reported), SV-06
non-approved documents take no part (reported), SV-08 one active setting per testing.* key per
document.

Output: one line per error, "<file>: <part>: <field>: <message> (<rule>)", where <part> is
"frontmatter", "document" or a setting ("SET-01 (interview.max_calls)") and <rule> is "schema" or
"SV-NN"; then the notes for SV-05 and SV-06; then one summary line.

Exit codes: 0 when every approved document is valid (or none exists), 1 when any is invalid, 2 when
the check can't run while approved policy exists (PyYAML or jsonschema missing, a schema copy missing
or unreadable) or POLICY_DIR is not a folder. Read-only: it never writes a file.
"""
import json
import re
import sys
import warnings
from pathlib import Path

SCHEMAS = Path(__file__).resolve().parent.parent / "references" / "schemas"
FRONTMATTER = re.compile(r"\A---[ \t]*\n(.*?)\n---[ \t]*(?:\n|\Z)", re.S)
STATUS = re.compile(r"^status:[ \t]*[\"']?([A-Za-z-]+)[\"']?[ \t]*(?:#.*)?$", re.M)
ITEM_BLOCK = re.compile(r"^```yaml items[ \t]*\n(.*?)^```[ \t]*$", re.S | re.M)
MAX_MESSAGE = 160
TESTING_KEYS = ("testing.method", "testing.coverage_metric", "testing.coverage_threshold", "testing.coverage_scope",
                "testing.coverage_exclusions", "testing.exception_approvers")


class CannotRun(Exception):
    pass


def split_documents(folder):
    """Returns (candidates, ignored): approved (or undeterminable) documents, and the others."""
    candidates, ignored = [], []
    for path in sorted(folder.glob("POL-*.md")):
        text = path.read_text(encoding="utf-8")
        m = FRONTMATTER.match(text)
        status = STATUS.search(m.group(1)) if m else None
        if status and status.group(1) != "approved":
            ignored.append((path, status.group(1)))
        else:
            candidates.append((path, text))
    return candidates, ignored


def load_libraries():
    try:
        import yaml
        import jsonschema
    except ImportError as exc:
        raise CannotRun(f"{exc.name or exc} is not installed")
    return yaml, jsonschema


def make_validator(jsonschema):
    try:
        policy = json.loads((SCHEMAS / "policy.schema.json").read_text(encoding="utf-8"))
        common = json.loads((SCHEMAS / "common.schema.json").read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise CannotRun(f"schema copy unreadable: {exc}")
    cls = jsonschema.Draft202012Validator
    try:
        from referencing import Registry, Resource  # jsonschema 4.18 and later
    except ImportError:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", DeprecationWarning)
            resolver = jsonschema.RefResolver.from_schema(policy, store={common["$id"]: common})
            return cls(policy, resolver=resolver, format_checker=jsonschema.FormatChecker())
    registry = Registry().with_resource(common["$id"], Resource.from_contents(common))
    return cls(policy, registry=registry, format_checker=jsonschema.FormatChecker())


def yaml_loader(yaml):
    class Loader(yaml.SafeLoader):
        """Keeps dates as strings, as the schemas expect."""

    Loader.yaml_implicit_resolvers = {
        k: [r for r in v if r[0] != "tag:yaml.org,2002:timestamp"]
        for k, v in yaml.SafeLoader.yaml_implicit_resolvers.items()
    }
    return Loader


def parse(yaml, loader, text):
    """Returns the document as the schema sees it: {"frontmatter": {...}, "settings": [...]}."""
    m = FRONTMATTER.match(text)
    if not m:
        raise ValueError("no YAML frontmatter between --- lines at the top of the file")
    doc = {"frontmatter": yaml.load(m.group(1), Loader=loader)}
    for block in ITEM_BLOCK.findall(text):
        data = yaml.load(block, Loader=loader)
        if not isinstance(data, dict):
            raise ValueError("an item block must hold one collection key, such as settings:")
        for key, value in data.items():
            if isinstance(doc.get(key), list) and isinstance(value, list):
                doc[key].extend(value)
            else:
                doc[key] = value
    return doc


def setting_label(doc, index):
    settings = doc.get("settings")
    item = settings[index] if isinstance(settings, list) and index < len(settings) else None
    if not isinstance(item, dict):
        return f"settings[{index}]"
    sid = item.get("id") if isinstance(item.get("id"), str) else f"settings[{index}]"
    key = item.get("key")
    return f"{sid} ({key})" if isinstance(key, str) else sid


def field_path(parts):
    out = ""
    for p in parts:
        out += f"[{p}]" if isinstance(p, int) else (f".{p}" if out else str(p))
    return out


def shorten(text):
    return text if len(text) <= MAX_MESSAGE else text[: MAX_MESSAGE - 3] + "..."


def locate(doc, path):
    """Splits an error's path into the <part> and the rest, which field_path turns into <field>."""
    if path[:1] == ["frontmatter"]:
        return "frontmatter", path[1:]
    if path[:1] == ["settings"] and len(path) > 1 and isinstance(path[1], int):
        return setting_label(doc, path[1]), path[2:]
    return "document", path


def nan_paths(value, path=()):
    """Yields the path of every NaN. JSON has no NaN, so a YAML .nan fits no schema type, yet a schema's
    minimum and maximum can't reject it: every comparison with NaN is false."""
    if isinstance(value, float) and value != value:
        yield path
    elif isinstance(value, dict):
        for k, v in value.items():
            yield from nan_paths(v, path + (k,))
    elif isinstance(value, list):
        for i, v in enumerate(value):
            yield from nan_paths(v, path + (i,))


def schema_errors(validator, doc):
    errors = []
    found = sorted(validator.iter_errors(doc), key=lambda e: [str(p) for p in e.absolute_path])
    for e in found:
        path = list(e.absolute_path)
        part, rest = locate(doc, path)
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
        elif e.validator == "oneOf" and path == ["frontmatter", "approved_on"]:
            formats = [c for c in e.context if c.validator == "format"]
            if formats:
                message = formats[0].message
        errors.append((part, field_path(rest) or "(whole)", shorten(message), "schema"))
    reported = {tuple(e.absolute_path) for e in found}
    for path in nan_paths(doc):
        if path not in reported:  # a type error at the same place already reports it
            part, rest = locate(doc, list(path))
            errors.append((part, field_path(rest) or "(whole)", "nan is not a number", "schema"))
    return errors


def active_settings(doc):
    settings = doc.get("settings")
    for i, s in enumerate(settings if isinstance(settings, list) else []):
        if isinstance(s, dict) and s.get("status") == "active":
            yield i, s


def capability(setting):
    value = setting.get("value")
    cap = value.get("capability") if isinstance(value, dict) else None
    return cap.strip().lower() if isinstance(cap, str) else None


def semantic_errors(doc):
    errors, notes = [], []
    settings = doc.get("settings")
    settings = settings if isinstance(settings, list) else []
    seen = {}
    for s in settings:
        if isinstance(s, dict) and isinstance(s.get("id"), str):
            seen[s["id"]] = seen.get(s["id"], 0) + 1
    for sid, count in seen.items():
        if count > 1:
            errors.append((sid, "id", f"setting ID used {count} times in this document", "SV-01"))
    max_calls = [s.get("id", f"settings[{i}]") for i, s in active_settings(doc) if s.get("key") == "interview.max_calls"]
    if len(max_calls) > 1:
        errors.append(("settings", "key", "more than one active interview.max_calls setting: "
                       + ", ".join(map(str, max_calls)), "SV-03"))
    for key in TESTING_KEYS:
        ids = [s.get("id", f"settings[{i}]") for i, s in active_settings(doc) if s.get("key") == key]
        if len(ids) > 1:
            errors.append(("settings", "key", f"more than one active {key} setting: " + ", ".join(map(str, ids)),
                           "SV-08"))
    by_capability = {}
    for i, s in active_settings(doc):
        if s.get("key") == "architecture.mandated_platforms" and capability(s):
            by_capability.setdefault(capability(s), []).append(s.get("id", f"settings[{i}]"))
    for cap, ids in by_capability.items():
        if len(ids) > 1:
            errors.append(("settings", "value.capability", f"more than one active mandated platform for "
                           f"'{cap}': " + ", ".join(map(str, ids)), "SV-04"))
    for i, s in enumerate(settings):
        if isinstance(s, dict) and s.get("status") == "deprecated":
            notes.append(f"{setting_label(doc, i)} is deprecated and takes no part (SV-05)")
    return errors, notes


def cross_document_errors(docs):
    """SV-02 and SV-04's cross-layer clause, over the approved documents that parsed."""
    errors = []
    by_scope = {}
    for path, doc in docs:
        fm = doc.get("frontmatter")
        scope = fm.get("scope") if isinstance(fm, dict) else None
        if isinstance(scope, str):
            by_scope.setdefault(scope, []).append(path)
    for scope, paths in by_scope.items():
        for path in paths[1:]:
            errors.append((path, "frontmatter", "scope", f"a second approved {scope} policy; "
                           f"{paths[0]} is also approved", "SV-02"))
    org = [(p, d) for p, d in docs if isinstance(d.get("frontmatter"), dict)
           and d["frontmatter"].get("scope") == "organization"]
    project = [(p, d) for p, d in docs if isinstance(d.get("frontmatter"), dict)
               and d["frontmatter"].get("scope") == "project"]
    for ppath, pdoc in project:
        for pi, ps in active_settings(pdoc):
            if ps.get("key") != "architecture.mandated_platforms" or not capability(ps):
                continue
            for opath, odoc in org:
                for oi, os_ in active_settings(odoc):
                    if os_.get("key") != "architecture.mandated_platforms" or capability(os_) != capability(ps):
                        continue
                    allowed = os_.get("overridable_by")
                    if not (isinstance(allowed, list) and "project" in allowed):
                        errors.append((ppath, setting_label(pdoc, pi), "value.capability",
                                       f"overrides {opath} {setting_label(odoc, oi)}, whose overridable_by "
                                       "doesn't include project", "SV-04"))
    return errors


def main(argv):
    folder = Path(argv[1]) if len(argv) > 1 else Path("docs/specs/policy")
    if not folder.exists():
        print(f"No policy folder at {folder}: no policy, so the framework defaults apply.")
        return 0
    if not folder.is_dir():
        print(f"Cannot run: {folder} is not a folder.")
        return 2
    candidates, ignored = split_documents(folder)
    if not candidates:
        for path, status in ignored:
            print(f"ignored {path} (status {status}) (SV-06)")
        print(f"OK: no approved policy document in {folder}; the framework defaults apply.")
        return 0
    try:
        yaml, jsonschema = load_libraries()
        validator = make_validator(jsonschema)
    except CannotRun as exc:
        print(f"Cannot run: {exc}. {len(candidates)} approved policy document(s) in {folder} were not validated.")
        return 2
    loader = yaml_loader(yaml)
    errors, notes, parsed = [], [], []
    for path, text in candidates:
        try:
            doc = parse(yaml, loader, text)
        except (yaml.YAMLError, ValueError) as exc:
            errors.append((path, "document", "YAML", shorten(" ".join(str(exc).split())), "schema"))
            continue
        parsed.append((path, doc))
        for part, field, message, rule in schema_errors(validator, doc):
            errors.append((path, part, field, message, rule))
        sv_errors, sv_notes = semantic_errors(doc)
        errors += [(path,) + e for e in sv_errors]
        notes += [f"{path}: {n}" for n in sv_notes]
    errors += cross_document_errors(parsed)
    for path, part, field, message, rule in errors:
        print(f"{path}: {part}: {field}: {message} ({rule})")
    for note in notes:
        print(note)
    for path, status in ignored:
        print(f"ignored {path} (status {status}) (SV-06)")
    if errors:
        print(f"INVALID: {len(errors)} error(s) in {len({e[0] for e in errors})} approved policy document(s).")
        return 1
    print(f"OK: {len(candidates)} approved policy document(s) valid.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

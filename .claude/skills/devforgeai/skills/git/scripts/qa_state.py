#!/usr/bin/env python3
"""QA state of a pull request for the devforgeai git skill (SPEC-007 BEH-20, SPEC-008 §4).

Usage:
    gh pr view <number> --json labels,comments,headRefOid | python3 qa_state.py \
        [--approved-label merge-approved] [--failed-label qa-failed]

Reads the gh JSON on stdin and prints one JSON object with "state": pending, unverified,
conflicting, stale, approved or failed, and "merge_allowed" (true only for approved). The verdict is
the latest comment whose first line matches ^QA verdict: (passed|failed) [0-9a-f]{40}$, with its
author and URL for the readiness report. Never uses the network. Exit status: 0 with a report, 2 when
the input isn't gh JSON of the expected shape (no state was derived).
"""
import argparse
import json
import re
import sys

VERDICT = re.compile(r"^QA verdict: (passed|failed) ([0-9a-f]{40})$")


def latest_verdict(comments):
    found = []
    for i, c in enumerate(comments or []):
        body = (c.get("body") or "").replace("\r\n", "\n")
        first = body.split("\n", 1)[0].rstrip()
        m = VERDICT.match(first)
        if m:
            found.append((c.get("createdAt") or "", i, m.group(1), m.group(2), c, body))
    if not found:
        return None
    _, _, result, sha, c, body = max(found, key=lambda f: (f[0], f[1]))
    author = c.get("author") or {}
    return {"result": result, "sha": sha, "created_at": c.get("createdAt"),
            "author": author.get("login") if isinstance(author, dict) else author,
            "url": c.get("url"), "findings": body.split("\n", 1)[1].strip() if "\n" in body else ""}


def qa_state(pr, approved_label="merge-approved", failed_label="qa-failed"):
    labels = {(lb.get("name") if isinstance(lb, dict) else lb) for lb in pr.get("labels") or []}
    approved, failed = approved_label in labels, failed_label in labels
    head = pr.get("headRefOid")
    verdict = latest_verdict(pr.get("comments"))
    qa_labels = [n for n, on in ((approved_label, approved), (failed_label, failed)) if on]

    if approved and failed:
        state, reason = "conflicting", "both QA labels are set"
    elif not verdict:
        state = "unverified" if qa_labels else "pending"
        reason = ("a QA label is set but no verdict comment names a commit" if qa_labels
                  else "no QA label and no verdict comment yet")
    elif (approved and verdict["result"] == "failed") or (failed and verdict["result"] == "passed"):
        state, reason = "conflicting", "the QA label contradicts the latest verdict"
    elif verdict["sha"] != head:
        state, reason = "stale", "the latest verdict names a commit other than the PR's head"
    elif approved:
        state, reason = "approved", "merge-approved with a passing verdict for the head commit"
    elif failed:
        state, reason = "failed", "qa-failed with a failing verdict for the head commit"
    else:
        state, reason = "pending", "a verdict names the head commit but no QA label is set yet"
    return {"state": state, "merge_allowed": state == "approved", "reason": reason, "head": head,
            "qa_labels": qa_labels, "verdict": verdict}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--approved-label", default="merge-approved")
    ap.add_argument("--failed-label", default="qa-failed")
    a = ap.parse_args(argv)
    try:
        pr = json.load(sys.stdin)
        if not isinstance(pr, dict):
            raise ValueError("expected a JSON object")
    except ValueError as e:
        print(json.dumps({"error": f"invalid gh pr view JSON: {e}"}))
        return 2
    try:
        state = qa_state(pr, a.approved_label, a.failed_label)
    except Exception as e:  # input of an unexpected shape: no state, never a guess
        print(json.dumps({"error": f"unexpected gh pr view JSON: {type(e).__name__}: {e}"}))
        return 2
    print(json.dumps(state, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())

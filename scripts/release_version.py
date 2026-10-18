#!/usr/bin/env python3
"""Release helpers for HangarScope: version bump, release notes and features.json checks.

Every merge to master is released (AGENTS.md rule 5). The CI workflows call this script so the
rules live in one tested place (tests: scripts/tests/test_release_version.py):

  latest-tag          read git tags on stdin, print the highest vX.Y.Z (empty if none)
  bump                work out the next version from the latest tag, changed paths and PR labels
  notes               build GitHub Release notes from new docs/changelog entries and what's-new entries
  validate-features   check docs/whats-new/features.json (valid JSON, required fields, unique pr)

Bump rules (references/versioning-and-release.md in the ship-change skill):
  1. the biggest semver:* label wins;
  2. otherwise a new docs/whats-new/features.json entry means minor;
  3. otherwise patch. Major only ever comes from the semver:major label.

Standard library only, so it runs on any GitHub runner without setup.
"""

from __future__ import annotations

import argparse
import datetime
import json
import os
import re
import sys
from pathlib import Path

FEATURES_PATH = "docs/whats-new/features.json"
CHANGELOG_DIR = "docs/changelog"
BUMPS = ("patch", "minor", "major")
TAG_RE = re.compile(r"^v(\d+)\.(\d+)\.(\d+)$")
LABEL_RE = re.compile(r"^semver:(patch|minor|major)$")
DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


# ---- versions ----

def parse_tag(tag: str) -> tuple[int, int, int] | None:
    m = TAG_RE.match(tag.strip())
    return (int(m[1]), int(m[2]), int(m[3])) if m else None


def latest_tag(tags: list[str]) -> str:
    """The highest strict vX.Y.Z tag, compared numerically; '' when there is none."""
    versions = [(v, t.strip()) for t in tags if (v := parse_tag(t))]
    return max(versions)[1] if versions else ""


def apply_bump(version: tuple[int, int, int], bump: str) -> tuple[int, int, int]:
    major, minor, patch = version
    if bump == "major":
        return (major + 1, 0, 0)
    if bump == "minor":
        return (major, minor + 1, 0)
    if bump == "patch":
        return (major, minor, patch + 1)
    raise ValueError(f"unknown bump '{bump}'")


def feature_prs(features_json: str | None) -> set[int]:
    """PR numbers in a features.json text; empty for a missing or unreadable file."""
    if not features_json:
        return set()
    try:
        data = json.loads(features_json)
        return {f["pr"] for f in data.get("features", []) if isinstance(f, dict) and isinstance(f.get("pr"), int)}
    except (ValueError, AttributeError):
        return set()


def decide_bump(
    latest: str,
    changed_paths: list[str],
    labels: list[str],
    features_before: str | None = None,
    features_after: str | None = None,
) -> dict[str, str]:
    """Returns {'bump', 'version', 'reason'} for the next release.

    features_before/after are the features.json texts at the latest tag and at the new head. When
    given, only an entry with a new pr number counts as a feature (editing an old entry is a patch);
    without them, any change to features.json counts.
    """
    base = parse_tag(latest) if latest else (0, 0, 0)
    if base is None:
        raise ValueError(f"latest tag '{latest}' is not vX.Y.Z")

    label_bumps = sorted({m[1] for l in labels if (m := LABEL_RE.match(l.strip()))}, key=BUMPS.index)
    paths = {p.strip().replace("\\", "/") for p in changed_paths if p.strip()}
    if label_bumps:
        bump = label_bumps[-1]
        reason = f"label semver:{bump}"
    elif FEATURES_PATH in paths and features_after is not None:
        new = sorted(feature_prs(features_after) - feature_prs(features_before))
        if new:
            bump = "minor"
            reason = "new what's-new entry for PR " + ", ".join(f"#{n}" for n in new)
        else:
            bump = "patch"
            reason = f"{FEATURES_PATH} changed but has no new entry"
    elif FEATURES_PATH in paths:
        bump = "minor"
        reason = f"{FEATURES_PATH} changed"
    else:
        bump = "patch"
        reason = "no semver label and no new what's-new entry"

    version = ".".join(str(n) for n in apply_bump(base, bump))
    return {"bump": bump, "version": version, "reason": reason}


# ---- features.json ----

def validate_features(text: str, root: Path | None = None) -> list[str]:
    """Problems with a features.json text (empty list = valid). root = docs/whats-new, to check images exist."""
    try:
        data = json.loads(text)
    except ValueError as e:
        return [f"not valid JSON: {e}"]
    if not isinstance(data, dict) or not isinstance(data.get("features"), list):
        return ['top level must be an object with a "features" array']

    errors: list[str] = []
    seen: set[int] = set()
    for i, f in enumerate(data["features"]):
        where = f"features[{i}]"
        if not isinstance(f, dict):
            errors.append(f"{where}: must be an object")
            continue
        pr = f.get("pr")
        # bool is an int subclass in Python; true/false is not a PR number.
        if not isinstance(pr, int) or isinstance(pr, bool) or pr <= 0:
            errors.append(f"{where}: pr must be a positive integer")
        elif pr in seen:
            errors.append(f"{where}: pr {pr} is used by an earlier entry")
        else:
            seen.add(pr)
        date = f.get("date")
        if not isinstance(date, str) or not DATE_RE.match(date):
            errors.append(f"{where}: date must be YYYY-MM-DD")
        else:
            try:
                datetime.date.fromisoformat(date)
            except ValueError:
                errors.append(f"{where}: date {date} is not a real date")
        for key in ("title", "description"):
            if not isinstance(f.get(key), str) or not f[key].strip():
                errors.append(f"{where}: {key} must be a non-empty string")
        images = f.get("images", [])
        if not isinstance(images, list):
            errors.append(f"{where}: images must be an array")
            continue
        for j, img in enumerate(images):
            iw = f"{where}.images[{j}]"
            if not isinstance(img, dict) or not isinstance(img.get("src"), str) or not img["src"].strip():
                errors.append(f"{iw}: src must be a non-empty string")
                continue
            if not isinstance(img.get("caption"), str) or not img["caption"].strip():
                errors.append(f"{iw}: caption must be a non-empty string")
            if root is not None and not (root / img["src"]).is_file():
                errors.append(f"{iw}: {img['src']} not found under {root.as_posix()}")
    return errors


# ---- release notes ----

def _absolute_links(markdown: str, entry_dir: str, repo: str, sha: str) -> str:
    """Rewrites relative image/link targets in a changelog entry to URLs pinned to the release commit."""
    def fix(m: re.Match) -> str:
        target = m[2]
        if re.match(r"^[a-z][a-z0-9+.-]*:|^#|^/", target, re.I):
            return m[0]
        url = f"https://github.com/{repo}/blob/{sha}/{entry_dir}/{target}"
        if m[1].startswith("!"):
            url += "?raw=true"
        return f"{m[1]}({url})"
    return re.sub(r"(!?\[[^\]]*\])\(([^)\s]+)\)", fix, markdown)


def changelog_entries(added_paths: list[str]) -> list[str]:
    """Entry folders (docs/changelog/<entry>) whose README.md was added, oldest first (folders are dated)."""
    entries = set()
    for p in added_paths:
        parts = p.strip().replace("\\", "/").split("/")
        if len(parts) == 4 and "/".join(parts[:2]) == CHANGELOG_DIR and parts[3] == "README.md":
            entries.add("/".join(parts[:3]))
    return sorted(entries)


def build_notes(
    version: str,
    added_paths: list[str],
    read_file,
    repo: str,
    sha: str,
    features_before: str | None = None,
    features_after: str | None = None,
    commits: list[str] | None = None,
) -> str:
    """Markdown for the GitHub Release. read_file(path) returns a repo file's text."""
    lines = [f"# HangarScope v{version}", ""]

    new_prs = feature_prs(features_after) - feature_prs(features_before)
    if new_prs:
        features = [f for f in json.loads(features_after)["features"] if f.get("pr") in new_prs]
        lines += ["## What's new", ""]
        for f in features:
            lines += [f"### {f['title']}", "", f["description"].strip(), ""]

    entries = changelog_entries(added_paths)
    if entries:
        lines += ["## Changes", ""]
        for entry in entries:
            text = read_file(f"{entry}/README.md").strip()
            # Demote headings two levels so each entry's title nests under "## Changes".
            text = re.sub(r"^(#{1,4}) ", r"##\1 ", text, flags=re.M)
            lines += [_absolute_links(text, entry, repo, sha), ""]
    else:
        lines += ["## Changes", "", "No release-log entry in this release. Commits:", ""]
        lines += [f"- {c.strip()}" for c in (commits or []) if c.strip()] or ["- (none)"]
        lines.append("")

    return "\n".join(lines).rstrip() + "\n"


# ---- CLI ----

def _read_lines(path: str | None) -> list[str]:
    if not path:
        return []
    return [l for l in Path(path).read_text(encoding="utf-8").splitlines() if l.strip()]


def _read_optional(path: str | None) -> str | None:
    if not path or not os.path.isfile(path):
        return None
    return Path(path).read_text(encoding="utf-8")


def _labels(raw: str | None) -> list[str]:
    """Labels as a JSON array or a comma/newline separated list."""
    if not raw or not raw.strip():
        return []
    raw = raw.strip()
    if raw.startswith("["):
        return [str(l) for l in json.loads(raw)]
    return [l for l in re.split(r"[,\n]", raw) if l.strip()]


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("latest-tag", help="read tags on stdin, print the highest vX.Y.Z")

    b = sub.add_parser("bump", help="print bump=, version=, reason= lines")
    b.add_argument("--latest-tag", default="")
    b.add_argument("--changed-paths-file", help="file with one changed path per line")
    b.add_argument("--labels", default="", help="PR labels: JSON array or comma separated")
    b.add_argument("--features-before", help="features.json at the latest tag (missing file = no entries)")
    b.add_argument("--features-after", help="features.json at the new head")

    n = sub.add_parser("notes", help="print release notes markdown")
    n.add_argument("--version", required=True)
    n.add_argument("--added-paths-file", help="file with one added path per line")
    n.add_argument("--repo", required=True, help="owner/name")
    n.add_argument("--sha", required=True)
    n.add_argument("--root", default=".", help="repo checkout to read changelog entries from")
    n.add_argument("--features-before")
    n.add_argument("--features-after")
    n.add_argument("--commits-file", help="commit subjects, used when there is no changelog entry")

    v = sub.add_parser("validate-features", help="check features.json; exit 1 on problems")
    v.add_argument("path", nargs="?", default=FEATURES_PATH)

    args = p.parse_args(argv)

    if args.cmd == "latest-tag":
        print(latest_tag(sys.stdin.read().split()))
        return 0

    if args.cmd == "bump":
        result = decide_bump(
            args.latest_tag,
            _read_lines(args.changed_paths_file),
            _labels(args.labels),
            _read_optional(args.features_before),
            _read_optional(args.features_after),
        )
        for k in ("bump", "version", "reason"):
            print(f"{k}={result[k]}")
        return 0

    if args.cmd == "notes":
        root = Path(args.root)
        sys.stdout.write(build_notes(
            args.version,
            _read_lines(args.added_paths_file),
            lambda path: (root / path).read_text(encoding="utf-8"),
            args.repo,
            args.sha,
            _read_optional(args.features_before),
            _read_optional(args.features_after),
            _read_lines(args.commits_file),
        ))
        return 0

    if args.cmd == "validate-features":
        path = Path(args.path)
        if not path.is_file():
            print(f"{path}: file not found", file=sys.stderr)
            return 1
        errors = validate_features(path.read_text(encoding="utf-8"), path.parent)
        for e in errors:
            # ::error makes each problem show up as an annotation in GitHub Actions.
            print(f"::error file={path.as_posix()}::{e}")
        if not errors:
            print(f"{path}: OK")
        return 1 if errors else 0

    return 2


if __name__ == "__main__":
    sys.exit(main())

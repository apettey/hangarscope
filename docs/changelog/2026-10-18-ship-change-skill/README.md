# Ship-change process

**Date:** 2026-10-18

## Description

Replaces the "docs + screenshots" policy in `AGENTS.md` with the shared six-rule way of working: tests, a fresh
reviewer loop until a round has no findings, before/after screenshots, a release-log entry, a new version for every
merge and fast releases. Agents follow it through a new `ship-change` skill.

## Change

- New skill `.claude/skills/ship-change/` (`SKILL.md` plus `references/` for formats, review loop, versioning and
  release, and a checklist). Repo-specific values sit in one block so the skill can be copied to other repos.
- New release log `docs/changelog/` (index, `TEMPLATE.md`, this entry).
- New `docs/whats-new/features.json` for user-facing announcements (empty; used as release notes until the app has a
  What's New panel).
- New `.github/pull_request_template.md`.
- `AGENTS.md`: the documentation policy is now the six rules and points to the skill. `README.md`: new Contributing
  section.
- No app code, settings, ESI endpoints or scopes changed. No tests added: nothing executable changed.
- Release automation is still manual (`scripts/release.ps1` after each merge); the gaps are listed in
  `.claude/skills/ship-change/references/versioning-and-release.md`.

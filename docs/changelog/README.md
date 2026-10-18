# Release log

One entry per change, newest first. Every merge to `master` is released as a new version (see
[`AGENTS.md`](../../AGENTS.md) and the `ship-change` skill in [`.claude/skills/ship-change/`](../../.claude/skills/ship-change/SKILL.md)).
New entries start from [`TEMPLATE.md`](TEMPLATE.md); add the row at the top of the table. Entries are a log and are
never rewritten: changing a feature again gets a new entry.

| Date | Change | Summary |
| --- | --- | --- |
| 2026-10-18 | [Pin the shared release actions](2026-10-18-pin-shared-actions/README.md) | Internal: release and CI use the shared eve-contracts actions pinned to a commit, updated by Dependabot PRs, instead of `@main` |
| 2026-10-18 | [Shared release versioning](2026-10-18-shared-versioning/README.md) | Internal: versioning, release notes and the `features.json` check run on the shared code in `apettey/eve-contracts`; the local release script and `release.ps1` are gone |
| 2026-10-18 | [Release automation](2026-10-18-release-automation/README.md) | Internal: every merge to `master` is versioned, built, tagged and released by CI; PR CI predicts the version and checks `features.json`; `deploy:manual` publishes a prerelease |
| 2026-10-18 | [Ship-change process](2026-10-18-ship-change-skill/README.md) | Internal: `ship-change` skill, release log, what's-new file and PR template; every merge is released |

# Checklist

```markdown
### Start
- [ ] Branched from latest master (or stacked on the newest open PR): claude/<name>
- [ ] Visible change: before.png captured with --demo --snapshot before the first edit

### Build
- [ ] Tests added/updated in HangarScope.Tests (failing test first for bug fixes)
- [ ] `dotnet build HangarScope/HangarScope.csproj`: 0 warnings, 0 errors
- [ ] `dotnet test HangarScope.slnx` passes
- [ ] `dotnet publish HangarScope/HangarScope.csproj -c Release -r win-x64 --self-contained true -o publish` succeeds
- [ ] Tried in the running app (--demo), edge cases and toggles included
- [ ] Visible change: after.png of the final version; docs/screenshots, README.md, docs/FEATURES.md updated
- [ ] SETTINGS captures checked for the real ESI client ID

### Document
- [ ] docs/changelog/YYYY-MM-DD-<name>/README.md (+ before/after); row at the top of docs/changelog/README.md

### Review
- [ ] Fresh reviewer subagent given the diff, the intent and the checklist
- [ ] Every finding fixed or answered; same reviewer re-reviewed the new diff
- [ ] Repeated until a round had zero findings; round count in the PR body

### Release
- [ ] PR opened; visible change: docs/whats-new/features.json entry with "pr": N and img/pr-N-*.png, reviewed clean
- [ ] PR body: what/why, SHA-pinned before/after, release-log link, tests, review rounds
- [ ] semver:* label if the default is wrong; deploy:manual if outside work must happen before installed copies update (publishes a prerelease; promote with `gh release edit vX.Y.Z --prerelease=false`)
- [ ] CI green, merged with a merge commit
- [ ] Release run on master green; GitHub Release vX.Y.Z (the version CI predicted) has zip + Velopack packages
```

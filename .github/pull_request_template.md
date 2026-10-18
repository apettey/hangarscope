## What

<!-- What changed and why, in plain language. -->

## Screenshots

<!-- Visible changes: link images pinned to a commit SHA, e.g.
![before](https://github.com/apettey/hangarscope/blob/<sha>/docs/changelog/<entry>/before.png?raw=true)
Nothing visible? Write "Not visible" and delete the table. -->

| Before | After |
| --- | --- |
|  |  |

Release log: `docs/changelog/YYYY-MM-DD-<name>/`

## Checklist

- [ ] Tests added/updated; `dotnet test HangarScope.slnx` passes; `dotnet build` has 0 warnings
- [ ] Release-log entry: `docs/changelog/YYYY-MM-DD-<name>/` (with before/after if visible), row at the top of the index
- [ ] Visible to users? `docs/whats-new/features.json` entry with this PR's number; README/FEATURES/screenshots updated
- [ ] Review loop done: <N> rounds, last one with no findings
- [ ] Version bump: default (what's-new entry = minor, everything else = patch) or a `semver:*` label; CI's job summary says "Merging this PR would release vX.Y.Z"; `deploy:manual` if installed copies must wait for outside work (publishes a prerelease)

# Review loop

No PR is opened until a review round comes back with **zero findings**, and none is merged until the last round
(which also covers the what's-new entry added after the PR exists) is clean. CI can't check this; the round count in
the PR body is the evidence.

## How to run it

1. Commit everything. Collect:
   - `git diff origin/master...HEAD` (and `--stat`)
   - a short statement of what the change is meant to do
   - whether it's visible to users
2. Spawn a **fresh** reviewer with the Agent tool (`subagent_type: general-purpose`, or `feature-dev:code-reviewer`).
   The author (you) must not review your own diff. Paste the brief below with the blanks filled in.
3. For every finding: fix it, or write down why it isn't a problem. Commit.
4. Send the **same** reviewer (SendMessage to its agent id, so it can check its earlier findings) the new diff and
   your answers to anything you didn't fix. Ask for a full re-review, not only of the fixes.
5. Repeat until a round reports `NO FINDINGS`. Record `Review rounds: K, last one clean` in the PR body.

If a finding is a disagreement about intent rather than a defect, ask the user instead of going round in circles.

## Reviewer brief (paste into the subagent prompt)

```text
You are reviewing a change to HangarScope (.NET 10 + Avalonia desktop app for EVE Online assets, repo at
<REPO_PATH>). You did not write this change. Review the diff, not the whole codebase, but open any file you need for
context. Do not edit files.

Intent: <WHAT THE CHANGE IS MEANT TO DO>
Visible to users: <yes/no>
Diff base: origin/master. Run `git diff origin/master...HEAD` yourself if the pasted diff is cut off.

<DIFF OR "run git diff origin/master...HEAD">

Check:
- Correctness: does it do what the intent says, including edge cases (empty data, unknown type IDs, many
  characters, ESI errors/timeouts)? Regressions in nearby behaviour?
- Tests: is the new behaviour covered in HangarScope.Tests? Would the tests fail without the change? Is logic that
  can be pure kept in Services/ rather than the view model?
- Evidence: docs/changelog/YYYY-MM-DD-<name>/README.md exists, is accurate, and its row is at the top of
  docs/changelog/README.md. If visible: before.png and after.png exist and after.png matches the final UI; README.md
  / docs/FEATURES.md / docs/screenshots are updated; a docs/whats-new/features.json entry is present (or will be
  once the PR number exists) and written for users.
- Security: no ESI client secrets, tokens or character credentials committed, logged or shown; the SETTINGS
  screenshot doesn't expose a real client ID; no new third-party calls beyond ESI/SSO unless the release log says so.
- Scope: one change, no unrelated reformatting or drive-by edits.
- HangarScope rules:
  - No new UI stacks (Electron, web frontends). Avalonia + MVVM (CommunityToolkit.Mvvm) only.
  - Design contract (design/README.md): Barlow Condensed / IBM Plex Mono, letter-spaced labels, no border radius,
    no shadows, 1px borders only, design-token colours and copy.
  - Lists bound to asset-scale data are virtualized; per-item aggregations in MainViewModel.Recompute() are
    precomputed into dictionaries, not nested LINQ scans.
  - Anything that writes to the game is an explicit button followed by an in-UI confirmation; fit sync saves the
    new copy before deleting the old one and only deletes after the save succeeded.
  - Fit merging, variants, search ranking and the tidy plan stay in the pure Services/FitCatalog.cs.
  - New ESI scopes are listed in the release log and README.
  - `dotnet build` has 0 warnings.

Report each finding as:
  <file>:<line> — <what's wrong> — <why it matters>
Only report real problems, not style preferences already consistent with the surrounding code.
If there is nothing to report, reply with exactly: NO FINDINGS
```

## Re-review message

```text
Round <K+1>. I've pushed fixes. New diff: `git diff origin/master...HEAD` (or pasted below).
Answers to findings I didn't change: <list>.
Check that your earlier findings are resolved, then review the whole diff again with the same checklist.
Reply NO FINDINGS if there is nothing left.
```

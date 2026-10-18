# Formats

## Release-log entry

Folder: `docs/changelog/YYYY-MM-DD-<short-name>/` (lower case, hyphenated). Start from `docs/changelog/TEMPLATE.md`:

```markdown
# <Change name>

**Date:** YYYY-MM-DD

## Description

One to three sentences: what changed and why.

## Before / After

| Before | After |
| --- | --- |
| ![before](before.png) | ![after](after.png) |

<!-- Nothing visible? Delete the table and describe the change under "Change" instead. -->

## Change

What is different now: files or modules added or changed, new settings or files under %APPDATA%\HangarScope, new
ESI endpoints or scopes ("none"), tests added, anything stubbed when testing.
```

Keep it short: a log entry, not documentation. Feature documentation lives in `README.md` and `docs/FEATURES.md`.

Index row at the **top** of the table in `docs/changelog/README.md` (newest first):

```markdown
| Date | Change | Summary |
| --- | --- | --- |
| 2026-10-17 | [Export to CSV](2026-10-17-export-csv/README.md) | Export button on ASSETS saves the filtered table as CSV |
```

Internal changes' summaries start with "Internal:".

## What's-new entry (visible changes only)

`docs/whats-new/features.json`, newest first. HangarScope has no in-app "What's New" yet, so this file feeds the
GitHub Release notes; keying by PR number lets an in-app panel show "new since you last looked" later.

```json
{
  "features": [
    {
      "pr": 42,
      "date": "2026-10-17",
      "title": "Export to CSV",
      "description": "The ASSETS screen now has an EXPORT button that saves the table you're looking at as a CSV file.\n\nThe export uses your current filters and sort order.",
      "images": [
        { "src": "img/pr-42-export-csv.png", "caption": "The EXPORT button above the asset table" }
      ]
    }
  ]
}
```

| Field | Rule |
| --- | --- |
| `pr` | Integer, unique: the PR's number. |
| `date` | `YYYY-MM-DD` |
| `title` | The feature's name. |
| `description` | 1–3 sentences of plain text (no HTML or Markdown), in the user's words: what it does and **where to find it** (screen and control names as they appear in the UI). `\n\n` starts a new paragraph. |
| `images` | Optional, ideally 1–2: `{ src, caption }`, `src` relative to `docs/whats-new/`. The caption says where in the app the screenshot is. |

Anything that renders this file must escape the text.

## PR body

`.github/pull_request_template.md` holds the template. Fill in:

- **What**: what changed and why, in plain language.
- **Screenshots**: before/after as
  `![before](https://github.com/apettey/hangarscope/blob/<sha>/docs/changelog/<entry>/before.png?raw=true)`. Pin to
  a commit SHA, not the branch name, so links survive the branch being deleted.
- **Checklist**: all ticked, with the review round count and the bump.

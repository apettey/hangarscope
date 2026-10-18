"""Tests for scripts/release_version.py. Run: python3 -m unittest discover -s scripts/tests"""

import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import release_version as rv  # noqa: E402

FEATURES = "docs/whats-new/features.json"


def features(*prs):
    return json.dumps({"features": [
        {"pr": n, "date": "2026-10-18", "title": f"Feature {n}", "description": f"Does thing {n}."} for n in prs
    ]})


class LatestTagTests(unittest.TestCase):
    def test_picks_highest_numerically(self):
        self.assertEqual(rv.latest_tag(["v1.2.0", "v1.10.0", "v1.9.3"]), "v1.10.0")

    def test_ignores_non_semver_tags(self):
        self.assertEqual(rv.latest_tag(["v2.0.0-rc.1", "release-3", "v1.2.0", "1.5.0"]), "v1.2.0")

    def test_empty_when_no_tags(self):
        self.assertEqual(rv.latest_tag([]), "")


class BumpTests(unittest.TestCase):
    def test_no_label_no_features_is_patch(self):
        r = rv.decide_bump("v1.2.0", ["HangarScope/Services/FitSync.cs"], [])
        self.assertEqual((r["bump"], r["version"]), ("patch", "1.2.1"))

    def test_docs_and_ci_only_is_patch(self):
        r = rv.decide_bump("v1.2.0", ["README.md", ".github/workflows/ci.yml"], [])
        self.assertEqual(r["bump"], "patch")

    def test_empty_change_is_still_patch(self):
        # No skip: even a merge with no file changes gets a version.
        self.assertEqual(rv.decide_bump("v1.2.0", [], [])["version"], "1.2.1")

    def test_new_features_entry_is_minor(self):
        r = rv.decide_bump("v1.2.0", [FEATURES], [], features(14), features(15, 14))
        self.assertEqual((r["bump"], r["version"]), ("minor", "1.3.0"))
        self.assertIn("#15", r["reason"])

    def test_first_features_entry_with_no_file_at_tag_is_minor(self):
        self.assertEqual(rv.decide_bump("v1.2.0", [FEATURES], [], None, features(15))["bump"], "minor")

    def test_editing_existing_entry_is_patch(self):
        r = rv.decide_bump("v1.2.0", [FEATURES], [], features(14), features(14))
        self.assertEqual(r["bump"], "patch")

    def test_features_path_without_contents_counts_as_minor(self):
        self.assertEqual(rv.decide_bump("v1.2.0", [FEATURES], [])["bump"], "minor")

    def test_windows_separators_match(self):
        self.assertEqual(rv.decide_bump("v1.2.0", ["docs\\whats-new\\features.json"], [])["bump"], "minor")

    def test_label_overrides_features(self):
        r = rv.decide_bump("v1.2.0", [FEATURES], ["semver:patch"], features(), features(15))
        self.assertEqual((r["bump"], r["reason"]), ("patch", "label semver:patch"))

    def test_minor_label_without_features(self):
        self.assertEqual(rv.decide_bump("v1.2.3", [], ["semver:minor"])["version"], "1.3.0")

    def test_biggest_label_wins(self):
        r = rv.decide_bump("v1.2.3", [], ["semver:patch", "semver:major", "semver:minor"])
        self.assertEqual((r["bump"], r["version"]), ("major", "2.0.0"))

    def test_major_only_by_label(self):
        r = rv.decide_bump("v1.2.3", [FEATURES, "HangarScope/App.axaml"], ["bug"], features(), features(1, 2, 3))
        self.assertEqual(r["bump"], "minor")

    def test_other_labels_ignored(self):
        self.assertEqual(rv.decide_bump("v1.2.3", [], ["deploy:manual", "semver:huge", "dependencies"])["bump"], "patch")

    def test_no_tag_starts_from_zero(self):
        self.assertEqual(rv.decide_bump("", [], [])["version"], "0.0.1")

    def test_bad_tag_raises(self):
        with self.assertRaises(ValueError):
            rv.decide_bump("1.2.0", [], [])

    def test_invalid_features_json_has_no_entries(self):
        self.assertEqual(rv.decide_bump("v1.0.0", [FEATURES], [], features(1), "{oops")["bump"], "patch")


class CombineTests(unittest.TestCase):
    def test_patch_label_on_one_pr_does_not_hide_another_prs_feature(self):
        a = rv.decide_bump("v1.2.0", ["README.md"], ["semver:patch"])
        b = rv.decide_bump("v1.2.0", [FEATURES], [], features(14), features(15, 14))
        r = rv.combine_bumps("v1.2.0", [(a["bump"], a["reason"]), (b["bump"], b["reason"])])
        self.assertEqual((r["bump"], r["version"]), ("minor", "1.3.0"))
        self.assertIn("#15", r["reason"])

    def test_biggest_wins_and_reasons_deduplicated(self):
        r = rv.combine_bumps("v1.2.3", [("patch", "x"), ("major", "label semver:major"), ("major", "label semver:major")])
        self.assertEqual((r["version"], r["reason"]), ("2.0.0", "label semver:major"))

    def test_no_merges_is_patch(self):
        self.assertEqual(rv.combine_bumps("v1.2.3", [])["version"], "1.2.4")

    def test_unknown_bump_raises(self):
        with self.assertRaises(ValueError):
            rv.combine_bumps("v1.2.3", [("huge", "?")])


class ValidateFeaturesTests(unittest.TestCase):
    def entry(self, **overrides):
        e = {"pr": 15, "date": "2026-10-18", "title": "T", "description": "D"}
        e.update(overrides)
        return e

    def check(self, *entries, root=None):
        return rv.validate_features(json.dumps({"features": list(entries)}), root)

    def test_empty_list_is_valid(self):
        self.assertEqual(rv.validate_features('{"features": []}'), [])

    def test_valid_entry_with_images(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "img").mkdir()
            (Path(d) / "img" / "pr-15-x.png").write_bytes(b"png")
            img = [{"src": "img/pr-15-x.png", "caption": "Top of ASSETS"}]
            self.assertEqual(self.check(self.entry(images=img), root=Path(d)), [])

    def test_invalid_json(self):
        self.assertIn("not valid JSON", rv.validate_features("{")[0])

    def test_missing_features_array(self):
        self.assertEqual(len(rv.validate_features('{"items": []}')), 1)
        self.assertEqual(len(rv.validate_features("[]")), 1)

    def test_duplicate_pr(self):
        errors = self.check(self.entry(), self.entry(title="Other"))
        self.assertEqual(errors, ["features[1]: pr 15 is used by an earlier entry"])

    def test_pr_must_be_positive_int(self):
        for bad in ("15", 0, -1, True, 1.5, None):
            self.assertEqual(len(self.check(self.entry(pr=bad))), 1, bad)

    def test_missing_required_fields(self):
        errors = self.check({"pr": 1})
        self.assertEqual(len(errors), 3)  # date, title, description

    def test_blank_title(self):
        self.assertEqual(len(self.check(self.entry(title="  "))), 1)

    def test_bad_dates(self):
        self.assertEqual(len(self.check(self.entry(date="18-10-2026"))), 1)
        self.assertEqual(self.check(self.entry(date="2026-02-30")), ["features[0]: date 2026-02-30 is not a real date"])

    def test_bad_images(self):
        self.assertEqual(len(self.check(self.entry(images="img/a.png"))), 1)
        self.assertEqual(len(self.check(self.entry(images=[{"caption": "c"}]))), 1)
        self.assertEqual(len(self.check(self.entry(images=[{"src": "img/a.png"}]))), 1)

    def test_missing_image_file(self):
        with tempfile.TemporaryDirectory() as d:
            errors = self.check(self.entry(images=[{"src": "img/nope.png", "caption": "c"}]), root=Path(d))
            self.assertEqual(len(errors), 1)
            self.assertIn("not found", errors[0])

    def test_repo_file_is_valid(self):
        path = Path(__file__).resolve().parents[2] / FEATURES
        self.assertEqual(rv.validate_features(path.read_text(encoding="utf-8"), path.parent), [])


class NotesTests(unittest.TestCase):
    ENTRY = "# Export to CSV\n\n**Date:** 2026-10-18\n\n## Description\n\nAdds export.\n\n" \
            "| Before | After |\n| --- | --- |\n| ![before](before.png) | ![after](after.png) |\n\n" \
            "See [the docs](https://example.com/docs) and [log](../README.md).\n"

    def notes(self, added, **kw):
        files = {"docs/changelog/2026-10-18-export-csv/README.md": self.ENTRY,
                 "docs/changelog/2026-10-17-fix/README.md": "# Fix crash\n\nFixed.\n"}
        return rv.build_notes("1.3.0", added, files.__getitem__, "apettey/hangarscope", "abc123", **kw)

    def test_entries_oldest_first_with_demoted_headings(self):
        md = self.notes(["docs/changelog/2026-10-18-export-csv/README.md",
                         "docs/changelog/2026-10-18-export-csv/after.png",
                         "docs/changelog/2026-10-17-fix/README.md",
                         "docs/changelog/README.md", "docs/changelog/TEMPLATE.md"])
        self.assertTrue(md.startswith("# HangarScope v1.3.0\n"))
        self.assertLess(md.index("### Fix crash"), md.index("### Export to CSV"))
        self.assertIn("#### Description", md)

    def test_relative_links_pinned_to_sha(self):
        md = self.notes(["docs/changelog/2026-10-18-export-csv/README.md"])
        base = "https://github.com/apettey/hangarscope/blob/abc123/docs/changelog/2026-10-18-export-csv/"
        self.assertIn(f"![before]({base}before.png?raw=true)", md)
        self.assertIn(f"[log]({base}../README.md)", md)
        self.assertIn("[the docs](https://example.com/docs)", md)

    def test_whats_new_section_only_for_new_entries(self):
        md = self.notes([], features_before=features(14), features_after=features(15, 14))
        self.assertIn("## What's new", md)
        self.assertIn("### Feature 15", md)
        self.assertNotIn("Feature 14", md)

    def test_no_entry_falls_back_to_commits(self):
        md = self.notes(["HangarScope/App.axaml.cs"], commits=["Bump Avalonia", ""])
        self.assertIn("No release-log entry", md)
        self.assertIn("- Bump Avalonia", md)
        self.assertNotIn("What's new", md)


class CliTests(unittest.TestCase):
    def run_cli(self, *argv, stdin=""):
        out = io.StringIO()
        old_stdin = sys.stdin
        sys.stdin = io.StringIO(stdin)
        try:
            with contextlib.redirect_stdout(out):
                code = rv.main(list(argv))
        finally:
            sys.stdin = old_stdin
        return code, out.getvalue()

    def test_latest_tag(self):
        self.assertEqual(self.run_cli("latest-tag", stdin="v1.0.0\nv1.2.0\nv1.1.9\n"), (0, "v1.2.0\n"))

    def test_bump_outputs_key_value_lines(self):
        with tempfile.TemporaryDirectory() as d:
            paths = Path(d) / "paths.txt"
            paths.write_text("README.md\n", encoding="utf-8")
            code, out = self.run_cli("bump", "--latest-tag", "v1.2.0", "--changed-paths-file", str(paths),
                                     "--labels", '["semver:minor","bug"]')
        self.assertEqual(code, 0)
        self.assertEqual(out, "bump=minor\nversion=1.3.0\nreason=label semver:minor\n")

    def test_bump_missing_features_before_file_means_none(self):
        with tempfile.TemporaryDirectory() as d:
            paths, after = Path(d) / "paths.txt", Path(d) / "after.json"
            paths.write_text(FEATURES + "\n", encoding="utf-8")
            after.write_text(features(15), encoding="utf-8")
            _, out = self.run_cli("bump", "--latest-tag", "v1.2.0", "--changed-paths-file", str(paths),
                                  "--features-before", str(Path(d) / "missing.json"), "--features-after", str(after))
        self.assertIn("bump=minor", out)

    def test_combine_reads_decisions_file(self):
        with tempfile.TemporaryDirectory() as d:
            f = Path(d) / "decisions.txt"
            f.write_text("patch label semver:patch\nminor new what's-new entry for PR #15\n", encoding="utf-8")
            code, out = self.run_cli("combine", "--latest-tag", "v1.2.0", "--decisions-file", str(f))
        self.assertEqual((code, out), (0, "bump=minor\nversion=1.3.0\nreason=new what's-new entry for PR #15\n"))

    def test_labels_comma_separated(self):
        self.assertEqual(rv._labels("semver:patch, semver:major"), ["semver:patch", " semver:major"])
        self.assertEqual(rv.decide_bump("v1.0.0", [], rv._labels("semver:patch, semver:major"))["bump"], "major")

    def test_validate_features_exit_codes(self):
        with tempfile.TemporaryDirectory() as d:
            good, bad = Path(d) / "good.json", Path(d) / "bad.json"
            good.write_text('{"features": []}', encoding="utf-8")
            bad.write_text('{"features": [{"pr": 1}]}', encoding="utf-8")
            self.assertEqual(self.run_cli("validate-features", str(good))[0], 0)
            code, out = self.run_cli("validate-features", str(bad))
        self.assertEqual(code, 1)
        self.assertEqual(out.count("::error"), 3)


if __name__ == "__main__":
    unittest.main()

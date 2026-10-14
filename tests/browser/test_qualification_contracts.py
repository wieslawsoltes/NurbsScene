"""Pure artifact-gate tests using synthetic files, not Uno/GPU acceptance.
Actual application campaigns remain mandatory in the workflow matrix.
"""
import copy
import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("uno_qualification", ROOT / "tools/uno-qualification.py")
GATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GATE)
COMMIT, TREE = "1" * 40, "2" * 40


class QualificationContracts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.site = self.root / "site"
        self.site.mkdir()
        (self.site / "index.html").write_text("<html>fixture</html>")
        (self.site / "app.wasm").write_bytes(b"\x00asm")
        (self.site / ".nojekyll").touch()
        self.manifest = GATE.create_site_manifest(self.site, COMMIT, TREE)
        self.evidence = self.root / "evidence"
        self.output = self.root / "result"
        self.registry = GATE.campaigns()
        self.names = [entry["name"] for entry in self.registry["campaigns"]]
        for entry in self.registry["campaigns"]:
            name = entry["name"]
            folder = self.evidence / name
            checks = ["fixture check " + str(i) for i in range(entry["minimumUiChecks"] - 1)] + ["Interaction test session restored: " + name]
            record = {"campaign": name, "status": "passed", "checks": checks,
                      "final": {"state": {"gpuError": None, "graphErrors": 0}}}
            report = {"status": "passed", "checks": checks, "campaigns": [record], "pageErrors": [],
                      "modelingChecksPerCampaign": [29]}
            modeling = {"application": "Uno Platform", "status": "passed", "pageErrors": [],
                        "checks": ["model fixture " + str(i) for i in range(29)], "lastGpuError": None,
                        "gpuBackend": "WebGL2 fixture", "frames": 1, "surfacePixels": 1,
                        "gpuReadbackSha256": "A" * 64}
            GATE.write_json(folder / "uno-ui.json", report)
            GATE.write_json(folder / ("uno-browser-" + name + ".json"), modeling)
            GATE.write_json(folder / "uno-publication.json", self.manifest)
            GATE.write_json(folder / "uno-console.json", {"errors": [], "console": []})

    def collect(self):
        return GATE.collect(self.evidence, self.site, COMMIT, TREE, self.output)

    def edit(self, filename, change, name="workspace"):
        path = self.evidence / name / filename
        value = GATE.read_json(path)
        change(value)
        GATE.write_json(path, value)

    def rejected(self):
        with self.assertRaises((ValueError, OSError)):
            self.collect()
        self.assertFalse(self.output.exists(), "A partial acceptance aggregate must not be published")

    def test_all_nine_campaigns_aggregate_without_losing_assertions(self):
        result = self.collect()
        self.assertEqual(len(result["checks"]), 291)
        self.assertEqual([r["campaign"] for r in result["campaigns"]], self.names)
        self.assertEqual(result["modelingChecksPerCampaign"], [29] * 9)
        self.assertEqual(len(list((self.output / "campaigns").glob("*/uno-ui.json"))), 9)
        self.assertEqual(GATE.read_json(self.output / "uno-publication.json"), self.manifest)

    def test_original_campaign_registry_remains_mandatory(self):
        self.assertEqual(self.names, ["workspace", "inputs-sliders", "panels", "panel-edges", "ellipse-loft", "panel-display", "typography", "pointer-selection", "panel-scrolling"])
        self.assertEqual(self.registry["minimumModelingChecks"], 29)

    def test_hidden_site_files_are_part_of_exact_content_verification(self):
        self.assertIn(".nojekyll", self.manifest["files"])
        (self.site / ".nojekyll").unlink()
        self.rejected()

    def test_changed_same_length_site_file_rejects(self):
        (self.site / "app.wasm").write_bytes(b"bad!")
        self.rejected()

    def test_added_site_file_rejects(self):
        (self.site / "unexpected.js").write_text("bad")
        self.rejected()

    def test_source_commit_and_tree_mismatch_reject(self):
        for commit, tree in [("3" * 40, TREE), (COMMIT, "4" * 40), ("main", TREE)]:
            with self.subTest(commit=commit, tree=tree), self.assertRaises(ValueError):
                GATE.verify_site(self.site, commit, tree)

    def test_publication_symlinks_reject(self):
        (self.site / "link").symlink_to(self.site / "index.html")
        self.rejected()

    def test_missing_campaign_cannot_create_aggregate(self):
        shutil.rmtree(self.evidence / "typography")
        self.rejected()

    def test_duplicate_campaign_rejects(self):
        shutil.copytree(self.evidence / "workspace", self.evidence / "duplicate")
        self.rejected()

    def test_unknown_campaign_rejects(self):
        self.edit("uno-ui.json", lambda r: r["campaigns"][0].update(campaign="unknown"))
        self.rejected()

    def test_cancelled_campaign_rejects(self):
        self.edit("uno-ui.json", lambda r: r.update(status="cancelled"))
        self.rejected()

    def test_failed_record_cannot_hide_under_passed_outer_report(self):
        self.edit("uno-ui.json", lambda r: r["campaigns"][0].update(status="failed"))
        self.rejected()

    def test_reduced_assertion_count_rejects(self):
        self.edit("uno-ui.json", lambda r: r.update(checks=r["checks"][1:]))
        self.rejected()

    def test_session_restoration_is_required(self):
        def remove_restore(report):
            report["checks"][-1] = "not restored"
            report["campaigns"][0]["checks"][-1] = "not restored"
        self.edit("uno-ui.json", remove_restore)
        self.rejected()

    def test_page_errors_are_not_ignored(self):
        self.edit("uno-ui.json", lambda r: r.update(pageErrors=["error"]))
        self.rejected()

    def test_console_page_errors_are_not_ignored(self):
        self.edit("uno-console.json", lambda r: r.update(errors=["error"]))
        self.rejected()

    def test_missing_native_graph_state_rejects(self):
        self.edit("uno-ui.json", lambda r: r["campaigns"][0]["final"].update(state={}))
        self.rejected()

    def test_different_campaign_publication_rejects(self):
        self.edit("uno-publication.json", lambda r: r.update(commit="3" * 40))
        self.rejected()

    def test_modeling_report_must_describe_actual_uno(self):
        self.edit("uno-browser-workspace.json", lambda r: r.update(application="offline JavaScript harness"))
        self.rejected()

    def test_framebuffer_evidence_is_required(self):
        self.edit("uno-browser-workspace.json", lambda r: r.update(surfacePixels=0))
        self.rejected()

    def test_inconsistent_modeling_counts_reject(self):
        self.edit("uno-ui.json", lambda r: r.update(modelingChecksPerCampaign=[0]))
        self.rejected()

    def test_combined_report_cannot_substitute_for_independent_shards(self):
        self.edit("uno-ui.json", lambda r: r["campaigns"].append(copy.deepcopy(r["campaigns"][0])))
        self.rejected()

    def test_evidence_symlinks_reject(self):
        (self.evidence / "link").symlink_to(self.evidence / "workspace", target_is_directory=True)
        self.rejected()

    def test_success_does_not_overwrite_prior_aggregate(self):
        self.collect()
        with self.assertRaises(ValueError):
            self.collect()
        self.assertEqual(GATE.read_json(self.output / "uno-ui.json")["status"], "passed")

    def test_recursive_aggregate_destination_rejects(self):
        with self.assertRaises(ValueError):
            GATE.collect(self.evidence, self.site, COMMIT, TREE, self.evidence / "workspace" / "result")
        self.assertFalse((self.evidence / "workspace" / "result").exists())

    def test_registry_rejects_duplicates_and_reduced_modeling_minimum(self):
        path = self.root / "registry.json"
        for registry in [self.registry | {"minimumModelingChecks": 0}, self.registry | {"campaigns": self.registry["campaigns"] * 2}]:
            GATE.write_json(path, registry)
            with self.assertRaises(ValueError):
                GATE.campaigns(path)


if __name__ == "__main__":
    unittest.main(verbosity=2)

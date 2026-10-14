"""Fail-closed source and all-campaign gate for the actual Uno publication.

The browser build is shared, not rebuilt separately per campaign. Every campaign
verifies every downloaded site byte and attaches that exact manifest to its
report. Only complete, consistent passing evidence for every registered campaign
can produce the aggregate consumed by the qualified-site upload. No network or
application mutation occurs in this utility.
"""
import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REGISTRY = ROOT / "tests/browser/uno-campaigns.json"
MANIFEST = "nurbsscene-qualification-source.json"


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def campaigns(path=REGISTRY):
    registry = read_json(path)
    if registry.get("schema") != 1 or registry.get("minimumModelingChecks", 0) < 29:
        raise ValueError("Invalid modeling acceptance registry")
    entries = registry.get("campaigns", [])
    names = [entry.get("name") for entry in entries]
    if not names or any(not isinstance(name, str) or not re.fullmatch(r"[a-z]+(?:-[a-z]+)*", name) for name in names):
        raise ValueError("Invalid campaign names")
    if len(names) != len(set(names)) or any(type(entry.get("minimumUiChecks")) is not int or entry["minimumUiChecks"] < 1 for entry in entries):
        raise ValueError("Duplicate campaigns or invalid assertion minimum")
    return registry


def revision(value):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{40}", value):
        raise ValueError("An exact Git commit/tree is required")
    return value


def site_files(root):
    root = Path(root)
    if root.is_symlink() or not root.is_dir():
        raise ValueError("A regular publication directory is required")
    result = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError("Publication symlinks are not permitted")
        if path.is_file() and path.relative_to(root).as_posix() != MANIFEST:
            result[path.relative_to(root).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    if "index.html" not in result or not any(name.endswith(".wasm") for name in result):
        raise ValueError("Missing actual Uno index/WASM publication")
    return result


def create_site_manifest(root, commit, tree):
    manifest = {"schema": 1, "application": "Uno Platform", "commit": revision(commit),
                "tree": revision(tree), "files": site_files(root)}
    write_json(Path(root) / MANIFEST, manifest)
    return manifest


def verify_site(root, commit, tree):
    root = Path(root)
    if (root / MANIFEST).is_symlink():
        raise ValueError("Unexpected source-manifest symlink")
    manifest = read_json(root / MANIFEST)
    expected = {"schema": 1, "application": "Uno Platform", "commit": revision(commit),
                "tree": revision(tree), "files": site_files(root)}
    if manifest != expected:
        raise ValueError("Publication bytes or exact source revision do not match the build manifest")
    return manifest


def check_strings(checks, minimum, label):
    if not isinstance(checks, list) or len(checks) < minimum or any(not isinstance(check, str) or not check for check in checks):
        raise ValueError("Missing or incomplete " + label)


def collect(evidence, site, commit, tree, output, registry_path=REGISTRY):
    manifest = verify_site(site, commit, tree)
    registry = campaigns(registry_path)
    required = {item["name"]: item for item in registry["campaigns"]}
    evidence = Path(evidence)
    if evidence.is_symlink() or not evidence.is_dir() or any(path.is_symlink() for path in evidence.rglob("*")):
        raise ValueError("A regular campaign-evidence tree is required")
    reports = {}
    for path in sorted(evidence.rglob("uno-ui.json")):
        report = read_json(path)
        records = report.get("campaigns", [])
        if report.get("status") != "passed" or report.get("pageErrors") != [] or len(records) != 1:
            raise ValueError("A campaign failed or did not produce a single completed report")
        record = records[0]
        name = record.get("campaign")
        if name not in required or name in reports:
            raise ValueError("Unexpected or duplicate campaign: " + str(name))
        checks = report.get("checks")
        check_strings(checks, required[name]["minimumUiChecks"], name + " UI assertions")
        if record.get("status") != "passed" or record.get("checks") != checks or checks[-1] != "Interaction test session restored: " + name:
            raise ValueError("Campaign assertions or session restoration are inconsistent: " + name)
        state = record.get("final", {}).get("state", {})
        if "gpuError" not in state or state["gpuError"] is not None or state.get("graphErrors") != 0:
            raise ValueError("Campaign ended with missing or erroneous native graph/GPU state: " + name)
        if read_json(path.parent / "uno-publication.json") != manifest:
            raise ValueError("Campaign tested a different publication: " + name)
        modeling = read_json(path.parent / ("uno-browser-" + name + ".json"))
        if modeling.get("application") != "Uno Platform" or modeling.get("status") != "passed" or modeling.get("pageErrors") != [] or "lastGpuError" not in modeling or modeling["lastGpuError"] is not None:
            raise ValueError("Actual Uno modeling/framebuffer qualification did not pass: " + name)
        if not isinstance(modeling.get("gpuBackend"), str) or not modeling["gpuBackend"] or type(modeling.get("frames")) is not int or modeling["frames"] < 1 or type(modeling.get("surfacePixels")) is not int or modeling["surfacePixels"] < 1 or not re.fullmatch(r"[0-9a-fA-F]{64}", modeling.get("gpuReadbackSha256", "")):
            raise ValueError("Missing actual native framebuffer evidence: " + name)
        check_strings(modeling.get("checks"), registry["minimumModelingChecks"], name + " modeling/framebuffer assertions")
        if report.get("modelingChecksPerCampaign") != [len(modeling["checks"])]:
            raise ValueError("Inconsistent modeling assertion count: " + name)
        console = read_json(path.parent / "uno-console.json")
        if console.get("errors") != []:
            raise ValueError("Campaign recorded browser page errors: " + name)
        reports[name] = (path.parent, record, modeling, console)
    if set(reports) != set(required):
        raise ValueError("Missing mandatory campaigns: " + ", ".join(sorted(set(required) - set(reports))))
    # Publish nothing until every independent report and source identity passed.
    output = Path(output)
    if output.resolve().is_relative_to(evidence.resolve()) or output.resolve().is_relative_to(Path(site).resolve()):
        raise ValueError("Aggregate output must be outside source evidence and publication")
    if output.exists() and any(output.iterdir()):
        raise ValueError("Aggregate output must be empty")
    output.mkdir(parents=True, exist_ok=True)
    ordered = [reports[name] for name in required]
    aggregate = {"status": "passed", "checks": [check for _, record, _, _ in ordered for check in record["checks"]],
                 "campaigns": [record for _, record, _, _ in ordered],
                 "modelingChecksPerCampaign": [len(modeling["checks"]) for _, _, modeling, _ in ordered], "pageErrors": []}
    for name, (folder, _, _, _) in reports.items():
        shutil.copytree(folder, output / "campaigns" / name)
    write_json(output / "uno-ui.json", aggregate)
    write_json(output / "uno-browser.json", ordered[0][2])
    write_json(output / "uno-console.json", {"errors": [], "console": [entry for _, _, _, console in ordered for entry in console.get("console", [])]})
    write_json(output / "uno-publication.json", manifest)
    return aggregate


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("matrix")
    for action in ("create", "verify", "collect"):
        item = sub.add_parser(action)
        item.add_argument("--site", type=Path, required=True)
        item.add_argument("--commit", required=True)
        item.add_argument("--tree", required=True)
        if action == "verify":
            item.add_argument("--evidence", type=Path, required=True)
        if action == "collect":
            item.add_argument("--evidence", type=Path, required=True)
            item.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "matrix":
        print(json.dumps([entry["name"] for entry in campaigns()["campaigns"]], separators=(",", ":")))
    elif args.command == "create":
        create_site_manifest(args.site, args.commit, args.tree)
    elif args.command == "verify":
        write_json(args.evidence / "uno-publication.json", verify_site(args.site, args.commit, args.tree))
    else:
        report = collect(args.evidence, args.site, args.commit, args.tree, args.output)
        print(json.dumps({"status": "passed", "campaigns": len(report["campaigns"]), "uiChecks": len(report["checks"])}))


if __name__ == "__main__":
    main()

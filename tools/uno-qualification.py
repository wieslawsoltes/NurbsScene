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
import math
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
    if registry.get("schema") != 1 or registry.get("minimumModelingChecks", 0) < 33:
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


def require_gpu_observation(observation, name):
    if (not isinstance(observation, dict) or observation.get("version") != 1
            or observation.get("installed") is not True
            or type(observation.get("deviceCount")) is not int or observation["deviceCount"] < 1
            or observation.get("errors") != []):
        raise ValueError("Missing or erroneous actual WebGPU device observation: " + name)


def require_capture_measurements(captures, name):
    if (not isinstance(captures, list) or len(captures) != 3
            or any(not isinstance(sample, dict) for sample in captures)
            or [sample.get("name") for sample in captures] != ["empty", "filled", "rotated"]):
        raise ValueError("Missing one-shot framebuffer capture measurements: " + name)
    for sample in captures:
        if sample.get("status") != "passed" or sample.get("phase") != "completed":
            raise ValueError("Framebuffer capture did not complete: " + name)
        if sample.get("budgetMilliseconds") != 60_000 or sample.get("outerBudgetMilliseconds") != 65_000:
            raise ValueError("Unexpected software qualification capture budget: " + name)
        count = sample.get("byteCount")
        elapsed = sample.get("elapsedMilliseconds")
        if type(count) is not int or count < 4 or count % 4:
            raise ValueError("Missing complete RGBA capture bytes: " + name)
        if type(elapsed) not in (int, float) or not math.isfinite(elapsed) or not 0 <= elapsed <= 65_000:
            raise ValueError("Missing or out-of-budget capture elapsed time: " + name)
    if len({sample["byteCount"] for sample in captures}) != 1:
        raise ValueError("Differential framebuffer captures have different dimensions: " + name)


def require_camera_reuse(performance, name):
    if (not isinstance(performance, dict) or performance.get("schema") != 1
            or performance.get("sceneUnchanged") is not True or performance.get("geometryReused") is not True):
        raise ValueError("Missing camera-only geometry reuse evidence: " + name)
    for stage in ("before", "after"):
        sample = performance.get(stage)
        if not isinstance(sample, dict):
            raise ValueError("Missing camera submission sample: " + name)
        for field in ("Frames", "GeometryUploadBytes", "GeometryUploadCount", "DrawCalls", "TriangleCount"):
            if type(sample.get(field)) is not int or sample[field] < 1:
                raise ValueError("Missing resident geometry or submitted mesh work: " + name + " " + field)
        if type(sample.get("LineCount")) is not int or sample["LineCount"] < 0:
            raise ValueError("Missing line submission count: " + name)
        timing = sample.get("CpuSubmissionMilliseconds")
        if type(timing) not in (int, float) or not math.isfinite(timing) or timing < 0:
            raise ValueError("Invalid CPU submission timing: " + name)
    before, after = performance["before"], performance["after"]
    if after["Frames"] <= before["Frames"]:
        raise ValueError("Camera sample did not submit another frame: " + name)
    if any(before[field] != after[field] for field in ("GeometryUploadBytes", "GeometryUploadCount")):
        raise ValueError("Camera-only motion uploaded geometry again: " + name)


def require_startup_checkpoint(folder, name):
    observation = read_json(folder / (name + ".json"))
    state = observation.get("state", {})
    console = observation.get("console", {})
    host = observation.get("host", {})
    canvas = host.get("canvas") or {}
    presentation = host.get("presentation") or {}
    if (observation.get("status") != "ready" or type(observation.get("sample")) is not int
            or observation["sample"] < 1 or state.get("objects") != 0 or state.get("nodes") != 0
            or type(state.get("frames")) is not int or state["frames"] < 1
            or "gpuError" not in state or state["gpuError"] is not None
            or not isinstance(state.get("history"), str) or console.get("text") != state["history"]):
        raise ValueError("Missing or invalid ordinary native startup observation: " + name)
    if (host.get("documentVisibility") != "visible" or host.get("loaderCount") != 0
            or canvas.get("id") != "uno-canvas" or canvas.get("connected") is not True
            or canvas.get("visible") is not True
            or any(type(canvas.get(key)) not in (int, float) or not math.isfinite(canvas[key]) or canvas[key] <= 0
                   for key in ("width", "height", "pixelWidth", "pixelHeight"))
            or type(presentation.get("sample")) is not int or presentation["sample"] < 1
            or observation["sample"] < presentation["sample"] + 2
            or any(type(presentation.get(key)) not in (int, float) or not math.isfinite(presentation[key])
                   for key in ("firstFrame", "secondFrame"))
            or presentation["firstFrame"] < 0 or presentation["secondFrame"] <= presentation["firstFrame"]):
        raise ValueError("Startup screenshot lacks a visible Uno host and fresh samples after presentation: " + name)
    if not (folder / (name + ".png")).read_bytes().startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError("Missing actual ordinary startup screenshot: " + name)
    return observation


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
        if modeling.get("gpuApi") != "WebGPU" or not isinstance(modeling.get("gpuBackend"), str) or not modeling["gpuBackend"].startswith("WebGPU") or type(modeling.get("frames")) is not int or modeling["frames"] < 1 or type(modeling.get("surfacePixels")) is not int or modeling["surfacePixels"] < 1 or not re.fullmatch(r"[0-9a-fA-F]{64}", modeling.get("gpuReadbackSha256", "")):
            raise ValueError("Missing actual native framebuffer evidence: " + name)
        require_capture_measurements(modeling.get("captureMeasurements"), name)
        require_camera_reuse(modeling.get("renderingPerformance"), name)
        require_gpu_observation(modeling.get("gpuObservation"), name + " modeling")
        require_gpu_observation(record.get("gpuObservation"), name + " final")
        if name == "rhino-workspace":
            require_gpu_observation(read_json(path.parent / "uno-rhino-pre-reload-gpu.json"), name + " before reload")
            startup = require_startup_checkpoint(path.parent, "uno-rhino-startup")
            startup_reload = require_startup_checkpoint(path.parent, "uno-rhino-startup-reload")
            if record.get("startup") != startup or modeling.get("startup", {}).get("state", {}).get("history") != startup["state"]["history"]:
                raise ValueError("Ordinary startup evidence differs from the campaign/modeling observation")
            if read_json(path.parent / "uno-rhino-workspace.json").get("startupReload") != startup_reload:
                raise ValueError("Reload startup evidence differs from its native workflow observation")
        if name == "rhino-grasshopper-ui":
            require_gpu_observation(read_json(path.parent / "uno-rhino-grasshopper-pre-reload-gpu.json"), name + " before reload")
        check_strings(modeling.get("checks"), registry["minimumModelingChecks"], name + " modeling/framebuffer assertions")
        if report.get("modelingChecksPerCampaign") != [len(modeling["checks"])]:
            raise ValueError("Inconsistent modeling assertion count: " + name)
        console = read_json(path.parent / "uno-console.json")
        if console.get("errors") != []:
            raise ValueError("Campaign recorded browser page errors: " + name)
        if console.get("gpuObservations") != [{"campaign": name, "state": record["gpuObservation"]}]:
            raise ValueError("Campaign WebGPU observation differs from its final evidence: " + name)
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
    write_json(output / "uno-console.json", {"errors": [], "console": [entry for _, _, _, console in ordered for entry in console.get("console", [])],
        "gpuObservations": [entry for _, _, _, console in ordered for entry in console["gpuObservations"]]})
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

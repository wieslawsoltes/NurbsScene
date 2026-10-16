"""Qualify the actual published Uno application in isolated input campaigns.
Each context loads the same artifact and runs the unchanged managed/framebuffer
checks. Every input scenario is mandatory; separate disposable sessions avoid a
single expanded campaign exhausting the bounded observation lifetime. No GPU
mocks, retry-to-pass, skip-on-unavailable or mutation hooks are used.
"""
import argparse
import asyncio
import json
import os
from pathlib import Path
from playwright.async_api import async_playwright
from ui_observation import wait_ready, wait_for_restored
from test_workspace_ui import verify_workspace_ui
from test_graph_inputs_ui import verify_graph_inputs_ui
from test_canvas_values_ui import verify_canvas_values_ui
from test_panel_layout_ui import verify_panel_layout_ui
from test_panel_edges_ui import verify_panel_edges_ui
from test_loft_ellipse_ui import verify_loft_ellipse_ui
from test_panel_display_ui import verify_panel_display_ui
from test_typography_ui import verify_typography_ui
from test_pointer_ui import verify_pointer_ui
from test_panel_scrolling_ui import verify_panel_scrolling_ui
from test_rhino_workspace import verify_rhino_workspace

ROOT = Path(__file__).resolve().parents[2]
EVIDENCE = ROOT / "evidence"

async def verify_panels(page):
    checks = await verify_canvas_values_ui(page)
    checks.extend(await verify_panel_layout_ui(page))
    return checks

async def main(url, campaign=None):
    EVIDENCE.mkdir(exist_ok=True)
    campaigns = (("workspace", verify_workspace_ui),
                 ("inputs-sliders", verify_graph_inputs_ui),
                 ("panels", verify_panels),
                 ("panel-edges", verify_panel_edges_ui),
                 ("ellipse-loft", verify_loft_ellipse_ui),
                 ("panel-display", verify_panel_display_ui),
                 ("typography", verify_typography_ui),
                 ("pointer-selection", verify_pointer_ui),
                 ("panel-scrolling", verify_panel_scrolling_ui),
                 ("rhino-workspace", verify_rhino_workspace))
    registered = [entry["name"] for entry in json.loads((ROOT / "tests/browser/uno-campaigns.json").read_text())["campaigns"]]
    if registered != [name for name, _ in campaigns]:
        raise ValueError("Native runner and mandatory campaign registry disagree")
    if campaign is not None:
        if campaign not in registered:
            raise ValueError("Unknown native campaign: " + campaign)
        campaigns = tuple(item for item in campaigns if item[0] == campaign)
    all_checks, reports, campaign_reports, errors, logs = [], [], [], [], []
    def write(name, data):
        (EVIDENCE / name).write_text(json.dumps(data, indent=2))
    async with async_playwright() as pw:
        options = {"headless": True}
        if os.getenv("CHROMIUM_PATH"):
            options["executable_path"] = os.environ["CHROMIUM_PATH"]
        browser = await pw.chromium.launch(**options)
        try:
            for name, verify in campaigns:
                context = await browser.new_context(viewport={"width": 1600, "height": 1100}, device_scale_factor=1)
                await context.grant_permissions(["clipboard-read", "clipboard-write"])
                page = await context.new_page()
                campaign_errors = []
                def on_error(error, campaign=name, found=campaign_errors):
                    found.append(str(error)); errors.append({"campaign": campaign, "error": str(error)})
                page.on("pageerror", on_error)
                page.on("console", lambda msg, campaign=name: logs.append({"campaign": campaign, "message": msg.type + ": " + msg.text}))
                record = {"campaign": name, "status": "running", "checks": []}
                campaign_reports.append(record)
                print("CAMPAIGN " + name, flush=True)
                try:
                    await page.goto(url + ("&" if "?" in url else "?") + "qualification=1", wait_until="domcontentloaded", timeout=60000)
                    await page.wait_for_function('globalThis.nurbsSceneQualification && globalThis.nurbsSceneQualification.status !== "running"', timeout=180000)
                    report = await page.evaluate("globalThis.nurbsSceneQualification")
                    report["pageErrors"] = list(campaign_errors)
                    write("uno-browser-" + name + ".json", report)
                    if report.get("application") != "Uno Platform" or report.get("status") != "passed" or campaign_errors:
                        raise AssertionError(json.dumps(report, indent=2))
                    reports.append(report)
                    if len(reports) == 1:
                        write("uno-browser.json", report)  # canonical modeling/framebuffer report
                    # Modeling qualification is published before the first UI
                    # observation. Every fresh context needs this explicit handoff.
                    ready = await wait_ready(page, timeout=20000)
                    record["firstSample"] = ready["sample"]
                    record["checks"] = await verify(page)
                    record["final"] = await page.evaluate("globalThis.nurbsSceneUi")
                    await page.evaluate("globalThis.nurbsSceneUiFinish=true")
                    await wait_for_restored(page)
                    record["checks"].append("Interaction test session restored: " + name)
                    if campaign_errors:
                        raise AssertionError(campaign_errors)
                    record["status"] = "passed"
                    all_checks.extend(record["checks"])
                    print(json.dumps({"campaign": name, "status": "passed", "checks": len(record["checks"])}), flush=True)
                except Exception as error:
                    record["status"] = "failed"; record["error"] = str(error)
                    try:
                        record["observed"] = await page.evaluate("globalThis.nurbsSceneUi")
                        await page.evaluate("globalThis.nurbsSceneUiFinish=true")
                    except Exception as observation_error:
                        record["observationError"] = str(observation_error)
                    raise
                finally:
                    write("uno-campaign-" + name + ".json", record)
                    try:
                        await page.screenshot(path=str(EVIDENCE / ("uno-campaign-" + name + ".png")))
                        await page.screenshot(path=str(EVIDENCE / "uno-browser.png"))
                    finally:
                        await context.close()
            write("uno-ui.json", {"status": "passed", "checks": all_checks, "campaigns": campaign_reports,
                "modelingChecksPerCampaign": [len(r["checks"]) for r in reports], "pageErrors": errors})
        except Exception as error:
            write("uno-ui.json", {"status": "failed", "error": str(error), "checks": all_checks, "campaigns": campaign_reports, "pageErrors": errors})
            raise
        finally:
            write("uno-console.json", {"errors": errors, "console": logs})
            await browser.close()
    print(json.dumps({"suite": "real-uno-pointer-keyboard", "passed": len(all_checks), "campaigns": len(campaign_reports)}), flush=True)

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="http://127.0.0.1:8766/")
    parser.add_argument("--campaign", help="Run one registered CI shard; omission retains the complete local campaign")
    args = parser.parse_args()
    asyncio.run(main(args.url, args.campaign))

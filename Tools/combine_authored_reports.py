"""Build a current comparison view from audited runs, retaining row provenance.

Inputs are applied in priority order: later runs replace matching conditions.
This combines aggregate views, not raw measurements or populations. Every row
keeps its original matched-baseline delta and source cell orders.
"""

import argparse
import hashlib
import html
import json
import os
from collections import defaultdict
from pathlib import Path

from production_report import KEYS, PAGE, content_note, json_for_script


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, allow_nan=False), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("runs", nargs="+", type=Path, help="Lowest to highest priority")
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    selected, selected_samples, sources = {}, {}, []
    snapshot = None
    metadata = None
    hardware = None
    for run in args.runs:
        audit = read_json(run / "audit.json")
        if not audit.get("passed"):
            raise SystemExit(f"Source audit failed: {run}")
        current_snapshot = read_json(run / "content-snapshot.json")
        current_meta = read_json(run / "metadata.json")
        current_hardware = tuple(
            current_meta[key] for key in ("gpu", "cpu", "api", "unity")
        )
        if snapshot is not None and (
            current_snapshot["catalogHash"] != snapshot["catalogHash"]
            or current_hardware != hardware
        ):
            raise SystemExit("Content or hardware differs between source runs.")
        snapshot, metadata, hardware = current_snapshot, current_meta, current_hardware
        summaries = [
            json.loads(line)
            for line in (run / "summary.jsonl")
            .read_text(encoding="utf-8-sig")
            .splitlines()
            if line.strip()
        ]
        camera_validated = read_json(run / "suite.json").get(
            "enforceSpreadFrustum", False
        )
        for original in read_json(run / "report/aggregated.json"):
            if original["workload"] == "Spread" and not camera_validated:
                continue
            key = tuple(original[field] for field in KEYS)
            population = [
                row for row in summaries if tuple(row[field] for field in KEYS) == key
            ]
            row = dict(original)
            row["sourceRun"] = run.name
            row["sourceBuildGuid"] = current_meta["buildGuid"]
            row["sourceOrders"] = [item["order"] for item in population]
            selected[key] = row
            selected_samples[key] = population
        sources.append(
            {
                "directory": run.name,
                "buildGuid": current_meta["buildGuid"],
                "spreadFrustumValidated": camera_validated,
                "audit": audit,
                "censusProtocol": read_json(run / "authored-metadata.json")[
                    "particleCensus"
                ],
                "report": Path(
                    os.path.relpath(run / "report/report.html", args.out)
                ).as_posix(),
                "rawFrames": Path(
                    os.path.relpath(run / "raw-frames.csv", args.out)
                ).as_posix(),
                "summarySha256": hashlib.sha256(
                    (run / "summary.jsonl").read_bytes()
                ).hexdigest(),
            }
        )
    rows = list(selected.values())
    groups = defaultdict(set)
    for row in rows:
        groups[tuple(row[field] for field in KEYS[1:])].add(row["technique"])
    if any(len(methods) != 12 for methods in groups.values()):
        raise SystemExit("The combined view is missing a technique/baseline condition.")
    samples = [row for population in selected_samples.values() for row in population]
    audit = {
        "passed": True,
        "kind": "audited-source-comparison-view",
        "cells": len(samples),
        "rawSamples": sum(row["samples"] for row in samples),
        "validGpuSamples": sum(row["validGpuSamples"] for row in samples),
        "conditions": len(groups),
        "aggregateRows": len(rows),
        "checks": [
            "Source audits",
            "Identical content/hardware",
            "Complete technique groups",
            "Row provenance",
            "Exclude Spread populations without corrected frustum validation",
        ],
        "errors": [],
    }
    metadata = dict(metadata)
    metadata.update(
        suite="authored-current-comparison-view", buildGuid="multiple; see source runs"
    )
    metadata["authored"] = True
    metadata["content"] = snapshot
    metadata["sourceRuns"] = sources
    links = " · ".join(
        '<a href="'
        + html.escape(source["report"])
        + '">'
        + html.escape(source["directory"])
        + "</a>"
        for source in sources
    )
    overview = (
        '<div class="note"><b>Current cross-run comparison view.</b> '
        "The corrected LineRenderer rows take priority. Other adapters retain their original audited rows. "
        "Each row preserves its source population and matched-baseline delta; no populations are pooled. "
        "The displayed baseline row is therefore not necessarily the baseline used for another row’s delta. "
        "The later census freezes native inputs outside sampling; source protocols/builds are retained. "
        "Spread rows require the corrected camera/frustum validation; earlier clipped rows are excluded. "
        "This page is a derived comparison, not a claim that a single player run measured this matrix. "
        "See sources.json and aggregated.json for exact source orders.<br><br>"
        + links
        + "</div>"
    )
    visuals = Path(__file__).resolve().parent.parent / "Screenshots/Authored"
    page = (
        PAGE.replace("__OVERVIEW__", overview)
        .replace(
            "__VISUAL_PREFIX__",
            json_for_script(Path(os.path.relpath(visuals, args.out)).as_posix() + "/"),
        )
        .replace("__DATA__", json_for_script(rows))
        .replace("__META__", json_for_script(metadata))
        .replace("__AUDIT__", json_for_script(audit))
        .replace("__CONTENT_NOTE__", content_note(snapshot))
    )
    (args.out / "report.html").write_text(page, encoding="utf-8")
    write_json(args.out / "aggregated.json", rows)
    write_json(args.out / "sources.json", sources)
    write_json(args.out / "audit.json", audit)
    print(json.dumps(audit, indent=2))
    print("Comparison:", (args.out / "report.html").resolve())


if __name__ == "__main__":
    main()

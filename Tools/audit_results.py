"""Audit the completed reference run against its raw timestamped samples."""

import argparse
import csv
import json
import math
from collections import Counter, defaultdict
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("run", type=Path)
    args = parser.parse_args()
    root = args.run
    errors = []
    checks = []

    def check(test, message):
        (checks if test else errors).append(message)

    check((root / "COMPLETE.txt").exists(), "Run has a completion marker")
    check(not (root / "INVALID.txt").exists(), "No invalid-cell marker")
    meta = json.loads((root / "metadata.json").read_text())
    check(
        meta["burstEnabled"]
        and not meta["developmentBuild"]
        and meta["frameTimingEnabled"],
        "Release player, Burst and frame timing enabled",
    )
    cells = [
        json.loads(line)
        for line in (root / "summary.jsonl").read_text().splitlines()
        if line.strip()
    ]
    check(
        len(cells) == 900 if meta["suite"] == "full" else len(cells) == 12,
        "Expected number of measured cells",
    )
    methods = {c["technique"] for c in cells}
    check(len(methods) == 12, "Eleven rendering configurations plus baseline")
    groups = defaultdict(list)
    for c in cells:
        groups[
            (
                c["workload"],
                c["count"],
                c["segments"],
                c["width"],
                c["height"],
                c["repetition"],
            )
        ].append(c)
    check(
        all(
            {r["technique"] for r in rs} == methods and len(rs) == 12
            for rs in groups.values()
        ),
        "Every configuration/repetition has all techniques and its own baseline",
    )
    keys = [
        (
            c["technique"],
            c["workload"],
            c["count"],
            c["segments"],
            c["width"],
            c["height"],
            c["repetition"],
        )
        for c in cells
    ]
    check(len(set(keys)) == len(cells), "No duplicate cells")
    if meta["suite"] == "full":
        reps = {c["repetition"] for c in cells}
        check(reps == {0, 1, 2}, "Three measured repetitions")
        check(
            {(c["width"], c["height"]) for c in cells} == {(1920, 1080), (2560, 1440)},
            "Actual 1080p and 1440p output resolutions",
        )
        check(
            {c["count"] for c in cells} == {1, 32, 128, 256, 512, 1024, 2500, 5000},
            "Expected line-count sweep",
        )
        check(
            {c["segments"] for c in cells} == {1, 8, 16, 32},
            "Expected geometry-density sweep",
        )
        check(
            {c["workload"] for c in cells} == {"Moving", "Static", "Churn", "Overdraw"},
            "All four workload classes",
        )
    raw = defaultdict(list)
    with (root / "raw-frames.csv").open(newline="") as f:
        for row in csv.DictReader(f):
            raw[int(row["cell"])].append(row)
    check(
        sum(len(r) for r in raw.values()) == sum(c["samples"] for c in cells),
        "Raw sample count equals reported sample count",
    )
    for c in cells:
        index = c["order"]
        rs = raw[index]
        if len(rs) != c["samples"]:
            errors.append(f"Cell {index}: wrong raw sample count")
        gpu = [float(r["gpu_ms"]) for r in rs if r["gpu_ms"]]
        if len(gpu) != c["validGpuSamples"] or len(gpu) < 20:
            errors.append(f"Cell {index}: invalid GPU sample accounting")
        stamps = [r["timing_timestamp"] for r in rs if r["gpu_ms"]]
        if len(stamps) != len(set(stamps)):
            errors.append(f"Cell {index}: duplicate GPU timestamps")
        if any(r["gpu_ms"] and r["fresh_timing"] != "1" for r in rs):
            errors.append(f"Cell {index}: stale timing used as GPU sample")
        if gpu:
            gpu.sort()
            med = gpu[round((len(gpu) - 1) * 0.5)]
            if abs(med - c["gpuMedianMs"]) > 0.000002:
                errors.append(f"Cell {index}: GPU median does not match raw data")
        if not all(
            math.isfinite(c[k]) and c[k] >= 0
            for k in ["gpuMedianMs", "commandMedianMs", "bridgeMedianMs"]
        ):
            errors.append(f"Cell {index}: invalid cost value")
        particle = c["technique"].startswith("Vfx") or c["technique"].startswith(
            "Particle"
        )
        expected = c["count"] * c["segments"] / (2 if c["workload"] == "Churn" else 1)
        if particle and c["observedParticles"] < expected:
            errors.append(f"Cell {index}: insufficient live particles")
        requested = c["technique"] in {"VfxPerLineBatched", "ParticleMeshInstanced"}
        if c["instancingRequested"] != requested or c["instancingEnabled"] != requested:
            errors.append(f"Cell {index}: wrong instancing configuration")
    check(
        not any("Cell " in e for e in errors),
        "All cell timing, particle and instancing checks passed",
    )
    result = {
        "passed": not errors,
        "cells": len(cells),
        "rawSamples": sum(map(len, raw.values())),
        "validGpuSamples": sum(c["validGpuSamples"] for c in cells),
        "buildGuid": meta.get("buildGuid"),
        "checks": checks,
        "errors": errors,
    }
    (root / "audit.json").write_text(json.dumps(result, indent=2))
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result["passed"] else 1)


if __name__ == "__main__":
    main()

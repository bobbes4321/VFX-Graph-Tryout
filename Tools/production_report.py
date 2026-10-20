"""Audit and report the complete ability experiment. Standard library only."""

import argparse, csv, html, json, math, statistics, os
from collections import defaultdict
from pathlib import Path
from report import NAMES


def med(a):
    a = [x for x in a if isinstance(x, (int, float)) and math.isfinite(x)]
    return statistics.median(a) if a else None


def main():
    p = argparse.ArgumentParser()
    p.add_argument("run", type=Path)
    p.add_argument("--out", type=Path, default=Path("BenchmarkResults/production"))
    a = p.parse_args()
    rows = [
        json.loads(s)
        for s in (a.run / "summary.jsonl").read_text().splitlines()
        if s.strip()
    ]
    meta = json.loads((a.run / "metadata.json").read_text())
    raw = defaultdict(list)
    with (a.run / "raw-frames.csv").open(newline="") as f:
        for r in csv.DictReader(f):
            raw[int(r["cell"])].append(r)
    errors = []
    if not (a.run / "COMPLETE.txt").exists():
        errors.append("Completion marker missing")
    if (a.run / "INVALID.txt").exists():
        errors.append("Runtime rejected a cell")
    expected = 12 if meta["suite"] == "production-smoke" else 360
    if len(rows) != expected:
        errors.append(f"Expected {expected} cells, found {len(rows)}")
    if (
        meta["developmentBuild"]
        or not meta["burstEnabled"]
        or not meta["frameTimingEnabled"]
    ):
        errors.append("Release/Burst/frame timing flags invalid")
    keys = ["technique", "workload", "count", "width", "height", "fullStack"]
    seen = set()
    groups = defaultdict(list)
    for r in rows:
        k = tuple(r[k] for k in keys) + (r["repetition"],)
        requested = r["technique"] in {"ParticleMeshInstanced", "VfxPerLineBatched"}
        if r["instancingRequested"] != requested or r["instancingEnabled"] != requested:
            errors.append(f"Instancing configuration mismatch {r['order']}")
        expected_quads = (
            0
            if r["technique"] == "Baseline"
            else r["count"] * (180 if r["fullStack"] else 96)
        )
        if r["allocatedQuads"] != expected_quads:
            errors.append(f"Allocation recipe mismatch {r['order']}")
        is_particle = r["technique"].startswith("Vfx") or r["technique"].startswith(
            "Particle"
        )
        if is_particle and r["observedParticles"] < expected_quads / 2:
            errors.append(f"Live particle count mismatch {r['order']}")
        if (r["width"], r["height"]) not in {(1920, 1080), (2560, 1440)}:
            errors.append(f"Unexpected output dimensions {r['order']}")
        if k in seen:
            errors.append(f"Duplicate cell {k}")
        seen.add(k)
        frames = raw[r["order"]]
        if len(frames) != r["samples"]:
            errors.append(f"Raw sample mismatch {r['order']}")
        gpu = [float(f["gpu_ms"]) for f in frames if f["gpu_ms"]]
        stamps = [f["timing_stamp"] for f in frames if f["gpu_ms"]]
        if len(set(stamps)) != len(stamps):
            errors.append(f"Duplicate GPU stamps {r['order']}")
        if len(gpu) != r["validGpuSamples"] or len(gpu) < 20:
            errors.append(f"GPU sample count {r['order']}")
        if gpu:
            gpu.sort()
            value = gpu[round((len(gpu) - 1) * 0.5)]
            if abs(value - r["gpuMedianMs"]) > 1e-5:
                errors.append(f"GPU median mismatch {r['order']}")
        for source, target in [
            ("bridge_ms", "bridgeP95Ms"),
            ("commands_ms", "commandP95Ms"),
        ]:
            xs = sorted(float(f[source]) for f in frames)
            r[target] = xs[round((len(xs) - 1) * 0.95)]
        r["bridgeMaxMs"] = max(float(f["bridge_ms"]) for f in frames)
        for key, value in list(r.items()):
            if isinstance(value, float) and not math.isfinite(value):
                r[key] = None
        groups[tuple(r[k] for k in keys)].append(r)
    aggregated = []
    for key, rs in groups.items():
        d = dict(zip(keys, key))
        d["name"] = NAMES[d["technique"]]
        d["runs"] = len(rs)
        d["composition"] = rs[0]["composition"]
        for field, value in rs[0].items():
            if field in keys or field in {"repetition", "order", "composition"}:
                continue
            if isinstance(value, (int, float)) or value is None:
                d[field] = med([r.get(field) for r in rs])
        for field in ["gpuMedianMs", "mainMedianMs", "bridgeMedianMs"]:
            valid = [r[field] for r in rs if r[field] is not None]
            d[field + "Min"] = min(valid) if valid else None
            d[field + "Max"] = max(valid) if valid else None
            diffs = []
            for r in rs:
                base = next(
                    (
                        b
                        for b in rows
                        if b["technique"] == "Baseline"
                        and b["repetition"] == r["repetition"]
                        and all(b[k] == r[k] for k in keys[1:])
                    ),
                    None,
                )
                if base and base[field] is not None and r[field] is not None:
                    diffs.append(r[field] - base[field])
            d[field + "Delta"] = med(diffs)
        d["bridgeMaxMs"] = max(r["bridgeMaxMs"] for r in rs)
        aggregated.append(d)
    for k, rs in groups.items():
        if len(rs) != (1 if expected == 12 else 3):
            errors.append(f"Repetition mismatch {k}")
    method_groups = defaultdict(set)
    for r in rows:
        method_groups[tuple(r[k] for k in keys[1:]) + (r["repetition"],)].add(
            r["technique"]
        )
    if any(len(x) != 12 for x in method_groups.values()):
        errors.append("Technique/baseline group incomplete")
    audit = {
        "passed": not errors,
        "cells": len(rows),
        "rawSamples": sum(map(len, raw.values())),
        "validGpuSamples": sum(r["validGpuSamples"] for r in rows),
        "buildGuid": meta["buildGuid"],
        "checks": [
            "Completion and cell matrix",
            "Release / Burst / frame timing flags",
            f"Unique cells and {1 if expected==12 else 3} repetitions",
            "Complete technique/baseline groups",
            "Actual output dimensions",
            "Allocated recipe and live particle counts",
            "Explicit instancing configuration",
            "Raw sample counts and unique fresh GPU timestamps",
            "GPU medians match raw frames",
        ],
        "errors": errors,
    }
    (a.run / "audit.json").write_text(json.dumps(audit, indent=2))
    if errors:
        raise SystemExit(json.dumps(audit, indent=2))
    a.out.mkdir(parents=True, exist_ok=True)
    (a.out / "aggregated.json").write_text(
        json.dumps(aggregated, indent=2, allow_nan=False)
    )
    with (a.out / "aggregated.csv").open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(aggregated[0]))
        w.writeheader()
        w.writerows(aggregated)

    def find(tech, count=256, width=1920, workload="Sustain"):
        return next(
            (
                d
                for d in aggregated
                if d["technique"] == tech
                and d["count"] == count
                and d["width"] == width
                and d["workload"] == workload
                and d["fullStack"]
            ),
            None,
        )

    shared = find("VfxBuffer")
    auto = find("VfxPerLineBatched", 512)
    unbatched = find("VfxPerLine", 512)
    billboard = find("ParticleBillboardShared", width=2560, workload="Overdraw")
    vfx1440 = find("VfxBuffer", width=2560, workload="Overdraw")
    overview = '<div class="note">Functional smoke preflight. Use the complete matrix before making performance decisions.</div>'
    if all(d is not None for d in [shared, auto, unbatched, billboard, vfx1440]):
        overview = f'<div class="note"><b>Measured implications.</b> Shared VFX and procedural rendering remain strong CPU choices for complete effects. At 256 sustained abilities / 1080p, shared VFX measured {shared["gpuMedianMsDelta"]:.3f} ms GPU delta and {shared["bridgeMedianMs"]:.3f} ms presentation bridge. At 512 complete abilities, per-ability VFX main-thread frames were {auto["mainMedianMs"]:.3f} ms with automatic batching and {unbatched["mainMedianMs"]:.3f} ms with batching disabled. Both are whole frames, not isolated setter costs.<br><br>At 256 abilities / 1440p / heavy overlap, shared Shuriken billboards recorded {billboard["gpuMedianMsDelta"]:.3f} ms GPU delta versus {vfx1440["gpuMedianMsDelta"]:.3f} ms for shared VFX, but their main-thread frames were {billboard["mainMedianMs"]:.3f} versus {vfx1440["mainMedianMs"]:.3f} ms. A small GPU saving can trade against a much larger CPU cost. Inspect the case tables and repeat ranges. See <a href="../../Documentation/ProductionFindings.md">the complete findings</a> for interpretation.</div>'
    visual_prefix = (
        Path(
            os.path.relpath(
                Path(__file__).resolve().parent.parent / "Screenshots/Production",
                a.out.resolve(),
            )
        ).as_posix()
        + "/"
    )
    page = (
        PAGE.replace("__OVERVIEW__", overview)
        .replace("__VISUAL_PREFIX__", json.dumps(visual_prefix))
        .replace("__DATA__", json.dumps(aggregated, allow_nan=False))
        .replace("__META__", json.dumps(meta))
        .replace("__AUDIT__", json.dumps(audit))
    )
    (a.out / "report.html").write_text(page, encoding="utf-8")
    print(json.dumps(audit, indent=2))
    print("Report:", (a.out / "report.html").resolve())


# Keep report markup, styles and browser code readable in a separate template.
PAGE = (
    Path(__file__).resolve().parent / "Templates" / "production_report.html"
).read_text(encoding="utf-8")
if __name__ == "__main__":
    main()

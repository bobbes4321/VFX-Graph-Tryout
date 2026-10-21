"""Audit and report the complete ability experiment. Standard library only."""

import argparse
import csv
import hashlib
import html
import json
import math
import os
import statistics
from collections import defaultdict
from pathlib import Path
from report import NAMES


def med(a):
    a = [x for x in a if isinstance(x, (int, float)) and math.isfinite(x)]
    return statistics.median(a) if a else None


KEYS = ["technique", "workload", "count", "width", "height", "fullStack"]
PARTICLE_METHODS = {name for name in NAMES if name.startswith(("Vfx", "Particle"))}
PER_ABILITY = {"VfxPerLine", "VfxPerLineBatched", "ParticleBillboardPerLine"}


def load_authored(run, errors):
    """Validate immutable recipes and the exact planned matrix before reading cells."""
    path = run / "authored-metadata.json"
    if not path.exists():
        return None, None, None
    authored = json.loads(path.read_text(encoding="utf-8-sig"))
    if authored.get("schemaVersion") != 1:
        errors.append("Unsupported authored metadata schema")
    documents = {}
    for filename, hash_field in (
        ("content-snapshot.json", "contentSnapshotSha256"),
        ("suite.json", "suiteSha256"),
    ):
        source = run / filename
        if not source.exists():
            errors.append(f"Missing authored manifest: {filename}")
            continue
        payload = source.read_bytes()
        if hashlib.sha256(payload).hexdigest() != authored.get(hash_field):
            errors.append(f"Manifest digest mismatch: {filename}")
        documents[filename] = json.loads(payload)
    snapshot = documents.get("content-snapshot.json")
    suite = documents.get("suite.json")
    if snapshot:
        required = {
            "schemaVersion",
            "catalogHash",
            "ribbonSlots",
            "segments",
            "layerSlots",
            "layerCapacities",
            "allocatedCorePerAbility",
            "allocatedFullPerAbility",
            "effects",
            "ribbons",
            "layers",
            "ramps",
            "effectKeys",
            "effectNames",
        }
        missing = required.difference(snapshot)
        if missing:
            errors.append(
                "Incomplete content snapshot; missing: " + ", ".join(sorted(missing))
            )
            return authored, None, suite
        if snapshot.get("schemaVersion") != 1:
            errors.append("Unsupported content snapshot schema")
        if snapshot.get("catalogHash") != authored.get("catalogHash"):
            errors.append("Content fingerprint mismatch")
        core = snapshot["ribbonSlots"] * snapshot["segments"]
        full = core + sum(snapshot["layerCapacities"])
        if (core, full) != (
            snapshot["allocatedCorePerAbility"],
            snapshot["allocatedFullPerAbility"],
        ):
            errors.append("Content allocation arithmetic mismatch")
        count = len(snapshot["effects"])
        if not count or len(snapshot["ribbons"]) != count * snapshot["ribbonSlots"]:
            errors.append("Content ribbon table dimensions mismatch")
        if len(snapshot["layers"]) != count * snapshot["layerSlots"]:
            errors.append("Content layer table dimensions mismatch")
        if (
            len(snapshot["effectKeys"]) != count
            or len(set(snapshot["effectKeys"])) != count
        ):
            errors.append("Content keys incomplete or duplicated")
        if len(snapshot["effectNames"]) != count or len(snapshot["ramps"]) % 32:
            errors.append("Content names or ramp layout mismatch")
        for slot, capacity in enumerate(snapshot["layerCapacities"]):
            largest = max(
                int(
                    snapshot["layers"][effect * snapshot["layerSlots"] + slot][
                        "Layout"
                    ]["y"]
                )
                for effect in range(count)
            )
            if largest != capacity:
                errors.append(f"Secondary slot capacity mismatch: {slot}")
    if suite:
        if suite.get("schemaVersion") != 1:
            errors.append("Unsupported suite schema")
        if len(suite["cells"]) != authored["expectedCells"]:
            errors.append("Suite cell count differs from metadata")
        if suite["repetitions"] != authored["repetitions"]:
            errors.append("Suite repetition metadata mismatch")
        if {cell["order"] for cell in suite["cells"]} != set(
            range(len(suite["cells"]))
        ):
            errors.append("Suite orders are incomplete or duplicated")
        identities = [
            tuple(cell[key] for key in KEYS) + (cell["repetition"],)
            for cell in suite["cells"]
        ]
        if len(identities) != len(set(identities)):
            errors.append("Suite contains duplicate planned cells")
    return authored, snapshot, suite


def audit_authored_cell(row, snapshot, authored, errors):
    order = row["order"]
    core = snapshot["allocatedCorePerAbility"]
    full = snapshot["allocatedFullPerAbility"]
    if row.get("catalogHash") != authored["catalogHash"]:
        errors.append(f"Cell content fingerprint mismatch {order}")
    if (row.get("allocatedCorePerAbility"), row.get("allocatedFullPerAbility")) != (
        core,
        full,
    ):
        errors.append(f"Cell allocation topology mismatch {order}")
    expected_quads = (
        0
        if row["technique"] == "Baseline"
        else row["count"] * (full if row["fullStack"] else core)
    )
    if (
        row["allocatedQuads"] != expected_quads
        or row["segments"] != snapshot["segments"]
    ):
        errors.append(f"Allocation recipe mismatch {order}")
    streams = [0] * (1 + (snapshot["layerSlots"] if row["fullStack"] else 0))
    if row["technique"] in PARTICLE_METHODS:
        owners = (
            row["activeAbilities"] if row["technique"] in PER_ABILITY else row["count"]
        )
        streams[0] = owners * core
        for slot in range(1, len(streams)):
            streams[slot] = owners * snapshot["layerCapacities"][slot - 1]
    if (
        row.get("expectedStreamParticles") != streams
        or row.get("observedStreamParticles") != streams
    ):
        errors.append(f"Exact stream particle census mismatch {order}")
    if row["observedParticles"] != sum(streams):
        errors.append(f"Total particle census mismatch {order}")
    population = row.get("activeEffectCounts", [])
    if (
        len(population) != len(snapshot["effects"])
        or any(value < 0 for value in population)
        or sum(population) != row["activeAbilities"]
        or not 0 <= row["activeAbilities"] <= row["count"]
    ):
        errors.append(f"Active recipe population mismatch {order}")
    logical = 0
    for effect, owners in enumerate(population):
        recipe = snapshot["effects"][effect]
        per_ability = int(recipe["Layout"]["x"]) * snapshot["segments"]
        if row["fullStack"]:
            for slot in range(int(recipe["Layout"]["y"])):
                per_ability += int(
                    snapshot["layers"][effect * snapshot["layerSlots"] + slot][
                        "Layout"
                    ]["y"]
                )
        logical += owners * per_ability
    if row["technique"] == "Baseline":
        logical = 0
    if row.get("logicalQuads") != logical:
        errors.append(f"Logical authored geometry mismatch {order}")


def content_note(snapshot):
    if snapshot is None:
        return (
            "Historical complete-effect study: three 32-segment ribbons, 48 travelling motes, "
            "24 ballistic sparks, eight endpoint coronas and four ground runes per ability. "
            "Retirement retains a 1.4-second release tail. Core-only keeps the ribbons."
        )
    names = ", ".join(snapshot["effectNames"])
    retirement = [recipe["Timing"]["z"] for recipe in snapshot["effects"]]
    return html.escape(
        f"Authored library: {names}. Baked topology: {snapshot['ribbonSlots']} ribbon slots × "
        f"{snapshot['segments']} segments, {snapshot['layerSlots']} reusable secondary slots. "
        f"Allocated geometry per ability: {snapshot['allocatedCorePerAbility']} core / "
        f"{snapshot['allocatedFullPerAbility']} complete quads. Recipes may use fewer slots; "
        f"unused shared slots stay allocated. Authored retirement ranges from {min(retirement):g} "
        f"to {max(retirement):g} seconds. Content fingerprint: {snapshot['catalogHash']}."
    )


def json_for_script(value):
    """An artist's asset name must not be able to close the page's script tag."""
    return json.dumps(value, allow_nan=False).replace("<", "\\u003c")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("run", type=Path)
    p.add_argument("--out", type=Path)
    p.add_argument(
        "--visuals", type=Path, help="Directory of captures for this measured build"
    )
    a = p.parse_args()
    rows = [
        json.loads(s)
        for s in (a.run / "summary.jsonl").read_text(encoding="utf-8-sig").splitlines()
        if s.strip()
    ]
    meta = json.loads((a.run / "metadata.json").read_text(encoding="utf-8-sig"))
    raw = defaultdict(list)
    with (a.run / "raw-frames.csv").open(newline="", encoding="utf-8-sig") as f:
        for r in csv.DictReader(f):
            raw[int(r["cell"])].append(r)
    errors = []
    authored, snapshot, suite = load_authored(a.run, errors)
    if authored and (snapshot is None or suite is None):
        (a.run / "audit.json").write_text(
            json.dumps({"passed": False, "errors": errors}, indent=2), encoding="utf-8"
        )
        raise SystemExit(
            "Required authored manifests are missing: " + "; ".join(errors)
        )
    if meta["suite"].startswith("authored-") and authored is None:
        errors.append("Authored run is missing its metadata and manifests")
    if a.out is None:
        a.out = a.run / "report"
    if not (a.run / "COMPLETE.txt").exists():
        errors.append("Completion marker missing")
    if (a.run / "INVALID.txt").exists():
        errors.append("Runtime rejected a cell")
    expected = (
        len(suite["cells"])
        if suite
        else (12 if meta["suite"] == "production-smoke" else 360)
    )
    repetitions = suite["repetitions"] if suite else (1 if expected == 12 else 3)
    if len(rows) != expected:
        errors.append(f"Expected {expected} cells, found {len(rows)}")
    if (
        meta["developmentBuild"]
        or not meta["burstEnabled"]
        or not meta["frameTimingEnabled"]
    ):
        errors.append("Release/Burst/frame timing flags invalid")
    keys = KEYS
    seen = set()
    groups = defaultdict(list)
    for r in rows:
        k = tuple(r[k] for k in keys) + (r["repetition"],)
        requested = r["technique"] in {"ParticleMeshInstanced", "VfxPerLineBatched"}
        if r["instancingRequested"] != requested or r["instancingEnabled"] != requested:
            errors.append(f"Instancing configuration mismatch {r['order']}")
        if authored:
            audit_authored_cell(r, snapshot, authored, errors)
        else:
            expected_quads = (
                0
                if r["technique"] == "Baseline"
                else r["count"] * (180 if r["fullStack"] else 96)
            )
            if r["allocatedQuads"] != expected_quads:
                errors.append(f"Allocation recipe mismatch {r['order']}")
            if (
                r["technique"] in PARTICLE_METHODS
                and r["observedParticles"] < expected_quads / 2
            ):
                errors.append(f"Live particle count mismatch {r['order']}")
        if (r["width"], r["height"]) not in {(1920, 1080), (2560, 1440)}:
            errors.append(f"Unexpected output dimensions {r['order']}")
        if suite:
            if suite.get("enforceSpreadFrustum") and r["workload"] == "Spread":
                if (
                    r.get("requiredEndpoints") != r["activeAbilities"] * 2
                    or r.get("visibleEndpoints") != r.get("requiredEndpoints")
                    or not (r.get("cameraFarClip", 0) > r.get("cameraNearClip", 0) > 0)
                ):
                    errors.append(f"Spread endpoint frustum mismatch {r['order']}")
            planned = next(
                (cell for cell in suite["cells"] if cell["order"] == r["order"]), None
            )
            if planned is None or any(
                planned[key] != r[key] for key in keys + ["repetition"]
            ):
                errors.append(f"Cell differs from immutable plan {r['order']}")
        if k in seen:
            errors.append(f"Duplicate cell {k}")
        seen.add(k)
        frames = raw[r["order"]]
        if r["samples"] != meta["measurementFrames"]:
            errors.append(f"Measurement duration mismatch {r['order']}")
        if len(frames) != r["samples"]:
            errors.append(f"Raw sample mismatch {r['order']}")
        gpu = [float(f["gpu_ms"]) for f in frames if f["gpu_ms"]]
        if any(
            f["fresh_timing"] != "1" or int(f["timing_stamp"]) <= 0
            for f in frames
            if f["gpu_ms"]
        ):
            errors.append(f"GPU sample marked stale {r['order']}")
        if any(not math.isfinite(value) or value <= 0 for value in gpu):
            errors.append(f"Nonpositive/nonfinite GPU timing {r['order']}")
        if any(
            not math.isfinite(float(f[column]))
            for f in frames
            for column in ("frame_ms", "commands_ms", "bridge_ms")
        ):
            errors.append(f"Nonfinite CPU/frame timing {r['order']}")
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
        if len(rs) != repetitions:
            errors.append(f"Repetition mismatch {k}")
    method_groups = defaultdict(set)
    for r in rows:
        method_groups[tuple(r[k] for k in keys[1:]) + (r["repetition"],)].add(
            r["technique"]
        )
    if suite:
        planned_groups = defaultdict(set)
        for cell in suite["cells"]:
            group = tuple(cell[key] for key in keys[1:]) + (cell["repetition"],)
            planned_groups[group].add(cell["technique"])
        if dict(method_groups) != dict(planned_groups):
            errors.append("Technique/baseline groups differ from immutable plan")
    elif any(len(x) != 12 for x in method_groups.values()):
        errors.append("Technique/baseline group incomplete")
    if set(raw) != {row["order"] for row in rows}:
        errors.append("Raw frame cells differ from summary cells")
    if suite and {row["order"] for row in rows} != {
        cell["order"] for cell in suite["cells"]
    }:
        errors.append("Completed orders differ from planned suite")
    audit = {
        "passed": not errors,
        "cells": len(rows),
        "rawSamples": sum(map(len, raw.values())),
        "validGpuSamples": sum(r["validGpuSamples"] for r in rows),
        "buildGuid": meta["buildGuid"],
        "checks": [
            "Completion and cell matrix",
            "Release / Burst / frame timing flags",
            f"Unique cells and {repetitions} repetitions",
            "Complete technique/baseline groups",
            "Actual output dimensions",
            "Allocated recipe and live particle counts",
            "Explicit instancing configuration",
            "Raw sample counts and unique fresh GPU timestamps",
            "GPU medians match raw frames",
        ],
        "errors": errors,
    }
    if authored:
        audit["catalogHash"] = authored["catalogHash"]
        audit["checks"].extend(
            [
                "Content and suite SHA-256 digests",
                "Every cell matches immutable suite plan",
                "Authored table dimensions and capacity arithmetic",
                "Exact per-stream particle census",
                "Active recipe populations and logical geometry",
            ]
        )
        if suite and suite.get("enforceSpreadFrustum"):
            audit["checks"].append("Spread endpoint frustum coverage")
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
    if not authored and all(
        d is not None for d in [shared, auto, unbatched, billboard, vfx1440]
    ):
        overview = (
            '<div class="note"><b>Measured implications.</b> '
            "Shared VFX and procedural rendering remain strong CPU choices for complete effects. "
            "At 256 sustained abilities / 1080p, shared VFX measured "
            f'{shared["gpuMedianMsDelta"]:.3f} ms GPU delta and '
            f'{shared["bridgeMedianMs"]:.3f} ms presentation bridge. '
            "At 512 complete abilities, per-ability VFX main-thread frames were "
            f'{auto["mainMedianMs"]:.3f} ms with automatic batching and '
            f'{unbatched["mainMedianMs"]:.3f} ms with batching disabled. '
            "Both are whole frames, not isolated setter costs.<br><br>"
            "At 256 abilities / 1440p / heavy overlap, shared Shuriken billboards recorded "
            f'{billboard["gpuMedianMsDelta"]:.3f} ms GPU delta versus '
            f'{vfx1440["gpuMedianMsDelta"]:.3f} ms for shared VFX, '
            f'but their main-thread frames were {billboard["mainMedianMs"]:.3f} '
            f'versus {vfx1440["mainMedianMs"]:.3f} ms. '
            "A small GPU saving can trade against a much larger CPU cost. "
            "Inspect the case tables and repeat ranges. See "
            '<a href="../../Documentation/ProductionFindings.md">the complete findings</a> '
            "for interpretation.</div>"
        )
    if authored:
        counts = sorted({row["count"] for row in rows})
        overview = (
            '<div class="note"><b>Authored content / scaling study.</b> '
            + html.escape(", ".join(snapshot["effectNames"]))
            + f'. Measured counts: {", ".join(map(str, counts))}. '
            + "Dense Sustain uses a fixed 36x19 m footprint; Spread uses a fixed wider 180x95 m footprint "
            + "and a wider camera. Lines are smaller on screen in Spread; these are separate density workloads. Compare both to examine "
            + "sensitivity to concentrated transparent overlap. Every particle stream is checked against "
            + "the baked recipe. Small GPU deltas remain sensitive to baseline noise.</div>"
        )
        meta["authored"] = authored
        meta["content"] = snapshot
    if (a.run / "derivation.json").exists():
        derivation = json.loads(
            (a.run / "derivation.json").read_text(encoding="utf-8-sig")
        )
        overview += (
            '<div class="note"><b>Derived completed-prefix excerpt.</b> '
            + html.escape(derivation["parent"])
            + " did not complete. This report independently audits only its completed condition groups. "
            + "The original failed run and sample provenance remain intact; see derivation.json.</div>"
        )
    if (a.run / "annotations.json").exists():
        annotation = json.loads(
            (a.run / "annotations.json").read_text(encoding="utf-8-sig")
        )
        overview += (
            '<div class="note"><b>Version note.</b> '
            + html.escape(annotation["note"])
            + "</div>"
        )
    visual_directory = a.visuals or (
        Path(__file__).resolve().parent.parent
        / ("Screenshots/Authored" if authored else "Screenshots/Production")
    )
    visual_prefix = (
        Path(
            os.path.relpath(
                visual_directory.resolve(),
                a.out.resolve(),
            )
        ).as_posix()
        + "/"
    )
    page = (
        PAGE.replace("__OVERVIEW__", overview)
        .replace("__VISUAL_PREFIX__", json_for_script(visual_prefix))
        .replace("__DATA__", json_for_script(aggregated))
        .replace("__META__", json_for_script(meta))
        .replace("__AUDIT__", json_for_script(audit))
        .replace("__CONTENT_NOTE__", content_note(snapshot))
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

"""Aggregate independent runs and emit a self-contained, offline benchmark explorer."""

import argparse
import csv
import html
import json
import math
import os
import statistics as st
from collections import defaultdict
from pathlib import Path

NAMES = {
    "Baseline": "No lines / baseline",
    "VfxBuffer": "VFX · shared buffer",
    "VfxPerLine": "VFX · per line / batching off",
    "VfxPerLineBatched": "VFX · per line / batching on",
    "LineRenderer": "Unity LineRenderer",
    "BurstMesh": "Burst · combined mesh",
    "Procedural": "GPU · procedural ribbons",
    "ParticleBillboardShared": "Shuriken · shared billboards",
    "ParticleBillboardPerLine": "Shuriken · per-line billboards",
    "ParticleMeshShared": "Shuriken · shared mesh",
    "ParticleMeshInstanced": "Shuriken · instanced mesh",
    "ParticleJobs": "Shuriken · Burst particle jobs",
}


def finite(x):
    return isinstance(x, (int, float)) and math.isfinite(x)


def median(xs):
    xs = [x for x in xs if finite(x)]
    return st.median(xs) if xs else None


def raw_statistics(source, rows):
    samples = defaultdict(lambda: {"bridge": [], "commands": []})
    trace_orders = {
        r["order"]: r["technique"]
        for r in rows
        if r["workload"] == "Churn"
        and r["count"] == 512
        and r["segments"] == 16
        and r["width"] == 1920
        and r["repetition"] == 0
    }
    traces = defaultdict(list)
    with source.open(newline="") as f:
        for row in csv.DictReader(f):
            order = int(row["cell"])
            bridge = float(row["bridge_ms"])
            commands = float(row["commands_ms"])
            samples[order]["bridge"].append(bridge)
            samples[order]["commands"].append(commands)
            if order in trace_orders:
                traces[trace_orders[order]].append(
                    {"bridge": bridge, "commands": commands, "frame": int(row["frame"])}
                )
    for row in rows:
        s = samples[row["order"]]
        for source_name, target in [("bridge", "bridge"), ("commands", "command")]:
            values = sorted(s[source_name])
            row[target + "P95Ms"] = (
                values[round((len(values) - 1) * 0.95)] if values else None
            )
            row[target + "MaxMs"] = max(values) if values else None
    return dict(traces)


def aggregate(source):
    rows = []
    for line in source.read_text(encoding="utf-8-sig").splitlines():
        if line.strip():
            # Unity JsonUtility serializes unsupported timings as NaN, preserve missing values.
            row = json.loads(line)
            for k, v in list(row.items()):
                if isinstance(v, float) and not math.isfinite(v):
                    row[k] = None
            rows.append(row)
    traces = raw_statistics(source.parent / "raw-frames.csv", rows)
    groups = defaultdict(list)
    keyfields = ["technique", "workload", "count", "segments", "width", "height"]
    for r in rows:
        groups[tuple(r[k] for k in keyfields)].append(r)
    out = []
    for key, rs in groups.items():
        item = dict(zip(keyfields, key))
        item["name"] = NAMES[item["technique"]]
        item["runs"] = len(rs)
        for k in rs[0]:
            if k in keyfields or k in ["repetition", "order"]:
                continue
            if isinstance(rs[0][k], bool):
                item[k] = all(r[k] for r in rs)
            elif isinstance(rs[0][k], (int, float)) or rs[0][k] is None:
                item[k] = median([r[k] for r in rs])
        for k in [
            "frameMedianMs",
            "gpuMedianMs",
            "bridgeMedianMs",
            "bridgeP95Ms",
            "bridgeMaxMs",
            "commandMaxMs",
            "mainMedianMs",
            "setupMs",
        ]:
            xs = [r[k] for r in rs if finite(r[k])]
            item[k + "Min"] = min(xs) if xs else None
            item[k + "Max"] = max(xs) if xs else None
        for metric in [
            "gpuMedianMs",
            "mainMedianMs",
            "renderMedianMs",
            "frameMedianMs",
            "bridgeMedianMs",
            "nativeMemoryBytes",
            "batchesMedian",
        ]:
            diffs = []
            for r in rs:
                base = next(
                    (
                        b
                        for b in rows
                        if b["technique"] == "Baseline"
                        and b["repetition"] == r["repetition"]
                        and all(b[k] == r[k] for k in keyfields[1:])
                    ),
                    None,
                )
                if base and finite(r.get(metric)) and finite(base.get(metric)):
                    diffs.append(r[metric] - base[metric])
            item[metric + "Delta"] = median(diffs)
            item[metric + "DeltaMin"] = min(diffs) if diffs else None
            item[metric + "DeltaMax"] = max(diffs) if diffs else None
        out.append(item)
    return (
        rows,
        sorted(
            out,
            key=lambda r: (
                r["width"],
                r["workload"],
                r["segments"],
                r["count"],
                r["technique"],
            ),
        ),
        traces,
    )


def main():
    p = argparse.ArgumentParser()
    p.add_argument("run", type=Path)
    p.add_argument("--out", type=Path, default=Path("BenchmarkResults"))
    p.add_argument("--gc-run", type=Path)
    p.add_argument("--legacy-run", type=Path)
    args = p.parse_args()
    raw, data, traces = aggregate(args.run / "summary.jsonl")
    meta = json.loads((args.run / "metadata.json").read_text())
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "aggregated.json").write_text(
        json.dumps(data, indent=2, allow_nan=False), encoding="utf-8"
    )
    with (args.out / "aggregated.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(data[0]))
        w.writeheader()
        w.writerows(data)
    allocations = ""
    if args.gc_run:
        probe = [
            json.loads(line)
            for line in (args.gc_run / "summary.jsonl").read_text().splitlines()
            if line.strip()
        ]
        probe_meta = json.loads((args.gc_run / "metadata.json").read_text())
        assert probe_meta[
            "developmentBuild"
        ], "Allocation probe must be an explicitly instrumented development build"
        probe_frames = defaultdict(list)
        with (args.gc_run / "raw-frames.csv").open(newline="") as f:
            for row in csv.DictReader(f):
                if row["gc_bytes"]:
                    probe_frames[int(row["cell"])].append(float(row["gc_bytes"]))
        baseline = next(r for r in probe if r["technique"] == "Baseline")
        base_gc = median(probe_frames[baseline["order"]][2:])
        body = []
        for r in probe:
            values = probe_frames[r["order"]][2:]
            mid = median(values)
            maximum = max(values) if values else None
            body.append(
                "<tr><td>"
                + html.escape(NAMES[r["technique"]])
                + "</td><td>"
                + str(r["count"])
                + "</td><td>"
                + r["workload"]
                + "</td><td>"
                + str(mid)
                + "</td><td>"
                + str(maximum)
                + "</td><td>"
                + str(mid - base_gc if mid is not None else None)
                + "</td><td>"
                + str(r["gcMaxBytes"])
                + "</td></tr>"
            )
        allocations = (
            '<h2>Allocation probe / instrumented development player</h2><p class="muted">A separate 256-line moving workload measures managed allocations. Its CPU/GPU timings are excluded from the release comparison. Counters cover the whole frame. The first two GC samples are excluded because delayed counters include pre-measurement validation-array allocations; the unfiltered raw maximum is retained for transparency. Pool creation is outside the sampled interval.</p><div class="scroll"><table><thead><tr><th>Technique</th><th>Lines</th><th>Workload</th><th>Steady GC median B/frame</th><th>Steady GC max B/frame</th><th>Median Δ baseline B</th><th>Unfiltered raw max B</th></tr></thead><tbody>'
            + "".join(body)
            + "</tbody></table></div>"
        )
    legacy_section = ""
    if args.legacy_run:
        _, legacy, _ = aggregate(args.legacy_run / "summary.jsonl")
        body = []
        for current in data:
            if (
                current["technique"]
                not in {
                    "LineRenderer",
                    "VfxBuffer",
                    "VfxPerLine",
                    "VfxPerLineBatched",
                    "Procedural",
                }
                or current["count"] not in {256, 512, 1024, 5000}
                or current["segments"] != 16
                or current["width"] != 1920
                or current["workload"] != "Moving"
            ):
                continue
            previous = next(
                r
                for r in legacy
                if all(
                    r[k] == current[k]
                    for k in [
                        "technique",
                        "count",
                        "segments",
                        "width",
                        "height",
                        "workload",
                    ]
                )
            )
            body.append(
                "<tr><td>"
                + html.escape(current["name"])
                + "</td><td>"
                + str(current["count"])
                + "</td><td>"
                + f"{previous['bridgeMedianMs']:.3f}"
                + "</td><td>"
                + f"{current['bridgeMedianMs']:.3f}"
                + "</td><td>"
                + f"{current['bridgeP95Ms']:.3f}"
                + "</td></tr>"
            )
        legacy_section = (
            '<h2>ECS preparation comparison / additional 900-cell run</h2><p class="muted">The retained first matrix sampled LineRenderer points/colors on the main thread and used string property setters. The reference matrix moves that LineRenderer preparation into Burst and caches numeric property IDs for VFX/procedural setters. Both use the same visual workload and three repetitions. The table compares measured bridge costs; final recommendations use the reference matrix.</p><div class="scroll"><table><thead><tr><th>Technique</th><th>Lines</th><th>Before bridge ms</th><th>Reference bridge ms</th><th>Reference bridge p95 ms</th></tr></thead><tbody>'
            + "".join(body)
            + "</tbody></table></div>"
        )
    visual_prefix = (
        Path(
            os.path.relpath(
                Path(__file__).resolve().parent.parent / "Screenshots" / "Backends",
                args.out.resolve(),
            )
        ).as_posix()
        + "/"
    )
    page = (
        PAGE.replace("__DATA__", json.dumps(data, allow_nan=False))
        .replace("__META__", json.dumps(meta))
        .replace("__TRACES__", json.dumps(traces))
        .replace("__ALLOCATIONS__", allocations + legacy_section)
        .replace("__VISUAL_PREFIX__", json.dumps(visual_prefix))
        .replace("__RAWPATH__", html.escape(str(args.run.resolve())))
    )
    (args.out / "report.html").write_text(page, encoding="utf-8")
    print(
        f'{len(raw)} measured cells; {len(data)} aggregate configurations; report: {(args.out/"report.html").resolve()}'
    )
    focus = [
        r
        for r in data
        if r["count"] == 256
        and r["segments"] == 16
        and r["width"] == 1920
        and r["workload"] == "Moving"
    ]
    for r in sorted(focus, key=lambda r: r.get("gpuMedianMs") or 1e9):
        print(
            f"{r['name']:38} gpu={r.get('gpuMedianMs')} delta={r.get('gpuMedianMsDelta')} main={r.get('mainMedianMs')} bridge={r.get('bridgeMedianMs')}"
        )


# Keep report markup, styles and browser code readable in a separate template.
PAGE = (Path(__file__).resolve().parent / "Templates" / "line_report.html").read_text(
    encoding="utf-8"
)

if __name__ == "__main__":
    main()

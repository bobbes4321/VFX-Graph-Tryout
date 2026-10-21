"""Retain complete condition groups from an interrupted authored run, with provenance.

This creates a clearly labeled derived dataset. It never completes or rewrites
the original run and never invents samples from the failed/incomplete cell.
Run production_report.py on the destination to audit it independently.
"""

import argparse
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    source, destination = args.source.resolve(), args.destination.resolve()
    if destination.exists():
        raise SystemExit("Destination must not exist.")
    suite = read_json(source / "suite.json")
    summary_lines = (
        (source / "summary.jsonl").read_text(encoding="utf-8-sig").splitlines()
    )
    rows = [json.loads(line) for line in summary_lines if line.strip()]
    if [row["order"] for row in rows] != list(range(len(rows))):
        raise SystemExit("Source is not an ordered completed prefix.")
    group_keys = ("workload", "count", "width", "height", "fullStack")
    first = suite["cells"][0]
    group_size = sum(
        all(cell[key] == first[key] for key in group_keys) for cell in suite["cells"]
    )
    limit = len(rows) // group_size * group_size
    if limit == 0:
        raise SystemExit("No complete condition group is available.")
    destination.mkdir(parents=True)
    for filename in (
        "content-snapshot.json",
        "source-manifest.json",
        "binary-manifest.json",
        "build-report.json",
    ):
        path = source / filename
        if path.exists():
            shutil.copy2(path, destination / filename)
    (destination / "summary.jsonl").write_text(
        "\n".join(summary_lines[:limit]) + "\n", encoding="utf-8"
    )
    with (source / "raw-frames.csv").open(encoding="utf-8-sig", newline="") as incoming:
        with (destination / "raw-frames.csv").open(
            "w", encoding="utf-8", newline=""
        ) as outgoing:
            outgoing.write(next(incoming))
            for line in incoming:
                if int(line.split(",", 1)[0]) < limit:
                    outgoing.write(line)
    suite["cells"] = suite["cells"][:limit]
    suite["suite"] += "-completed-prefix"
    write_json(destination / "suite.json", suite)
    metadata = read_json(source / "metadata.json")
    metadata["suite"] += "-completed-prefix"
    metadata["notes"] += (
        f" Derived excerpt of {limit} completed cells from {source.name}; "
        "the parent did not complete. Original timing samples and build identity are retained. "
        "See derivation.json for hashes and the parent failure."
    )
    write_json(destination / "metadata.json", metadata)
    authored = read_json(source / "authored-metadata.json")
    authored["expectedCells"] = limit
    authored["suiteSha256"] = digest(destination / "suite.json")
    write_json(destination / "authored-metadata.json", authored)
    write_json(
        destination / "derivation.json",
        {
            "kind": "completed-prefix-excerpt",
            "parent": source.name,
            "completedCells": limit,
            "originalCommandLine": metadata["commandLine"],
            "sourceHashes": {
                name: digest(source / name)
                for name in (
                    "suite.json",
                    "summary.jsonl",
                    "raw-frames.csv",
                    "content-snapshot.json",
                )
            },
            "parentFailure": (
                (source / "INVALID.txt").read_text(encoding="utf-8-sig")
                if (source / "INVALID.txt").exists()
                else None
            ),
        },
    )
    (destination / "COMPLETE.txt").write_text(
        "Derived excerpt completed " + datetime.now(timezone.utc).isoformat(),
        encoding="utf-8",
    )
    print(f"Extracted {limit} completed cells; independently audit {destination}")


if __name__ == "__main__":
    main()

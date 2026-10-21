"""Index audited authored-effect runs without changing any measured data."""

import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RESULTS = ROOT / "BenchmarkResults" / "authored"


def main():
    cards = []
    other = []
    for run in sorted(RESULTS.glob("run-*"), reverse=True):
        if not run.is_dir():
            continue
        metadata_path = run / "metadata.json"
        audit_path = run / "audit.json"
        metadata = (
            json.loads(metadata_path.read_text(encoding="utf-8-sig"))
            if metadata_path.exists()
            else {}
        )
        audit = (
            json.loads(audit_path.read_text(encoding="utf-8-sig"))
            if audit_path.exists()
            else {}
        )
        label = html.escape(metadata.get("suite", run.name))
        if audit.get("passed"):
            annotation_path = run / "annotations.json"
            annotation = (
                json.loads(annotation_path.read_text(encoding="utf-8-sig"))[
                    "note"
                ].strip()
                if annotation_path.exists()
                else ""
            )
            version_note = (
                f"<small>Version note: {html.escape(annotation)}</small>"
                if annotation
                else ""
            )
            cards.append(
                f'<a class="card" href="{run.name}/report/report.html">'
                f"<strong>{label}</strong>"
                f'<span>{audit["cells"]:,} cells · {audit["rawSamples"]:,} frames'
                f' · {audit["validGpuSamples"]:,} fresh GPU samples</span>'
                f"<small>{html.escape(run.name)} · Audit passed</small>"
                f"{version_note}</a>"
            )
        else:
            state = "Audit failed" if audit else "Not yet audited"
            other.append(f"<li>{html.escape(run.name)} — {state}</li>")
    template = (ROOT / "Tools/Templates/authored_index.html").read_text(
        encoding="utf-8"
    )
    if (RESULTS / "comparison/audit.json").exists():
        comparison = json.loads(
            (RESULTS / "comparison/audit.json").read_text(encoding="utf-8")
        )
        if comparison.get("passed"):
            cards.insert(
                0,
                '<a class="card" href="comparison/report.html">'
                "<strong>Consolidated measured comparison</strong>"
                f'<span>{comparison["conditions"]} conditions · all 12 configurations · corrected LineRenderer</span>'
                "<small>Cross-run view with source orders and matched-baseline provenance</small></a>",
            )
    page = template.replace("__REPORTS__", "\n".join(cards)).replace(
        "__OTHER_RUNS__", "\n".join(other) or "<li>None.</li>"
    )
    RESULTS.mkdir(parents=True, exist_ok=True)
    destination = RESULTS / "report.html"
    destination.write_text(page, encoding="utf-8")
    print("Report index:", destination)


if __name__ == "__main__":
    main()

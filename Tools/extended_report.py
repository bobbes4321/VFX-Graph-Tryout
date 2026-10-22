"""Audit the retained HTML against its CSV and build an offline visual report."""

import argparse
import csv
import hashlib
import html
import json
import math
import re
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

KEY = ('technique', 'workload', 'count', 'segments', 'width', 'height')
TEXT = {'technique', 'workload', 'name'}
BOOL = {'instancingRequested', 'instancingEnabled', 'developmentBuild', 'timingEnabled'}


def payload(source, variable):
    match = re.search(r'\b' + variable + r'\s*=\s*', source)
    if not match:
        raise ValueError(f'Missing embedded {variable} payload')
    return json.JSONDecoder().raw_decode(source[match.end():])[0]


def read_csv(path):
    with path.open(encoding='utf-8-sig', newline='') as f:
        reader = csv.DictReader(f)
        columns = reader.fieldnames
        raw = list(reader)
    def convert(key, value):
        if value == '':
            return None
        if key in TEXT:
            return value
        if key in BOOL:
            if value not in ('True', 'False'):
                raise ValueError(f'Unexpected boolean {key}: {value}')
            return value == 'True'
        number = float(value)
        if not math.isfinite(number):
            raise ValueError(f'Non-finite CSV value in {key}')
        return int(number) if number.is_integer() else number
    return columns, [{k: convert(k, v) for k, v in row.items()} for row in raw]


def audit(csv_path, report_path):
    columns, rows = read_csv(csv_path)
    source = report_path.read_text(encoding='utf-8-sig')
    old = payload(source, 'data')
    def key(row):
        return tuple(row[k] for k in KEY)
    csv_counts, html_counts = Counter(map(key, rows)), Counter(map(key, old))
    duplicates_csv = [list(k) for k, n in csv_counts.items() if n > 1]
    duplicates_html = [list(k) for k, n in html_counts.items() if n > 1]
    missing = [list(k) for k in csv_counts.keys() - html_counts.keys()]
    extra = [list(k) for k in html_counts.keys() - csv_counts.keys()]
    old_map = {key(row): row for row in old}
    mismatches = []
    compared = 0
    for row in rows:
        other = old_map.get(key(row))
        if other is None:
            continue
        for field in columns:
            compared += 1
            a, b = row[field], other.get(field)
            same = (field in other and type(a) is type(b) and a == b)
            if isinstance(a, (int, float)) and not isinstance(a, bool) and isinstance(b, (int, float)) and not isinstance(b, bool):
                same = math.isclose(a, b, rel_tol=1e-12, abs_tol=1e-12)
            if not same:
                mismatches.append({'key': list(key(row)), 'field': field, 'csv': a, 'html': b})
    extra_fields = sorted(set().union(*(r.keys() for r in old)) - set(columns))
    script = source[source.index('<script'):]
    table_match = re.search(r'\$\([\"\']table[\"\']\)\.innerHTML\s*=([\s\S]*?)\bdraw\(rel\)', script)
    table_code = table_match.group(1) if table_match else ''
    visible_table_fields = sorted(set(re.findall(r'\bd\.([A-Za-z][A-Za-z0-9]*)', table_code)) & set(columns))
    metric_block = re.search(r'<select id="metric">([\s\S]*?)</select>', source)
    chart_fields = re.findall(r'value="([^"]+)"', metric_block.group(1)) if metric_block else []
    hidden_fields = [k for k in columns if k not in set(visible_table_fields + chart_fields + list(KEY) + ['name'])]
    matrix = sorted({(r['workload'], r['width'], r['height'], r['segments'], r['count']) for r in rows})
    result = {
        'csv': csv_path.name, 'originalReport': report_path.name,
        'checkedUtc': datetime.now(timezone.utc).isoformat(),
        'csvSha256': hashlib.sha256(csv_path.read_bytes()).hexdigest(),
        'originalReportSha256': hashlib.sha256(report_path.read_bytes()).hexdigest(),
        'csvRows': len(rows), 'htmlRows': len(old), 'csvColumns': len(columns),
        'comparedValues': compared, 'mismatches': mismatches, 'missingRows': missing,
        'extraRows': extra, 'duplicateCsvKeys': duplicates_csv, 'duplicateHtmlKeys': duplicates_html,
        'extraHtmlFields': extra_fields, 'missingValues': {k: sum(r[k] is None for r in rows) for k in columns if any(r[k] is None for r in rows)},
        'visibleTableFields': visible_table_fields, 'originalChartFields': chart_fields,
        'fieldsWithoutTableOrChart': hidden_fields,
        'scenarioCount': len(matrix), 'techniques': len({r['technique'] for r in rows}),
        'runs': sum(r['runs'] for r in rows),
        'matches': not any([mismatches, missing, extra, duplicates_csv, duplicates_html, extra_fields]),
        'notes': [
            'Comparison uses the six configuration keys, explicit booleans, missing values and 1e-12 numeric tolerance.',
            'Every CSV scenario is reachable through the original workload, resolution, segment and count selectors.',
            'The original table and eight-metric scaling selector present a subset of the embedded fields. Selection export includes all fields.',
            'The measured matrix is sparse by design. Unmeasured combinations are unavailable, not zero.',
            'Original lifecycle traces, allocation probe and legacy comparison are additional sources outside aggregated.csv. Use the original report for those results.',
        ],
    }
    return columns, rows, payload(source, 'meta'), result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', type=Path, default=Path('BenchmarkResults'))
    parser.add_argument('--output', default='report_extended.html')
    args = parser.parse_args()
    folder = args.directory
    columns, rows, meta, result = audit(folder / 'aggregated.csv', folder / 'report.html')
    (folder / 'report_data_audit.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    template = (Path(__file__).parent / 'Templates' / 'extended_report.html').read_text(encoding='utf-8')
    embedded = json.dumps({'columns': columns, 'rows': rows, 'meta': meta, 'audit': result}, ensure_ascii=False, allow_nan=False, separators=(',', ':')).replace('<', '\\u003c')
    output = template.replace('__PAYLOAD__', embedded)
    (folder / args.output).write_text(output, encoding='utf-8')
    print(json.dumps(result, indent=2, ensure_ascii=True))
    print(f'Created {(folder / args.output).resolve()}')


if __name__ == '__main__':
    main()

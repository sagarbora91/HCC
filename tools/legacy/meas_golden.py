"""F12 MEAS legacy golden (tools/meas-legacy-golden.ps1): headline totals of the ported HCC study reports vs an independent reference.

Sub-commands (all JSON in / out, nothing written next to the workbooks; workbooks and legacy data never enter the repository):
  extract  <results.json> <map.json> <out.json> [--dry]   HCC side: metric per FY from report results (prototype run or server API dump)
  ref-workbooks <dir> <map.json> <out.json>               reference: the legacy workbooks (openpyxl, read-only, data_only)
  ref-dry  <facts.json> <out.json>                         reference in dry mode: recomputed here from the FY26 fixture's raw room lines
  compare  <hcc.json> <ref.json> <map.json> [--dry]        tolerances from the map; exit 1 on any mismatch (or, real mode, a missing metric)
Units in every JSON: money = paise, pct = points (31.9 = 31.9 %), nights / count = integers.
"""
import json
import re
import sys
from collections import defaultdict

FY_RE = re.compile(r"^\d{4}-\d{2}$")


def load(p):
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def save(p, o):
    with open(p, "w", encoding="utf-8") as f:
        json.dump(o, f, indent=1, sort_keys=True)


def fy_of(v):
    m = re.search(r"(\d{4}-\d{2})", str(v or ""))
    return m.group(1) if m else None


def extract(results_p, map_p, out_p, dry=False):
    mp = load(map_p)
    metrics = mp["dryMetrics" if dry else "metrics"]
    reports = {r["id"]: r for r in load(results_p)["reports"]}
    out = {}
    for m in metrics:
        h = m["hcc"]
        r = reports.get(h["report"])
        if not r or r.get("error"):
            out[m["metric"]] = {"missing": (r or {}).get("error", "report not registered")}
            continue
        vals = {}
        for sec in r["sections"]:
            if h.get("section") and not re.search(h["section"], sec.get("title") or "", re.I):
                continue
            col = next((c for c in sec["columns"] if re.search(h["column"], c["label"], re.I)), None)
            fyc = next((c for c in sec["columns"] if re.match(r"^FY\b", c["label"])), None)
            if not col or not fyc:
                continue
            for row in sec["rows"]:
                fy = fy_of(row["cells"].get(fyc["key"]))
                v = row["cells"].get(col["key"])
                if fy is None or not isinstance(v, (int, float)):
                    continue
                # a total row wins over detail rows of the same FY (the pack's monthly rows carry the FY too)
                if fy not in vals or row.get("kind") == "total":
                    vals[fy] = v
        out[m["metric"]] = vals if vals else {"missing": "no FY column / no matching column in " + h["report"]}
    save(out_p, out)
    print("extract: " + ", ".join(f"{k}={'missing' if 'missing' in v else len(v)}" for k, v in out.items()))


def ref_workbooks(d, map_p, out_p):
    import os
    import openpyxl  # read-only use; the integrator's PC has it

    out = {}
    for m in load(map_p)["metrics"]:
        w = m["workbook"]
        vals = {}
        wb = openpyxl.load_workbook(os.path.join(d, w["file"]), read_only=True, data_only=True)
        for sn in [w["sheet"]] if w.get("sheet") else wb.sheetnames:
            col = None
            for row in wb[sn].iter_rows(values_only=True):
                cells = list(row)
                if col is None:
                    if cells and isinstance(cells[0], str) and re.match(r"^FY\b", cells[0]):
                        col = next((i for i, c in enumerate(cells) if isinstance(c, str) and re.search(w["column"], c, re.I)), None)
                        if col is None:
                            break
                    continue
                fy = fy_of(cells[0]) if cells else None
                if fy and FY_RE.match(fy) and col < len(cells) and isinstance(cells[col], (int, float)):
                    v = cells[col]
                    vals[fy] = round(v * 100) if m["kind"] == "money" else v * 100 if m["kind"] == "pct" else v
            if vals:
                break
        wb.close()
        out[m["metric"]] = vals if vals else {"missing": f"{w['file']}: no '{w['column']}' column"}
    save(out_p, out)
    print("ref-workbooks: " + ", ".join(f"{k}={'missing' if 'missing' in v else len(v)}" for k, v in out.items()))


def ref_dry(facts_p, out_p):
    """Contract section 9.2, recomputed independently: occupancy = distinct room-nights / (rooms x days); ADR = revenue / charged nights."""
    import datetime as dt

    f = load(facts_p)
    fy, rooms = f["fy"], f["rooms"]
    d0, d1 = dt.date.fromisoformat(f["from"]), dt.date.fromisoformat(f["to"])
    days = (d1 - d0).days + 1
    slept = {(room, night) for room, night, _ in f["lines"]}
    charged = len(f["lines"])
    revenue = sum(a for _, _, a in f["lines"])
    cap = rooms * days
    occ = round(len(slept) * 1000 / cap) / 10 if cap else 0
    adr = round(revenue / charged) if charged else 0
    out = {"roomNights": {fy: len(slept)}, "chargedNights": {fy: charged}, "roomRevenue": {fy: revenue}, "occupancy": {fy: occ}, "adr": {fy: adr}}
    save(out_p, out)
    print(f"ref-dry: FY {fy}: {len(slept)} room-nights, {charged} charged, revenue {revenue} paise, occupancy {occ} %, ADR {adr} paise")


def compare(hcc_p, ref_p, map_p, dry=False):
    mp = load(map_p)
    tol = mp["tolerance"]
    kinds = {m["metric"]: m["kind"] for m in mp["dryMetrics" if dry else "metrics"]}
    hcc, ref = load(hcc_p), load(ref_p)
    bad, lines = 0, []
    for metric, kind in kinds.items():
        h, r = hcc.get(metric, {}), ref.get(metric, {})
        if "missing" in h or "missing" in r or not h or not r:
            bad += 1
            lines.append(f"MISSING {metric}: hcc={h.get('missing', 'none') if isinstance(h, dict) else h} ref={r.get('missing', 'none') if isinstance(r, dict) else r}")
            continue
        for fy in sorted(set(h) | set(r)):
            a, b = h.get(fy), r.get(fy)
            if a is None or b is None:
                bad += 1
                lines.append(f"MISMATCH {metric} {fy}: hcc={a} ref={b} (FY on one side only)")
                continue
            limit = abs(b) * tol["money"] if kind == "money" else tol[kind]
            ok = abs(a - b) <= limit + 1e-9
            if not ok:
                bad += 1
            lines.append(f"{'ok      ' if ok else 'MISMATCH'} {metric} {fy}: hcc={a} ref={b} diff={a - b:.4g} limit={limit:.4g}")
    print("\n".join(lines))
    print(f"golden: {'PASS' if bad == 0 else 'FAIL'} ({bad} problem(s), {len(lines)} line(s))")
    return 1 if bad else 0


if __name__ == "__main__":
    a = [x for x in sys.argv[1:] if x != "--dry"]
    dry = "--dry" in sys.argv
    cmd = a[0] if a else ""
    if cmd == "extract":
        extract(a[1], a[2], a[3], dry)
    elif cmd == "ref-workbooks":
        ref_workbooks(a[1], a[2], a[3])
    elif cmd == "ref-dry":
        ref_dry(a[1], a[2])
    elif cmd == "compare":
        sys.exit(compare(a[1], a[2], a[3], dry))
    else:
        print(__doc__)
        sys.exit(2)

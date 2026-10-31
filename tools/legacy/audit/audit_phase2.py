"""lg-audit phase 2: verify an lg-extract run folder against the audit's own raw TSVs.

Run:  python -I audit_phase2.py <raw-dir> <run-dir> <out-dir>
Writes <out-dir>/phase2.json (counts / pass-fail only) and phase2-ids.json (legacy ids only).
"""
import csv, json, os, re, sys
from collections import Counter, defaultdict
from decimal import Decimal

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import audit_phase1 as p1  # noqa: E402

csv.field_size_limit(10 ** 9)
ID_COLS = re.compile(r'id_?number|idnumber|passport|aadhaar', re.I)


def rcsv(path):
    with open(path, encoding='utf-8', newline='') as f:
        return list(csv.DictReader(f))


def main(raw, run, out):
    res, ids, fails = {}, defaultdict(dict), []
    data = {fy: {t: p1.rd(raw, fy, t) for t in ['CustomerInfo', 'RoomBooking', 'TransactionEnd', 'CustomerDeposit', 'CustomerRefund']} for fy in p1.FYS}
    files = {n: rcsv(os.path.join(run, n)) for n in os.listdir(run) if n.endswith('.csv')}
    res['files'] = {n: len(v) for n, v in files.items()}
    # no ID-number column in any CSV
    bad_cols = {n: [c for c in (v[0].keys() if v else []) if ID_COLS.search(c)] for n, v in files.items()}
    res['id_columns'] = {n: c for n, c in bad_cols.items() if c}
    if res['id_columns']: fails.append('id-number-like column present')

    def fyval(r):
        return r.get('legacy_fy')

    # stays
    st = files.get('lgc_stay.csv', [])
    bl = files.get('lgc_bill.csv', [])
    rc = files.get('lgc_receipt.csv', [])
    res['stay_columns'] = list(st[0].keys()) if st else []
    res['bill_columns'] = list(bl[0].keys()) if bl else []
    res['receipt_columns'] = list(rc[0].keys()) if rc else []
    kst = Counter((r['legacy_fy'], r['legacy_booking_id']) for r in st)
    kbl = Counter((r['legacy_fy'], r['legacy_bill_no']) for r in bl)
    res['dup_stay_keys'] = sum(1 for v in kst.values() if v > 1)
    res['dup_bill_keys'] = sum(1 for v in kbl.values() if v > 1)
    rkey = [k for k in ('legacy_fy', 'kind', 'legacy_row_id') if rc and k in rc[0]]
    krc = Counter(tuple(r[k] for k in rkey) for r in rc)
    res['receipt_key_cols'] = rkey
    res['dup_receipt_keys'] = sum(1 for v in krc.values() if v > 1)
    for k in ('dup_stay_keys', 'dup_bill_keys', 'dup_receipt_keys'):
        if res[k]: fails.append(k)
    stay_refs = {f"{r['legacy_fy']}|{r['legacy_booking_id']}" for r in st}
    bill_refs = {f"{r['legacy_fy']}|{r['legacy_bill_no']}" for r in bl}
    unres = Counter()
    for name, rows in files.items():
        if not name.startswith('lgc_'): continue
        for r in rows:
            if r.get('stay_ref') and r['stay_ref'] not in stay_refs: unres[name + ':stay_ref'] += 1
            if r.get('bill_ref') and r['bill_ref'] not in bill_refs: unres[name + ':bill_ref'] += 1
    res['unresolved_refs'] = dict(unres)
    if unres: fails.append('unresolved refs')

    # per-FY counts and paise vs source
    per = {}
    for fy in p1.FYS:
        lf = f'{fy[:4]}-{fy[5:]}'
        te = data[fy]['TransactionEnd']; rb = data[fy]['RoomBooking']
        sb = [r for r in bl if r['legacy_fy'] == lf]
        ss = [r for r in st if r['legacy_fy'] == lf]
        d = {'src_bills': len(te), 'out_bills': len(sb), 'src_bookings': len(rb), 'out_stays': len(ss),
             'out_stays_status': dict(Counter(r.get('status') for r in ss))}
        cmp = {}
        for col, src in [('net_paise', 'NetBill'), ('room_charge_paise', 'RoomCharg'), ('room_gst_paise', 'LuxuryTax'), ('fb_paise', 'FB'),
                         ('advance_paise', 'Advance'), ('laundry_paise', 'Laundry'), ('extra_charge_paise', 'ExtraCharge'), ('others_paise', 'Others'),
                         ('fb_gst_paise', 'FoodGSTRs')]:
            if sb and col in sb[0]:
                srcv = sum(p1.paise(p1.dec(r, src)) for r in te)
                outv = sum(int(r[col] or 0) for r in sb)
                cmp[col] = {'src': srcv, 'out': outv, 'diff': outv - srcv}
        if sb and 'discount_paise' in sb[0]:
            srcv = sum(p1.paise(p1.dec(r, 'DiscountAmt')) + p1.paise(p1.dec(r, 'BillDiscount')) for r in te)
            outv = sum(int(r['discount_paise'] or 0) for r in sb)
            cmp['discount_paise(DiscountAmt+BillDiscount)'] = {'src': srcv, 'out': outv, 'diff': outv - srcv}
        d['money'] = cmp
        sr = [r for r in rc if r['legacy_fy'] == lf]
        d['out_receipts_by_kind'] = dict(Counter(r.get('kind') for r in sr))
        d['out_receipts_paise_by_kind'] = {k: sum(int(r['amount_paise'] or 0) for r in sr if r.get('kind') == k) for k in d['out_receipts_by_kind']} if sr and 'amount_paise' in sr[0] else {}
        dep = data[fy]['CustomerDeposit']
        d['src_payment_paise'] = sum(p1.paise(p1.dec(r, 'DepositAmt')) for r in dep)
        d['src_advance_paise'] = sum(p1.paise(p1.dec(r, 'Advance')) for r in dep)
        d['src_security_paise'] = sum(p1.paise(p1.dec(r, 'SecurityDeposit')) for r in dep)
        d['src_refund_paise'] = sum(p1.paise(p1.dec(r, 'RefundAmt') + p1.dec(r, 'SecurityDepositRefund')) for r in data[fy]['CustomerRefund'])
        per[fy] = d
    res['per_fy'] = per

    # guest map: customer_key per §1 recomputed from raw
    latest = {r['GuestID']: r for r in data['2026-27']['CustomerInfo']}
    gm = files.get('lgc_guest_map.csv', [])
    res['guest_map_rows'] = len(gm)
    res['guest_map_columns'] = list(gm[0].keys()) if gm else []
    badk, missing = [], []
    for r in gm:
        g = latest.get(r.get('legacy_guest_id'))
        if g is None: missing.append(r.get('legacy_guest_id')); continue
        exp = p1.phone_key(g['PhoneNo']) or ('n:' + p1.fold(g['Name']))
        if r.get('customer_key') != exp: badk.append(r['legacy_guest_id'])
    res['guest_map_key_mismatch'] = len(badk); res['guest_map_not_in_latest'] = len(missing)
    res['guest_map_ids_missing'] = len(set(latest) - {r.get('legacy_guest_id') for r in gm})
    ids['guest_map_key_mismatch'] = badk[:200]; ids['guest_map_not_in_latest'] = missing[:200]
    if badk: fails.append('customer_key mismatch')
    # guests.csv
    gs = files.get('guests.csv', [])
    exp_keys = {p1.phone_key(g['PhoneNo']) for g in latest.values() if p1.phone_valid(g['PhoneNo'])}
    out_keys = Counter(p1.phone_key(r['phone']) for r in gs)
    res['guests_rows'] = len(gs); res['guests_expected'] = len(exp_keys)
    res['guests_dup_phone'] = sum(1 for v in out_keys.values() if v > 1)
    res['guests_missing_keys'] = len(exp_keys - set(out_keys)); res['guests_extra_keys'] = len(set(out_keys) - exp_keys)
    res['guests_columns'] = list(gs[0].keys()) if gs else []
    if res['guests_dup_phone'] or res['guests_missing_keys'] or res['guests_extra_keys']: fails.append('guests.csv phone keys')
    # higher GuestID wins the name
    win = {}
    for g in latest.values():
        if p1.phone_valid(g['PhoneNo']):
            k = p1.phone_key(g['PhoneNo'])
            if k not in win or int(g['GuestID']) > int(win[k]['GuestID']): win[k] = g
    namebad = [r['phone'][-4:] for r in gs if p1.phone_key(r['phone']) in win and p1.fold(r['name']) != p1.fold(win[p1.phone_key(r['phone'])]['Name'])]
    res['guests_name_not_from_highest_guestid'] = len(namebad)
    if namebad: fails.append('guests name winner')
    # Title Case: initials / M/S. preserved
    tc = Counter()
    for r in gs:
        n = r['name']
        for w in n.split():
            if re.fullmatch(r'([A-Za-z]\.){2,}', w) and w != w.upper(): tc['initials_lowered'] += 1
            if w.lower() == 'm/s.' and w != 'M/S.': tc['ms_changed'] += 1
    src_ms = sum(1 for g in latest.values() if re.search(r'\bm/s\.', g['Name'] or '', re.I))
    out_ms = sum(1 for r in gs if 'M/S.' in r['name'])
    res['title_case'] = dict(tc); res['src_names_with_MS'] = src_ms; res['guests_names_with_MS'] = out_ms
    if tc: fails.append('title case')
    # secret ids
    sp = os.path.join(run, 'secret', 'guest_ids.jsonl')
    sec = [json.loads(l) for l in open(sp, encoding='utf-8')] if os.path.exists(sp) else []
    sec_ids = {str(o['legacyGuestId']) for o in sec}
    exp_sec = {g['GuestID'] for g in latest.values() if g['IDNumber']}
    import hashlib
    hash_bad = 0
    for o in sec:
        g = latest.get(str(o['legacyGuestId']))
        n = re.sub(r'[^A-Z0-9]', '', str(o.get('idNumber', '')).upper())
        if g is None or hashlib.sha256(n.encode()).hexdigest() != g['IDNumber']: hash_bad += 1
    res['secret'] = {'rows': len(sec), 'expected_real_ids': len(exp_sec), 'missing': len(exp_sec - sec_ids), 'extra': len(sec_ids - exp_sec),
                     'idnumber_not_matching_source_hash': hash_bad, 'duplicate_ids': len(sec) - len(sec_ids),
                     'keys': sorted({k for o in sec for k in o})}
    ids['secret_missing'] = sorted(exp_sec - sec_ids, key=int)[:300]; ids['secret_extra'] = sorted(sec_ids - exp_sec, key=int)[:300]
    # de-dup vs measured
    tj = json.load(open(os.path.join(run, 'totals.json'), encoding='utf-8')) if os.path.exists(os.path.join(run, 'totals.json')) else {}
    res['totals_dedup'] = tj.get('dedup')
    res['out_stays_duplicate_total'] = sum(1 for r in st if r.get('status') == 'Duplicate')
    res['out_bills_total'] = len(bl); res['src_bills_total'] = sum(len(data[fy]['TransactionEnd']) for fy in p1.FYS)
    res['fails'] = fails
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, 'phase2.json'), 'w', encoding='utf-8') as fh: json.dump(res, fh, indent=1, default=str)
    with open(os.path.join(out, 'phase2-ids.json'), 'w', encoding='utf-8') as fh: json.dump(ids, fh, indent=1)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], sys.argv[3])

"""lg-audit phase 1: independent integrity checks over the raw TSVs written by Export-AuditRaw.ps1.

Run:  python -I audit_phase1.py <raw-dir> <out-dir>
Writes <out-dir>/phase1.json (counts only, safe to quote) and <out-dir>/phase1-ids.json
(legacy row ids for defects; ids only, never names/phones/ID numbers; stays outside git).
Stdlib only.
"""
import csv, json, re, sys, os
from collections import Counter, defaultdict
from decimal import Decimal, ROUND_HALF_UP

csv.field_size_limit(10 ** 9)
FYS = ['2017-18', '2018-19', '2019-20', '2020-21', '2021-22', '2022-23', '2023-24', '2024-25', '2025-26', '2026-27']


def rd(raw, fy, t):
    with open(os.path.join(raw, f'{fy}.{t}.tsv'), encoding='utf-8', newline='') as f:
        return list(csv.DictReader(f, delimiter='\t', quoting=csv.QUOTE_NONE))


def dec(r, c):
    v = r.get(c)
    return Decimal(v) if v not in (None, '') else Decimal(0)


def paise(x):  # contract §8: half away from zero on Decimal(str(x))
    q = (abs(x) * 100).quantize(Decimal(1), rounding=ROUND_HALF_UP)
    return int(q) if x >= 0 else -int(q)


def rupee(x):
    q = abs(x).quantize(Decimal(1), rounding=ROUND_HALF_UP)
    return int(q) if x >= 0 else -int(q)


def digits(p): return re.sub(r'\D', '', p or '')


def phone_key(p):
    d = digits(p)
    return d[-10:] if len(d) >= 10 else ''


def phone_valid(p):
    d = digits(p)
    ok = len(d) == 10 or (len(d) == 11 and d[0] == '0') or (len(d) == 12 and d[:2] == '91')
    return ok and d[-10] in '6789'


def fold(n):
    s = re.sub(r'[^a-z0-9 ]+', ' ', (n or '').strip().lower())
    return re.sub(r' +', ' ', s).strip()


def day(s): return (s or '')[:10]


def fy_of(datestr):
    if not datestr: return None
    y, m = int(datestr[:4]), int(datestr[5:7])
    s = y if m >= 4 else y - 1
    return f'{s}-{str(s + 1)[2:]}'


BILL_SUM_COLS = ['RoomCharg', 'ExtraCharge', 'FB', 'Laundry', 'Others', 'DiscountAmt', 'LuxuryTax', 'FoodGSTRs', 'Advance', 'NetBill',
                 'BillDiscount', 'VatAmount', 'HalfDayDiscount', 'BalanceDue']
PLUS = ['RoomCharg', 'ACCharge', 'ExtraCharge', 'LuxuryTax', 'ServiceCharges', 'FB', 'Telephone', 'Laundry', 'Others', 'Addition',
        'FoodGSTRs', 'LaunderyGSTRs', 'OtherGSTRs', 'ElectricalExpCharges', 'WaterDonation', 'Damages', 'ServiceChargeAddition']
MINUS = ['DiscountAmt', 'BillDiscount', 'DonationDiscount']


def classify_netbill(r):
    nb = dec(r, 'NetBill')
    base = sum(dec(r, k) for k in PLUS) - sum(dec(r, k) for k in MINUS)
    if abs(base - nb) <= 1: return 'ok'
    if abs(base + dec(r, 'VatAmount') - nb) <= 1: return 'vat_on_fb_added_pre_gst'
    if abs(base - dec(r, 'HalfDayDiscount') - nb) <= 1: return 'half_day_discount_deducted'
    if abs(base + dec(r, 'BillDiscount') - nb) <= 1: return 'bill_discount_not_deducted'
    if nb == 0: return 'netbill_zero'
    return 'unexplained'


def groups(keyf, rows):
    g = defaultdict(list)
    for r in rows:
        k = keyf(r)
        if k: g[k].append(r['GuestID'])
    dup = {k: v for k, v in g.items() if len(v) > 1}
    return {'distinct_keys': len(g), 'groups_gt1': len(dup), 'rows_in_groups': sum(len(v) for v in dup.values()),
            'surplus_rows': sum(len(v) - 1 for v in dup.values()), 'largest_group': max((len(v) for v in dup.values()), default=0)}, dup


def main(raw, out):
    res = {'fy': {}, 'customers_latest': {}, 'cross_file': {}, 'carry_forward': {}}
    ids = defaultdict(dict)
    data = {}
    for fy in FYS:
        data[fy] = {t: rd(raw, fy, t) for t in ['CustomerInfo', 'RoomBooking', 'TransactionEnd', 'CustomerDeposit', 'CustomerRefund']}
    latest = data['2026-27']['CustomerInfo']
    latest_ids = {r['GuestID'] for r in latest}

    for fy in FYS:
        d = data[fy]; te = d['TransactionEnd']; rb = d['RoomBooking']; dep = d['CustomerDeposit']; ref = d['CustomerRefund']
        f = {'CustomerInfo': len(d['CustomerInfo']), 'RoomBooking': len(rb), 'TransactionEnd': len(te)}
        bn = [int(r['BillNo']) for r in te if r['BillNo'] != '']
        f['BillNo_min'], f['BillNo_max'] = (min(bn), max(bn)) if bn else (None, None)
        f['BillNo_duplicates'] = sum(c - 1 for c in Counter(bn).values() if c > 1)
        f['DataFlag'] = dict(Counter(r['DataFlag'] for r in te))
        f['PaidFlag'] = dict(Counter(r['PaidFlag'] for r in te))
        f['sum_rupees'] = {c: str(sum(dec(r, c) for r in te).quantize(Decimal('0.01'))) for c in BILL_SUM_COLS}
        f['sum_paise_rowrounded'] = {c: sum(paise(dec(r, c)) for r in te) for c in BILL_SUM_COLS}
        f['CustomerDeposit'] = len(dep)
        for c in ['DepositAmt', 'Advance', 'SecurityDeposit']:
            f[f'dep_sum_{c}'] = str(sum(dec(r, c) for r in dep).quantize(Decimal('0.01')))
            f[f'dep_paise_{c}'] = sum(paise(dec(r, c)) for r in dep)
        f['dep_PaymentType'] = dict(Counter(r['PaymentType'] for r in dep))
        f['dep_sum_Advance_where_type_Advance'] = str(sum((dec(r, 'Advance') for r in dep if r['PaymentType'] == 'Advance'), Decimal(0)).quantize(Decimal('0.01')))
        f['CustomerRefund'] = len(ref)
        f['ref_sum_RefundAmt'] = str(sum((dec(r, 'RefundAmt') for r in ref), Decimal(0)).quantize(Decimal('0.01')))
        f['ref_sum_SecurityDepositRefund'] = str(sum((dec(r, 'SecurityDepositRefund') for r in ref), Decimal(0)).quantize(Decimal('0.01')))
        f['LuxuryTaxPer_slabs'] = dict(sorted(Counter(r['LuxuryTaxPer'] for r in te).items(), key=lambda kv: float(kv[0] or 0)))
        # bookings without bill / bills without booking (TransactionEnd.TransactionID = RoomBooking.BookingID)
        bids = {r['BookingID'] for r in rb}; tids = {r['TransactionID'] for r in te}
        nobill = sorted((r['BookingID'] for r in rb if r['BookingID'] not in tids), key=int)
        nobook = sorted((r['TransactionID'] for r in te if r['TransactionID'] not in bids), key=int)
        f['bookings_without_bill'] = len(nobill); f['bills_without_booking'] = len(nobook)
        ids[fy]['bookings_without_bill'] = nobill; ids[fy]['bills_without_booking'] = nobook
        f['bills_departure_outside_fy'] = sum(1 for r in te if fy_of(day(r['DepartureDate'])) != fy)
        cls = defaultdict(list)
        for r in te: cls[classify_netbill(r)].append(r['BillNo'])
        f['netbill_classes'] = {k: len(v) for k, v in cls.items()}
        f['netbill_bill_discount_not_deducted_dataflag'] = dict(Counter(r['DataFlag'] for r in te if classify_netbill(r) == 'bill_discount_not_deducted'))
        ids[fy]['netbill'] = {k: v for k, v in cls.items() if k != 'ok'}
        f['balance_check_net_minus_adv_eq_balance'] = sum(1 for r in te if abs(dec(r, 'NetBill') - dec(r, 'Advance') - dec(r, 'BalanceDue')) <= 1)
        own = {r['GuestID'] for r in d['CustomerInfo']}
        ref_g = {r['GuestID'] for r in rb}
        f['booking_guestids_distinct'] = len(ref_g)
        f['booking_guestids_missing_in_own_file'] = len(ref_g - own)
        miss_latest = sorted(ref_g - latest_ids, key=int)
        f['booking_guestids_missing_in_latest'] = len(miss_latest)
        f['bookings_with_guestid_missing_in_latest'] = sum(1 for r in rb if r['GuestID'] not in latest_ids)
        ids[fy]['booking_guestids_missing_in_latest'] = miss_latest
        dep_g = {r['CustomerCode'] for r in dep if r['CustomerCode'] not in ('', '0')}
        f['deposit_customercodes_missing_in_latest'] = len(dep_g - latest_ids)
        res['fy'][fy] = f

    c = res['customers_latest']
    c['rows'] = len(latest)
    c['GuestID_min'] = min(int(r['GuestID']) for r in latest); c['GuestID_max'] = max(int(r['GuestID']) for r in latest)
    c['GuestID_duplicate_rows'] = len(latest) - len(latest_ids)
    c['phone_valid'] = sum(phone_valid(r['PhoneNo']) for r in latest)
    c['phone_invalid'] = len(latest) - c['phone_valid']
    c['phone_key_nonempty_but_invalid'] = sum(1 for r in latest if not phone_valid(r['PhoneNo']) and phone_key(r['PhoneNo']))
    c['deleteflag'] = dict(Counter(r['deleteflag'] for r in latest))
    valid = [r for r in latest if phone_valid(r['PhoneNo'])]
    c['dup_phone_key_valid'], _ = groups(lambda r: phone_key(r['PhoneNo']), valid)
    c['expected_guest_rows_valid_phone'] = c['dup_phone_key_valid']['distinct_keys']
    c['dup_phone_key_any'], _ = groups(lambda r: phone_key(r['PhoneNo']), latest)
    c['dup_name_address'], gna = groups(lambda r: (fold(r['Name']) + '|' + fold(r['Address1'])) if fold(r['Name']) and fold(r['Address1']) not in ('', '0', 'na') else '', latest)
    c['dup_id_hash'], gid = groups(lambda r: r['IDNumber'], latest)
    c['real_id_rows'] = sum(1 for r in latest if r['IDNumber'])
    byid = {r['GuestID']: r for r in latest}
    c['id_hash_groups_spanning_phone_keys'] = sum(1 for v in gid.values() if len({phone_key(byid[g]['PhoneNo']) for g in v}) > 1)
    pk = {r['GuestID']: (phone_key(r['PhoneNo']) if phone_valid(r['PhoneNo']) else '') for r in latest}
    c['name_address_groups_not_merged_by_phone'] = sum(1 for v in gna.values() if len({pk[g] or ('x' + g) for g in v}) > 1)
    c['customer_key_distinct'] = len({(phone_key(r['PhoneNo']) or ('n:' + fold(r['Name']))) for r in latest})
    c['IDCode_with_real_id'] = dict(Counter(r['IDCode'] for r in latest if r['IDNumber']))

    prev = {r['GuestID']: r for r in data['2025-26']['CustomerInfo']}
    cur = {r['GuestID']: r for r in latest}
    common = set(prev) & set(cur)
    diff = sorted((g for g in common if fold(prev[g]['Name']) != fold(cur[g]['Name'])), key=int)
    res['carry_forward'] = {'prev_rows': len(prev), 'latest_rows': len(cur), 'common_ids': len(common),
                            'same_name_fold': len(common) - len(diff), 'different_name_fold': len(diff),
                            'prev_ids_missing_in_latest': len(set(prev) - set(cur)),
                            'same_phone_key': sum(1 for g in common if phone_key(prev[g]['PhoneNo']) == phone_key(cur[g]['PhoneNo'])),
                            'new_ids_in_latest': len(set(cur) - set(prev)),
                            'new_ids_min': min((int(g) for g in set(cur) - set(prev)), default=None)}
    ids['carry_forward']['different_name_fold'] = diff
    cfb = {}
    for a, b in zip(FYS, FYS[1:]):
        pa = {r['GuestID']: fold(r['Name']) for r in data[a]['CustomerInfo']}
        pb = {r['GuestID']: fold(r['Name']) for r in data[b]['CustomerInfo']}
        cm = set(pa) & set(pb)
        cfb[f'{a}->{b}'] = {'common': len(cm), 'same_name': sum(pa[g] == pb[g] for g in cm), 'missing_in_next': len(set(pa) - set(pb))}
    res['carry_forward']['all_boundaries'] = cfb

    def bk(r): return (day(r['DepartureDate']), r['RoomNo'].strip().upper(), rupee(dec(r, 'NetBill')))
    def ok_(r): return (day(r['FromDate']), r['RoomNo'].strip().upper(), r['GuestID'])
    def rk(r): return (day(r['Date']), rupee(dec(r, 'DepositAmt')), r['CustomerCode'])
    cf = {}
    for a, b in zip(FYS, FYS[1:]):
        A, B = data[a], data[b]
        bkeysB = {bk(r) for r in B['TransactionEnd']}
        bill_match = [r for r in A['TransactionEnd'] if bk(r) in bkeysB]
        bill_drop = [r for r in bill_match if fy_of(day(r['DepartureDate'])) == b]
        bkB = {ok_(r) for r in B['RoomBooking']}
        book_match = [r for r in A['RoomBooking'] if ok_(r) in bkB]
        tA = {r['TransactionID'] for r in A['TransactionEnd']}
        tB = {r['TransactionID'] for r in B['TransactionEnd']}
        idxB = defaultdict(list)
        for r in B['RoomBooking']: idxB[ok_(r)].append(r)
        keep_in = Counter()
        for r in book_match:
            inA = r['BookingID'] in tA; inB = any(x['BookingID'] in tB for x in idxB[ok_(r)])
            keep_in['earlier' if inA and not inB else 'later'] += 1
        rkB = {rk(r) for r in B['CustomerDeposit']}
        rec_match = [r for r in A['CustomerDeposit'] if rk(r) in rkB]
        cf[f'{a}->{b}'] = {'bill_key_matches': len(bill_match), 'bills_dropped_rule1': len(bill_drop),
                           'bills_dropped_netbill_rupees': str(sum((dec(r, 'NetBill') for r in bill_drop), Decimal(0)).quantize(Decimal('0.01'))),
                           'bills_dropped_departure_range': [min((day(r['DepartureDate']) for r in bill_drop), default=None),
                                                             max((day(r['DepartureDate']) for r in bill_drop), default=None)],
                           'booking_key_matches': len(book_match), 'booking_keep_in': dict(keep_in),
                           'receipt_key_matches': len(rec_match),
                           'receipt_keep_in_earlier_by_date_fy': sum(1 for r in rec_match if fy_of(day(r['Date'])) == a)}
        ids['cross_file'][f'{a}->{b}'] = {'bills_dropped': [r['BillNo'] for r in bill_drop], 'booking_matches': [r['BookingID'] for r in book_match],
                                          'receipt_matches': [r['DepositID'] for r in rec_match]}
    res['cross_file'] = cf
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, 'phase1.json'), 'w', encoding='utf-8') as fh: json.dump(res, fh, indent=1)
    with open(os.path.join(out, 'phase1-ids.json'), 'w', encoding='utf-8') as fh: json.dump(ids, fh, indent=1)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])

"""Builds Fixtures/run-golden/*.csv for the LG golden test. FICTIONAL data only (names, phones, GSTIN are made up).
Run:  python -I make_golden.py <output folder>      (stdlib only; money written in paise; IST -> UTC = -5:30)
The test (GoldenImportTests) does NOT trust this script: it holds the expected figures as literals worked out from the
tables below and rebuilds totals.json itself at run time."""
import csv, os, sys
from datetime import datetime, timedelta

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
P = 100  # rupees -> paise


def utc(d, hhmm):  # IST wall clock -> UTC string
    t = datetime.strptime(d + " " + hhmm, "%Y-%m-%d %H:%M") - timedelta(hours=5, minutes=30)
    return t.strftime("%Y-%m-%dT%H:%M:%SZ")


GUESTS = [(i, "Test Guest " + n, "90000000%02d" % i) for i, n in enumerate(
    ["One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve"], start=1)]
G = {g[0]: g for g in GUESTS}


def key(g):
    return G[g][2]


GSTIN = "27AAAAA0000A1Z5"  # fictional pattern; guests 3, 4, 5 share it (the company)

# stays: fy, id, guest, room, arrival, departure, nights, adults, children, meal, room_count, status, bill_no, source_tag
# multi-room stay = 105 (203 + 204); room shift = 106 (205 -> 301); duplicate booking = 2025-26/111 (real copy 2026-27/201)
S = [
    ("2025-26", 101, 1, "101", "2025-06-10", "2025-06-12", 2, 2, 0, "EP", 1, "CheckedOut", 1, None),
    ("2025-26", 102, 2, "102", "2025-06-20", "2025-06-21", 1, 1, 0, "CP", 1, "CheckedOut", 2, None),
    ("2025-26", 103, 3, "201", "2025-08-01", "2025-08-04", 3, 2, 0, "EP", 1, "CheckedOut", 3, None),
    ("2025-26", 104, 4, "202", "2025-08-15", "2025-08-17", 2, 2, 1, "CP", 1, "CheckedOut", 4, "OTA:AGODA"),
    ("2025-26", 105, 5, "203", "2025-10-05", "2025-10-08", 3, 4, 0, "MAP", 2, "CheckedOut", 5, None),
    ("2025-26", 106, 6, "205", "2025-11-02", "2025-11-04", 2, 2, 0, "EP", 2, "CheckedOut", 6, None),
    ("2025-26", 107, 7, "101", "2025-12-24", "2025-12-27", 3, 2, 0, "AP", 1, "CheckedOut", 7, None),
    ("2025-26", 108, 8, "102", "2026-01-10", "2026-01-11", 1, 1, 0, "EP", 1, "CheckedOut", 8, None),
    ("2025-26", 109, 9, "103", "2026-02-14", "2026-02-16", 2, 2, 0, "CP", 1, "CheckedOut", 9, None),
    ("2025-26", 110, 10, "104", "2026-03-30", "2026-04-01", 2, 2, 0, "EP", 1, "CheckedOut", 10, None),
    ("2025-26", 111, 11, "204", "2026-04-02", None, 2, 2, 0, "EP", 1, "Duplicate", None, None),
    ("2026-27", 201, 11, "204", "2026-04-02", "2026-04-04", 2, 2, 0, "EP", 1, "CheckedOut", 1, None),
    ("2026-27", 202, 12, "105", "2026-04-10", "2026-04-11", 1, 1, 0, "EP", 1, "CheckedOut", 2, None),
    ("2026-27", 203, 1, "101", "2026-05-01", "2026-05-03", 2, 2, 0, "CP", 1, "CheckedOut", 3, None),
    ("2026-27", 204, 3, "201", "2026-06-12", "2026-06-14", 2, 2, 0, "EP", 1, "CheckedOut", 4, None),
    ("2026-27", 205, 4, "202", "2026-07-20", "2026-07-23", 3, 2, 0, "MAP", 1, "CheckedOut", 5, "OTA:AGODA"),
    ("2026-27", 206, 7, "103", "2026-08-08", "2026-08-09", 1, 1, 0, "EP", 1, "CheckedOut", 6, None),
    ("2026-27", 207, 2, "102", "2026-09-15", "2026-09-18", 3, 2, 0, "CP", 1, "CheckedOut", 7, None),
    ("2026-27", 208, 8, "104", "2026-10-01", "2026-10-03", 2, 2, 0, "EP", 1, "CheckedOut", 8, None),
    ("2026-27", 209, 9, "105", "2026-10-20", "2026-10-22", 2, 2, 0, "EP", 1, "CheckedOut", 9, None),
]
# bills (rupees): fy, bill_no, stay, room, bill_date, nights, room, extra, fb, laundry, other, discount,
#                 room_gst_bps, room_gst, fb_gst, advance, data_flag, paid_flag, gstin, refund, out_of_fy
# net = room+extra+fb+laundry+other+room_gst+fb_gst-discount ; balance = net only for the one P/N bill (2025-26/7)
B = [
    ("2025-26", 1, 101, "101", "2025-06-12", 2, 4000, 0, 500, 0, 0, 0, 1200, 480, 0, 1000, "C", "Y", None, 0, 0),
    ("2025-26", 2, 102, "102", "2025-06-21", 1, 2000, 0, 0, 0, 0, 0, 1200, 240, 0, 0, "C", "Y", None, 0, 0),
    ("2025-26", 3, 103, "201", "2025-08-04", 3, 9000, 600, 1200, 0, 0, 500, 1200, 1080, 60, 2000, "C", "Y", GSTIN, 0, 0),
    ("2025-26", 4, 104, "202", "2025-08-17", 2, 6000, 0, 800, 200, 0, 0, 1200, 720, 40, 0, "C", "Y", GSTIN, 0, 0),
    ("2025-26", 5, 105, "203", "2025-10-08", 3, 15000, 1000, 3000, 0, 300, 1000, 1200, 1800, 150, 5000, "C", "Y", GSTIN, 0, 0),
    ("2025-26", 6, 106, "205", "2025-11-04", 2, 5000, 0, 0, 0, 0, 0, 1200, 600, 0, 0, "C", "Y", None, 0, 0),
    ("2025-26", 7, 107, "101", "2025-12-27", 3, 12000, 0, 2000, 0, 0, 0, 1200, 1440, 100, 0, "P", "N", None, 0, 0),
    ("2025-26", 8, 108, "102", "2026-01-11", 1, 2000, 0, 300, 0, 0, 0, 1200, 240, 15, 0, "C", "Y", None, 0, 0),
    ("2025-26", 9, 109, "103", "2026-02-16", 2, 4000, 0, 0, 0, 0, 200, 1200, 480, 0, 3000, "C", "Y", None, 500, 0),
    ("2025-26", 10, 110, "104", "2026-04-01", 2, 4400, 0, 400, 0, 0, 0, 1200, 528, 20, 0, "C", "Y", None, 0, 1),
    ("2026-27", 1, 201, "204", "2026-04-04", 2, 4000, 0, 0, 0, 0, 0, 500, 200, 0, 1000, "C", "Y", None, 0, 0),
    ("2026-27", 2, 202, "105", "2026-04-11", 1, 2500, 0, 200, 0, 0, 0, 500, 125, 10, 0, "C", "Y", None, 0, 0),
    ("2026-27", 3, 203, "101", "2026-05-03", 2, 4400, 200, 600, 0, 0, 0, 500, 220, 30, 2000, "C", "Y", None, 0, 0),
    ("2026-27", 4, 204, "201", "2026-06-14", 2, 5000, 0, 0, 0, 0, 0, 500, 250, 0, 0, "C", "Y", GSTIN, 0, 0),
    ("2026-27", 5, 205, "202", "2026-07-23", 3, 9000, 0, 1500, 0, 0, 500, 500, 450, 75, 0, "C", "Y", GSTIN, 0, 0),
    ("2026-27", 6, 206, "103", "2026-08-09", 1, 2200, 0, 0, 0, 0, 0, 500, 110, 0, 0, "C", "Y", None, 0, 0),
    ("2026-27", 7, 207, "102", "2026-09-18", 3, 6600, 0, 900, 0, 0, 0, 500, 330, 45, 0, "C", "Y", None, 300, 0),
    ("2026-27", 8, 208, "104", "2026-10-03", 2, 4400, 0, 0, 100, 0, 0, 500, 220, 0, 1500, "C", "Y", None, 0, 0),
    ("2026-27", 9, 209, "105", "2026-10-22", 2, 5000, 0, 500, 0, 0, 0, 500, 250, 25, 0, "C", "Y", None, 0, 0),
    ("2026-27", 10, None, "107", "2026-09-02", 1, 2500, 0, 0, 0, 0, 0, 500, 125, 0, 0, "C", "Y", None, 0, 0),
]
# receipts (rupees): fy, source, row_id, stay, bill_no, guest, date, kind, method, amount, reference, bank
R = [
    ("2025-26", "deposit", 1, 101, 1, 1, "2025-06-10", "advance", "CASH", 1000, None, None),
    ("2025-26", "deposit", 2, 101, 1, 1, "2025-06-12", "payment", "UPI", 4065, "UPI-AAA001", None),
    ("2025-26", "deposit", 3, 102, 2, 2, "2025-06-21", "payment", "CASH", 2240, None, None),
    ("2025-26", "deposit", 4, 103, 3, 3, "2025-08-01", "advance", "BANK", 2000, "CHQ-000001", "Fictional Bank"),
    ("2025-26", "deposit", 5, 103, 3, 3, "2025-08-04", "payment", "BANK", 10120, "NEFT-0001", "Fictional Bank"),
    ("2025-26", "deposit", 6, 104, 4, 4, "2025-08-17", "payment", "CARD", 7560, "CARD-0001", None),
    ("2025-26", "deposit", 7, 105, 5, 5, "2025-10-05", "advance", "BANK", 5000, "NEFT-0002", "Fictional Bank"),
    ("2025-26", "deposit", 8, 105, 5, 5, "2025-10-08", "payment", "BANK", 16950, "NEFT-0003", "Fictional Bank"),
    ("2025-26", "deposit", 9, 106, 6, 6, "2025-11-04", "payment", "CASH", 5600, None, None),
    ("2025-26", "deposit", 10, 108, 8, 8, "2026-01-11", "payment", "UPI", 2555, "UPI-AAA002", None),
    ("2025-26", "deposit", 11, 109, 9, 9, "2026-02-14", "advance", "UPI", 3000, "UPI-AAA003", None),
    ("2025-26", "deposit", 12, 109, 9, 9, "2026-02-16", "payment", "CASH", 1780, None, None),
    ("2025-26", "refund", 1, 109, 9, 9, "2026-02-16", "refund", "CASH", 500, None, None),
    ("2026-27", "deposit", 1, 201, 1, 11, "2026-04-02", "advance", "UPI", 1000, "UPI-BBB001", None),
    ("2026-27", "deposit", 2, 201, 1, 11, "2026-04-04", "payment", "CASH", 3200, None, None),
    ("2026-27", "deposit", 3, 202, 2, 12, "2026-04-11", "payment", "UPI", 2835, "UPI-BBB002", None),
    ("2026-27", "deposit", 4, 203, 3, 1, "2026-05-01", "advance", "CASH", 2000, None, None),
    ("2026-27", "deposit", 5, 203, 3, 1, "2026-05-03", "payment", "CARD", 3450, "CARD-0002", None),
    ("2026-27", "deposit", 6, 204, 4, 3, "2026-06-14", "payment", "BANK", 5250, "NEFT-0004", "Fictional Bank"),
    ("2026-27", "deposit", 7, 205, 5, 4, "2026-07-23", "payment", "BANK", 10525, "NEFT-0005", "Fictional Bank"),
    ("2026-27", "deposit", 8, 206, 6, 7, "2026-08-09", "payment", "CASH", 2310, None, None),
    ("2026-27", "deposit", 9, 207, 7, 2, "2026-09-18", "payment", "UPI", 7575, "UPI-BBB003", None),
    ("2026-27", "deposit", 10, 208, 8, 8, "2026-10-01", "security", "CASH", 1000, None, None),
    ("2026-27", "refund", 1, 207, 7, 2, "2026-09-18", "refund", "CASH", 300, None, None),
]


def write(name, header, rows):
    with open(os.path.join(out, name), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(header)
        for r in rows:
            w.writerow(["" if v is None else v for v in r])


write("guests.csv", ["name", "phone", "email", "address", "nationality", "company", "gstin", "notes"],
      [(n, p, "", "1 Fictional Road, Testville", "Indian", "Fictional Traders Pvt Ltd" if i in (3, 4, 5) else "",
        GSTIN if i in (3, 4, 5) else "", "Legacy GuestID %d" % i) for i, n, p in GUESTS])
write("companies.csv", ["name", "gstin", "contact", "phone", "email", "city", "creditLimit", "negotiatedRates"],
      [("Fictional Traders Pvt Ltd", GSTIN, "", "", "", "Testville", 0, "")])
write("lgc_guest_map.csv", ["legacy_guest_id", "guest_id", "customer_key", "phone_key", "phone_valid", "name_snapshot", "name_fold", "deleted", "source_fy"],
      [(i, "", p, p, 1, n, n.lower(), 0, "2026-27") for i, n, p in GUESTS])
write("lgc_tags.csv", ["customer_key", "tag"], [(key(4), "OTA:AGODA")])

stay_rows, seg_rows = [], []
for (fy, sid, g, room, arr, dep, n, ad, ch, meal, rc, status, billno, tag) in S:
    stay_rows.append((fy, sid, g, key(g), G[g][1], arr, utc(arr, "14:00"), dep, utc(dep, "11:00") if dep else None, None,
                      n, ad, ch, room, rc, meal, None, None, tag, status, "Y" if status == "CheckedOut" else None, billno, None))
    ref = "%s|%d" % (fy, sid)
    seg_rows.append((ref, 1, room, "DELUXE", arr, dep, ad, ch, None, None, "booked"))
    if sid == 105:
        seg_rows.append((ref, 2, "204", "DELUXE", arr, dep, 0, 0, None, None, "booked"))
    if sid == 106:
        seg_rows.append((ref, 2, "301", "PREMIUM", None, None, None, None, None, None, "shift"))
write("lgc_stay.csv", ["legacy_fy", "legacy_booking_id", "legacy_guest_id", "customer_key", "guest_name_snapshot", "arrival_date", "arrival_utc",
                       "departure_date", "departure_utc", "expected_departure_date", "nights", "adults", "children", "room_no", "room_count",
                       "meal_plan", "grc_no", "purpose", "source_tag", "status", "paid_flag", "legacy_bill_no", "notes"], stay_rows)
write("lgc_room_segment.csv", ["stay_ref", "seq", "room_no", "room_type_code", "from_date", "to_date", "adults", "children", "rate_paise", "extra_paise", "kind"], seg_rows)

stay_guest = {s[1]: s[2] for s in S}
brow = []
for (fy, no, sid, room, bd, n, rm, ex, fb, lau, oth, disc, bps, rg, fg, adv, df, pf, gstin, ref, oof) in B:
    net = rm + ex + fb + lau + oth + rg + fg - disc
    bal = net if (df == "P" and pf == "N") else 0
    g = stay_guest.get(sid, 12)
    brow.append((fy, no, 1000000 + int(fy[:4]) * 100 + no, "%s|%d" % (fy, sid) if sid else None, g, key(g), bd, utc(bd, "11:00"), room, n,
                 rm * P, ex * P, fb * P, lau * P, oth * P, disc * P, bps, rg * P, fg * P, 0, 0,
                 adv * P, net * P, bal * P, ref * P, df, pf, gstin, 1 if gstin else 0, oof, None))
write("lgc_bill.csv", ["legacy_fy", "legacy_bill_no", "legacy_transaction_id", "stay_ref", "legacy_guest_id", "customer_key", "bill_date", "bill_utc",
                       "room_no", "nights", "room_charge_paise", "extra_paise", "fb_paise", "laundry_paise", "other_paise", "discount_paise",
                       "room_gst_bps", "room_gst_paise", "fb_gst_paise", "laundry_gst_paise", "other_gst_paise", "advance_paise", "net_paise",
                       "balance_paise", "refund_paise", "data_flag", "paid_flag", "gstin_snapshot", "b2b", "out_of_fy", "notes"], brow)

rrow = []
for (fy, src, rid, sid, bno, g, d, kind, meth, amt, ref, bank) in R:
    rrow.append((fy, src, rid, "%s|%d" % (fy, sid), "%s|%d" % (fy, bno), g, key(g), d, utc(d, "12:00"), kind, meth, amt * P, ref, bank, None))
write("lgc_receipt.csv", ["legacy_fy", "legacy_source", "legacy_row_id", "stay_ref", "bill_ref", "legacy_guest_id", "customer_key", "received_date",
                          "received_utc", "kind", "method", "amount_paise", "reference", "bank_name", "notes"], rrow)

print("stays", len(S), "bills", len(B), "receipts", len(R))
for fy in ("2025-26", "2026-27"):
    bs = [b for b in B if b[0] == fy]
    print(fy, "bills", len(bs), "net", sum(b[6] + b[7] + b[8] + b[9] + b[10] + b[13] + b[14] - b[11] for b in bs),
          "roomgst", sum(b[13] for b in bs), "fb", sum(b[8] for b in bs))
    rs = [r for r in R if r[0] == fy and r[7] != "refund"]
    rf = [r for r in R if r[0] == fy and r[7] == "refund"]
    print(fy, "receipts", len(rs), sum(r[9] for r in rs), "refunds", len(rf), sum(r[9] for r in rf))

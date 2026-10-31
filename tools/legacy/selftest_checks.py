"""selftest_checks.py - assertions of `extract.py --self-test` on the fictional fixture tools/legacy/selftest/*.csv.

The fixture is invented (20 guests, 30 bookings in FY 2025-26 and 2026-27); every figure below was computed
by hand when the fixture was designed, not copied from the extractor's output.
"""
from __future__ import annotations

import csv
import json
import shutil
import sys
import tempfile
from datetime import date, datetime
from pathlib import Path
from types import SimpleNamespace

import legacy_rules as R

FAILS: list[str] = []
CHECKS = 0


def ok(cond, msg):
    global CHECKS
    CHECKS += 1
    if not cond:
        FAILS.append(msg)


def eq(a, b, msg):
    ok(a == b, f"{msg}: expected {b!r}, got {a!r}")


def rows(p: Path):
    with open(p, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f))


def rules_tests():
    eq(R.paise("12.345"), 1235, "paise half away (+)")
    eq(R.paise("-0.005"), -1, "paise half away (-)")
    eq(R.paise("2.675"), 268, "paise on the printed value, not the float")
    eq(R.paise(""), 0, "paise blank")
    eq(R.phone_key("+91 90000 00002"), "9000000002", "phone_key +91")
    eq(R.phone_key("12345"), "", "phone_key short")
    ok(R.phone_valid("91 90000 00005"), "12 digits with 91 valid")
    ok(R.phone_valid("09000000005"), "11 digits with leading 0 valid")
    ok(not R.phone_valid("900000000312"), "12 digits without 91 invalid")
    ok(not R.phone_valid("5000000001"), "10 digits starting 5 invalid")
    ok(not R.phone_valid("-") and not R.phone_valid("0") and not R.phone_valid("123"), "placeholders invalid")
    eq(R.name_fold("  M/S.  Mehta-Traders "), "m s mehta traders", "name_fold")
    eq(R.customer_key("-", "Chitra  Demo"), "n:chitra demo", "customer_key name fallback")
    eq(R.customer_key("0240-2200007", "x"), "2402200007", "landline keeps its digits")
    eq(R.title_case("M/S NORTHWIND TRADERS"), "M/S. Northwind Traders", "title M/S.")
    eq(R.title_case("DR. MEERA S.K. IYER"), "Dr. Meera S.K. Iyer", "title Dr. and initials")
    eq(R.title_case("McDonald TESTSON"), "McDonald Testson", "mixed-case word kept")
    eq(R.title_case("RAM-KUMAR (JR)"), "Ram-Kumar (Jr)", "hyphen and bracket")
    eq(R.title_case("A B SHARMA LLP"), "A B Sharma LLP", "single initials and LLP")
    eq(R.utc_text(datetime(2026, 4, 2, 10, 0)), "2026-04-02T04:30:00Z", "IST to UTC")
    eq(R.utc_text(datetime(2026, 4, 1, 0, 10)), "2026-03-31T18:40:00Z", "IST to UTC across midnight")
    eq(R.combine_date_time(datetime(2026, 1, 1), "01:58 PM"), datetime(2026, 1, 1, 13, 58), "TTime PM")
    eq(R.combine_date_time(datetime(2026, 1, 1), "12:05 AM"), datetime(2026, 1, 1, 0, 5), "TTime 12 AM")
    eq(R.combine_date_time(datetime(2026, 1, 1), ""), datetime(2026, 1, 1, 12, 0), "TTime blank")
    eq(R.fy_label_from_filename("Lodge25-26.mdb"), "2025-26", "fy label")
    eq(R.fy_label_from_filename("Lodge24-25-ORG.mdb"), None, "-ORG copy is not an input")
    eq(R.fy_label_from_filename("Lodge26-27-asof-20260622.mdb"), None, "dated copy is not an input")
    eq(R.cash_method("Online", "QR"), "UPI", "online QR")
    eq(R.cash_method("Online", "-"), "BANK", "online other")
    eq(R.cash_method("Cheque", "1"), "BANK", "cheque")
    eq(R.cash_method("Credit Card", ""), "CARD", "card")
    eq(R.real_id_number(8, "1234 5678 9012"), "123456789012", "aadhaar 12 digits")
    eq(R.real_id_number(8, "12345678901"), "12345678901", "any 4+ char value is an ID (LG-R11)")
    ok(R.id_malformed("AADHAAR", "12345678901") and not R.id_malformed("AADHAAR", "123456789012"), "aadhaar shape")
    ok(not R.id_malformed("PAN", "ABCDE1234F") and R.id_malformed("PAN", "ABCDE1234") and R.id_malformed("PASSPORT", "X1234567X"), "pan / passport shape")
    eq(R.real_id_number(2, "-"), "", "placeholder id")
    eq(R.real_id_number(2, "ab"), "", "too short id")
    eq(R.ota_name("Make My Trip"), "MMT", "ota MMT")
    eq(R.ota_name("Laundry"), None, "non-ota group")


def main_tests(here: Path, run, tmp: Path):
    fx = here / "selftest"
    support = tmp / "support"
    for k in ("Photo", "IDProof"):
        (support / k).mkdir(parents=True)
        (support / k / "1.jpg").write_bytes(b"\xff\xd8fictional\xff\xd9")
    (support / "Photo" / "2.jpg").write_bytes(b"jpegdata")
    out = tmp / "run"
    a = SimpleNamespace(out=str(out), mdb_dir=None, raw_dir=str(fx), rate_card=None, cutover_date=None, latest_only=False,
                        fy=None, keep_raw=True, paidout_all=False, support_dir=[str(support)], today="2026-10-31")
    logs: list[str] = []
    rc = run(a, log=logs.append)
    eq(rc, 0, "extractor exit code on the fixture")
    if rc != 0:
        FAILS.append("\n".join(logs[-15:]))
        return
    t = json.loads((out / "totals.json").read_text(encoding="utf-8"))
    eq(t["fiscalYears"], ["2025-26", "2026-27"], "fiscal years")
    A, B = t["fy"]["2025-26"], t["fy"]["2026-27"]
    # hand-computed: A has 16 bills / Rs 82,568.00 net before de-dup, the boundary bill (Rs 6,300) is dropped
    eq(A["beforeDedup"]["billsCount"], 16, "A bills before")
    eq(A["beforeDedup"]["netPaise"], 8256800, "A net before")
    eq(A["afterDedup"]["billsCount"], 15, "A bills after")
    eq(A["afterDedup"]["netPaise"], 7626800, "A net after")
    eq(A["afterDedup"]["roomGstPaise"], 507600, "A room GST after")
    eq(A["beforeDedup"]["roomGstPaise"], 537600, "A room GST before")
    eq(A["afterDedup"]["fbPaise"], 30000, "A F&B after")
    eq(A["beforeDedup"]["roomBookingRows"], 17, "A bookings")
    eq(A["afterDedup"]["stays"], 17, "A stays loaded incl. duplicates")
    eq(A["afterDedup"]["staysDuplicate"], 2, "A duplicate stays")
    eq(A["afterDedup"]["outOfFy"], 1, "A out_of_fy bill")
    eq(A["beforeDedup"]["receiptsCount"], 17, "A deposit rows before")
    eq(A["afterDedup"]["receiptsCount"], 16, "A deposit rows after")
    eq(A["afterDedup"]["securityCount"], 1, "A security rows")
    eq(A["afterDedup"]["securityPaise"], 50000, "A security paise")
    eq(A["afterDedup"]["refundsCount"], 1, "A refunds")
    eq(A["afterDedup"]["refundsPaise"], 60000, "A refund paise (RefundAmt + SecurityDepositRefund)")
    eq(A["afterDedup"]["advancePaise"], 100000, "A advance paise")
    eq(A["beforeDedup"]["luxuryTaxPerSlabs"], {"1": 1, "5": 10, "12": 5}, "A slabs")
    eq(A["beforeDedup"]["billDataFlagP"], 1, "A pending bills")
    eq(B["afterDedup"]["billsCount"], 11, "B bills")
    eq(B["afterDedup"]["netPaise"], 4469485, "B net (4369.845 rounds to 4369.85 on the bill)")
    eq(B["afterDedup"]["roomGstPaise"], 212500, "B room GST")
    eq(B["afterDedup"]["fbPaise"], 15000, "B F&B")
    eq(B["afterDedup"]["staysDuplicate"], 0, "B duplicates")
    eq(t["dedup"]["billsDropped"], 1, "dedup bills dropped")
    eq(t["dedup"]["staysMarkedDuplicate"], 2, "dedup stays")
    eq(t["dedup"]["receiptsDropped"], 1, "dedup receipts")
    # guests
    gu = rows(out / "guests.csv")
    eq(list(gu[0].keys()), ["name", "phone", "email", "address", "nationality", "company", "gstin", "notes"], "guests.csv header")
    eq(len(gu), 15, "guests.csv rows (20 legacy - 4 no valid phone - 1 merged by name prefix)")
    eq(len({(r["phone"], r["name"]) for r in gu}), len(gu), "one row per phone + name")
    eq(sorted(r["name"] for r in gu if r["phone"] == "9000000019"), ["Quinn Short", "Raj Unused"], "same phone, different name = separate rows")
    names = {r["name"] for r in gu}
    ok("Dr. Meera S.K. Iyer" in names and "M/S. Northwind Traders" in names and "McDonald Testson" in names, "title-cased names")
    ok("Bharat Sample K" in names and "Bharat Sample" not in names, "prefix names merge, higher GuestID wins the profile")
    by = {r["name"]: r for r in gu}
    eq(by["Mohan Gone"]["notes"], "Legacy: marked deleted; Legacy GuestID 13", "deleted guest note")
    eq(by["Oscar Swiss"]["nationality"], "Swiss", "nationality spelling")
    eq(by["Asha Testvale"]["address"], "1 SAMPLE LANE, Sampleville, Testland, 400001", "address join")
    eq(by["Dr. Meera S.K. Iyer"]["notes"], "Legacy GuestID 14; Remark: needs quiet room; Vehicle: MH00AB0000; Anniversary: 2010-02-14", "notes")
    eq(by["M/S. Northwind Traders"]["company"], "M/S. Northwind Traders", "M/S. guest is a company")
    eq(by["M/S. Northwind Traders"]["gstin"], "27AAAAA0000A1Z5", "gstin")
    co = rows(out / "companies.csv")
    eq([(c["name"], c["gstin"]) for c in co], [("Skylark Ltd", "27BBBBB1111B1Z5")], "company from a GSTIN shared by 3 guests (single-guest GSTIN skipped)")
    gm = {r["legacy_guest_id"]: r for r in rows(out / "lgc_guest_map.csv")}
    eq(len(gm), 21, "guest_map: 20 + 1 stub for the missing GuestID 99")
    eq(gm["3"]["customer_key"], "n:chitra demo", "placeholder phone -> name key")
    eq(gm["3"]["phone_valid"], "0", "placeholder phone invalid")
    eq(gm["6"]["customer_key"], "0000000312", "12-digit phone keeps last 10 as key")
    eq(gm["7"]["customer_key"], "2402200007", "landline key")
    eq((gm["2"]["customer_key"], gm["8"]["customer_key"]), ("9000000002", "9000000002"), "duplicate phone shares a key")
    eq((gm["2"]["is_primary"], gm["8"]["is_primary"], gm["2"]["guest_name"]), ("0", "1", "Bharat Sample K"), "duplicate phone maps to the winner")
    eq(gm["5"]["phone_valid"], "1", "91-prefixed 12 digits valid")
    eq(gm["13"]["deleted"], "1", "deleted flag")
    eq(gm["1"]["dob"], "1985-03-12", "dob")
    eq(gm["20"]["dob"], "", "future dob dropped")
    eq(gm["99"]["customer_key"], "n:unknown guest 99", "stub guest")
    tags = rows(out / "lgc_tags.csv")
    eq(sorted((x["customer_key"], x["tag"]) for x in tags), [("9000000001", "OTA:AGODA"), ("9000000016", "OTA:MMT")], "OTA tags only")
    eq([r["code"] for r in rows(out / "booking-sources.csv")], ["OTA-AGODA", "OTA-MMT"], "booking sources")
    eq([r["roomNo"] for r in rows(out / "rooms.csv")], ["101", "102", "201", "202", "203", "CON"], "rooms with bookings, junk room skipped")
    eq(rows(out / "room-types.csv")[0]["rackRate"], "", "rack rate empty without a rate card")
    ok(not (out / "tariffs.csv").exists(), "no tariffs without a rate card")
    # stays
    st = {(r["legacy_fy"], r["legacy_booking_id"]): r for r in rows(out / "lgc_stay.csv")}
    eq(len(st), 30, "30 stays loaded")
    eq(st[("2025-26", "14")]["status"], "Duplicate", "boundary booking duplicate (old copy)")
    eq(st[("2026-27", "1")]["status"], "CheckedOut", "boundary booking kept in the later file")
    eq(st[("2025-26", "15")]["status"], "Duplicate", "advance booking without check-out kept in the file with the TransactionEnd")
    eq(st[("2026-27", "3")]["status"], "InHouseAtCutover", "open booking in the latest file")
    eq(st[("2025-26", "5")]["room_count"], "2", "multi-room booking")
    eq(st[("2025-26", "1")]["arrival_utc"], "2025-05-02T04:30:00Z", "arrival UTC")
    eq(st[("2025-26", "1")]["nights"], "2", "nights")
    eq(st[("2025-26", "7")]["nights"], "1", "same-day stay counts one night")
    eq(st[("2025-26", "1")]["source_tag"], "OTA:AGODA", "source tag from the guest group")
    eq(st[("2025-26", "10")]["purpose"], "Official", "purpose")
    eq(st[("2025-26", "1")]["meal_plan"], "CP", "meal plan CP")
    eq(st[("2025-26", "2")]["meal_plan"], "EP", "meal plan EP")
    ok("Rate changed 3200 -> 3000" in st[("2025-26", "6")]["notes"], "rate change note")
    eq(st[("2025-26", "6")]["paid_flag"], "N", "paid flag from the bill")
    seg = [r for r in rows(out / "lgc_room_segment.csv") if r["stay_ref"] == "2025-26|5"]
    eq([(s["seq"], s["room_no"], s["kind"]) for s in seg], [("1", "101", "booked"), ("2", "202", "booked"), ("3", "201", "shift")], "segments with a room shift")
    # bills
    bl = {(r["legacy_fy"], r["legacy_bill_no"]): r for r in rows(out / "lgc_bill.csv")}
    eq(len(bl), 26, "bills loaded (16 + 11 - 1 dropped)")
    ok(("2025-26", "14") not in bl or bl[("2025-26", "14")]["bill_date"] != "2026-04-02", "boundary bill dropped from the old file")
    eq(bl[("2025-26", "4")]["room_gst_bps"], "100", "LuxuryTaxPer 1 kept as 100 bps")
    eq(bl[("2025-26", "1")]["room_gst_bps"], "1200", "12 percent slab")
    eq(bl[("2026-27", "5")]["room_gst_bps"], "500", "5 percent slab")
    eq(bl[("2025-26", "3")]["discount_paise"], "20000", "discount")
    eq(bl[("2025-26", "5")]["gstin_snapshot"], "27AAAAA0000A1Z5", "gstin from GSTR1")
    eq(bl[("2025-26", "5")]["b2b"], "1", "b2b flag")
    eq(bl[("2025-26", "5")]["net_paise"], "444300", "multi-component net")
    eq((bl[("2025-26", "5")]["laundry_paise"], bl[("2025-26", "5")]["other_paise"], bl[("2025-26", "5")]["extra_paise"]), ("10000", "5000", "30000"), "laundry / others / extra")
    eq((bl[("2026-27", "10")]["other_paise"], bl[("2026-27", "10")]["fb_gst_paise"], bl[("2026-27", "10")]["net_paise"]), ("1235", "750", "436985"), "half-away rounding of 12.345 and 7.5")
    eq(bl[("2025-26", "16")]["out_of_fy"], "1", "bill departing outside its file's FY, no twin")
    eq(bl[("2025-26", "6")]["data_flag"], "P", "pending flag")
    eq(bl[("2025-26", "1")]["bill_utc"], "2025-05-04T04:42:00Z", "bill UTC")
    eq(bl[("2025-26", "1")]["customer_key"], "9000000001", "bill customer key")
    # receipts
    rc_ = {(r["legacy_fy"], r["legacy_source"], r["legacy_row_id"]): r for r in rows(out / "lgc_receipt.csv")}
    allr = list(rc_.values())
    ok(len(rc_) == len(allr), "receipt keys unique")
    eq(sum(1 for r in allr if r["kind"] == "security"), 1, "one security row")
    sec = next(r for r in allr if r["kind"] == "security")
    ok(int(sec["legacy_row_id"]) < 0, "security row has a negative row id (unique key)")
    eq(sec["amount_paise"], "50000", "security amount")
    eq(sum(1 for r in allr if r["kind"] == "refund"), 1, "one refund")
    eq(sum(1 for r in allr if r["kind"] == "advance"), 2, "two advances")
    eq(sorted({r["method"] for r in allr}), ["BANK", "CARD", "CASH", "UPI"], "payment methods")
    upi = [r for r in allr if r["method"] == "UPI"]
    ok(len(upi) == 2 and all(r["reference"] == "QR" for r in upi), "QR online is UPI")
    chq = [r for r in allr if r["reference"] == "123456"]
    eq(len(chq), 1, "cheque reference")
    eq(chq[0]["bank_name"], "Fictional Bank", "cheque bank")
    adv0 = next(r for r in allr if r["kind"] == "advance" and r["legacy_fy"] == "2025-26")
    eq((adv0["stay_ref"], adv0["legacy_guest_id"], adv0["received_utc"]), ("", "9", "2025-08-10T06:30:00Z"), "advance without a stay, blank TTime = 12:00")
    pm = next(r for r in allr if r["legacy_fy"] == "2025-26" and r["kind"] == "payment" and r["bill_ref"] == "2025-26|6")
    eq(pm["received_utc"], "2025-09-22T08:28:00Z", "TTime 01:58 PM")
    eq(pm["stay_ref"], "2025-26|6", "receipt links to the stay")
    # cutover
    inh = rows(out / "cutover_inhouse.csv")
    eq(sorted(r["legacy_booking_id"] for r in inh), ["3", "4"], "in-house list")
    eq(next(r for r in inh if r["legacy_booking_id"] == "3")["advance_paise"], "100000", "in-house advance")
    adv = rows(out / "cutover_advances.csv")
    eq([(r["amount_paise"], r["phone_key"]) for r in adv], [("200000", "9000000019")], "unadjusted advance")
    rv = rows(out / "cutover_receivables.csv")
    eq([(r["legacy_bill_no"], r["balance_paise"], r["gstin"], r["company"]) for r in rv], [("4", "525000", "27BBBBB1111B1Z5", "Skylark Ltd")], "receivables")
    op = rows(out / "cutover_customer_opening.csv")
    eq(sorted(int(r["opening_paise"]) for r in op), [-50000, 150000], "customer openings")
    po = rows(out / "cutover_paidout.csv")
    eq(sorted(r["amount_paise"] for r in po), ["30000", "99999"], "paid-out of the latest file")
    ok("2026-27: last BillNo 11" in (out / "cutover_number_series.md").read_text(encoding="utf-8"), "number series file")
    # secret
    sj = [json.loads(x) for x in (out / "secret" / "guest_ids.jsonl").read_text(encoding="utf-8").splitlines()]
    eq(sorted(s["legacyGuestId"] for s in sj), [1, 2, 5, 17, 18, 19], "secret rows: real ID, or image, or DOB")
    s19 = next(s for s in sj if s["legacyGuestId"] == 19)
    eq((s19["idNumber"], s19.get("malformed")), ("12345678901", True), "malformed Aadhaar kept and flagged")
    ok(not next(s for s in sj if s["legacyGuestId"] == 1).get("malformed"), "well-formed ID not flagged")
    eq(rows(out / "duplicates-review.csv"), [{"group_no": "1", "legacy_guest_id": "2"}, {"group_no": "1", "legacy_guest_id": "5"}], "duplicates-review: same ID hash, different phones, ids only")
    s18 = next(s for s in sj if s["legacyGuestId"] == 18)
    eq((s18["idNumber"], s18["photoFile"]), (None, "2.jpg"), "image without an ID number is kept")
    s1 = next(s for s in sj if s["legacyGuestId"] == 1)
    eq((s1["idType"], s1["idNumber"], s1["dob"], s1["photoFile"], s1["idProofFile"]), ("AADHAAR", "100000000001", "1985-03-12", "1.jpg", "1.jpg"), "secret row shape")
    ok((out / "secret" / "images" / "photo" / "1.jpg").is_file() and (out / "secret" / "images" / "idproof" / "1.jpg").is_file(), "images copied")
    # no ID number outside secret
    needles = ["100000000001", "MH2420190000002", "X1234567"]
    for p in out.rglob("*"):
        if p.is_file() and "secret" not in p.relative_to(out).parts:
            txt = p.read_text(encoding="utf-8", errors="replace")
            for n in needles:
                ok(n not in txt, f"ID number leaked into {p.name}")
    ok((out / "report.md").is_file(), "report.md written")

    # refuses an output folder inside a git work tree
    repo = here.parent.parent
    a2 = SimpleNamespace(**{**vars(a), "out": str(repo / "tools" / "legacy" / ".runs" / "selftest-refuse")})
    eq(run(a2, log=lambda s: None), 1, "refuses --out inside a git work tree")
    ok(not (repo / "tools" / "legacy" / ".runs" / "selftest-refuse").exists(), "nothing written when refused")

    # a reconciliation failure exits 2 (duplicate DepositID in the source)
    bad = tmp / "badfix"
    shutil.copytree(fx, bad)
    p = bad / "2026-27__CustomerDeposit.csv"
    lines = p.read_text(encoding="utf-8").splitlines()
    p.write_text("\n".join(lines + [lines[1]]) + "\n", encoding="utf-8")
    a3 = SimpleNamespace(**{**vars(a), "out": str(tmp / "badrun"), "raw_dir": str(bad)})
    eq(run(a3, log=lambda s: None), 2, "self-check failure exits 2")
    ok((tmp / "badrun" / "SELFCHECK-FAILED.txt").is_file(), "SELFCHECK-FAILED.txt written")


def run(here: Path, run_fn, _ns=None) -> int:
    tmp = Path(tempfile.mkdtemp(prefix="lgx-selftest-"))
    try:
        rules_tests()
        main_tests(here, run_fn, tmp)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    if FAILS:
        for f in FAILS:
            print("FAIL: " + f)
        print(f"self-test FAILED: {len(FAILS)} of {CHECKS} checks")
        return 1
    print(f"self-test OK: {CHECKS} checks")
    return 0

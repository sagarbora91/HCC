"""extract.py - legacy "Lodge" Access files -> HCC masters CSVs + lgc_*.csv + cutover_* (LG-HISTORY-CONTRACT section 4).

Run with  python -I  from the repository root, absolute paths:
    python -I tools/legacy/extract.py --mdb-dir <dir with LodgeYY-YY.mdb> --out <run folder outside git>
    python -I tools/legacy/extract.py --self-test

Reading the .mdb files is done by extract_read.ps1 (pwsh 7 + ADODB + ACE); every rule lives here and in
legacy_rules.py. Stdlib only. No ID number is ever written to a CSV, the report or the console.
"""
from __future__ import annotations

import argparse
import csv
import json
import re
import shutil
import subprocess
import sys
import tempfile
from collections import Counter, defaultdict
from datetime import date, datetime, timezone
from decimal import ROUND_HALF_UP, Decimal
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))  # -I removes the script directory from sys.path

import legacy_rules as R  # noqa: E402

VERSION = R.VERSION
TABLES = ["CustomerInfo", "RoomBooking", "RoomBookingDetail", "RoomMaster", "PackageMaster", "TransactionData",
          "TransactionEnd", "TransactionRoomShift", "RateChangeDetail", "CustomerDeposit", "CustomerRefund",
          "CustomerOpening", "PaidOut", "ExpensesMaster", "GroupMaster", "IdentityType", "Citymaster",
          "Statemaster", "PurposeOFVisitMaster", "GSTR1"]
REQUIRED = ["CustomerInfo", "RoomBooking", "TransactionEnd", "CustomerDeposit"]
DEFAULT_SUPPORT = [r"E:\Projects\HCC\Existing Software\Updated Version\SupportFile",
                   r"E:\Projects\HCC\Existing Software\Forms\Forms\SupportFile"]

# column orders (contract section 3 without identity / property_id; refs replace surrogate ids)
C_GUEST_MAP = ["legacy_guest_id", "guest_id", "customer_key", "phone_key", "phone_valid", "name_snapshot", "name_fold",
               "deleted", "source_fy", "guest_name", "guest_phone", "is_primary", "dob"]
C_STAY = ["legacy_fy", "legacy_booking_id", "legacy_guest_id", "customer_key", "guest_name_snapshot", "arrival_date",
          "arrival_utc", "departure_date", "departure_utc", "expected_departure_date", "nights", "adults", "children",
          "room_no", "room_count", "meal_plan", "grc_no", "purpose", "source_tag", "status", "paid_flag",
          "legacy_bill_no", "notes"]
C_SEG = ["stay_ref", "seq", "room_no", "room_type_code", "from_date", "to_date", "adults", "children", "rate_paise",
         "extra_paise", "kind"]
C_BILL = ["legacy_fy", "legacy_bill_no", "legacy_transaction_id", "stay_ref", "legacy_guest_id", "customer_key",
          "bill_date", "bill_utc", "room_no", "nights", "room_charge_paise", "extra_paise", "fb_paise", "laundry_paise",
          "other_paise", "discount_paise", "room_gst_bps", "room_gst_paise", "fb_gst_paise", "laundry_gst_paise",
          "other_gst_paise", "advance_paise", "net_paise", "balance_paise", "refund_paise", "data_flag", "paid_flag",
          "gstin_snapshot", "b2b", "out_of_fy", "notes"]
C_REC = ["legacy_fy", "legacy_source", "legacy_row_id", "stay_ref", "bill_ref", "legacy_guest_id", "customer_key",
         "received_date", "received_utc", "kind", "method", "amount_paise", "reference", "bank_name", "notes"]
FORBIDDEN_COLS = re.compile(r"id.?number|aadhaar|aadhar|passport|^pan$", re.I)


class ExtractError(Exception):
    pass


# --------------------------------------------------------------------------- small helpers

def g(row: dict, col: str) -> str:
    v = row.get(col)
    return v.strip() if isinstance(v, str) else ""


def write_csv(path: Path, header: list[str], rows) -> int:
    path.parent.mkdir(parents=True, exist_ok=True)
    n = 0
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, lineterminator="\n")
        w.writerow(header)
        for r in rows:
            w.writerow([R.csv_safe(r.get(h, "") if isinstance(r, dict) else r[i]) for i, h in enumerate(header)])
            n += 1
    return n


def inside_git_work_tree(p: Path) -> bool:
    p = p.resolve()
    for d in [p, *p.parents]:
        if (d / ".git").exists():
            return True
    return False


def money_fmt(paise: int) -> str:
    sign = "-" if paise < 0 else ""
    a = abs(paise)
    r, ps = divmod(a, 100)
    s = f"{r:,}"
    # Indian grouping
    digits = str(r)
    if len(digits) > 3:
        head, tail = digits[:-3], digits[-3:]
        head = re.sub(r"(\d)(?=(\d\d)+$)", r"\1,", head)
        s = head + "," + tail
    return f"{sign}{s}.{ps:02d}"


# --------------------------------------------------------------------------- reading

class Source:
    """Rows of one FY file: table name -> list of dict. Missing table -> []."""

    def __init__(self, fy: str, loader):
        self.fy = fy
        self._loader = loader
        self._cache: dict[str, list[dict]] = {}

    def __getitem__(self, table: str) -> list[dict]:
        if table not in self._cache:
            self._cache[table] = self._loader(self.fy, table)
        return self._cache[table]


def csv_loader(dir_for_fy):
    def load(fy: str, table: str):
        p = dir_for_fy(fy, table)
        if not p.exists():
            return []
        with open(p, encoding="utf-8", newline="") as f:
            return list(csv.DictReader(f))
    return load


def run_reader(mdb_dir: Path, fys: dict[str, Path], raw_root: Path, log) -> None:
    ps1 = HERE / "extract_read.ps1"
    for fy, mdb in fys.items():
        out = raw_root / fy
        log(f"reading {mdb.name} -> raw/{fy}")
        cp = subprocess.run(["pwsh", "-NoProfile", "-File", str(ps1), "-MdbPath", str(mdb), "-OutDir", str(out)],
                            capture_output=True, text=True, encoding="utf-8")
        seen = {}
        for line in cp.stdout.splitlines():
            parts = line.split("\t")
            if parts[0] == "OK" and len(parts) == 3:
                seen[parts[1]] = int(parts[2])
        missing = [t for t in REQUIRED if t not in seen]
        if missing:
            err = (cp.stderr or "").strip().splitlines()[-1:] or [""]
            raise ExtractError(f"reader failed for {mdb.name}: missing {missing} (exit {cp.returncode}) {err[0][:200]}")
        if cp.returncode != 0:
            log(f"  note: reader exit code {cp.returncode} after writing all tables (COM teardown)")


# --------------------------------------------------------------------------- guests (section 1)

def build_guests(ci: list[dict], lk: dict, latest_fy: str, today: date, warnings: list[str]):
    """Returns (guest_map rows, guests.csv rows, tag rows, secret rows, ctx dicts)."""
    city, state, grp = lk["city"], lk["state"], lk["group"]
    rows = []
    for r in ci:
        gid = R.to_int(g(r, "GuestID"), -1)
        if gid < 0 and g(r, "GuestID") == "":
            continue
        name = re.sub(r"\s+", " ", g(r, "Name"))
        phone = g(r, "PhoneNo")
        rows.append({"gid": gid, "r": r, "name": name, "phone": phone, "pk": R.phone_key(phone),
                     "valid": R.phone_valid(phone), "deleted": g(r, "deleteflag").upper() == "Y"})
    rows.sort(key=lambda x: x["gid"])
    by_pk: dict[str, list[dict]] = defaultdict(list)
    for x in rows:
        if x["valid"]:
            by_pk[x["pk"]].append(x)
    winner_of: dict[int, dict] = {}
    clusters: list[list[dict]] = []

    def compatible(a: str, b: str) -> bool:
        return a == b or (a != "" and b != "" and (a.startswith(b) or b.startswith(a)))

    for pk, grpx in by_pk.items():
        cl: list[list[dict]] = []
        for x in grpx:  # ascending GuestID
            f = R.name_fold(x["name"])
            for c in cl:
                if any(compatible(f, R.name_fold(m["name"])) for m in c):
                    c.append(x)
                    break
            else:
                cl.append([x])
        for c in cl:
            w = max(c, key=lambda x: x["gid"])
            for x in c:
                winner_of[x["gid"]] = w
            clusters.append(c)

    def guest_csv_row(w: dict) -> dict:
        r = w["r"]
        company = R.real_text(g(r, "CompanyN") or g(r, "CompanyName"))
        if not company and R.to_int(g(r, "CustCategoryCode")) == 3:
            company = R.title_case(w["name"])
        elif company:
            company = R.title_case(company)
        note_parts = []
        if w["deleted"]:
            note_parts.append("Legacy: marked deleted")
        note_parts.append(f"Legacy GuestID {w['gid']}")
        rm = R.real_text(g(r, "Remark"))
        if rm:
            note_parts.append(f"Remark: {rm}")
        veh = R.real_text(g(r, "VehicleNo"))
        if veh:
            note_parts.append(f"Vehicle: {veh}")
        ann = R.parse_dt(g(r, "AnniversaryDate"))
        if ann and 1900 < ann.year and ann.date() <= today:
            note_parts.append(f"Anniversary: {ann.date().isoformat()}")
        notes = "; ".join(note_parts)[:1000]
        gstin = R.valid_gstin(g(r, "GSTNo"))
        if gstin and not company:
            company = R.cap(R.title_case(w["name"]), 200)  # the masters importer needs a company on a GSTIN row
        return {
            "name": R.cap(R.title_case(w["name"]), 200), "phone": w["pk"], "email": "",
            "address": R.build_address(g(r, "Address1"), city.get(g(r, "Citycode1"), ""),
                                     lk["state"].get(lk["city_state"].get(g(r, "Citycode1"), "") or g(r, "Statecode"), ""), g(r, "PinNo1")),
            "nationality": R.nationality_for(g(r, "Nationality")), "company": company,
            "gstin": gstin, "notes": notes,
        }

    guest_rows = []
    for grpx in sorted(clusters, key=lambda c: c[0]["gid"]):
        w = max(grpx, key=lambda x: x["gid"])
        guest_rows.append(guest_csv_row(w) | {"_gids": [x["gid"] for x in grpx], "_group_gid": min(x["gid"] for x in grpx),
                                               "_winner": w["gid"]})

    gm = {}
    tags = set()
    secret = []
    dob_by_gid = {}
    for x in rows:
        gid, r = x["gid"], x["r"]
        ck = R.customer_key(x["phone"], x["name"]) if not x["valid"] else x["pk"]
        w = winner_of.get(gid)
        dob = R.real_dob(g(r, "DOB"), today)
        dob_by_gid[gid] = dob
        gm[gid] = {
            "legacy_guest_id": gid, "guest_id": "", "customer_key": ck, "phone_key": x["pk"],
            "phone_valid": 1 if x["valid"] else 0, "name_snapshot": R.cap(R.title_case(x["name"]), 200),
            "name_fold": R.cap(R.name_fold(x["name"]), 200), "deleted": 1 if x["deleted"] else 0, "source_fy": latest_fy,
            "guest_name": R.cap(R.title_case(w["name"]), 200) if w else "", "guest_phone": w["pk"] if w else "",
            "is_primary": (1 if w and w["gid"] == gid else 0) if w else 0, "dob": dob,
        }
        gname = grp.get(g(r, "GroupID"), "")
        ota = R.ota_name(gname)
        if ota:
            tags.add((ck, "OTA:" + ota))
        # secret: real ID number only
        idc = R.to_int(g(r, "IDCode"))
        num = R.real_id_number(idc, g(r, "IDNumber"))
        secret.append({"legacyGuestId": gid, "idType": (R.id_type_for(idc) or "OTHER") if num else R.id_type_for(idc),
                       "idNumber": num or None, "malformed": bool(num) and R.id_malformed(R.id_type_for(idc) or "OTHER", num), "dob": dob or None, "_photo": g(r, "Photpath"), "_idproof": g(r, "IDProofPath")})
    return gm, guest_rows, sorted(tags), secret, rows


def companies_from(guest_rows: list[dict], gm_unused=None):
    by = defaultdict(list)
    for gr in guest_rows:
        if gr["gstin"]:
            by[gr["gstin"]].append(gr)
    out, review = [], []
    for gstin, lst in sorted(by.items()):
        if len(lst) < 3:
            continue
        lst.sort(key=lambda x: x["_group_gid"])
        cnt = Counter(x["company"] for x in lst if x["company"])
        name = cnt.most_common(1)[0][0] if cnt else lst[0]["name"]
        out.append({"name": name, "gstin": gstin, "contact": lst[0]["name"], "phone": lst[0]["phone"], "email": "",
                    "city": "", "creditLimit": 0, "negotiatedRates": ""})
        review.append((gstin, name, [x["name"] for x in lst]))
    return out, review


# --------------------------------------------------------------------------- one FY

class Fy:
    pass


def process_fy(src: Source, fy: str, gm: dict, tag_of_key: dict, lk: dict, is_latest: bool, warnings: list[str]):
    o = Fy()
    o.fy = fy
    rm_rows = src["RoomMaster"]
    room_name_by_id = {g(r, "RoomNo"): R.room_name(g(r, "RoomName")) for r in rm_rows}
    room_pkg_by_name = {R.room_name(g(r, "RoomName")): R.to_int(g(r, "PackageID")) for r in rm_rows}
    o.room_pkg_by_name = room_pkg_by_name
    purpose = {g(r, "PVID"): g(r, "PurposeOFVisit") for r in src["PurposeOFVisitMaster"]}
    book_rows = src["RoomBooking"]
    te_rows = src["TransactionEnd"]
    tdata = {g(r, "TransactionID"): g(r, "BookingID") for r in src["TransactionData"]}
    det = defaultdict(list)
    for r in src["RoomBookingDetail"]:
        det[g(r, "BookingID")].append(r)
    for k in det:
        det[k].sort(key=lambda r: R.to_int(g(r, "Srno")))
    shifts = defaultdict(list)
    for r in src["TransactionRoomShift"]:
        shifts[g(r, "TransactionID")].append(r)
    for k in shifts:
        shifts[k].sort(key=lambda r: R.dec(g(r, "SequenceNo")))
    rate_changes = defaultdict(list)
    for r in src["RateChangeDetail"]:
        rate_changes[g(r, "BookingID")].append(r)
    gstr1 = {}
    for r in src["GSTR1"]:
        gstr1.setdefault(g(r, "BillNo"), r)

    te_by_booking = {}
    for r in te_rows:
        bk = tdata.get(g(r, "TransactionID"), g(r, "TransactionID"))
        te_by_booking.setdefault(bk, r)

    def gkey(gid: int, fallback_name: str = "") -> tuple[str, str]:
        m = gm.get(gid)
        if m is None:
            nm = f"Unknown guest {gid}"
            m = {"customer_key": "n:" + R.name_fold(nm), "name_snapshot": nm, "phone_key": "", "phone_valid": 0,
                 "name_fold": R.name_fold(nm), "deleted": 0, "source_fy": fy}
            gm[gid] = {"legacy_guest_id": gid, "guest_id": "", "customer_key": m["customer_key"], "phone_key": "",
                       "phone_valid": 0, "name_snapshot": nm, "name_fold": m["name_fold"], "deleted": 0,
                       "source_fy": fy, "guest_name": "", "guest_phone": "", "is_primary": 0, "dob": "",
                       "_stub": True}
            warnings.append(f"{fy}: GuestID {gid} is not in CustomerInfo (stub guest_map row)")
        return m["customer_key"], m["name_snapshot"]

    o.stays, o.segments, o.stay_meta = [], [], {}
    for b in sorted(book_rows, key=lambda r: R.to_int(g(r, "BookingID"))):
        bid = g(b, "BookingID")
        if bid == "":
            continue
        gid = R.to_int(g(b, "GuestID"))
        ck, nm = gkey(gid)
        fd = R.parse_dt(g(b, "FromDate")) or R.parse_dt(g(b, "TDate"))
        td = R.parse_dt(g(b, "ToDate"))
        if fd is None:
            warnings.append(f"{fy}: booking {bid} has no FromDate (skipped)")
            continue
        dets = det.get(bid, [])
        rooms = [room_name_by_id.get(g(d, "RoomNo"), "") for d in dets]
        rooms = [r for r in rooms if r]
        first_room = rooms[0] if rooms else R.room_name(g(b, "RoomNo").split(",")[0])
        te = te_by_booking.get(bid)
        dep = R.parse_dt(g(te, "DepartureDate")) if te else None
        latest_open = te is None
        if te is None:
            status = "InHouseAtCutover" if is_latest else "OpenAtBackup"
        else:
            status = "CheckedOut"
        end = dep if dep else td
        nights = max(1, (end.date() - fd.date()).days) if end else 1
        adults = sum(R.to_int(g(d, "Adult")) for d in dets)
        children = sum(R.to_int(g(d, "Childrens")) for d in dets)
        pv = purpose.get(g(b, "PurposeOfVisit"), "") if R.to_int(g(b, "PurposeOfVisit")) else ""
        notes = []
        if R.real_text(g(b, "NoOFPerson")):
            notes.append(f"Persons: {R.real_text(g(b, 'NoOFPerson'))}")
        for col, lab in (("NextDestination", "Next"), ("Remark", "Remark"), ("VehicleNo", "Vehicle")):
            if R.real_text(g(b, col)):
                notes.append(f"{lab}: {R.real_text(g(b, col))}")
        for rc in rate_changes.get(bid, []):
            notes.append(f"Rate changed {R.rupees_text(g(rc, 'OldRoomRant'))} -> {R.rupees_text(g(rc, 'NewRoomRant'))}"
                         + (f", reason {R.real_text(g(rc, 'Reason'))}" if R.real_text(g(rc, "Reason")) else ""))
        if shifts.get(bid):
            notes.append("Room shift dates are not stored in the legacy data")
        grc = R.to_int(g(b, "GRCNo"))
        ref = f"{fy}|{bid}"
        stay = {
            "legacy_fy": fy, "legacy_booking_id": bid, "legacy_guest_id": gid, "customer_key": ck,
            "guest_name_snapshot": R.cap(nm, 200), "arrival_date": R.date_text(fd), "arrival_utc": R.utc_text(fd),
            "departure_date": R.date_text(dep) if dep else "", "departure_utc": R.utc_text(dep) if dep else "",
            "expected_departure_date": R.date_text(td) if td else "", "nights": nights, "adults": adults,
            "children": children, "room_no": R.cap(first_room or "?", 20), "room_count": len(set(rooms)) or 1,
            "meal_plan": R.MEAL_PLAN.get(R.to_int(g(b, "FacilityID")), ""), "grc_no": grc if grc else "",
            "purpose": R.cap(pv, 100), "source_tag": R.cap(tag_of_key.get(ck, ""), 30), "status": status, "paid_flag": "",
            "legacy_bill_no": "", "notes": "; ".join(notes)[:1000],
        }
        o.stays.append(stay)
        o.stay_meta[bid] = {"key": R.stay_key(fd, R.room_name(g(b, "RoomNo")), gid), "has_te": te is not None, "stay": stay}
        # segments
        seq = 0
        for d in dets:
            seq += 1
            rn = room_name_by_id.get(g(d, "RoomNo"), "") or first_room
            pkg = room_pkg_by_name.get(rn, 0)
            single = len(dets) == 1 and not shifts.get(bid)
            o.segments.append({
                "stay_ref": ref, "seq": seq, "room_no": R.cap(rn, 20), "room_type_code": R.ROOM_TYPE_BY_PACKAGE_NAME.get(pkg, ""),
                "from_date": R.date_text(fd) if seq == 1 else "", "to_date": (R.date_text(end) if (single and end) else ""),
                "adults": R.to_int(g(d, "Adult")), "children": R.to_int(g(d, "Childrens")),
                "rate_paise": R.paise(g(d, "RoomCharges")), "extra_paise": R.paise(g(d, "ExtraCharges")), "kind": "booked"})
        for s in shifts.get(bid, []):
            seq += 1
            rn = room_name_by_id.get(g(s, "ToRoom"), "") or "?"
            pkg = room_pkg_by_name.get(rn, 0)
            o.segments.append({
                "stay_ref": ref, "seq": seq, "room_no": R.cap(rn, 20), "room_type_code": R.ROOM_TYPE_BY_PACKAGE_NAME.get(pkg, ""),
                "from_date": "", "to_date": "", "adults": "", "children": "", "rate_paise": "", "extra_paise": "",
                "kind": "shift"})
    stay_by_bid = {s["legacy_booking_id"]: s for s in o.stays}

    # bills
    o.bills = []
    seen_bn = set()
    for t in sorted(te_rows, key=lambda r: R.to_int(g(r, "BillNo"))):
        bn = R.to_int(g(t, "BillNo"))
        tid = g(t, "TransactionID")
        bk = tdata.get(tid, tid)
        dep = R.parse_dt(g(t, "DepartureDate"))
        if dep is None:
            warnings.append(f"{fy}: bill {bn} has no DepartureDate (skipped)")
            continue
        if bn in seen_bn:
            warnings.append(f"{fy}: BillNo {bn} repeats (second row skipped)")
            continue
        seen_bn.add(bn)
        stay = stay_by_bid.get(bk)
        gid = stay["legacy_guest_id"] if stay else None
        ck = stay["customer_key"] if stay else ""
        room = R.room_name(g(t, "RoomNo")) or (stay["room_no"] if stay else "?")
        gr = gstr1.get(str(bn))
        gst = R.valid_gstin(g(gr, "GSTNo")) if gr else ""
        sv = {
            "room": R.dec(g(t, "RoomCharg")), "extra": R.dec(g(t, "ExtraCharge")),
            "extra_other": R.dec(g(t, "ACCharge")) + R.dec(g(t, "ElectricalExpCharges")),
            "fb": R.dec(g(t, "FB")), "laundry": R.dec(g(t, "Laundry")), "others": R.dec(g(t, "Others")),
            "others_other": sum((R.dec(g(t, c)) for c in ("Telephone", "Addition", "WaterDonation", "Damages")), Decimal(0)),
            "discount": R.dec(g(t, "DiscountAmt")) + R.dec(g(t, "BillDiscount")), "gst": R.dec(g(t, "LuxuryTax")),
            "fbgst": R.dec(g(t, "FoodGSTRs")), "lgst": R.dec(g(t, "LaunderyGSTRs")), "ogst": R.dec(g(t, "OtherGSTRs")),
            "advance": R.dec(g(t, "Advance")), "net": R.dec(g(t, "NetBill")), "bal": R.dec(g(t, "BalanceDue")),
            "refund": R.dec(g(t, "Refundamt")),
        }
        dflag = g(t, "DataFlag").upper()
        pflag = g(t, "PaidFlag").upper()
        notes = []
        if R.real_text(g(t, "Note")):
            notes.append(f"Note: {R.real_text(g(t, 'Note'))}")
        if R.real_text(g(t, "DamagesRemark")):
            notes.append(f"Damages: {R.real_text(g(t, 'DamagesRemark'))}")
        if len(room) > 20:
            notes.insert(0, f"RoomNo: {room}")
        b = {
            "legacy_fy": fy, "legacy_bill_no": bn, "legacy_transaction_id": R.to_int(tid),
            "stay_ref": f"{fy}|{bk}" if stay else "", "legacy_guest_id": gid if gid is not None else "",
            "customer_key": ck, "bill_date": R.date_text(dep), "bill_utc": R.utc_text(dep), "room_no": R.cap(room, 20),
            "nights": stay["nights"] if stay else "",
            "room_charge_paise": R.paise(g(t, "RoomCharg")),
            "extra_paise": R.paise(g(t, "ExtraCharge")) + R.paise(g(t, "ACCharge")) + R.paise(g(t, "ElectricalExpCharges")),
            "fb_paise": R.paise(g(t, "FB")), "laundry_paise": R.paise(g(t, "Laundry")),
            "other_paise": sum(R.paise(g(t, c)) for c in ("Others", "Telephone", "Addition", "WaterDonation", "Damages")),
            "discount_paise": R.paise(g(t, "DiscountAmt")) + R.paise(g(t, "BillDiscount")),
            "room_gst_bps": int((R.dec(g(t, "LuxuryTaxPer")) * 100).quantize(Decimal(1), rounding=ROUND_HALF_UP)),
            "room_gst_paise": R.paise(g(t, "LuxuryTax")), "fb_gst_paise": R.paise(g(t, "FoodGSTRs")),
            "laundry_gst_paise": R.paise(g(t, "LaunderyGSTRs")), "other_gst_paise": R.paise(g(t, "OtherGSTRs")),
            "advance_paise": R.paise(g(t, "Advance")), "net_paise": R.paise(g(t, "NetBill")),
            "balance_paise": R.paise(g(t, "BalanceDue")), "refund_paise": R.paise(g(t, "Refundamt")),
            "data_flag": dflag if dflag in ("C", "P") else "?", "paid_flag": pflag if pflag in ("Y", "N") else "?",
            "gstin_snapshot": R.cap(gst, 15), "b2b": 1 if gr else 0, "out_of_fy": 0, "notes": R.cap("; ".join(notes), 500),
            "_dep": dep, "_key": R.bill_key(dep, room, g(t, "NetBill")), "_src": sv,
            "_lt_slab": g(t, "LuxuryTaxPer"), "_disc_amt_paise": R.paise(g(t, "DiscountAmt")),
        }
        o.bills.append(b)
        if stay:
            stay["paid_flag"] = b["paid_flag"] if b["paid_flag"] in ("Y", "N") else ""
            stay["legacy_bill_no"] = bn
    bill_by_no = {b["legacy_bill_no"]: b for b in o.bills}
    o.bill_by_no = bill_by_no

    # receipts
    o.receipts = []
    for d in sorted(src["CustomerDeposit"], key=lambda r: R.to_int(g(r, "DepositID"))):
        did = R.to_int(g(d, "DepositID"))
        day = R.parse_dt(g(d, "Date"))
        if day is None:
            warnings.append(f"{fy}: deposit {did} has no Date (skipped)")
            continue
        when = R.combine_date_time(day, g(d, "TTime"))
        tid = g(d, "TransactionID")
        bk = tdata.get(tid, tid)
        stay = stay_by_bid.get(bk) if R.to_int(tid) > 0 else None
        cust = R.to_int(g(d, "CustomerCode"))
        gid = cust if cust > 0 else (stay["legacy_guest_id"] if stay else None)
        ck = gm[gid]["customer_key"] if gid is not None and gid in gm else (stay["customer_key"] if stay else "")
        bn = R.to_int(g(d, "BillNo"))
        ptype = g(d, "PaymentType").lower()
        adv, dep_amt = R.dec(g(d, "Advance")), R.dec(g(d, "DepositAmt"))
        method = R.cash_method(g(d, "CashType"), g(d, "ChequeNo"))
        cheq = g(d, "ChequeNo")
        notes = []
        for col, lab in (("Remark", "Remark"), ("CashReceiptNo", "Receipt"), ("CompanyName", "Company")):
            if R.real_text(g(d, col)):
                notes.append(f"{lab}: {R.real_text(g(d, col))}")
        for col, lab in (("TDSAmount", "TDS"), ("CommissionAmount", "Commission"), ("DirectBillAmount", "Direct bill")):
            if R.dec(g(d, col)) != 0:
                notes.append(f"{lab}: {R.rupees_text(g(d, col))}")
        base = {
            "legacy_fy": fy, "legacy_source": "deposit", "stay_ref": f"{fy}|{bk}" if stay else "",
            "bill_ref": f"{fy}|{bn}" if bn > 0 and bn in bill_by_no else "",
            "legacy_guest_id": gid if gid is not None else "", "customer_key": ck,
            "received_date": R.date_text(day), "received_utc": R.utc_text(when), "method": method,
            "reference": R.cap("" if cheq in ("", "-", "0") else cheq, 100), "bank_name": R.cap(R.real_text(g(d, "BankName")), 100),
            "notes": "; ".join(notes)[:300], "_day": day, "_key": R.receipt_key(day, g(d, "DepositAmt"), cust),
        }
        if ptype == "advance":
            main_kind, main_amt, other_amt = "advance", adv, dep_amt
        else:
            main_kind, main_amt, other_amt = "payment", dep_amt, adv
        o.receipts.append(base | {"legacy_row_id": did, "kind": main_kind, "amount_paise": R.paise(main_amt),
                                  "_src_amt": main_amt, "_primary": True})
        if other_amt != 0:
            o.receipts.append(base | {"legacy_row_id": -(did + 1_000_000_000_000), "kind": "advance" if main_kind == "payment" else "payment",
                                      "amount_paise": R.paise(other_amt), "_src_amt": other_amt, "_primary": False,
                                      "_key": None, "notes": "second amount on the same deposit row"})
        sec = R.dec(g(d, "SecurityDeposit"))
        if sec != 0:
            o.receipts.append(base | {"legacy_row_id": -did, "kind": "security", "amount_paise": R.paise(sec),
                                      "_src_amt": sec, "_primary": False, "_key": None, "notes": "security deposit"})
    for rf in sorted(src["CustomerRefund"], key=lambda r: R.to_int(g(r, "RefundID"))):
        rid = R.to_int(g(rf, "RefundID"))
        day = R.parse_dt(g(rf, "RefundDate"))
        if day is None:
            warnings.append(f"{fy}: refund {rid} has no RefundDate (skipped)")
            continue
        when = R.combine_date_time(day, g(rf, "TTime"))
        gid = R.to_int(g(rf, "GuestID"))
        bn = R.to_int(g(rf, "billno"))
        ra, sa = R.dec(g(rf, "RefundAmt")), R.dec(g(rf, "SecurityDepositRefund"))
        cheq = g(rf, "ChequeNo")
        o.receipts.append({
            "legacy_fy": fy, "legacy_source": "refund", "legacy_row_id": rid, "stay_ref": "",
            "bill_ref": f"{fy}|{bn}" if bn > 0 and bn in bill_by_no else "", "legacy_guest_id": gid,
            "customer_key": gm[gid]["customer_key"] if gid in gm else "", "received_date": R.date_text(day),
            "received_utc": R.utc_text(when), "kind": "refund", "method": R.cash_method(g(rf, "CashType"), cheq),
            "amount_paise": R.paise(ra) + R.paise(sa), "reference": R.cap("" if cheq in ("", "-", "0") else cheq, 100),
            "bank_name": R.cap(R.real_text(g(rf, "BankName")), 100), "notes": R.real_text(g(rf, "Remark"))[:300],
            "_day": day, "_key": None, "_src_amt": ra + sa, "_primary": True})
    return o


# --------------------------------------------------------------------------- dedup (section 7)

def dedup(fys: list[Fy], warnings: list[str]):
    info = {"billsDropped": 0, "staysMarkedDuplicate": 0, "receiptsDropped": 0, "perBoundary": [], "dropped": []}
    drop_bills, dup_stays, drop_rec = set(), set(), set()
    for a, b in zip(fys, fys[1:]):
        bound = {"from": a.fy, "to": b.fy, "bills": 0, "stays": 0, "receipts": 0}
        # bills
        idx = defaultdict(list)
        for x in b.bills:
            idx[x["_key"]].append(x)
        for x in a.bills:
            cands = [y for y in idx.get(x["_key"], []) if id(y) not in drop_bills]
            if not cands or id(x) in drop_bills:
                continue
            y = cands[0]
            if R.date_in_fy(x["_dep"], b.fy):
                drop_bills.add(id(x)); bound["bills"] += 1
                info["dropped"].append(("bill", a.fy, x["legacy_bill_no"], x["bill_date"], x["room_no"], x["net_paise"]))
            elif R.date_in_fy(x["_dep"], a.fy):
                drop_bills.add(id(y)); bound["bills"] += 1
                info["dropped"].append(("bill", b.fy, y["legacy_bill_no"], y["bill_date"], y["room_no"], y["net_paise"]))
        # stays
        idx = defaultdict(list)
        for bid, m in b.stay_meta.items():
            idx[m["key"]].append((bid, m))
        used = set()
        for bid, m in a.stay_meta.items():
            for bid2, m2 in idx.get(m["key"], []):
                if bid2 in used:
                    continue
                used.add(bid2)
                if m["has_te"] and not m2["has_te"]:
                    dup_stays.add(id(m2["stay"])); bound["stays"] += 1; who = (b.fy, bid2, m2["stay"])
                else:
                    dup_stays.add(id(m["stay"])); bound["stays"] += 1; who = (a.fy, bid, m["stay"])
                info["dropped"].append(("stay", who[0], who[1], who[2]["arrival_date"], who[2]["room_no"], ""))
                break
        # receipts
        idx = defaultdict(list)
        for r in b.receipts:
            if r["_key"] is not None:
                idx[r["_key"]].append(r)
        used = set()
        for r in a.receipts:
            if r["_key"] is None:
                continue
            for r2 in idx.get(r["_key"], []):
                if id(r2) in used:
                    continue
                used.add(id(r2))
                if R.date_in_fy(r2["_day"], a.fy) and not R.date_in_fy(r2["_day"], b.fy):
                    victim, vfy = r2, b.fy
                else:
                    victim, vfy = r, a.fy
                drop_rec.add(id(victim)); bound["receipts"] += 1
                info["dropped"].append(("receipt", vfy, victim["legacy_row_id"], victim["received_date"], "", victim["amount_paise"]))
                break
        info["perBoundary"].append(bound)
    for f in fys:
        f.dropped_bills = [x for x in f.bills if id(x) in drop_bills]
        f.bills = [x for x in f.bills if id(x) not in drop_bills]
        f.dropped_receipts = [x for x in f.receipts if id(x) in drop_rec or
                              (not x["_primary"] and x["legacy_row_id"] != 0 and False)]
        # secondary rows (security / second amount) of a dropped primary row go with it
        gone_ids = {x["legacy_row_id"] for x in f.dropped_receipts if x["legacy_source"] == "deposit"}
        f.receipts_all = f.receipts
        kept, dropped_extra = [], []
        for x in f.receipts:
            if id(x) in drop_rec:
                continue
            if x["legacy_source"] == "deposit" and not x["_primary"]:
                base_id = -x["legacy_row_id"] if x["legacy_row_id"] > -1_000_000_000_000 else -(x["legacy_row_id"]) - 1_000_000_000_000
                if base_id in gone_ids:
                    dropped_extra.append(x)
                    continue
            kept.append(x)
        f.dropped_receipts += dropped_extra
        f.receipts = kept
        valid_bills = {x["legacy_bill_no"] for x in f.bills}
        for r in f.receipts:
            if r["bill_ref"]:
                if int(r["bill_ref"].split("|")[1]) not in valid_bills:
                    r["bill_ref"] = ""
        for s in f.stays:
            if id(s) in dup_stays:
                s["status"] = "Duplicate"
        for x in f.bills:
            x["out_of_fy"] = 0 if R.date_in_fy(x["_dep"], f.fy) else 1
    info["billsDropped"] = sum(len(f.dropped_bills) for f in fys)
    info["staysMarkedDuplicate"] = sum(1 for f in fys for s in f.stays if s["status"] == "Duplicate")
    info["receiptsDropped"] = sum(len(f.dropped_receipts) for f in fys)
    return info


# --------------------------------------------------------------------------- totals

SRC_KEYS = [("room", "RoomCharg"), ("extra", "ExtraCharge"), ("fb", "FB"), ("laundry", "Laundry"), ("others", "Others"),
            ("discount", "DiscountAmt+BillDiscount"), ("gst", "LuxuryTax"), ("fbgst", "FoodGSTRs"),
            ("advance", "Advance"), ("net", "NetBill")]


def bill_sums(bills: list[dict]) -> dict:
    return {
        "billsCount": len(bills), "roomChargePaise": sum(b["room_charge_paise"] for b in bills),
        "extraPaise": sum(b["extra_paise"] for b in bills), "fbPaise": sum(b["fb_paise"] for b in bills),
        "laundryPaise": sum(b["laundry_paise"] for b in bills), "otherPaise": sum(b["other_paise"] for b in bills),
        "discountPaise": sum(b["discount_paise"] for b in bills),
        "discountAmtOnlyPaise": sum(b["_disc_amt_paise"] for b in bills), "roomGstPaise": sum(b["room_gst_paise"] for b in bills),
        "fbGstPaise": sum(b["fb_gst_paise"] for b in bills), "advanceOnBillsPaise": sum(b["advance_paise"] for b in bills),
        "netPaise": sum(b["net_paise"] for b in bills),
        "billDataFlagC": sum(1 for b in bills if b["data_flag"] == "C"), "billDataFlagP": sum(1 for b in bills if b["data_flag"] == "P"),
        "billPaidFlagN": sum(1 for b in bills if b["paid_flag"] == "N"),
        "billNoMin": min((b["legacy_bill_no"] for b in bills), default=0), "billNoMax": max((b["legacy_bill_no"] for b in bills), default=0),
        "luxuryTaxPerSlabs": dict(sorted(Counter(R.rupees_text(b["_lt_slab"]) or "0" for b in bills).items(), key=lambda kv: float(kv[0]))),
    }


def receipt_sums(recs: list[dict]) -> dict:
    dep = [r for r in recs if r["legacy_source"] == "deposit" and r["kind"] in ("advance", "payment")]
    sec = [r for r in recs if r["kind"] == "security"]
    ref = [r for r in recs if r["legacy_source"] == "refund"]
    return {
        "receiptsCount": len(dep), "receiptsPaise": sum(r["amount_paise"] for r in dep),
        "advancePaise": sum(r["amount_paise"] for r in dep if r["kind"] == "advance"),
        "paymentPaise": sum(r["amount_paise"] for r in dep if r["kind"] == "payment"),
        "securityCount": len(sec), "securityPaise": sum(r["amount_paise"] for r in sec),
        "refundsCount": len(ref), "refundsPaise": sum(r["amount_paise"] for r in ref),
    }


def fy_totals(f: Fy, src: Source, before_bills, before_recs) -> dict:
    bs = bill_sums(before_bills)
    rs = receipt_sums(before_recs)
    before = {
        "customerInfoRows": len(src["CustomerInfo"]), "roomBookingRows": len(src["RoomBooking"]),
        "transactionEndRows": len(src["TransactionEnd"]), "customerDepositRows": len(src["CustomerDeposit"]),
        "customerRefundRows": len(src["CustomerRefund"]), **bs, **rs, "staysCount": len(f.stays),
    }
    bsa, rsa = bill_sums(f.bills), receipt_sums(f.receipts)
    after = {
        "stays": len(f.stays), "staysDuplicate": sum(1 for s in f.stays if s["status"] == "Duplicate"),
        "segments": len(f.segments), "bills": len(f.bills), "receipts": len(f.receipts),
        "billsDropped": len(f.dropped_bills), "receiptsDropped": len(f.dropped_receipts),
        "outOfFy": sum(1 for b in f.bills if b["out_of_fy"]), **bsa, **rsa,
    }
    return {"beforeDedup": before, "afterDedup": after}


# --------------------------------------------------------------------------- self-checks

def self_check(fys: list[Fy], gm: dict, guest_rows: list[dict], out_files: dict) -> list[str]:
    bad = []
    for f in fys:
        # per-row paise vs source rupees (contract: +/- rows)
        for key, col in (("room", "room_charge_paise"), ("fb", "fb_paise"), ("laundry", "laundry_paise"),
                         ("gst", "room_gst_paise"), ("net", "net_paise"), ("advance", "advance_paise")):
            want = sum(b["_src"][key] for b in f.bills) * 100
            have = sum(b[col] for b in f.bills)
            if abs(have - want) > len(f.bills):
                bad.append(f"{f.fy}: sum {col} {have} differs from source x100 {want} by more than {len(f.bills)} paise")
        for key, cols in (("extra", ["extra_paise"]), ):
            want = sum(b["_src"]["extra"] + b["_src"]["extra_other"] for b in f.bills) * 100
            have = sum(b["extra_paise"] for b in f.bills)
            if abs(have - want) > 3 * len(f.bills):
                bad.append(f"{f.fy}: extra_paise {have} vs source {want}")
        want = sum(r["_src_amt"] for r in f.receipts) * 100
        have = sum(r["amount_paise"] for r in f.receipts)
        if abs(have - want) > len(f.receipts):
            bad.append(f"{f.fy}: receipt amounts {have} vs source x100 {want}")
        # unique keys
        for name, keys in (("stay", [s["legacy_booking_id"] for s in f.stays]), ("bill", [b["legacy_bill_no"] for b in f.bills]),
                           ("receipt", [(r["legacy_source"], r["legacy_row_id"]) for r in f.receipts])):
            if len(keys) != len(set(keys)):
                bad.append(f"{f.fy}: duplicate {name} keys")
        refs = {f"{f.fy}|{s['legacy_booking_id']}" for s in f.stays}
        for lst, col in ((f.segments, "stay_ref"), (f.bills, "stay_ref"), (f.receipts, "stay_ref")):
            for r in lst:
                if r[col] and r[col] not in refs:
                    bad.append(f"{f.fy}: unresolved {col} {r[col]}")
                    break
        brefs = {f"{f.fy}|{b['legacy_bill_no']}" for b in f.bills}
        for r in f.receipts:
            if r["bill_ref"] and r["bill_ref"] not in brefs:
                bad.append(f"{f.fy}: unresolved bill_ref {r['bill_ref']}")
                break
        for s in f.stays:
            if s["legacy_guest_id"] not in gm:
                bad.append(f"{f.fy}: stay {s['legacy_booking_id']} guest not in guest_map")
                break
    pn = [(r["phone"], r["name"]) for r in guest_rows]
    if len(pn) != len(set(pn)):
        bad.append("guests.csv has a repeated phone + name")
    for name, hdr in out_files.items():
        for h in hdr:
            if FORBIDDEN_COLS.search(h):
                bad.append(f"{name}: column '{h}' looks like an ID-number column")
    return bad


# --------------------------------------------------------------------------- main run

def run(a, log=print) -> int:
    out = Path(a.out).resolve()
    if inside_git_work_tree(out):
        log(f"refusing --out inside a git work tree: {out}")
        return 1
    today = date.today() if not a.today else date.fromisoformat(a.today)
    out.mkdir(parents=True, exist_ok=True)
    raw_root = out / "raw"
    # which FYs
    if a.raw_dir:  # pre-read raw CSVs (self-test): <fy>__<Table>.csv
        rd = Path(a.raw_dir)
        names = sorted({p.name.split("__")[0] for p in rd.glob("*__*.csv")})
        fy_files = {fy: None for fy in names}
        loader = csv_loader(lambda fy, t: rd / f"{fy}__{t}.csv")
        keep_raw = True
    else:
        mdb_dir = Path(a.mdb_dir)
        fy_files = {}
        for p in sorted(mdb_dir.glob("*.mdb")):
            fy = R.fy_label_from_filename(p.name)
            if fy:
                fy_files[fy] = p
        if not fy_files:
            log("no LodgeYY-YY.mdb files found")
            return 1
        loader = csv_loader(lambda fy, t: raw_root / fy / f"{t}.csv")
        keep_raw = a.keep_raw
    all_fys = sorted(fy_files)
    sel = all_fys
    if a.fy:
        sel = [f for f in all_fys if f in a.fy]
    if a.latest_only:
        sel = sel[-1:]
    if not sel:
        log("no financial year selected")
        return 1
    if not a.raw_dir:
        run_reader(Path(a.mdb_dir), {fy: fy_files[fy] for fy in sel}, raw_root, log)
    srcs = {fy: Source(fy, loader) for fy in sel}
    latest = sel[-1]
    warnings: list[str] = []

    lk = {
        "city": {g(r, "Citycode"): g(r, "CityName") for r in srcs[latest]["Citymaster"]},
        "state": {g(r, "Statecode"): g(r, "StateName") for r in srcs[latest]["Statemaster"]},
        "group": {g(r, "GroupID"): g(r, "GroupName") for r in srcs[latest]["GroupMaster"]},
    }
    lk["city_state"] = {g(r, "Citycode"): g(r, "Statecode") for r in srcs[latest]["Citymaster"]}
    gm, guest_rows, tags, secret, ci_rows = build_guests(srcs[latest]["CustomerInfo"], lk, latest, today, warnings)
    tag_of_key = {}
    for ck, t in tags:
        tag_of_key.setdefault(ck, t)
    fys: list[Fy] = []
    before = {}
    for fy in sel:
        log(f"processing {fy}")
        f = process_fy(srcs[fy], fy, gm, tag_of_key, lk, fy == latest, warnings)
        before[fy] = (list(f.bills), list(f.receipts))
        fys.append(f)
    dd = dedup(fys, warnings)

    # ---- masters CSVs
    n_guest = write_csv(out / "guests.csv", ["name", "phone", "email", "address", "nationality", "company", "gstin", "notes"], guest_rows)
    gh = ["name", "phone", "email", "address", "nationality", "company", "gstin", "notes"]
    for i in range(0, len(guest_rows), 5000):  # the masters importer takes at most 5,000 rows per upload
        write_csv(out / f"guests-{i // 5000 + 1}.csv", gh, guest_rows[i:i + 5000])
    comp_rows, comp_review = companies_from(guest_rows)
    write_csv(out / "companies.csv", ["name", "gstin", "contact", "phone", "email", "city", "creditLimit", "negotiatedRates"], comp_rows)
    with open(out / "companies-review.md", "w", encoding="utf-8", newline="") as fh:
        fh.write("# Companies to review before the masters import (one per GSTIN with 3 or more guests)\n\n")
        if not comp_review:
            fh.write("None.\n")
        for gstin, nm, names in comp_review:
            fh.write(f"- {gstin} - {nm} - {len(names)} guests: {', '.join(names)}\n")
    rate = {}
    if a.rate_card:
        with open(a.rate_card, encoding="utf-8", newline="") as fh:
            for r in csv.DictReader(fh):
                rate[r["code"].strip().upper()] = r
    types = [("SUITE", "Luxury Suite", 3, 1), ("PREMIUM", "Premium Room", 2, 2), ("DELUXE", "Deluxe Room", 2, 3),
             ("TWIN", "Twin Room", 2, 4), ("CONF", "Conference Hall", 2, 5)]
    rt_rows = []
    for code, nm, occ, so in types:
        rr = rate.get(code)
        rt_rows.append({"code": code, "name": nm, "maxOccupancy": occ, "rackRate": (rr["double"].strip() if rr else ""), "sortOrder": so})
    write_csv(out / "room-types.csv", ["code", "name", "maxOccupancy", "rackRate", "sortOrder"], rt_rows)
    if not rate:
        warnings.append("no --rate-card: room-types rackRate is empty, tariffs.csv and occupancy-charges.csv are not written (DEC-163)")
    else:
        cut = a.cutover_date or today.isoformat()
        write_csv(out / "tariffs.csv", ["roomType", "effectiveFrom", "rate"],
                  [{"roomType": c, "effectiveFrom": cut, "rate": rate[c]["double"].strip()} for c, *_ in types if c in rate])
        write_csv(out / "occupancy-charges.csv",
                  ["code", "name", "roomType", "extraAdult", "extraChild", "extraBed", "childAgeFrom", "childAgeTo", "sortOrder", "active"],
                  [{"code": "OC-" + c, "name": nm + " extra person", "roomType": c, "extraAdult": rate[c]["extraPerson"].strip(),
                    "extraChild": "", "extraBed": "", "childAgeFrom": "", "childAgeTo": "", "sortOrder": so * 10, "active": "yes"}
                   for c, nm, _, so in types if c in rate])
    # rooms: with at least one booking in any selected FY, type from the newest RoomMaster that lists it
    booked = set()
    for f in fys:
        booked |= {s["room_no"] for s in f.stays} | {sg["room_no"] for sg in f.segments}
    room_type = {}
    for f in fys:  # later FY overrides
        for nm, pkg in f.room_pkg_by_name.items():
            room_type[nm] = pkg
    room_rows = []
    for nm in sorted(booked, key=lambda x: (len(x), x)):
        if nm == "?":
            continue
        code = R.ROOM_TYPE_BY_PACKAGE_NAME.get(room_type.get(nm, 0), "")
        if not code:
            warnings.append(f"room {nm}: package {room_type.get(nm, 0)} is not a known type (row left out of rooms.csv)")
            continue
        room_rows.append({"roomNo": nm, "roomType": code})
    write_csv(out / "rooms.csv", ["roomNo", "roomType"], room_rows)
    idt = [("AADHAAR", "Aadhaar card", 10), ("DL", "Driving licence", 20), ("VOTER", "Voter ID (election card)", 30), ("PAN", "PAN card", 40),
           ("PASSPORT", "Passport", 50), ("OFFICE-ID", "Office ID card", 60), ("OTHER", "Other ID", 70)]
    write_csv(out / "id-types.csv", ["code", "name", "accepted", "needsPhoto", "foreignerOnly", "sortOrder", "active"],
              [{"code": c, "name": n, "accepted": "yes", "needsPhoto": "no", "foreignerOnly": "no", "sortOrder": s, "active": "yes"} for c, n, s in idt])
    otas = sorted({R.ota_name(n) for n in lk["group"].values() if R.ota_name(n)})
    write_csv(out / "booking-sources.csv", ["code", "name", "segment", "channel", "commissionBps", "sortOrder", "active"],
              [{"code": R.ota_source_code(o), "name": R.OTA_DISPLAY.get(o, o), "segment": "OTA", "channel": R.OTA_DISPLAY.get(o, o),
                "commissionBps": 0, "sortOrder": 100 + 10 * i, "active": "yes"} for i, o in enumerate(otas)])

    # ---- lgc files
    gm_rows = [gm[k] for k in sorted(gm)]
    write_csv(out / "lgc_guest_map.csv", C_GUEST_MAP, gm_rows)
    write_csv(out / "lgc_stay.csv", C_STAY, [s for f in fys for s in f.stays])
    write_csv(out / "lgc_room_segment.csv", C_SEG, [s for f in fys for s in f.segments])
    write_csv(out / "lgc_bill.csv", C_BILL, [b for f in fys for b in f.bills])
    write_csv(out / "lgc_receipt.csv", C_REC, [r for f in fys for r in f.receipts])
    write_csv(out / "lgc_tags.csv", ["customer_key", "tag"], [{"customer_key": k, "tag": t} for k, t in tags])

    # ---- cutover (latest file only)
    lf = fys[-1]
    lsrc = srcs[latest]
    cut = build_cutover(out, lf, lsrc, gm, lk, a.paidout_all, fys, srcs)

    # ---- duplicates-review.csv (legacy ids only): same ID-number hash, different phone keys
    import hashlib
    byh = defaultdict(list)
    for s in secret:
        if s["idNumber"]:
            byh[hashlib.sha256(s["idNumber"].encode("utf-8")).hexdigest()].append(s["legacyGuestId"])
    dup_rows, ngrp = [], 0
    for h, ids in sorted(byh.items(), key=lambda kv: min(kv[1])):
        if len(ids) > 1 and len({gm[i]["phone_key"] for i in ids}) > 1:
            ngrp += 1
            dup_rows += [{"group_no": ngrp, "legacy_guest_id": i} for i in sorted(ids)]
    write_csv(out / "duplicates-review.csv", ["group_no", "legacy_guest_id"], dup_rows)

    # ---- secret
    sec_stats = write_secret(out, secret, a.support_dir or DEFAULT_SUPPORT, log)

    # ---- totals / report
    tot_fy = {f.fy: fy_totals(f, srcs[f.fy], *before[f.fy]) for f in fys}
    totals = {
        "extractorVersion": VERSION, "generatedUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "fiscalYears": [f.fy for f in fys], "latestFy": latest,
        "note": "beforeDedup = every source row; afterDedup = the rows written to lgc_*.csv (what the importer must reproduce). "
                "receipts* = deposit rows of kind advance + payment; security* = SecurityDeposit rows; refunds* = CustomerRefund rows "
                "(RefundAmt + SecurityDepositRefund).",
        "fy": tot_fy,
        "dedup": {k: dd[k] for k in ("billsDropped", "staysMarkedDuplicate", "receiptsDropped", "perBoundary")},
        "guests": {"customerInfoRows": len(ci_rows), "guestsCsvRows": n_guest, "validPhoneRows": sum(1 for x in gm_rows if x["phone_valid"] and not x.get("_stub")),
                   "invalidPhoneRows": sum(1 for x in gm_rows if not x["phone_valid"] and not x.get("_stub")),
                   "mergedAway": sum(1 for x in gm_rows if x["guest_name"] and not x["is_primary"]),
                   "stubs": sum(1 for x in gm_rows if x.get("_stub")), "tags": len(tags), "companies": len(comp_rows),
                   "secretRows": sec_stats["rows"], "duplicateIdGroups": ngrp, "malformedIds": sum(1 for s in secret if s.get("malformed"))},
        "cutover": cut,
    }
    (out / "totals.json").write_text(json.dumps(totals, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    write_report(out, totals, dd, warnings, comp_review, room_rows, sec_stats, fys)

    hdrs = {"guests.csv": ["name", "phone", "email", "address", "nationality", "company", "gstin", "notes"],
            "lgc_guest_map.csv": C_GUEST_MAP, "lgc_stay.csv": C_STAY, "lgc_bill.csv": C_BILL, "lgc_receipt.csv": C_REC,
            "lgc_room_segment.csv": C_SEG}
    bad = self_check(fys, gm, guest_rows, hdrs)
    if not keep_raw and raw_root.exists():
        shutil.rmtree(raw_root, ignore_errors=True)
    print_summary(totals, warnings, log)
    if bad:
        (out / "SELFCHECK-FAILED.txt").write_text("\n".join(bad) + "\n", encoding="utf-8")
        for b in bad:
            log("SELF-CHECK FAILED: " + b)
        return 2
    log(f"done -> {out}")
    return 0


def build_cutover(out: Path, lf: Fy, lsrc: Source, gm: dict, lk: dict, paidout_all: bool, fys, srcs) -> dict:
    # in-house
    adv_by_booking = defaultdict(lambda: Decimal(0))
    tdata = {g(r, "TransactionID"): g(r, "BookingID") for r in lsrc["TransactionData"]}
    for d in lsrc["CustomerDeposit"]:
        tid = g(d, "TransactionID")
        if R.to_int(tid) > 0 and R.to_int(g(d, "BillNo")) <= 0:
            adv_by_booking[tdata.get(tid, tid)] += R.dec(g(d, "DepositAmt")) + R.dec(g(d, "Advance"))
    inh = []
    rate_by = defaultdict(list)
    for s in lf.segments:
        rate_by[s["stay_ref"]].append(s)
    for s in lf.stays:
        if s["status"] != "InHouseAtCutover":
            continue
        m = gm.get(s["legacy_guest_id"], {})
        segs = rate_by.get(f"{lf.fy}|{s['legacy_booking_id']}", [])
        note = "; ".join(x for x in [s["notes"], "Rooms/rates: " + ", ".join(
            f"{x['room_no']} @ {R.rupees_text(str(Decimal(x['rate_paise'])/100)) if x['rate_paise'] != '' else '?'}" for x in segs if x["kind"] == "booked")] if x)
        inh.append({"legacy_booking_id": s["legacy_booking_id"], "guest_name": s["guest_name_snapshot"], "phone_key": m.get("phone_key", ""),
                    "room_no": s["room_no"], "arrival_date": s["arrival_date"], "expected_departure_date": s["expected_departure_date"],
                    "adults": s["adults"], "children": s["children"], "meal_plan": s["meal_plan"],
                    "advance_paise": R.paise(adv_by_booking.get(s["legacy_booking_id"], 0)), "notes": note[:1000]})
    write_csv(out / "cutover_inhouse.csv", ["legacy_booking_id", "guest_name", "phone_key", "room_no", "arrival_date",
              "expected_departure_date", "adults", "children", "meal_plan", "advance_paise", "notes"], inh)
    # advances not adjusted
    last_bill_date = defaultdict(lambda: None)
    for b in lf.bills:
        if b["legacy_guest_id"] != "":
            cur = last_bill_date[b["legacy_guest_id"]]
            if cur is None or b["_dep"] > cur:
                last_bill_date[b["legacy_guest_id"]] = b["_dep"]
    adv = []
    for r in lf.receipts:
        if r["legacy_source"] != "deposit" or r["kind"] != "advance":
            continue
        raw = next((d for d in lsrc["CustomerDeposit"] if R.to_int(g(d, "DepositID")) == r["legacy_row_id"]), None)
        if raw is None or R.to_int(g(raw, "TransactionID")) != 0 or R.dec(g(raw, "AdvanceAdjust")) != 0:
            continue
        gid = r["legacy_guest_id"]
        lb = last_bill_date.get(gid) if gid != "" else None
        if lb is not None and lb >= r["_day"]:
            continue
        m = gm.get(gid, {}) if gid != "" else {}
        adv.append({"legacy_deposit_id": r["legacy_row_id"], "guest_name": m.get("name_snapshot", ""), "phone_key": m.get("phone_key", ""),
                    "received_date": r["received_date"], "amount_paise": r["amount_paise"], "method": r["method"], "reference": r["reference"]})
    write_csv(out / "cutover_advances.csv", ["legacy_deposit_id", "guest_name", "phone_key", "received_date", "amount_paise", "method", "reference"], adv)
    # receivables
    ci_company = {g(r, "GuestID"): R.real_text(g(r, "CompanyN") or g(r, "CompanyName")) for r in lsrc["CustomerInfo"]}
    gstr_company = {g(r, "BillNo"): R.real_text(g(r, "CompanyN")) for r in lsrc["GSTR1"]}
    rec = []
    for b in lf.bills:
        if b["balance_paise"] > 0 or (b["data_flag"] == "P" and b["paid_flag"] == "N"):
            gid = b["legacy_guest_id"]
            m = gm.get(gid, {}) if gid != "" else {}
            comp = gstr_company.get(str(b["legacy_bill_no"])) or (ci_company.get(str(gid), "") if gid != "" else "")
            rec.append({"legacy_bill_no": b["legacy_bill_no"], "bill_date": b["bill_date"], "guest_name": m.get("name_snapshot", ""),
                        "phone_key": m.get("phone_key", ""), "company": R.title_case(comp), "gstin": b["gstin_snapshot"],
                        "net_paise": b["net_paise"], "balance_paise": b["balance_paise"]})
    write_csv(out / "cutover_receivables.csv", ["legacy_bill_no", "bill_date", "guest_name", "phone_key", "company", "gstin", "net_paise", "balance_paise"], rec)
    # customer opening
    opn = []
    for r in lsrc["CustomerOpening"]:
        gid = R.to_int(g(r, "CustomerCode"))
        m = gm.get(gid, {})
        td = R.parse_dt(g(r, "TDate"))
        opn.append({"legacy_guest_id": gid, "guest_name": m.get("name_snapshot", ""), "phone_key": m.get("phone_key", ""),
                    "opening_paise": R.paise(g(r, "OpeningBalance")), "as_of_date": R.date_text(td)})
    write_csv(out / "cutover_customer_opening.csv", ["legacy_guest_id", "guest_name", "phone_key", "opening_paise", "as_of_date"], opn)
    # paid out
    po = []
    use = fys if paidout_all else [fys[-1]]
    for f in use:
        s = srcs[f.fy]
        names = {g(r, "ExpensesID"): g(r, "Expenses") for r in s["ExpensesMaster"]}
        for r in s["PaidOut"]:
            td = R.parse_dt(g(r, "Tdate"))
            qty, rt = R.dec(g(r, "Qty")), R.dec(g(r, "Rate"))
            po.append({"fy": f.fy, "date": R.date_text(td), "expense": names.get(g(r, "ExpensesID"), ""), "remark": g(r, "Remark"),
                       "paid_to": g(r, "PaidTo"), "voucher_no": g(r, "VoucherNo"), "qty": R.rupees_text(g(r, "Qty")),
                       "rate_paise": R.paise(rt), "amount_paise": R.paise(qty * rt)})
    write_csv(out / "cutover_paidout.csv", ["fy", "date", "expense", "remark", "paid_to", "voucher_no", "qty", "rate_paise", "amount_paise"], po)
    # number series
    lines = ["# Number series at cut-over (DEC-163)\n", "Last legacy BillNo per financial year; GRC / REG / RCP series start at 1.\n"]
    for f in fys:
        lines.append(f"- {f.fy}: last BillNo {max((b['legacy_bill_no'] for b in f.bills + f.dropped_bills), default=0)}")
    (out / "cutover_number_series.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return {"customerOpeningRows": len(opn), "receivablesRows": len(rec), "advancesRows": len(adv), "inhouseRows": len(inh),
            "paidoutRows": len(po), "receivablesBalancePaise": sum(r["balance_paise"] for r in rec),
            "customerOpeningNetPaise": sum(r["opening_paise"] for r in opn)}


def write_secret(out: Path, secret: list[dict], support_dirs, log) -> dict:
    sd = out / "secret"
    sd.mkdir(parents=True, exist_ok=True)
    (sd / "images" / "photo").mkdir(parents=True, exist_ok=True)
    (sd / "images" / "idproof").mkdir(parents=True, exist_ok=True)

    def find(kind: str, name: str):
        if not name or name in ("-", "0"):
            return None
        base = Path(name.replace("\\", "/")).name
        for d in support_dirs:
            p = Path(d) / kind / base
            if p.is_file():
                return p
        return None

    n_photo = n_proof = kept = 0
    with open(sd / "guest_ids.jsonl", "w", encoding="utf-8", newline="") as f:
        for s in secret:
            ph, ip = find("Photo", s.pop("_photo")), find("IDProof", s.pop("_idproof"))
            if not (s["idNumber"] or s["dob"] or ph or ip):
                continue
            kept += 1
            if ph:
                shutil.copyfile(ph, sd / "images" / "photo" / ph.name); n_photo += 1
            if ip:
                shutil.copyfile(ip, sd / "images" / "idproof" / ip.name); n_proof += 1
            s["photoFile"] = ph.name if ph else None
            s["idProofFile"] = ip.name if ip else None
            row = {k: s[k] for k in ("legacyGuestId", "idType", "idNumber", "dob", "photoFile", "idProofFile")}
            if s.get("malformed"):
                row["malformed"] = True
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    return {"rows": kept, "photos": n_photo, "idProofs": n_proof}


def write_report(out, totals, dd, warnings, comp_review, room_rows, sec, fys):
    L = ["# Legacy extract report", "", f"Extractor {VERSION}, generated {totals['generatedUtc']}.", "",
         "## Per financial year (rupees; source = before de-dup, loaded = what lgc_*.csv holds)", "",
         "| FY | bookings | bills src | bills loaded | net src | net loaded | room GST loaded | F&B loaded | receipts src | receipts loaded | receipts amount loaded | refunds | duplicates (stays / bills / receipts) | out of FY |",
         "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|"]
    for fy in totals["fiscalYears"]:
        b, a = totals["fy"][fy]["beforeDedup"], totals["fy"][fy]["afterDedup"]
        L.append(f"| {fy} | {b['roomBookingRows']} | {b['billsCount']} | {a['billsCount']} | {money_fmt(b['netPaise'])} | {money_fmt(a['netPaise'])} | "
                 f"{money_fmt(a['roomGstPaise'])} | {money_fmt(a['fbPaise'])} | {b['receiptsCount']} | {a['receiptsCount']} | {money_fmt(a['receiptsPaise'])} | "
                 f"{a['refundsCount']} ({money_fmt(a['refundsPaise'])}) | {a['staysDuplicate']} / {a['billsDropped']} / {a['receiptsDropped']} | {a['outOfFy']} |")
    L += ["", "## Bill flags and GST slabs", ""]
    for fy in totals["fiscalYears"]:
        b = totals["fy"][fy]["beforeDedup"]
        L.append(f"- {fy}: C/P/PaidFlag N = {b['billDataFlagC']} / {b['billDataFlagP']} / {b['billPaidFlagN']}; BillNo {b['billNoMin']}-{b['billNoMax']}; "
                 f"LuxuryTaxPer {b['luxuryTaxPerSlabs']}")
    L += ["", "## Guests", "", json.dumps(totals["guests"], indent=1), "", f"Secret folder: {sec['rows']} ID rows, {sec['photos']} photos, {sec['idProofs']} ID proofs copied.", "",
          "## De-dup (section 7)", "", f"bills dropped {dd['billsDropped']}, stays marked Duplicate {dd['staysMarkedDuplicate']}, receipts dropped {dd['receiptsDropped']}", ""]
    for bd in dd["perBoundary"]:
        L.append(f"- {bd['from']} -> {bd['to']}: bills {bd['bills']}, stays {bd['stays']}, receipts {bd['receipts']}")
    L += ["", "Dropped rows (kind, FY, id, date, room, paise):", ""]
    for d in dd["dropped"]:
        L.append("- " + " | ".join(str(x) for x in d))
    L += ["", "## Cut-over", "", json.dumps(totals["cutover"], indent=1), "", "## Companies for review", ""]
    L += [f"- {g_} {n} ({len(ns)} guests)" for g_, n, ns in comp_review] or ["None."]
    L += ["", "## Rooms", "", ", ".join(r["roomNo"] + "=" + r["roomType"] for r in room_rows), "", "## Warnings", ""]
    L += [f"- {w}" for w in warnings[:500]] or ["None."]
    (out / "report.md").write_text("\n".join(L) + "\n", encoding="utf-8")


def print_summary(totals, warnings, log):
    log("")
    log("FY       | bookings | bills src>loaded | net loaded (Rs) | room GST loaded | receipts src>loaded | refunds | dup stay/bill/rec")
    for fy in totals["fiscalYears"]:
        b, a = totals["fy"][fy]["beforeDedup"], totals["fy"][fy]["afterDedup"]
        log(f"{fy} | {b['roomBookingRows']:>8} | {b['billsCount']:>6} > {a['billsCount']:<6} | {money_fmt(a['netPaise']):>15} | {money_fmt(a['roomGstPaise']):>15} | "
            f"{b['receiptsCount']:>6} > {a['receiptsCount']:<6} | {a['refundsCount']:>7} | {a['staysDuplicate']}/{a['billsDropped']}/{a['receiptsDropped']}")
    log(f"guests: {totals['guests']}")
    log(f"cutover: {totals['cutover']}")
    for w in warnings[:40]:
        log("warning: " + w)
    if len(warnings) > 40:
        log(f"... {len(warnings) - 40} more warnings in report.md")


# --------------------------------------------------------------------------- self-test

def self_test() -> int:
    sys.path.insert(0, str(HERE))
    import selftest_checks  # noqa: F401  (tools/legacy/selftest_checks.py)
    return selftest_checks.run(HERE, run, argparse.Namespace)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Legacy Lodge extractor (LG-HISTORY-CONTRACT section 4)")
    ap.add_argument("--mdb-dir")
    ap.add_argument("--out")
    ap.add_argument("--rate-card")
    ap.add_argument("--cutover-date")
    ap.add_argument("--latest-only", action="store_true")
    ap.add_argument("--fy", nargs="*")
    ap.add_argument("--keep-raw", action="store_true")
    ap.add_argument("--paidout-all", action="store_true", help="cutover_paidout.csv for every FY (default: latest file only)")
    ap.add_argument("--support-dir", action="append", help="folder holding Photo\\ and IDProof\\ (repeatable)")
    ap.add_argument("--raw-dir", help=argparse.SUPPRESS)
    ap.add_argument("--today", help=argparse.SUPPRESS)
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args(argv)
    if a.self_test:
        return self_test()
    if not a.mdb_dir or not a.out:
        ap.error("--mdb-dir and --out are required")
    try:
        return run(a)
    except ExtractError as e:
        print("error: " + str(e))
        return 1


if __name__ == "__main__":
    sys.exit(main())

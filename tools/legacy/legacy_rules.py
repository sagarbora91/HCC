"""legacy_rules.py - the pure rules of the legacy "Lodge" extractor (LG-HISTORY-CONTRACT sections 1, 7, 8).

Stdlib only. No I/O here: everything is a function of strings / numbers so the self-test can exercise
every edge. Money is integer paise (Decimal on the printed value, round half away from zero), IST is
the fixed +05:30 offset, identity keys are exactly the Customers 360 keys.
"""
from __future__ import annotations

import re
from datetime import date, datetime, timedelta
from decimal import ROUND_HALF_UP, Decimal, InvalidOperation

VERSION = "1.0.0"

IST = timedelta(hours=5, minutes=30)

# --------------------------------------------------------------------------- numbers / money


def dec(value) -> Decimal:
    """Decimal of a printed value ('' / None / junk -> 0). Never goes through float."""
    if value is None:
        return Decimal(0)
    s = str(value).strip().replace(",", "")
    if s == "":
        return Decimal(0)
    try:
        d = Decimal(s)
    except InvalidOperation:
        return Decimal(0)
    return d if d.is_finite() else Decimal(0)


def paise(value) -> int:
    """Rupees (printed Single / Double) -> integer paise, half away from zero (contract section 8)."""
    return int((dec(value) * 100).quantize(Decimal(1), rounding=ROUND_HALF_UP))


def to_int(value, default: int = 0) -> int:
    d = dec(value)
    try:
        return int(d)
    except (ValueError, OverflowError):
        return default


def rupees_text(value) -> str:
    """Printed rupees without a trailing .0 (for notes)."""
    d = dec(value).normalize()
    s = format(d, "f")
    return s


# --------------------------------------------------------------------------- dates / UTC


def parse_dt(value) -> datetime | None:
    s = (value or "").strip() if isinstance(value, str) else ""
    if not s:
        return None
    for fmt in ("%Y-%m-%d %H:%M:%S", "%Y-%m-%d"):
        try:
            return datetime.strptime(s, fmt)
        except ValueError:
            continue
    return None


def utc_text(ist_dt: datetime) -> str:
    """IST wall clock -> 'yyyy-mm-ddTHH:MM:SSZ' (fixed +05:30, no DST)."""
    return (ist_dt - IST).strftime("%Y-%m-%dT%H:%M:%SZ")


def date_text(d: date | datetime | None) -> str:
    if d is None:
        return ""
    return d.strftime("%Y-%m-%d")


_TTIME = re.compile(r"^\s*(\d{1,2})\s*[:.]\s*(\d{2})\s*([AaPp])\.?[Mm]?\.?\s*$")


def combine_date_time(day: datetime, ttime: str | None) -> datetime:
    """`Date` + `TTime` ('01:58 PM'; blank or unreadable -> 12:00) as an IST wall-clock datetime."""
    base = datetime(day.year, day.month, day.day)
    m = _TTIME.match(ttime or "")
    if not m:
        return base.replace(hour=12)
    h, mi, ap = int(m.group(1)), int(m.group(2)), m.group(3).upper()
    if h == 12:
        h = 0
    if ap == "P":
        h += 12
    if h > 23 or mi > 59:
        return base.replace(hour=12)
    return base.replace(hour=h, minute=mi)


def fy_label_from_filename(name: str) -> str | None:
    """'Lodge25-26.mdb' -> '2025-26' (exact pattern only; dated copies and -ORG files are not inputs)."""
    m = re.fullmatch(r"Lodge(\d\d)-(\d\d)\.mdb", name, flags=re.I)
    if not m:
        return None
    a, b = int(m.group(1)), int(m.group(2))
    if (a + 1) % 100 != b:
        return None
    return f"20{a:02d}-{b:02d}"


def fy_bounds(label: str) -> tuple[date, date]:
    y = int(label[:4])
    return date(y, 4, 1), date(y + 1, 3, 31)


def date_in_fy(d: date | datetime | None, label: str) -> bool:
    if d is None:
        return False
    dd = d.date() if isinstance(d, datetime) else d
    lo, hi = fy_bounds(label)
    return lo <= dd <= hi


# --------------------------------------------------------------------------- identity (section 1)


def digits(p) -> str:
    return re.sub(r"\D", "", p or "")


def phone_key(p) -> str:
    d = digits(p)
    return d[-10:] if len(d) >= 10 else ""


def phone_valid(p) -> bool:
    d = digits(p)
    if len(d) == 10:
        core = d
    elif len(d) == 11 and d.startswith("0"):
        core = d[1:]
    elif len(d) == 12 and d.startswith("91"):
        core = d[2:]
    else:
        return False
    return core[0] in "6789"


def name_fold(n) -> str:
    s = re.sub(r"[^a-z0-9 ]+", " ", (n or "").strip().lower())
    return re.sub(r" +", " ", s).strip()


def cap(s, n: int) -> str:
    """Cut a text to the length of the bounded column it is written to (contract section 3)."""
    s = "" if s is None else str(s)
    return s if len(s) <= n else s[:n].rstrip()


def customer_key(phone, name) -> str:
    k = phone_key(phone)
    return k if k else cap("n:" + name_fold(name), 80)


_FIXED_TITLES = {
    "m/s": "M/S.", "m/s.": "M/S.", "dr": "Dr.", "dr.": "Dr.", "mr": "Mr.", "mr.": "Mr.",
    "mrs": "Mrs.", "mrs.": "Mrs.", "ms": "Ms.", "ms.": "Ms.", "adv": "Adv.", "adv.": "Adv.",
    "er": "Er.", "er.": "Er.", "capt": "Capt.", "capt.": "Capt.",
}
_KEEP_UPPER = {"LLP", "HUF", "OPC", "II", "III", "IV", "VI", "VII", "VIII", "IX", "XI", "XII"}
_INITIALS = re.compile(r"^(?:[A-Za-z]\.)+[A-Za-z]?\.?$")


def _title_part(w: str) -> str:
    if not w:
        return w
    low = w.lower()
    if low in _FIXED_TITLES:
        return _FIXED_TITLES[low]
    if w.upper() in _KEEP_UPPER:
        return w.upper()
    if _INITIALS.match(w):
        return w.upper()
    if len(w) == 1:
        return w.upper()
    return w[0].upper() + w[1:].lower()


def _title_word(w: str) -> str:
    # never change a word that already mixes case
    if w != w.upper() and w != w.lower():
        return w
    lead = ""
    while w and w[0] in "(\"'[":
        lead += w[0]
        w = w[1:]
    if not w:
        return lead
    # parts joined by '-' are capitalised separately; 'M/S' stays a single token
    if "-" in w and w.lower() not in _FIXED_TITLES:
        return lead + "-".join(_title_part(p) for p in w.split("-"))
    return lead + _title_part(w)


def title_case(n) -> str:
    s = re.sub(r"\s+", " ", (n or "").strip())
    return " ".join(_title_word(w) for w in s.split(" ")) if s else ""


# placeholders the legacy app writes instead of NULL
_PLACEHOLDERS = {"", "-", "--", "0", "00", ".", "..", "na", "n/a", "nil", "no", "none", "null", "123", "xx", "x"}


def is_placeholder(v) -> bool:
    return (v or "").strip().lower() in _PLACEHOLDERS


def real_text(v) -> str:
    s = re.sub(r"\s+", " ", (v or "").strip())
    return "" if is_placeholder(s) else s


_GSTIN = re.compile(r"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")


def valid_gstin(v) -> str:
    s = re.sub(r"\s+", "", (v or "")).upper()
    return s if _GSTIN.match(s) else ""


# --------------------------------------------------------------------------- guests

ID_CODE_TYPE = {1: "PASSPORT", 2: "DL", 3: "VOTER", 5: "PAN", 6: "PAN", 7: "OFFICE-ID", 8: "AADHAAR"}


def id_type_for(code: int) -> str | None:
    if code == 0:
        return None
    return ID_CODE_TYPE.get(code, "OTHER")


def normalise_id_number(v) -> str:
    return re.sub(r"[^A-Z0-9]", "", (v or "").upper())


def real_id_number(id_code: int, raw) -> str:
    """Normalised ID number or '' when it is a placeholder or has fewer than 4 characters (contract section 4, LG-R11)."""
    s = normalise_id_number(raw)
    if len(s) < 4 or s.lower() in _PLACEHOLDERS:
        return ""
    return s


def id_malformed(id_type: str | None, num: str) -> bool:
    """True when the number fails the declared type's expected shape (Aadhaar 12 digits, PAN AAAAA9999A, passport 8 characters)."""
    if id_type == "AADHAAR":
        return not (len(num) == 12 and num.isdigit())
    if id_type == "PAN":
        return re.fullmatch(r"[A-Z]{5}[0-9]{4}[A-Z]", num) is None
    if id_type == "PASSPORT":
        return len(num) != 8
    return False


def real_dob(v, today: date) -> str:
    d = parse_dt(v)
    if d is None:
        return ""
    dd = d.date()
    if dd.year <= 1900 or dd > today:
        return ""
    return dd.isoformat()


NATIONALITY_FIX = {
    "swizerland": "Swiss", "switzerland": "Swiss", "swiss": "Swiss", "usa": "American", "american": "American",
    "uk": "British", "british": "British", "english": "British", "nepal": "Nepali", "nepali": "Nepali",
    "nepalese": "Nepali", "bangladesh": "Bangladeshi", "bangladeshi": "Bangladeshi", "german": "German",
    "germany": "German", "french": "French", "france": "French", "japanese": "Japanese", "japan": "Japanese",
    "australian": "Australian", "australia": "Australian", "canadian": "Canadian", "canada": "Canadian",
    "srilanka": "Sri Lankan", "sri lankan": "Sri Lankan", "sri lanka": "Sri Lankan",
}
_INDIAN = {"indian", "india", "ind", "hindu", "hindustani", "bharatiya"}


def nationality_for(raw) -> str:
    s = real_text(raw)
    if not s or s.lower() in _INDIAN:
        return "Indian"
    return NATIONALITY_FIX.get(s.lower(), title_case(s))


def build_address(address1, city, state, pin) -> str:
    parts: list[str] = []
    for p in (address1, city, state):
        t = real_text(p)
        if t and t.lower() not in {x.lower() for x in parts}:
            parts.append(t)
    pn = real_text(pin)
    if pn and not (pn.isdigit() and set(pn) == {"0"}):
        parts.append(pn)
    return ", ".join(parts)


OTA_NAMES = {
    "AGODA": "AGODA", "MMT": "MMT", "MAKEMYTRIP": "MMT", "MAKE MY TRIP": "MMT", "MAKE-MY-TRIP": "MMT",
    "GOIBIBO": "GOIBIBO", "GOBIBO": "GOIBIBO", "BOOKING.COM": "BOOKING.COM", "BOOKING": "BOOKING.COM",
    "OYO": "OYO", "YATRA": "YATRA", "TRIVAGO": "TRIVAGO",
}


def ota_name(group_name) -> str | None:
    """Canonical OTA code for a GroupMaster.GroupName, else None (contract section 1)."""
    s = re.sub(r"\s+", " ", (group_name or "").strip().upper())
    if s in OTA_NAMES:
        return OTA_NAMES[s]
    return OTA_NAMES.get(s.replace(" ", ""))


OTA_DISPLAY = {
    "AGODA": "Agoda", "MMT": "MakeMyTrip", "GOIBIBO": "Goibibo", "BOOKING.COM": "Booking.com",
    "OYO": "OYO", "YATRA": "Yatra", "TRIVAGO": "Trivago",
}


def ota_source_code(canon: str) -> str:
    return "OTA-" + re.sub(r"[^A-Z0-9]", "", canon)[:15]


# --------------------------------------------------------------------------- stays / bills / receipts

MEAL_PLAN = {4: "EP", 5: "CP", 6: "MAP", 7: "AP"}
ROOM_TYPE_BY_PACKAGE_NAME = {1: "SUITE", 2: "PREMIUM", 3: "DELUXE", 4: "CONF", 5: "TWIN"}


def room_name(v) -> str:
    return re.sub(r"\s+", " ", (v or "").strip()).upper()


def cash_method(cash_type, cheque_no) -> str:
    c = (cash_type or "").strip().lower()
    if c == "cash":
        return "CASH"
    if c == "online":
        return "UPI" if (cheque_no or "").strip().upper() == "QR" else "BANK"
    if c in ("credit card", "card", "debit card"):
        return "CARD"
    if c == "cheque":
        return "BANK"
    return "OTHER"


def stay_key(from_dt: datetime | None, room: str, guest_id: int):
    return (from_dt.date() if from_dt else None, room, guest_id)


def bill_key(dep: datetime | None, room: str, net_rupees) -> tuple:
    n = dec(net_rupees).quantize(Decimal(1), rounding=ROUND_HALF_UP)
    return (dep.date() if dep else None, room, int(n))


def receipt_key(day: datetime | None, deposit_amt, cust_code: int) -> tuple:
    n = dec(deposit_amt).quantize(Decimal(1), rounding=ROUND_HALF_UP)
    return (day.date() if day else None, int(n), cust_code)


def csv_safe(v) -> str:
    """CSV cell value: no CR/LF, trimmed (the writer quotes commas and quotes)."""
    if v is None:
        return ""
    return re.sub(r"[\r\n]+", " ", str(v)).strip()

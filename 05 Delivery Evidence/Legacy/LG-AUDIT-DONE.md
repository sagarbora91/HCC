# LG-AUDIT DONE

Lane lg-audit / branch `claude/lg-audit` / worktree `E:\Projects\HCC\worktrees\lg-audit` / base `ced9861e`. Phases run: **1, 2, 3** (all). Report: `05 Delivery Evidence/Legacy/LG-Audit-Report.md`.

## Files
- `tools/legacy/audit/Export-AuditRaw.ps1` (ADODB raw reader, ID columns hashed at read time), `audit_phase1.py` (source integrity), `audit_phase2.py` (extractor run vs source) — `python -I`, stdlib only.
- `08 Production/tests/Hcc.Legacy.Tests/Audit/LegacyAuditTests.cs` (2 tests).
- `claude/lg-import` (2bb0b0b3, includes lg-schema 25d1f8b5) is merged into this branch so the Audit tests build; the integrator merges lg-audit after lg-import.

## Results
- Phase 1: contract §6 reproduced for all 10 FY, 0 differences; §7 reproduced exactly (bills 22 / ₹60,053; bookings 1/6/0/0/3/13/3/6/1; receipts 0/0/0/0/0/6/1/8/1). Latest CustomerInfo 18,022 rows → 12,842 valid phone keys (2,249 rows merged), 2,931 without a valid phone; 0 GuestIDs missing; GuestID carried forward (0 lost, 11 renamed 25-26 → 26-27).
- Phase 2: extractor dffdb2b9 over all ten files: counts, paise, keys, refs, customer_key, guests.csv, de-dup, Title Case all pass.
- Phase 3: Hcc.Legacy.Tests 33/33 (2 new audit tests), 0 warnings.

## Defects / deviations (ids only)
1. **lg-extract, check `secret.missing` (contract §4 "placeholder or fewer than 4 characters is no ID"):** expected 12,356 guests in `guest_ids.jsonl`, actual 11,931 — 425 guests dropped by the extra rule in `legacy_rules.real_id_number` (Aadhaar `IDCode` 8 not 12 digits; > 20 chars). Owner/coordinator: amend the contract or write them as `OTHER`.
2. Contract §7 wording: the 22 dropped bills depart 1–2 Apr 2023 (contract says 1–3 Apr). Figures unaffected.
3. Data note (no lane defect): 21 bills of 26-27 store a `BillDiscount` not taken off `NetBill` (BillNo 896, 917, 958, 966, 967, 968, 969, 1136, 1214, 1413, 1437, 1457, 1458, 1459, 1594, 1683, 1690, 1706, 1725, 1845, 1872); NetBill loaded as stored.
4. Docs: `Existing Software\DB-PROFILE\README.md` says GuestID restarts each year — wrong; it is carried forward.

## Housekeeping
`LG-EXTRACT\audit-20261031\` (raw dumps, extractor run incl. `secret\`) deleted. No names, phones, addresses, GSTINs or ID numbers in any committed file.

## Integration needed
None beyond merging this branch after lg-import.

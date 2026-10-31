# LG audit report (lane lg-audit, 31 Oct 2026)

Independent re-computation from the ten compacted files in `Existing Software\DB-PROFILE\compacted\Lodge??-??.mdb` (2026-27 = live copy of 30 Oct 2026). Readers: `tools/legacy/audit/Export-AuditRaw.ps1` (ADODB, one MDB at a time, ID columns replaced by SHA-256 of the normalised value at read time) and `tools/legacy/audit/audit_phase1.py` (`python -I`, stdlib). Counts only; row keys are legacy ids. The temporary extract folder `LG-EXTRACT\audit-20261031\` was deleted after the run.

## 1. Phase 1 — source integrity (from the MDBs only)

### 1.1 Contract §6 recomputed (computed first, compared after)

Every figure of every §6 row matches my independent computation after rounding to whole rupees: row counts (CustomerInfo, RoomBooking, TransactionEnd, CustomerDeposit, CustomerRefund), BillNo ranges (all 1…N, **no duplicate BillNo** in any FY), C / P / PaidFlag N, Σ RoomCharg, ExtraCharge, FB, Laundry, Others, DiscountAmt, LuxuryTax, FoodGSTRs, Advance (bills), NetBill, DepositAmt, Advance (deposits), SecurityDeposit, RefundAmt and the LuxuryTaxPer slab counts — **10 / 10 FY rows, 0 differences.** `SecurityDepositRefund` is 0 in every FY.

### 1.2 Duplicates inside the latest `CustomerInfo` (2026-27, 18,022 rows, GuestID 0…18,021, no repeated GuestID)

| Check | Result |
|---|---:|
| `phone_valid` rows / not valid | 15,091 / 2,931 (289 of the invalid ones still have a 10+-digit `phone_key`) |
| Distinct valid phone keys (= expected `gst.guest` rows from §1) | **12,842** |
| Valid-phone keys shared by > 1 GuestID | 1,633 groups, 3,882 rows, 2,249 surplus rows, largest group 22 |
| Same folded name + same folded address | 55 groups, 114 rows; **23 of them are not merged by phone** (different or invalid phones) |
| Same ID-number hash (12,356 rows carry a real ID) | 1,124 groups, 2,522 rows; **396 groups span more than one phone key** (same document, different phones — §1 keeps them as separate guests) |
| Distinct `customer_key` over all rows | 15,708 |
| `deleteflag = Y` | 315 |

### 1.3 Cross-file duplicates (§7 keys)

| Boundary | Bill key matches / dropped by rule 1 | Booking key matches (kept in later file) | Receipt key matches |
|---|---|---:|---:|
| 17-18→18-19 | 0 / 0 | 1 | 0 |
| 18-19→19-20 | 0 / 0 | 6 | 0 |
| 19-20→20-21 | 0 / 0 | 0 | 0 |
| 20-21→21-22 | 0 / 0 | 0 | 0 |
| 21-22→22-23 | 0 / 0 | 3 | 0 |
| 22-23→23-24 | **22 / 22, ₹60,053** | 13 | 6 (3 dated in 22-23) |
| 23-24→24-25 | 0 / 0 | 3 | 1 |
| 24-25→25-26 | 0 / 0 | 6 | 8 (7 dated in 24-25) |
| 25-26→26-27 | 0 / 0 | 1 | 1 |

Matches §7 exactly. One wording difference: the 22 dropped bills depart 1–2 Apr 2023 (the contract says 1–3 Apr). Every booking duplicate resolves to the later file under rule 2 (none has its check-out only in the earlier file).

### 1.4 Bookings without a bill / bills without a booking (`TransactionEnd.TransactionID` = `RoomBooking.BookingID`)

| FY | 17-18 | 18-19 | 19-20 | 20-21 | 21-22 | 22-23 | 23-24 | 24-25 | 25-26 | 26-27 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Bookings without a bill | 2 | 7 | 0 | 1 | 3 | 5 | 3 | 10 | 5 | 24 |
| Bills without a booking | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Bills departing outside the file's FY | 0 | 0 | 1 | 0 | 0 | 26 | 0 | 1 | 8 | 0 |

The 26-27 bookings without a bill (BookingID 1893…1932, 24 ids) are the in-house / advance bookings of the live file (cut-over set); earlier years' are the advance bookings carried into the next file (see 1.3) plus cancelled no-shows.

### 1.5 `NetBill` arithmetic (± ₹1)

Formula that holds: `NetBill = RoomCharg + ACCharge + ExtraCharge + LuxuryTax + ServiceCharges + FB + Telephone + Laundry + Others + Addition + FoodGSTRs + LaunderyGSTRs + OtherGSTRs + ElectricalExpCharges + WaterDonation + Damages + ServiceChargeAddition − DiscountAmt − BillDiscount − DonationDiscount` (`VatAmount` is **not** part of it from GST onwards; `Advance` / `BalanceDue` are not).

| FY | OK | Half-day discount also deducted | Pre-GST VAT on F&B added | BillDiscount **not** deducted | Unexplained |
|---|---:|---:|---:|---:|---:|
| 17-18 | 1,967 | 8 | 44 | 0 | 0 |
| 18-19 | 2,840 | 19 | 0 | 0 | 0 |
| 19-20 | 2,648 | 17 | 0 | 0 | 0 |
| 20-21 | 1,184 | 21 | 0 | 0 | 0 |
| 21-22 | 2,543 | 27 | 0 | 0 | 0 |
| 22-23 | 3,209 | 33 | 0 | 0 | 0 |
| 23-24 | 3,056 | 27 | 0 | 0 | 0 |
| 24-25 | 2,874 | 10 | 0 | 0 | 0 |
| 25-26 | 3,215 | 8 | 0 | 0 | 0 |
| 26-27 | 1,885 | 2 | 0 | **21** (9 C, 12 P) | 0 |

The 21 bills of 26-27 (BillNo 896, 917, 958, 966–969, 1136, 1214, 1413, 1437, 1457–1459, 1594, 1683, 1690, 1706, 1725, 1845, 1872) store a `BillDiscount` that the legacy program did not take off `NetBill` — new behaviour of the live program. The importer loads `NetBill` as stored, so reconciliation is unaffected; the owner/CA should know these 21 bills over-state net by their `BillDiscount`.

### 1.6 GuestIDs referenced but missing

Every `RoomBooking.GuestID` and every non-zero `CustomerDeposit.CustomerCode` in all ten files exists in the latest `CustomerInfo` (0 missing, every FY), and in its own file's `CustomerInfo`.

### 1.7 GuestID carry-forward

25-26 → 26-27: all 17,033 ids of 25-26 are present in 26-27; 17,022 keep the same folded name, **11 differ** (GuestID 2428, 3172, 8663, 9006, 9227, 11203, 13382, 13853, 16114, 16907, 17032 — edited names), 17,007 keep the same phone key; 989 new ids start at 17,033. Every earlier boundary behaves the same (0 ids lost; 1–35 renamed per boundary). **`GuestID` is carried forward** — the DB-PROFILE README note "GuestID restarts from 0 in every yearly file" is wrong (the ids start at 0 in every file because every file holds all earlier guests).

### 1.8 Defects found in phase 1

None against the contract: §6 and §7 reproduce exactly. Data notes for the owner: 1.5 (21 bills), 1.2 (396 ID-hash groups across phones, 23 name+address groups across phones), README wording in 1.7.

## 2. Phase 2 — extractor output vs source

Extractor `claude/lg-extract` @ dffdb2b9 run from its worktree over all ten compacted files into `LG-EXTRACTudit-20261031\ext\` (exit 0, no `SELFCHECK-FAILED.txt`); checked by `tools/legacy/audit/audit_phase2.py` against my own raw readers. Run folder deleted after the check.

| Check | Result |
|---|---|
| Row counts per FY: `lgc_stay` = RoomBooking, `lgc_bill` = TransactionEnd − de-dup | all 10 FY exact (25,718 stays; 25,636 bills = 25,658 − 22) |
| Σ paise per column (net, room, room GST, F&B, F&B GST, laundry, advance, discount = DiscountAmt + BillDiscount) | 0 paise difference in 9 FY; 22-23 differs by exactly the 22 dropped bills (net −₹60,053.00) |
| Receipts per FY and kind (payment / advance / security / refund) vs source | exact after the 16 dropped receipts (each dropped from the file whose FY does not contain its date) |
| Duplicate keys `(fy, booking)` / `(fy, bill)` / `(fy, kind, row id)` | 0 / 0 / 0 |
| `stay_ref` / `bill_ref` that do not resolve | 0 |
| No ID-number-like column in any CSV | pass (17 CSVs) |
| `lgc_guest_map` | 18,022 rows = every latest GuestID; `customer_key` recomputed from raw phone + name: 0 mismatches |
| `guests.csv` | 12,842 rows = distinct valid phone keys; 0 duplicate / missing / extra keys; name always from the highest GuestID of the key: 0 exceptions |
| §7 de-dup | bills dropped 22, receipts dropped 16, stays `Duplicate` 33 (1/6/0/0/3/13/3/6/1) — exactly the measured rows, kept in the later file |
| Title Case | 0 initials lowered; no `M/S.` names exist in the source (0 to check) |
| `secret\guest_ids.jsonl` | 11,931 rows, keys `legacyGuestId,idType,idNumber,dob,photoFile,idProofFile`; every `idNumber` hashes to the source's normalised value (0 mismatches), 0 extra, 0 duplicate guests; **425 guests with a non-placeholder ID number (≥ 4 chars) are left out** — see 2.1 |

### 2.1 Deviation: `guest_ids.jsonl` stricter than contract §4

Contract §4: an `IDNumber` is "no ID" only when a placeholder or fewer than 4 characters. `legacy_rules.real_id_number` also drops an Aadhaar (`IDCode` 8) that is not 12 digits and any value longer than 20 characters: 425 guests (12,356 expected → 11,931 written; every sampled one is `IDCode` 8, 2 have `deleteflag = Y`). Either the contract gains that rule (owner call: a mis-typed Aadhaar is probably worth keeping as `OTHER`) or the extractor writes them. The guest ids are in the audit's off-git id list; regenerate with `audit_phase2.py`.

## 3. Phase 3 — importer invariants

`claude/lg-import` @ 2bb0b0b3 (with lg-schema) merged into `claude/lg-audit`; tests in `08 Production/tests/Hcc.Legacy.Tests/Audit/LegacyAuditTests.cs` on the fictional run folder (`Fixtures/run-fictional`), disposable `HccTest_*` database through lg-import's `LegacyImportWorld`:

- `Dry_run_reconciliation_equals_an_independent_computation`: the eight figures per FY (bills, net, room GST, F&B, receipts count / paise (advance + payment), refunds count / paise) recomputed from the CSVs with the test's own CSV reader equal the importer's dry-run `LegacyReconciliation`; stays and `Duplicate` counts per FY equal the CSV.
- `Committed_load_keeps_the_lgc_invariants`: no duplicate `(fy, booking)` / `(fy, bill)` / `(fy, source, row id)`; every non-null `lgc.guest_map.guest_id` exists in `gst.guest` of the same property; no `customer_key` on `lgc.stay` / `bill` / `receipt` differing from `lgc.guest_map` for the same legacy guest; guest map = CSV rows and keys; `lgc.v_customer_stay` hides exactly the `Duplicate` stays; DB bill count, Σ net and Σ receipts per FY equal the CSV.

`dotnet test tests/Hcc.Legacy.Tests -maxcpucount:1` through the lock: **33 passed / 0 failed** (31 lg-import + lg-schema, 2 audit), 0 warnings. A real-data load was not run (not this lane's step: the dry run on this PC is the coordinator's `Run-LegacyDryRun.ps1`); the same invariants hold as SQL in the test and can be re-run against that database.

# run-fictional — fictional legacy run folder (lane lg-import creates it; lg-tests and lg-audit only ADD files)

Shaped exactly like an extractor run folder (`tools/legacy/README.md`, LG-HISTORY-CONTRACT §4): history CSVs, cut-over CSVs, `totals.json`
(`fy.<fy>.beforeDedup` / `afterDedup`), `secret/guest_ids.jsonl` + `secret/images/{photo,idproof}`. Every name, phone and ID number here is
invented (phones 98765000xx; the ID number is a made-up test value). Tests COPY this folder to a temp directory before running the importer,
because a committed `guests` part shreds `secret/guest_ids.jsonl`.

Hand-computed after-de-dup totals: 2025-26 = 2 bills, net 1,68,000 paise… see totals.json; 2026-27 = 1 bill.

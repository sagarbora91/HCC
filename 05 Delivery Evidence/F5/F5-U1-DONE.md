# F5-U1 — Accounting year end / bank / clearing screens and Internet screens (lane U1, Sonnet)

Branch `claude/f5` (worktree `/home/user/wt-f5`), commits prefixed `F5-U1:`. Screens only: every figure is an A1 selector, every change an A1 / E1 command.

## Built
- **Accounting tabs** (`03 Prototype/src/features/accounting/acc4-ui.tsx`, `acc4-views.ts`): **Year end** (financial-year picker, seven-check list, carry-forward preview / draft / approved lines, Draft, Lock, Reopen (reason >= 10 characters), Lock again, history, PROVISIONAL line), **Bank reconciliation** (Import with layout and file preview and the refusals, Match board with auto-match and manual match by selected rows and lines, Unmatched with Ignore / Create voucher / UPI-card settlement dialogs, Report with as-of date, difference shown red when not zero and CSV export, Layouts master for the Owner), **Clearing** (1010 / 1020 balance, unsettled tender days with amber over 3 / red over 7, by-day detail). Every tab has phone cards (no table on the phone). Registered in `Accounting.tsx` (ACC_TAB_IDS 24), `Mobile.tsx` chips, `subscreens.ts` (palette + role gates), `navmap.ts` keywords, `register.tsx` (7 dialogs: `acc-year-lock`, `acc-year-relock`, `acc-year-reopen`, `acc-bank-ignore`, `acc-bank-create`, `acc-bank-settle`, `acc-bank-layout`).
- Roles: year and bank tabs = Owner + Accountant (PERM-ACC-014 / 016), lock / reopen / relock and the layout master = Owner (015 / 017), Clearing also Manager (read), everyone else nothing (`acc4Gates`). The PERM-ACC-014…017 matrix rows and RBAC seeds were already on the branch (A1); the UI gates are tested against them.
- **PROVISIONAL — year reopened**: banner on the Accounting page, status line on Year end, and the stamp on the CSV / Excel / zip exports of the five ACC-3 reports (`acc4Stamp` in `accReportTables`) and on the Tally masters and vouchers XML (comment note).
- **Master configuration › Internet access** (13th group, `internet`, Software and security): `InternetPanel` in `features/admin/InternetPanel.tsx` driven by `NET_SETTING_DEFS`; master switch, six service switches, window editor; each change opens the `net-set` dialog (reason 3–120 characters) and runs `CFG_NET_SET {setting, value, reason}`. Owner changes, Manager reads, the group is hidden from every other role (`cfgListRows`, `cfgGroupAccess`, palette gate). Also on the phone hub (`mobile/admin/Settings.tsx`).
- **System health › Egress block** (`EgressBlock`; desktop table, phone cards): master, per-service state with reason, window, last change (name only for the Owner), held / waiting per queue, firewall-rule line. Owner and Manager only.
- **Banners / badge**: Messages shows "Internet off — messages queue · N held" (page sub-line, phone top, Outbox tab badge); Channel manager and Room board show "OTA bookings are not being received — last sync hh:mm" while the OTA switch is off.
- **APK**: `04 Android/capacitor.config.json` `server.allowNavigation = ["localhost"]` (the app origin only; the hotel server address is typed on the device and reached by CapacitorHttp, not navigation); README explains.
- Styles: `03 Prototype/src/styles/acc4.css` (tokens only).

## Tests
`tests/stream-accounting-ui4.mjs` (8), `tests/stream-egress-ui.mjs` (6), `tests/stream-accounting-golden4.mjs` (3). Changed pins: `stream-config.mjs` (13 groups), `stream-fit-admin-a.mjs` (13 sub-screens), `stream-fit2-accounting.mjs` (24 views), `stream-fit.mjs` phone-file allow list (by name: `features/accounting/Mobile.tsx`, `mobile/admin/Settings.tsx`).

## Integration needed (f5-int)
1. Server side of the PROVISIONAL stamp: Tally / report export endpoints and `MarkReexportAsync` (prototype only here). Not stamped yet in the prototype: trial balance, day book, ledger statement and day voucher CSVs.
2. OCR scan button "Internet off — enter the ID by hand" and the hidden payment-link actions (contract §6) are not done here; the gate is E1's (`netOn`).
3. Phone Channels page (`ChannelsMobile.tsx`) has no OTA banner (phone file, left alone).
4. Fit check (Playwright) result: see the section below.

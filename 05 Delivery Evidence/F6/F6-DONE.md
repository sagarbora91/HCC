# F6 — Hall booking (module 27) · integration DONE (3 Nov 2026)

Branch `claude/f6` (worktree `/home/user/wt-f6`) = setup + H1 (billing) + H2 (quotations, T&C) + H3 (UI, sync, prints, enquiries, server reads) + the F6-int fixes. `origin/main` merged: nothing new on it. Spec `02 Planning/modules/HCC_Module_27_Hall_Booking_Spec.md` (approved by the owner, OP-17); contract `02 Planning/agents/F6-CONTRACT.md`; lane reports `F6-H1-DONE.md`, `F6-H2-DONE.md`, `F6-H3-DONE.md`.

## What was built
- **Prototype** (`03 Prototype`): page `hall-booking` (Enquiries · Quotations · Calendar · Events · Menus · Templates), the quotation editor, the event-day modal, prints (quotation, function sheet with / without prices, tax invoice, advance receipt), phone page; stream handlers `handlers/hall.ts` (H1: accept, advances, close and bill, cancel slabs, refund, room block, credit note) and `handlers/hall-quote.ts` (H2: QUO_CREATE / EDIT / REVISE / SEND / PLACEHOLDER_OVERRIDE / DISCOUNT_APPROVE / LOST / CANCEL / EXPIRE, HALL_TNC_TEMPLATE_SAVE); sync area `hall` (`src/clients/sync/hall.ts`).
- **Server** (`08 Production`): `hall` schema (4300, 4320), quotations / T&C / menus / lost reasons (4330–4334, 4349 seed), RBAC seed PERM-HAL-001…006 (4345), read indexes (4346), print log (4348), source kind `hall` on invoices and accounting sources (4740); `HallBillingCommands`, `HallQuoteCommands`, `HallCommands`, `HallQueries`, the SQL stores, endpoints `/hall/*` and `/commands/<Op>`.

## F6-int changes
1. Merge of `claude/f6-h3`. The migration lock was regenerated with `tools/migrations-lock.mjs --add --accept-out-of-order` (211 migrations). Two H2/H3 clashes found while testing: both lanes declared `HallEnquiryRow` (H2's is now `HallQuoteEnquiryRow`), and both 4332 and 4346 created `IX_hall_quotation_version_status`. 4346 now widens the index `WITH (DROP_EXISTING = ON)`; its lock checksum was updated in place, which is allowed because the script is not on main. `Hcc.slnx` was missing H3's 4345, 4346 and 4348; they are now listed.
2. H3's UI payloads now match H2's real commands. `HALL_TNC_TEMPLATE_SAVE` sends `eventType` (it sent `templateId`). The advance plan is sent only by Manager or Owner, as plain rows (`label`, `due`, `pctBps` or `amountPaise`). The Owner gets an "Approve discount" button (`QUO_DISCOUNT_APPROVE`, hidden on the Owner's own quotations). The sync layer now maps every QUO_* command and SaveTncTemplate to H2's server commands. No `_contract-stubs-f6.ts` file was found on any F6 branch, so there was nothing to delete.
3. Gaps closed:
   - **RBAC matrix:** rows for QUO_CREATE/EDIT/REVISE/SEND/LOST/CANCEL (OWN MGR REC), QUO_PLACEHOLDER_OVERRIDE / QUO_DISCOUNT_APPROVE / HALL_TNC_TEMPLATE_SAVE (OWN) and QUO_EXPIRE (OWN MGR). The prototype's Owner-only refusals now use the code `FORBIDDEN`. The server keeps `HCC_TNC_OWNER_ONLY`.
   - **Server room block on accept:** `AcceptQuotationRequest.RoomBlock` (allotments, with defaults for arrival, departure and cut-off) creates the group block `HEV-<event>` in the same transaction through `IInventoryStore.CreateGroupBlockAsync`, which is the equivalent of the prototype's registerRoomHold. If the rooms are not free, the whole accept is refused NOT_AVAILABLE and rolled back. While `Hcc:GroupBlocks:Enabled` is off (DEC-080) it is refused GROUP_BLOCKS_OFF. The block is saved in `hall.event.block_id`.
   - **Cash:** hall cash already went through `ICashDrawerPort`, which needs an open shift (TillId, or the property's only open shift). It now also passes the B9 cash-receipt limit per event: a new `CashParty.Hall` in `CashGuardService` and `SqlFlowsStore`.
   - **Reads:** `GET /hall/menus` (per-plate price null without the view of PERM-HAL-002, DEC-061) and `GET /hall/templates` (anyone with the view of PERM-HAL-002). `_role-reads.json` was regenerated; the diff is only these two hall routes. `sync/reads.ts` READS was updated to match.
   - BASE and FY26 hashes did not need re-pinning.
   - New tests in `Hcc.Hall.Tests/Sql/HallIntegrationSqlTests.cs` (3): room block (switched off, too many rooms, success), cash limit, and the menu and template reads.

## Golden (HB-GOLDEN, prototype and server, H1)
Enquiry → quotation QT/2627/… v1 → revised v2, total ₹1,37,322.50 → sent with the T&C snapshot → accepted, ₹20,000 advance (Dr tender · Cr 2000, no GST, DEC-142) → event day, 160 plates billed (the higher of guaranteed and actual) → tax invoice in the hotel series `HPE/…` ₹1,44,609 → the advance is applied (Dr 2000 · Cr 1100), so 2000 nets to 0 → balance ₹1,24,609 to 1110/CO-1 → ledgers: 4400 Cr ₹23,750 (hall), 4200 Cr ₹98,800 (food), CGST = SGST ₹11,029.50. Cancellation at 40 days keeps 25 % → 4300, with GST.

## Verification
- `tsc` clean; `npm run build` and `build:production` OK.
- Full `npm test`: **2621/2623**. The 2 failures were the `[D5]` READS checks for the new routes. After the READS fix, `sync-roles`, `sync-hall` and `stream-rbac-matrix` pass 92/92. The full suite was not re-run after that fix.
- Prototype count: about 2623 against 2571 on main (+52).
- Fit check `hall-booking` for owner and reception at tab-land and lap-1366: 260 checks, 0 failing. Phone check at 360 px for owner, reception and housekeeper: 12 checks, 0 failing.
- **Server suite** (41 projects, one at a time): **3978 / 3984 passed**, against 3946 in 40 projects on main. The new project `Hcc.Hall.Tests` passes 31/31. The 6 failures are the known Linux-only ones (GstIds DPAPI key ring 1, Legacy absolute folder 1, Operations HostingTests 3, Stays RoomBoardQuerySqlTests page ceiling 1). Masters (35/35) was re-run after the slnx fix. Build: 0 warnings.

## Follow-up fix (coordinator, 3 Nov)
- **Cancel releases the room block on the server:** `CancelAsync` releases the event's group block while it is still Open, in the same transaction (store `OpenEventBlockAsync` → `IInventoryStore.ReleaseGroupBlockAsync`, `GroupBlockReleased` published, noted in the audit line). Cancel is the only way an accepted event ends without a booking. Lost or declined happen on a quotation before accept, when there is no block yet. A billed event keeps its block for the in-house guests. The SQL test `Accept_blocks_rooms_…` now cancels the event and checks that the block is no longer Open.
- **The Accountant's hall view is now in `/me/capabilities`** (spec §16: the Accountant views quotations and bills). `SqlCapabilitiesQuery` now reports an ACTION permission held only for viewing as mode `V`. Regenerating `_role-reads.json` added six `V` cells, all reads the server already allowed: Accountant PERM-HAL-002 and PERM-HAL-004, plus PERM-ADM-003, PERM-ADM-005, PERM-FOL-004 and PERM-MNT-005 for other roles. The PERM-ACC-001 stand-in in READS is removed.
- Re-run: `Hcc.Hall.Tests` 31/31, `Hcc.Access.Tests` 97/97, RoleReadMatrix 1/1, dotnet build 0 warnings; prototype: every `sync-*` suite 385/385, plus RBAC and hall suites 133/133; tsc clean.

## Gaps (open)
- On the server, rooms can be blocked only at accept time, not later (a later block comes from Groups).
- Cash is not recorded if `ICashDrawerPort` is missing. It is registered in production, so this only affects test setups.
- The menu master on the server has a read only, no write command, so HALL_MENU_UPSERT stays prototype-only.
- Spec §11, maintenance downtime on a hall, is not modelled.
- `Booking.hallEventId` is not written.
- QUO is not in the prototype's number-series master (H2 note).

## DEC proposals (for the register)
- **DEC-227** Hall booking spec approved (OP-17) with the A26 defaults: itemised GST per line from the dated master (CA to confirm the banquet catering slab), "OWNER TO REPLACE" T&C placeholders guarded at send time, with a one-time Owner override per quotation and a reason.
  - 3 Nov 2026: GST "CA to confirm" closed by **DEC-239** (CA: room GST 5 %, banquet incl. catering 18 %) — `Accounting/GST-CA-2026-11-03.md`.
- **DEC-228** Quotation numbering `QT/<FY>/<nnnn>` (doc type QUO), with no gaps and a restart on 1 April. Versions keep the number, a cancelled number is never reused, and only the latest version can be sent or accepted.
- **DEC-229** Advance pattern 25 % on acceptance, 50 % 30 days before the event, balance on the event day. Advances are not income (Dr tender · Cr 2000, no GST, DEC-142). Manager and Owner may edit the plan; Reception quotes the default.
- **DEC-230** Discount limits: Reception 0 %, Manager 10 % before GST, Owner any. Above the limit the Owner approves, never the author. The same limits apply on the event-day bill.
- **DEC-231** Plate billing: bill the higher of guaranteed and actual plates. Guaranteed plates may be reduced by up to 10 % after acceptance without charge.
- **DEC-232** Ledgers: 4400 Banquet & events revenue (hall, decor, services); food to 4200 (switch `foodLedger` = 4200 | 4400); kept cancellation money to 4300 with GST; advances to 2000; hall invoices in the hotel series `HPE/…`.
- **DEC-233** Migration 4740 (`hall` source kind) sits outside the F6 range 4300–4349 because 4687 (LF) re-creates those constraints. Any later migration that re-creates them must keep `hall`. The F6 range migrations are locked as accepted out-of-order history.
- **DEC-234** Hall cash goes into the open shift's drawer (shared till, DEC-052) and is subject to the B9 cash-receipt limit per event. The room block at accept time is a group block in the same transaction, following the DEC-080 switch.

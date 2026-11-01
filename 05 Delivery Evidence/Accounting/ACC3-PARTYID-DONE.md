# ACC3-PARTYID-DONE — lane partyid (DEC-193: every guest in the party has their own ID; the photo is optional)

Branch `claude/partyid` (cut from `claude/acc3` = main 829f0d91). Contract `02 Planning/agents/ACC3-REPORTS-CONTRACT.md` §8.

## Commits
- `c0bbdd9c` migration 4620, sealing per seq, register / check-in refusal, contracts, prototype rows + chips, photo optional, tests
- `2bb7a7ae` fixtures: adults / seniors carry fictional party IDs; BASE / FY26 hashes re-pinned
- final commit (see `git log`): server edit paths check "ID missing" after the stored member IDs are carried over; this file

## What was built
- **Migration 4620** `08 Production/database/migrations/4620_guest_party_ids.sql` (lock + `Hcc.slnx`): `gst.guest_party` + `id_type`, `id_last4`, `id_required` (existing rows set from the age band); `gst.guest_party_id_secret(guest_id, seq, key_id, sealed, …)` FK to the party row; `doc.id_capture.party_seq TINYINT NULL` (NULL = the registered guest). No data invented.
- **Rules** (`Hcc.Domain/Stays/GuestRules.cs`, prototype `domain/streams/guests.ts`): one constant `PartyIdRequiredBands` / `GUEST_PARTY_ID_REQUIRED_BANDS` = Adult, Senior (OP-21: children not required by default); per-member validation (type needed, per-type format, last 4 shape; messages never echo the number); registration refused with VALIDATION "ID missing for <name>"; `CleanMember` normalises the number and derives the last 4.
- **Refusals**: server `RegisterGuest`, `SaveGuestProfile` (verified), `CheckIn` (after the existing check-in rules, reads the registered party); prototype `REGISTER`, `GUEST_SAVE` (verified), `CHECKIN`.
- **Sealing**: `GuestIdProtector.Seal/Open(property, guest, seq, …)` — associated data `HCC-GUESTID-v1|property|guest|seq|keyId` (a blob does not open for another seq, guest, property, or as the registered guest's). `GuestIdService.ApplyPartyAsync` seals after the party rows are written; `CarryPartyIds` keeps a member's stored ID when the edit does not retype it and the member at that seq keeps name + ID kind; the store drops the secret otherwise. Party numbers are fingerprinted in the idempotency hash. Key rotation (`RekeyGuestIds`) re-seals party secrets too (counted with the guests'). Without a key ring a party number is refused (UNAVAILABLE), never stored in clear.
- **Masking / reveal audit**: `GuestPartyDto` + `IdType, IdNumber (write-only, never returned), IdLast4, IdMasked, IdRequired, IdOnFile` (`XXXX-XXXX-1234` for PERM-GST-003, "Restricted" otherwise). `RevealGuestIdRequest(GuestId, PartySeq?)` — PERM-GST-003, audit `GUEST_ID_VIEW` "party member <seq>" (never the number or last 4), replay re-audited. Prototype `guestView` strips party numbers; `GUEST_ID_VIEW {id, seq}`; `guestRevealPartyId`.
- **Prototype**: `types.ts` party gains optional `idType, idLast4, idNumber` (**no SCHEMA bump**); `GuestForm.tsx` party rows with ID type + ID number (desktop and phone share the form; 48 px min heights; "blank = keep" with the masked value on edit; child rows say optional); `CheckIn.tsx` red "ID missing · <name>" chips in the check-in heading and a "Party IDs" list with per-member Show ID (`GuestId.tsx` `GuestPartyIdValue`).
- Legacy import untouched (registered guest only). Form C untouched.

## Photo rules relaxed (every place a photo was asked for)
No rule anywhere enforced an ID photo; the wording that "required" one was relaxed:
1. `03 Prototype/src/features/reservations/masters.tsx` — ID-type note "photo of the document needed" → "photo of the document suggested (optional)".
2. `03 Prototype/src/domain/streams/frontdesk-master.ts` — ID-type master field "Needs a photo of the document" → "Suggest a photo of the document (optional)", help notes DEC-193.
3. `08 Production/src/Hcc.Domain/Import/ImportLists.cs` — import column `needsPhoto` help "a photo of the ID must be taken" → "is suggested (optional; …never required)".
4. `03 Prototype/src/features/guests/GuestForm.tsx` — footer "No ID images." → "An ID photo is optional — never required."; party hint "A photo is optional."
(Checked: server Documents / ID capture and StayRules never require a capture; slip / bill photos are not ID photos and are unchanged.)

## Tests (before → after)
- Prototype `npm test`: 2095 → **2103 / 2103** (new `tests/stream-guestid-party.mjs`, 8 tests `[ACC3-PARTYID]`); `npx tsc --noEmit` clean.
- Server `Hcc.Stays.Tests`: 69 → **74 / 74** (new `tests/Hcc.Stays.Tests/PartyIdTests.cs`: 3 kernel + 2 SQL).
- Server `Hcc.GstIds.Tests`: 51 → **56 / 56** (new `tests/Hcc.GstIds.Tests/GuestIds/PartyIdSealTests.cs`: 1 unit + 4 SQL).
- `dotnet build Hcc.slnx`: 0 errors, 0 warnings.

## Files outside the ownership table (needed; integrator please note)
- `03 Prototype/src/services/mock/handlers/guests.ts` (REGISTER / CHECKIN / GUEST_SAVE / GUEST_ID_VIEW — the prototype twin of RegisterGuest / CheckInStay).
- `03 Prototype/src/fixtures/ext/guests.ts`, `src/fixtures/history/guests.ts`: seed adults / seniors now carry fictional passport IDs (Z…), otherwise every scenario that registers a seeded party fails. This changes the pinned BASE / FY26 hashes, re-pinned in `tests/stream-catalogue.mjs`, `stream-frontdesk-master.mjs`, `stream-masters-fixture.mjs`, `stream-masters.mjs`, `stream-ops-master.mjs`, `stream-people-master.mjs`, `stream-perf-store.mjs`, `stream-pos-master.mjs`, `stream-roommasters.mjs` (three hashes: BASE, FY26 whole state, FY26 history). Golden figures (occupancy, collections, drawer) unchanged. **If another lane re-pins these hashes, recompute after merge.**
- `08 Production/src/Hcc.Application/Stays/StaysPorts.cs` (`GuestRecord.PartyIdsOnFile`), `Hcc.Contracts/GuestIds/GuestIdContracts.cs` (`PartySeq`), the three photo-wording files above.

## Integration needed
- `03 Prototype/src/clients/sync/frontdesk.ts` (production client): map party `idType / idLast4` from `GuestView.party` and send `idType, idNumber` per member in RegisterGuest / SaveGuestProfile; `frontdesk/shared.ts` party type gains the same optional fields. Production `GuestPartyIdValue` shows the masked value only; a server Show ID for a member = `RevealGuestId {guestId, partySeq}` via `clients/sync/guestid-ui` (button not wired yet).
- `03 Prototype/src/mobile/frontdesk/Arrivals.tsx`: render `<PartyIdChips g={g}/>` (exported from `features/guests/CheckIn.tsx`) in the phone check-in sheet heading.
- Optional: `GuestProfile.tsx` `GuestDetails` party list could show `<GuestPartyIdValue g={g} seq={i+1}/>` per member (today shown in the check-in step).

## Open items
- OP-21 (owner): children / infants need an ID? Default no; flip the one constant on both sides.
- `doc.id_capture.party_seq` exists; the Documents capture UI / API does not yet let a capture be attached to a party member.
- CA / lawyer ruling on storing full Aadhaar (DEC-068) now covers party members as well.

## HANDOFF bullet
- ACC-3 partyid (DEC-193): every adult / senior on a registration has their own ID (sealed per member, seq in the AAD; masked; audited reveal with the seq); register / check-in refused "ID missing for <name>"; photo optional everywhere; migration 4620; prototype 2103, Stays 74, GstIds 56; no SCHEMA bump.

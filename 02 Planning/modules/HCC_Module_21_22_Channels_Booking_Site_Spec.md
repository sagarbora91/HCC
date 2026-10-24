# HCC Modules 21 & 22 — OTA channel management and Direct booking website (prototype spec)

Status: prototype scope, built in stream `channels` (prefix `chn`). Phase 3 modules (Master PRD §3 rows 21–22).
**Everything is simulated.** No network call is made; channel names are fictional ("BookNest", "StayHub", "TripKart", "RoomRaahi"). Every channel screen carries a "Simulated" label and the booking site carries "Preview — not published".

## 1. Goals
1. Show how HCC would act as a light channel manager: which OTAs are connected, how hotel room types map to each channel's room codes, what rates/availability would be sent, and how inbound OTA reservations become real HCC bookings without overbooking.
2. Show a guest-facing direct booking page (phone-first) that turns a web enquiry into a **held** booking that front-desk staff confirm or decline.
3. Keep all inventory rules in one place: OTA and web bookings go through the existing `BOOK` / `CONFIRM` / `CANCEL` / `CHANGE_DATES` handlers (stream A), so availability, capacity, hold expiry and day-closed rules are identical to manual bookings.

## 2. Data (state slice `s.channelsX`, type `CHNState`)
| Entity | Fields | Notes |
|---|---|---|
| `CHNChannel` | id, name, status `Connected · Paused · Error · Disconnected`, commissionBps, markupBps, mapping {roomType → channel code}, stopSell [roomType], lastSync, statusNote | Commission/markup in basis points (1800 = 18%). Unmapped room type = not sold on that channel. |
| `CHNMessage` | id, channel, kind `New · Modify · Cancel`, ref (channel reservation no.), guest, phone, email, roomType, arrival, departure, adults, children, amount (paise, as received), receivedAt, status `Pending · Accepted · Applied · Rejected`, booking, room, reason, decidedBy, decidedAt | Inbound queue. `Accepted` = New turned into a booking; `Applied` = Modify/Cancel processed. |
| `CHNPush` | id, at, actor, channel, rows, closed, summary | Log of simulated rate/availability pushes. |
| `outbox` | scripted CHNMessage list | Delivered one at a time by "Check for new messages (simulated)". |
| `CHNWebRequest` | id, booking, guest, phone, email, roomType, arrival, departure, adults, children, net, tax, total, createdAt, status `Pending · Confirmed · Declined`, reason, decidedBy | A web request always owns a Held booking (source `Website`). Expiry is derived from the booking hold. |

Absent slice (old saves) = empty. Fixtures (`fixtures/ext/channels.ts`, every scenario): 4 channels, 6 pending inbound messages (incl. one capacity problem and one modification), 1 historical rejection, 2 push log entries, 3 scripted messages. No bookings or money are added to the base fixture (keeps occupancy 8, collections ₹21,500, drawer ₹10,200).

## 3. Rules
### 3.1 Channels (Module 21)
- R1 Connection is simulated: Connect / Retry → `Connected` with `lastSync = s.clock`; Pause → `Paused`; Disconnect → `Disconnected`. TripKart starts in `Error` ("Simulated: credentials expired") to show the failure state.
- R2 Mapping: each hotel room type (Classic, Deluxe, Suite) maps to a channel code (2–20 chars, A–Z 0–9 -). Codes must be unique within a channel. Blank = unmapped.
- R3 Commission 0–30 %, rate markup 0–25 % (rate parity uplift shown in the push preview).
- R4 Stop-sell per channel × room type (or "all channels" for a type). A stop-sold or unmapped type pushes `closed`.
- R5 **Push preview** (pure, `chnPushPreview`): for each mapped type and the next 7 nights from the business date: rooms available that night (not blocked, no Confirmed / In house / live Held booking, via `available()`), rate = `rateFor(room, date)` × (1 + markup), closed if stop-sold or 0 available. "Push now" (`CHN_PUSH`) only for `Connected` channels; it records a log entry and updates last sync. Nothing leaves the device.
- R6 **Inbound New**: pre-check `chnAcceptCheck` → channel known, ref not already accepted, room type mapped on that channel, arrival ≥ business date, party ≤ room-type capacity, at least one room of that type free for every night. Accept (`CHN_ACCEPT`) picks the first free room of the type (or the room staff chose) and calls the existing `BOOK` handler with `source:'OTA'`, `otaChannel:<channel name>`; the hotel's tariff applies (agreed rate snapshot). On conflict Accept fails with `NOT_AVAILABLE` and the reason; staff then **Reject with reason** (`CHN_REJECT`, reason required, prefilled from the pre-check). A stop-sell does not block accepting a reservation already sold (shown as a warning).
- R7 **Modify**: if the original New is still pending, its dates/party are updated in the queue. If accepted, the booking is moved via `CHANGE_DATES` (same room if still free, else another free room of the type) and party size updated with capacity check. In-house or departed stays are refused ("handle at the front desk").
- R8 **Cancel**: if the original New is pending, it is rejected ("Cancelled by channel before acceptance"). If accepted, the booking is cancelled through `CANCEL` (fails if an advance is held — refund first, same as manual).
- R9 **Commission report** (`chnCommissionReport`): OTA bookings whose `otaChannel` matches a channel; status Confirmed / In house / Departed; base = agreed rate × nights (room revenue, excluding synthetic tax); commission = base × commissionBps; net = base − commission. Per-channel totals + per-booking rows. Cancelled/expired excluded.
### 3.2 Direct booking site (Module 22)
- R10 Guest page (preview): hotel name (`settings.hotel`), date search (check-in ≥ business date, 1–30 nights), adults 1–4, children 0–3; one card per room type with capacity, rooms left, rate per night, total for stay, synthetic tax (`taxRateFor(arrival)`), grand total. Sold-out / over-capacity types show the reason and cannot be selected.
- R11 Guest details: name (≥ 2 chars), phone (fictional, 7–16 digits), email optional, special requests ≤ 200 chars. Submit → `CHN_WEB_REQUEST` → `BOOK` with `hold:true`, `source:'Website'` → Held booking for `RES_HOLD_MINUTES` (120 simulated minutes). Confirmation screen shows request ID, "Pending confirmation by the hotel", and the quote.
- R12 Staff panel: pending requests with hold time left. Confirm (`CHN_WEB_CONFIRM` → `CONFIRM`) while the hold runs; Decline with reason (`CHN_WEB_DECLINE` → `CANCEL`; if already expired just records the decline). Expired holds show "Expired" and can only be declined.
- R13 No payment is taken on the preview page ("Pay at hotel · simulated"). Nothing is published.

## 4. Commands (all `CHN_*`, via `execute`)
| Command | Permission | Day-closed check | Effect |
|---|---|---|---|
| CHN_CHANNEL_STATUS {channel, action: connect\|pause\|disconnect} | approve | no | Simulated connection state |
| CHN_CHANNEL_SAVE {channel, commissionBps, markupBps, mapping} | approve | no | Commercial terms + mapping |
| CHN_STOP_SELL {channel\|'ALL', roomType, on} | reservations | no | Stop-sell toggle |
| CHN_PUSH {channel} | reservations | no | Log simulated push (Connected only) |
| CHN_PULL {} | reservations | no | Deliver next scripted message from a connected channel |
| CHN_ACCEPT {id, room?} | reservations | yes (via BOOK etc.) | New → booking; Modify/Cancel → applied |
| CHN_REJECT {id, reason} | reservations | yes | Reject a pending message |
| CHN_WEB_REQUEST {roomType, arrival, departure, adults, children, guest, phone, email, notes} | reservations (staff-operated preview) | yes | Held booking + request |
| CHN_WEB_CONFIRM {id} | reservations | yes | Held → Confirmed |
| CHN_WEB_DECLINE {id, reason} | reservations | yes | Release hold / record decline |
Commercial configuration uses `approve` (Owner, Manager) on the coarse model; Reception sees it read-only. Idempotency keys handled by `execute`; repeated decisions on a decided message fail `INVALID_TRANSITION`.

## 5. Screens
| Page | Desktop | Phone |
|---|---|---|
| `channels` (nav REVENUE & SALES, `reservations`) | Tabs: **Inbox** (table with pre-check result, Accept / Reject), **Channels & mapping** (cards per channel: status, last sync, commission, mapping, stop-sell toggles, Connect/Pause), **Rate & availability push** (channel picker, 7-night grid preview, Push now, push log), **Commission report** (per channel + per booking, printable). KPIs: pending messages, connected channels, OTA bookings, commission. | Chips Inbox / Channels / Push / Commission; list cards; message sheet (`chn-message`) with Accept / Reject; channel sheet (`chn-channel`); push preview as per-type cards; sticky "Check for new messages" on Inbox. |
| `booking-site` | Left: phone-sized preview frame of the guest page; right: staff panel of web requests. | Chips Preview / Requests; preview is full-width (phone-first); requests as cards with Confirm / Decline. |
Modals: `chn-message`, `chn-reject`, `chn-channel`, `chn-web-decline`.

## 6. Acceptance (tests in `tests/stream-channels.mjs`)
- Accept New creates a Confirmed OTA booking with the channel name, correct room type, hotel tariff; occupancy figures unaffected until check-in.
- Accept with no free room → `NOT_AVAILABLE`, state unchanged; reject requires reason.
- Capacity, unmapped type, duplicate ref and past-arrival pre-checks.
- Modify pending/accepted; Cancel pending/accepted (booking cancelled).
- Push preview availability falls when a booking is added; stop-sell closes the type; push refused for non-connected channel.
- Commission report figures (18% of agreed room revenue).
- Web request → Held booking with quote incl. synthetic tax; confirm → Confirmed; decline releases inventory; expired hold cannot be confirmed.
- Permissions: housekeeper denied; reception cannot change commission; idempotent repeat.

## 7. Out of scope / production notes
Real OTA APIs (ARI push, reservation pull/webhooks, certification), payment gateway on the website, rate plans/restrictions (min stay, CTA/CTD), per-channel inventory allotments, OTA virtual cards, GST on commission invoices, publishing/hosting the website, guest authentication. Production would add a `channel` permission set and a background sync worker.

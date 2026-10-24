# HCC Module 24–25 · WhatsApp / notifications and OCR / document capture — prototype spec

Stream `messaging` · prefix `msg` / `MSG` · state slice `s.messagingX` (type `MSGState`) · pages `messages` (permission `reservations`) and `documents` (permission `checkin`), nav group FRONT DESK.

**Everything is simulated.** No message is ever sent, there are no network calls, no real OCR and no camera access. Every screen carries a visible "Simulated" label. Fictional data only.

## 1. Goals
- Show how guest notifications would work: templates with placeholders, channel choice (WhatsApp / SMS / Email), consent and opt-out, an outbox with delivery statuses, manual compose, and trigger rules.
- Show how document capture would work: pick a fictional sample, "extract" fixture fields with confidence scores, staff review and correct, then confirm into a guest profile or an expense draft.
- Never store full ID numbers or document images (DEC privacy rule, REQ-GUEST-006): only ID type + last 4 characters reach a guest profile.

## 2. State (`MSGState`, all optional in old saves)
| Field | Meaning |
|---|---|
| `templates` | Six templates: `booking-confirmation`, `pre-arrival`, `checkin-welcome`, `payment-receipt`, `checkout-invoice`, `feedback`. Each: name, subject (email), body with `{placeholders}`, default channel. |
| `rules` | Trigger rules: event → template + channel, enabled flag. |
| `consent` | Per guest profile (or `B:<booking>` when no profile): `whatsapp`, `sms`, `email` booleans, `optOut`, `updatedAt`, `by`. Missing record = all channels allowed, not opted out (transactional consent captured at booking in this demo). |
| `outbox` | Messages: id, template (or `manual`), channel, booking, guest, masked recipient, rendered subject/body, `queuedAt` (simulated clock), predetermined `outcome`, `boost` minutes (Simulate delivery), `blocked` reason, `retryOf`/`retriedBy`, actor, trigger rule. |
| `fired` | Trigger keys already evaluated (`<rule>:<booking or receipt>`) — makes rule evaluation idempotent. |
| `docs` | Captures: sample id, kind, status (`Extracted` → `Applied` / `Discarded`), fields `{key,label,value,confidence,original?}`, applied target, actor, times. |

## 3. Messaging rules
- **Placeholders** `{guest} {firstName} {hotel} {booking} {room} {arrival} {departure} {nights} {checkin} {checkout} {amount} {balance} {invoiceLink} {feedbackLink}` are filled from the booking, its guest profile, settings and folio totals. Links point to `https://demo.invalid/...` and are marked simulated. Unknown placeholders are rejected when a template is saved.
- **Channels**: WhatsApp and SMS need a phone; Email needs an email. SMS shows segment count (160 chars). Email has a subject.
- **Consent**: opted-out guests or a channel without consent → nothing is queued by manual compose (VALIDATION error); trigger rules record the message as **Not sent** with the reason (visible, never delivered).
- **Delivery simulation** (pure, from the simulated clock): Queued → Sent after 1 min → Delivered after 3 min → Read after 8 min (WhatsApp only; SMS and Email stop at Delivered). Outcome is predetermined at queue time: invalid phone (not 10 digits) or a deterministic 1-in-7 hash → **Failed** at minute 2. "Simulate delivery" adds minutes to jump one step. A failed message can be **retried** once per message (new message, outcome forced to success).
- **Manual compose** only to an **in-house** guest; body 1–600 characters; optional template pre-fill.
- **Trigger rules** are evaluated by `msgPendingTriggers(s)` (selector) and queued by `MSG_RUN_RULES` — no other stream's handlers are edited.
  | Rule | Event condition | Template |
  |---|---|---|
  | R1 | Booking status Confirmed | booking-confirmation |
  | R2 | Confirmed, arrival = business date or next day | pre-arrival |
  | R3 | In house | checkin-welcome |
  | R4 | Each `payment` receipt on a booking | payment-receipt |
  | R5 | Departed | checkout-invoice |
  | R6 | Departed | feedback (disabled by default) |
  Fixture marks the events of seeded bookings as already fired, so new bookings / check-ins / payments / checkouts made in the demo appear as pending triggers.

## 4. Document capture rules
- Samples (fixture): 2 ID cards (Aadhaar-style and passport-style, fictional), 2 vendor bills, 1 company letter. Rendered as a drawn illustration — no image is stored.
- **Extract** (`MSG_DOC_EXTRACT`) copies the sample's fixture fields with confidence 0–1. The ID number is only ever held masked (`XXXX XXXX 7342`); the "last 4" field is separate.
- **Review**: fields below 0.80 confidence are flagged; Confirm requires every flagged field to be ticked as reviewed or corrected.
- **Confirm** (`MSG_DOC_CONFIRM`):
  - ID card → chosen guest profile gets `idType` + `idLast4` (exactly 4 letters/digits) only; profile version bumps. Name mismatch shows a warning (not blocking).
  - Company letter → chosen guest profile gets company name + GSTIN (validated with the guest GSTIN rule).
  - Vendor bill → runs the existing `EXPENSE` handler with `draft:true` (vendor, category, amount in paise, funding source, receipt placeholder text, notes with bill no.). Needs `expenses` permission; day-closed rule applies. The draft then follows the normal Expenses flow (submit → another person approves).
- **Discard** (`MSG_DOC_DISCARD`) keeps the record as Discarded (audit trail). Applied/discarded captures cannot be confirmed again (INVALID_TRANSITION).

## 5. Commands
| Command | Permission | Notes |
|---|---|---|
| `MSG_SEND {booking, channel, body, template?, subject?}` | reservations | In-house only; consent; recipient required. |
| `MSG_RUN_RULES {only?}` | reservations | Queues all (or listed) pending triggers; idempotent via `fired`. |
| `MSG_SIMULATE {id?}` | reservations | One message or all non-final ones advance one step. |
| `MSG_RETRY {id}` | reservations | Failed only, once. |
| `MSG_CONSENT {guestKey, whatsapp, sms, email, optOut}` | reservations or checkin | Opt-out clears queued (not yet sent) messages to that guest → Not sent. |
| `MSG_TEMPLATE {id, body, subject?, channel?}` | approve | Placeholder whitelist, ≤ 600 chars. |
| `MSG_RULE {id, enabled?, channel?}` | approve | |
| `MSG_DOC_EXTRACT {sample}` | checkin | |
| `MSG_DOC_CONFIRM {id, fields, reviewed[], guestId? , method?}` | checkin (+ expenses for bills) | |
| `MSG_DOC_DISCARD {id, reason?}` | checkin | |
All commands accept an idempotency `key`; all are audited via `execute`.

## 6. Screens
- **Desktop `messages`**: KPI strip (queued, delivered/read, failed, not sent, pending triggers); tabs Outbox / Triggers / Templates / Consent. Outbox table (guest, channel, template, status pill with text, time) with Simulate delivery / Retry; message dialog `msg-message` shows the bubble preview + status timeline. Compose dialog `msg-compose` (in-house guest, channel, template, live preview, consent warning). Template editor `msg-template`; consent editor `msg-consent`.
- **Desktop `documents`**: sample picker cards (illustration labelled "Fictional sample"), review panel (fields with confidence bars, correction inputs, reviewed ticks, target selection), capture history.
- **Phone `messages`**: chip filter Outbox / Triggers / Consent; outbox as chat-style preview bubbles with status ticks + text; pending-trigger card with one button; FAB "Compose" → full-screen sheet.
- **Phone `documents`**: camera button placeholder (explains it is simulated), sample cards, review as the `msg-doc` sheet with Stepper (Extract → Review → Confirm), sticky primary action.

## 7. Roles
Owner, Manager, Reception see Messages (reservations) and Document capture (checkin). Template/rule edits: Owner, Manager (approve). Housekeeping, maintenance, accountant and tech admin see neither page; no money is shown to housekeeping roles.

## 8. Tests (`tests/stream-messaging.mjs`)
Placeholder rendering, unknown placeholder rejected, consent/opt-out blocking, in-house-only compose, permission denial, delivery timeline from the clock, simulate/retry, rule evaluation idempotent (fixture has no pending triggers except today's arrivals; new BOOK → pending confirmation), extraction confidence + review requirement, ID apply stores last 4 only (no full number anywhere in state), expense draft via EXPENSE, double confirm rejected, idempotency keys, S1 empty state.

# HCC Module 26 — Management assistant (simulated, rule-based)

Status: prototype spec · stream `assistant` · prefix `ai` / `AI_` · state slice `s.assistantX` · page `assistant` (nav CLOSING & REPORTS, permission `reports`).

## 1. Purpose
Give the owner/manager a chat-style place to ask plain questions about today's operations ("How full are we tomorrow?", "Who owes us the most?") and get a precise, explained answer with a link to the screen that holds the detail. **No real AI and no network:** a keyword matcher maps the question to one of a fixed set of intents and a pure selector computes the answer from the current demo state. Every screen carries the label **"Simulated assistant · rule-based · answers computed from this demo's data"**.

## 2. Intents (keyword matched, case-insensitive)
| Intent | Example | Computation (pure selector over State) | Go there |
|---|---|---|---|
| occupancy-today | "occupancy today" | in-house stays ÷ sellable rooms tonight (not blocked), plus arrivals still due → projected | rooms, reports |
| occupancy-tomorrow | "how full tomorrow" | Confirmed + In house stays covering the night (arrival ≤ d < departure) ÷ sellable rooms that night | reservations |
| occupancy-7d | "next 7 days" | the same per night for business date … +6, average | reservations, yield |
| arrivals | "arrivals today" | Confirmed with arrival ≤ date (due, incl. late) + already checked in today | arrivals |
| departures | "who checks out" | In house with departure ≤ date (due/overdue) + balance if money is visible | folios |
| adr | "ADR" | tonight's room revenue ÷ occupied rooms; posted room line for tonight, else the agreed rate (counted and stated) | reports |
| revpar | "RevPAR" | the same room revenue ÷ sellable rooms tonight | reports |
| collections | "collections vs outstanding" | today's net receipts (payments − refunds) vs positive folio balances; deposits held shown separately | reports, folios |
| cash | "cash drawer variance" | expected drawer (opening + cash in − cash out − posted cash expenses), shift status, submitted count and difference | cashier |
| approvals | "pending approvals" | pending approvals by kind (+ amount) and pending expenses | approvals, expenses |
| rooms | "dirty or blocked rooms" | Dirty / Cleaning / Inspection rooms and rooms blocked tonight (with reason) | rooms, housekeeping |
| expenses-week | "expenses this week" | posted (and reversed) expenses dated business date −6 … date, by category; pending count | expenses |
| balances | "top balances due" | five largest positive balances on open or departed folios | folios |
| hk-backlog | "housekeeping backlog" | open tasks by status and assignee, oldest age on the simulated clock | housekeeping |
| day-close | "ready to close the day?" | the Day-closing preflight (stream F) — blocking checks and item counts | closing |
| briefing | "daily briefing" | the briefing card (section 4) | overview |
| anomalies | "any problems?" | the anomaly flags (section 5) | per flag |
| unknown | anything else | fallback that lists the topics this user can ask about | — |

Matching: each intent has keyword patterns; the highest score wins, ties go to the more specific intent (e.g. "top balances" beats "outstanding", "RevPAR" beats "ADR"). "tomorrow" / "week / 7 days" modifiers turn occupancy into the dated variants.

## 3. Permissions (no new permission values)
- Topics are filtered per user with the coarse `can()` gate:
  - **Money** (ADR, RevPAR, collections, cash, balances, expenses, money amounts inside approvals/departures/briefing/flags): user is not a Housekeeper/Maintenance role **and** has one of `reports, billing, payments, close, approve`.
  - Front-desk counts (occupancy, arrivals, departures): `reservations, checkin, billing` or `reports`.
  - Rooms and housekeeping backlog: `housekeeping, inspect, reservations` or `reports`.
  - Approvals: `approve` or `reports`. Day-close readiness: `close` or `reports`. Cash drawer: money **and** one of `payments, close, reports`.
- A restricted topic returns a polite "not available for your role" answer with no figures. Housekeepers and Maintenance never receive money in any answer, briefing line or flag.
- "Go there" links are shown only for pages the user may open.
- The page itself is gated by `reports` (nav). `AI_ASK` is open to every enabled user because the answer is filtered anyway.

## 4. Daily briefing card
Auto-generated summary of the business date: occupancy now and projected, arrivals/departures due, rooms not ready, open housekeeping tasks, pending approvals, and (money users) collections, outstanding, expected drawer; ends with day-close readiness and the count of anomaly flags. Each line links to its screen.

## 5. Anomaly flags (severity shown as text: High / Medium / Low)
| Flag | Rule | Severity |
|---|---|---|
| Large discount | discount line in the last 7 days larger than the discount limit (settings) | Medium; High if > 2× limit |
| Cash difference | today's submitted count ≠ expected | High if > ₹200 (manager limit), else Medium |
| Drawer formula mismatch | shift formula parts ≠ drawer() | High |
| Dirty room too long | open clean task for a Dirty room older than N hours (default 4) on the simulated clock; tasks without a timestamp are aged from 00:00 of the business date and say so | Medium; High if > 2N |
| Arrival into a dirty room | arrival due today, room not Ready | Medium |
| Unbalanced folio | departed stay with balance ≠ 0 or deposit still held | High |
| Overdue departure with balance | In house, departure ≤ date, balance > 0 | Medium |
| Credit balance | open folio overpaid (balance < 0) | Low |
| Pending approval waiting | pending approval/expense on the business date | Low |
Money flags are hidden for users without money visibility. Thresholds (N hours) are stored in the slice and changed by `AI_SETTINGS` (`approve` permission).

## 6. Commands
| Command | Permission | Effect |
|---|---|---|
| `AI_ASK {question}` | any enabled user | computes the answer from the current state for the asking user and appends `{id, at (sim clock), date, question, answer snapshot}` to that user's history (max 50, oldest dropped). Question 1–300 characters. Idempotent through the command key (the UI sends one key per send, so a double tap records once). Read-only towards hotel data; allowed on a closed day. |
| `AI_CLEAR` | any enabled user | clears the caller's own history only |
| `AI_SETTINGS {dirtyHours}` | `approve` | dirty-room threshold 1–48 hours |

Answers are stored as snapshots (they record the figures at the time asked); the UI offers "Ask again" to recompute.

## 7. Screens
- **Desktop** `assistant`: simulated label; left = conversation (bubbles, answer figures, "How this was computed", Go there buttons), suggested chips, text box + Ask; right = Daily briefing card and Anomaly flags card.
- **Phone** `assistant`: segmented chips Ask / Briefing / Flags; Ask shows chat bubbles, horizontally scrolling suggestion chips and a sticky input above the tab bar (`enterKeyHint="send"`, 48 px targets, 16 px text); briefing and flags as cards.

## 8. Out of scope
Real language models, voice, network calls, actions taken by the assistant (it only reads and links).

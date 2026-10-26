# Hotel Control Centre — Interactive Prototype

A clickable prototype for internal hotel operations. All data is **fictional**. Everything runs in the browser against an in-memory mock service. There is no SQL Server, no real sign-in, no payment gateway and no GST validation.

Planning documents are in `..\02 Planning\`. Test and walkthrough evidence is in `..\05 Delivery Evidence\`.

## Prerequisites

- Node.js 20 or later (tested on Node 22).
- Windows, macOS or Linux. `npm install` downloads the build tool binary for your platform.

## Commands

Run these from this folder (`03 Prototype`).

| Command | What it does |
|---|---|
| `npm install` | Install dependencies (first time, or after copying the folder to another PC) |
| `npm run typecheck` | TypeScript check (must report no errors) |
| `npm test` | Domain and mock-service tests; writes `evidence/domain-tests.json` |
| `npm run build` | Builds `dist/index.html` and `Hotel Control Centre.html` |
| `npm start` | Serves the build at http://127.0.0.1:4173 (this PC only) |
| `npm start -- --lan` | Also serves on the local network so tablets and phones can open it; prints the Network URL |

The built file is self-contained, so opening `Hotel Control Centre.html` directly usually works too. Serving it with `npm start` is the documented and tested route.

### Tablets and phones (LAN mode)

1. Run `npm run build`, then `npm start -- --lan`.
2. On the tablet, open the printed `Network:` URL. The tablet must be on the same Wi-Fi network.
3. If it does not load, allow **Node.js** through Windows Defender Firewall for **Private** networks.
4. Every device keeps its **own** demo data in its own browser. Nothing is shared or synchronised between devices; that is production scope.

## Demo controls (the cream strip under the header)

- **Scenario** — load a fixture:
  - **BASE** — normal day
  - **S0** — owner walkthrough
  - **S1** — empty hotel
  - **S2** — Room 105 blocked
  - **S3** — exceptions day
  - **S6** — access policy

  S4 and S5 show as "not yet built" until M2/M3.
- **Preview as** — switch the demo user. This is a demonstration tool, not sign-in.
- **Next action** — simulate a fault on the next action only:
  - **Slow response** — waits 2 seconds; repeated clicks are ignored while it waits.
  - **Server rejects** — nothing changes.
  - **Lost response** — the action is saved but the confirmation is lost. Retrying the same dialog does not duplicate it.
  - **Stale data** — nothing changes.
- **Reset** — reload the current scenario from its fixture.
- **Walkthrough** — the guided steps.

## Data and persistence

- Demo data is saved in this browser's local storage under `hcc-prototype-v2`, so a refresh keeps your changes.
- If the stored data is from an older version, or unreadable, it is replaced with the default scenario (FY 2026-27 history, FY26) and a notice explains why. Saved state lives in IndexedDB (database hcc), with localStorage only as a small-state fallback.
- Clearing browser data removes demo edits. Any scenario can always be reloaded.
- Times use a **simulated hotel clock** (Asia/Kolkata). It starts at 08:00 on the business date and moves forward one minute per action. The real clock is never used, so test results do not depend on the day you run them.

## Code layout

```text
src/
  main.tsx              entry point
  app/                  shell, navigation, context, storage adapter
    demo/               demo toolbar and dialogs (removable for production)
  features/             one folder per area: overview, rooms, reservations, folio,
                        housekeeping, expenses, closing, reports, admin
  clients/              typed feature clients (mock + HTTP adapters); clients/sync/ = production server sync layer (Wave 4, see its README.md)
  components/           shared presentation components
  domain/               types, typed errors, money/date helpers, selectors (pure)
  services/mock/        command service (execute), fault simulation, persistence format
  fixtures/             base fixture, scenarios S0–S6 and FY26 (fixtures/history/: seeded generator for 1 Apr–23 Oct 2026, incl. module 13–26 history)
  styles.css, styles/   design styles
  domain.ts             compatibility re-export of services/mock (older imports)
tests/                  run.mjs (original suite) + m1.mjs (M1 suite)
```

Every change goes through `execute(state, user, command)` in `services/mock/commands.ts`. It checks permission, scope and state, and either returns a new state or throws an `HccError` with a code: `FORBIDDEN`, `NOT_FOUND`, `NOT_AVAILABLE`, `INVALID_TRANSITION`, `VALIDATION`, `STALE_VERSION` or `DAY_CLOSED`. Commands carry an idempotency key; repeating a key returns the earlier result.

## Known limitations (M1)

- **Coarse permissions.** The model has 12 permissions per role. The full resource/action, scope, limit and field-masking matrix arrives in M3. Accountant, Maintenance and Tech admin use approximate grants until then.
- **"Post stay charges" still pre-posts future nights.** Nightly posting and the three-day walkthrough script arrive in M2.
- **Missing screens.** These are not built yet: calendar, guest profile, check-in stepper, cashier shift, approvals/audit, backup, and configuration history.
- **Emulated layout checks only.** Mobile layout has only been checked in an emulated 390×844 browser. No physical Android test has been done.
- **No sharing between devices.** Mock state is per browser.

## Use on an Android phone over Wi-Fi

1. On the Windows PC, in `03 Prototype`: `npm run build`, then `npm start -- --lan`. The console prints the PC's network address, e.g. `http://192.168.1.20:4173`. Allow Node.js through Windows Firewall on **private** networks when asked.
2. Connect the phone to the **same Wi-Fi**, open Chrome and type that address. The phone layout opens automatically on narrow screens; to force it, tap the avatar (or **More → Display mode**) and choose **Mobile**.
3. Demo controls are behind the **DEMO** pill in the app bar: scenario, preview-as user, next-action fault, reset and the S0 walkthrough (each step has **Go there**).
4. **Demo data lives on each device.** The phone and the PC (or two phones) do not share bookings, tasks or cash — each browser keeps its own copy. Use **Reset** to start again.

**Add to Home screen.** The page carries a web app manifest (name "Hotel Control Centre", short name "HCC", maroon icon with a white "H", standalone display). Over plain `http://` on the LAN, Chrome treats the page as insecure, so **Add to Home screen** creates a normal shortcut (opens in a browser tab with the address bar) rather than an installed full-screen app, and there is no offline mode (no service worker in the prototype). A full-screen install needs HTTPS or the Capacitor APK planned for production (Mobile plan §5, DEC-08). On `http://127.0.0.1` on the PC itself the manifest is honoured normally.

# ADM-UX - Administration switches: usability fixes (5 Nov 2026)

Branch `claude/adm-ux` (from main 403d3172). UI only: no server change, dependants cascade unchanged. Tester could not reproduce a bug; five usability problems fixed.

| # | Change | Files (03 Prototype) |
|---|---|---|
| 1 | Phone menu: top-level "Admin & Setup - Modules on/off, users, roles, audit" entry (only when `permission('admin')`: Owner, Tech admin; Manager does not see it). Modules Home tile caption now "Modules on/off, users, roles, audit, backup". | `src/app/NavDrawer.tsx`, `src/app/navmap.ts` |
| 2 | The On/Off pill is a native `<button role="switch" aria-checked>` (Space/Enter work natively) running the same handler as the "Switch on/off X" button (same confirm, SETTING_SET, rules). The row is tappable where allowed. Existing button aria-labels kept. Unchangeable rows get a plain, non-button pill and no tap. | `src/features/admin/config/panels.tsx` |
| 3 | Locked rows show a lock icon plus a plain sentence ("Always on - the hotel cannot run without it.", "Switch Messages on first - Templates & rules needs it.", "Switch the module on first."), grey background, no hover. | panels.tsx, `src/styles/v22-fixes.css` |
| 4 | Dependants still stay off. Switching a parent on shows "Messages switched on. Templates & rules stays off - switch it on separately."; dependant rows keep "Off with it" and a Switch on button, plus a note "Stayed off when the part it needs was switched back on". | panels.tsx, `config/logic.ts` (`cfgSwitchOnNote`) |
| 5 | Config-change toasts last at least 8 s: `RunOptions.toastMs` and `toast(text,error,minMs)`; `toastDuration()` in `prefs.ts` never shortens the personal setting and keeps "until dismissed". Other toasts unchanged. | `context.tsx`, `App.tsx`, `prefs.ts` |

CSS only in `v22-fixes.css` (not frozen). No phone-layout file (FIT guard patterns) was changed, so the FROM_MAIN allowlist needed no entry. Line endings preserved per file.

Tests: new `tests/stream-adm-ux.mjs` (7 tests, [ADM-UX]): switch role/native button; pill = button command incl. confirm and 8 s option; row tap; locked rows and Manager; dependants-still-off note and rows; toast duration rules; drawer entry for Owner/Tech admin and not Manager. A spy for `react/jsx-runtime` replays real onClick handlers (no DOM library in the repo); Space/Enter is the browser's button behaviour, asserted by `type==='button'`.

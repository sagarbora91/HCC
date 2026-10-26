# CP-5 themes — screenshot pass (27 Oct 2026)

Build: `npm run build` of branch `claude/cp5-themes`, served with `PORT=4182 node serve.mjs`, Playwright (Chromium), signed in as Sagar · Owner, scenario FY26.
Theme switched through the real preference (`hcc-prefs-1` + storage event, the same path as a second tab). 30 screenshots: `<width>-<theme>-<page>.png`.

| Width | Themes | Pages |
|---|---|---|
| 1366 × 900 (desktop shell) | light, dark, hc | Modules Home, Room board, Guests & folios, Reports, Appearance & display |
| 375 × 812 (phone shell) | light, dark, hc | same five pages |

Checks on every screenshot (computed styles in the live page, not the static audit):
- `<html data-theme>` matched the chosen theme; `<meta name="theme-color">` = #7a1f2b (light) / #ae4155 (dark) / #5e1620 (hc).
- Every visible text element vs its effective solid background: **0 below 4.5:1 in light and dark, 0 below 7:1 in high contrast** (28–105 text elements per page; skipped: disabled controls, the preview cards, text over gradients/images, and the phone avatar letter whose white disc is a ::before).
- No horizontal overflow at 375 px; **no console errors** in the whole run.

Found and fixed during the pass: the size names under the text-size "A" buttons (`.pf-seg button.on small`, CP-1) used --muted on the maroon selected button (1.5:1 in light); now inherit the button's text colour in every theme.

Unreadable elements: none found. Not covered: print preview (print uses the light palette by construction — dark/hc blocks are `@media screen`), the guest-ID dialog (`src/clients/sync/guestid-ui.tsx`, inline white dialog with dark text — readable in every theme but not themed), demo data screens not listed above.

# Admin module / sub-function switch check, APK v23 (vc24 demo), phone AVD codex_api35_google

Date 5 Nov 2026. Branch claude/af2-int (not touched). Emulator-5554 only; physical R52N807PTTE untouched; emulator shut down at the end.
Screenshots: `05 Delivery Evidence\Acceptance\v23\admin-*.png`.

## Result: NOT reproduced as Owner (phone layout and desktop layout)

### Owner (Sagar), Mobile layout
Path: Modules Home > scroll to last tile "Admin & Setup" > "Administration" > tab "Modules" (also Admin & Setup > "Master configuration" > "Modules on/off").
- Restaurant & POS Switch off: pill On -> Off, button -> "Switch on", toast "Restaurant & POS switched off · data kept" (admin-09/10). Cash & Billing tile list then has no restaurant entry (admin-11). Switch on: pill On, toast "Restaurant & POS switched on" (admin-17).
- Sub-function with dependant (Messages, dependant Templates & rules): window.confirm DOES appear in the Capacitor WebView ("Switching off Messages also switches off: Templates & rules. Continue?", CANCEL / OK; admin-14). OK -> Messages Off, "Off with it: Templates & rules", toast "Messages switched off · data kept" (admin-15). Switch on works (admin-16). Note: switching Messages back on does not restore Templates (stays off; Master configuration then says "46 of 47 sub-functions on").
- Tapping, toasts, pill state, re-render all fine.

### Owner, Desktop display mode (Demo controls > Display mode > Desktop) on the phone AVD
Master configuration (master/detail list) > "Modules on/off": same switches, pill and button change, toast shown (admin-31, 32). Layout is cramped (app bar clipped: breadcrumb truncated, DEMO pill cut off at right edge) but nothing needed is off-screen. Detail pane opens in place with "Back to list". Same component (CfgModulesPanel/CfgFeaturesTree), so confirm behaves the same. Tablet AVD not needed.

### Manager (Meera)
Admin & Setup shows only: Approvals & audit, Roadmap, Appearance & display. No Administration, no Master configuration. No switches and no explanation at all (tile absent, not disabled).

### Tech admin (Lata)
Home has only People & Payroll and Admin & Setup; bottom bar is Users / Menu. Administration > Modules shows enabled Switch off buttons; Switch off POS worked (toast, pill Off). Master configuration also visible. Module switches for Tech admin are not money, approver none, so allow.

### Administration page tabs (Owner, phone)
Modules, Roles, Users (People / Effective access / Exceptions), Audit all render; Users cards stack; own account Disable is disabled with visible reason "This is your own access — another administrator must change it."

## Findings that could explain the complaint (not bugs in the switch itself)
1. Discoverability on the phone: the Owner's hamburger Menu drawer lists Modules Home, Dashboard and the 9 categories but NO "Admin & Setup" entry (admin-04). The only phone route is Modules Home > scroll to the last tile. (Drawer built in `src/app/NavDrawer.tsx:22`, categories filtered by navItems; Admin pages seem absent from the phone drawer items.)
2. "Administration" tile is described "Users, roles and permissions" while module switches are the "Modules" tab inside it or "Modules on/off" in Master configuration; the two screens look different (cards with Switch buttons vs row list).
3. Switch buttons are small text buttons with the same look as secondary buttons; the pill ("On"/"Off") is NOT tappable, so someone tapping the pill (or the whole row) sees nothing happen. Panels `src/features/admin/config/panels.tsx:18-27` (only the `<button>` has onClick).
4. Sub-function rows: core ones show only "Always on" text; rows of a switched-off module show "Switch the module on first"; features switched off by dependency show a reason with no button (`panels.tsx:47-51`). Easy to read as "cannot turn on/off".
5. Toast appears at bottom for ~3 s only (admin-09 taken 0.7 s after tap; gone after 3 s).
6. Manager/others see nothing about why the admin tiles are missing.

## Root cause in code
None found for Owner: SETTING_SET path, cfgCanChange (`src/domain/streams/config.ts:326`), window.confirm all work on the APK.

## Proposed small improvements (not applied)
- Add "Admin & Setup" to the phone drawer for Owner / Tech admin.
- Make the whole module row (or the pill) toggle, or label the control as a real switch (role="switch").
- Keep toast visible longer for config changes.
- If the owner's report was via a stale/older build or a different user, ask for the exact screen and user.

# FIT2-lists-DONE (wave FIT-2, lane lists, branch claude/fit2-lists)

Layout only; no text size, sub-screen, money or permission change; no phone code.

## Per page
- Messages, Groups, Guest services, Dashboard (overview), Tariff & yield, OTA channels: key figures are now `StatStrip compact collapseOnTablet` (one line per figure, starts compact at tab-land, toggle remembered).
- POS Day summary: strip compact + collapsible; the "Settled bills..." note moved from the toolbar (where it was cut to one line and forced the manage-list links onto three rows) into the summary panel footer (FitNote, 2 lines); links now sit on one row.
- Groups: Blocks list owns the whole width until a block is opened (no auto-selected first block; SplitView list-only; 2-3 blocks per row via fit2-lists.css); footer notes show full text (lines 2 / 3) before a measured More.
- Yield: legend and note use `lines={2}`, so full text shows at tab-land (no More).
- Reservations, Messages Outbox, all lane tables: cell padding trimmed (6 px top and bottom), "Show more" shares one footer line with its note. Tab-land Outbox 3.5 to about 4.5 rows, Reservations 5 to about 6.
- Sub-screens, rail rows: none added (these are lists, not tasks; no SUB_SCREENS lines).

## Not done / notes
- Tabs and stat strip stay two rows (tab bar is the 48 px touch height; merging them would push Groups' five tabs into More at 1138 px).
- Expenses notes belong to another lane's files; not touched.
- Reservations toolbar left as it was (one row at tab-land).
- Tests: stream-fit2-lists.mjs (5 tests); stream-fit-guest / revenue / cash-b assertions updated for the compact class.

## Checks (Invoke-Serialized)
tsc clean; npm test 2351/2351; npm run build ok; fit-screen-check --pages messages,groups,guest-services,overview,yield,channels,reservations,pos, owner, tab-land + lap-1366 + tab-port: PASS 0 failing of 117 (page scroll, fonts vs baseline). Screens: 05 Delivery Evidence/Fit/fit2-lists-shots/screens-20261102-0546.

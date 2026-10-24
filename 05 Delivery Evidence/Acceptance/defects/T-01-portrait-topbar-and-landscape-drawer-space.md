# T-01 · S3 · Tablet space: portrait top bar and landscape drawer list (PHYSICAL, Tab S7)
- **Steps:** (a) rotate to portrait, any page. (b) landscape, hamburger → expand a category (Front Desk).
- **Expected:** content gets most of the screen; drawer sub-items reachable without hunting.
- **Actual:** (a) portrait top bar + demo-controls band wraps onto ~6 rows = 248 of 1006 CSS px (~25 %) before page content (`APK/Tab-S7/w0d-30-portrait-arrivals.png`, `w0d-32-cold-start-back-exit-prompt.png`). (b) in landscape the drawer footer (user, Demo controls, Display mode, Sign out) takes 210 of 601 CSS px, leaving a 254 px scrolling list; with Front Desk expanded, "Reservations" sits below the fold and a tap at its un-scrolled position hits nothing; needs a swipe (`w0d-04-drawer-open.png`, `w0d-12-drawer-scrolled.png`).
- **Suggested:** collapse demo/display controls into the drawer on tablet widths; make the drawer footer compact/sticky only for sign-out.

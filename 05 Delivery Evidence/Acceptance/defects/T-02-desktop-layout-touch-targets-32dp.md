# T-02 · S3 · Desktop layout touch targets below 48 dp on the tablet (PHYSICAL)
- **Steps:** Tab S7 (density 380 → 1 CSS px = 1 dp), Auto display, measure interactive elements.
- **Expected:** finger-friendly (~44–48 dp) controls on a touch tablet.
- **Actual:** hamburger/breadcrumb/Auto-Desktop-Mobile/Reset/Walkthrough = 32 dp; Search/Sign out 36; Walk-in 38; Preview-as select values 22 dp; search field row 15 dp text line. All were tappable with real taps in this run (no mis-taps on 32 dp targets), but they are below Android's 48 dp guidance. The Mobile layout on the same tablet has 15 of 17 controls ≥ 44 dp.
- **Evidence:** `APK/Tab-S7/w0d-02-signin-owner-home.png`, `w0d-24-display-mobile-cashier.png`.

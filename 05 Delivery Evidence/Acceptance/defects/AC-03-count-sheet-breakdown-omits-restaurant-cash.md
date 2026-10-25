# AC-03 · S4 · "Count the cash drawer" explanatory line omits the restaurant cash (API 35 emulator, APK v8)
- **Steps:** S0, 24 Oct, after the ₹500 deposit and POS settlement → Manager → Closing → Count & submit cash.
- **Expected:** the formula adds up to the expected cash ₹11,250.
- **Actual:** "Expected cash: ₹11,250 = opening float ₹5,000 + cash in ₹7,500 − cash out ₹1,800" — that arithmetic gives ₹10,700; "cash in" excludes the ₹550 restaurant cash that the Cashier page includes (+₹8,050). The total (₹11,250) is right; only the breakdown text is wrong.
- **Evidence:** `AC-03-count-sheet-breakdown-omits-restaurant-cash.png`

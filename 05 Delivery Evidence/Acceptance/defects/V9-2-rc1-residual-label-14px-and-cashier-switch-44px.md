# V9-2 · S4 · RC-1 only partly fixed in v9

- Reports > Change (range sheet): the field labels "From (business date)" / "To" are 14 px (spec 16 px). Inputs are 16 px. Screenshot `apk-v9\v9-rc1-range-sheet.png`.
- Cashier shift > Count drawer: the "Denominations / Total" switch is 44 px high (spec >= 48 px). Screenshot `apk-v9\v9-rc1-cashier-count-sheet.png`.
- Day closing > Count & submit cash: switch is 48 px high (Pass).
- Measured with getBoundingClientRect/getComputedStyle in the WebView at 412 px CSS width (density 2.625).

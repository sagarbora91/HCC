# RC-1 Phone: count-sheet "Total / Denominations" switch is 32 px high; Reports range-sheet labels are 14 px
Severity: S4 (found in the 25 Oct recheck on integration/w05 ee46ee6, phone 360x800, coarse touch)
Steps: (a) Accounts & Reports > Day closing > Count & submit cash. (b) Reports > Business dates > Change.
Actual: (a) the "Total" (50x32) and "Denominations" (104x32) segmented buttons are under 48 px; on the 800 px tablet the same buttons are 48 px. (b) the labels "From (business date)" and "To" are 14 px (phone rule 16 px); date inputs and buttons are 48 px.
Also seen (cosmetic): count badges inside Compliance filter chips and the DEMO button are 12 px.
Expected: 48 px targets, 16 px labels per CLAUDE.md phone rules and DEC-060.

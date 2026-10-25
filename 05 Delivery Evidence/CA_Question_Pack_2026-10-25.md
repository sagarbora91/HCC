# Hotel Control Centre (HCC): Questions for the Chartered Accountant

Prepared 25 October 2026 for the hotel owner's Chartered Accountant. Hotel in Maharashtra, intra-state supplies.

**About the app.** Hotel Control Centre is the hotel's operations software: bookings, room and restaurant billing, cash book, staff attendance, leave, payroll and a compliance calendar. It is at prototype stage and uses fictional data. Every GST, payroll and due-date rate or rule it applies is a configurable, dated setting that the owner can change in "Master configuration" (earlier dates keep the rule that applied then). The starting values were taken from public sources and from the owner's earlier payroll and tax tool, and each is marked **"Illustrative — CA to confirm"**. The app calculates and reminds. **Nothing is filed, paid or e-mailed automatically**: returns, challans and the bank salary file are produced or tracked for a person to act on.

Priority: **High** = affects every bill or every payslip; **Med** = affects some bills or payslips, or an exit; **Low** = occasional.

## 1. GST: rooms

| # | Question | What the app does today | What we need from you | Priority |
|---|---|---|---|---|
| G1 | Room GST slab and rates | Per room, per night, on the actual price charged that night: at or below ₹7,500 = 5 % without input tax credit (ITC) from 22 Sep 2025 (12 % with ITC before that date); above ₹7,500 = 18 % with ITC. The ₹7,500 limit is a setting. | Confirm the rates, the ₹7,500 limit and the 22 Sep 2025 date. | High |
| G2 | Basis of the slab test | Uses the price actually charged for that night (not the declared tariff, not an average across nights). | Confirm "actual price charged" is correct, and whether it is before or after discount and before or after tax. | High |
| G3 | Small-value exemption before 22 Sep 2025 (room charge up to ₹1,000 per day) | **Not modelled.** Nights before 22 Sep 2025 are taxed at 12 %. | Does the exemption apply to this hotel's past nights? If so, how should they be treated? | Low |

## 2. GST: restaurant, room service and other services

| # | Question | What the app does today | What we need from you | Priority |
|---|---|---|---|---|
| G4 | Restaurant and room service rate | 5 % without ITC; 18 % with ITC if the hotel is "specified premises" for the financial year (from 1 Apr 2025). | Confirm both rates and the date. | High |
| G5 | "Specified premises" test | Derived from the previous financial year: specified if any room's nightly tariff was above the ₹7,500 limit at any time in that year (tariff history is checked). The owner can also record an opt-in declaration, effective from the date entered. | Confirm the basis (previous-year highest tariff versus actual billing), who decides, and when an opt-in must be made (we assume from 1 April for the whole year). | High |
| G6 | Minibar | A charge described as minibar, tea, coffee, water, meal and similar is treated as restaurant (rate per G4). | Confirm minibar and in-room food follow the restaurant rate. | Med |
| G7 | No-show / cancellation charges | **Not sure.** The app has no separate rule; such a charge takes the folio's room rate or the "other service" rate depending on how it is posted. | State the correct rate and treatment. | Med |
| G8 | Laundry, banquet or hall, other services | One dated rate each, all seeded at 18 % with ITC. A free-text charge is sorted into these by its description. | Confirm 18 % for each, and give any different rate (for example hall rent). | Med |

## 3. GST: invoicing, CGST/SGST, discounts and credit notes

| # | Question | What the app does today | What we need from you | Priority |
|---|---|---|---|---|
| G9 | CGST/SGST split and rounding | Intra-state only. Tax is worked out per invoice line (rounded to the paisa, half away from zero) and split into CGST and SGST halves; an odd paisa goes to CGST. IGST is not supported. | Confirm per-line rounding and the split are acceptable on invoices and in GSTR-1, and whether IGST is needed (for example out-of-state corporate bookings). | High |
| G10 | Discount on a mixed bill (room plus food) | A discount, credit note or correction reduces the room supply and takes the room-night rate of that date. It is **not** split across rates on the folio. | Should a discount be apportioned across room, food and other lines, and at what rates? | Med |
| G11 | Discount versus the ₹7,500 slab | The slab is tested on the price charged. A discount given as a separate line does not change that night's slab. | Does a discount reduce the price for the slab test? Does it matter whether it is agreed before or after the stay? | High |
| G12 | Credit notes | Posted lines keep the rate they had when posted; an adjustment follows the original room-night line's rate. | Confirm the treatment and the credit-note timing rules we must follow. | Med |

## 4. Payroll

All payroll rates are dated settings; defaults apply from 1 Apr 2025.

| # | Question | What the app does today | What we need from you | Priority |
|---|---|---|---|---|
| P1 | PF wages base and pay structure | Employee 12 % and employer 13 % on basic pay only, capped at ₹15,000. Monthly staff pay structure: basic 50 % of earned pay, HRA 80 % of basic (40 % of pay), washing allowance 10 % of basic (5 % of pay), special allowance the balance (5 %). | After the Labour Codes (in force 21 Nov 2025), does the 50 % wages rule change the PF/ESIC base? Should the special allowance (paid to all) count as PF wages? | High |
| P2 | Employer PF split | Treated as one 13 % figure. | Confirm split: EPF 3.67 %, EPS 8.33 % (capped at ₹15,000), EDLI 0.5 %, admin 0.5 %. | Med |
| P3 | PF for basic-only and daily-wage staff | PF is off by default for them; it can be switched on per person. | Confirm the default, or say who must be enrolled. | Med |
| P4 | ESIC rate and wage base | Employee 0.75 % and employer 3.25 % on earned pay less washing allowance, when pay is at or below ₹21,000. Overtime is excluded from ESIC wages. | Confirm rates and ceiling; should washing allowance and overtime be in ESIC wages? | High |
| P5 | ESIC contribution period | Coverage is decided once per period (Apr to Sep, Oct to Mar): from the earliest locked month of the period, else the contractual wage (daily wage x 26 for daily staff). It then continues to period end even if pay crosses ₹21,000. | Confirm this rule, the daily-wage x 26 basis, and how to treat someone crossing the ceiling mid-period. | High |
| P6 | Rounding | To the paisa by default. A setting rounds PF to the nearest rupee and ESIC up to the next rupee. | Which should be the default for ECR and ESIC filing? | Med |
| P7 | Maharashtra Professional Tax | On earned pay excluding overtime. Men: nil up to ₹7,500; ₹175 up to ₹10,000; ₹200 above; February ₹300 on the top slab. Women: nil up to ₹25,000, else ₹200 (₹300 in February). Staff with no gender recorded use the men's slab. Charged whenever earned pay is above zero, including a part-month. | Confirm slabs, base (should overtime count?) and treatment of a partial exit month. | High |
| P8 | Overtime | Paid at 1 times the ordinary hourly rate (monthly pay / days in month / 8 hours), per minute. Work on a weekly off earns no extra pay and no compensatory off. | The Maharashtra Shops and Establishments Act and the Factories Act are understood to require 2 times. Confirm the rate and the weekly-off rule. | High |
| P9 | Late marks, half-days, absence | Three late marks (after a 15-minute grace) = half-day loss of pay. A half-day = half-day loss of pay. Unmarked days count as absent. Weekly offs are paid. | Confirm these deductions are lawful. | Med |
| P10 | Absence-based forfeiture (rule of the owner's earlier tool, **not** used here) | Not applied. The earlier tool added or forfeited paid days depending on total absence and capped leave applied at 5.5 days a month. | Is such a rule lawful, or only company policy? | Low |
| P11 | Salary advances | Recovered monthly by instalment; instalment 0 = full balance in one month. Recovery never exceeds net pay; no percentage limit. | Is a limit under the Payment of Wages Act needed (for example 50 % of wages)? | Med |
| P12 | Leave entitlements | Casual 12, sick 7, earned 15 days a year; reset each financial year; no carry-forward; full year granted to a mid-year joiner. Daily-wage staff: leave without pay only. | Statutory minimums under the Maharashtra Shops and Establishments Act 2017; carry-forward; pro-rating for joiners. | Med |
| P13 | Leave encashment on exit | Suggested figure only: unused earned leave x (monthly salary / calendar days of the exit month), editable. | Confirm the formula (or basic / 26) and whether encashment is mandatory. | Med |
| P14 | Full and final settlement | Final-month pay, less PF/ESIC/PT, less the whole outstanding advance, plus or minus manual adjustments. A second payroll user must confirm. Owner's limits: each adjustment up to ₹2,00,000; additions above ₹1,00,000 need the Owner. **Gratuity, notice-pay recovery and statutory bonus are not calculated.** | Formulas and rules for gratuity (5-year rule, wages used), notice pay, bonus (Payment of Bonus Act), and the time limit for paying the settlement. | Med |
| P15 | Gratuity provision | Not provided anywhere in the app. | Advise whether a provision or report is needed. | Med |
| P16 | Labour Codes | Rules follow the older Acts; no Labour Code changes are applied. | List the changes (wage definition, overtime, gratuity for fixed-term staff, leave, deduction limits) to apply, and from which date. | High |
| P17 | Payslip and statutory files | Payslip shows the rates in force; the PF/ESIC/PT statement and bank file are produced for a person to upload or send. Nothing is filed. | Confirm the layout needed for ECR, ESIC and PT returns. | Low |

## 5. Compliance due dates

The compliance calendar lists each return by month with its due date; the owner or accountant marks it Filed with a reference. Dates are dated settings. Nothing is filed by the app.

| # | Question | What the app does today | What we need from you | Priority |
|---|---|---|---|---|
| C1 | GSTR-1 | 11th of the next month. Monthly filer assumed (not QRMP). | Confirm date, and monthly versus quarterly (QRMP). | High |
| C2 | GSTR-3B | 20th of the next month. | Confirm date (and any state-wise alternative dates). | High |
| C3 | GSTR-9 (annual) | 31 December after the financial year. | Confirm date, and whether GSTR-9C or other annual forms apply. | Low |
| C4 | EPF | 15th of the next month. | Confirm. | High |
| C5 | ESIC | 15th of the next month. | Confirm. | High |
| C6 | Professional Tax (Maharashtra) | Last day of the next month. | Confirm date, and the correct form and periodicity (monthly or annual). | Med |
| C7 | Other obligations | Only the seven in this section and TDS (T1) are tracked. | Tell us what else a hotel must track (advance tax, TCS, audit, labour returns, licences). | Med |

## 6. TDS

| # | Question | What the app does today | What we need from you | Priority |
|---|---|---|---|---|
| T1 | TDS deposit date and deductions | The calendar tracks a TDS deposit on the 7th of the next month (March deductions: 30 April). The app does **not** calculate TDS on salary, rent, professional fees or contractors, and does not prepare returns. | Confirm the date, and list the TDS the hotel must deduct (sections, rates, thresholds) so it can be added. | Med |

## 7. Guest ID and Aadhaar storage (legal)

| # | Question | What the app does today | What we need from you | Priority |
|---|---|---|---|---|
| L1 | Storing full Aadhaar numbers | By the owner's decision (25 Oct 2026, against advice), the full ID number is stored for every ID type including Aadhaar. It is shown masked (last 4 digits) except to roles with the ID-view permission, kept out of audit text, logs, exports and messages, and the production version encrypts it at rest. The owner has been told UIDAI rules expect hotels to keep only a masked Aadhaar and that storing the full number may be an offence under the Aadhaar Act 2016. | Please (or the hotel's lawyer) confirm whether this is permitted. If not, advise masked-only storage and which alternative ID to keep. | High |
| L2 | Retention and guest register | Kept with the guest profile; no automatic deletion. | Required register form (including Form C for foreign guests), retention period and deletion rule. | Med |

## How to reply

Answer inline, or mark each row **Confirm** or **Change to ...** with the correct rate, rule or date and the date from which it applies. The owner will enter any change in Master configuration as a dated setting: **no software change is needed for rates, thresholds or due dates**. Rules the app does not have yet (for example G10, gratuity, TDS calculation, IGST) will go to the developers as a separate request after your reply.

Sources: HCC Decision Register (DEC-056, 061, 066, 068, 069); payroll code review of 25 Oct 2026; HANDOFF notes.

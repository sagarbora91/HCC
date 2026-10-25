# PB-2 · S4 · DEC-065: expense "Reverse..." and folio "Request refund" are not disabled for a locked day

- Locked 23 Oct in Cash book (Owner). Day closing "Request correction" is disabled and shows the "Owner must reopen" message (good).
- Expenses EXP-H0492 (23 Oct) "Reverse..." and folio HCC-H1248 receipt H-RC-02310 (23 Oct) "Request refund" stay enabled and open their forms; the refusal comes only after typing a reason and pressing the submit button ("Business day closed: Cash book 2026-10-23 is locked ... The Owner must reopen 2026-10-23 (with a reason) first.").
- The rule is enforced (nothing posted), so this is a usability gap only: disable the button with the same reason text, as Corrections does.
- Screenshots: `polish-browser\dec065-expense-refused.jpg`, `dec065-refund-blocked.jpg`.

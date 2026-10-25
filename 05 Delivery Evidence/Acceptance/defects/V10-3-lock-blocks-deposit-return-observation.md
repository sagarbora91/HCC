# V10-3 Locked cash-book day blocks returning a deposit during checkout (observation)

Severity S4 (design observation, per DEC-065, not a code fault) · APK v10, phone, EMULATED, scenario S0.

Steps: Owner locks 24 Oct in Cash book (after Meera counted). Reception later checks out Room 105 on 26 Oct; Priya records "Return deposit" ₹500 (deposit received 24 Oct).
Actual: "Business day closed: Cash book 2026-10-24 is locked, so returning a deposit taken on that day is blocked. The Owner must reopen 2026-10-24 (with a reason) first." The message is clear, and after the Owner reopened 24 Oct the return posted (drawer 12,725).
Why note it: a normal checkout deposit refund (dated today) is blocked whenever the deposit receipt day is locked; consider allowing a new dated return on an open day, or lock only after guests depart. Evidence: apk-v10\v10-deposit-return-blocked.png, v10-cashbook-lock-message.png.

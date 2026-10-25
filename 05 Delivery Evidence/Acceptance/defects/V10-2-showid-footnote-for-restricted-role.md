# V10-2 "Show ID is logged" footnote shown to a role that has no ID access

Severity S4 · APK v10, phone, EMULATED.

Steps: sign in as Farhan (Accountant), Cash & Billing > Folios > a guest > More > Guest profile > expand "ID (masked)".

Expected: only "ID details are restricted for your role" (the role cannot use Show ID).
Actual: the section shows "Restricted" and the restricted note, and also the line "Shown masked (last 4). "Show ID" is logged in the audit trail (DEC-068)" although there is no Show ID button for this role. Confusing, no data exposed.

Housekeeping (Ravi) and Maintenance have no route to a guest profile on the phone, so "Restricted" for Housekeeping could not be shown; the Accountant case was used instead. Evidence: apk-v10\v10-id-restricted-accountant.png.

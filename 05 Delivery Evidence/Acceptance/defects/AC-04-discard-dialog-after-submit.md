# AC-04 · S3 · Native "Discard the details you entered?" dialog appears after a successful submit (API 35 emulator, APK v8)
- **Steps:** (a) Closing → Count & submit cash → type the counted amount → Submit count & close shift. (b) Reports → Change → set From date → Apply range. Reproduced 4 times (24, 25, 26 Oct counts and the report range).
- **Expected:** the sheet closes with no prompt once the action succeeded.
- **Actual:** the action succeeds (shift closed / range applied) but an Android `confirm()` "Discard the details you entered?" (CANCEL / OK) pops up on top; the WebView is frozen until it is answered (DevTools calls hang), and a user may think the submit failed. Other sheets (payment, charge, discount, deposit return) do not show it. Likely the sheet's dirty flag is not cleared on success for sheets using the count / date-range forms.
- **Evidence:** `AC-04-discard-dialog-after-count-submit.png`

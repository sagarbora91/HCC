# PB-1 · S4 · Escape does not close an open dialog when focus is on BODY

- Build: integration/polish 78f8833, desktop 1366x768, Chrome.
- Steps: open a folio dialog, press Record payment, Review (the "Confirm before posting" dialog), then press Ctrl+K, search a page, Enter. The page changes but the dialog stays open and `document.activeElement` is BODY. Escape (pressed 3 times) does nothing; only the "Close dialog" button closes it.
- Cause (from `ModalHost.tsx` line 61): Escape is handled in the dialog's own onKeyDown, so it only works while focus is inside the dialog.
- Expected: Escape closes the top dialog from anywhere, or the dialog closes / traps focus when the palette closes. Seen once (not re-run in isolation).

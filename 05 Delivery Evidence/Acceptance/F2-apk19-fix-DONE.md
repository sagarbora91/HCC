# F2 apk19-fix - DONE (2 Nov 2026, branch claude/apk19)

No Android build or emulator here: every item below is a source fix plus a static check (`03 Prototype/tools/v19-fixes-check.mjs`, 7/7 OK); device confirmation goes in the APK v19 emulator run and the Tab S7 test.

| ID | Result | Files |
|---|---|---|
| V17-1 insets lost / modal footer clipped | FIT's `ModalFrame` sticky `.modal-actions` in a scrolling `.modal-body` keeps the footer reachable (already fixed by FIT for the clipping symptom). Root cause (decor padding dropped after the window-background swap / IME / config change) fixed natively: `reapplyInsets` after every paint and in `MainActivity.onConfigurationChanged`. **Needs device** to confirm the `innerHeight` 842/915 flip is gone. | `04 Android/.../StatusBarColorPlugin.java`, `MainActivity.java` |
| V17-2 resize at idle | Plugin skips the background swap when the colour pair is unchanged; JS ignores `resize` with unchanged window size (orientation / night changes still repaint). Cause may be partly the DevTools connection: **needs device** (`adb logcat`). | `StatusBarColorPlugin.java`, `03 Prototype/src/app/native.ts` |
| V17-3 transient bar colours | Not changed separately; same family as V17-1 (inset re-dispatch and fewer swaps should shorten it). Cosmetic, settles in 1-2 s. **Needs device.** | - |
| V17-4 Room board date line | Date part ellipsises, status word ("Open"/"Closed") never shrinks (inline style). | `03 Prototype/src/app/mobile/MobileShell.tsx` |
| V15-4 "Approvals" mid-word | Tab labels `overflow-wrap:normal; word-break:keep-all`, font `min(scaled 16px, 4vw)`. Visual confirmation at 360 px **needs device**. | `MobileShell.tsx` |
| V16-3 DEMO pill at 412 px | Not changed: the V15-3 rule is icon-only up to 400 px by design; observation only. Owner may ask for 412. (The rule lives in `src/styles/z-largetext.css`, outside this lane.) | - |

Results: `node tools/v19-fixes-check.mjs` 7/7 OK; `npx tsc --noEmit` clean; `npm test` 2388/2388 (was 2388).

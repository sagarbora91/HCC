# Prompts for Claude Code — Hotel Control Centre

Open Claude Code in `V:\Codex\Hotel Control Centre` (model: Sonnet). Paste ONE prompt per session.

## Session prompt (use every time — it picks the next unticked phase)

```
Read CLAUDE.md, HANDOFF.md and "02 Planning\HCC_Plan_FY_History_and_APK_v1.md" first.
Do ONLY the next phase that is not ticked in §9 of the plan. Follow every rule in §1 of the plan and the architecture rules in CLAUDE.md.
Owner answers to the plan's §8: OWNER-1 yes, FY26 is the startup default. OWNER-2 yes, the Android share sheet is fine if no print plugin works. OWNER-3: <write your phone model here>.
Work on this Windows PC in PowerShell; quote paths (they contain spaces). Single agent — do not start parallel agents.
After each step run, in "03 Prototype": npx tsc --noEmit ; npm test ; npm run build — all must pass (currently 388 tests; never delete or weaken tests).
If a step fails twice the same way, stop and report the exact error.
Before stopping (or if usage runs low): stop at the last green step, tick the phase in §9, update HANDOFF.md (done / next / open issues) and CLAUDE.md (test count, new commands), then give me the full Windows paths of every changed file and a 5-line summary.
```

## Phase-specific openers (optional, if you want to force a phase)
- Phase A: `Do Phase A of the plan (generator foundation, FY26 scenario, IndexedDB storage).`
- Phase B: `Do Phase B of the plan (daily operations, closing records, reports over months, history summary).`
- Phase C: `Do Phase C of the plan (history for modules 13–26, performance pass, full Playwright smoke).`
- Phase D1: `Do Phase D1 of the plan (native back button, print/CSV helpers in the prototype — no new npm packages in 03 Prototype).`
- Phase D2–D5: `Do Phases D2–D5 of the plan (04 Android Capacitor project, debug APK, emulator checklist with screenshots). Set JAVA_HOME to Android Studio's jbr first (plan D0).`
- Phase D6–D7: `Help me install the APK on my phone (plan D6) and write 04 Android\README.md (D7).`

## Tips
- One phase per session keeps usage low; HANDOFF.md carries the context between sessions.
- If Claude Code asks about something already decided in the Decision Register or the mobile plan §11, answer "already approved — see the plan".

# HCC training media (wave TRN)

Work in progress on branch `claude/training`. Plan: `E:\Projects\HCC\02 Planning\HCC_Training_Media_Plan_TRN_2026-11-07.md`.
Pipeline: `hcc-web\` (desktop course, `steps.json` -> shots -> deck -> narration -> video). Driver: `03 Prototype\src\app\demo\training\` (prototype build, `?training=1`).
See `TRAINING_NOTES.md` (oddities, changes under 03 Prototype), `coverage.md` (ticked per chapter).

Rebuild after a UI change (from `03 Prototype`): `npm run build`, `npm start` (leave running), then in `06 Training\hcc-web`: `python make_steps.py`, `node "C:\Users\Sagar\.claude\skills\training-media\scripts\capture.js" --config training.config.json [--from 1.1 --to 1.22]`, read `build\shots\capture_report.json`.

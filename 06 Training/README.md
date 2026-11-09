# Hotel Control Centre (HCC) training media (wave TRN)

Training course for every module of HCC: a desktop course (25 chapters), a phone course (8 chapters), 10 role cards, a trainer's guide. English (Windows Zira) and Hinglish (edge-tts hi-IN-SwaraNeural; Hindi in Devanagari, on-screen terms in English). Built 7 to 8 Nov 2026 by wave TRN on branch `claude/training` (cut from `origin/main` 3a0c2d0e). Plan: `E:\Projects\HCC\02 Planning\HCC_Training_Media_Plan_TRN_2026-11-07.md`. Pipeline: the `/training-media` skill.

The data is the **fictional demo hotel** (HCC Demo Residency, 24 rooms 101 to 308, demo users, FY26 fixture), not Hotel Parth. One example guest, Mr. Rahul Deshmukh (Pune, room 203, later 107), is followed through the chapters, with Ms. Anita Rao (walk-in), Mr. Vikram Jadhav of Shree Agro Industries (company credit), the Patil wedding block, the Kulkarni engagement (Moti Hall) and the fictional staff.

## Where everything is

| What | Path |
|---|---|
| Final copy of all files (decks, MP4s, role cards, guide, notes, SHA256.txt) | `E:\Projects\HCC\Builds\2026-11-08\training-hcc\` |
| Desktop course sources (committed) | `06 Training\hcc-web\` : `chapters_00..24.py`, `make_steps.py` -> `steps.json`, `content_src.py` -> `content.json`, `training.config.json`, `coverage.py`, `tools\build_deck_hcc.py`, `tools\encode_gpu.py`, `sources\HCC-Training-notes-hinglish.md` |
| Phone course sources | `06 Training\hcc-phone\` (same shape; `tools\build_deck_phone.py` is in `hcc-web\tools`) |
| Role cards | `06 Training\role-cards\` (`role_cards.py`, `cards_data.py`) -> `build\RC-1 ... RC-10` |
| Trainer's guide | `06 Training\trainers-guide\trainer_guide.py` -> `build\Hotel Control Centre - Trainer's Guide.docx` |
| Driver (training mode of the prototype) | `03 Prototype\src\app\demo\training\` (prototype build only, `?training=1`), test `03 Prototype\tests\stream-training.mjs` |
| Oddities and defects found while scripting | `TRAINING_NOTES.md` (for the next HCC fix wave) |
| Coverage of pages and features | `coverage.md` (50 of 50 pages, 67 of 67 catalogue features ticked, 4 partly shown: see notes in the file) |

Outputs (`build\`, `*.mp4`, `*.pptx`) are never committed; only sources are.

## Role tracks (playlists of chapter videos)

| Role (card) | Chapters desktop | Phone chapters |
|---|---|---|
| Owner (RC-1) | 0 to 24 (start with 0, 19, 20, 21, 23) | 6 |
| Manager (RC-2) | 0 to 20, 22, 23 | 6, 7 |
| Reception (RC-3) | 0, 1, 2, 3, 4, 5, 8, 11, 12, 14 (groups), 17 | 0, 1, 2, 5 |
| Cashier (RC-4) | 0, 5, 6 (settle), 7, 8, 17 | 5 |
| Housekeeping supervisor (RC-5) | 0, 3, 9, 10, 11, 16 | 2, 3 |
| Room attendant (RC-6) | 0, 9, 11, 18 (My shifts & pay) | 0, 2, 3, 7 |
| Accountant (RC-7) | 0, 7, 8, 16, 17, 19 to 22 | 6 |
| Maintenance (RC-8) | 0, 10, 11 | 0, 4, 7 |
| Restaurant (RC-9) | 0, 6 | |
| Tech admin (RC-10) | 0, 23 | 7 |

Each chapter MP4 plays title, steps and recap of one chapter (`chapters-en\chNN.mp4`, `chapters-hi\chNN.mp4`); the full course is `HCC-Training.mp4` / `HCC-Training-hi.mp4`.

## Deviations from the plan (details in TRAINING_NOTES.md)

- The prototype has a staff picker instead of "no sign-in"; the course says staff pick their name on the demo and use their PIN on the real system. Roles are switched with Preview as (the training's `user:` act).
- 589 steps instead of about 460 (more screens met in the build), 67 features / 50 pages instead of 60 / 39.
- A guest can only leave on the booked date, so the driver act `closeday` closes the business day through the app's own command path twice (before chapter 8's check-outs); chapters 9 to 24 are on 26 Oct 2026. The Day close chapter teaches the seven steps; the day itself is not closed because ten fixture guests are due out.
- Room 107 (not 207) for Rahul's move and Suite 106 (not 206) for Vikram; discount Rs 150 (cap 25 per cent); halls are Sagar Hall, Moti Hall, Garden Lawn; the Restaurant role is played by the Cashier (no Restaurant user).
- Hall quotation terms are all "OWNER TO REPLACE" in the demo: the Owner records an override; revision, discount and the event-day bill are read, not clicked.
- October payroll cannot be run before the month ends: the locked September run and payslips are shown. Year end, legacy import and the bank deposit slip photo are explained, not executed. The cashier shift page and the close command disagree on the expected cash on 26 Oct (TRAINING_NOTES F14), so the count is typed but not submitted.
- The phone deck is the mobile shell in Chrome (412x915 at 2.6), no emulator, as the owner agreed (Q3).
- The skill's `build_deck.py` and `encode.py` were copied and adapted (3-column outline for 25 chapters, a portrait layout for the phone, Intel QSV `h264_qsv` encoding at 2 frames per second per the owner's GPU rule); the skill itself is unchanged.
- Narration and text keep `Rs`; no Marathi (Q5).

## How to rebuild after a UI change

From `03 Prototype`: `npm run build` and `npm start` (leave running, port 4173). Then in `06 Training\hcc-web`:

```
python make_steps.py
node "C:\Users\Sagar\.claude\skills\training-media\scripts\capture.js" --config training.config.json [--from 3.1 --to 3.16]
python rep.py                      (fails / nohl must be empty)
python content_src.py && python coverage.py
python tools\build_deck_hcc.py --config training.config.json
python C:\Users\Sagar\.claude\skills\training-media\scripts\render_slides.py --config training.config.json
python C:\Users\Sagar\.claude\skills\training-media\scripts\check.py --config training.config.json --render
python C:\Users\Sagar\.claude\skills\training-media\scripts\tts.py --config training.config.json --lang en     (then --lang hi)
python tools\encode_gpu.py --config training.config.json --lang en                                             (then --lang hi, never together)
```

Hinglish: after the English notes change, rewrite the changed `## Slide N` blocks of `sources\HCC-Training-notes-hinglish.md`, copy to `build\`, `notes_to_pptx.py --lang hi`. Phone: same in `hcc-phone` with `tools\build_deck_phone.py`. Finally `python 06 Training\finalize.py` copies everything to `Builds\<date>\training-hcc\` with SHA256.txt.

Work-PC rules kept: one browser, one ffmpeg (QSV), one LibreOffice, no emulator, sequential stages, long jobs in the background with a log, Sonnet sub-agents only for the Hinglish text, one wave at a time.

## Owed by the owner

- Look at 5 to 10 slides and play one chapter in each language; say whether the demo hotel is right or the course should be re-captured later on a Hotel Parth fixture (Q1: one run of the pipeline).
- Decisions the build suggests: the cashier shift page versus the close command (TRAINING_NOTES F14), the hall terms template that cannot be edited in the FY26 demo (F17), a customer merge screen if wanted (F18).
- Listen to three Hinglish audio files (voice quality) before the staff sessions.

## Training-mode driver (for the next developer)

`?training=1` on the prototype build installs `window.TRAINING.{replay,resolve,state,act}`. Saving is switched off (`storage.ts`), the course steps are read from `/training/steps.json` (dev server route in `serve.mjs`). Acts: `nav user click type select check uncheck key menu modal wait js display signout hover scroll focus closeday typebal typedrawer`. Targets: `sel aria btn tab field row modalbtn lastbtn btnin fldin menutop menuleaf tr text dialog`. A step fails when the app answers with an error toast or a new form error (unless the step says `allowError`), when a control is disabled, or when its `check` is not true within 5 s. The production build contains none of this (a test checks it).

<!-- GENERATED by finalize.py: do not edit between the markers -->
## Spot-check (final report)

**Desktop course** (`hcc-web`): 589 steps in 25 chapters, 643 slides; English 3:40:38, Hinglish 4:52:06.

| Ch | Title | Role | Steps | Slides | English | Hinglish |
|---|---|---|---|---|---|---|
| 0 | Welcome: Modules Home and the Dashboard | Aarav (Reception) | 10 | 12 | 0:05:26 | 0:07:13 |
| 1 | Reservations and the Calendar | Aarav (Reception) | 39 | 41 | 0:13:57 | 0:18:46 |
| 2 | Arrivals, registration and check-in | Aarav (Reception) | 42 | 44 | 0:14:50 | 0:19:34 |
| 3 | The Room board | Aarav (Reception) | 16 | 18 | 0:05:36 | 0:07:38 |
| 4 | In-house guests and guest services | Aarav (Reception) | 35 | 37 | 0:11:44 | 0:15:45 |
| 5 | The folio: charges, discounts and corrections | Priya (Cashier) | 26 | 28 | 0:09:20 | 0:12:24 |
| 6 | Restaurant and room service | Priya (Cashier) | 30 | 32 | 0:09:30 | 0:12:39 |
| 7 | Cashier: shift, payments, cash book | Priya (Cashier) | 20 | 22 | 0:07:12 | 0:09:19 |
| 8 | Check-out, invoice, credit note, counter bills | Priya (Cashier) | 41 | 43 | 0:14:15 | 0:19:02 |
| 9 | Housekeeping | Neha (Supervisor) | 24 | 26 | 0:09:02 | 0:12:04 |
| 10 | Maintenance | Joseph (Maintenance) | 19 | 21 | 0:06:55 | 0:09:10 |
| 11 | Checklists and handover | Neha (Supervisor) | 11 | 13 | 0:04:35 | 0:06:06 |
| 12 | Customers 360 | Aarav (Reception) | 17 | 19 | 0:06:22 | 0:08:23 |
| 13 | Tariff, yield and the tariff card | Meera (Manager) | 19 | 21 | 0:06:55 | 0:09:04 |
| 14 | Groups, companies, OTA and website | Meera (Manager) | 24 | 26 | 0:08:29 | 0:11:14 |
| 15 | Hall booking | Meera (Manager) | 26 | 28 | 0:10:14 | 0:13:14 |
| 16 | Linen, stock and purchasing | Neha (Supervisor) | 16 | 18 | 0:05:57 | 0:07:50 |
| 17 | Expenses | Aarav (Reception) | 17 | 19 | 0:06:22 | 0:08:19 |
| 18 | Staff, leave and payroll | Meera (Manager) | 24 | 26 | 0:09:09 | 0:11:58 |
| 19 | Day close | Meera (Manager) | 16 | 18 | 0:07:14 | 0:09:08 |
| 20 | Reports and the Report hub | Meera (Manager) | 19 | 21 | 0:06:56 | 0:09:08 |
| 21 | Accounting | Farhan (Accountant) | 36 | 38 | 0:11:49 | 0:15:33 |
| 22 | Compliance calendar and the assistant | Farhan (Accountant) | 16 | 18 | 0:06:25 | 0:08:21 |
| 23 | Administration and setup | Sagar (Owner) | 35 | 37 | 0:12:25 | 0:16:19 |
| 24 | Closing: shortcuts, help, quick-reference | Aarav (Reception) | 11 | 13 | 0:04:57 | 0:06:29 |

**Phone course** (`hcc-phone`, mobile shell 412x915 in Chrome): 80 steps in 8 chapters, 100 slides; English 0:30:36, Hinglish 0:40:25.

| Ch | Title | Role | Steps | Slides | English | Hinglish |
|---|---|---|---|---|---|---|
| 0 | The phone shell: app bar, tabs and menu | Aarav (Reception) | 12 | 14 | 0:04:05 | 0:05:25 |
| 1 | Arrivals and check-in on the phone | Aarav (Reception) | 12 | 14 | 0:04:12 | 0:05:27 |
| 2 | Room board and housekeeping tasks | Aarav (Reception) | 11 | 13 | 0:03:40 | 0:04:53 |
| 3 | Guest requests on the phone | Ravi (Housekeeper) | 9 | 11 | 0:03:14 | 0:04:21 |
| 4 | Maintenance work orders on the phone | Joseph (Maintenance) | 11 | 13 | 0:03:36 | 0:04:48 |
| 5 | Folio and payment on the phone | Priya (Cashier) | 11 | 13 | 0:03:45 | 0:04:54 |
| 6 | Approvals and day close on the phone | Meera (Manager) | 6 | 8 | 0:02:41 | 0:03:27 |
| 7 | My shifts and pay, and appearance | Ravi (Housekeeper) | 8 | 10 | 0:03:06 | 0:04:00 |

## Deliverables with sizes (Builds/2026-11-08/training-hcc)

| File | Size |
|---|---|
| `Hotel Control Centre - Trainer's Guide.docx` | 0.0 MB |
| `README.md` | 0.0 MB |
| `TRAINING_NOTES.md` | 0.0 MB |
| `coverage.md` | 0.0 MB |
| `Role cards/RC-1 Owner.pdf` | 0.1 MB |
| `Role cards/RC-10 Tech admin.pdf` | 0.1 MB |
| `Role cards/RC-2 Manager.pdf` | 0.1 MB |
| `Role cards/RC-3 Reception.pdf` | 0.1 MB |
| `Role cards/RC-4 Cashier.pdf` | 0.1 MB |
| `Role cards/RC-5 Housekeeping supervisor.pdf` | 0.1 MB |
| `Role cards/RC-6 Room attendant.pdf` | 0.1 MB |
| `Role cards/RC-7 Accountant.pdf` | 0.1 MB |
| `Role cards/RC-8 Maintenance.pdf` | 0.1 MB |
| `Role cards/RC-9 Restaurant.pdf` | 0.1 MB |
| `desktop/HCC-Training-hi.mp4` | 922.0 MB |
| `desktop/HCC-Training-hi.pptx` | 95.7 MB |
| `desktop/HCC-Training-notes-hinglish.md` | 0.3 MB |
| `desktop/HCC-Training-notes.md` | 0.2 MB |
| `desktop/HCC-Training.mp4` | 684.5 MB |
| `desktop/HCC-Training.pptx` | 95.6 MB |
| `desktop/chapters-en/ch00.mp4` | 18.5 MB |
| `desktop/chapters-en/ch01.mp4` | 43.0 MB |
| `desktop/chapters-en/ch02.mp4` | 41.9 MB |
| `desktop/chapters-en/ch03.mp4` | 14.8 MB |
| `desktop/chapters-en/ch04.mp4` | 34.8 MB |
| `desktop/chapters-en/ch05.mp4` | 26.8 MB |
| `desktop/chapters-en/ch06.mp4` | 28.3 MB |
| `desktop/chapters-en/ch07.mp4` | 21.6 MB |
| `desktop/chapters-en/ch08.mp4` | 42.4 MB |
| `desktop/chapters-en/ch09.mp4` | 27.3 MB |
| `desktop/chapters-en/ch10.mp4` | 20.8 MB |
| `desktop/chapters-en/ch11.mp4` | 14.1 MB |
| `desktop/chapters-en/ch12.mp4` | 17.7 MB |
| `desktop/chapters-en/ch13.mp4` | 22.3 MB |
| `desktop/chapters-en/ch14.mp4` | 29.5 MB |
| `desktop/chapters-en/ch15.mp4` | 30.6 MB |
| `desktop/chapters-en/ch16.mp4` | 17.3 MB |
| `desktop/chapters-en/ch17.mp4` | 19.1 MB |
| `desktop/chapters-en/ch18.mp4` | 29.0 MB |
| `desktop/chapters-en/ch19.mp4` | 24.6 MB |
| `desktop/chapters-en/ch20.mp4` | 21.5 MB |
| `desktop/chapters-en/ch21.mp4` | 40.0 MB |
| `desktop/chapters-en/ch22.mp4` | 23.4 MB |
| `desktop/chapters-en/ch23.mp4` | 42.5 MB |
| `desktop/chapters-en/ch24.mp4` | 14.3 MB |
| `desktop/chapters-hi/ch00.mp4` | 25.1 MB |
| `desktop/chapters-hi/ch01.mp4` | 58.9 MB |
| `desktop/chapters-hi/ch02.mp4` | 56.4 MB |
| `desktop/chapters-hi/ch03.mp4` | 20.6 MB |
| `desktop/chapters-hi/ch04.mp4` | 47.3 MB |
| `desktop/chapters-hi/ch05.mp4` | 36.7 MB |
| `desktop/chapters-hi/ch06.mp4` | 38.3 MB |
| `desktop/chapters-hi/ch07.mp4` | 28.6 MB |
| `desktop/chapters-hi/ch08.mp4` | 57.1 MB |
| `desktop/chapters-hi/ch09.mp4` | 37.1 MB |
| `desktop/chapters-hi/ch10.mp4` | 28.2 MB |
| `desktop/chapters-hi/ch11.mp4` | 19.0 MB |
| `desktop/chapters-hi/ch12.mp4` | 23.8 MB |
| `desktop/chapters-hi/ch13.mp4` | 29.8 MB |
| `desktop/chapters-hi/ch14.mp4` | 39.7 MB |
| `desktop/chapters-hi/ch15.mp4` | 39.9 MB |
| `desktop/chapters-hi/ch16.mp4` | 23.6 MB |
| `desktop/chapters-hi/ch17.mp4` | 25.4 MB |
| `desktop/chapters-hi/ch18.mp4` | 38.1 MB |
| `desktop/chapters-hi/ch19.mp4` | 31.5 MB |
| `desktop/chapters-hi/ch20.mp4` | 28.8 MB |
| `desktop/chapters-hi/ch21.mp4` | 53.2 MB |
| `desktop/chapters-hi/ch22.mp4` | 31.1 MB |
| `desktop/chapters-hi/ch23.mp4` | 56.7 MB |
| `desktop/chapters-hi/ch24.mp4` | 19.2 MB |
| `phone/HCC-Training-Phone-hi.mp4` | 98.9 MB |
| `phone/HCC-Training-Phone-hi.pptx` | 12.3 MB |
| `phone/HCC-Training-Phone-notes-hinglish.md` | 0.0 MB |
| `phone/HCC-Training-Phone-notes.md` | 0.0 MB |
| `phone/HCC-Training-Phone.mp4` | 72.7 MB |
| `phone/HCC-Training-Phone.pptx` | 12.2 MB |
| `phone/chapters-en/ch00.mp4` | 10.0 MB |
| `phone/chapters-en/ch01.mp4` | 10.0 MB |
| `phone/chapters-en/ch02.mp4` | 9.0 MB |
| `phone/chapters-en/ch03.mp4` | 7.1 MB |
| `phone/chapters-en/ch04.mp4` | 8.4 MB |
| `phone/chapters-en/ch05.mp4` | 8.4 MB |
| `phone/chapters-en/ch06.mp4` | 7.0 MB |
| `phone/chapters-en/ch07.mp4` | 7.7 MB |
| `phone/chapters-hi/ch00.mp4` | 14.0 MB |
| `phone/chapters-hi/ch01.mp4` | 13.3 MB |
| `phone/chapters-hi/ch02.mp4` | 12.4 MB |
| `phone/chapters-hi/ch03.mp4` | 9.9 MB |
| `phone/chapters-hi/ch04.mp4` | 11.4 MB |
| `phone/chapters-hi/ch05.mp4` | 11.3 MB |
| `phone/chapters-hi/ch06.mp4` | 9.2 MB |
| `phone/chapters-hi/ch07.mp4` | 10.1 MB |
<!-- END GENERATED -->

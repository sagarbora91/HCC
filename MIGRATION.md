# Moving Hotel Control Centre to the work PC

Prepared 25 Oct 2026 (old PC crashing). The package is `V:\HCC-Migration-2026-10-25\` on the old PC; copy the whole folder to the work PC (USB drive / OneDrive / network share). **Do not push to GitHub as a transfer method** — `github.com/sagarbora91/HCC` is public.

## 1. What is in the package
| Item | What it is | Needed? |
|---|---|---|
| `HCC.bundle` | Complete git repository: `main` + every branch (incl. Wave 0.5 `claude/w05*`) and full history | **Yes** |
| `HCC-0.1.0-proto-debug-v8.apk` | Latest debug APK (versionCode 8) — APKs are not in git | Yes (tablet testing) |
| `Hotel Control Centre.html` | Built prototype, opens in any browser | Convenience |
| `claude-memory\` | Claude's saved preferences for this project | Yes |
| `reference-inputs\SCC-modules\` | SCC `payroll`, `leave`, `tax`, `expense` module files (read-only rule sources for Wave 0.5 B/C/D) | Yes, unless SCC is also moved |
| `reference-inputs\06-inventory\` | Donor repository inventory (origins + pinned commits) | Yes |
| `06-donor-bundles\*.bundle` | The six donor clones as git bundles (pinned snapshot) | Optional (reference only) |
| `restore-on-new-pc.ps1` | Restore + verify script (also in the repo under `tools\`) | Yes |
| `SHA256SUMS.txt` | Checksums of every file in the package | Check after copying |

Not included on purpose: `node_modules` (reinstalled), build outputs, the Norton trust store `04 Android\.build\hcc-cacerts.jks` (PC-specific; recreate only if the work PC also has Norton SSL scanning), emulator images.

## 2. Install on the work PC (once)
1. **Git** (any recent), **Node.js 25.x** (old PC: v25.9.0; 22 LTS also works for the prototype, but keep 25 to match).
2. **Claude desktop app** (Code tab) and, if used, Codex.
3. For the APK and devices: **Android Studio** (includes JBR Java 21), Android SDK platform 36, platform-tools, emulator. Recreate AVDs: `codex_api35_google` (Android 15, Google APIs, x86_64) and `codex_apk_review` (old WebView 83 test image — or any API 28–30 image with the stock WebView).
4. For production work later (A3/Wave 1): **SQL Server 2025 Express** (`.\SQLEXPRESS`) and **.NET 10 SDK**. The ETP project has its own migration notes (`V:\ETP\NEW-PC-HANDOFF.md`).

## 3. Restore (about 10 minutes)
Pick the folder (keeping the same drive path avoids fixing documents that mention `V:\Codex\Hotel Control Centre`; any path works):
```powershell
powershell -ExecutionPolicy Bypass -File "<package>\restore-on-new-pc.ps1" -Package "<package>" -Target "V:\Codex\Hotel Control Centre"
```
The script: checks the package checksums → clones `HCC.bundle` → recreates every branch locally → sets `core.autocrlf=false` → copies the APK into `05 Delivery Evidence\APK\` → `npm ci` in `03 Prototype` and `04 Android` → `npx tsc --noEmit`, `npm test`, `npm run build` → prints the results. Expected: **tsc clean, 502/502 tests, build ≈ 1,884 KB** on `main`.

Then by hand:
- Copy `claude-memory\*` to `C:\Users\<you>\.claude\projects\<project-key>\memory\` (the key is the project path with `\` and spaces replaced by `-`, e.g. `V--Codex-Hotel-Control-Centre`). Open the project once in Claude first so the folder exists.
- Put `reference-inputs\SCC-modules` somewhere stable (or move SCC itself) and update the SCC path in `02 Planning\agents\W05_Briefs.md` if it differs from `V:\Co work\Projects\Retail\saagar-control-centre-phase1-clone\www\modules\`.
- Optional: restore donor repos: `git clone 06-donor-bundles\haip.bundle "<target>\06 Reference Repositories\haip"` (same for the other five), then copy `reference-inputs\06-inventory\*` into `06 Reference Repositories\`.
- Tablet: enable USB debugging again for the new PC (the Tab S7 will ask "Allow USB debugging?" for the new computer).

## 4. Resume work (tell Claude: "resume after migration")
Status at migration (details in `HANDOFF.md`, last sections):
- **W0-D acceptance** — browser walkthrough 15/15, Tab S7 17 checks, AC-01/AC-02 fixed and verified on WebView 83 (v7/v8). Remaining: emulator Part 2 (10 roles, Back, Reset, CSV, A4 print, wording) and Part 3 (AC-M05…M08) on APK v8 — brief `02 Planning\agents\W0-D_Final_Emulator_Brief.md`; then the combined acceptance report + matrix update; owner acceptance.
- **Wave 0.5** (branches): `claude/w05d-cashbook-99d1d6` **done** (512 tests), `claude/w05e-exports-3573bc` **done** (509 tests), `claude/w05c-compliance-258931` **WIP** (snapshot, untested), `claude/w05a-config-75c322` **WIP** (barely started), `claude/w05b-payroll-c92985` **not started**. Briefs: `02 Planning\agents\W05_Briefs.md`. Known merge conflict: `03 Prototype/src/features/closing/DayClosing.tsx` (D and E each add one line). Lead bumps SCHEMA 7 → 8 once at merge.
- Restart the 7-minute evidence save on the new PC: `bash tools/checkpoint.sh`.
- Old Claude agent sessions cannot move between PCs; start new agents from the briefs (each brief is self-contained).

## 5. Afterwards on the old PC
Keep the old folder until the work PC passes the restore check. Nothing has been pushed anywhere.

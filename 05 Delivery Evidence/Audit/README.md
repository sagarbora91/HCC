# Independent audit of HCC — working folder (26 Oct 2026, Fable lead)

Plan: `02 Planning\agents\Audit_Multi_Agent_Plan_2026-10-26.md` (binding for every agent). Prompt: `02 Planning\agents\Audit_Prompt_Fable.md`.
Branch: `claude/audit-fable-2026-10-26`, cut from `main` at `c0c477d` (HANDOFF with APK v12 results). Audit-only: no product code changes.

## Layout
| Path | Who writes | What |
|---|---|---|
| `HCC_Independent_Audit_2026-10-26.md` | lead | the report for the owner |
| `agents\A<nn>_<name>.md`, `agents\V1_verification.md` | that agent only | one file per agent, finding template below |
| `evidence\A<nn>\`, `evidence\lead\` | that agent only | command outputs, logs, probe results (`.txt`, `.json`, `.png`) |
| `probes\A<nn>_*.{mjs,ps1,sql,cs}` | that agent only | throwaway scripts used for evidence; runnable from the repo root |
| `locks\dotnet-test.lock`, `locks\emulator.lock` | whoever holds it | create with `<agent id> <ISO time>` only if absent; delete when done; never delete another's |

## Finding template (one row per finding; use `A<nn>-<seq>` ids)
```
| ID | Sev | Area | File:line | Evidence | Impact | Fix | Hours | Conf |
|----|-----|------|-----------|----------|--------|-----|-------|------|
| A03a-01 | High | GST | 08 Production/src/Hcc.Domain/Tax/GstRules.cs:88 | probes/A03a_gst.mjs gives ₹…; server test X expects ₹… | wrong tax on … | … | 3 | High |
```
Severity: **Critical** = money, tax or data loss in normal use, or an auth bypass · **High** = wrong result in a common flow, or a go-live step that cannot be done · **Medium** = wrong in an edge case, or a missing control with a manual workaround · **Low** = quality, clarity, hardening · **Info** = confirmed good, or a note. Confidence High/Medium/Low = how sure you are the finding is real. The lead may re-grade.

Every agent file ends with three lists: **Claims I could not confirm**, **Open owner/CA decisions I hit (and the code that depends on each)**, **Questions for the lead**.

## Environment at start (see `evidence\lead\env.txt`)
Node v25.9.0 (with `E:\Tools\nodejs` first on PATH), .NET SDK 10.0.401, E: 268 GB free, no emulator running. `.\SQLEXPRESS` holds `EtpReporting`, `EtpScratch_20261020` and system DBs only (no leftover `HccTest_*`); an **ETP** test session (`E:\ETP\Code\Worktrees\stress-minors`) is using the same SQL instance in parallel, so leave every non-`HccTest_*` database alone and expect some CPU contention.

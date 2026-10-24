# Claude continuation prompt — updated 25 October 2026

Continue Hotel Control Centre at `V:\Codex\Hotel Control Centre`. Read the newest entries in HANDOFF.md, CLAUDE.md and the decision register first, then:
- `05 Delivery Evidence/Assembly/2026-10-25_Development_Review.md`
- `02 Planning/HCC_Component_Selection_and_Assembly_Plan_v1.md`
- `02 Planning/HCC_Claude_Coding_and_Assembly_Instructions_v1.md`
- `02 Planning/HCC_Component_Qualification_Register.md`
- `02 Planning/HCC_Production_Contracts_and_Schema_v1.md`
- `02 Planning/HCC_FirstParty_Reuse_ETP_SCC_v1.md`
- Current W0-D acceptance reports and approved Wave 0.5/master-configuration plans.

Resume from completed work: A0/A1 qualification, the T-SQL spike, shared-till accounting and restriction-override UX have already been reported complete. Do not repeat them from the old starter prompt. Confirm current Git changes and task ownership before editing; compatibility-fix files may still be uncommitted.

First finish the remaining W0-D acceptance work and verify fixes against the actual rebuilt APK. The saved v6 evidence does not prove the newer compatibility fix works. Keep device/emulator results distinct and never mark missing reports as passed. Reuse completed evidence when applicable, recording changed-build regression scope. Do not start extra emulators or background checkpoint loops without checking existing processes and the owner's heat constraints.

After W0-D closes, implement the approved Wave 0.5 scope under APR-006. Preserve the working interface, tablet/phone layout, native print/export and existing data. Production construction still requires A4 authorization; routine work within an already authorized milestone does not require repeated permission.

The approved production server is .NET 10 ASP.NET Core (C#) with SQL Server 2025 Express (DEC-054), reusing qualified ETP platform code and selected SCC rules. React/Capacitor remain the clients. Do not build a NestJS server. Port accepted HCC/lab rules to C# with provenance and parity tests; keep H02/H04 lab-only until accepted for production use. Keep all six reference clones unchanged.

Work locally in bounded milestones, one stream by default. Do not publish or push. Record actual checks, failures and unrun tests. Update HANDOFF.md and the appropriate evidence report before stopping.

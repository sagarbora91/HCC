-- HCC migration 4238 · ACC-2 source kinds and voucher types (02 Planning/agents/ACC2-POSTING-CONTRACT.md §1; lane acc2-manual, block 4238–4241).
-- acc.source.source_kind gains the ACC-2 kinds (Hcc.Domain.Accounting.SourceKinds.All): manual vouchers, purchases, vendor payments, tax
-- entries (RCM, GST set-off, TDS), OTA payouts. acc.source.voucher_type gains Tally's Purchase and Contra (VoucherType enum, prefixes PUR / CTR).
-- Written to run after any earlier ACC-2 migration that touched the same constraints: each is dropped when present and re-created with the
-- full contract list (a superset of 1000_accounting.sql). acc.locked_voucher.voucher_type has no CHECK (VARCHAR(8) fits Purchase).
IF OBJECT_ID(N'acc.CK_acc_source_kind', N'C') IS NOT NULL ALTER TABLE acc.source DROP CONSTRAINT CK_acc_source_kind;
IF OBJECT_ID(N'acc.CK_acc_source_type', N'C') IS NOT NULL ALTER TABLE acc.source DROP CONSTRAINT CK_acc_source_type;
GO
ALTER TABLE acc.source ADD CONSTRAINT CK_acc_source_kind
  CHECK (source_kind IN ('line','receipt','pos','expense','closing','payroll','manual','purchase','vendor','tax','ota'));
ALTER TABLE acc.source ADD CONSTRAINT CK_acc_source_type
  CHECK (voucher_type IN ('Sales','Receipt','Payment','Journal','Purchase','Contra'));
GO

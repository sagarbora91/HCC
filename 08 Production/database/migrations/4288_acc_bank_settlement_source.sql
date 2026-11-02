-- HCC migration 4288 · ACC-4 acc4-bank · vouchers made from a statement row (ACC4-CONTRACT §2.3 rule 5, §2.5 B8). The B8 settlement journal
-- (Dr bank net · Dr 5080 MDR (+ GST unless ITC → 1310 / 1320) · Cr 1010 / 1020 gross), bank charges and interest post as approved manual
-- vouchers carrying the statement row they came from; the template list gains 'bank-settlement'.
IF OBJECT_ID(N'acc.CK_acc_manual_voucher_template', N'C') IS NOT NULL ALTER TABLE acc.manual_voucher DROP CONSTRAINT CK_acc_manual_voucher_template;
GO
ALTER TABLE acc.manual_voucher ADD CONSTRAINT CK_acc_manual_voucher_template CHECK (template IS NULL OR template IN ('journal','contra','capital-introduced',
  'owner-drawing-bank','loan-received','loan-emi','bank-charges','interest-income','bad-debt','statutory-remittance','gst-payment','tds-remittance','bank-settlement'));
ALTER TABLE acc.manual_voucher ADD bank_row_id BIGINT NULL CONSTRAINT FK_acc_manual_voucher_bank_row REFERENCES acc.bank_statement_row;
GO

-- HCC migration 4285 · ACC-4 acc4-bank · bank statement layout master (ACC4-CONTRACT §2.1; owner default A23: no bank CSV yet → generic layout).
-- Owner master. Seed row "Generic": date, narration, ref, debit, credit, balance · DD/MM/YYYY · comma · one header row (same as
-- deployment/import-templates/bank-statement-layouts.csv and the prototype seed fixtures/ext/bank-layouts.ts).
CREATE TABLE mst.bank_statement_layout(
  layout_id VARCHAR(40) NOT NULL CONSTRAINT PK_mst_bank_statement_layout PRIMARY KEY,
  name NVARCHAR(80) NOT NULL,
  bank_ledger VARCHAR(41) NOT NULL,
  delimiter CHAR(1) NOT NULL CONSTRAINT CK_mst_bank_layout_delim CHECK (delimiter IN (',', ';', CHAR(9))),
  header_rows TINYINT NOT NULL CONSTRAINT CK_mst_bank_layout_header CHECK (header_rows BETWEEN 1 AND 10),
  date_format VARCHAR(11) NOT NULL CONSTRAINT CK_mst_bank_layout_date CHECK (date_format IN ('DD/MM/YYYY','DD-MM-YYYY','YYYY-MM-DD','DD-MMM-YYYY')),
  col_date NVARCHAR(60) NOT NULL, col_narration NVARCHAR(60) NOT NULL, col_ref NVARCHAR(60) NULL, col_debit NVARCHAR(60) NULL, col_credit NVARCHAR(60) NULL,
  col_amount NVARCHAR(60) NULL, col_drcr NVARCHAR(60) NULL, col_balance NVARCHAR(60) NULL,
  ref_pattern NVARCHAR(200) NULL, settlement_pattern NVARCHAR(200) NULL,
  active BIT NOT NULL CONSTRAINT DF_mst_bank_layout_active DEFAULT (1),
  row_ver ROWVERSION,
  CONSTRAINT CK_mst_bank_layout_amounts CHECK ((col_debit IS NOT NULL AND col_credit IS NOT NULL) OR col_amount IS NOT NULL));
INSERT mst.bank_statement_layout(layout_id, name, bank_ledger, delimiter, header_rows, date_format, col_date, col_narration, col_ref, col_debit, col_credit,
  col_balance, settlement_pattern)
VALUES ('generic', N'Generic', '1030', ',', 1, 'DD/MM/YYYY', N'date', N'narration', N'ref', N'debit', N'credit', N'balance', N'(UPI|CARD|POS|MERCHANT)[^A-Z]*(SETTL|STL)');

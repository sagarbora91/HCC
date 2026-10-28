-- HCC migration 4253 · ACC-2 flows: company retire (contract §8; ACC1-DONE §6 / §7 item 10; DEC-157). A retired company account is hidden from
-- the pickers and takes no new booking, block or credit; retiring refuses a non-zero 1110/CMP-<id> ledger balance, open stays and open
-- group masters (SetCompanyActive). Every existing account stays active.
ALTER TABLE grp.company_account ADD
  active BIT NOT NULL CONSTRAINT DF_company_account_active DEFAULT 1,
  retired_by INT NULL,
  retired_utc DATETIME2(3) NULL,
  retire_reason NVARCHAR(200) NULL;
GO
ALTER TABLE grp.company_account ADD CONSTRAINT CK_company_account_retired CHECK ((active = 1 AND retired_by IS NULL AND retired_utc IS NULL) OR (active = 0 AND retired_by IS NOT NULL AND retired_utc IS NOT NULL));
GO

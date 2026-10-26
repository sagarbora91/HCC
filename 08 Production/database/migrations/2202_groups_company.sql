-- HCC migration 2202 · Company accounts (W4 N1, Groups block 2200-2299). Prototype module 14 "New company": a company account with
-- GSTIN, contact, optional negotiated nightly rates per room type code and a credit limit. The credit limit is maker-checker:
-- the Owner sets it directly (PERM-GRP-003 within limit), anyone else with group.manage raises a request that a different
-- approver decides. Original HCC design; amounts are integer paise; event times are written by the application from its TimeProvider.

CREATE TABLE grp.company_account(
  company_id BIGINT IDENTITY CONSTRAINT PK_company_account PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_company_account_property REFERENCES inv.property,
  name NVARCHAR(200) NOT NULL,
  gstin CHAR(15) NULL,
  contact NVARCHAR(120) NULL,
  phone VARCHAR(20) NULL,
  email NVARCHAR(120) NULL,
  city NVARCHAR(80) NULL,
  rates_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_company_rates DEFAULT '{}' CONSTRAINT CK_company_rates CHECK (ISJSON(rates_json) = 1),
  credit_limit_paise BIGINT NOT NULL CONSTRAINT DF_company_limit DEFAULT 0 CONSTRAINT CK_company_limit CHECK (credit_limit_paise BETWEEN 0 AND 100000000000),
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_company_gstin CHECK (gstin IS NULL OR LEN(gstin) = 15));
CREATE UNIQUE INDEX UX_company_name ON grp.company_account(property_id, name);

CREATE TABLE grp.company_limit_request(
  request_id BIGINT IDENTITY CONSTRAINT PK_company_limit_request PRIMARY KEY,
  company_id BIGINT NOT NULL CONSTRAINT FK_company_limit_company REFERENCES grp.company_account,
  from_paise BIGINT NOT NULL,
  to_paise BIGINT NOT NULL CONSTRAINT CK_company_limit_to CHECK (to_paise BETWEEN 0 AND 100000000000),
  reason NVARCHAR(200) NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_company_limit_status CHECK (status IN ('Pending','Approved','Rejected')),
  requested_by INT NOT NULL,
  requested_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  note NVARCHAR(200) NULL,
  CONSTRAINT CK_company_limit_sod CHECK (decided_by IS NULL OR decided_by <> requested_by));
CREATE UNIQUE INDEX UX_company_limit_pending ON grp.company_limit_request(company_id) WHERE status = 'Pending';

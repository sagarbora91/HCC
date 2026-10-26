-- HCC migration 3500 · Company credit enforced (DEC-076, audit A03a-05; Wave B item B5, block 3500–3509). Original HCC design.
-- Until now a company credit limit was a stored number only: a booking or a folio put on company credit named the company in free
-- text (inv.reservation.company_name, the receipt reference) and no limit was checked. This migration links company billing to the
-- company account (grp.company_account) so the application can compute the company's exposure and refuse beyond the limit:
--   inv.reservation.company_id  the booking is billed to this company account (its stay counts in the exposure; company credit at checkout
--                               needs it). company_name / company_gstin stay the invoice details (a free-text company is NOT billed to an account).
--   pay.receipt.company_id      a company-credit receipt executed at checkout: the receivable the company owes (one per stay). Group-master
--                               transfers keep NULL (the group master holds that receivable).
--   grp.group_account.company_id  the group master is billed to this company account (its routed master balance counts in the exposure).
--   grp.company_payment         money received from the company against its company-credit receivable (non-cash; append-only), which frees credit.
-- Existing rows stay NULL (no name matching): link bookings and groups explicitly. Money = BIGINT paise; event times are written by the
-- application from its TimeProvider.
ALTER TABLE inv.reservation ADD company_id BIGINT NULL CONSTRAINT FK_reservation_company REFERENCES grp.company_account;
GO
CREATE INDEX IX_reservation_company ON inv.reservation(company_id, status) WHERE company_id IS NOT NULL;
GO
ALTER TABLE pay.receipt ADD company_id BIGINT NULL CONSTRAINT FK_receipt_company REFERENCES grp.company_account;
GO
ALTER TABLE pay.receipt ADD CONSTRAINT CK_receipt_company CHECK (company_id IS NULL OR kind = 'company-credit');
CREATE INDEX IX_receipt_company ON pay.receipt(company_id) INCLUDE (amount_paise) WHERE company_id IS NOT NULL;
GO
ALTER TABLE grp.group_account ADD company_id BIGINT NULL CONSTRAINT FK_group_account_company REFERENCES grp.company_account;
GO
CREATE INDEX IX_group_account_company ON grp.group_account(company_id, status) WHERE company_id IS NOT NULL;
GO
CREATE TABLE grp.company_payment(
  payment_id BIGINT IDENTITY CONSTRAINT PK_company_payment PRIMARY KEY,
  company_id BIGINT NOT NULL CONSTRAINT FK_company_payment_company REFERENCES grp.company_account,
  business_date DATE NOT NULL,
  tender VARCHAR(16) NOT NULL CONSTRAINT CK_company_payment_tender CHECK (tender IN ('UPI','Card','Bank transfer')),
  reference NVARCHAR(120) NOT NULL CONSTRAINT CK_company_payment_reference CHECK (LEN(reference) > 0),
  invoice_no VARCHAR(16) NULL,                 -- the tax invoice the company says it pays (statement text; the receivable is one balance)
  amount_paise BIGINT NOT NULL CONSTRAINT CK_company_payment_amount CHECK (amount_paise > 0 AND amount_paise <= 100000000000),
  principal_id INT NOT NULL,
  recorded_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_company_payment_company ON grp.company_payment(company_id) INCLUDE (amount_paise);
GO
CREATE TRIGGER grp.TR_company_payment_append_only ON grp.company_payment AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 52203, 'grp.company_payment is append-only.', 1;
END;
GO

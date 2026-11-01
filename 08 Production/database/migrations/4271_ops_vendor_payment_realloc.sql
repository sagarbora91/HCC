-- HCC migration 4271 · ACC-3 acc3-carry C3 (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §7, §9): ReallocateVendorPayment (PERM-EXP-004 Owner /
-- Manager / Accountant) replaces a live payment's rows in ops.vendor_payment_alloc; each change keeps an audit row here with the lists before
-- and after (JSON: [{expenseId, amountPaise, opening}]). The voucher does not change (it debits the vendor's party ledger either way); only
-- the bill-wise ageing follows. Append-only.
CREATE TABLE ops.vendor_payment_realloc(
  realloc_id BIGINT IDENTITY CONSTRAINT PK_vendor_payment_realloc PRIMARY KEY,
  vendor_payment_id BIGINT NOT NULL CONSTRAINT FK_vendor_payment_realloc_payment REFERENCES ops.vendor_payment,
  before_json NVARCHAR(MAX) NOT NULL CONSTRAINT CK_vendor_payment_realloc_before CHECK (ISJSON(before_json) = 1),
  after_json NVARCHAR(MAX) NOT NULL CONSTRAINT CK_vendor_payment_realloc_after CHECK (ISJSON(after_json) = 1),
  changed_by INT NOT NULL,
  changed_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_vendor_payment_realloc_payment ON ops.vendor_payment_realloc(vendor_payment_id);
GO
CREATE TRIGGER ops.TR_vendor_payment_realloc_append_only ON ops.vendor_payment_realloc AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  THROW 52711, 'ops.vendor_payment_realloc is append-only.', 1;
END;
GO

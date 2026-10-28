-- HCC migration 4232 · ACC-2 vendor payments and advances (contract §3.2; rulebook C3, C4). One row per payment to a vendor (bank / UPI need
-- the reference; cash leaves the drawer of the open shift, pay.cash_movement source 'vendor-payment'); allocations to open bills, oldest
-- first by default — the unallocated rest is an advance (the party ledger 2200/<vendor> goes Dr). A void on the payment's own business day
-- posts nothing; a later void posts the reversal on the void date (voided_date). Accounting: VendorPaymentRecorded / VendorPaymentVoided.
CREATE TABLE ops.vendor_payment(
  vendor_payment_id BIGINT IDENTITY CONSTRAINT PK_vendor_payment PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_vendor_payment_property REFERENCES inv.property,
  vendor_code VARCHAR(20) NOT NULL,
  vendor_name NVARCHAR(60) NOT NULL,
  business_date DATE NOT NULL,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_vendor_payment_amount CHECK (amount_paise BETWEEN 1 AND 10000000000),
  method NVARCHAR(60) NOT NULL,
  reference NVARCHAR(120) NULL,
  till_id INT NULL CONSTRAINT FK_vendor_payment_till REFERENCES pay.till,
  cash_movement_id BIGINT NULL,
  cash_deduction_lost BIT NOT NULL CONSTRAINT DF_vendor_payment_cash_lost DEFAULT 0,
  recorded_by INT NOT NULL,
  recorded_utc DATETIME2(3) NOT NULL,
  voided BIT NOT NULL CONSTRAINT DF_vendor_payment_voided DEFAULT 0,
  void_reason NVARCHAR(200) NULL,
  voided_by INT NULL,
  voided_date DATE NULL,
  voided_utc DATETIME2(3) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_vendor_payment_void CHECK ((voided = 0 AND void_reason IS NULL AND voided_by IS NULL AND voided_date IS NULL AND voided_utc IS NULL)
    OR (voided = 1 AND LEN(void_reason) >= 3 AND voided_by IS NOT NULL AND voided_date IS NOT NULL AND voided_utc IS NOT NULL)));
CREATE INDEX IX_vendor_payment_vendor ON ops.vendor_payment(property_id, vendor_code, business_date);
CREATE INDEX IX_vendor_payment_date ON ops.vendor_payment(property_id, business_date);

-- Coordinator ruling (ACC-2, rulebook T11): a vendor's opening balance (approved opening entry on 2200/<vendor>) has no expense record — it is
-- the oldest open item, allocated with expense_id NULL and opening = 1.
CREATE TABLE ops.vendor_payment_alloc(
  alloc_id BIGINT IDENTITY CONSTRAINT PK_vendor_payment_alloc PRIMARY KEY,
  vendor_payment_id BIGINT NOT NULL CONSTRAINT FK_vendor_payment_alloc_payment REFERENCES ops.vendor_payment,
  expense_id BIGINT NULL CONSTRAINT FK_vendor_payment_alloc_expense REFERENCES ops.expense,
  opening BIT NOT NULL CONSTRAINT DF_vendor_payment_alloc_opening DEFAULT 0,
  amount_paise BIGINT NOT NULL CONSTRAINT CK_vendor_payment_alloc_amount CHECK (amount_paise BETWEEN 1 AND 10000000000),
  CONSTRAINT CK_vendor_payment_alloc_target CHECK ((opening = 1 AND expense_id IS NULL) OR (opening = 0 AND expense_id IS NOT NULL)));
CREATE UNIQUE INDEX UX_vendor_payment_alloc_expense ON ops.vendor_payment_alloc(vendor_payment_id, expense_id) WHERE expense_id IS NOT NULL;
CREATE UNIQUE INDEX UX_vendor_payment_alloc_opening ON ops.vendor_payment_alloc(vendor_payment_id) WHERE opening = 1;
CREATE INDEX IX_vendor_payment_alloc_expense ON ops.vendor_payment_alloc(expense_id) INCLUDE (amount_paise) WHERE expense_id IS NOT NULL;
GO

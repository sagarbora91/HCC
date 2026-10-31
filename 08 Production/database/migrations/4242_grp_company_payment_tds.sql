-- HCC migration 4242 · ACC-2 company payment net of TDS (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4 A9; rulebook §5 A9, §8.2 T7b; lane acc2-tds).
-- A company paying the hotel under a regular arrangement deducts TDS (s.393, old 194-I, on the taxable value excluding GST) and pays the net:
-- RecordCompanyPayment carries the TDS it deducted. amount_paise + tds_paise settle the company-credit receivable; accounting books
-- Receipt Dr tender (amount) · Dr 1360 TDS receivable (tds) · Cr 1110/<company> (amount + tds). Existing rows: 0 (no TDS).
-- TDS is at most 20 % of what the payment settles (the no-PAN rate; the application refuses more with the same wording as the prototype).
ALTER TABLE grp.company_payment ADD
  tds_paise BIGINT NOT NULL CONSTRAINT DF_company_payment_tds DEFAULT 0
    CONSTRAINT CK_company_payment_tds CHECK (tds_paise >= 0 AND tds_paise <= 100000000000);
GO
ALTER TABLE grp.company_payment ADD CONSTRAINT CK_company_payment_tds_share CHECK (tds_paise * 5 <= amount_paise + tds_paise);
GO

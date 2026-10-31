-- HCC migration 4250 · ACC-2 flows (lane acc2-flows; 02 Planning/agents/ACC2-POSTING-CONTRACT.md §4; rulebook A6a, A13). Two new folio
-- receipt kinds, neither moves cash (no till, no shift, no cash movement):
--   deposit-apply  the refundable deposit held applied to the folio at check-out (ApplyDeposit): Journal Dr 2000 Advance deposits · Cr 1100
--   ota-credit     an OTA-paid stay's folio balance moved to the channel's receivable (SettleFolioToOta): Journal Dr 1115/<channel> · Cr 1100
ALTER TABLE pay.receipt DROP CONSTRAINT CK_receipt_kind;
GO
ALTER TABLE pay.receipt ADD CONSTRAINT CK_receipt_kind CHECK (kind IN ('payment','deposit','deposit-return','refund','company-credit','deposit-apply','ota-credit'));
GO
ALTER TABLE pay.receipt ADD CONSTRAINT CK_receipt_flows_no_cash CHECK (kind NOT IN ('deposit-apply','ota-credit') OR (till_id IS NULL AND shift_id IS NULL));
GO

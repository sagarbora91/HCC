-- HCC migration 4252 · ACC-2 flows: a mistaken Udhaar desk payment reversed by the Owner (contract §8; rulebook A17b). The reversal is a
-- NEGATIVE settlement row on the business date naming the payment it reverses (reversal_of) and the Owner's reason; the outstanding comes back,
-- cash leaves the drawer (pay.cash_movement kind payment, negative, source type udhaar-reversal) and Accounting books the negative Receipt
-- (UdhaarPaymentReversed: Dr 1120/<customer> · Cr tender). A payment is reversed at most once; a reversal is never reversed.
ALTER TABLE ops.udhaar_settlement DROP CONSTRAINT CK_udhaar_settlement_amount;
GO
ALTER TABLE ops.udhaar_settlement ADD
  reversal_of BIGINT NULL CONSTRAINT FK_udhaar_settlement_reversal_of REFERENCES ops.udhaar_settlement,
  reversal_reason NVARCHAR(200) NULL;
GO
ALTER TABLE ops.udhaar_settlement ADD CONSTRAINT CK_udhaar_settlement_amount CHECK (
  (reversal_of IS NULL AND amount_paise BETWEEN 1 AND 10000000000 AND reversal_reason IS NULL)
  OR (reversal_of IS NOT NULL AND amount_paise BETWEEN -10000000000 AND -1 AND reversal_reason IS NOT NULL));
CREATE UNIQUE INDEX UX_udhaar_settlement_reversal_of ON ops.udhaar_settlement(reversal_of) WHERE reversal_of IS NOT NULL;
GO

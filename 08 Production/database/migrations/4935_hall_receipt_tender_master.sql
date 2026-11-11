-- HCC migration 4935 · CU-PAY-B: hall receipts take any method of the Payment methods master (mst.payment_method), as pay.receipt / pos / expenses did since 2890.
-- Before (4320): hall.event_receipt.tender VARCHAR(16) CHECK IN ('Cash','UPI','Card','Bank transfer'), so an Owner-added or renamed method was refused by the database.
-- After: NVARCHAR(60) (= mst.payment_method.name) with a length check; the application resolves the value against the master (HallBillingCommands).
-- Re-runnable; existing rows are only widened, never rewritten. CK_hall_event_receipt_side reads tender, so it is dropped before the column change and added again exactly as in 4320.
IF OBJECT_ID(N'hall.CK_hall_event_receipt_tender', N'C') IS NOT NULL ALTER TABLE hall.event_receipt DROP CONSTRAINT CK_hall_event_receipt_tender;
IF OBJECT_ID(N'hall.CK_hall_event_receipt_side', N'C') IS NOT NULL ALTER TABLE hall.event_receipt DROP CONSTRAINT CK_hall_event_receipt_side;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'hall.event_receipt') AND name = N'tender' AND (system_type_id <> TYPE_ID(N'nvarchar') OR max_length <> 120))
    ALTER TABLE hall.event_receipt ALTER COLUMN tender NVARCHAR(60) NULL;
IF OBJECT_ID(N'hall.CK_hall_event_receipt_tender', N'C') IS NULL
    ALTER TABLE hall.event_receipt WITH CHECK ADD CONSTRAINT CK_hall_event_receipt_tender CHECK (tender IS NULL OR LEN(tender) BETWEEN 1 AND 60);
IF OBJECT_ID(N'hall.CK_hall_event_receipt_side', N'C') IS NULL
    ALTER TABLE hall.event_receipt WITH CHECK ADD CONSTRAINT CK_hall_event_receipt_side CHECK ((kind = 'BALANCE' AND (tender IS NOT NULL OR company_id IS NOT NULL OR udhaar = 1)) OR (kind <> 'BALANCE' AND tender IS NOT NULL));

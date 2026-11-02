-- HCC migration 4686 · LF A1 rate change re-post (LF-CONTRACT §2, coordinator ruling 2 Nov 2026): a posted room night of an open day is
-- re-posted at the new price = a reversal row + a new room charge. UX_charge_room_night (0002) allowed one live room charge per folio night
-- (reverses_charge_id IS NULL), which still counted the reversed original. bill.charge.reversed_by_charge_id is set on the original when its
-- reversal row is written (the ONLY update the append-only trigger lets through: NULL -> the reversal's id, every other column unchanged);
-- the index ignores reversed originals.
ALTER TABLE bill.charge ADD reversed_by_charge_id BIGINT NULL CONSTRAINT FK_charge_reversed_by REFERENCES bill.charge;
GO
ALTER TRIGGER bill.TR_charge_append_only ON bill.charge AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;
  IF NOT EXISTS (SELECT 1 FROM inserted) THROW 51001, 'bill.charge is append-only; post a reversal line instead.', 1;
  IF EXISTS (
      SELECT 1 FROM deleted d JOIN inserted i ON i.charge_id = d.charge_id
      WHERE d.reversed_by_charge_id IS NOT NULL OR i.reversed_by_charge_id IS NULL
         OR NOT EXISTS (SELECT 1 FROM bill.charge r WHERE r.charge_id = i.reversed_by_charge_id AND r.reverses_charge_id = i.charge_id)
         OR EXISTS (SELECT d.folio_id, d.kind, d.service_date, d.label, d.amount_paise, d.tax_bps, d.tax_paise, d.reverses_charge_id, d.posted_utc, d.principal_id, d.approval_id
                    EXCEPT SELECT i.folio_id, i.kind, i.service_date, i.label, i.amount_paise, i.tax_bps, i.tax_paise, i.reverses_charge_id, i.posted_utc, i.principal_id, i.approval_id))
    THROW 51001, 'bill.charge is append-only; post a reversal line instead.', 1;
END;
GO
DROP INDEX UX_charge_room_night ON bill.charge;
CREATE UNIQUE INDEX UX_charge_room_night ON bill.charge(folio_id, service_date)
  WHERE kind = 'room' AND reverses_charge_id IS NULL AND reversed_by_charge_id IS NULL;

-- HCC migration 4258 · ACC-2 parity (02 Planning/agents/ACC2-POSTING-CONTRACT.md §8, rulebook A3; coordinator ruling 3, 28 Oct 2026): the folio
-- charge kind 'food' — F&B charged to a folio (a restaurant / room-service bill posted to the room, an extra of GST class food). It books to
-- line:food (default 4200 F&B revenue); the daily report keeps it inside service revenue; existing 'service' charges are unchanged.
ALTER TABLE bill.charge DROP CONSTRAINT CK_charge_kind;
GO
ALTER TABLE bill.charge ADD CONSTRAINT CK_charge_kind CHECK (kind IN ('room','service','food','discount','correction'));
GO

-- HCC migration 3951 · supporting indexes for the "specified premises" derivation (SqlGstMasterStore.MaxUnitTariffSql; Wave C7, found in
-- the Wave C room-board probe: about 15,000 page reads on average and up to 119,000 / 1.34 s on a 240-day seed, growing with last year's
-- room nights). The statement now takes the highest charged room night of the previous FY that belongs to the property and was not
-- reversed by walking the room nights from the highest amount down, instead of joining every night of the year to its folio and
-- reservation. Indexes only: no table or column changes, same result (05 Delivery Evidence/Audit/probes/A03a_specified_premises_perf.md).
-- Room-night charges that are not themselves reversals, highest amount first (covering: date, folio; charge_id is the clustered key).
CREATE INDEX IX_charge_room_night_amount ON bill.charge(amount_paise DESC, service_date) INCLUDE (folio_id)
  WHERE kind = 'room' AND reverses_charge_id IS NULL;
-- The reversal of a charge (A03a-02: a reversed night does not count); only reversal rows are in it.
CREATE INDEX IX_charge_reverses ON bill.charge(reverses_charge_id) WHERE reverses_charge_id IS NOT NULL;
GO

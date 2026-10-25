-- HCC migration 0200 · P2 inventory & reservations (contracts package §3 GetAvailability/QuoteStay/CreateReservation/
-- AmendReservation/CancelReservation/MarkNoShow; §4 rules 1, 2, 7, 8, 9, 10). Original HCC design; no donor migrations.
-- Extends the 0002 inventory tables (never edits 0002). Lock order used by every writer (deadlock-free by construction):
--   inv.group_block_type rows (block id asc) → inv.type_night_count (room type asc, night asc) → inv.room_night (room, night asc).
-- Wave 1 note: lives in database/modules/ (with P3/P4) until the Wave 1 integration step relaxes EmbeddedMigrationSource.Validate
-- (unique, ascending, gaps allowed) and moves the module scripts into database/migrations/.

-- ── Room types: rack rate (fallback tariff, prototype room.rate) and display order ──
ALTER TABLE inv.room_type ADD
  name NVARCHAR(60) NULL,
  rack_rate_paise BIGINT NOT NULL CONSTRAINT DF_room_type_rack DEFAULT 0
    CONSTRAINT CK_room_type_rack CHECK (rack_rate_paise BETWEEN 0 AND 10000000000),
  sort_order INT NOT NULL CONSTRAINT DF_room_type_sort DEFAULT 0;
GO
-- Effective-dated tariff per room type (prototype adminTariffOn: latest effective_from <= date, else rack rate).
CREATE TABLE inv.room_type_tariff(
  room_type_id INT NOT NULL CONSTRAINT FK_tariff_room_type REFERENCES inv.room_type,
  effective_from DATE NOT NULL,
  rate_paise BIGINT NOT NULL CONSTRAINT CK_tariff_rate CHECK (rate_paise > 0 AND rate_paise <= 10000000000),
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_tariff_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_room_type_tariff PRIMARY KEY (room_type_id, effective_from));

-- Out-of-service blocks (prototype room.blocked/blockFrom/blockTo, blockedDuring). Covers nights [from_night, to_night);
-- NULL from = no lower bound, NULL to = open-ended. P3 maintenance blocks write here through IRoomBlockPort (source = 'Maintenance').
CREATE TABLE inv.room_block(
  room_block_id BIGINT IDENTITY CONSTRAINT PK_room_block PRIMARY KEY,
  room_id INT NOT NULL CONSTRAINT FK_room_block_room REFERENCES inv.room,
  from_night DATE NULL,
  to_night DATE NULL,
  reason NVARCHAR(200) NOT NULL,
  source VARCHAR(20) NOT NULL CONSTRAINT DF_room_block_source DEFAULT 'Inventory'
    CONSTRAINT CK_room_block_source CHECK (source IN ('Inventory','Maintenance')),
  source_ref BIGINT NULL,
  active BIT NOT NULL CONSTRAINT DF_room_block_active DEFAULT 1,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_room_block_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT CK_room_block_dates CHECK (from_night IS NULL OR to_night IS NULL OR to_night > from_night));
CREATE INDEX IX_room_block_room ON inv.room_block(room_id) WHERE active = 1;

-- House stay restrictions (A2.1, DEC-053; prototype YLDRestriction): arrivals in [arrival_from, arrival_to] inclusive.
CREATE TABLE inv.stay_restriction(
  restriction_id INT IDENTITY CONSTRAINT PK_stay_restriction PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_restriction_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  room_type_id INT NULL CONSTRAINT FK_restriction_room_type REFERENCES inv.room_type,   -- NULL = all types ('*')
  arrival_from DATE NOT NULL,
  arrival_to DATE NOT NULL,
  min_stay TINYINT NOT NULL CONSTRAINT CK_restriction_min_stay CHECK (min_stay BETWEEN 1 AND 30),
  closed_to_arrival BIT NOT NULL,
  note NVARCHAR(200) NOT NULL CONSTRAINT DF_restriction_note DEFAULT N'',
  active BIT NOT NULL CONSTRAINT DF_restriction_active DEFAULT 1,
  CONSTRAINT UQ_stay_restriction UNIQUE (property_id, code),
  CONSTRAINT CK_restriction_dates CHECK (arrival_to >= arrival_from));

-- Group blocks (module 14): allotment per room type over [arrival, departure); unpicked rooms are soft-held until cut-off.
CREATE TABLE inv.group_block(
  group_block_id BIGINT IDENTITY CONSTRAINT PK_group_block PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_group_block_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(200) NOT NULL,
  arrival DATE NOT NULL,
  departure DATE NOT NULL,
  cutoff DATE NOT NULL,
  status VARCHAR(10) NOT NULL CONSTRAINT CK_group_block_status CHECK (status IN ('Open','Released','Cancelled')),
  released_utc DATETIME2(3) NULL,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_group_block_created DEFAULT SYSUTCDATETIME(),
  row_ver ROWVERSION,
  CONSTRAINT UQ_group_block UNIQUE (property_id, code),
  CONSTRAINT CK_group_block_dates CHECK (departure > arrival));
CREATE TABLE inv.group_block_type(
  group_block_id BIGINT NOT NULL CONSTRAINT FK_gbt_block REFERENCES inv.group_block,
  room_type_id INT NOT NULL CONSTRAINT FK_gbt_room_type REFERENCES inv.room_type,
  allotted INT NOT NULL,
  picked INT NOT NULL CONSTRAINT DF_gbt_picked DEFAULT 0,
  released INT NOT NULL CONSTRAINT DF_gbt_released DEFAULT 0,
  rate_paise BIGINT NULL CONSTRAINT CK_gbt_rate CHECK (rate_paise IS NULL OR (rate_paise > 0 AND rate_paise <= 10000000000)),
  CONSTRAINT PK_group_block_type PRIMARY KEY (group_block_id, room_type_id),
  CONSTRAINT CK_gbt_counts CHECK (allotted >= 0 AND picked >= 0 AND released >= 0 AND picked <= allotted));

-- Room-type capacity per night (contracts §4 rule 2): sold = room nights allocated, held = unpicked open-block allotment.
-- Updated in the same transaction as inv.room_night; the CHECK is the backstop behind the locked conditional updates.
CREATE TABLE inv.type_night_count(
  room_type_id INT NOT NULL CONSTRAINT FK_tnc_room_type REFERENCES inv.room_type,
  night DATE NOT NULL,
  sellable INT NOT NULL,
  sold INT NOT NULL,
  held INT NOT NULL,
  CONSTRAINT PK_type_night_count PRIMARY KEY (room_type_id, night),
  CONSTRAINT CK_tnc_capacity CHECK (sellable >= 0 AND sold >= 0 AND held >= 0 AND sold + held <= sellable));

-- Reservation details beyond the 0002 baseline.
ALTER TABLE inv.reservation ADD
  room_id INT NULL CONSTRAINT FK_reservation_room REFERENCES inv.room,
  room_type_id INT NULL CONSTRAINT FK_reservation_room_type REFERENCES inv.room_type,
  adults TINYINT NOT NULL CONSTRAINT DF_reservation_adults DEFAULT 1,
  children TINYINT NOT NULL CONSTRAINT DF_reservation_children DEFAULT 0,
  phone VARCHAR(20) NULL,
  source VARCHAR(40) NOT NULL CONSTRAINT DF_reservation_source DEFAULT 'Direct',
  nightly_rate_paise BIGINT NULL CONSTRAINT CK_reservation_rate CHECK (nightly_rate_paise IS NULL OR (nightly_rate_paise >= 0 AND nightly_rate_paise <= 10000000000)),
  quote_id BIGINT NULL,
  group_block_id BIGINT NULL CONSTRAINT FK_reservation_group_block REFERENCES inv.group_block,
  restriction_override NVARCHAR(400) NULL,
  cancel_reason NVARCHAR(200) NULL,
  notes NVARCHAR(400) NULL,
  created_by INT NULL,
  status_utc DATETIME2(3) NULL;
GO
CREATE INDEX IX_reservation_room ON inv.reservation(room_id, arrival) INCLUDE (departure, status, hold_until_utc);
CREATE INDEX IX_reservation_held ON inv.reservation(hold_until_utc) WHERE status = 'Held';
CREATE INDEX IX_reservation_block ON inv.reservation(group_block_id) WHERE group_block_id IS NOT NULL;
CREATE SEQUENCE inv.seq_reservation_no AS BIGINT START WITH 1101 INCREMENT BY 1;

-- Agreed price per night (snapshot at booking/amend: rate + tax rate + tax). Billing posts room nights from it.
CREATE TABLE inv.reservation_night(
  reservation_id BIGINT NOT NULL CONSTRAINT FK_reservation_night_res REFERENCES inv.reservation,
  night DATE NOT NULL,
  rate_paise BIGINT NOT NULL CONSTRAINT CK_reservation_night_rate CHECK (rate_paise BETWEEN 0 AND 10000000000),
  tax_bps INT NOT NULL CONSTRAINT CK_reservation_night_tax CHECK (tax_bps BETWEEN 0 AND 10000),
  tax_paise BIGINT NOT NULL,
  CONSTRAINT PK_reservation_night PRIMARY KEY (reservation_id, night));

-- Quotes (contracts §3 QuoteStay): nightly rate/tax snapshot, expiry, version; used by at most one reservation.
CREATE TABLE bill.quote(
  quote_id BIGINT IDENTITY CONSTRAINT PK_quote PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_quote_property REFERENCES inv.property,
  principal_id INT NOT NULL,
  room_type_id INT NOT NULL CONSTRAINT FK_quote_room_type REFERENCES inv.room_type,
  room_id INT NULL CONSTRAINT FK_quote_room REFERENCES inv.room,
  arrival DATE NOT NULL,
  departure DATE NOT NULL,
  adults TINYINT NOT NULL,
  children TINYINT NOT NULL,
  rate_plan VARCHAR(20) NOT NULL,
  net_paise BIGINT NOT NULL,
  tax_paise BIGINT NOT NULL,
  total_paise BIGINT NOT NULL,
  expires_utc DATETIME2(3) NOT NULL,
  used_by_reservation_id BIGINT NULL CONSTRAINT FK_quote_reservation REFERENCES inv.reservation,
  created_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_quote_dates CHECK (departure > arrival),
  CONSTRAINT CK_quote_money CHECK (net_paise BETWEEN 0 AND 10000000000 AND tax_paise BETWEEN 0 AND 10000000000 AND total_paise BETWEEN 0 AND 10000000000));
CREATE TABLE bill.quote_night(
  quote_id BIGINT NOT NULL CONSTRAINT FK_quote_night_quote REFERENCES bill.quote,
  night DATE NOT NULL,
  rate_paise BIGINT NOT NULL,
  tax_bps INT NOT NULL CONSTRAINT CK_quote_night_tax CHECK (tax_bps BETWEEN 0 AND 10000),
  tax_paise BIGINT NOT NULL,
  CONSTRAINT PK_quote_night PRIMARY KEY (quote_id, night));
GO
ALTER TABLE inv.reservation ADD CONSTRAINT FK_reservation_quote FOREIGN KEY (quote_id) REFERENCES bill.quote(quote_id);
GO
-- Count deltas for one transaction (sold/held change per room type and night).
CREATE TYPE inv.type_night_delta AS TABLE(
  room_type_id INT NOT NULL,
  night DATE NOT NULL,
  d_sold INT NOT NULL,
  d_held INT NOT NULL,
  d_sellable INT NOT NULL DEFAULT 0,
  PRIMARY KEY (room_type_id, night));
GO
-- Applies count deltas inside the caller's transaction, in ascending (room type, night) order: each row is first ensured
-- with a locked check-then-insert (values computed from room nights, blocks and open block allotment), then updated
-- conditionally. Callers therefore lock block rows first, apply the deltas, and only then change room nights, room blocks or
-- block allotment, so an ensured row never already reflects this transaction's own change. Zero deltas still lock the row (serializes every writer that
-- touches the same room-night). A violation throws 52001 'TYPE_FULL|<type>|<night>' (the caller's transaction is rolled back).
CREATE PROCEDURE inv.usp_apply_type_night_deltas
  @property_id INT, @deltas inv.type_night_delta READONLY
AS
BEGIN
  SET NOCOUNT ON; SET XACT_ABORT ON;
  BEGIN TRY
    DECLARE @t INT, @n DATE, @ds INT, @dh INT, @dse INT;
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT room_type_id, night, d_sold, d_held, d_sellable FROM @deltas ORDER BY room_type_id, night;
    OPEN c;
    FETCH NEXT FROM c INTO @t, @n, @ds, @dh, @dse;
    WHILE @@FETCH_STATUS = 0
    BEGIN
      IF NOT EXISTS (SELECT 1 FROM inv.type_night_count WITH (UPDLOCK, HOLDLOCK) WHERE room_type_id = @t AND night = @n)
        INSERT inv.type_night_count(room_type_id, night, sellable, sold, held)
        SELECT @t, @n,
          (SELECT COUNT(*) FROM inv.room r WHERE r.room_type_id = @t AND r.property_id = @property_id
             AND NOT EXISTS (SELECT 1 FROM inv.room_block b WHERE b.room_id = r.room_id AND b.active = 1
                               AND (b.from_night IS NULL OR b.from_night <= @n) AND (b.to_night IS NULL OR b.to_night > @n))),
          (SELECT COUNT(*) FROM inv.room_night rn JOIN inv.room r ON r.room_id = rn.room_id WHERE r.room_type_id = @t AND rn.night = @n),
          (SELECT ISNULL(SUM(gt.allotted - gt.picked), 0) FROM inv.group_block_type gt JOIN inv.group_block g ON g.group_block_id = gt.group_block_id
             WHERE gt.room_type_id = @t AND g.status = 'Open' AND g.arrival <= @n AND g.departure > @n);
      UPDATE inv.type_night_count SET sold = sold + @ds, held = held + @dh, sellable = sellable + @dse
        WHERE room_type_id = @t AND night = @n AND sold + @ds >= 0 AND held + @dh >= 0 AND sellable + @dse >= 0
          AND sold + @ds + held + @dh <= sellable + @dse;
      IF @@ROWCOUNT = 0
      BEGIN
        DECLARE @msg NVARCHAR(200) = CONCAT(N'TYPE_FULL|', @t, N'|', CONVERT(NVARCHAR(10), @n, 23));
        THROW 52001, @msg, 1;
      END
      FETCH NEXT FROM c INTO @t, @n, @ds, @dh, @dse;
    END
    CLOSE c; DEALLOCATE c;
  END TRY
  BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK; THROW; END CATCH
END;
GO
-- Operations / data import: recompute counts for a property and night range from room nights, blocks and open allotment.
-- Takes an exclusive table lock; run while no booking traffic is expected (import, repair after a verified mismatch).
CREATE PROCEDURE inv.usp_rebuild_type_night_count
  @property_id INT, @from DATE, @to DATE
AS
BEGIN
  SET NOCOUNT ON; SET XACT_ABORT ON;
  BEGIN TRY
  BEGIN TRAN;
    DELETE c FROM inv.type_night_count c WITH (TABLOCKX)
      JOIN inv.room_type t ON t.room_type_id = c.room_type_id
      WHERE t.property_id = @property_id AND c.night >= @from AND c.night < @to;
    ;WITH nights AS (
      SELECT TOP (DATEDIFF(day, @from, @to)) DATEADD(day, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1, @from) AS night FROM sys.all_objects)
    INSERT inv.type_night_count(room_type_id, night, sellable, sold, held)
    SELECT t.room_type_id, n.night,
      (SELECT COUNT(*) FROM inv.room r WHERE r.room_type_id = t.room_type_id
         AND NOT EXISTS (SELECT 1 FROM inv.room_block b WHERE b.room_id = r.room_id AND b.active = 1
                           AND (b.from_night IS NULL OR b.from_night <= n.night) AND (b.to_night IS NULL OR b.to_night > n.night))),
      (SELECT COUNT(*) FROM inv.room_night rn JOIN inv.room r ON r.room_id = rn.room_id WHERE r.room_type_id = t.room_type_id AND rn.night = n.night),
      (SELECT ISNULL(SUM(gt.allotted - gt.picked), 0) FROM inv.group_block_type gt JOIN inv.group_block g ON g.group_block_id = gt.group_block_id
         WHERE gt.room_type_id = t.room_type_id AND g.status = 'Open' AND g.arrival <= n.night AND g.departure > n.night)
    FROM inv.room_type t CROSS JOIN nights n WHERE t.property_id = @property_id;
  COMMIT;
  END TRY
  BEGIN CATCH IF @@TRANCOUNT > 0 ROLLBACK; THROW; END CATCH
END;
GO
-- Verification: maintained counts that differ from a recomputation (empty result = consistent).
CREATE VIEW inv.v_type_night_count_drift AS
SELECT c.room_type_id, c.night, c.sellable, c.sold, c.held, x.sellable AS sellable_expected, x.sold AS sold_expected, x.held AS held_expected
FROM inv.type_night_count c
CROSS APPLY (SELECT
    (SELECT COUNT(*) FROM inv.room r WHERE r.room_type_id = c.room_type_id
       AND NOT EXISTS (SELECT 1 FROM inv.room_block b WHERE b.room_id = r.room_id AND b.active = 1
                         AND (b.from_night IS NULL OR b.from_night <= c.night) AND (b.to_night IS NULL OR b.to_night > c.night))) AS sellable,
    (SELECT COUNT(*) FROM inv.room_night rn JOIN inv.room r ON r.room_id = rn.room_id WHERE r.room_type_id = c.room_type_id AND rn.night = c.night) AS sold,
    (SELECT ISNULL(SUM(gt.allotted - gt.picked), 0) FROM inv.group_block_type gt JOIN inv.group_block g ON g.group_block_id = gt.group_block_id
       WHERE gt.room_type_id = c.room_type_id AND g.status = 'Open' AND g.arrival <= c.night AND g.departure > c.night) AS held) x
WHERE c.sellable <> x.sellable OR c.sold <> x.sold OR c.held <> x.held;

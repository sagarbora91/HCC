-- HCC migration 0202 · W3-8 room-block history (Inventory block 0202-0299). Original HCC design; never edits 0200.
-- A released or edited block keeps its past: inv.room_block gets released_on (the business date the release took effect) and
-- the block's effective window becomes [from_night, min(to_night, released_on)); nights before the release date stay blocked
-- and their type-night counts stay reduced. Every create/edit/release is also written to inv.room_block_history with the actor
-- and UTC time. An edit is clip-and-replace: the old row is released as of the edit date and a successor row (same source and
-- source_ref) carries the new window. "As of" queries (GetAvailability AsOf, room status) read these rows; they never use today's state
-- for a release or edit that happened after the asked date.

ALTER TABLE inv.room_block ADD
  created_on DATE NULL,          -- business date the block was created
  released_on DATE NULL,         -- business date the release (or edit) took effect; coverage ends there
  released_by INT NULL,
  released_utc DATETIME2(3) NULL,
  successor_block_id BIGINT NULL;
GO
-- Existing blocks: created on the UTC date of creation; already-released blocks (no release date was kept) cover nothing from now on
-- except nights before their creation date, i.e. they are treated as released on their creation date.
UPDATE inv.room_block SET created_on = CAST(created_utc AS DATE);
UPDATE inv.room_block SET released_on = created_on, released_utc = created_utc WHERE active = 0;
GO
ALTER TABLE inv.room_block ALTER COLUMN created_on DATE NOT NULL;
ALTER TABLE inv.room_block ADD CONSTRAINT DF_room_block_created_on DEFAULT (CAST(SYSUTCDATETIME() AS DATE)) FOR created_on;
ALTER TABLE inv.room_block ADD CONSTRAINT CK_room_block_release CHECK (active = 1 OR released_on IS NOT NULL);
CREATE INDEX IX_room_block_room_released ON inv.room_block(room_id) WHERE released_on IS NOT NULL;
GO
CREATE TABLE inv.room_block_history(
  history_id BIGINT IDENTITY CONSTRAINT PK_room_block_history PRIMARY KEY,
  room_block_id BIGINT NOT NULL CONSTRAINT FK_rbh_block REFERENCES inv.room_block,
  room_id INT NOT NULL CONSTRAINT FK_rbh_room REFERENCES inv.room,
  event VARCHAR(10) NOT NULL CONSTRAINT CK_rbh_event CHECK (event IN ('Created','Edited','Released')),
  effective_on DATE NOT NULL,                 -- business date the event took effect
  from_night DATE NULL,
  to_night DATE NULL,
  reason NVARCHAR(200) NOT NULL,
  source VARCHAR(20) NOT NULL,
  source_ref BIGINT NULL,
  related_block_id BIGINT NULL,               -- Edited: the successor block
  actor_id INT NULL,
  event_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_rbh_room ON inv.room_block_history(room_id, effective_on, history_id);
CREATE INDEX IX_rbh_block ON inv.room_block_history(room_block_id, history_id);
GO
-- Backfill: one Created row per existing block, plus a Released row for blocks that were already released.
INSERT inv.room_block_history(room_block_id, room_id, event, effective_on, from_night, to_night, reason, source, source_ref, actor_id, event_utc)
SELECT room_block_id, room_id, 'Created', created_on, from_night, to_night, reason, source, source_ref, NULL, created_utc FROM inv.room_block;
INSERT inv.room_block_history(room_block_id, room_id, event, effective_on, from_night, to_night, reason, source, source_ref, actor_id, event_utc)
SELECT room_block_id, room_id, 'Released', released_on, from_night, to_night, reason, source, source_ref, NULL, released_utc FROM inv.room_block WHERE active = 0;
GO
-- Blocks that cover nights today: active rows, and released rows clipped to their release date (empty clips excluded).
-- to_night NULL = open-ended. Every reader of "what is blocked" uses this view instead of filtering active = 1.
CREATE VIEW inv.v_room_block_effective AS
SELECT b.room_block_id, b.room_id, b.from_night,
  CASE WHEN b.released_on IS NULL THEN b.to_night
       WHEN b.to_night IS NULL OR b.to_night > b.released_on THEN b.released_on
       ELSE b.to_night END AS to_night,
  b.reason, b.source, b.source_ref, b.active, b.created_on, b.released_on
FROM inv.room_block b
WHERE b.active = 1 OR (b.released_on IS NOT NULL AND (b.from_night IS NULL OR b.from_night < b.released_on));
GO
ALTER PROCEDURE inv.usp_apply_type_night_deltas
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
             AND NOT EXISTS (SELECT 1 FROM inv.v_room_block_effective b WHERE b.room_id = r.room_id
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
ALTER PROCEDURE inv.usp_rebuild_type_night_count
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
         AND NOT EXISTS (SELECT 1 FROM inv.v_room_block_effective b WHERE b.room_id = r.room_id
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
ALTER VIEW inv.v_type_night_count_drift AS
SELECT c.room_type_id, c.night, c.sellable, c.sold, c.held, x.sellable AS sellable_expected, x.sold AS sold_expected, x.held AS held_expected
FROM inv.type_night_count c
CROSS APPLY (SELECT
    (SELECT COUNT(*) FROM inv.room r WHERE r.room_type_id = c.room_type_id
       AND NOT EXISTS (SELECT 1 FROM inv.v_room_block_effective b WHERE b.room_id = r.room_id
                         AND (b.from_night IS NULL OR b.from_night <= c.night) AND (b.to_night IS NULL OR b.to_night > c.night))) AS sellable,
    (SELECT COUNT(*) FROM inv.room_night rn JOIN inv.room r ON r.room_id = rn.room_id WHERE r.room_type_id = c.room_type_id AND rn.night = c.night) AS sold,
    (SELECT ISNULL(SUM(gt.allotted - gt.picked), 0) FROM inv.group_block_type gt JOIN inv.group_block g ON g.group_block_id = gt.group_block_id
       WHERE gt.room_type_id = c.room_type_id AND g.status = 'Open' AND g.arrival <= c.night AND g.departure > c.night) AS held) x
WHERE c.sellable <> x.sellable OR c.sold <> x.sold OR c.held <> x.held;

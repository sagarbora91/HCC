-- HCC migration 0301 · W3-7 stays gaps (block 0300–0399, Stays). Room move, extend / shorten stay, early departure
-- (rules ported from the prototype MOVE handler, handlers/guests.ts) and stay-over cleaning tasks.
-- Adds one history table; no column is added to tables owned by other modules. Inventory is changed only through
-- IInventoryAllocator (P2 port); nightly price snapshots (inv.reservation_night) are rewritten for un-posted nights only.

-- One row per applied change of an in-house stay: who, when (business date), what moved, rate decision and the revenue given up.
CREATE TABLE gst.stay_change(
  change_id BIGINT IDENTITY CONSTRAINT PK_stay_change PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_stay_change_property REFERENCES inv.property,
  stay_id BIGINT NOT NULL CONSTRAINT FK_stay_change_stay REFERENCES gst.stay,
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_stay_change_kind CHECK (kind IN ('move','extend','shorten','early')),
  business_date DATE NOT NULL,
  from_room_id INT NULL CONSTRAINT FK_stay_change_from_room REFERENCES inv.room,
  to_room_id INT NULL CONSTRAINT FK_stay_change_to_room REFERENCES inv.room,
  old_departure DATE NOT NULL,
  new_departure DATE NOT NULL,
  rate_decision VARCHAR(6) NOT NULL CONSTRAINT CK_stay_change_rate_decision CHECK (rate_decision IN ('keep','change')),
  old_rate_paise BIGINT NULL,
  new_rate_paise BIGINT NULL,
  rate_loss_paise BIGINT NOT NULL CONSTRAINT DF_stay_change_loss DEFAULT 0 CONSTRAINT CK_stay_change_loss CHECK (rate_loss_paise >= 0),
  reason NVARCHAR(200) NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_stay_change_stay ON gst.stay_change(stay_id, change_id);

-- Stay-over tasks are idempotent per room and business date through ops.hk_task.causal_key (UX_hk_task_causal, 0300):
-- hk:stayover:<roomId>:<yyyymmdd>. Move cleans use hk:move:<stayId>:<segment seq>. This index serves the daily generator.
CREATE INDEX IX_hk_task_kind_date ON ops.hk_task(kind, business_date) INCLUDE (room_id);

-- HCC migration 4920 · AF2-CTL01 (consolidated audit CTL-01; AUDITFIX-PLAN §2 Q4 default (a)): a rate reduction during a stay is judged against the
-- stay's AGREED rate as well as the tariff (the stricter wins), with the Owner's threshold `rate.agreedReductionBps` (Master configuration › Rooms and
-- rate plans, default 15 %). Each applied change now records what it was judged against, so the owner digest can list the changes that went beyond
-- the threshold (who / what / how much) and the next change of the same stay is measured against the right agreed rate:
--   tariff_paise        the room type's tariff on the business date
--   agreed_rate_paise   the agreed rate before this change
--   agreed_after_paise  the agreed rate after it (an Owner-set or Owner-approved rate becomes the new agreed rate; a Manager's change does not)
--   threshold_bps       the threshold in force
--   over_threshold      1 = more than the threshold below the agreed rate (only the Owner can set or approve it)
-- Rows written before 4920 keep NULL / 0 (the agreed rate then falls back to the rate before the stay's first change). Append-only as before.
ALTER TABLE gst.stay_rate_change ADD
  tariff_paise BIGINT NULL CONSTRAINT CK_stay_rate_change_tariff CHECK (tariff_paise >= 0),
  agreed_rate_paise BIGINT NULL CONSTRAINT CK_stay_rate_change_agreed CHECK (agreed_rate_paise >= 0),
  agreed_after_paise BIGINT NULL CONSTRAINT CK_stay_rate_change_agreed_after CHECK (agreed_after_paise >= 0),
  threshold_bps INT NULL CONSTRAINT CK_stay_rate_change_threshold CHECK (threshold_bps BETWEEN 0 AND 10000),
  over_threshold BIT NOT NULL CONSTRAINT DF_stay_rate_change_over DEFAULT 0;
GO
CREATE INDEX IX_stay_rate_change_over ON gst.stay_rate_change(property_id, business_date) WHERE over_threshold = 1;

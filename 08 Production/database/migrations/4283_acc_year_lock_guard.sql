-- HCC migration 4283 · ACC-4 acc4-year · the year-lock guard view (ACC4-CONTRACT §1.2: after the lock every post / edit / void dated in the year
-- is refused ACC_YEAR_LOCKED). The application refuses (YearLockRules.GuardPost) inside the posting transaction; stores read this view. No
-- trigger: acc.manual_voucher is written with OUTPUT clauses, which SQL Server refuses on a table with enabled triggers.
GO
CREATE VIEW acc.v_locked_year AS
  SELECT property_id, fy, DATEFROMPARTS(CAST(LEFT(fy, 4) AS INT), 4, 1) AS from_date, DATEFROMPARTS(CAST(LEFT(fy, 4) AS INT) + 1, 3, 31) AS to_date
  FROM acc.fiscal_year WHERE status = 'locked';
GO

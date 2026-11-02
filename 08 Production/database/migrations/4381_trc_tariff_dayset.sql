-- HCC migration 4381 · F4-T1 (F4-CONTRACT §1.3): tariff schedule rows gain a night set (A all, W weekday, E weekend) and the card version that
-- wrote them; the primary key is extended with day_set. Existing rows become 'A' and behave exactly as before. inv.fn_tariff_on = the day-set
-- aware resolver (TariffCardRules.Resolve): latest row effective on or before the night whose set matches it (weekend days of the writing card
-- version, default Fri / Sat / Sun), a W / E row beating an A row of the same date.
ALTER TABLE inv.room_type_tariff ADD day_set CHAR(1) NOT NULL CONSTRAINT DF_room_type_tariff_day_set DEFAULT 'A'
  CONSTRAINT CK_room_type_tariff_day_set CHECK (day_set IN ('A','W','E'));
ALTER TABLE inv.room_type_tariff ADD card_version_id BIGINT NULL CONSTRAINT FK_room_type_tariff_card REFERENCES mst.tariff_card_version;
GO
ALTER TABLE inv.room_type_tariff DROP CONSTRAINT PK_room_type_tariff;
ALTER TABLE inv.room_type_tariff ADD CONSTRAINT PK_room_type_tariff PRIMARY KEY (room_type_id, effective_from, day_set);
GO
CREATE OR ALTER FUNCTION inv.fn_tariff_on(@t INT, @d DATE) RETURNS BIGINT
AS
BEGIN
  DECLARE @dow INT = DATEDIFF(DAY, CAST('19000107' AS DATE), @d) % 7; -- 7 Jan 1900 was a Sunday → 0 = Sun … 6 = Sat
  RETURN (SELECT TOP (1) x.rate_paise
    FROM inv.room_type_tariff x LEFT JOIN mst.tariff_card_version v ON v.version_id = x.card_version_id
    WHERE x.room_type_id = @t AND x.effective_from <= @d
      AND (x.day_set = 'A' OR x.day_set = CASE WHEN CHARINDEX(',' + CAST(@dow AS VARCHAR(1)) + ',', ',' + COALESCE(v.weekend_days, '5,6,0') + ',') > 0 THEN 'E' ELSE 'W' END)
    ORDER BY x.effective_from DESC, CASE x.day_set WHEN 'A' THEN 1 ELSE 0 END);
END;

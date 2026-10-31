-- HCC migration 4251 · ACC-2 flows: OTA payouts from the channel's statement (contract §4; rulebook A13, §4.14; DEC-150). Every OTA pays the
-- room amount less its commission (with 18 % GST) and less the GST TCS, with a statement. One row per statement; the bookings it settles in
-- ota.payout_booking. Accounting books OtaPayoutRecorded: Receipt Dr 1030/<bank> net · Dr 5070 commission (+ GST unless ITC is allowed) ·
-- Dr 1340 TCS · Cr 1115/<channel> gross. Money BIGINT paise. Append-only (a wrong statement is corrected in Accounting, never edited here).
--   ota.channel.itc_allowed  the GST on this channel's commission invoice may be taken as input tax credit (Owner setting; default off, §4.4)
CREATE TABLE ota.payout(
  payout_id BIGINT IDENTITY CONSTRAINT PK_ota_payout PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_ota_payout_property REFERENCES inv.property,
  channel_id INT NOT NULL CONSTRAINT FK_ota_payout_channel REFERENCES ota.channel,
  business_date DATE NOT NULL,
  statement_ref NVARCHAR(40) NOT NULL CONSTRAINT CK_ota_payout_ref CHECK (LEN(statement_ref) > 0),
  gross_paise BIGINT NOT NULL CONSTRAINT CK_ota_payout_gross CHECK (gross_paise BETWEEN 1 AND 100000000000),
  commission_paise BIGINT NOT NULL CONSTRAINT CK_ota_payout_commission CHECK (commission_paise >= 0),
  commission_gst_paise BIGINT NOT NULL CONSTRAINT CK_ota_payout_commission_gst CHECK (commission_gst_paise >= 0),
  tcs_paise BIGINT NOT NULL CONSTRAINT CK_ota_payout_tcs CHECK (tcs_paise >= 0),
  net_paise BIGINT NOT NULL CONSTRAINT CK_ota_payout_net CHECK (net_paise > 0),
  bank_account VARCHAR(20) NOT NULL,
  itc_allowed BIT NOT NULL,
  recorded_by INT NOT NULL CONSTRAINT FK_ota_payout_user REFERENCES plat.app_user,
  recorded_utc DATETIME2(3) NOT NULL,
  CONSTRAINT CK_ota_payout_sum CHECK (gross_paise = net_paise + commission_paise + commission_gst_paise + tcs_paise),
  CONSTRAINT UQ_ota_payout_statement UNIQUE (channel_id, statement_ref));
CREATE INDEX IX_ota_payout_property ON ota.payout(property_id, payout_id);
CREATE TABLE ota.payout_booking(
  payout_id BIGINT NOT NULL CONSTRAINT FK_ota_payout_booking_payout REFERENCES ota.payout,
  reservation_id BIGINT NOT NULL CONSTRAINT FK_ota_payout_booking_reservation REFERENCES inv.reservation,
  CONSTRAINT PK_ota_payout_booking PRIMARY KEY (payout_id, reservation_id));
GO
CREATE TRIGGER ota.TR_payout_immutable ON ota.payout INSTEAD OF UPDATE, DELETE AS
BEGIN
  THROW 52502, 'OTA payouts are append-only.', 1;
END;
GO
ALTER TABLE ota.channel ADD itc_allowed BIT NOT NULL CONSTRAINT DF_ota_channel_itc_allowed DEFAULT 0;
GO

-- HCC migration 4677 · LF B1 a regular guest's agreed rate (LF-CONTRACT §3). One row per guest and room type, set / cleared by the Owner
-- (PERM-GST-010). Sent to money roles only (GuestView.agreedRates, DEC-MA-11e). A booking that used it records rate_source 'agreed'.
CREATE TABLE gst.guest_agreed_rate(
  guest_id BIGINT NOT NULL CONSTRAINT FK_guest_agreed_rate_guest REFERENCES gst.guest,
  room_type_id INT NOT NULL CONSTRAINT FK_guest_agreed_rate_type REFERENCES inv.room_type,
  rate_paise BIGINT NOT NULL CONSTRAINT CK_guest_agreed_rate_rate CHECK (rate_paise > 0 AND rate_paise <= 10000000000),
  plan_code VARCHAR(20) NULL,
  valid_to DATE NULL,
  note NVARCHAR(200) NULL,
  set_by INT NOT NULL,
  set_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_guest_agreed_rate PRIMARY KEY (guest_id, room_type_id));
GO
ALTER TABLE inv.reservation ADD rate_source VARCHAR(8) NULL CONSTRAINT CK_reservation_rate_source CHECK (rate_source IS NULL OR rate_source IN ('tariff','agreed'));

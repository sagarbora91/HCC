-- HCC migration 4675 · LF A1 rate / guest-count change during a stay (02 Planning/agents/LF-CONTRACT.md §2; lane lf-money-rate).
-- One row per applied change (direct by the Owner / a Manager within PERM-RATE-001, or executed from an approved 'rate-change' request).
-- `reposted_json` = the re-posted nights [{reversal, line, night}] (a posted night of the business date is reversed and re-posted at the
-- new rate with the GST slab of that night's actual price, DEC-069). Rows are never updated or deleted (report "Rate change detail").
CREATE TABLE gst.stay_rate_change(
  change_id BIGINT IDENTITY CONSTRAINT PK_stay_rate_change PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_stay_rate_change_property REFERENCES inv.property,
  stay_id BIGINT NOT NULL CONSTRAINT FK_stay_rate_change_stay REFERENCES gst.stay,
  reservation_id BIGINT NOT NULL CONSTRAINT FK_stay_rate_change_reservation REFERENCES inv.reservation,
  room_id INT NOT NULL CONSTRAINT FK_stay_rate_change_room REFERENCES inv.room,
  business_date DATE NOT NULL,
  changed_utc DATETIME2(3) NOT NULL,
  changed_by INT NOT NULL,
  approval_id BIGINT NULL CONSTRAINT FK_stay_rate_change_approval REFERENCES bill.approval,
  approved_by INT NULL,
  scope VARCHAR(10) NOT NULL CONSTRAINT CK_stay_rate_change_scope CHECK (scope IN ('next','today-next','all')),
  reason NVARCHAR(200) NOT NULL,
  old_rate_paise BIGINT NOT NULL CONSTRAINT CK_stay_rate_change_old CHECK (old_rate_paise >= 0),
  new_rate_paise BIGINT NOT NULL CONSTRAINT CK_stay_rate_change_new CHECK (new_rate_paise >= 0),
  old_people TINYINT NOT NULL, new_people TINYINT NOT NULL CONSTRAINT CK_stay_rate_change_people CHECK (new_people BETWEEN 1 AND 4),
  old_children TINYINT NOT NULL, new_children TINYINT NOT NULL CONSTRAINT CK_stay_rate_change_children CHECK (new_children BETWEEN 0 AND 3),
  effective_from DATE NOT NULL,
  reposted_json NVARCHAR(2000) NOT NULL CONSTRAINT DF_stay_rate_change_reposted DEFAULT N'[]',
  CONSTRAINT CK_stay_rate_change_differs CHECK (old_rate_paise <> new_rate_paise OR old_people <> new_people OR old_children <> new_children),
  CONSTRAINT CK_stay_rate_change_approver CHECK ((approval_id IS NULL AND approved_by IS NULL) OR (approval_id IS NOT NULL AND approved_by IS NOT NULL)));
CREATE INDEX IX_stay_rate_change_stay ON gst.stay_rate_change(stay_id, changed_utc);
CREATE INDEX IX_stay_rate_change_date ON gst.stay_rate_change(property_id, business_date);
GO
CREATE TRIGGER gst.TR_stay_rate_change_append_only ON gst.stay_rate_change AFTER UPDATE, DELETE AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 51675, 'gst.stay_rate_change is append-only.', 1;
END;

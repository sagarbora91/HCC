-- HCC migration 4332 · F6 hall booking quotation versions (lane H2; spec §6–§8): the T&C snapshot each version keeps (copied from the template in
-- force when it is drafted, re-taken at Send, immutable afterwards), the placeholder flag, the Owner's discount approval (never the author), the
-- lost reason and the last change time. The override columns (override_by / reason / utc) are already on the version (4300).
CREATE TABLE hall.quotation_tnc_snapshot(
  version_id BIGINT NOT NULL CONSTRAINT FK_hall_quotation_tnc_snapshot_v REFERENCES hall.quotation_version,
  n INT NOT NULL,
  heading NVARCHAR(80) NOT NULL,
  body NVARCHAR(2000) NOT NULL,
  CONSTRAINT PK_hall_quotation_tnc_snapshot PRIMARY KEY (version_id, n));
ALTER TABLE hall.quotation_version ADD
  tnc_template_code VARCHAR(20) NULL,
  tnc_v INT NULL,
  tnc_has_placeholder BIT NOT NULL CONSTRAINT DF_hall_quotation_version_tncph DEFAULT 1,
  approval_by INT NULL,
  approval_utc DATETIME2(3) NULL,
  approval_bps INT NULL,
  lost_reason_id VARCHAR(20) NULL,
  lost_note NVARCHAR(300) NULL,
  updated_utc DATETIME2(3) NULL;
GO
ALTER TABLE hall.quotation_version ADD CONSTRAINT CK_hall_quotation_version_approval_sod CHECK (approval_by IS NULL OR approval_by <> created_by);
ALTER TABLE hall.quotation_version ADD CONSTRAINT CK_hall_quotation_version_lost CHECK (status <> 'LOST' OR lost_reason_id IS NOT NULL);
CREATE INDEX IX_hall_quotation_version_status ON hall.quotation_version(status, valid_until);

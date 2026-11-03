-- HCC migration 4394 · F4-T3: Owner follow-up calls for a complaint or lost-and-found item unresolved 7+ days (one open row per source + ref).
CREATE TABLE ops.feedback_followup(
  followup_id BIGINT IDENTITY CONSTRAINT PK_feedback_followup PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_feedback_followup_property REFERENCES inv.property,
  source VARCHAR(10) NOT NULL CONSTRAINT CK_feedback_followup_source CHECK (source IN ('complaint','lostfound')),
  ref_id BIGINT NOT NULL,
  since_date DATE NOT NULL,
  status VARCHAR(4) NOT NULL CONSTRAINT DF_feedback_followup_status DEFAULT 'open' CONSTRAINT CK_feedback_followup_status CHECK (status IN ('open','done')),
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_feedback_followup_created DEFAULT SYSUTCDATETIME(),
  done_utc DATETIME2(3) NULL, done_by INT NULL);
CREATE UNIQUE INDEX UX_feedback_followup_open ON ops.feedback_followup(property_id, source, ref_id) WHERE status = 'open';

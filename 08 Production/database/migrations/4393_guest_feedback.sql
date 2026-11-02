-- HCC migration 4393 · F4-T3 qw-feedback: guest feedback at check-out (1-5 stars + optional comment), one per stay. Never blocks check-out.
-- A low rating (or a categorised comment) opens a complaint (ops.guest_complaint); complaint_id links it.
CREATE TABLE ops.guest_feedback(
  feedback_id BIGINT IDENTITY CONSTRAINT PK_guest_feedback PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_guest_feedback_property REFERENCES inv.property,
  stay_id BIGINT NOT NULL CONSTRAINT FK_guest_feedback_stay REFERENCES gst.stay,
  stars TINYINT NOT NULL CONSTRAINT CK_guest_feedback_stars CHECK (stars BETWEEN 1 AND 5),
  comment NVARCHAR(400) NULL,
  complaint_id BIGINT NULL CONSTRAINT FK_guest_feedback_complaint REFERENCES ops.guest_complaint,
  recorded_utc DATETIME2(3) NOT NULL CONSTRAINT DF_guest_feedback_utc DEFAULT SYSUTCDATETIME(),
  recorded_by INT NOT NULL,
  CONSTRAINT UQ_guest_feedback_stay UNIQUE (stay_id));
CREATE INDEX IX_guest_feedback_recorded ON ops.guest_feedback(property_id, recorded_utc);

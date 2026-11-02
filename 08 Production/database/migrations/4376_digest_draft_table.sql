-- HCC migration 4376 · F4-T2 digest.draft table (see 4375).
CREATE TABLE digest.draft(
  property_id INT NOT NULL CONSTRAINT FK_digest_draft_property REFERENCES inv.property,
  business_date DATE NOT NULL,
  channel VARCHAR(8) NOT NULL,
  counts_only BIT NOT NULL,
  status VARCHAR(4) NOT NULL CONSTRAINT DF_digest_draft_status DEFAULT 'held',
  reason NVARCHAR(200) NOT NULL,
  body NVARCHAR(MAX) NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  created_by INT NOT NULL,
  CONSTRAINT PK_digest_draft PRIMARY KEY (property_id, business_date, channel),
  CONSTRAINT CK_digest_draft_channel CHECK (channel IN ('Email','WhatsApp')),
  CONSTRAINT CK_digest_draft_status CHECK (status = 'held'));

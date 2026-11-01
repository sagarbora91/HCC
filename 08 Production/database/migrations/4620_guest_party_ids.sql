-- HCC migration 4620 · DEC-193 (ACC-3 lane partyid, contract ACC3-REPORTS-CONTRACT §8): every guest on a registration has their own ID;
-- the photo is optional everywhere. Party members (gst.guest_party, 0300) gain the ID type, the display last 4 and whether an ID is required
-- (Adult / Senior = 1, Child / Infant = 0 — OP-21 default, one constant GuestRules.PartyIdRequiredBands). The full number is sealed exactly like
-- the registered guest's (0601): AES-256-GCM in the application, associated data HCC-GUESTID-v1|property|guest|seq|keyId; only the blob + key id
-- reach SQL. doc.id_capture gains party_seq (NULL = the registered guest). Existing rows: id_required follows the age band. No data is invented.
ALTER TABLE gst.guest_party ADD
  id_type NVARCHAR(40) NULL,
  id_last4 VARCHAR(4) NULL CONSTRAINT CK_guest_party_id_last4 CHECK (id_last4 IS NULL OR id_last4 NOT LIKE '%[^A-Za-z0-9]%'),
  id_required BIT NOT NULL CONSTRAINT DF_guest_party_id_required DEFAULT 1;
GO
UPDATE gst.guest_party SET id_required = CASE WHEN age_band IN ('Adult','Senior') THEN 1 ELSE 0 END;
GO
CREATE TABLE gst.guest_party_id_secret(
  guest_id BIGINT NOT NULL,
  seq TINYINT NOT NULL,
  key_id VARCHAR(40) NOT NULL CONSTRAINT CK_guest_party_id_secret_key CHECK (LEN(key_id) BETWEEN 1 AND 40 AND key_id NOT LIKE '%[^A-Za-z0-9._-]%'),
  sealed VARBINARY(64) NOT NULL CONSTRAINT CK_guest_party_id_secret_len CHECK (DATALENGTH(sealed) BETWEEN 33 AND 49),
  created_utc DATETIME2(3) NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_guest_party_id_secret PRIMARY KEY (guest_id, seq),
  CONSTRAINT FK_guest_party_id_secret FOREIGN KEY (guest_id, seq) REFERENCES gst.guest_party(guest_id, seq));
CREATE INDEX IX_guest_party_id_secret_key ON gst.guest_party_id_secret(key_id);
GO
ALTER TABLE doc.id_capture ADD party_seq TINYINT NULL CONSTRAINT CK_id_capture_party_seq CHECK (party_seq IS NULL OR party_seq BETWEEN 1 AND 6);

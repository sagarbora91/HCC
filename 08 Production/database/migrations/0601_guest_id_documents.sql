-- HCC migration 0601 · full guest ID numbers, encrypted at rest (Wave 1 agent C, block 0600–0699). DEC-068: the full ID number is
-- stored for every ID type, including Aadhaar (owner decision against advice — LEGAL RISK: UIDAI regulations expect hotels to keep
-- only a masked Aadhaar and storing full numbers can be an offence under the Aadhaar Act 2016; CA/lawyer to confirm before production).
-- The number is never stored in clear: the application encrypts it with AES-256-GCM (key from Windows DPAPI-protected configuration,
-- never in git or in the database) and stores only the sealed blob + the key id. gst.guest.id_last4 (0300) stays the display value.
-- Original HCC design; no donor code.
CREATE TABLE gst.guest_id_secret(
  guest_id BIGINT NOT NULL CONSTRAINT PK_guest_id_secret PRIMARY KEY CONSTRAINT FK_guest_id_secret_guest REFERENCES gst.guest,
  key_id VARCHAR(40) NOT NULL CONSTRAINT CK_guest_id_secret_key CHECK (LEN(key_id) BETWEEN 1 AND 40 AND key_id NOT LIKE '%[^A-Za-z0-9._-]%'),
  -- format byte (1) | nonce (12) | tag (16) | ciphertext (4–20 bytes: the normalised number, ASCII)
  sealed VARBINARY(64) NOT NULL CONSTRAINT CK_guest_id_secret_len CHECK (DATALENGTH(sealed) BETWEEN 33 AND 49),
  created_utc DATETIME2(3) NOT NULL,
  updated_utc DATETIME2(3) NOT NULL);
CREATE INDEX IX_guest_id_secret_key ON gst.guest_id_secret(key_id);

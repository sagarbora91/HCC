-- HCC migration 4900 · AF-CRM (PR-01 / AF-DEC-3): per-property secret for the opaque customer wire key (ck_ token = HMAC of the internal key).
-- The secret is created on first use by the server; it is never sent anywhere. Stored customer keys stay raw.
CREATE TABLE crm.customer_key_secret(
  property_id INT NOT NULL CONSTRAINT PK_crm_customer_key_secret PRIMARY KEY CONSTRAINT FK_crm_key_secret_property REFERENCES inv.property,
  secret VARBINARY(32) NOT NULL,
  created_utc DATETIME2(3) NOT NULL
);

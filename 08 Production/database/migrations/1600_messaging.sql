-- HCC migration 1600 · W2-10 messaging (block 1600–1699). Module 24 of the prototype (templates, consent, trigger rules, outbox).
-- Original HCC design; no donor code. ALL SENDING IS SIMULATED: nothing here talks to WhatsApp/SMS/e-mail providers; a gateway
-- port with a fake implementation decides the simulated outcome. Delivery status is DERIVED (queued_utc + boost_minutes vs. now),
-- exactly like the prototype, so no status column can drift. No guest ID number (full or last 4) is read or stored here (DEC-068).
-- Uses the 0002/0300 tables as they are (inv.property, inv.reservation, gst.guest); adds no column to another module's table.
CREATE SCHEMA msg;
GO
CREATE SEQUENCE msg.message_seq AS BIGINT START WITH 1 INCREMENT BY 1;
GO
-- Editable templates ({placeholders} are validated by the application against a whitelist). Seeded lazily per property.
CREATE TABLE msg.template(
  property_id INT NOT NULL CONSTRAINT FK_msg_template_property REFERENCES inv.property,
  code VARCHAR(30) NOT NULL,
  name NVARCHAR(80) NOT NULL,
  subject NVARCHAR(120) NOT NULL,
  body NVARCHAR(600) NOT NULL,
  channel VARCHAR(8) NOT NULL CONSTRAINT CK_msg_template_channel CHECK (channel IN ('WhatsApp','SMS','Email')),
  updated_utc DATETIME2(3) NULL,
  updated_by INT NULL,
  row_ver ROWVERSION,
  CONSTRAINT PK_msg_template PRIMARY KEY (property_id, code));
-- Trigger rules: outbox/business event -> template + channel.
CREATE TABLE msg.trigger_rule(
  property_id INT NOT NULL CONSTRAINT FK_msg_rule_property REFERENCES inv.property,
  rule_id VARCHAR(8) NOT NULL,
  event VARCHAR(20) NOT NULL CONSTRAINT CK_msg_rule_event CHECK (event IN ('booking-confirmed','pre-arrival','checked-in','payment-received','checked-out')),
  template_code VARCHAR(30) NOT NULL,
  channel VARCHAR(8) NOT NULL CONSTRAINT CK_msg_rule_channel CHECK (channel IN ('WhatsApp','SMS','Email')),
  enabled BIT NOT NULL,
  updated_utc DATETIME2(3) NULL,
  updated_by INT NULL,
  row_ver ROWVERSION,
  CONSTRAINT PK_msg_rule PRIMARY KEY (property_id, rule_id),
  CONSTRAINT FK_msg_rule_template FOREIGN KEY (property_id, template_code) REFERENCES msg.template(property_id, code));
-- Consent per guest profile. No row = all channels allowed, not opted out (transactional consent captured at booking).
CREATE TABLE msg.consent(
  property_id INT NOT NULL CONSTRAINT FK_msg_consent_property REFERENCES inv.property,
  guest_id BIGINT NOT NULL CONSTRAINT FK_msg_consent_guest REFERENCES gst.guest,
  whatsapp BIT NOT NULL,
  sms BIT NOT NULL,
  email BIT NOT NULL,
  opt_out BIT NOT NULL,
  updated_utc DATETIME2(3) NOT NULL,
  updated_by INT NOT NULL,
  row_ver ROWVERSION,
  CONSTRAINT PK_msg_consent PRIMARY KEY (property_id, guest_id));
-- The outbox of guest messages. recipient_masked only (••••••1234 / a•••@example.test): the full contact is read from the
-- guest profile when a real gateway needs it. outcome is predetermined at queue time by the gateway port (simulation).
CREATE TABLE msg.message(
  message_id BIGINT NOT NULL CONSTRAINT PK_msg_message PRIMARY KEY CONSTRAINT DF_msg_message_id DEFAULT (NEXT VALUE FOR msg.message_seq),
  property_id INT NOT NULL CONSTRAINT FK_msg_message_property REFERENCES inv.property,
  reservation_id BIGINT NOT NULL CONSTRAINT FK_msg_message_reservation REFERENCES inv.reservation,
  guest_id BIGINT NULL CONSTRAINT FK_msg_message_guest REFERENCES gst.guest,
  template_code VARCHAR(30) NULL,                    -- NULL = manual message
  channel VARCHAR(8) NOT NULL CONSTRAINT CK_msg_message_channel CHECK (channel IN ('WhatsApp','SMS','Email')),
  recipient_masked NVARCHAR(120) NOT NULL,
  subject NVARCHAR(200) NULL,
  body NVARCHAR(1000) NOT NULL,
  queued_utc DATETIME2(3) NOT NULL,
  outcome VARCHAR(9) NOT NULL CONSTRAINT CK_msg_message_outcome CHECK (outcome IN ('Read','Delivered','Failed')),
  boost_minutes INT NOT NULL CONSTRAINT DF_msg_message_boost DEFAULT 0,
  blocked_reason NVARCHAR(200) NULL,                 -- set = never sent (consent / opt-out / no recipient)
  rule_id VARCHAR(8) NULL,
  trigger_key VARCHAR(60) NULL,                      -- e.g. R1:res:42 — one message per trigger event (idempotent consumption)
  retry_of BIGINT NULL CONSTRAINT FK_msg_message_retry_of REFERENCES msg.message,
  retried_by BIGINT NULL,
  actor INT NULL,                                    -- principal; NULL = the trigger engine (system)
  created_utc DATETIME2(3) NOT NULL,
  row_ver ROWVERSION);
CREATE UNIQUE INDEX UX_msg_message_trigger ON msg.message(property_id, trigger_key) WHERE trigger_key IS NOT NULL;
CREATE UNIQUE INDEX UX_msg_message_retry_of ON msg.message(retry_of) WHERE retry_of IS NOT NULL;
CREATE INDEX IX_msg_message_list ON msg.message(property_id, message_id DESC);
CREATE INDEX IX_msg_message_guest ON msg.message(guest_id, message_id) WHERE guest_id IS NOT NULL;
CREATE INDEX IX_msg_message_reservation ON msg.message(reservation_id);

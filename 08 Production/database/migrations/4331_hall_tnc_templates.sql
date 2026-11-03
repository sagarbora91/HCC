-- HCC migration 4331 · F6 hall booking T&C templates (F6-CONTRACT §6, lane H2; spec §7): one template per event type (GENERAL where none),
-- versions with an effective date, numbered clauses. Only the Owner saves (PERM-HAL-005); a save adds a version and never changes an older
-- one, so the snapshots kept by sent quotations (4332) stay as sent. Nothing is deleted.
CREATE TABLE hall.tnc_template(
  template_id INT IDENTITY CONSTRAINT PK_hall_tnc_template PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_hall_tnc_template_property REFERENCES inv.property,
  code VARCHAR(20) NOT NULL,
  event_type VARCHAR(12) NOT NULL CONSTRAINT CK_hall_tnc_template_type CHECK (event_type IN ('GENERAL','WEDDING','RECEPTION','BIRTHDAY','ENGAGEMENT','CORPORATE','CONFERENCE','RELIGIOUS','OTHER')),
  CONSTRAINT UX_hall_tnc_template_code UNIQUE (property_id, code),
  CONSTRAINT UX_hall_tnc_template_type UNIQUE (property_id, event_type));
CREATE TABLE hall.tnc_template_version(
  template_id INT NOT NULL CONSTRAINT FK_hall_tnc_template_version_t REFERENCES hall.tnc_template,
  v INT NOT NULL CONSTRAINT CK_hall_tnc_template_version_v CHECK (v >= 1),
  effective_from DATE NOT NULL,
  created_by INT NOT NULL,
  created_utc DATETIME2(3) NOT NULL,
  CONSTRAINT PK_hall_tnc_template_version PRIMARY KEY (template_id, v));
CREATE TABLE hall.tnc_clause(
  template_id INT NOT NULL,
  v INT NOT NULL,
  n INT NOT NULL CONSTRAINT CK_hall_tnc_clause_n CHECK (n BETWEEN 1 AND 40),
  heading NVARCHAR(80) NOT NULL,
  body NVARCHAR(2000) NOT NULL,
  CONSTRAINT PK_hall_tnc_clause PRIMARY KEY (template_id, v, n),
  CONSTRAINT FK_hall_tnc_clause_version FOREIGN KEY (template_id, v) REFERENCES hall.tnc_template_version(template_id, v));

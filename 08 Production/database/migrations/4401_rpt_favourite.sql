-- HCC migration 4401 · F7-R1 report favourites per principal (POST /reports/favourites/{id} toggles a row).
CREATE TABLE rpt.favourite(
  property_id INT NOT NULL CONSTRAINT FK_rpt_favourite_property REFERENCES inv.property,
  principal_id INT NOT NULL,
  report_id VARCHAR(80) NOT NULL,
  created_utc DATETIME2(3) NOT NULL CONSTRAINT DF_rpt_favourite_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT PK_rpt_favourite PRIMARY KEY (property_id, principal_id, report_id));

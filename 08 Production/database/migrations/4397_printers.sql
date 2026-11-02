-- HCC migration 4397 · F4-T3 qw-alerts: printers with a last test page (System health "Printers" block, Print test page). 4398-4399 reserved.
CREATE TABLE plat.printer(
  printer_id INT IDENTITY CONSTRAINT PK_printer PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_printer_property REFERENCES inv.property,
  name NVARCHAR(80) NOT NULL,
  kind VARCHAR(8) NOT NULL CONSTRAINT CK_printer_kind CHECK (kind IN ('A4','thermal')),
  online BIT NOT NULL CONSTRAINT DF_printer_online DEFAULT 1,
  last_test_utc DATETIME2(3) NULL,
  last_test_ok BIT NULL,
  CONSTRAINT UQ_printer_name UNIQUE (property_id, name));

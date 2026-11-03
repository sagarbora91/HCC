-- HCC migration 4334 · F6 hall booking lost reasons (lane H2; spec §5, §6.3): the list an enquiry / quotation is closed with ("Other" needs a note).
-- While a property has no rows the commands use the built-in list (HallQuoteRules.DefaultLostReasons = the seed in 4349).
CREATE TABLE hall.lost_reason(
  property_id INT NOT NULL CONSTRAINT FK_hall_lost_reason_property REFERENCES inv.property,
  reason_id VARCHAR(20) NOT NULL,
  label NVARCHAR(60) NOT NULL,
  needs_note BIT NOT NULL CONSTRAINT DF_hall_lost_reason_note DEFAULT 0,
  sort_order INT NOT NULL,
  active BIT NOT NULL CONSTRAINT DF_hall_lost_reason_active DEFAULT 1,
  CONSTRAINT PK_hall_lost_reason PRIMARY KEY (property_id, reason_id));

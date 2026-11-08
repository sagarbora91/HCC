-- HCC migration 4351 · F8 split folio routing (DEC-152, SF-CONTRACT §2.3 / §3): which folio a new posting of a category lands on.
-- Append-only: setting the routing again writes a new seq; the effective route is the highest seq per (reservation, category);
-- no row = folio_seq 1. Routing applies to postings made after it is set (posted charges never move between folios).
-- Original HCC design; event times are written by the application from its TimeProvider.

CREATE TABLE bill.folio_route(
  route_id BIGINT IDENTITY CONSTRAINT PK_folio_route PRIMARY KEY,
  reservation_id BIGINT NOT NULL CONSTRAINT FK_folio_route_reservation REFERENCES inv.reservation,
  category VARCHAR(8) NOT NULL CONSTRAINT CK_folio_route_category CHECK (category IN ('room','extras')),
  folio_id BIGINT NOT NULL CONSTRAINT FK_folio_route_folio REFERENCES bill.folio,
  seq INT NOT NULL CONSTRAINT CK_folio_route_seq CHECK (seq >= 1),
  reason NVARCHAR(200) NULL,
  changed_by INT NOT NULL,
  changed_utc DATETIME2(3) NOT NULL,
  CONSTRAINT UQ_folio_route UNIQUE (reservation_id, category, seq));
GO
CREATE TRIGGER bill.TR_folio_route_append_only ON bill.folio_route AFTER UPDATE, DELETE
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM deleted) THROW 52350, 'bill.folio_route is append-only; set the routing again.', 1;
END;
GO

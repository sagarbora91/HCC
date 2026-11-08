-- HCC migration 4350 · F8 split folio (DEC-152, SF-CONTRACT §2.3): up to four folios per reservation, each with its payer.
-- folio_seq 1 is the primary (guest) folio; 2..4 are the split folios B..D. Every existing row becomes folio_seq = 1,
-- payer_kind = 'guest' by the defaults, so every existing stay is a single-folio stay (invariant I1).
-- Original HCC design; event times are written by the application from its TimeProvider.

ALTER TABLE bill.folio DROP CONSTRAINT UQ_folio_reservation;
GO
ALTER TABLE bill.folio ADD
  folio_seq TINYINT NOT NULL CONSTRAINT DF_folio_seq DEFAULT 1 CONSTRAINT CK_folio_seq CHECK (folio_seq BETWEEN 1 AND 4),
  payer_kind VARCHAR(8) NOT NULL CONSTRAINT DF_folio_payer_kind DEFAULT 'guest'
    CONSTRAINT CK_folio_payer_kind CHECK (payer_kind IN ('guest','company','group','udhaar')),
  payer_company_id BIGINT NULL CONSTRAINT FK_folio_payer_company REFERENCES grp.company_account,
  payer_customer_key NVARCHAR(80) NULL,
  payer_name NVARCHAR(200) NULL,
  payer_gstin CHAR(15) NULL,
  opened_by INT NULL,
  opened_utc DATETIME2(3) NULL,
  removed_utc DATETIME2(3) NULL;
GO
CREATE UNIQUE INDEX UX_folio_reservation_seq ON bill.folio(reservation_id, folio_seq);
ALTER TABLE bill.folio ADD
  CONSTRAINT CK_folio_primary_guest CHECK (folio_seq > 1 OR payer_kind = 'guest'),
  CONSTRAINT CK_folio_company CHECK (payer_kind <> 'company' OR payer_company_id IS NOT NULL),
  CONSTRAINT CK_folio_udhaar CHECK (payer_kind <> 'udhaar' OR payer_customer_key IS NOT NULL),
  CONSTRAINT CK_folio_payer_gstin CHECK (payer_gstin IS NULL OR LEN(payer_gstin) = 15);
-- One live company folio per reservation.
CREATE UNIQUE INDEX UX_folio_one_company ON bill.folio(reservation_id) WHERE payer_kind = 'company' AND removed_utc IS NULL;
GO

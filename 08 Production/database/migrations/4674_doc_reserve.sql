-- HCC migration 4674 · LF-docdb fallout (reserved by the contract). Records the daily dangling-reference count that System health shows
-- (contract §1.7). One row per kind, replaced by the daily check.
CREATE TABLE plat.doc_ref_check(
  kind VARCHAR(20) NOT NULL CONSTRAINT PK_doc_ref_check PRIMARY KEY,
  checked_utc DATETIME2(3) NOT NULL,
  dangling_refs INT NOT NULL,
  orphan_objects INT NOT NULL);

-- HCC migration 4707 · LF-images (LF-CONTRACT 10.1, B5; owner ruling DEC-201): a guest / party-member PHOTO is a capture of its own kind, sealed like an ID image
-- (kind GuestPhoto in HccDocuments, PERM-DOC-004 to view, counted in the retention purge like captures). doc.id_capture.party_seq already exists
-- (4620, DEC-193: NULL = the registered guest, 1-6 = gst.guest_party.seq); this adds the doc_kind 'photo' and an index for the per-member cards.
ALTER TABLE doc.id_capture DROP CONSTRAINT CK_id_capture_kind;
GO
ALTER TABLE doc.id_capture ADD CONSTRAINT CK_id_capture_kind CHECK (doc_kind IN ('aadhaar','passport','dl','voter','pan','other','legacy','photo'));
GO
CREATE INDEX IX_id_capture_party ON doc.id_capture(property_id, guest_id, party_seq, doc_kind, capture_id);

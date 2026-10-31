-- HCC migration 4605 · W1a LG (lane lg-import, LG-HISTORY-CONTRACT §5 rule 1, §11): the legacy importer copies the old program's guest photo and
-- ID-proof images into the documents capture store as kind 'legacy' (sealed with the guest-ID key like every capture). doc_kind gains 'legacy';
-- the documents module shows such a capture as 'Other' (DocumentRules.Label); ocr_doc_kind keeps the real ID kind. No data change.
ALTER TABLE doc.id_capture DROP CONSTRAINT CK_id_capture_kind;
ALTER TABLE doc.id_capture ADD CONSTRAINT CK_id_capture_kind CHECK (doc_kind IN ('aadhaar','passport','dl','voter','pan','other','legacy'));

-- HCC migration 4690 · LF A1 (LF-CONTRACT §2): the meta of a pending 'rate-change' approval (bill.approval has no meta column):
-- {rate, people, children, scope, oldRate, oldPeople, oldChildren}. Written with the request; the approval executes it.
CREATE TABLE gst.stay_rate_request(
  approval_id BIGINT NOT NULL CONSTRAINT PK_stay_rate_request PRIMARY KEY CONSTRAINT FK_stay_rate_request_approval REFERENCES bill.approval,
  stay_id BIGINT NOT NULL CONSTRAINT FK_stay_rate_request_stay REFERENCES gst.stay,
  scope VARCHAR(10) NOT NULL CONSTRAINT CK_stay_rate_request_scope CHECK (scope IN ('next','today-next','all')),
  new_rate_paise BIGINT NOT NULL CONSTRAINT CK_stay_rate_request_rate CHECK (new_rate_paise >= 0),
  new_people TINYINT NOT NULL CONSTRAINT CK_stay_rate_request_people CHECK (new_people BETWEEN 1 AND 4),
  new_children TINYINT NOT NULL CONSTRAINT CK_stay_rate_request_children CHECK (new_children BETWEEN 0 AND 3),
  old_rate_paise BIGINT NOT NULL, old_people TINYINT NOT NULL, old_children TINYINT NOT NULL);

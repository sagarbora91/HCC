-- HCC migration 4679 · LF-money approval actions (LF-CONTRACT §0, §2, §4, §6, §7). Folio-bound: 'rate-change' (A1), 'bill-correction' (A2,
-- Owner decides), 'guest-credit' (B2, Manager within PERM-PAY-005, Owner beyond). 'counter-credit' (A7 void of a counter bill, Owner decides)
-- belongs to neither a folio nor a shift: target_kind 'counter', target_id = ctr.bill.bill_id.
ALTER TABLE bill.approval DROP CONSTRAINT CK_approval_action;
ALTER TABLE bill.approval DROP CONSTRAINT CK_approval_target_kind;
ALTER TABLE bill.approval DROP CONSTRAINT CK_approval_scope;
GO
ALTER TABLE bill.approval ADD CONSTRAINT CK_approval_action
  CHECK (action IN ('discount','refund','credit','correction','cash-difference','cash-banking','till-transfer','rate-change','bill-correction','guest-credit','counter-credit'));
ALTER TABLE bill.approval ADD CONSTRAINT CK_approval_target_kind CHECK (target_kind IN ('folio','receipt','charge','shift','banking','transfer','counter'));
ALTER TABLE bill.approval ADD CONSTRAINT CK_approval_scope
  CHECK ((action IN ('cash-difference','cash-banking','till-transfer') AND shift_id IS NOT NULL AND folio_id IS NULL)
      OR (action = 'counter-credit' AND target_kind = 'counter' AND folio_id IS NULL AND shift_id IS NULL)
      OR (action NOT IN ('cash-difference','cash-banking','till-transfer','counter-credit') AND folio_id IS NOT NULL AND shift_id IS NULL));

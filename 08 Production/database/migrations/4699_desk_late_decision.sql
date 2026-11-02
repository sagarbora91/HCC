-- HCC migration 4699 · LF A6 late check-out decision (LF-CONTRACT §8.4). gst.stay.late_checkout_decision = how a stay past its due-out time + grace was settled:
-- 'half-day' (the charge was posted), 'waived' (an approver waived it, audited with the reason). NULL = no decision yet. CheckoutStay refuses a late departure
-- without a decision or a LateCharge on the request.
ALTER TABLE gst.stay ADD late_checkout_decision VARCHAR(10) NULL CONSTRAINT CK_stay_late_decision CHECK (late_checkout_decision IN ('half-day','waived'));

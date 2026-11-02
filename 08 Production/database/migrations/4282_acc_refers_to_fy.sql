-- HCC migration 4282 · ACC-4 acc4-year · corrections after a year lock (rulebook G4) and the opening source (ACC4-CONTRACT §1.2).
-- A correction of a locked year is a journal dated in the new year that names the year it corrects (refers_to_fy). The opening entry records
-- where it came from: 'hand' (DEC-139 / DEC-148, FY 2026-27) or 'carry-forward' (approved at the Owner's year lock, so no Manager entry).
ALTER TABLE acc.manual_voucher ADD refers_to_fy CHAR(7) NULL
  CONSTRAINT CK_acc_manual_voucher_refers_to_fy CHECK (refers_to_fy IS NULL OR refers_to_fy LIKE '[0-9][0-9][0-9][0-9]-[0-9][0-9]');
ALTER TABLE acc.opening_entry ADD source VARCHAR(13) NOT NULL CONSTRAINT DF_acc_opening_entry_source DEFAULT ('hand')
  CONSTRAINT CK_acc_opening_entry_source CHECK (source IN ('hand','carry-forward'));
ALTER TABLE acc.opening_entry ADD carry_forward_id VARCHAR(48) NULL;

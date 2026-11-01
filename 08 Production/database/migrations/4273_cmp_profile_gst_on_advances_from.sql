-- HCC migration 4273 · ACC-3 acc3-carry C10 (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §7; rulebook A6; DEC-142). The GST-on-advances switch
-- is dated: with the switch on, a deposit received on or after gst_on_advances_from books the advance GST (NULL = every deposit, as before).
-- SetGstOnAdvances {on, from} sets it (default the business date) and clears it when the switch goes off.
ALTER TABLE cmp.profile ADD gst_on_advances_from DATE NULL;
GO

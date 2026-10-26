-- HCC migration 2702 · W4 fix F3 (D6): the import module also takes the POS outlets and the POS menu (block 2700-2799).
-- imp.batch.master is checked against the list of masters: add 'pos-outlets' and 'pos-menu'. No other change.
ALTER TABLE imp.batch DROP CONSTRAINT CK_imp_batch_master;
GO
ALTER TABLE imp.batch ADD CONSTRAINT CK_imp_batch_master CHECK (master IN
  ('room-types','rooms','tariffs','seasons','staff','ledgers','opening-balances','till-cash','guests','pos-outlets','pos-menu'));
GO

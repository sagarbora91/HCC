-- HCC migration 2800 · Fix wave A (A09-09): the import module also takes departments, company accounts, suppliers and stock items.
-- imp.batch.master is checked against the list of masters: add 'departments', 'companies', 'suppliers' and 'stock-items'. No other change;
-- committed rows go into the owning modules' tables (hr.department, grp.company_account, mnt.vendor, stk.item) through their stores.
ALTER TABLE imp.batch DROP CONSTRAINT CK_imp_batch_master;
GO
ALTER TABLE imp.batch ADD CONSTRAINT CK_imp_batch_master CHECK (master IN
  ('room-types','rooms','tariffs','seasons','staff','ledgers','opening-balances','till-cash','guests','pos-outlets','pos-menu',
   'departments','companies','suppliers','stock-items'));
GO

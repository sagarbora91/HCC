-- HCC migration 2880 · MA-8 import/export for every master (block 2880-2889). The import module takes the list masters of the masters wave:
-- floors, amenities, occupancy charges, meal plans, extras, discount reasons, payment methods, bank accounts, expense categories and vendors,
-- POS menu categories and tables, booking sources, ID types, number series, policies, designations, shift patterns, leave types, holidays,
-- housekeeping task types and zones, complaint and lost-and-found categories. imp.batch.master (the wire name of the file) is widened
-- (lost-found-categories is 21 characters) and its list of masters extended; committed rows go to the owning modules' tables through the
-- list writer of the import module (no new tables). Code: Domain/Import/ImportLists.cs, Infrastructure/Import/SqlImportLists.cs.
ALTER TABLE imp.batch DROP CONSTRAINT CK_imp_batch_master;
GO
ALTER TABLE imp.batch ALTER COLUMN master VARCHAR(30) NOT NULL;
GO
ALTER TABLE imp.batch ADD CONSTRAINT CK_imp_batch_master CHECK (master IN
  ('room-types','rooms','tariffs','seasons','staff','ledgers','opening-balances','till-cash','guests','pos-outlets','pos-menu',
   'departments','companies','suppliers','stock-items',
   'floors','amenities','occupancy-charges','meal-plans','extras','discount-reasons','payment-methods','bank-accounts','expense-categories','expense-vendors',
   'menu-categories','pos-tables','booking-sources','id-types','number-series','policies','designations','shift-patterns','leave-types','holidays',
   'hk-task-types','hk-zones','complaint-categories','lost-found-categories'));
GO

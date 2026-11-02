-- HCC migration 4700 · LF A3 / A4 import of the purposes of visit and vehicle types (templates purposes.csv, vehicle-types.csv). imp.batch.master is checked against the
-- wire names of the files; the two new lists are added to the list of 2880. Committed rows go to mst.purpose_of_visit / mst.vehicle_type through the front-desk masters writer.
ALTER TABLE imp.batch DROP CONSTRAINT CK_imp_batch_master;
GO
ALTER TABLE imp.batch ADD CONSTRAINT CK_imp_batch_master CHECK (master IN
  ('room-types','rooms','tariffs','seasons','staff','ledgers','opening-balances','till-cash','guests','pos-outlets','pos-menu',
   'departments','companies','suppliers','stock-items',
   'floors','amenities','occupancy-charges','meal-plans','extras','discount-reasons','payment-methods','bank-accounts','expense-categories','expense-vendors',
   'menu-categories','pos-tables','booking-sources','id-types','number-series','policies','designations','shift-patterns','leave-types','holidays',
   'hk-task-types','hk-zones','complaint-categories','lost-found-categories','purposes','vehicle-types'));
GO

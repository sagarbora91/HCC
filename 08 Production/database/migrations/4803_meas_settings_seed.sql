-- HCC migration 4803 · F12 MEAS measurement settings seed (MEAS-CONTRACT §1.2, §7 M-2/M-5/M-6/M-7/M-9; lane meas-settings).
-- The meas.* / yld.overbook.* / yld.restrictionsEnforce / ffe.reservePct settings are registered in code (Hcc.Application
-- MeasurementCatalogue, group "measure", Owner only) with their defaults: every pack and item ON, targets per M-9, blockClose / regrets
-- mandatory / restrictions enforce OFF, FF&E reserve 3, overbooking 0. The settings hub stores only changes, so this seed writes no value rows;
-- it refuses to run when a stored change already switches a core item off (meas.daily.flash, meas.daily.inventory are always on, contract §1.3).
IF EXISTS (SELECT 1 FROM cfg.setting_change WHERE setting_key IN ('meas.daily.flash', 'meas.daily.inventory') AND withdrawn_utc IS NULL AND value_json = N'false')
  THROW 51803, 'A core measurement item is stored OFF; withdraw that change first (MEAS-CONTRACT 1.3: core items are always on).', 1;

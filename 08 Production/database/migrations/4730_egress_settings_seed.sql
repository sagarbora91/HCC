-- HCC migration 4730 · F5-E1 internet egress gate (02 Planning/agents/EGRESS-CONTRACT.md §1.1, owner default A28: master switch OFF at go-live).
-- The net.* settings (net.internetEnabled, net.messaging, net.ota, net.ocr, net.payments, net.email, net.updateCheck, net.window) are
-- registered in code (Hcc.Api EgressSettings provider, group "internet" = Master configuration › Internet, Owner only) with default OFF;
-- the settings hub stores only changes. This seed therefore writes no value rows: it guarantees that no stored change turns the internet on
-- at install (fail closed) by refusing to run when such a row already exists.
IF EXISTS (SELECT 1 FROM cfg.setting_change WHERE setting_key LIKE 'net.%' AND withdrawn_utc IS NULL AND value_json = N'true')
  THROW 51730, 'A net.* setting is already ON before the egress gate is installed; withdraw it first (EGRESS-CONTRACT 1.1: default OFF).', 1;

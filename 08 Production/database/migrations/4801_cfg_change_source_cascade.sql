-- HCC migration 4801 · ADM-int: a sub-function switched off because a feature it requires went off (DEC-214 dependants) is recorded with
-- source 'cascade'. 0700 allowed only change / revert / import / approval, so the first dependant write failed the CHECK.
ALTER TABLE cfg.setting_change DROP CONSTRAINT CK_cfg_change_source;
ALTER TABLE cfg.setting_change ADD CONSTRAINT CK_cfg_change_source CHECK (source IN ('change', 'revert', 'import', 'approval', 'cascade'));

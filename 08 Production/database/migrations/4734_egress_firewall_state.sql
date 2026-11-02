-- HCC migration 4734 · F5-E1 Windows firewall rule state (EGRESS-CONTRACT §4). One row: the state the API wants (wanted_state, written when the
-- master switch changes) and what the \HCC\ToggleEgress task last applied (deployment/windows/Set-HccEgress.ps1 -State On|Off writes
-- applied_state / applied_utc / detail). No row applied = System health "firewall rule not applied"; the software gate holds regardless.
CREATE TABLE ops.egress_firewall_state(
  id TINYINT NOT NULL CONSTRAINT PK_egress_firewall_state PRIMARY KEY CONSTRAINT CK_egress_firewall_state_one CHECK (id = 1),
  wanted_state VARCHAR(3) NOT NULL CONSTRAINT CK_egress_firewall_wanted CHECK (wanted_state IN ('On', 'Off')),
  wanted_utc DATETIME2(3) NOT NULL,
  applied_state VARCHAR(3) NULL CONSTRAINT CK_egress_firewall_applied CHECK (applied_state IN ('On', 'Off')),
  applied_utc DATETIME2(3) NULL,
  detail NVARCHAR(300) NULL);
-- Off = internet blocked (the rule "HCC Egress Block" enabled), the install default.
INSERT ops.egress_firewall_state(id, wanted_state, wanted_utc) VALUES (1, 'Off', SYSUTCDATETIME());

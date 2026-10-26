-- HCC migration 3910 · A06-01 (security review F-M03 residual) + A02b-05: "must change at next sign-in".
-- An administrator who sets or resets someone's password or PIN (SetCredential) knows that secret, so the credential is marked
-- must_change = 1. A session opened with it may only read its own capabilities, change the credential (ChangeOwnCredential) and
-- sign out; every other request answers 403 FORBIDDEN, details.reason MUST_CHANGE_CREDENTIAL. The user's own change (and the first
-- Owner's bootstrap password, the setup wizard's password) store must_change = 0. Existing credentials keep 0 (no forced change).
ALTER TABLE plat.credential ADD must_change BIT NOT NULL CONSTRAINT DF_credential_must_change DEFAULT 0;

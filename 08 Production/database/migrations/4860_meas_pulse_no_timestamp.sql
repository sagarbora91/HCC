-- HCC migration 4860 · F12 MEAS f12-int (review H4): the anonymous staff pulse keeps no time finer than its quarter.
-- meas.pulse had created_utc DATETIME2(3): a millisecond time could be matched to a signed-in user's request, so it is dropped. The row keeps
-- quarter_key (the only time the reports need) and a random client_id. The report floor is 5 answers per department and quarter (contract,
-- PeopleRules.PulseMinAnswers); the "three answers" note in 4826 is superseded by this line.
IF COL_LENGTH('meas.pulse', 'created_utc') IS NOT NULL
BEGIN
  IF OBJECT_ID('meas.DF_meas_pulse_created', 'D') IS NOT NULL ALTER TABLE meas.pulse DROP CONSTRAINT DF_meas_pulse_created;
  ALTER TABLE meas.pulse DROP COLUMN created_utc;
END

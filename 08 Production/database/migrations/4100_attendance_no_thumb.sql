-- HCC migration 4100 · SCC "No Thumb" attendance mark (DEC-115, owner 27 Oct 2026: "import the entire payroll rules from SCC").
-- NT = present without a thumb punch: a present day that costs ½ day in SCC calcAttendance (noThumb × 0.5 in the total deduction).
-- Only the status list of hr.attendance changes; existing rows are untouched.
ALTER TABLE hr.attendance DROP CONSTRAINT CK_attendance_status;
ALTER TABLE hr.attendance ADD CONSTRAINT CK_attendance_status CHECK (status IN ('P','L','H','A','OFF','NT'));

# W05P-1 Payroll switched off, but Payroll / Advances / Full and final / Statutory rates tabs stay visible
Severity: S3
Steps: Owner (desktop or phone) -> Admin & Setup -> Master configuration -> Modules on/off -> Payroll "Switch off". Open People & Payroll -> Staff & payroll.
Expected (DEC-057, brief): switching Payroll off hides its tiles, menu, search entries and phone tabs; commands refused.
Actual: commands ARE refused (Unlock shows "Not permitted: Payroll is switched off in Master configuration > Modules on/off", toast shown twice). But the four payroll tabs (Payroll, Advances, Full and final, Statutory rates) still show with the locked September run, register/bank/statutory download buttons and payslips; Ctrl K "payroll" still lists Staff & payroll (the page also holds attendance/leave, so this is acceptable). Same on phone. Dead-end clicks give an error toast.
Evidence: w05-browser2\d-payroll-off.jpg

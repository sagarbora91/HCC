# W05-1 Compliance calendar listed for Manager and Cashier
Severity: S3 (page body is blocked, navigation leaks; breaches DEC-055 intent)
Steps: Preview as Meera (Manager) or Priya (Cashier). Open Accounts & Reports tile, or the drawer, or Ctrl+K "compliance".
Expected: no Compliance calendar entry for anyone except Owner and Accountant (Reception, Supervisor, Housekeeping, Maintenance, Tech admin correctly do not see it).
Actual: entry appears in the tile page, drawer and search for Manager and Cashier; opening it shows "Owner and Accountant only". Likely NavItem.p uses a broader permission than the in-page gate.
Desktop and phone: desktop verified; phone not separately checked.

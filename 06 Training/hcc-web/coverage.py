"""Renders ../coverage.md: the plan §4 chapter table, every page id and every feature id of the catalogue with the chapter that shows it.
A chapter is ticked when its number is in coverage_state.json ("done": [0, 1, ...]) - written by hand after the chapter replayed clean
(capture_report.json: 0 fails, 0 nohl). Run: python coverage.py   (reads steps.json for the step counts, feature-catalogue.ts and navmap.ts for the ids)."""
import json, os, re
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
PROTO = os.path.join(ROOT, '03 Prototype')
PAGES = {  # page id -> chapter (plan section 4)
    'home': 0, 'overview': 0, 'prefs': 0, 'reservations': 1, 'calendar': 1, 'arrivals': 2, 'guests': 2, 'keys': 2, 'documents': 2, 'rooms': 3,
    'inhouse': 4, 'guest-services': 4, 'gsv-my-requests': 4, 'desk-alerts': 4, 'messages': 4, 'folios': 5, 'plans-extras': 5, 'pos': 6, 'cashier': 7, 'cashbook': 7,
    'counter-bills': 8, 'approvals': 8, 'housekeeping': 9, 'maintenance': 10, 'handover': 11, 'customers': 12, 'yield': 13, 'tariff-card': 13,
    'groups': 14, 'channels': 14, 'booking-site': 14, 'hall-booking': 15, 'inventory': 16, 'expenses': 17, 'staff': 18, 'my-hr': 18, 'closing': 19,
    'reports': 20, 'report-hub': 20, 'owner-digest': 20, 'revenue-category': 20, 'accounting': 21, 'compliance': 22, 'assistant': 22,
    'access': 23, 'settings': 23, 'backup': 23, 'syshealth': 23, 'legacy-import': 23, 'roadmap': 23}
FEATURES = {
    'frontdesk.roomboard': 3, 'frontdesk.reservations': 1, 'frontdesk.checkin': 2, 'frontdesk.guests': 2, 'frontdesk.alerts': 4, 'frontdesk.keys': 2,
    'cash.folios': 5, 'cash.cashier': 7, 'cash.cashbook': 7, 'cash.counter': 8, 'cash.expenses': 17, 'cash.invoices': 8, 'pos.checks': 6, 'pos.menu': 6,
    'housekeeping.board': 9, 'housekeeping.complaints': 9, 'housekeeping.lostfound': 9, 'maintenance.workorders': 10, 'maintenance.assets': 10,
    'guestsvc.requests': 4, 'guestsvc.inhouse': 4, 'guestsvc.handover': 11, 'messaging.messages': 4, 'messaging.templates': 4, 'documents.capture': 2,
    'customers.c360': 12, 'customers.crm': 12, 'revenue.yield': 13, 'revenue.groups': 14, 'revenue.ota': 14, 'revenue.payouts': 14, 'revenue.website': 14,
    'revenue.tariffcard': 13, 'revenue.feedback': 14, 'revenue.hall': 15, 'revenue.quotations': 15, 'stock.linen': 16, 'stock.purchases': 16, 'stock.assets': 16,
    'people.staff': 18, 'people.leave': 18, 'people.payroll': 18, 'people.myhr': 18, 'closing.dayclose': 19,
    'accounting.ledgers': 21, 'accounting.vouchers': 21, 'accounting.gst': 21, 'accounting.tds': 21, 'accounting.tally': 21, 'accounting.yearend': 21, 'accounting.bank': 21,
    'reports.reports': 20, 'reports.hub': 20, 'reports.digest': 20, 'reports.plans': 5, 'compliance.calendar': 22, 'assistant.assistant': 22,
    'admin.access': 23, 'admin.config': 23, 'admin.masters': 23, 'admin.approvals': 23, 'admin.backup': 23, 'admin.health': 23, 'admin.prefs': 0,
    'admin.legacy': 23, 'admin.internet': 23, 'admin.roadmap': 23}
PLAN = [  # id, title, step target (plan section 4)
    (0, 'Welcome: Modules Home and the Dashboard', 10), (1, 'Reservations and the Calendar', 22), (2, 'Arrivals, registration and check-in', 28), (3, 'The Room board', 10),
    (4, 'In-house guests and guest services', 26), (5, 'The folio: charges, discounts and corrections', 20), (6, 'Restaurant and room service', 18),
    (7, 'Cashier: shift, payments, cash book', 20), (8, 'Check-out, invoice, credit note, counter bills', 24), (9, 'Housekeeping', 18), (10, 'Maintenance', 16),
    (11, 'Checklists and handover', 8), (12, 'Customers 360', 12), (13, 'Tariff, yield and the tariff card', 16), (14, 'Groups, companies, OTA and website', 20),
    (15, 'Hall booking', 18), (16, 'Linen, stock and purchasing', 16), (17, 'Expenses', 16), (18, 'Staff, leave and payroll', 22), (19, 'Day close', 14),
    (20, 'Reports and the Report hub', 16), (21, 'Accounting', 30), (22, 'Compliance calendar and the assistant', 12), (23, 'Administration and setup', 34),
    (24, 'Closing: shortcuts, help, quick-reference', 6)]
state = json.load(open(os.path.join(HERE, 'coverage_state.json'))) if os.path.exists(os.path.join(HERE, 'coverage_state.json')) else {'done': [], 'notes': {}}
done = set(state['done'])
steps = {}
if os.path.exists(os.path.join(HERE, 'steps.json')):
    for c in json.load(open(os.path.join(HERE, 'steps.json'), encoding='utf-8'))['chapters']: steps[c['id']] = len(c['steps'])
# check against the build
cat = open(os.path.join(PROTO, 'src', 'domain', 'feature-catalogue.ts'), encoding='utf-8').read()
ids = []
for m in re.split(r"\n  \{id:'", cat)[1:]:
    mid = m.split("'")[0]
    ids += [mid + '.' + f for f in re.findall(r"\n    \{id:'([^']+)',label:'", m)]
nav = open(os.path.join(PROTO, 'src', 'app', 'navmap.ts'), encoding='utf-8').read()
miss_f = [i for i in ids if i not in FEATURES]; extra_f = [i for i in FEATURES if i not in ids]
tick = lambda ch: 'x' if ch in done else ' '
L = ['# HCC training: coverage (generated by hcc-web/coverage.py; do not edit by hand)', '',
     'A box is ticked when the chapter replayed clean in capture (0 fails, 0 nohl). Plan: 02 Planning/HCC_Training_Media_Plan_TRN_2026-11-07.md section 4.', '',
     f'Features in the catalogue on this build: {len(ids)} (plan text said 60). Pages: {len(PAGES)} = 48 registered pages + Dashboard (`overview`) + Modules Home (`home`) (plan text said 39).', '']
if miss_f or extra_f: L += [f'**MISMATCH with feature-catalogue.ts**: missing {miss_f}, extra {extra_f}', '']
L += ['## Chapters', '', '| Done | Ch | Title | Steps (plan) | Steps (steps.json) |', '|---|---|---|---|---|']
for cid, title, n in PLAN: L.append(f'| [{tick(cid)}] | {cid} | {title} | {n} | {steps.get(cid, "-")} |')
L += ['', f'Total steps in steps.json: {sum(steps.values())}', '', '## Pages (id -> chapter)', '', '| Done | Page id | Chapter |', '|---|---|---|']
for p, c in PAGES.items(): L.append(f'| [{tick(c)}] | `{p}` | {c} |')
L += ['', '## Feature catalogue (module.feature -> chapter)', '', '| Done | Feature id | Chapter |', '|---|---|---|']
for f, c in FEATURES.items(): L.append(f'| [{tick(c)}] | `{f}` | {c} |')
if state.get('notes'):
    L += ['', '## Notes', ''] + [f'- {k}: {v}' for k, v in state['notes'].items()]
open(os.path.join(ROOT, '06 Training', 'coverage.md'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')
print('pages', len(PAGES), 'features', len(FEATURES), 'catalogue', len(ids), 'done', sorted(done), 'mismatch', miss_f, extra_f)

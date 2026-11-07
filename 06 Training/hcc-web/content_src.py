"""Builds content.json (recap + 3-question quiz per chapter, shortcuts, menu map, demo users) for build_deck.py. Edit THIS, run python content_src.py.
Every fact must match steps.json (checked by eye at each gate). Demo users are the 'Preview as' names of the prototype; there are no passwords."""
import json, os
HERE = os.path.dirname(os.path.abspath(__file__))
R = {}
def ch(i, learned, quiz): R[str(i)] = {'learned': learned, 'quiz': [{'q': q, 'a': a} for q, a in quiz]}

ch(0, ['Modules Home shows only the modules your role may use; the red number is work waiting.', 'The top bar shows the business date, the clock and who is signed in; Ctrl K opens search.', 'Appearance & display changes text size and theme on this device only.'],
   [('What does the red number on a module tile mean?', 'Work waiting in that module, for example arrivals to check in.'), ('Which key opens the search box?', 'Control and K.'), ('Does a text-size change affect other computers?', 'No. It belongs to this device only and is not hotel data.')])
ch(1, ['A reservation is confirmed with Confirm reservation or kept for two hours with Hold 2 hours.', 'The Calendar shows rooms against nights; an empty cell starts a booking.', 'A confirmed booking with money on it cannot be cancelled until the advance is refunded.'],
   [('Which button keeps a room for a guest who has not decided?', 'Hold 2 hours.'), ('Who may give a company a credit limit?', 'The Owner.'), ('What must happen before a booking with an advance can be cancelled?', 'The advance must be refunded first.')])
ch(2, ['Guided check-in has six steps: find the reservation, verify the guest, room eligibility, confirm the tariff, advance, check in.', 'The ID is checked and shown masked afterwards; the purpose of visit is compulsory.', 'A walk-in guest is registered and checked in in one step; key cards are made from the booking.'],
   [('How many steps does the guided check-in have?', 'Six.'), ('How is the Aadhaar number shown after saving?', 'Masked, for example XXXX XXXX 4321.'), ('Where do you make a key card?', 'In the booking dialog, with the Room key button.')])
ch(3, ['Each room card shows the number, type, guest and the date it is busy until.', 'The chips Occupied, Ready, Dirty, Cleaning, Inspection and Blocked filter the board.', 'Only a manager blocks or unblocks a room.'],
   [('What does Dirty mean on the board?', 'The room needs cleaning.'), ('Can Reception block a room?', 'No. A manager or owner does it.'), ('Why return the filter to All?', 'An old filter hides rooms from the next person.')])
ch(4, ['In-house stays lists every guest in the hotel; a room move keeps the booking and the folio.', 'Requests, wake-up calls and complaints are logged, assigned and timed.', 'Messages are queued in the Outbox and sent only to guests who gave consent; sending is simulated in the demo.'],
   [('Which tab handles a noisy air conditioner complaint?', 'Complaints in Guest services.'), ('Who sees My guest requests?', 'The housekeeper or other staff the request is assigned to.'), ('Does a queued message leave the hotel in the demo?', 'No. Delivery is simulated.')])
ch(5, ['Charges are posted from the catalogue so price and GST are right: laundry at 18 per cent, rooms at 5 per cent.', 'A discount is requested by the front desk and approved by the manager; at most 25 per cent of the charges.', 'A rate or guest change needs a reason and an approval.'],
   [('What GST rate does laundry carry?', '18 per cent.'), ('Who approves a service-recovery discount?', 'The manager.'), ('Which action changes the meal plan of a stay?', 'The Meal plan button on the folio.')])
ch(6, ['A table check is opened, dishes are added, the KOT is sent and the check is settled.', 'UPI and card settlements need a reference; room service is settled to the room folio.', 'A void needs a written reason and stays in the audit log.'],
   [('What does Send KOT do?', 'It sends the kitchen order ticket to the kitchen.'), ('How is a room-service dinner paid?', 'It is posted to the room folio and paid at check-out.'), ('What is required to void a bill?', 'A written reason.')])
ch(7, ['The cashier shift shows the expected cash and the opening float; the count is compared at once.', 'A bank deposit needs a photo of the slip; a till transfer moves cash between tills.', 'A cash receipt joins the drawer total immediately; the cash book lists each account.'],
   [('What does the Difference box show?', 'Counted cash minus expected cash.'), ('What is required to record a bank deposit?', 'A photo of the deposit slip.'), ('Where do you see cash by account?', 'In the Cash book.')])
ch(8, ['Check-out needs every room night posted and a zero balance; the invoice number is allocated once.', 'A company guest\'s balance moves to the company receivable after a manager approves it.', 'After the invoice only a credit note, approved by the Owner, can reduce it.'],
   [('Who approves a company credit of Rs 4,725?', 'A manager, because it is below Rs 10,000.'), ('Which GST rate applies to a room at Rs 3,000 a night?', '5 per cent, split into equal CGST and SGST.'), ('Who approves a credit note?', 'The Owner.')])

content = {'recap': R,
           'fkeys': [['F2', 'New booking'], ['F3', 'Arrivals & check-in'], ['F4', 'In-house stays'], ['F6', 'Room board'], ['F7', 'Guests & folios'], ['F8', 'Check-out'], ['F9', 'Rate / guest change'], ['F10', 'Cashier shift'], ['Alt+B', 'Counter bill'], ['Alt+/', 'Keyboard shortcuts'], ['Ctrl K', 'Search pages and actions']],
           'menumap': [['Front Desk', 'Room board, calendar, reservations, guests, room keys, arrivals & check-in'], ['Cash & Billing', 'Folios, counter bills, cashier shift, cash book, restaurant, expenses'],
                       ['Housekeeping & Maintenance', 'Housekeeping board, maintenance work orders, shift handover'], ['Guests & Services', 'In-house stays, guest services, messages, document capture'],
                       ['Customers', 'Customer 360 profiles and stay history'], ['Revenue & Sales', 'Tariff and yield, tariff card, groups, OTA, booking site, hall booking'],
                       ['Stock & Purchasing', 'Linen, stock, purchases, assets'], ['People & Payroll', 'Staff, leave, payroll, my shifts and pay'],
                       ['Accounts & Reports', 'Day closing, reports, report hub, accounting, compliance, assistant'], ['Admin & Setup', 'Users and roles, settings, masters, approvals, backup, system health']],
           'reference_lines': {'keyboard': ['Ctrl K - search pages and actions', 'Esc - close a dialog or the search box', 'F2 new booking, F3 arrivals, F6 room board, F7 folios, F10 cashier'],
                               'passwords': ['Demo users: choose a name with Preview as. There are no passwords in the training build.'],
                               'keyboard_narration': 'Control K opens the search box, Escape closes a dialog. F2 is a new booking, F3 arrivals, F6 the room board, F7 folios and F10 the cashier shift.',
                               'passwords_narration': 'The training build has demo users that you choose by name. There are no passwords.'}}
json.dump(content, open(os.path.join(HERE, 'content.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('recaps', len(R))

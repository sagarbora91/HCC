"""Builds content.json for the phone deck (recap + quiz per chapter, keys, menu map)."""
import json, os
HERE = os.path.dirname(os.path.abspath(__file__))
R = {}
def ch(i, learned, quiz): R[str(i)] = {'learned': learned, 'quiz': [{'q': q, 'a': a} for q, a in quiz]}
ch(0, ['The phone shows modules as cards; the red number is work waiting.', 'The bottom tabs are Rooms, Arrivals, Bookings, Folios and Menu.', 'The magnifier in the app bar searches pages and actions.'],
   [('Which five tabs are at the bottom?', 'Rooms, Arrivals, Bookings, Folios and Menu.'), ('What does the badge on the Arrivals tab count?', 'The arrivals still to check in.'), ('Where do you sign out?', 'In the Menu.')])
ch(1, ['Tapping an arrival opens the guided check-in at Step 2, Verify guest.', 'The steps are Find, Verify guest, Room, Tariff, Advance and Check in.', 'The ID is checked and shown masked; the purpose of visit is compulsory.'],
   [('Which step comes after Verify guest?', 'Room.'), ('What is needed before Check in?', 'The purpose of visit.'), ('Is an advance compulsory at check-in?', 'No, it is optional.')])
ch(2, ['The room board filters by status and floor.', 'An attendant starts a task, ticks the checklist and completes the cleaning; the supervisor inspects.', 'A fault found while cleaning is reported with Report repair.'],
   [('What must be ticked before Complete cleaning works?', 'All three checklist items.'), ('Who sees only their own rooms?', 'The room attendant.'), ('Where do you report a fault from the room card?', 'Report repair.')])
ch(3, ['My guest requests lists the requests assigned to you.', 'Start and Complete take the request through its time target; Complete asks for a note.', 'Found item logs anything found in a room.'],
   [('What does Start do?', 'It stops the waiting time and starts the work time.'), ('What does Complete ask for?', 'A short note for the guest.'), ('Where do you log a found charger?', 'Found item.')])
ch(4, ['My work orders lists the jobs by priority.', 'A repair request becomes a work order with Create work order.', 'A job is completed with the parts booked and a note.'],
   [('What does Create work order do?', 'It turns a repair request into a tracked job.'), ('What does Complete need?', 'A note of what you did.'), ('Does the technician see prices?', 'No.')])
ch(5, ['A folio opens from the Folios tab with its charges, receipts and requests.', 'Laundry is posted from the catalogue at 18 per cent GST.', 'A UPI payment needs a reference and a confirmation.'],
   [('Which GST rate does laundry carry?', '18 per cent.'), ('What does UPI need?', 'A reference.'), ('Which button finishes the payment?', 'Confirm with the amount.')])
ch(6, ['The Approvals inbox has Requests, Expenses and Audit log.', 'The manager approves an expense after reading it.', 'Day closing shows the steps and what blocks the close.'],
   [('Who approves staff expenses?', 'The manager.'), ('Where do you see what blocks the day close?', 'In the Preflight card.'), ('Does the phone follow the same close order as the PC?', 'Yes.')])
ch(7, ['My shifts & pay shows your own shift, week, payslips and leave.', 'Request leave sends the request to a manager.', 'Appearance & display sets the text size of this device only.'],
   [('Whose pay do you see?', 'Only your own.'), ('Who approves your leave?', 'A manager or the Owner.'), ('Does a text-size change affect other phones?', 'No, it belongs to this device.')])
content = {'recap': R,
 'fkeys': [['Rooms', 'Room board'], ['Arrivals', 'Arrivals and check-in'], ['Bookings', 'Reservations'], ['Folios', 'Guest bills and payments'], ['Menu', 'All modules, display mode, sign out'], ['Magnifier', 'Search pages and actions']],
 'menumap': [['Front Desk', 'Rooms, bookings, arrivals and check-in'], ['Cash & Billing', 'Folios, cashier, expenses'], ['Housekeeping & Maintenance', 'My tasks, work orders, handover'], ['Guests & Services', 'My guest requests, in-house, messages'],
             ['People & Payroll', 'My shifts & pay'], ['Accounts & Reports', 'Day closing and reports'], ['Admin & Setup', 'Approvals, appearance, users']],
 'reference_lines': {'keyboard': ['Back (or Esc) closes the top-most sheet', 'Tap the magnifier to search', 'The tabs are always at the bottom'], 'passwords': ['Demo users: choose a name with Preview as. No passwords in the training build.'],
                     'keyboard_narration': 'Back closes the top-most sheet. Tap the magnifier to search. The tabs are always at the bottom.', 'passwords_narration': 'The training build has demo users that you choose by name. There are no passwords.'}}
json.dump(content, open(os.path.join(HERE, 'content.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('recaps', len(R))

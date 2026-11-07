chapter(1, 'Reservations and the Calendar', 'Take a booking for Mr. Rahul Deshmukh, hold a room for an enquiry, book a company guest, read the Calendar and cancel a booking.',
        'Aarav (Reception)', 'reception', menu='Front Desk > Reservations')
read('You are Aarav at Reception, on Modules Home. This chapter takes bookings, so you will work in the Front Desk module.', 'Choose the module first, then the screen.', target='sel:.fit-home-grid', expect='Modules Home for Aarav.')
tile('Front Desk', 'Click the Front Desk tile.', 'Reservations, the Calendar, check-in and the room board all live in this module.', expect='The Front Desk page opens.')
leaf('Front Desk', 'Reservations', 'Click the Reservations tile.', 'This list holds every booking, past and future.',
     expect='The Reservations page opens with all bookings.', check="ctx.page==='reservations'")
read('Read the list. Each row shows the guest, booking number, room, stay dates, status, advance paid and folio balance.',
     'Reception checks this list before telling a caller whether a booking exists.', target='sel:table', expect='The list of all bookings, newest first.')
click('btn:Held', 'Click the Held chip to see only bookings that are on hold.',
      'A hold keeps a room for a short time while the guest decides. Held, Expired, Confirmed, In house, Departed, Cancelled and No show each have a chip.',
      expect='Only held bookings are listed.')
step('Click All, then type a guest name in the search box. Type Ghate.', 'Search finds a booking by guest name, phone number, room number or booking number.',
     target='field:Search guest', auto=['click:btn:All', 'type:field:Search guest|Ghate'], action='type', expect='Only bookings of guests named Ghate are listed.')
typ('field:Search guest', '', 'Clear the search box to see all bookings again.', 'An old search left in the box hides bookings and confuses the next person.', expect='All bookings are listed again.')
click('btn:New reservation', 'Click New reservation to take a booking.', 'A reservation reserves a room before the guest arrives.',
      expect='The New reservation dialog opens.', check="!!q('[role=dialog]')")
read('The dialog opens with Arrival 24 Oct 2026 and Departure 26 Oct 2026. Rahul Deshmukh stays two nights, so leave the dates as they are.',
     'The dates decide the room rate, the tax and the availability list below.', target='field:Arrival', expect='Arrival 24 Oct, Departure 26 Oct.')
pick('field:Adults', '2', 'Choose 2 adults. Rahul travels with Mrs. Sneha Deshmukh.', 'The number of guests limits which room types can be booked.', expect='Adults 2.')
pick('field:Room type', 'Deluxe', 'Choose the room type Deluxe.', 'The list of rooms below shows only the rooms that are free for these dates.', expect='Only free Deluxe rooms are listed.')
tick('field:203 ·', 'Choose room 203, Deluxe, Rs 3,000 a night.', 'Auto-assign would pick the first free room. Rahul asked for 203, so choose it yourself.', expect='Room 203 is selected.')
typ('field:Guest name', 'Rahul Deshmukh', 'Type the guest name: Rahul Deshmukh.', 'The name on the booking is printed on the registration card and the invoice.', expect='Guest name filled.')
typ('field:Phone', '9800000001', 'Type the mobile number. The training uses a dummy number.', 'The phone number is how reception reaches the guest and finds the booking again.', expect='Phone filled.')
pick('field:Booking source', 'Phone', 'Choose the booking source Phone, because Rahul booked by calling the hotel.', 'The source feeds the reports on where bookings come from.', expect='Source is Phone.')
typ('field:Notes', 'Wake-up call 06:30 on 25 Oct. Two extra towels.', 'Type the notes: wake-up call at 06:30 on 25 October and two extra towels.',
    'Notes travel with the booking so the night staff and housekeeping can see the guest\'s requests.', expect='Notes filled.')
click('modalbtn:Confirm reservation', 'Click Confirm reservation.', 'Confirm keeps the room for the guest. Use Hold 2 hours instead when the guest has not decided yet.',
      expect='The reservation is confirmed as HCC-1101 and its booking dialog opens.', check="has('Rahul Deshmukh') && has('HCC-1101')",
      msgText='Reservation confirmed. Open it to register the guest and check in.')
read('The booking dialog opens for HCC-1101: Rahul Deshmukh, room 203, 24 to 26 October, Confirmed. The buttons are the actions you can take from here.',
     'From this dialog reception changes, cancels, prints and checks in the booking.', target='dialog:', expect='The booking dialog with the folio and the action buttons.')
click('modalbtn:Change dates / room', 'Click Change dates / room to see how a booking is amended.', 'Guests change plans; the booking can be moved without cancelling it.',
      expect='The Move / change dates view opens inside the dialog.', check="has('Move / change dates')")
click('modalbtn:Back', 'Change nothing. Click Back.', 'Back returns to the booking without saving anything.', expect='The booking dialog is shown again.')
press('Escape', 'Press Escape to close the booking dialog.', 'Escape closes any dialog and leaves the data as it was.', target='sel:table', expect='The Reservations list is shown.',
      check="!q('[role=dialog]')")
click('btn:New reservation', 'Now an enquiry: Mrs. Meena Kulkarni phones but has not decided. Click New reservation.', 'A hold stops another guest taking the room while she decides.',
      expect='A new, empty reservation dialog opens.', check="!!q('[role=dialog]')")
step('Enter her name and phone, then set Arrival 28 Oct and Departure 30 Oct.', 'Always enter dates first, because only rooms free on those dates are offered.',
     target='field:Guest name', auto=['type:field:Guest name|Mrs. Meena Kulkarni', 'type:field:Phone|9800000002', 'type:field:Arrival|2026-10-28', 'type:field:Departure|2026-10-30'],
     action='type', expect='Name, phone and the new dates are filled.')
click('modalbtn:Hold 2 hours', 'Click Hold 2 hours.', 'The room is kept for two hours. If nobody confirms it, the hold lapses by itself and shows under Expired.',
      expect='The booking is saved as Held.')
step('Before booking a company guest, the company needs an account with a credit limit, and only the Owner may set a limit. Sagar, the Owner, opens Revenue & Sales, Groups, the Companies tab and clicks New company.',
     'A company account lets the hotel bill the company later. Reception asks the Owner for it; reception cannot book a company on credit without one.', target='dialog:',
     auto=['user:owner', 'menu:Revenue & Sales>Groups', 'click:btn:Companies', 'click:btn:New company'], action='click', menu='Revenue & Sales > Groups > Companies',
     expect='The New company account dialog opens.', check="has('New company account')")
step('Type the company name Shree Agro Industries, the fictional GSTIN 27AABCS1234A1Z5 and the opening credit limit 50,000 rupees.',
     'The credit limit stops the company running up more than the hotel agreed to carry.', target='field:Company name',
     auto=['type:field:Company name|Shree Agro Industries', 'type:field:GSTIN|27AABCS1234A1Z5', 'type:field:Opening credit limit|50000'], action='type',
     value='Shree Agro Industries; 27AABCS1234A1Z5; 50000', expect='Company details filled.')
click('modalbtn:Create account', 'Click Create account.', 'The company now appears in the booking form and in Accounting as a party.', expect='Shree Agro Industries is created.')
step('Back at Reception, Aarav books Mr. Vikram Jadhav of Shree Agro Industries: Suite 106 from 24 to 25 October, billed to the company account. Open Reservations, click New reservation and fill the form.',
     'The company guest does not pay at check-out; the folio goes to the company account.', target='field:Bill to company account',
     auto=['user:reception', 'menu:Front Desk>Reservations', 'click:btn:New reservation', 'type:field:Guest name|Mr. Vikram Jadhav', 'type:field:Phone|9800000003', 'select:field:Room type|Suite',
           'check:field:106 ·', 'type:field:Departure|2026-10-25', 'select:field:Bill to company account|Shree Agro'], action='type',
     expect='The form shows Vikram Jadhav, Suite 106, 24 to 25 October, billed to Shree Agro Industries.')
click('modalbtn:Confirm reservation', 'Click Confirm reservation.', 'A company booking is confirmed like any other; only the billing differs.',
      expect='The booking for Vikram Jadhav is confirmed.')
press('Escape', 'Press Escape to close the booking dialog.', 'You will check Vikram in later, with the other arrivals.', target='sel:table', expect='The list is shown.')
step('Open the Calendar from Front Desk. Each row is a room and each column a night; coloured bars are bookings.', 'The Calendar shows at a glance which rooms are free on which nights.',
     target='sel:main h1', auto=['menu:Front Desk>Calendar'], action='click', menu='Front Desk > Calendar', expect='The Calendar opens, 24 Oct to 6 Nov.', check="ctx.page==='calendar'")
click('btn:Next week', 'Click Next week to move the window forward a week.', 'The grid shows two weeks at a time; the arrows move it.', expect='The window now starts a week later.')
click('btn:Today', 'Click Today to come back to the business date.', 'Today always returns to 24 Oct 2026, the business date.', expect='The window starts at 24 Oct again.')
click('aria:Book room 105 from 26 Oct', 'Click an empty cell, for example room 105 from 26 Oct, to start a booking for that room and night.',
      'This is the quickest way to book when you are looking at the grid.', expect='The New reservation dialog opens for room 105 from 26 Oct.', check="!!q('[role=dialog]')")
press('Escape', 'Press Escape to leave without booking.', 'Nothing is saved until you click Confirm or Hold.', target='sel:main h1', expect='The Calendar is shown.')
step('Mrs. Kulkarni calls back to cancel. Open Reservations, search booking HCC-1103 and open it.', 'Search by booking number is exact, so you never open the wrong guest.',
     target='dialog:', auto=['menu:Front Desk>Reservations', 'type:field:Search guest|HCC-1103', 'click:btn:Open Mrs. Meena Kulkarni'], action='click',
     expect='The booking dialog for the held booking opens.', check="has('Kulkarni') && !!q('[role=dialog]')")
read('A held booking has Confirm hold when the guest agrees and Cancel hold when she does not. A confirmed booking has Cancel / no-show instead; if money is held on it, refund the advance first.',
     'Knowing which button belongs to which status saves a wrong click.', target='dialog:', expect='The held booking dialog with Confirm hold and Cancel hold.')
click('modalbtn:Cancel hold', 'Click Cancel hold. Mrs. Kulkarni does not want the rooms.', 'A hold is cancelled in two clicks so that nobody cancels one by mistake.',
      expect='A confirmation asks you to confirm the cancellation.', check="has('Back')")
click('modalbtn:Cancel hold', 'Click Cancel hold again to confirm.', 'Cancelling a hold frees the room at once; the booking stays in the list as Cancelled for the record.',
      expect='The hold is cancelled and the room is free again.', check="has('Hold cancelled')", msgText='Hold cancelled. Room released.')

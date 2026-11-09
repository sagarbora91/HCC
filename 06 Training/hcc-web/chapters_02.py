chapter(2, 'Arrivals, registration and check-in', 'Check in Mr. Rahul Deshmukh with a verified ID and an advance, walk in Ms. Anita Rao, find a guest profile, capture IDs and issue a room key.',
        'Aarav (Reception)', 'reception', menu='Front Desk > Arrivals & check-in')
read('Rahul Deshmukh has arrived. You are Aarav on Modules Home; check-in starts in the Front Desk module.', 'Check-in is the most important job at the front desk: it ties the guest, the ID and the room together.',
     target='sel:.fit-home-grid', expect='Modules Home.')
tile('Front Desk', 'Click the Front Desk tile.', 'Arrivals, guests, keys and documents are all front-desk screens.', expect='The Front Desk page opens.')
leaf('Front Desk', 'Arrivals & check-in', 'Click the Arrivals & check-in tile.', 'This screen lists everyone expected today.', expect='The Arrivals & check-in page opens.', check="ctx.page==='arrivals'")
read('Four bookings are expected today: Ishaan Bose, Vikram Jadhav, Rahul Deshmukh and Aditi Nair. The list shows the registration status, the room status and the advance paid.',
     'You see at a glance whether the room is ready and whether the guest is already registered.', target='sel:table', expect='The list of expected arrivals.')
typ('field:Search guest', 'HCC-1101', 'Type the booking number HCC-1101 in the search box. The box also accepts a name, a room or a vehicle number.',
    'A booking number is the surest way to find the right guest.', expect='Only Rahul Deshmukh is listed.')
click('btn:Start check-in, Rahul Deshmukh', 'Click Start check-in on Rahul\'s row.', 'The guided check-in takes you through six steps, so nothing is forgotten.',
      expect='The guided check-in opens at the registration step.', check="has('Verify guest')")
read('Step 2 of 6 is the registration card. The name and phone come from the booking; address and ID are still empty.',
     'The law requires a verified identity for every guest, so the room cannot be given until this step is done.', target='dialog:', expect='The registration fields.')
typ('field:Address', '12 Karve Road, Pune 411004', 'Type the address from the guest\'s ID: 12 Karve Road, Pune 411004.', 'The address is printed on the registration card and the invoice.', expect='Address filled.')
pick('field:ID type', 'Aadhaar', 'Choose the ID type Aadhaar.', 'Each ID type has its own number check; Aadhaar needs exactly twelve digits with a valid check digit.', expect='ID type Aadhaar.')
typ('field:ID number', '234510094321', 'Type the Aadhaar number from the card. The training uses a fictional number that passes the check.',
    'After saving, HCC shows the number masked, XXXX XXXX 4321, to everyone except those who may view it.', expect='ID number accepted.')
click('modalbtn:Verify registration', 'Click Verify registration.', 'The registration is saved and the guest is marked Verified.', expect='Step 3 of 6, Room eligibility, opens.',
      check="has('Room eligibility')", msgText='Guest verified. Registration saved.')
click('modalbtn:Continue', 'Room 203 is vacant and ready, so it is eligible. Click Continue.', 'A dirty or blocked room cannot be given; HCC would say why on this step.', expect='Step 4 of 6, Confirm tariff, opens.', check="has('Confirm tariff')")
tick('field:The guest has confirmed', 'Read the tariff to the guest, Rs 3,000 a night for 2 nights, and tick that the guest confirmed it.',
     'Rate disputes at check-out are avoided when the guest agreed at check-in.', expect='The tariff box is ticked.')
click('modalbtn:Continue', 'Click Continue.', 'Next is the advance. It is optional here.', expect='Step 5 of 6, Advance / deposit, opens.', check="has('Advance / deposit')")
click('modalbtn:Record advance / deposit', 'Rahul pays Rs 2,000 as advance. Click Record advance / deposit.', 'An advance is money received before the stay; it is kept on the booking and counted against the final bill.',
      expect='The payment dialog opens.', check="has('Entry type')")
typ('field:Amount', '2000', 'Type the amount 2000.', 'Amounts are entered in rupees and stored exactly.', expect='Amount 2000.')
pick('field:Method', 'UPI', 'Choose the method UPI.', 'The method decides which cashier total the money joins: cash, UPI, card or bank transfer.', expect='Method UPI.')
typ('field:Reference', 'UPI-DEMO-2001', 'Type the UPI reference number from the guest\'s phone.', 'The reference lets the accountant match the receipt with the bank statement.', expect='Reference filled.')
click('modalbtn:Review', 'Click Review.', 'Review shows the balance after posting before anything is saved.', expect='A summary of the receipt is shown.', check="has('Post receipt')")
click('modalbtn:Post receipt', 'Click Post receipt.', 'Posting a receipt twice is blocked; a repeated click never creates a second receipt.', expect='The receipt is recorded and the booking dialog is shown.',
      msgText='Receipt recorded once', check="has('Guided check-in')")
click('modalbtn:Guided check-in', 'Click Guided check-in to continue where you stopped.', 'Completed steps are remembered from the record.', expect='The guided check-in opens again.', check="has('Confirm tariff')")
step('Tick the tariff confirmation again and click Continue twice to reach step 6, Check in.', 'The tick belongs to this session; the advance step is complete now.',
     target='field:Purpose of visit', auto=['check:field:The guest has confirmed', 'click:modalbtn:Continue', 'click:modalbtn:Continue'], action='click',
     expect='Step 6 of 6, Check in, with the stay details.', check="has('Step 6 of 6')")
pick('field:Purpose of visit', 'Business', 'Choose the purpose of visit: Business.', 'The purpose of visit is part of the legal guest record.', expect='Purpose Business.')
typ('field:Vehicle number', 'MH 12 AB 1234', 'Type the car number MH 12 AB 1234.', 'The vehicle number helps parking and can be searched on this screen.', expect='Vehicle number filled.')
click('modalbtn:Check in', 'Click Check in.', 'Check-in makes the guest in-house and updates the room board to Occupied.', expect='Rahul Deshmukh is checked in to room 203.',
      msgText='Checked in to room 203. Room board updated.', check="has('Checked in')")
click('modalbtn:Registration card', 'Click Registration card to see the card the guest signs.', 'The card carries the hotel letterhead, the stay, the guests and the masked ID.', expect='The registration card opens.', check="has('DEMO-REG-HCC-1101')")
read('This is the registration card of Rahul Deshmukh, with the accompanying guests and the masked ID. Print it, or save a PDF, and let the guest sign.',
     'A signed card is the proof of registration.', target='dialog:', expect='The registration card for HCC-1101.')
step('Close the card and the dialog, then open Walk-in on the Arrivals page for a guest without a booking.', 'A walk-in guest is registered and checked in in one step.',
     target='dialog:', auto=['key:Escape', 'menu:Front Desk>Arrivals & check-in', 'click:btn:Walk-in'], action='click', expect='The Walk-in guest dialog opens.', check="has('Walk-in guest')")
step('Ms. Anita Rao from Hyderabad has no booking. Type her name, phone and address.', 'The same registration rules apply to a walk-in.', target='field:Guest name',
     auto=['type:field:Guest name|Ms. Anita Rao', 'type:field:Phone|9800000004', 'type:field:Address|8 Banjara Hills, Hyderabad 500034'], action='type', expect='Name, phone and address filled.')
step('Choose ID type Aadhaar and type her fictional number 345678901238.', 'The ID is checked before the guest can be checked in.', target='field:ID number',
     auto=['select:field:ID type|Aadhaar', 'type:field:ID number|345678901238'], action='type', expect='ID filled.')
step('Choose room 105, Classic, for one night and the purpose Tourism.', 'Only rooms that are vacant, ready and free for the whole stay are offered.', target='field:Room',
     auto=['select:field:Room|105', 'select:field:Purpose of visit|Tourism'], action='select', expect='Room 105 and Tourism chosen.')
click('modalbtn:Create walk-in and check in', 'Click Create walk-in and check in.', 'HCC creates the booking, registers the guest and checks her in. Anita pays her bill at the cashier.',
      expect='Anita Rao is checked in to room 105.', check="has('Anita Rao')")
step('Open Guests from Front Desk and search for Deshmukh. This is the guest register.', 'The register keeps one profile per guest, with every stay.', target='field:Search',
     auto=['key:Escape', 'menu:Front Desk>Guests', 'type:field:Search|Rahul Deshmukh'], action='type', expect='Guests named Rahul Deshmukh are listed.', check="ctx.page==='guests'")
click('btn:Rahul DeshmukhG-1110', 'Click Rahul Deshmukh, profile G-1110.', 'The profile shows his contact details, the accompanying guests, photos and the stay history.',
      expect='The profile opens on the right.', check="has('Stay history')")
step('Open Document capture under Guests & Services. Here a scanned ID or bill is filed against a guest.', 'Scanning is simulated in the demo; on a real PC the scanner or camera sends the picture here.',
     target='sel:main h1', auto=['menu:Guests & Services>Document capture'], action='click', menu='Guests & Services > Document capture', expect='The Document capture page opens.', check="ctx.page==='documents'")
OPEN_KEY = ['user:reception', 'menu:Front Desk>Reservations', 'type:field:Search guest|HCC-1101', 'click:btn:Open Rahul Deshmukh', 'click:modalbtn:Room key']
FINISH_JOB = ['user:manager', 'menu:Front Desk>Room keys', 'click:btn:Encoder', 'click:btn:Finish job']
step('Key cards are made from the booking. Open the booking of Rahul in Reservations and click Room key.', 'The key belongs to the stay, so it is made from the booking of the stay.', target='dialog:',
     auto=['key:Escape'] + OPEN_KEY, action='click', expect='The room key panel opens for room 203 and says the key service is offline.', check="has('Encoder offline')")
step('The panel says the encoder is offline. In the training, Meera, the Manager, brings the simulated encoder online on the Room keys page, Encoder tab.', 'Only a manager can switch the encoder. In the real hotel the Key Agent beside the front-desk PC does this by itself.',
     target='btn:Take agent offline', auto=['key:Escape', 'user:manager', 'menu:Front Desk>Room keys', 'click:btn:Encoder', 'click:btn:Bring agent online'], action='click',
     expect='The encoder is online.', msgText='Agent online', check="has('Take agent offline')")
step('Back at Reception, open the room key panel again and click Issue key to make the card for room 203.', 'The request goes to the encoder at the front desk, which writes the card.',
     target='dialog:', auto=OPEN_KEY + ['click:modalbtn:Issue key'], action='click', expect='A key request is sent and shown as waiting.', msgText='Key request sent')
step('At the encoder, the job runs. In the training Meera clicks Finish job on the Encoder tab.', 'When the card is written the job turns to Done and the card is listed under Active cards.',
     target='btn:Finish job', auto=['key:Escape'] + FINISH_JOB, action='click', expect='The encoder ran the job.', msgText='Encoder ran the job')
step('Suppose the guest loses the card. Open the room key panel and click Lost card.', 'A lost card is cancelled and a new one issued, so the old card cannot open the door.',
     target='dialog:', auto=['key:Escape'] + OPEN_KEY + ['click:modalbtn:Lost card'], action='click', expect='A re-issue request is sent.', msgText='Key request sent')
step('Click Finish job again on the Encoder tab to write the new card.', 'The old card is cancelled; only the new card opens room 203.', target='btn:Finish job',
     auto=['key:Escape'] + FINISH_JOB, action='click', expect='The encoder ran the job.', msgText='Encoder ran the job')
step('Open the Key log tab. It lists every key action with its time, room and user.', 'The log lets the manager see who made which card and when.', target='sel:table',
     auto=['click:btn:Key log'], action='click', expect='The key log shows the issue and the re-issue for room 203.', check="has('203')")

chapter(4, 'In-house guests and guest services', 'Look after Rahul Deshmukh while he stays: in-house list, requests, wake-up call, a complaint, a room move, reminders and messages.',
        'Aarav (Reception)', 'reception', menu='Guests & Services')
read('Rahul is in room 203. You are Aarav on Modules Home; everything for guests who are staying lives under Guests & Services.', 'A good stay is made of small requests handled on time.', target='sel:.fit-home-grid', expect='Modules Home.')
tile('Guests & Services', 'Click the Guests & Services tile.', 'It holds in-house stays, guest requests, messages and document capture.', expect='The Guests & Services page opens.')
leaf('Guests & Services', 'In-house stays', 'Click the In-house stays tile.', 'This is the list of every guest in the hotel now.', expect='The In-house stays page opens.', check="ctx.page==='inhouse'")
read('The list shows each in-house room with the guest, booking number, stay dates and the balance. Find room 203, Rahul Deshmukh.', 'The balance column tells reception who still owes money before check-out.', target='row:HCC-1101', expect='The in-house list.')
click('btn:203Rahul Deshmukh', 'Click the row of room 203.', 'The details of the stay open on the right: room segments, occupants and the folio summary.', expect='The details of the stay HCC-1101 open.', check="has('Room segments')")
click('btn:Extend / shorten', 'Click Extend / shorten to see how the departure date is changed.', 'A guest who stays longer or leaves early is handled here, with the folio and the room kept in step.',
      expect='The Extend / shorten tab shows the departure date.', check="has('Extend')")
press('Escape', 'Press Escape. Rahul keeps his dates, so change nothing.', 'The Leaves today tab next to it starts an early check-out.', target='sel:main h1', expect='The In-house list is shown.', check="!q('[role=dialog]')")
step('Open Guest services under Guests & Services and click New request. Rahul asks for two extra towels.', 'Every request is logged so that it is done, timed and not forgotten.', target='dialog:',
     auto=['menu:Guests & Services>Guest services', 'click:btn:New request'], action='click', expect='The New request dialog opens.', check="has('Log request')")
step('Choose Room 203, Rahul Deshmukh, and the request Extra towels. Assign it to Ravi.', 'The assignee gets the request on his phone; the priority decides how fast it must be done.', target='field:Guest / room',
     auto=['select:field:Guest / room|Room 203', 'select:field:Request|Extra towels', 'select:field:Assign to|Ravi'], action='select', expect='Room 203, Extra towels, assigned to Ravi.')
click('modalbtn:Log request', 'Click Log request.', 'The request starts its service clock now.', expect='The request is logged.', msgText='Request logged', check="has('Extra towels')")
step('Rahul also wants a wake-up call at 06:30 on 25 October. Click New request, choose Room 203, the request Wake-up call and type the time in Detail.', 'A wake-up request is a promise; the night desk calls at the time given.',
     target='field:Detail', auto=['click:btn:New request', 'select:field:Guest / room|Room 203', 'select:field:Request|Wake-up call', 'type:field:Detail|06:30 on 25 Oct', 'select:field:Department|Front desk'], action='type',
     expect='A wake-up request for room 203 at 06:30.')
click('modalbtn:Log request', 'Click Log request.', 'The wake-up now appears on the Wake-ups list of Reminders & wake-ups.', expect='The wake-up request is logged.')
click('tab:Complaints', 'Rahul calls at night: the air conditioner is noisy. Open the Complaints tab.', 'A complaint is different from a request: it needs an owner and a follow-up.', expect='The Complaints list opens.')
click('btn:Log complaint', 'Click Log complaint.', 'The dialog links the complaint to the stay.', expect='The Log complaint dialog opens.', check="has('What happened')")
step('Link it to Room 203, choose the category Maintenance, severity Medium, and describe what happened.', 'The category sends the complaint to the right team; the severity decides how quickly the manager must act.',
     target='field:What happened', auto=['select:field:Stay|Room 203', 'select:field:Category|Maintenance', 'select:field:Severity|Medium', 'type:field:What happened|Air conditioner in room 203 is noisy at night'], action='type',
     expect='The complaint form is filled.')
click('modalbtn:Log complaint', 'Click Log complaint.', 'You will turn this into a work order for the maintenance team later, and offer the guest a service-recovery discount on the folio.', expect='The complaint is logged.', msgText='Complaint logged')
click('tab:Lost & found', 'Open the Lost & found tab.', 'Anything a guest leaves behind is logged here with the room and the finder; housekeeping adds the entries, reception returns the items.', expect='The Lost & found list opens.')
step('The next day the air conditioner is still faulty, so Rahul is moved. Open In-house stays, open room 203 and click Move room.', 'A move keeps the booking, the folio and the guest history; only the room changes.',
     target='dialog:', auto=['menu:Guests & Services>In-house stays', 'click:btn:203Rahul Deshmukh', 'click:btn:Move room'], action='click', expect='The Move room dialog opens.', check="has('Move to')")
read('The dialog offers rooms that are free and ready: 107 Deluxe (same type), 205 Classic and 306 Suite. Room 207 in the plan is reserved for an arrival today, so the training uses 107.',
     'Giving the same room type avoids a rate change; a cheaper or dearer type needs the rate rule explained to the guest.', target='dialog:', expect='107 Deluxe is selected.')
click('modalbtn:Move to 107', 'Click Move to 107.', 'The old room goes to housekeeping as dirty and the room board changes at once.', expect='Rahul is now in room 107.', check="has('107')")
step('Ravi, the room attendant, sees the towel request on his phone-style page My guest requests. Open it as Ravi.', 'Housekeepers see only their own requests and no money.', target='sel:main h1',
     auto=['key:Escape', 'user:housekeeper', 'click:menutop:Guests & Services'], action='click', menu='Guests & Services', expect='My guest requests lists the towels for room 203.', check="ctx.page==='gsv-my-requests'")
click('btnin:Rahul Deshmukh|Start', 'Click Start when you take the request.', 'Start stops the waiting time and starts the work time.', expect='The request is In progress.')
click('btnin:Rahul Deshmukh|Complete', 'Click Complete when the towels are delivered.', 'Completing closes the request and records the service time against the target.', expect='The request is Done.')
step('Back at Reception, open Reminders & wake-ups under Front Desk and click New reminder.', 'Reminders are notes with a date that the desk sees on the day.', target='dialog:',
     auto=['user:reception', 'menu:Front Desk>Reminders & wake-ups', 'click:btn:New reminder'], action='click', expect='The New reminder dialog opens.', check="has('Save reminder')")
step('Type the subject Collect ID copy from 206, keep today\'s date and the alert of 3 days.', 'The alert days make the reminder show earlier, so it is not missed.', target='field:Subject',
     auto=['type:field:Subject|Collect ID copy from 206', 'type:field:Alert days before|0'], action='type', expect='Subject and date filled.')
click('modalbtn:Save reminder', 'Click Save reminder.', 'The reminder appears on the desk list today and as a banner until it is done.', expect='The reminder is saved.')
click('tab:Wake-ups', 'Open the Wake-ups tab.', 'All wake-up calls of the day are listed; the night desk marks each as done after calling.', expect='The wake-up list opens.')
step('Open Messages under Guests & Services. The Outbox lists every message sent to guests; click Compose.', 'Messages are queued here; sending is simulated in the demo, so no real WhatsApp, SMS or e-mail leaves the hotel.', target='dialog:',
     auto=['menu:Guests & Services>Messages', 'click:btn:Compose'], action='click', expect='The Compose message dialog opens.', check="has('Queue message')")
step('Choose the guest Rahul Deshmukh, the channel WhatsApp and the template Booking confirmation.', 'A template fills the text with the guest\'s name and booking details.', target='field:Start from template',
     auto=['select:field:Guest|Rahul Deshmukh', 'select:field:Channel|WhatsApp', 'select:field:Start from template|Booking confirmation'], action='select', expect='The message text is filled from the template.')
click('modalbtn:Queue message', 'Click Queue message.', 'The message waits in the Outbox. A guest who has not given consent receives nothing.', expect='The message is in the Outbox.')
click('btn:Simulate delivery', 'Click Simulate delivery to play the delivery of the queued messages.', 'In the real hotel the messaging service delivers; here the demo shows what would happen.', expect='The message shows as delivered.')
click('tab:Consent', 'Open the Consent tab.', 'Guests must agree before they get marketing or service messages; HCC keeps the record.', expect='The consent register opens.')
click('tab:Templates', 'Open the Templates tab.', 'Templates are the standard texts of the hotel; the owner and manager can edit them.', expect='The six message templates are listed.')
click('tab:Triggers', 'Open the Triggers tab.', 'A trigger is a rule: for example send a welcome message at check-in or a feedback request after check-out.', expect='The seven message rules are listed.')

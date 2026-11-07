chapter(12, 'Customers 360', 'Find a customer, read the whole history on one screen, see who viewed an ID, and keep preferences and notes on the profile.',
        'Aarav (Reception)', 'reception', menu='Customers')
step('You are Aarav on Modules Home. The Customers tile opens the page of customer profiles; with only one page in the module, the tile goes straight to it.', 'Customers 360 is the memory of the hotel: every stay, payment and request of one person.', target='sel:.fit-home-grid',
     auto=['key:Escape', 'user:reception'], action='read', expect='Modules Home.', check="ctx.uid==='reception'")
tile('Customers', 'Click the Customers tile.', 'The page keeps one profile per person, built from all stays.', expect='The Customers page opens with the list on the left.', check="ctx.page==='customers'")
read('The list shows each customer with the mobile number, the number of stays and the last stay. Badges say In house or Repeat; stays marked legacy come from the old Lodge software.', 'A returning guest can be greeted by name and given the room he liked.', target='sel:main h2', expect='The customer list.')
typ('field:Search', '9800000001', 'Type the mobile number 9800000001 in the search box.', 'Search takes a name, a mobile number or a vehicle number.', expect='Rahul Deshmukh is listed.')
click('btn:Rahul Deshmukh9800000001', 'Click Rahul Deshmukh.', 'His profile opens on the right.', expect='The profile opens on the Overview tab.', check="has('Accompanying guests')")
read('The Overview tab shows the contact details, the accompanying guest Mrs. Sneha Deshmukh, photos and the stay history. The tabs Stays, Legacy, Money and Notes & requests hold the detail.', 'Everything you need to talk to the guest is on one screen.', target='sel:main h2', expect='The profile overview.')
click('tab:Stays', 'Open the Stays tab.', 'Each stay shows the dates, rooms and booking number; a room move appears as 203 then 107.', expect='The stay HCC-1101 is listed.')
click('tab:Legacy', 'Open the Legacy tab.', 'Stays imported from the old Lodge software appear here; a new guest has none.', expect='The legacy list is empty for Rahul.')
click('tab:Money', 'Open the Money tab.', 'The bills of the guest are listed with their totals.', expect='The bill of the stay is listed.')
click('tab:Notes', 'Open the Notes & requests tab.', 'Guest requests such as the towels and the wake-up call, with their status, are kept on the profile.', expect='The guest requests are listed.')
step('Meera, the Manager, opens the profile and clicks Show ID. The ID can only be seen by roles that may.', 'Seeing an ID is a privacy event, so HCC logs every view.', target='sel:main h2',
     auto=['user:manager', 'click:menutop:Customers', 'type:field:Search|9800000001', 'click:btn:Rahul Deshmukh9800000001', 'click:btn:Show ID'], action='click', expect='The ID is shown and the view is logged.', msgText='ID shown — this view is logged')
click('btn:Edit profile', 'Click Edit profile to keep preferences and notes.', 'Preferences and notes travel with the guest to every future stay.', expect='The profile dialog opens.')
click('modalbtn:Edit profile', 'Click Edit profile inside the dialog.', 'The form opens with the saved values.', expect='The Edit Rahul Deshmukh form opens.', check="has('Save profile')")
step('Type the preferences: Quiet room, away from the lift, and the note: Business traveller, wants a GST invoice.', 'A good preference saves a complaint next time.', target='field:Preferences',
     auto=['type:field:Preferences|Quiet room, away from the lift', 'type:field:Notes|Business traveller, wants a GST invoice'], action='type', expect='Preferences and notes filled.')
click('modalbtn:Save profile', 'Click Save profile.', 'The profile is saved and the next booking shows the preferences.', expect='The profile is saved.')
click('tab:Foreigner register', 'Open the Foreigner register tab.', 'Hotels must report foreign guests; this register lists them for a date range and prints the form.', expect='The register opens for the last month.')
step('Open Guests under Front Desk. Profiles that look the same are flagged Possible duplicate; HCC never merges records by itself.', 'A person with two profiles splits their history; the flag lets reception clean it up.', target='sel:main h1',
     auto=['user:reception', 'menu:Front Desk>Guests', 'type:field:Search|Aarav Joshi'], action='click', menu='Front Desk > Guests', expect='Two Aarav Joshi profiles are listed with the flag.', check="ctx.page==='guests'")

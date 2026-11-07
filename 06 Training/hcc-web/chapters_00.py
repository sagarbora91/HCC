chapter(0, 'Welcome: Modules Home and the Dashboard', 'Find your way around HCC: the Modules Home tiles, the top bar, search, the menu, Appearance & display and the Dashboard.',
        'Aarav (Reception)', 'reception', menu='Modules Home')
read('This is Modules Home, the first screen after you choose your name. Each tile is one module; the red number on a tile is the work waiting there.',
     'Everything in HCC starts from these tiles, and you only see the modules your role may use.', target='sel:.fit-home-grid',
     expect='Nine module tiles for Reception: Front Desk, Cash & Billing, Housekeeping & Maintenance, Guests & Services, Customers, Revenue & Sales, People & Payroll, Accounts & Reports, Admin & Setup.',
     check="has('Welcome, Aarav')")
read('Look at the top bar. It shows the business date, whether the day is Open, the hotel clock, and who is signed in: Aarav, Reception.',
     'Every entry you make carries this business date and your name, so check them before you start work.', target='sel:.topbar-right .today',
     expect='Business date 24 Oct 2026, Open, 08:00 IST, Aarav · Reception.')
tile('Front Desk', 'Click the Front Desk tile to open that module.', 'A module page lists its screens as tiles; the number on the tile shows open work.',
     expect='The Front Desk page opens with tiles such as Room board, Calendar, Reservations, Guests, Arrivals & check-in and Room keys.',
     check="has('Arrivals & check-in')")
click('sel:.crumbs button', 'Look at the breadcrumb at the top left, Modules Home then Front Desk. Click Modules Home to go back.',
      'The breadcrumb always shows where you are, and every earlier part of it is a link.', expect='You are back on Modules Home.', check="has('Welcome, Aarav')")
press('ctrl+k', 'Press Control and K, or click the Search button in the top bar, to open the search box.',
      'Search is the quickest way to any screen, and it also offers quick actions: New reservation, Walk-in guest and Add expense.',
      target='sel:.search-box', expect='The search box opens with the three quick actions and the popular pages.', check="!!q('.search-box')")
typ('sel:.search-box input', 'rates', 'Type part of a name, for example rates. The list narrows as you type.',
    'You only see pages your role may open, so a search never leads to a screen you cannot use.', expect='Pages and actions that match the word.')
press('Escape', 'Press Escape to close the search box without choosing anything.', 'Escape closes any dialog in HCC and leaves your work as it was.',
      target='sel:.search-btn', expect='The search box is closed.', check="!q('.search-box')")
click('aria:Open menu', 'Click the menu button at the top left to open the side menu.',
      'The side menu lists every module and page in the same order as the tiles, and has Change my PIN and Sign out.', expect='The side menu opens with Modules Home, the module list, Demo & display, Change my PIN and Sign out.', check="!!q('.drawer')")
step('Close the menu, then open Admin & Setup, Appearance & display. Here each person sets text size, theme and start page for this device.',
     'These choices belong to this device only. They are not hotel data and they are not audited.', target='sel:.fit-page-head',
     auto=['key:Escape', 'menu:Admin & Setup>Appearance & display'], action='click', menu='Admin & Setup > Appearance & display',
     expect='The Appearance & display page opens: text size, theme, density, motion, start page, list size and message time.', check="has('Appearance') && !q('.drawer')")
step('Meera, the Manager, also sees a Dashboard tile that Reception does not have. Click Dashboard to see the day at a glance.',
     'The Dashboard gives the manager the day\'s occupancy, arrivals, cash and open work on one screen.', target='sel:.fit-page-head',
     auto=['user:manager', 'click:menutop:Dashboard'], action='click', menu='Dashboard',
     expect='The Dashboard page opens for Meera (Manager).', check="ctx.page==='overview'")

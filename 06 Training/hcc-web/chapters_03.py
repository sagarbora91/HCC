chapter(3, 'The Room board', 'Read the Room board: status colours, filters, floors, the room drawer, and how a manager blocks and unblocks a room.',
        'Aarav (Reception)', 'reception', menu='Front Desk > Room board')
read('You are Aarav on Modules Home. The Room board is the screen the front desk looks at most often.', 'It shows every room and its state right now.', target='sel:.fit-home-grid', expect='Modules Home.')
tile('Front Desk', 'Click the Front Desk tile.', 'The Room board is the first tile of the module.', expect='The Front Desk page opens.')
leaf('Front Desk', 'Room board', 'Click the Room board tile.', 'The board opens on all floors.', expect='The Room board opens.', check="ctx.page==='rooms'")
read('Every room is a card with its number, type, guest or housekeeping state and the date it is busy until. Rahul Deshmukh is in 203, Anita Rao in 105.',
     'You can tell at a glance who is in which room and when they leave.', target='tab:All floors', expect='The cards of all 24 rooms.')
read('The chips Occupied, Ready, Dirty, Cleaning, Inspection and Blocked filter the board. Ready means clean and sold-ready; Dirty needs cleaning; Inspection waits for the supervisor.',
     'Colours always come with a word, so the board is readable without colour.', target='btn:Dirty', expect='The status chips.')
click('btn:Dirty', 'Click the Dirty chip to see only rooms that need cleaning.', 'This is the list housekeeping works from.', expect='Only dirty rooms are shown.')
click('btn:All', 'Click All to see every room again.', 'Always return to All; an old filter hides rooms from the next person.', expect='All rooms are shown.')
click('tab:Floor 2', 'Click Floor 2 to see only the second floor.', 'The floor tabs help on a busy day.', expect='Only floor 2 rooms are shown.')
click('tab:All floors', 'Click All floors to see the whole hotel.', 'The tab shows the number of rooms on each floor.', expect='All floors are shown.')
click('btn:203Deluxe', 'Click room 203 to open its drawer.', 'The drawer shows who is in the room and what can be done from here.', expect='The Room 203 drawer opens with the guest Rahul Deshmukh.', check="has('Room 203')")
read('The drawer lists the guest and the stay, 24 Oct to 26 Oct, and a Report repair button. Reception cannot block a room; that is a manager\'s decision.',
     'A blocked room is removed from sale, so only people responsible for stock may do it.', target='dialog:', expect='The room drawer for 203.')
step('Meera, the Manager, opens room 108 on the Room board and clicks Block for repair. Room 108 is dirty and needs a deep clean.', 'A block takes the room out of sale until it is lifted.',
     target='field:Reason', auto=['key:Escape', 'user:manager', 'menu:Front Desk>Room board', 'click:btn:108Classic', 'click:modalbtn:Block for repair'], action='click',
     expect='The Block for repair form opens.', check="has('Block for repair')")
typ('field:Reason', 'Deep clean', 'Type the reason: Deep clean.', 'The reason is kept in the room history so everyone knows why the room is off sale.', expect='Reason filled; From is today.')
click('modalbtn:Block room', 'Click Block room.', 'From today the room cannot be booked or given to a guest.', expect='Room 108 is blocked.', msgText='Room blocked', check="has('Room blocked')")
step('When the deep clean is finished, open room 108 again and click Unblock room.', 'Unblocking returns the room to the normal housekeeping flow.', target='btn:Unblock room',
     auto=['key:Escape', 'click:btn:108Classic'], action='click', expect='The drawer for 108 shows Change block and Unblock room.')
click('modalbtn:Unblock room', 'Click Unblock room.', 'Housekeeping then cleans and inspects the room as usual.', expect='Room 108 is back in service.')

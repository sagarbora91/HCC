chapter(7, 'Cashier: shift, payments, cash book', 'Read the cashier shift, count the drawer, see the bank deposit and till transfer forms, take Ms. Anita Rao\'s cash payment and read the cash book.',
        'Priya (Cashier)', 'cashier', menu='Cash & Billing > Cashier shift')
read('You are Priya on Modules Home. The cashier works from the Cashier shift screen: it knows how much cash should be in the drawer.', 'The cashier answers for every rupee in the drawer, so the screen counts for you and asks you to count back.', target='sel:.fit-home-grid', expect='Modules Home.')
tile('Cash & Billing', 'Click the Cash & Billing tile.', 'The cashier shift is the second tile after the folios.', expect='The Cash & Billing page opens.')
leaf('Cash & Billing', 'Cashier shift', 'Click the Cashier shift tile.', 'There is one cash shift per business day.', expect='The Cashier shift page opens.', check="ctx.page==='cashier'")
read('Step 1 says the shift is Open: the expected cash is Rs 10,725 and the opening float carried from the last counted close is Rs 5,000. The five steps on the left take you from the expected cash to the credit guests.',
     'The float is carried from yesterday\'s counted close, so a shortage is never hidden in the opening.', target='btn:1. Expected cash', expect='Step 1, Expected cash.')
click('btn:3. Cash movements', 'Click 3. Cash movements to see what moved through the drawer today.', 'Every receipt, refund, restaurant cash bill, deposit and expense in cash is listed.', expect='The cash movements of the day are listed.')
click('btn:2. Count & result', 'Click 2. Count & result.', 'At the end of the shift the cashier counts the cash and enters the total.', expect='The count form opens.', check="has('Physical cash counted')")
typ('field:Physical cash counted', '10700', 'Type a count to see what happens: 10700.', 'HCC shows the difference at once, so the cashier can recount before closing.', expect='The Difference box shows the shortage of Rs 25.')
click('btn:Denominations', 'Click Denominations to count note by note.', 'Counting by notes helps when the total does not match.', expect='One box per note value appears.')
click('btn:Bank deposit', 'Click Bank deposit.', 'When cash is taken to the bank, the cashier records it here with the slip.', expect='The Bank deposit dialog opens.', check="has('Record bank deposit')")
read('The form asks for the amount, the bank and account, the deposit-slip number and a photo of the slip. The photo is required, so a deposit cannot be recorded without proof.', 'The manager and the accountant can open the slip photo later.', target='dialog:', expect='The bank deposit form.')
press('Escape', 'Press Escape; no deposit is made in the training.', 'Nothing is saved until you click Record bank deposit.', target='sel:main h1', expect='The shift page is shown.', check="!q('[role=dialog]')")
click('btn:Till transfer', 'Click Till transfer.', 'Cash can move between the front-desk drawer and the back-office cash, for example to give change.', expect='The Till transfer dialog opens.', check="has('Send to till')")
press('Escape', 'Press Escape.', 'A till transfer has an amount and a reason, and both tills record it.', target='sel:main h1', expect='The shift page is shown.', check="!q('[role=dialog]')")
step('Anita Rao pays her bill now, in cash. Open Guests & folios, search HCC-1115 and open her folio.', 'Cash is taken at the cashier, never by the room attendant.', target='btn:Record payment',
     auto=['menu:Cash & Billing>Guests & folios', 'type:field:Search|HCC-1115', 'click:btn:Ms. Anita RaoHCC-1115'], action='click', expect='The folio of Ms. Anita Rao opens.', check="has('Guest folio')")
click('btn:Record payment', 'Click Record payment.', 'A payment before check-out is an advance against the final bill.', expect='The payment dialog opens.', check="has('Entry type')")
typ('field:Amount', '2100', 'Type 2100: one night at Rs 2,000 plus 5 per cent GST.', 'The cashier takes the whole amount; any difference is settled at check-out.', expect='Amount 2100; the method is Cash.')
click('modalbtn:Review', 'Click Review.', 'Review shows the balance after posting.', expect='The summary of the receipt is shown.', check="has('Post receipt')")
click('modalbtn:Post receipt', 'Click Post receipt.', 'The cash receipt is added to the drawer total at once.', expect='The receipt is recorded.', msgText='Receipt recorded once')
step('Open the Cash book under Cash & Billing. It shows the day\'s cash and bank money by account.', 'The cash book is the daily record the accountant checks against the bank.', target='sel:main h1',
     auto=['key:Escape', 'menu:Cash & Billing>Cash book'], action='click', menu='Cash & Billing > Cash book', expect='The Cash book opens with the drawer and the hotel current account.', check="ctx.page==='cashbook'")
read('Each block lists the receipts and payments of one account: the front-desk cash drawer, the hotel current account and money not settled through an account. CSV and Excel buttons export the day.',
     'The book follows the same numbers as the cashier shift and the day close.', target='sel:main h3', expect='The cash book blocks.')

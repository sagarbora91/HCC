OPEN = lambda bid, name: ['key:Escape', 'user:reception', 'menu:Front Desk>Reservations', 'type:field:Search guest|' + bid, 'click:btn:Open ' + name]
chapter(8, 'Check-out, invoice, credit note, counter bills', 'Check out Ms. Anita Rao and the company guest Mr. Vikram Jadhav, take the card payment of Mr. Rahul Deshmukh, check him out, read the GST invoice and issue a counter bill.',
        'Priya (Cashier)', 'cashier', menu='Guests & Services > In-house stays')
step('It is now 25 October. The night audit and the day close ran at the end of 24 October (chapter 19 shows them in detail), so room nights are posted and Anita Rao and Vikram Jadhav are due out.',
     'A guest can only be checked out once every agreed room night is posted by the night audit.', target='sel:.fit-home-grid', auto=['closeday'], action='read', expect='Modules Home on 25 Oct 2026.', check="ctx.s.date==='2026-10-25'")
step('The wake-up call of the guest and the reminder from chapter 4 are due. Meera, the Manager, clicks Done on both banners at the top.', 'A banner stays on every screen until somebody marks it Done, so nobody forgets a call.',
     target='sel:.dk-alerts', auto=['user:manager', 'click:btnin:Wake-up|Done', 'click:btnin:Collect ID copy|Done'], action='click', expect='The banners are gone.')
step('Aarav opens the booking of Ms. Anita Rao, HCC-1115, from Reservations. She paid Rs 2,100 in cash, so the balance is zero.', 'Check-out starts from the booking dialog.', target='dialog:',
     auto=OPEN('HCC-1115', 'Ms. Anita Rao'), action='click', expect='The booking dialog of Anita Rao opens.', check="has('Anita Rao') && !!q('[role=dialog]')")
click('modalbtn:Checkout', 'Click Checkout.', 'The dialog reviews the folio before the room is released.', expect='The Ready to say goodbye? dialog opens.', check="has('Ready to say goodbye')")
read('The dialog shows the room nights posted against agreed, the total charges, the payments, the balance, the deposit to return and any pending approvals. It is blocked while anything is open. For Anita everything is clear.',
     'Check-out cannot finish while there is a balance, a deposit or an approval waiting.', target='dialog:', expect='The check-out review for Anita Rao.')
click('modalbtn:Confirm checkout', 'Click Confirm checkout.', 'The room goes to housekeeping as dirty and the invoice number is allocated once.', expect='Anita Rao is checked out.', msgText='Checkout completed. Housekeeping task created.', check="has('Sample invoice')")
click('modalbtn:Sample invoice', 'Click the invoice button.', 'The invoice is created at check-out and cannot be created again.', expect='The tax invoice opens.', check="has('Tax invoice')")
read('The tax invoice shows the guest, the invoice number, and every line with its GST rate, CGST and SGST. In this demo it is stamped as a sample, and the number series is DEMO.', 'Rooms up to Rs 7,500 a night carry 5 per cent GST; other services 18 per cent; CGST and SGST are always equal halves.',
     target='dialog:', expect='The tax invoice of Anita Rao.')
step('Mr. Vikram Jadhav of Shree Agro Industries is due out. Open his booking HCC-1107 and click Checkout.', 'A company guest does not pay at the desk.', target='dialog:',
     auto=OPEN('HCC-1107', 'Mr. Vikram Jadhav') + ['click:modalbtn:Checkout'], action='click', expect='The check-out review shows a balance of Rs 4,725 and the company credit box.', check="has('Company credit')")
read('The review shows the balance Rs 4,725 and a box Company credit, Shree Agro Industries: Rs 4,725 used of Rs 50,000, Rs 45,275 left. The balance moves to the company\'s receivable, and needs approval: a Manager up to Rs 10,000, the Owner above.',
     'The credit limit and the approval protect the hotel from unpaid company bills.', target='text:Company credit', expect='The company credit box.')
click('modalbtn:Request company credit', 'Click Request company credit for Rs 4,725.', 'The request goes to the manager; the guest need not wait at the desk.', expect='The request is sent for approval.')
step('Meera opens Approvals & audit, clicks the request of Vikram Jadhav and reads it.', 'The manager sees the amount and the company before agreeing.', target='btn:Approve',
     auto=['key:Escape', 'user:manager', 'menu:Admin & Setup>Approvals & audit', 'click:btn:Mr. Vikram Jadhav'], action='click', expect='The request opens with Approve and Reject.', check="has('Approve')")
click('btn:Approve', 'Click Approve.', 'The amount is now a receivable of Shree Agro Industries and appears in Accounting.', expect='The request is approved.')
step('Aarav opens the booking again and clicks Checkout.', 'With the approval in place the check-out is no longer blocked.', target='btn:Confirm checkout',
     auto=OPEN('HCC-1107', 'Mr. Vikram Jadhav') + ['click:modalbtn:Checkout'], action='click', expect='The review shows the balance moved to the company.', check="has('Confirm checkout')")
click('modalbtn:Confirm checkout', 'Click Confirm checkout.', 'The guest leaves; the company account owes Rs 4,725.', expect='Vikram Jadhav is checked out.', msgText='Checkout completed. Housekeeping task created.')
step('Next morning, 26 October, Rahul Deshmukh leaves. The night audit of 25 October has run.', 'Both room nights of his stay are now posted.', target='sel:.fit-home-grid', auto=['key:Escape', 'closeday', 'user:cashier'], action='read',
     expect='Modules Home on 26 Oct 2026.', check="ctx.s.date==='2026-10-26'")
step('Priya opens Guests & folios, searches HCC-1101 and opens the folio of Rahul Deshmukh.', 'The folio shows what he owes after all charges.', target='sel:main h3',
     auto=['menu:Cash & Billing>Guests & folios', 'type:field:Search|HCC-1101', 'click:btn:Rahul DeshmukhHCC-1101'], action='click', expect='The folio of Rahul Deshmukh opens.', check="has('Guest folio')")
read('The folio lists the room nights at 5 per cent, the laundry and courier at 18 per cent, the approved service-recovery discount, the room-service dinner and the breakfast plan. The advance of Rs 2,000 is deducted. The balance is what Rahul pays now.',
     'Always read the balance to the guest before taking the money.', target='sel:main h3', expect='The final folio with the balance.')
click('btn:Record payment', 'Click Record payment for the balance.', 'The balance is paid by card, so the receipt carries the card reference.', expect='The payment dialog opens.', check="has('Entry type')")
pick('field:Method', 'Card', 'Choose Card.', 'The method decides the cashier total the money joins.', expect='Method Card.')
step('The amount field takes the whole balance of the folio. Type it exactly as the folio shows it.', 'Paying the exact balance leaves nothing to settle later.', target='field:Amount',
     auto=['typebal:field:Amount|HCC-1101'], action='type', expect='The balance is filled in.')
typ('field:Reference', 'CARD-DEMO-8101', 'Type the card slip reference.', 'The reference lets the accountant match the card settlement.', expect='Reference filled.')
click('modalbtn:Review', 'Click Review.', 'Review shows that the balance after posting is zero.', expect='The receipt summary shows a zero balance.', check="has('Post receipt')")
click('modalbtn:Post receipt', 'Click Post receipt.', 'The folio is now fully paid.', expect='The receipt is recorded.', msgText='Receipt recorded once')
step('Aarav opens the booking and clicks Checkout. Nothing blocks it now.', 'Reception releases the room.', target='dialog:', auto=OPEN('HCC-1101', 'Rahul Deshmukh') + ['click:modalbtn:Checkout'], action='click',
     expect='The review shows a zero balance.', check="has('Confirm checkout')")
click('modalbtn:Confirm checkout', 'Click Confirm checkout.', 'Room 107 goes to housekeeping.', expect='Rahul Deshmukh is checked out and the invoice is allocated.', msgText='Checkout completed. Housekeeping task created.', check="has('Sample invoice')")
click('modalbtn:Sample invoice', 'Click the invoice button.', 'The invoice sums every line with the GST rate that applied on the day.', expect='The tax invoice opens.', check="has('Tax invoice')")
read('Rooms and food at 5 per cent, laundry and courier at 18 per cent, the discount as a negative line. Print or save it as a PDF for the guest. A duplicate is printed from the same screen and is marked as a duplicate.', 'The guest takes the invoice with him; the same number is never issued twice.',
     target='dialog:', expect='The invoice of Rahul Deshmukh.')
click('modalbtn:Back to folio', 'Click Back to folio.', 'The folio of a departed guest stays open for corrections.', expect='The folio of the departed guest is shown.')
click('modalbtn:Request credit note', 'Click Request credit note. Suppose the guest was charged Rs 100 too much.', 'After the invoice is issued only a credit note can reduce it, and only the Owner approves it.', expect='The credit note dialog opens.', check="has('Submit credit note request')")
step('Type the credit amount 100 before GST and the reason: Courier charged twice.', 'The reason goes on the credit note and into the audit log.', target='field:Reason', auto=['type:field:Credit amount|100', 'type:field:Reason|Courier charged twice'], action='type', expect='Amount and reason filled.')
click('modalbtn:Submit credit note request', 'Click Submit credit note request.', 'The request waits for the Owner; the invoice does not change yet.', expect='The credit note request is submitted.')
step('Sagar, the Owner, opens Approvals & audit and opens the request.', 'Only the Owner can approve a credit note.', target='btn:Approve', auto=['key:Escape', 'user:owner', 'menu:Admin & Setup>Approvals & audit', 'click:btn:Rahul Deshmukh'], action='click',
     expect='The credit note request opens with Approve and Reject.', check="has('Approve')")
click('btn:Approve', 'Click Approve.', 'The credit note is issued with its own number against the invoice.', expect='The credit note is approved.')
step('Now a counter bill. Open Counter bills under Cash & Billing. Anita Rao\'s friend wants to pay for a lunch.', 'A counter bill is for people who are not staying in the hotel.', target='sel:main h1',
     auto=['user:cashier', 'menu:Cash & Billing>Counter bills'], action='click', menu='Cash & Billing > Counter bills', expect='The Counter bills page opens on New bill.', check="ctx.page==='counter-bills'")
step('Type the customer Mr. Sanjay Rao, choose Free text, describe the item as Lunch for two at 600 rupees with the GST type food.', 'Food is taxed at 5 per cent, laundry at 18 per cent; the GST type decides the rate.', target='field:Item',
     auto=['type:field:Customer name|Mr. Sanjay Rao', 'select:field:From catalogue|Free text', 'type:field:Item|Lunch for two', 'type:field:Unit price|600', 'select:field:GST|food'], action='type', expect='The line is filled.')
click('btn:Add line', 'Click Add line.', 'A bill can have many lines; each keeps its own GST rate.', expect='The line is added to the bill.')
click('btn:Issue counter bill', 'Settlement is Paid now, by cash. Click Issue counter bill.', 'The bill gets its own number and the cash joins the drawer total at once.', expect='The counter bill is issued.')
step('Open the Bills tab to see the bills issued. Choose Udhaar credit when the customer will pay later.', 'Udhaar is credit given to a customer; it is a receivable until the customer pays and appears in the cashier shift and the accounts.', target='tab:Bills', auto=['click:tab:Bills'], action='click',
     expect='The list of counter bills opens.')
step('Open Approvals & audit under Admin & Setup to see all requests in one place.', 'Approvals are how managers and owners keep control without standing at the desk.', target='sel:main h1',
     auto=['user:manager', 'menu:Admin & Setup>Approvals & audit'], action='click', menu='Admin & Setup > Approvals & audit', expect='The Approvals inbox opens with the tabs Pending, Approved, Rejected, Expired, Invalidated and Executed.', check="ctx.page==='approvals'")
click('tab:Audit log', 'Open the Audit log tab.', 'Every approval, void and credit note leaves a line with the user and the time; the log cannot be edited.', expect='The audit log opens.')

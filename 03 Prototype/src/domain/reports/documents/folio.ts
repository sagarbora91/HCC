// F7-R7 · folio, tax invoice, credit note, receipt and quotation documents (prototype). Figures are the folio's own (totals / gstLineSplit / gstStayTax);
// nothing is re-derived. Viewed with the folio rights ('billing' or 'payments'; 'reservations' for the quotation). Housekeeping / maintenance / stock roles never.
// Invoice number only if issued; the QR text is a plain string (hotel | number | date | total) - no IRN (the hotel is below the e-invoice threshold, DEC-197).
import type {State,Booking} from '../../types';
import {HccError} from '../../errors';
import {money,nights} from '../../money';
import {totals} from '../../selectors';
import {gstLineSplit,gstSplitLines,gstStayTax} from '../../gst';
import {bookingRef} from '../../bookingRef';
import {folioReceiptLabel} from '../../streams/folio';
import {hrAmountWords} from '../../streams/staff';
import {type DocModel,T,R,canAny,docRequire} from './model';

const FOLIO_PERMS=['billing','payments'] as const;
const find=(s:State,id:string):Booking=>{const b=s.bookings.find(x=>x.id===id||x.publicNo===id);if(!b)throw new HccError('NOT_FOUND','No such booking.');return b;};
const prod=()=>process.env.HCC_BUILD==='production';
const qr=(s:State,no:string,date:string,total:number)=>`HCC|${s.settings.hotel}|${no}|${date}|${(total/100).toFixed(2)}`;

/** Folio summary (no invoice number yet) or the tax invoice of a checked-out stay. */
export function folioDocument(s:State,uid:string,bookingId:string,kind:'folio'|'invoice'='folio'):DocModel{
  docRequire(s,uid,canAny(s,uid,[...FOLIO_PERMS]),kind==='invoice'?'tax invoice':'folio');
  const b=find(s,bookingId),t=totals(b),sp=gstSplitLines(b.lines),inv=!!b.invoiceNo&&kind==='invoice';
  const lines=b.lines.map(l=>{const g=gstLineSplit(l);return R({desc:l.label,date:l.date,net:l.amount,rate:l.taxBps,cgst:g.cgst,sgst:g.sgst,total:l.amount+g.tax});});
  lines.push(R({desc:'Total',date:null,net:t.net,rate:null,cgst:sp.cgst,sgst:sp.sgst,total:t.total},'total'));
  const credit=b.receipts.filter(r=>r.kind==='company-credit').reduce((a,r)=>a+r.amount,0);
  const sum=[R({item:'Net charges',amount:t.net}),R({item:'CGST',amount:sp.cgst}),R({item:'SGST',amount:sp.sgst}),R({item:'Total including GST',amount:t.total},'total'),R({item:'Payments received (net)',amount:t.paid}),
    ...(credit?[R({item:'On company credit (receivable)',amount:credit})]:[]),R({item:'Balance',amount:t.balance},'total'),R({item:'Refundable deposit held separately',amount:t.deposit})];
  const pairs:[string,string][]=[['Guest',b.guest],['Room',b.room],['Booking',bookingRef(b)],['Arrival',b.arrival],['Departure',b.departure],['Nights',String(nights(b.arrival,b.departure))],
    ['Bill to',b.company?.name??b.guest],['Buyer GSTIN',b.company?.gstin??''],[inv?'Tax invoice no':'Invoice no',b.invoiceNo??'Not yet issued']];
  const cn=(b.creditNotes??[]).map(c=>`Credit note ${c.no} · ${c.date} · ${money(c.amount)} before tax · ${c.reason}`);
  return {id:inv?'doc.invoice':'doc.folio',title:inv?'Tax invoice':'Guest folio',number:inv?b.invoiceNo!:bookingRef(b),property:s.settings.hotel,pairs,money:true,
    tables:[{key:'lines',title:'Charges',columns:[T('desc','Description'),T('date','Date','date'),T('net','Net','money'),T('rate','GST rate','pct'),T('cgst','CGST','money'),T('sgst','SGST','money'),T('total','Total','money')],rows:lines},
      {key:'summary',title:'Summary',columns:[T('item','Item'),T('amount','Amount','money')],rows:sum}],
    notes:[...cn,prod()?'GST from the dated GST master, intra-state (CGST + SGST), per line, half away from zero. The issued tax invoice is the hotel server’s document.':'Demonstration - not a tax invoice. GST from the dated GST master, intra-state (CGST + SGST); illustrative, CA to confirm.'],
    sign:['Guest signature','Reception'],basis:'Charges by service date, receipts by receipt date.',...(inv?{qrText:qr(s,b.invoiceNo!,b.departure,t.total)}:{})};
}

/** Credit note `creditNoteId` (the note's id or number) of a booking's tax invoice. */
export function creditNoteDocument(s:State,uid:string,bookingId:string,creditNoteId:string):DocModel{
  docRequire(s,uid,canAny(s,uid,[...FOLIO_PERMS]),'credit note');
  const b=find(s,bookingId),c=(b.creditNotes??[]).find(x=>x.id===creditNoteId||x.no===creditNoteId);
  if(!c)throw new HccError('NOT_FOUND','No such credit note.');
  const l=b.lines.find(x=>x.id===c.lineId),g=gstLineSplit({amount:c.amount,taxBps:l?.taxBps??0});
  return {id:'doc.credit-note',title:'Credit note',number:c.no,property:s.settings.hotel,money:true,
    pairs:[['Credit note no',c.no],['Date',c.date],['Against invoice',b.invoiceNo??''],['Guest',b.guest],['Bill to',b.company?.name??b.guest],['Buyer GSTIN',b.company?.gstin??''],['Reason',c.reason]],
    tables:[{key:'lines',title:'Credited',columns:[T('desc','Description'),T('net','Before GST','money'),T('rate','GST rate','pct'),T('cgst','CGST','money'),T('sgst','SGST','money'),T('total','Credit total','money')],
      rows:[R({desc:l?.label??c.reason,net:c.amount,rate:l?.taxBps??0,cgst:g.cgst,sgst:g.sgst,total:c.amount+g.tax},'total')]}],
    notes:[prod()?'Issued against the tax invoice shown; GST reversed at the rate of the original line.':'Demonstration credit note. GST reversed at the rate of the original line; illustrative, CA to confirm.'],
    sign:['Authorised signatory'],basis:'Credit note date.',qrText:qr(s,c.no,c.date,c.amount+g.tax)};
}

/** One folio receipt (payment, advance, deposit, refund ...). */
export function receiptDocument(s:State,uid:string,bookingId:string,receiptId:string):DocModel{
  docRequire(s,uid,canAny(s,uid,[...FOLIO_PERMS]),'receipt');
  const b=find(s,bookingId),r=b.receipts.find(x=>x.id===receiptId);
  if(!r)throw new HccError('NOT_FOUND','No such receipt.');
  const refund=r.kind==='refund'||r.kind==='deposit-return';
  return {id:'doc.receipt',title:refund?'Payment voucher':'Receipt',number:r.id,property:s.settings.hotel,money:true,
    pairs:[['Receipt no',r.id],['Date',r.date],[refund?'Paid to':'Received from',b.guest],['Booking',bookingRef(b)],['Room',b.room],['Towards',folioReceiptLabel[r.kind]??r.kind],['Mode',r.method],['Reference',r.reference],['Amount',money(r.amount)],['In words',hrAmountWords(r.amount)]],
    tables:[],notes:['Subject to realisation of cheque or card payment. A tax invoice is issued at check-out.'],sign:['Guest','Cashier'],basis:'Receipt date.'};
}

/** Quotation / booking confirmation of a stay: nights x agreed rate, GST of the dated master, advance. */
export function quotationDocument(s:State,uid:string,bookingId:string):DocModel{
  docRequire(s,uid,canAny(s,uid,['reservations','billing']),'quotation');
  const b=find(s,bookingId),n=nights(b.arrival,b.departure),tax=gstStayTax(s,b.rate,b.arrival,b.departure),net=b.rate*n,paid=totals(b).paid;
  return {id:'doc.quotation',title:'Quotation',number:bookingRef(b),property:s.settings.hotel,money:true,
    pairs:[['Guest',b.guest],['Phone',b.phone??''],['Room',b.room],['Arrival',b.arrival],['Departure',b.departure],['Nights',String(n)],['Guests',String(b.people)],['Source',b.source],['Check-in from',s.settings.checkin],['Check-out by',s.settings.checkout]],
    tables:[{key:'quote',title:'Stay charges',columns:[T('desc','Description'),T('qty','Nights','int'),T('rate','Rate per night','money'),T('net','Before GST','money'),T('gst','GST','money'),T('total','Total','money')],
      rows:[R({desc:`Room ${b.room}`,qty:n,rate:b.rate,net,gst:tax,total:net+tax},'total')]},
      {key:'terms',title:'Payment',columns:[T('item','Item'),T('amount','Amount','money')],rows:[R({item:'Advance received',amount:paid}),R({item:'Balance on this quote',amount:net+tax-paid},'total')]}],
    notes:['A quotation, not a tax invoice. GST applies per the dated GST master on each night (illustrative, CA to confirm).'+(b.notes?` Notes: ${b.notes}`:'')],sign:['Guest','Reception'],basis:'Stay dates.'};
}

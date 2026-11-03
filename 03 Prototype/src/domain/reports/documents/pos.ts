// F7-R7 · restaurant documents: the kitchen order ticket (no money at all - safe for the kitchen roles) and the outlet bill / tax invoice (money).
// Figures are posOrderTotals / the stored settlement (RST invoice number, CGST / SGST as stored). Viewed with the POS page right ('billing').
import type {State} from '../../types';
import {HccError} from '../../errors';
import {posOrder,posOutlet,posSpot,posKotLabel,posActiveLines,posLineValue,posOrderTotals,posBillSplit} from '../../streams/pos';
import {gstHalfPct} from '../../gst';
import {type DocModel,T,R,userName,canAny,docRequire} from './model';

const order=(s:State,id:string)=>{const o=posOrder(s,id);if(!o)throw new HccError('NOT_FOUND','No such check.');return o;};
/** KOT `kotId` of a check. No money column anywhere; the kitchen and the restaurant role may print it. */
export function kotDocument(s:State,uid:string,orderId:string,kotId:string):DocModel{
  const o=order(s,orderId),k=o.kots.find(x=>x.id===kotId);if(!k)throw new HccError('NOT_FOUND','No such KOT.');
  const u=s.users.find(x=>x.id===uid);if(!u||!(canAny(s,uid,['billing','payments'])||u.role==='Restaurant'||u.role==='Manager'))throw new HccError('FORBIDDEN','You cannot print kitchen tickets.');
  const out=posOutlet(s,o.outlet);
  return {id:'doc.kot',title:'Kitchen order ticket',number:posKotLabel(s,o,k),property:s.settings.hotel,money:false,
    pairs:[['Outlet',out?.name??o.outlet],['Table / room',posSpot(o)],['Check',o.id],['Time',k.at],['By',userName(s,k.by)],['Covers',String(o.covers)],['Status',k.status]],
    tables:[{key:'items',columns:[T('qty','Qty','int'),T('item','Item')],rows:o.lines.filter(l=>l.kot===k.id).map(l=>R({qty:l.qty,item:l.name+(l.voided?' - VOID':'')}))}],
    notes:[],sign:[],basis:'KOT time.'};
}
/** The outlet bill (pro-forma before settlement, the RST tax invoice after). */
export function outletBillDocument(s:State,uid:string,orderId:string):DocModel{
  docRequire(s,uid,canAny(s,uid,['billing','payments']),'restaurant bill');
  const o=order(s,orderId),out=posOutlet(s,o.outlet),t=posOrderTotals(s,o),st=o.settlement,inv=st?.invoiceNo,split=st?posBillSplit(st):undefined;
  const rows=posActiveLines(o).map(l=>R({item:l.name,qty:l.qty,rate:l.price,amount:posLineValue(l)}));
  const sum=[R({item:inv?'Taxable value':'Net',amount:t.net}),...(inv&&split?[R({item:`CGST @${gstHalfPct(t.taxBps)}`,amount:split.cgst}),R({item:`SGST @${gstHalfPct(t.taxBps)}`,amount:split.sgst})]:[R({item:`GST ${t.taxBps/100}%`,amount:t.tax})]),R({item:'Total',amount:t.total},'total')];
  const pairs:[string,string][]=[['Outlet',out?.name??o.outlet],['Table / room',posSpot(o)],['Check',o.id],['Guest',o.guest??''],['Date',st?.date??s.date],['Covers',String(o.covers)],[inv?'Tax invoice no':'Bill',inv??(st?st.billNo:'Pro-forma (not a bill)')],
    ...(inv?[['SAC','996332'],['Hotel GSTIN',st!.hotelGstin??''],['Buyer',st!.buyer?.name??''],['Buyer GSTIN',st!.buyer?.gstin??''],['Supply',st!.supply??'B2C']] as [string,string][]:[]),
    ...(st?[['Settled',st.mode==='folio'?`Charged to room folio ${st.booking}`:`Paid by ${st.method}${st.reference?' · '+st.reference:''}`] as [string,string]]:[])];
  return {id:'doc.outlet-bill',title:inv?'Tax invoice':'Outlet bill',number:inv??(st?.billNo??o.id),property:s.settings.hotel,money:true,pairs,
    tables:[{key:'items',title:'Items',columns:[T('item','Item'),T('qty','Qty','int'),T('rate','Rate','money'),T('amount','Amount','money')],rows},{key:'sum',title:'Total',columns:[T('item','Item'),T('amount','Amount','money')],rows:sum}],
    notes:[st?.creditNoteNo?`Cancelled: reversed in full by credit note ${st.creditNoteNo}.`:inv?'GST from the dated GST master, intra-state (CGST + SGST); CA to confirm.':'Not a tax invoice.'],sign:[],basis:'Bill date.',
    ...(inv?{qrText:`HCC|${s.settings.hotel}|${inv}|${st!.date}|${(t.total/100).toFixed(2)}`}:{})};
}

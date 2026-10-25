// W05-8 · Cash book → Excel through the W05-E xlsx helper: the same figures as the CSV (cbRange / cbDay), numbers as numbers, money as ₹ cells.
import {buildXlsx,XSheet,XCell} from '../../app/xlsx';
import type {State} from '../../domain/types';
import {cbRange,CB_DIFF_LABEL} from '../../domain/streams/cashbook';

const m=(p:number|undefined):XCell=>p===undefined?'':{paise:p};
export function cbSheets(s:State,from:string,to:string):XSheet[]{
  const r=cbRange(s,from,to);
  const about:XSheet={name:'About',widths:[34,60],rows:[['Item','Value'],['Report','Hotel Control Centre — DEMONSTRATION · fictional data · Cash book'],['Property',s.settings.hotel],
    ['From (business date)',{date:from}],['To (business date)',{date:to}],['Generated (simulated clock)',s.clock],
    ['Opening',m(r.opening)],['Receipts',m(r.receipts)],['Payments (incl. banking)',m(r.payments)],[CB_DIFF_LABEL,m(r.differences)],
    ...(r.gaps?[['Opening differs from previous carry',m(r.gaps)] as XCell[]]:[]),['Closing (carried forward)',m(r.closing)]]};
  const days:XSheet={name:'Days',widths:[12,18,14,14,16,14,14,14,14,14,12],rows:[['Date','Status','Opening','Receipts','Payments (incl. banking)','Closing (book)','Expected','Counted','Over/short','Carried forward','Locked by'],
    ...r.days.map(d=>[{date:d.date},d.status+(d.locked?' · locked':''),m(d.opening),m(d.receipts),m(d.payments),m(d.closing),m(d.expected),m(d.counted),m(d.difference),m(d.carried),d.locked?.lockedBy??''] as XCell[])]};
  const rows:XCell[][]=[['Date','Reference','Entry','Detail','Booking','Guest','Receipt','Payment','Balance','By']];
  for(const d of r.days){
    let bal=d.opening;rows.push([{date:d.date},'','Opening balance','','','','','',m(bal),'']);
    for(const e of d.entries){bal+=e.in-e.out;rows.push([{date:d.date},e.id,e.label,e.reference,e.booking??'',e.guest??'',e.in?m(e.in):'',e.out?m(e.out):'',m(bal),e.actor]);}
    rows.push([{date:d.date},'','Closing balance','','','',m(d.receipts),m(d.payments),m(d.closing),'']);
    const x=d.carried-d.closing;if(x)rows.push([{date:d.date},'',CB_DIFF_LABEL,'Cashier count','','',x>0?m(x):'',x<0?m(-x):'',m(d.carried),'']);
    if(d.counted!==undefined)rows.push([{date:d.date},'','Carried forward to next day','','','','','',m(d.carried),'']);
  }
  return [about,days,{name:'Entries',widths:[12,16,34,30,12,20,12,12,12,12],rows}];
}
export const cbXlsx=(s:State,from:string,to:string)=>buildXlsx(cbSheets(s,from,to));

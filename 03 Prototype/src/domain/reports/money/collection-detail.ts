// F7-R4 · rpt.money.collection-detail: every receipt with guest, bill, tender, TDS and entered-by (the receipts register). Adapter over the same
// records as the closing pack's receipts tab (reportReceipts) plus counter-bill receipts and credit settlements (company payments, Udhaar payments).
// Include: receipts = payments + counter bills + credit settlements; advances = deposits, deposit returns and applied advances; corrections = refunds.
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import {reportReceipts} from '../../streams/closing';
import {grpSlice} from '../../streams/groups';
import {ctrState,ctrCreditDate} from '../../streams/counter';
import {col,userName,rangeParams,seesAll,rowOf} from './common';

export const COLLECT_COLS=[col('date','Date','date'),col('receiptNo','Receipt no','text'),col('guest','Guest','text',false,{mask:'guestName'}),col('room','Room','text'),
  col('billNo','Bill no','text'),col('tender','Tender','text'),col('amount','Amount','money',true),col('tds','TDS','money',true),col('by','Entered by','text'),col('source','Source','text')];
export const COLLECT_TENDER_COLS=[col('tender','Tender','text'),col('count','Receipts','int'),col('amount','Amount','money',true),col('tds','TDS','money',true)];
export type CollectLine={date:string;receiptNo:string;guest:string;room:string;billNo:string;tender:string;amount:number;tds:number;by:string;source:'stay'|'counter bill'|'credit settlement';inc:'receipts'|'advances'|'corrections'};
const INC:Record<string,CollectLine['inc']>={payment:'receipts','company-credit':'receipts','ota-credit':'receipts','guest-credit':'receipts',deposit:'advances','deposit-return':'advances','deposit-apply':'advances',refund:'corrections'};

/** Every collection line dated in the range, before the report filters (date, then record order). The tie-out target is reportReceipts. */
export function collectionLines(s:State,from:string,to:string):CollectLine[]{
  const out:CollectLine[]=[];
  for(const {b,r} of reportReceipts(s,from,to)){
    // company credit / OTA credit / guest credit are credit raised, not money received (credit-bills report); they stay out of the receipts register.
    if(r.kind==='company-credit'||r.kind==='ota-credit'||r.kind==='guest-credit')continue;
    out.push({date:r.date,receiptNo:r.id,guest:b.guest,room:b.room,billNo:b.invoiceNo??b.id,tender:r.method,
      amount:r.kind==='refund'||r.kind==='deposit-return'?-r.amount:r.amount,tds:0,by:r.actor,source:'stay',inc:INC[r.kind]??'receipts'});
  }
  for(const b of ctrState(s).bills){
    if(b.settlement.mode!=='paid')continue;
    if(b.date>=from&&b.date<=to)out.push({date:b.date,receiptNo:b.settlement.receiptId??b.no,guest:b.party.name,room:'',billNo:b.no,tender:b.settlement.method??'',amount:b.total,tds:0,by:b.by,source:'counter bill',inc:'receipts'});
    const cd=ctrCreditDate(s,b);
    if(cd&&cd>=from&&cd<=to)out.push({date:cd,receiptNo:b.creditNoteNo??b.no,guest:b.party.name,room:'',billNo:b.no,tender:b.settlement.method??'',amount:-b.total,tds:0,by:b.by,source:'counter bill',inc:'corrections'});
  }
  const g=grpSlice(s);
  for(const p of g.payments??[]){
    if(p.date<from||p.date>to)continue;
    out.push({date:p.date,receiptNo:p.id,guest:g.companies.find(c=>c.id===p.company)?.name??p.company,room:'',billNo:p.invoice??'',tender:p.method,amount:p.amount,tds:p.tds??0,by:p.actor,source:'credit settlement',inc:'receipts'});
  }
  for(const r of s.expensesX?.udhaar??[])for(const x of r.settlements){
    if(x.date<from||x.date>to)continue;
    out.push({date:x.date,receiptNo:x.id,guest:r.name,room:'',billNo:r.booking??r.counterBill??r.id,tender:x.mode,amount:x.amount,tds:0,by:x.by,source:'credit settlement',inc:'receipts'});
  }
  return out.sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:0);
}

export const collectionDetail=registerReport({
  id:'rpt.money.collection-detail',title:'Collection detail (receipts register)',area:'money',priority:'P1',status:'built',moneyFlag:true,moneyGate:'PERM-RPT-002',
  viewPerms:['PERM-RPT-002'],ownShift:true,dateClass:'D-B',
  params:[...rangeParams('D-B'),{key:'tender',label:'Tender',type:'text'},{key:'groupBy',label:'Group by',type:'enum',options:['tender','bill','user'],default:'tender'},
    {key:'include',label:'Include',type:'enum',options:['receipts','advances','corrections'],default:'receipts'}],
  columns:COLLECT_COLS,dateBasis:'Receipts by receipt (business) date. Refunds and deposit returns are negative. Credit raised on a company or guest account is not a receipt (see Credit bills); money received against that credit is a credit settlement.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,uid,p)=>{
    const all=seesAll(s,uid),inc=(p.include as string)||'receipts',gb=(p.groupBy as string)||'tender';
    const lines=collectionLines(s,p.from!,p.to!).filter(l=>l.inc===inc&&(all||l.by===uid)&&(!p.tender||l.tender===p.tender));
    const key=(l:CollectLine)=>gb==='bill'?l.billNo||'(no bill)':gb==='user'?userName(s,l.by):l.tender;
    const keys=[...new Set(lines.map(key))].sort();
    const rows=keys.flatMap(k=>{
      const g=lines.filter(l=>key(l)===k);
      return [...g.map(l=>rowOf('row',{date:l.date,receiptNo:l.receiptNo,guest:l.guest,room:l.room,billNo:l.billNo,tender:l.tender,amount:l.amount,tds:l.tds,by:userName(s,l.by),source:l.source},k)),
        rowOf('subtotal',{date:null,receiptNo:'Total '+k,guest:null,room:null,billNo:null,tender:null,amount:g.reduce((a,l)=>a+l.amount,0),tds:g.reduce((a,l)=>a+l.tds,0),by:null,source:null},k)];
    });
    if(rows.length)rows.push(rowOf('total',{date:null,receiptNo:'Grand total',guest:null,room:null,billNo:null,tender:null,amount:lines.reduce((a,l)=>a+l.amount,0),tds:lines.reduce((a,l)=>a+l.tds,0),by:null,source:null}));
    const tenders=[...new Set(lines.map(l=>l.tender))].sort();
    const summary=tenders.map(t=>{const g=lines.filter(l=>l.tender===t);return rowOf('row',{tender:t,count:g.length,amount:g.reduce((a,l)=>a+l.amount,0),tds:g.reduce((a,l)=>a+l.tds,0)});});
    const sections=[{key:'receipts',title:'Receipts',columns:COLLECT_COLS,rows},{key:'tenders',title:'Summary by tender',columns:COLLECT_TENDER_COLS,rows:summary}];
    return {params:p,columns:COLLECT_COLS,rows,sections};
  },
});

// F7-R4 · rpt.money.credit-bills: bills charged to a company or credit guest and not paid on the spot. Adapter over the company-credit and
// guest-credit receipts of stays and the company counter bills; "used after this bill" = the account's open balance after the bill (company:
// opening outstanding + credit given up to it - company payments up to that date; guest: open Udhaar receivables on that date).
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import {reportReceipts} from '../../streams/closing';
import {grpSlice,type GRPCompany} from '../../streams/groups';
import {ctrState} from '../../streams/counter';
import {col,rangeParams,rowOf} from './common';

export const CREDIT_COLS=[col('date','Date','date'),col('billNo','Bill no','text'),col('party','Company or guest','text',false,{mask:'guestName'}),col('room','Room','text'),
  col('credit','Credit amount','money',true),col('limit','Credit limit','money',true),col('used','Used after this bill','money',true)];
export type CreditLine={date:string;order:number;billNo:string;party:string;room:string;credit:number;limit:number;used:number;account:string};

const companyOf=(s:State,id?:string,name?:string):GRPCompany|undefined=>{const g=grpSlice(s).companies;return g.find(c=>c.id===id)??g.find(c=>c.name===name);};
/** Credit raised in [from, to] with the account's balance after each bill (only the lines in range are returned; balances use all earlier history). */
export function creditLines(s:State,from:string,to:string):CreditLine[]{
  const g=grpSlice(s),all:Omit<CreditLine,'used'>[]=[];let n=0;
  for(const {b,r} of reportReceipts(s,'0000-01-01','9999-12-31')){
    if(r.kind!=='company-credit'&&r.kind!=='guest-credit')continue;
    const c=r.kind==='company-credit'?companyOf(s,b.company?.account,b.company?.name):undefined;
    const gu=r.kind==='guest-credit'?(s.guests??[]).find(x=>x.name===b.guest):undefined;
    all.push({date:r.date,order:n++,billNo:b.invoiceNo??b.id,party:r.kind==='company-credit'?(c?.name??b.company?.name??b.guest):b.guest,room:b.room,credit:r.amount,
      limit:r.kind==='company-credit'?(c?.creditLimit??0):(gu?.creditAccount?.limit??0),account:r.kind==='company-credit'?'C:'+(c?.id??b.company?.name):'G:'+(gu?.id??b.guest)});
  }
  for(const b of ctrState(s).bills){
    if(b.settlement.mode!=='company'||b.status!=='Issued')continue;
    const c=companyOf(s,b.settlement.companyId,b.party.name);
    all.push({date:b.date,order:n++,billNo:b.no,party:c?.name??b.party.name,room:'',credit:b.total,limit:c?.creditLimit??0,account:'C:'+(c?.id??b.party.name)});
  }
  all.sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.order-b.order);
  const running=new Map<string,number>();
  const used=(l:Omit<CreditLine,'used'>):number=>{
    const before=running.get(l.account)??0,after=before+l.credit;running.set(l.account,after);
    if(l.account.startsWith('C:')){
      const c=g.companies.find(x=>'C:'+x.id===l.account);
      const paid=(g.payments??[]).filter(p=>c&&p.company===c.id&&p.date<=l.date).reduce((a,p)=>a+p.amount+(p.tds??0),0);
      return Math.max(0,(c?.openingOutstanding??0)+after-paid);
    }
    return Math.max(0,after-(s.expensesX?.udhaar??[]).filter(r=>!r.void&&(r.guestId&&'G:'+r.guestId===l.account)).reduce((a,r)=>a+r.settlements.filter(x=>x.date<=l.date).reduce((x,y)=>x+y.amount,0),0));
  };
  return all.map(l=>({...l,used:used(l)})).filter(l=>l.date>=from&&l.date<=to);
}

export const creditBills=registerReport({
  id:'rpt.money.credit-bills',title:'Credit bills of the day',area:'money',priority:'P1',status:'built',moneyFlag:true,moneyGate:'PERM-RPT-002',
  viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:[...rangeParams('D-B'),{key:'party',label:'Party',type:'text'}],
  columns:CREDIT_COLS,dateBasis:'Credit raised by business date: company credit and guest credit given at check-out, and company counter bills. Used after this bill = the account\'s open balance after the bill. Outstanding and ageing are in the accounting ageing report.',
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_uid,p)=>{
    const q=String(p.party??'').trim().toLowerCase();
    const lines=creditLines(s,p.from!,p.to!).filter(l=>!q||l.party.toLowerCase().includes(q));
    const rows=lines.map(l=>rowOf('row',{date:l.date,billNo:l.billNo,party:l.party,room:l.room,credit:l.credit,limit:l.limit,used:l.used}));
    if(rows.length)rows.push(rowOf('total',{date:null,billNo:'Total credit raised',party:null,room:null,credit:lines.reduce((a,l)=>a+l.credit,0),limit:null,used:null}));
    return {params:p,columns:CREDIT_COLS,rows};
  },
});

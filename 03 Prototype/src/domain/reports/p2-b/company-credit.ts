// F8-RPT-P2-B · rpt.money.company-credit: credit limit, used (DEC-076 exposure) and remaining per company; with a company chosen, its statement (opening
// outstanding, company credit given, payments incl. TDS, running balance) as a second block. Partial -> built on the engine, adapting grpCredit (no new figure).
// Split stays (SF-CONTRACT R8): the company-credit receipt of a split stay belongs to the company folio (I5), so the statement counts exactly that receipt;
// the exposure is the D7 figure of grpCredit (sf-domain). Server twin: Hcc.Reporting/Reports/P2B/CompanyCreditReport.cs.
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import {grpSlice,grpCredit,grpCompanyBookings,GRP_CREDIT_LABEL} from '../../streams/groups';
import './grants';
import {col,rowOf} from '../money/common';

export const CC_COLS=[col('company','Company','text'),col('contact','Contact','text'),col('rates','Negotiated rates','text'),col('limit','Credit limit','money',true),
  col('used','Used','money',true),col('remaining','Remaining','money',true),col('status','Credit status','text')];
export const CCS_COLS2=[col('date','Date','date'),col('ref','Bill or reference','text'),col('narration','Narration','text'),col('credit','Credit given','money',true),
  col('payment','Payment received','money',true),col('balance','Balance','money',true)];
export const ratesText=(r:Record<string,number>)=>Object.keys(r).sort().map(k=>`${k} ${(r[k]/100).toFixed(2)}`).join('; ');
export function companyStatement(s:State,id:string){
  const c=grpSlice(s).companies.find(x=>x.id===id);if(!c)return [];
  const ev:{date:string;ord:number;ref:string;narration:string;credit:number;payment:number}[]=[];
  for(const b of grpCompanyBookings(s,c))for(const r of b.receipts)if(r.kind==='company-credit')ev.push({date:r.date,ord:0,ref:(r.folio?b.folios?.find(f=>f.id===r.folio)?.invoiceNo:undefined)??b.invoiceNo??b.id /* F8-INT (L1) */,narration:'Company credit · '+b.guest,credit:r.amount,payment:0});
  for(const p of grpSlice(s).payments??[])if(p.company===c.id)ev.push({date:p.date,ord:1,ref:p.reference||p.id,narration:'Payment · '+p.method+(p.tds?' (incl. TDS)':''),credit:0,payment:p.amount+(p.tds??0)});
  ev.sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.ord-b.ord||(a.ref<b.ref?-1:a.ref>b.ref?1:0));
  let run=c.openingOutstanding;
  const rows=[rowOf('subtotal',{date:null,ref:null,narration:'Opening outstanding',credit:null,payment:null,balance:run})];
  for(const e of ev){run+=e.credit-e.payment;rows.push(rowOf('row',{date:e.date,ref:e.ref,narration:e.narration,credit:e.credit||null,payment:e.payment||null,balance:run}));}
  rows.push(rowOf('total',{date:null,ref:null,narration:'Closing outstanding',credit:ev.reduce((a,e)=>a+e.credit,0),payment:ev.reduce((a,e)=>a+e.payment,0),balance:run}));
  return rows;
}
export const companyCredit=registerReport({
  id:'rpt.money.company-credit',title:'Company credit and statement',area:'money',priority:'P2',status:'built',moneyFlag:true,moneyGate:'PERM-GRP-004',viewPerms:['PERM-GRP-001'],dateClass:'D-NOW',
  params:[{key:'company',label:'Company',type:'party'}],columns:CC_COLS,
  dateBasis:'The state now (no history). One row per company: limit, used (receivable plus open stays billed to it, DEC-076) and remaining. With a company chosen, its statement: opening outstanding, company credit given, payments received (TDS included) and the running balance.',
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_u,p)=>{
    const q=String(p.company??'').trim();
    const list=grpSlice(s).companies.filter(c=>c.active!==false&&(!q||c.id===q||c.name===q)).sort((a,b)=>a.name<b.name?-1:a.name>b.name?1:0);
    const rows=list.map(c=>{const k=grpCredit(s,c);return rowOf('row',{company:c.name,contact:[c.contact,c.phone].filter(Boolean).join(' · '),rates:ratesText(c.rates),limit:c.creditLimit,used:k.exposure,remaining:k.available,status:GRP_CREDIT_LABEL[k.level]});});
    const sections=[{key:'companies',columns:CC_COLS,rows}];
    if(q&&list.length===1)sections.push({key:'statement',title:'Statement · '+list[0].name,columns:CCS_COLS2,rows:companyStatement(s,list[0].id)} as never);
    return {params:p,columns:CC_COLS,rows,...(sections.length>1?{sections}:{})};
  },
});

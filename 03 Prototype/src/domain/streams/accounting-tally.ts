// ACC-3 · lane acc3-tally · Tally XML masters (per financial year) and vouchers (per range ≤ 31 days) — contract 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §4.
// Mirrors the server byte for byte (Hcc.Domain/Accounting/VoucherExport.Tally.cs): same envelope, order, names, bill allocations, indentation (one space),
// CRLF, no trailing text after the final CRLF. The XmlWriter of the server is imitated by `render` below; a test diffs both on the same input
// (tests/stream-accounting-tally.mjs, Golden/tally-april-2026.input.json). Pure selectors; money = integer paise; names start with 'acc' / 'ACC'.
import type {State} from '../types';
import {ACC_TALLY_ID_NOTE,accDates,accTallyPendingReason,accVouchersUpTo,type ACCVoucher} from './accounting';
import {accChart,accGroups} from './accounting-chart';
import {accPartyLedgers} from './accounting-parties';
import {accOpeningPending} from './accounting-opening';
import {accRptIsOpening} from './accounting-reports';

// ── Neutral inputs (what the server's TallyBook / TallyVouchersXml take) ──
export type TallyGroupIn={id:string;name:string;parent?:string;nature:'Dr'|'Cr';side:'BS'|'PL';custom?:boolean};
export type TallyLedgerIn={code:string;name:string;tallyName?:string;group:string};
export type TallyChartIn={groups:TallyGroupIn[];ledgers:TallyLedgerIn[]};
export type TallyPartyIn={name:string;gstin?:string};
export type TallyVoucherIn={no:string;date:string;type:string;sub:string;sourceId:string;narration:string;entries:{account:string;debit:number;credit:number}[]};
export type TallyGroupMaster={name:string;parent:string};
export type TallyLedgerMaster={id:string;name:string;group:string;billWise:boolean;openingNet:number;gstin?:string;nature:'Dr'|'Cr';party:boolean};
export type TallyBook={groups:TallyGroupMaster[];ledgers:TallyLedgerMaster[];profitCarryNet:number};
export type TallyBill={name:string;type:'New Ref'|'Agst Ref'|'On Account';net:number};

export const TALLY_COMMENT='SIMULATED EXPORT · Hotel Control Centre · nothing was sent to Tally · saving this file does not mean it was imported';
export const TALLY_OPENING_BILL='Opening balance';
export const TALLY_MASTERS_NOTE='MASTERS · import this file into the Tally company first (Import › Masters), then the vouchers files. Tally\'s own 28 groups are not created;'
  +' ledger opening balances use Tally\'s sign (credit positive, debit negative) as at the start of the financial year; party ledgers under Sundry Debtors / Creditors are bill-wise,'
  +' their opening kept as one bill named "Opening balance". The opening journal of HCC is not in the vouchers files (it would count twice).';

// ── XML imitation of System.Xml.XmlWriter (Indent, one-space IndentChars, CRLF) ──
type Node={name:string;attrs?:[string,string][];text?:string;kids?:Node[]};
export const tallyClean=(t?:string)=>(t??'').replace(/[\u0000-\u001F￾￿]/g,' ');
const escText=(t:string)=>t.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
const escAttr=(t:string)=>escText(t).replace(/"/g,'&quot;');
const el=(name:string,text:string):Node=>({name,text:tallyClean(text)});
const box=(name:string,kids:Node[],attrs?:[string,string][]):Node=>({name,kids,attrs});
function draw(n:Node,depth:number):string{
  const pad=' '.repeat(depth),at=(n.attrs??[]).map(([k,v])=>` ${k}="${escAttr(tallyClean(v))}"`).join('');
  if(n.kids&&n.kids.length)return `${pad}<${n.name}${at}>\r\n${n.kids.map(k=>draw(k,depth+1)).join('\r\n')}\r\n${pad}</${n.name}>`;
  return `${pad}<${n.name}${at}>${n.kids?'':escText(n.text??'')}</${n.name}>`;
}
const commentText=(t:string)=>' '+tallyClean(t).split('--').join('- -').trim()+' ';
const render=(comments:string[],root:Node)=>'<?xml version="1.0" encoding="utf-8"?>\r\n'+comments.map(c=>`<!--${commentText(c)}-->\r\n`).join('')+draw(root,0)+'\r\n';
const envelope=(report:string,company:string|undefined,messages:Node[]):Node=>box('ENVELOPE',[
  box('HEADER',[el('TALLYREQUEST','Import Data')]),
  box('BODY',[box('IMPORTDATA',[
    box('REQUESTDESC',[el('REPORTNAME',report),box('STATICVARIABLES',company&&company.trim()?[el('SVCURRENTCOMPANY',company)]:[])]),
    box('REQUESTDATA',messages)])])]);
const message=(body:Node):Node=>box('TALLYMESSAGE',[body],[['xmlns:UDF','TallyUDF']]);
const nameList=(name:string)=>box('NAME.LIST',[el('NAME',name)]);
const td=(d:string)=>d.split('-').join('');
export function tallyAmount(net:number):string{const a=Math.abs(net),t=`${Math.trunc(a/100)}.${String(a%100).padStart(2,'0')}`;return net>0?'-'+t:t;}
const dayBefore=(d:string)=>new Date(Date.parse(d+'T00:00:00Z')-86400000).toISOString().slice(0,10);
const ord=(a:string,b:string)=>a<b?-1:a>b?1:0;
const ctl=(id:string)=>{const i=id.indexOf('/');return i<0?id:id.slice(0,i);};
const partyOf=(id:string)=>{const i=id.indexOf('/');return i<0?undefined:id.slice(i+1);};
const isParty=(id:string)=>/^\d{4}\/.+$/.test(id);

// ── Masters ──
export function tallyBuildBook(chart:TallyChartIn,opening:Record<string,number>,parties:Record<string,TallyPartyIn>,seen:string[]):TallyBook{
  const grp=(id?:string)=>chart.groups.find(g=>g.id===id);
  const depth=(g:TallyGroupIn)=>{let d=0,p=g;while(p.parent&&d<32){const n=grp(p.parent);if(!n)break;p=n;d++;}return d;};
  const root=(id?:string)=>{let g=grp(id);for(let i=0;g?.parent&&i<32;i++)g=grp(g.parent);return g&&!g.parent?g:undefined;};
  const under=(id:string,anc:string)=>{let g=grp(id);for(let i=0;g&&i<32;i++){if(g.id===anc)return true;g=grp(g.parent);}return false;};
  const groups=chart.groups.filter(g=>g.custom).sort((a,b)=>depth(a)-depth(b)||ord(a.id,b.id))
    .map(g=>({name:tallyClean(g.name),parent:g.parent?tallyClean(grp(g.parent)?.name??g.parent):''}));
  const byCode=new Map(chart.ledgers.map(l=>[l.code,l]));
  const ids=new Set<string>(chart.ledgers.map(l=>l.code));
  for(const id of [...Object.keys(parties),...Object.keys(opening),...seen])if(id.length)ids.add(id);
  const ordered=[...ids].sort((a,b)=>ord(ctl(a),ctl(b))||(+isParty(a))-(+isParty(b))||ord(partyOf(a)??'',partyOf(b)??''));
  const used=new Set<string>();
  const unique=(name:string)=>{let n=name;for(let k=2;used.has(n.toLowerCase());k++)n=`${name} (${k})`;used.add(n.toLowerCase());return n;};
  const names=new Map<string,string>();
  for(const id of ordered.filter(i=>!isParty(i))){const l=byCode.get(id);names.set(id,unique(tallyClean(l?(l.tallyName&&l.tallyName.trim()?l.tallyName:l.name):id)));}
  for(const id of ordered.filter(isParty)){const p=parties[id];names.set(id,unique(tallyClean(p&&p.name.trim()?p.name:partyOf(id)!)));}
  let carry=0;const ledgers:TallyLedgerMaster[]=[];
  for(const id of ordered){
    const c=byCode.get(ctl(id)),gid=c?.group,r=root(gid),group=grp(gid)?.name??'Suspense A/c',nature=(r?.nature??'Dr') as 'Dr'|'Cr',party=isParty(id);
    let net=opening[id]??0;
    if(r&&r.side==='PL'){carry+=net;net=0;}
    const billWise=party&&!!gid&&(under(gid,'debtors')||under(gid,'creditors'));
    const g=party&&parties[id]?.gstin?.trim()?parties[id].gstin!.trim():undefined;
    ledgers.push({id,name:names.get(id)!,group:tallyClean(group),billWise,openingNet:net,gstin:g,nature,party});
  }
  return {groups,ledgers,profitCarryNet:carry};
}
export function tallyMastersXml(book:TallyBook,company:string|undefined,fyStart:string,notes:string[]=[]):string{
  const msgs:Node[]=[];
  for(const g of book.groups)msgs.push(message(box('GROUP',[nameList(g.name),el('PARENT',g.parent),el('ISSUBLEDGER','No')],[['NAME',g.name],['ACTION','Create']])));
  for(const l of book.ledgers){
    const k:Node[]=[nameList(l.name),el('PARENT',l.group),el('ISBILLWISEON',l.billWise?'Yes':'No')];
    if(l.openingNet!==0)k.push(el('OPENINGBALANCE',tallyAmount(l.openingNet)));
    if(l.gstin){k.push(el('GSTREGISTRATIONTYPE','Regular'),el('PARTYGSTIN',l.gstin));}
    if(l.billWise&&l.openingNet!==0)k.push(box('BILLALLOCATIONS.LIST',[el('NAME',TALLY_OPENING_BILL),el('BILLDATE',td(dayBefore(fyStart))),el('OPENINGBALANCE',tallyAmount(l.openingNet))]));
    msgs.push(message(box('LEDGER',k,[['NAME',l.name],['ACTION','Create']])));
  }
  if(book.profitCarryNet!==0)msgs.push(message(box('LEDGER',[nameList('Profit & Loss A/c'),el('OPENINGBALANCE',tallyAmount(book.profitCarryNet))],[['NAME','Profit & Loss A/c'],['ACTION','Alter']])));
  return render([TALLY_COMMENT,TALLY_MASTERS_NOTE,...notes],envelope('All Masters',company,msgs));
}

// ── Voucher type mapping (server VoucherExport.TallyVoucherType) ──
export function tallyVoucherType(v:TallyVoucherIn,chart:TallyChartIn):string{
  if(v.sub==='credit-note')return 'Credit Note';
  switch(v.type){
    case 'Receipt':return 'Receipt';case 'Payment':return 'Payment';case 'Contra':return 'Contra';case 'Sales':return 'Sales';
    case 'Purchase':return v.sub==='return'?'Debit Note':'Purchase';
    default:{
      const grp=(id?:string)=>chart.groups.find(g=>g.id===id);
      const under=(id:string,anc:string)=>{let g=grp(id);for(let i=0;g&&i<32;i++){if(g.id===anc)return true;g=grp(g.parent);}return false;};
      const cashBank=(a:string)=>{const g=chart.ledgers.find(l=>l.code===ctl(a))?.group;return !!g&&(under(g,'cash')||under(g,'bank'));};
      return v.entries.length>=2&&v.entries.every(e=>cashBank(e.account))?'Contra':'Journal';
    }
  }
}

// ── Bill allocations (server TallyBills) ──
export class TallyBills{
  private open=new Map<string,{name:string;left:number}[]>();
  constructor(private book:TallyBook){
    for(const l of book.ledgers.filter(l=>l.billWise&&l.openingNet!==0)){
      const nat=l.nature==='Dr'?1:-1;if(l.openingNet*nat>0)this.open.set(l.id,[{name:TALLY_OPENING_BILL,left:Math.abs(l.openingNet)}]);
    }
  }
  allocate(v:TallyVoucherIn):TallyBill[][]{
    const out:TallyBill[][]=[],names=new Map<string,number>();
    for(const e of v.entries){
      const net=e.debit-e.credit,l=this.book.ledgers.find(x=>x.id===e.account);
      if(net===0||!l||!l.billWise){out.push([]);continue;}
      const side=net>0?1:-1,nat=l.nature==='Dr'?1:-1,list:TallyBill[]=[];
      if(side===nat){
        const k=(names.get(l.id)??0)+1;names.set(l.id,k);const name=k===1?v.no:`${v.no}/${k}`;
        list.push({name,type:'New Ref',net});
        const b=this.open.get(l.id)??[];b.push({name,left:Math.abs(net)});this.open.set(l.id,b);
      }else{
        let need=Math.abs(net);const b=this.open.get(l.id);
        if(b){
          for(let i=0;i<b.length&&need>0;i++){const take=Math.min(need,b[i].left);list.push({name:b[i].name,type:'Agst Ref',net:side*take});b[i].left-=take;need-=take;}
          this.open.set(l.id,b.filter(x=>x.left!==0));
        }
        if(need>0)list.push({name:v.no,type:'On Account',net:side*need});
      }
      out.push(list);
    }
    return out;
  }
}
export const tallyRemoteId=(identity:string,sourceKey:string)=>`HCC-${identity}-${sourceKey}`;
export function tallyVouchersXml(stream:TallyVoucherIn[],emit:Set<string>,book:TallyBook,chart:TallyChartIn,company:string|undefined,identity:string,notes:string[]=[]):string{
  const written=stream.filter(v=>emit.has(v.sourceId)),ids=written.map(v=>tallyRemoteId(identity,v.sourceId));
  if(new Set(ids).size!==ids.length)throw new Error('Two vouchers in one Tally export share a source key; every REMOTEID must be unique.');
  const bills=new TallyBills(book),msgs:Node[]=[];let n=0;
  for(const v of stream){
    const alloc=bills.allocate(v);if(!emit.has(v.sourceId))continue;
    const id=ids[n++],type=tallyVoucherType(v,chart);
    const k:Node[]=[el('DATE',td(v.date)),el('GUID',id),el('VOUCHERTYPENAME',type),el('VOUCHERNUMBER',v.no),el('NARRATION',v.narration),el('PERSISTEDVIEW','Accounting Voucher View'),el('EFFECTIVEDATE',td(v.date))];
    v.entries.forEach((e,j)=>{
      const net=e.debit-e.credit,led=book.ledgers.find(x=>x.id===e.account),cl=chart.ledgers.find(l=>l.code===ctl(e.account));
      const sub:Node[]=[el('LEDGERNAME',led?.name??(cl?(cl.tallyName&&cl.tallyName.trim()?cl.tallyName:cl.name):ctl(e.account))),
        el('ISDEEMEDPOSITIVE',net>0?'Yes':'No'),el('AMOUNT',tallyAmount(net))];
      for(const a of alloc[j])sub.push(box('BILLALLOCATIONS.LIST',[el('NAME',a.name),el('BILLTYPE',a.type),el('AMOUNT',tallyAmount(a.net))]));
      k.push(box('ALLLEDGERENTRIES.LIST',sub));
    });
    msgs.push(message(box('VOUCHER',k,[['REMOTEID',id],['VCHTYPE',type],['ACTION','Create'],['OBJVIEW','Accounting Voucher View']])));
  }
  return render([TALLY_COMMENT,ACC_TALLY_ID_NOTE.slice(4,-3).trim(),...notes],envelope('Vouchers',company,msgs));
}

// ── State selectors (prototype) ──
export type TallyFile={xml:string;fileName:string;bytes:number;error?:string};
export type TallyMastersResult=TallyFile&{fy:string;from:string;to:string;groups:number;ledgers:number;partyLedgers:number;provisional:boolean;notes:string[]};
export type TallyVouchersResult=TallyFile&{from:string;to:string;count:number;debit:number;credit:number;dates:number;provisional:string[];reexport:string[]};
export const ACC_TALLY_FY_RE=/^(\d{4})(?:-(\d{2}))?$/;
/** Financial year 'YYYY' or 'YYYY-YY' (April to March) → its start and end dates; undefined when not valid. */
export function accTallyFy(fy:string):{start:string;end:string;label:string}|undefined{
  const m=ACC_TALLY_FY_RE.exec(String(fy??'').trim());if(!m)return undefined;const y=+m[1];
  if(m[2]&&(y+1)%100!==+m[2])return undefined;
  return {start:`${y}-04-01`,end:`${y+1}-03-31`,label:`${y}-${String(y+1).slice(2)}`};
}
const fyStartOf=(d:string)=>{const y=+d.slice(0,4),m=+d.slice(5,7);return `${m>=4?y:y-1}-04-01`;};
const toIn=(v:ACCVoucher):TallyVoucherIn=>({no:v.no,date:v.date,type:v.type,sub:v.source.sub,sourceId:v.source.id,narration:v.narration,entries:v.entries.map(e=>({account:e.account,debit:e.debit,credit:e.credit}))});
export function accTallyChartIn(s:State):TallyChartIn{
  return {groups:accGroups(s).map(g=>({id:g.id,name:g.name,parent:g.parent,nature:g.nature,side:g.side,custom:g.custom})),
    ledgers:accChart(s).map(l=>({code:l.code,name:l.name,tallyName:l.tallyName,group:l.group}))};
}
function partiesOf(s:State):Record<string,TallyPartyIn>{
  const o:Record<string,TallyPartyIn>={};
  const comp=new Map(((s.groupsX?.companies??[]) as {id:string;gstin?:string}[]).map(c=>[c.id,c.gstin]));
  const ven=new Map(((s as unknown as {moneyX?:{vendors?:{code:string;gstin?:string}[]}}).moneyX?.vendors??[]).map(v=>[v.code,v.gstin]));
  for(const p of accPartyLedgers(s)){
    const g=p.control==='1110'?comp.get(p.partyId):p.control==='2200'?ven.get(p.partyId):undefined;
    o[p.id]={name:p.name,gstin:g};
  }
  return o;
}
function bookFor(s:State,fyStart:string,seenVouchers:ACCVoucher[]){
  const opening:Record<string,number>={};
  for(const v of accVouchersUpTo(s,fyStart)){
    if(v.date>fyStart||(v.date===fyStart&&!accRptIsOpening(v)))continue;
    for(const e of v.entries)opening[e.account]=(opening[e.account]??0)+e.debit-e.credit;
  }
  const chart=accTallyChartIn(s);
  return {chart,book:tallyBuildBook(chart,opening,partiesOf(s),seenVouchers.flatMap(v=>v.entries.map(e=>e.account)))};
}
const company=(s:State,c?:string)=>c??s.settings.hotel;
const bytes=(x:string)=>new TextEncoder().encode(x).length;
/** Masters file of a financial year: `fy` = 'YYYY' (start year) or 'YYYY-YY'. Opening = every voucher before the FY start plus the opening journal dated on it. */
export function accTallyMasters(s:State,fy:string,companyName?:string):TallyMastersResult{
  const f=accTallyFy(fy);
  const empty={xml:'',fileName:'',bytes:0,fy,from:'',to:'',groups:0,ledgers:0,partyLedgers:0,provisional:false,notes:[] as string[]};
  if(!f)return {...empty,error:'Choose a financial year such as 2026 or 2026-27.'};
  const {chart,book}=bookFor(s,f.start,[]);void chart;
  const pending=accOpeningPending(s);
  const notes=pending?['PROVISIONAL · no approved opening entry yet, so the opening balances in this file may be incomplete; export the masters again once the opening entry is approved.']:[];
  const xml=tallyMastersXml(book,company(s,companyName),f.start,notes);
  return {xml,fileName:`HCC-tally-masters-SIMULATED-FY${f.label}.xml`,bytes:bytes(xml),fy:f.label,from:f.start,to:f.end,groups:book.groups.length,ledgers:book.ledgers.length,
    partyLedgers:book.ledgers.filter(l=>l.party).length,provisional:pending,notes};
}
/** Vouchers file of a range (≤ 31 days, dates with activity only; the opening journal never). */
export function accTallyVouchers(s:State,from:string,to:string,companyName?:string,identity?:string):TallyVouchersResult{
  const empty={xml:'',fileName:'',bytes:0,from,to,count:0,debit:0,credit:0,dates:0,provisional:[] as string[],reexport:[] as string[]};
  const real=(d:string)=>/^\d{4}-\d{2}-\d{2}$/.test(d)&&new Date(d+'T00:00:00Z').toISOString().slice(0,10)===d;
  if(!real(from)||!real(to)||from>to)return {...empty,error:'Choose a valid range: "from" on or before "to".'};
  if(Math.round((Date.parse(to)-Date.parse(from))/86400000)+1>31)return {...empty,error:'Choose at most 31 days.'};
  const start=fyStartOf(from),cmp=(x:ACCVoucher,y:ACCVoucher)=>ord(x.date,y.date)||ord(x.no,y.no);
  const stream=accVouchersUpTo(s,to).filter(v=>!accRptIsOpening(v)&&v.date>=start).sort(cmp),period=stream.filter(v=>v.date>=from&&v.date<=to);
  const {chart,book}=bookFor(s,start,stream);
  const tag=identity??'DEMO';
  const xml=tallyVouchersXml(stream.map(toIn),new Set(period.map(v=>v.source.id)),book,chart,company(s,companyName),tag);
  const debit=period.reduce((a,v)=>a+v.entries.reduce((x,e)=>x+e.debit,0),0),credit=period.reduce((a,v)=>a+v.entries.reduce((x,e)=>x+e.credit,0),0);
  void accDates;void accTallyPendingReason;
  return {xml,fileName:`HCC-tally-vouchers-SIMULATED-${from}_${to}.xml`,bytes:bytes(xml),from,to,count:period.length,debit,credit,dates:new Set(period.map(v=>v.date)).size,provisional:[],reexport:[]};
}
/** The neutral input of a masters + vouchers pair (golden test fixture): chart, opening by ledger id, parties, the FY's vouchers up to the range end and those written. */
export type TallyGoldenInput={company:string;identity:string;fyStart:string;from:string;to:string;chart:TallyChartIn;opening:Record<string,number>;
  parties:Record<string,TallyPartyIn>;seen:string[];stream:TallyVoucherIn[];emit:string[]};
export function accTallyGoldenInput(s:State,from:string,to:string,companyName:string,identity:string):TallyGoldenInput{
  const fyStart=fyStartOf(from),cmp=(a:ACCVoucher,b:ACCVoucher)=>ord(a.date,b.date)||ord(a.no,b.no);
  const stream=accVouchersUpTo(s,to).filter(v=>!accRptIsOpening(v)&&v.date>=fyStart).sort(cmp);
  const opening:Record<string,number>={};
  for(const v of accVouchersUpTo(s,fyStart)){
    if(v.date>fyStart||(v.date===fyStart&&!accRptIsOpening(v)))continue;
    for(const e of v.entries)opening[e.account]=(opening[e.account]??0)+e.debit-e.credit;
  }
  const seen=[...new Set(stream.flatMap(v=>v.entries.map(e=>e.account)))].sort(ord);
  return {company:companyName,identity,fyStart,from,to,chart:accTallyChartIn(s),opening:Object.fromEntries(Object.entries(opening).filter(([,n])=>n!==0).sort(([a],[b])=>ord(a,b))),
    parties:Object.fromEntries(Object.entries(partiesOf(s)).sort(([a],[b])=>ord(a,b))),seen,stream:stream.map(toIn),emit:stream.filter(v=>v.date>=from&&v.date<=to).map(v=>v.source.id)};
}

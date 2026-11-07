// F12 MEAS · meas-study-a · rpt.study.zero-rate-complimentary and rpt.study.receivables-bad-debt (workbooks 8-9).
// DEC-266: live rows use AnStay.complimentary (the explicit complimentary type is not built on main, so this is the clearly named zero-rate flag:
// complimentary OR a zero room charge OR a zero stay rate). DEC-265: every legacy receivable is a bad debt, never an opening receivable.
// C# twin: Analytics/LegacyZero.cs.
import type {AnalyticsSelector} from './types';
import {T,I,M,P,row,tbl,series,NOTE,result,fyOf,monthOf,MON_LABEL,pct1,rdiv,sum,fyRows,billStay,roomType,sortedUnique,focusFy,slept,nightsOf} from './legacy-core';

const fyLabel=(fy:string)=>'FY '+fy;
const monthLabel=(m:string)=>MON_LABEL[m.slice(5)]+' '+m.slice(0,4);

export const zeroRateComplimentary:AnalyticsSelector=(i,p)=>{
  const fy=focusFy(i,p),st=billStay(i),fys=fyRows(i);
  const billOf=new Map<string,typeof i.bills[number]>();for(const b of i.bills)billOf.set(b.stay,b);
  type Ev={stay:typeof i.stays[number];bill?:typeof i.bills[number];cat:string;date:string;expected:number;short:number};
  const evs:Ev[]=[];
  for(const s of i.stays){
    if(!slept(s))continue;const b=billOf.get(s.id);const zeroBill=!!b&&b.roomChargePaise===0,zeroRate=s.ratePaise===0;
    if(!(s.complimentary||zeroBill||zeroRate))continue;
    const exp=s.ratePaise*nightsOf(s);
    evs.push({stay:s,bill:b,cat:s.complimentary?'Complimentary':zeroBill?'Zero room charge':'Zero rate',date:b?.date??s.to,expected:exp,short:b?Math.max(0,exp-b.roomChargePaise):0});
  }
  const cats=['Complimentary','Zero room charge','Zero rate'];
  const inFy=evs.filter(e=>fyOf(e.date)===fy);
  const changed=i.bills.filter(b=>{const s=st.get(b.stay);return !!s&&b.chargedNights>0&&s.ratePaise>0&&rdiv(b.roomChargePaise,b.chargedNights)!==s.ratePaise;});
  const rc=tbl('rate-changes-by-fy','Rate changes by FY (billed rate differs from the stay rate)',[T('fy','FY'),I('stays','Stays billed'),I('changed','Stays with a rate change'),P('share','Share %')],
    fys.map(f=>{const n=i.bills.filter(b=>fyOf(b.date)===f.fy).length;const c=changed.filter(b=>fyOf(b.date)===f.fy).length;return row({fy:fyLabel(f.fy),stays:n,changed:c,share:pct1(c,n)});}));
  const events=tbl('zero-events',`Zero-rate events (${fyLabel(fy)})`,[T('stay','Stay'),T('date','Date'),T('room','Room'),T('category','Category'),I('nights','Nights'),M('expected','Expected room charge'),M('billed','Room charge billed'),M('received','Received')],
    inFy.map(e=>row({stay:e.stay.id,date:e.date,room:e.stay.room,category:e.cat,nights:nightsOf(e.stay),expected:e.expected,billed:e.bill?.roomChargePaise??0,received:e.bill?.paidPaise??0})));
  const zb=tbl('zero-bills','Zero room-charge bills by FY',[T('fy','FY'),I('bills','Bills'),I('zero','Zero room-charge bills'),P('share','Share %')],
    fys.map(f=>{const bs=i.bills.filter(b=>fyOf(b.date)===f.fy);const z=bs.filter(b=>b.roomChargePaise===0).length;return row({fy:fyLabel(f.fy),bills:bs.length,zero:z,share:pct1(z,bs.length)});}));
  const byCat=tbl('zero-by-fy-category','Zero by FY and category',[T('fy','FY'),...cats.map(c=>I('c_'+c,c)),I('total','Total')],
    fys.map(f=>{const es=evs.filter(e=>fyOf(e.date)===f.fy);return row({fy:fyLabel(f.fy),...Object.fromEntries(cats.map(c=>['c_'+c,es.filter(e=>e.cat===c).length])),total:es.length});}));
  const months=sortedUnique(evs.map(e=>monthOf(e.date)));
  const byMonth=tbl('zero-by-month','Zero by month',[T('month','Month'),I('events','Events'),M('expected','Expected room charge')],months.map(m=>{const es=evs.filter(e=>monthOf(e.date)===m);return row({month:monthLabel(m),events:es.length,expected:sum(es.map(e=>e.expected))});}));
  const byRoom=tbl('zero-by-room',`Zero by room (${fyLabel(fy)})`,[T('room','Room'),T('type','Type'),I('events','Events')],
    sortedUnique(inFy.map(e=>e.stay.room)).map(r=>row({room:r,type:roomType(i,r),events:inFy.filter(e=>e.stay.room===r).length})));
  const big=changed.filter(b=>fyOf(b.date)===fy).map(b=>{const s=st.get(b.stay)!;const nightly=rdiv(b.roomChargePaise,b.chargedNights),cut=s.ratePaise-nightly;return {b,s,cut,total:cut*b.chargedNights,nightly};})
    .filter(x=>x.nightly>0&&(x.cut*10>x.s.ratePaise*3||x.cut>100000)).sort((a,b)=>b.total-a.total||(a.b.id<b.b.id?-1:1)).slice(0,50);
  const down=tbl('large-down-adjustments','Large down-adjustments (cut over 30 % or over Rs 1,000 a night; top 50)',[T('bill','Bill'),T('room','Room'),M('from','Stay rate'),M('to','Billed rate'),M('cut','Cut per night'),M('total','Cut x nights')],
    big.map(x=>row({bill:x.b.id,room:x.s.room,from:x.s.ratePaise,to:x.nightly,cut:x.cut,total:x.total})));
  const reasons=tbl('reasons-ranking','Reasons ranking (purpose of the stay)',[T('reason','Purpose'),I('events','Events')],
    sortedUnique(inFy.map(e=>e.stay.purpose||'not recorded')).map(r=>({r,c:inFy.filter(e=>(e.stay.purpose||'not recorded')===r).length})).sort((a,b)=>b.c-a.c||(a.r<b.r?-1:1)).map(x=>row({reason:x.r,events:x.c})));
  const vs=tbl('zero-vs-receipts','Zero vs receipts',[T('item','Item'),I('stays','Stays')],
    [row({item:'Zero-event stays',stays:inFy.length}),row({item:'With a zero room-charge bill',stays:inFy.filter(e=>e.cat==='Zero room charge'||e.bill?.roomChargePaise===0).length}),
     row({item:'Zero room-charge bill and money received',stays:inFy.filter(e=>e.bill?.roomChargePaise===0&&(e.bill?.paidPaise??0)>0).length})]);
  const A=sum(inFy.map(e=>e.expected)),B=sum(inFy.filter(e=>e.bill).map(e=>e.short)),C=sum(inFy.filter(e=>e.bill?.roomChargePaise===0).map(e=>e.expected));
  const given=tbl('revenue-given-up','Revenue given up (estimates, room tariff before GST)',[T('est','Estimate'),M('amount','Amount')],
    [row({est:'A: expected charge of every zero-event stay',amount:A}),row({est:'B: shortfall against the bill (stays with a bill)',amount:B}),row({est:'C: expected charge of stays whose bill is zero',amount:C})]);
  const sr=series('events-by-fy','bar','Zero-rate events by FY',fys.map(f=>f.fy),fys.map(f=>evs.filter(e=>fyOf(e.date)===f.fy).length),false);
  const note=NOTE('method','Evidence of a zero rate, not proof of a free stay. Legacy rows keep note-parsed events; live rows read the complimentary flag (DEC-266: the explicit complimentary stay type is pending). Revenue-given-up figures are estimates, not losses.');
  return result('rpt.study.zero-rate-complimentary','Zero-rate and complimentary stays',{fy},[rc,events,zb,byCat,byMonth,byRoom,down,reasons,vs,given,sr,note],i);
};

const BUCKETS=['0-30','31-90','91-180','181-365','366-730','731+'];
const bucketOf=(age:number)=>age<=30?BUCKETS[0]:age<=90?BUCKETS[1]:age<=180?BUCKETS[2]:age<=365?BUCKETS[3]:age<=730?BUCKETS[4]:BUCKETS[5];

export const receivablesBadDebt:AnalyticsSelector=(i,p)=>{
  const asOf=(p.asOf as string|undefined)??i.asOf,top=Math.max(1,Math.min(+(p.topN??100),100)),st=billStay(i);
  const age=(d:string)=>Math.round((Date.parse(asOf)-Date.parse(d))/86400000);
  const dues=i.bills.filter(b=>b.date<=asOf&&b.totalPaise>b.paidPaise).map(b=>{const s=st.get(b.stay);const due=b.totalPaise-b.paidPaise;
    return {b,due,gst:rdiv(due*b.gstPaise,b.totalPaise),party:s?.company||s?.guestKey||'unknown',company:!!s?.company,legacy:b.legacy,age:age(b.date),fy:fyOf(b.date)};});
  const legacy=dues.filter(d=>d.legacy),live=dues.filter(d=>!d.legacy);
  const fys=sortedUnique(dues.map(d=>d.fy));
  const summary=tbl('summary','Summary by FY (legacy = bad debt, DEC-265)',[T('fy','FY'),T('kind','Kind'),I('bills','Bills'),M('due','Due'),P('share','Share of total due %')],
    [...fys.map(f=>{const ds=dues.filter(d=>d.fy===f);const lg=ds.some(d=>d.legacy);return row({fy:fyLabel(f),kind:lg?'Bad debt (legacy)':'Live receivable',bills:ds.length,due:sum(ds.map(d=>d.due)),share:pct1(sum(ds.map(d=>d.due)),sum(dues.map(d=>d.due)))});}),
     row({fy:'Total',kind:'',bills:dues.length,due:sum(dues.map(d=>d.due)),share:100},'total')]);
  const bad=tbl('bad-debt-schedule','Bad Debt Schedule (legacy, write-off provision 100 %; GST is an estimate, CA to confirm)',[T('fy','FY'),I('bills','Bills'),M('due','Due'),M('gst','GST included (estimate)'),M('net','Net of GST')],
    [...sortedUnique(legacy.map(d=>d.fy)).map(f=>{const ds=legacy.filter(d=>d.fy===f);return row({fy:fyLabel(f),bills:ds.length,due:sum(ds.map(d=>d.due)),gst:sum(ds.map(d=>d.gst)),net:sum(ds.map(d=>d.due-d.gst))});}),
     row({fy:'Total',bills:legacy.length,due:sum(legacy.map(d=>d.due)),gst:sum(legacy.map(d=>d.gst)),net:sum(legacy.map(d=>d.due-d.gst))},'total')]);
  const ageing=tbl('ageing-by-fy','Ageing by FY',[T('fy','FY'),...BUCKETS.map(b=>M('b_'+b,b+' days')),M('total','Total')],
    fys.map(f=>{const ds=dues.filter(d=>d.fy===f);return row({fy:fyLabel(f),...Object.fromEntries(BUCKETS.map(b=>['b_'+b,sum(ds.filter(d=>bucketOf(d.age)===b).map(d=>d.due))])),total:sum(ds.map(d=>d.due))});}));
  const topDues=tbl('top-dues',`Top ${top} dues`,[T('bill','Bill'),T('party','Party','guestName'),T('date','Bill date'),I('age','Age (days)'),M('due','Due'),T('kind','Kind')],
    [...dues].sort((a,b)=>b.due-a.due||(a.b.id<b.b.id?-1:1)).slice(0,top).map(d=>row({bill:d.b.id,party:d.party,date:d.b.date,age:d.age,due:d.due,kind:d.legacy?'Bad debt (legacy)':'Live'})));
  const byParty=new Map<string,{n:number;due:number;old:string}>();for(const d of dues){const r=byParty.get(d.party)??{n:0,due:0,old:d.b.date};r.n++;r.due+=d.due;if(d.b.date<r.old)r.old=d.b.date;byParty.set(d.party,r);}
  const debtors=tbl('top-debtors','Top debtors',[T('party','Debtor','guestName'),I('bills','Bills'),M('due','Due'),T('oldest','Oldest bill')],
    [...byParty.entries()].sort((a,b)=>b[1].due-a[1].due||(a[0]<b[0]?-1:1)).slice(0,top).map(([k,r])=>row({party:k,bills:r.n,due:r.due,oldest:r.old})));
  const cw=tbl('company-vs-walkin','Company vs walk-in',[T('kind','Kind'),I('bills','Bills'),M('due','Due'),P('share','Share %')],
    [['Company / B2B',true],['Walk-in',false]].map(([l,c])=>{const ds=dues.filter(d=>d.company===c);return row({kind:l as string,bills:ds.length,due:sum(ds.map(d=>d.due)),share:pct1(sum(ds.map(d=>d.due)),sum(dues.map(d=>d.due)))});}));
  const follow=tbl('follow-up-list','Follow-up list (dues older than 2 years are verify-first)',[T('bill','Bill'),T('party','Party','guestName'),M('due','Due'),I('age','Age (days)'),T('action','Action')],
    [...dues].sort((a,b)=>b.due-a.due||(a.b.id<b.b.id?-1:1)).slice(0,top).map(d=>row({bill:d.b.id,party:d.party,due:d.due,age:d.age,action:d.age>730?'Verify first':'Call'})));
  const mu=sortedUnique(dues.map(d=>monthOf(d.b.date)));
  const monthly=tbl('monthly-unpaid','Monthly unpaid',[T('month','Month'),I('bills','Bills'),M('due','Due')],mu.map(m=>{const ds=dues.filter(d=>monthOf(d.b.date)===m);return row({month:monthLabel(m),bills:ds.length,due:sum(ds.map(d=>d.due))});}));
  const cut=tbl('cutover-comparison',`Cutover comparison (legacy to ${i.legacyUntil} / HCC live)`,[T('side','Side'),I('bills','Bills owing'),M('due','Due'),T('treatment','Treatment')],
    [row({side:'Legacy',bills:legacy.length,due:sum(legacy.map(d=>d.due)),treatment:'Bad debt, not carried as opening receivable (DEC-265)'}),row({side:'HCC live',bills:live.length,due:sum(live.map(d=>d.due)),treatment:'Receivable; see rpt.acc.ageing'})]);
  const notes=NOTE('contacts','Debtor Contacts (phone, GSTIN) are pending: the analytics input carries no contact data. Names stay under rule V.');
  const sr=series('due-by-fy','bar','Due by FY',fys,fys.map(f=>sum(dues.filter(d=>d.fy===f).map(d=>d.due))),true);
  return result('rpt.study.receivables-bad-debt','Receivables and bad debt (legacy + live ageing)',{asOf,topN:String(top)},[summary,bad,ageing,topDues,debtors,cw,follow,monthly,cut,sr,notes],i,undefined,asOf);
};

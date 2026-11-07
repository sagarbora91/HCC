// F12 MEAS · meas-study-a · revenue family: rpt.study.revenue-monthly, year-comparison, index-vs-base, fy-deep-dive (workbooks 1-4).
// "Billed" = total billed incl. GST (the workbooks' headline); "net" = before GST. C# twin: Analytics/LegacyRevenue.cs.
import type {AnalyticsSelector} from './types';
import {T,I,M,P,row,tbl,series,result,fyOf,fyPrev,monthStart,monthOf,net,adr,pct1,idx1,rdiv,sum,fyRows,revenueMonths,focusFy,marker,FY_MONTHS,MON_LABEL,
  addDays,fyStartYear,billStay,roomType,type Tbl} from './legacy-core';

const fyLabel=(fy:string)=>'FY '+fy;
const monthLabel=(m:string)=>MON_LABEL[m.slice(5)]+' '+m.slice(0,4);

export const revenueMonthly:AnalyticsSelector=(i,p)=>{
  const fy=focusFy(i,p),rev=revenueMonths(i),fys=fyRows(i);
  const monthly=tbl('monthly',`Monthly (${fyLabel(fy)})`,[T('month','Month'),T('data','Data'),I('bills','Bills'),M('room','Room'),M('fnb','F&B'),M('other','Other'),M('discount','Discount'),M('netBilled','Net billed'),
    M('gst','GST'),M('billed','Total billed'),I('chargedNights','Charged nights'),M('adr','ADR'),M('received','Cash received'),P('receivedPct','Received % of billed')],
    rev.filter(r=>fyOf(monthStart(r.month))===fy).map(r=>row({month:monthLabel(r.month),data:marker(i,monthStart(r.month),r.month+'-28'),bills:r.bills,room:r.room,fnb:r.fnb,other:r.other,discount:r.discount,
      netBilled:net(r),gst:r.gst,billed:r.total,chargedNights:r.chargedNights,adr:adr(r),received:r.received,receivedPct:pct1(r.received,r.total)})));
  const summary=tbl('fy-summary','FY summary',[T('fy','FY'),T('data','Data'),I('bills','Bills'),M('netBilled','Net billed'),M('gst','GST'),M('billed','Total billed'),I('chargedNights','Charged nights'),M('adr','ADR'),
    M('received','Cash received'),P('receivedPct','Received % of billed'),T('note','Note')],
    fys.map(f=>row({fy:fyLabel(f.fy),data:marker(i,f.fy.slice(0,4)+'-04-01',(fyStartYear(f.fy)+1)+'-03-31'),bills:f.bills,netBilled:net(f),gst:f.gst,billed:f.total,chargedNights:f.chargedNights,adr:adr(f),received:f.received,
      receivedPct:pct1(f.received,f.total),note:f.partial?'Partial year':f.normal?'':'COVID window'})));
  const grid=tbl('fy-month-grid','FY x Month grid (total billed)',[T('fy','FY'),...FY_MONTHS.map(m=>M('m'+m,MON_LABEL[m])),M('total','Total')],
    fys.map(f=>{const c:Record<string,string|number>={fy:fyLabel(f.fy),total:f.total};for(const m of FY_MONTHS){const yr=m>='04'?fyStartYear(f.fy):fyStartYear(f.fy)+1;c['m'+m]=rev.find(r=>r.month===`${yr}-${m}`)?.total??0;}return row(c);}));
  const sr=series('billed-by-fy','bar','Total billed by FY',fys.map(f=>f.fy),fys.map(f=>f.total),true);
  const sm=series('billed-by-month','line',`Total billed by month ${fyLabel(fy)}`,monthly.rows.map(r=>String(r.cells.month)),monthly.rows.map(r=>r.cells.billed as number),true);
  return result('rpt.study.revenue-monthly','Monthly revenue and cash (FY history)',{fy},[monthly,summary,grid,sr,sm],i);
};

export const yearComparison:AnalyticsSelector=(i,p)=>{
  const fys=fyRows(i),rev=revenueMonths(i);const from=p.from??`${fyStartYear(fyOf(i.asOf))}-04-01`,to=p.to??i.asOf;
  const mdFrom=from.slice(5),mdTo=to.slice(5);
  const summary=tbl('summary','Summary',[T('fy','FY'),M('billed','Total billed'),I('nights','Room-nights sold'),P('occ','Occupancy %'),M('adr','ADR'),P('yoy','YoY % (as reported)'),T('note','Note')],
    fys.map((f,k)=>row({fy:fyLabel(f.fy),billed:f.total,nights:f.sold,occ:pct1(f.sold,f.capacity),adr:adr(f),yoy:k>0&&fys[k-1].total>0?pct1(f.total-fys[k-1].total,fys[k-1].total):null,note:f.partial?'Partial year, use like-for-like':f.normal?'':'COVID window'})));
  const grid=tbl('month-year-grid','Month x Year (total billed)',[T('month','Month'),...fys.map(f=>M('y'+f.fy,f.fy))],
    FY_MONTHS.map(m=>row({month:MON_LABEL[m],...Object.fromEntries(fys.map(f=>['y'+f.fy,rev.find(r=>r.month===`${m>='04'?fyStartYear(f.fy):fyStartYear(f.fy)+1}-${m}`)?.total??0]))})));
  const yoy=tbl('yoy','YoY growth (billed, room-nights, ADR)',[T('fy','FY'),P('billed','Billed %'),P('nights','Room-nights %'),P('adr','ADR %')],
    fys.slice(1).map((f,k)=>{const a=fys[k];return row({fy:fyLabel(f.fy),billed:pct1(f.total-a.total,a.total),nights:pct1(f.sold-a.sold,a.sold),adr:pct1(adr(f)-adr(a),adr(a))});}));
  const lfl=tbl('like-for-like',`Like-for-like ${from.slice(8)}-${from.slice(5,7)} to ${to.slice(8)}-${to.slice(5,7)} (date-exact)`,[T('fy','FY'),M('billed','Total billed'),I('bills','Bills'),P('vsPrev','vs previous year %')],
    fys.map((f,k)=>{const y0=fyStartYear(f.fy),a=(mdFrom>='04-01'?y0:y0+1)+'-'+mdFrom,b=(mdTo>='04-01'?y0:y0+1)+'-'+mdTo;
      const bs=i.bills.filter(x=>x.date>=a&&x.date<=b);const t=sum(bs.map(x=>x.totalPaise));
      const pa=k>0?(()=>{const y1=fyStartYear(fys[k-1].fy),a1=(mdFrom>='04-01'?y1:y1+1)+'-'+mdFrom,b1=(mdTo>='04-01'?y1:y1+1)+'-'+mdTo;return sum(i.bills.filter(x=>x.date>=a1&&x.date<=b1).map(x=>x.totalPaise));})():0;
      return row({fy:fyLabel(f.fy),billed:t,bills:bs.length,vsPrev:k>0&&pa>0?pct1(t-pa,pa):null});}));
  const cum=tbl('cumulative','Cumulative YTD (total billed)',[T('fy','FY'),...FY_MONTHS.map(m=>M('m'+m,MON_LABEL[m]))],
    fys.map(f=>{let acc=0;const c:Record<string,string|number>={fy:fyLabel(f.fy)};for(const m of FY_MONTHS){const yr=m>='04'?fyStartYear(f.fy):fyStartYear(f.fy)+1;acc+=rev.find(r=>r.month===`${yr}-${m}`)?.total??0;c['m'+m]=acc;}return row(c);}));
  const full=fys.filter(f=>f.normal);
  const cagr=tbl('cagr','CAGR (normal full years)',[T('from','From FY'),T('to','To FY'),I('years','Years'),M('first','First billed'),M('last','Last billed'),P('cagr','CAGR %')],
    full.length>=2?[(()=>{const a=full[0],b=full[full.length-1],yrs=fyStartYear(b.fy)-fyStartYear(a.fy);
      const g=yrs>0&&a.total>0?Math.round((Math.pow(b.total/a.total,1/yrs)-1)*1000)/10:0;return row({from:fyLabel(a.fy),to:fyLabel(b.fy),years:yrs,first:a.total,last:b.total,cagr:g});})()]:[]);
  const mix=tbl('head-mix','Revenue head mix (share of net billed)',[T('fy','FY'),P('room','Room %'),P('fnb','F&B %'),P('other','Other %'),P('discount','Discount %')],
    fys.map(f=>{const g=f.room+f.fnb+f.other;return row({fy:fyLabel(f.fy),room:pct1(f.room,g),fnb:pct1(f.fnb,g),other:pct1(f.other,g),discount:pct1(f.discount,g)});}));
  const sr=series('billed-by-fy','bar','Total billed by FY',fys.map(f=>f.fy),fys.map(f=>f.total),true);
  return result('rpt.study.year-comparison','Year comparison',{from,to},[summary,grid,yoy,lfl,cum,cagr,mix,sr],i,from,to);
};

export const indexVsBase:AnalyticsSelector=(i,p)=>{
  const base=p.baseFy??'2018-19',fys=fyRows(i),rev=revenueMonths(i),b=fys.find(f=>f.fy===base);
  const index=tbl('index',`Index vs ${fyLabel(base)} (base = 100)`,[T('fy','FY'),P('billed','Billed index'),P('nights','Room-night index'),P('adr','ADR index'),T('note','Note')],
    b?fys.map(f=>row({fy:fyLabel(f.fy),billed:idx1(f.total,b.total),nights:idx1(f.sold,b.sold),adr:idx1(adr(f),adr(b)),note:f.partial?'Partial year':f.normal?'':'COVID window'})):[]);
  const covid=fys.filter(f=>!f.normal&&!f.partial);
  const impact=tbl('covid-impact','COVID impact and recovery',[T('fy','FY'),M('billed','Total billed'),M('vsBase','Change vs base'),P('vsBasePct','Change %'),T('status','Status')],
    b?fys.filter(f=>f.fy!==base).map(f=>{const back=f.normal&&f.total>=b.total;return row({fy:fyLabel(f.fy),billed:f.total,vsBase:f.total-b.total,vsBasePct:pct1(f.total-b.total,b.total),
      status:f.partial?'Partial year':covid.includes(f)?'COVID window':back?'Recovered':'Below base'});}):[]);
  const normal=fys.filter(f=>f.normal);
  const months=rev.filter(r=>normal.some(f=>f.fy===fyOf(monthStart(r.month)))).sort((x,y)=>y.total-x.total||(x.month<y.month?-1:1));
  const bw=tbl('best-worst','Best and worst months (normal years, total billed)',[T('rank','Rank'),T('month','Month'),M('billed','Total billed'),T('kind','Kind')],
    [...months.slice(0,5).map((r,k)=>row({rank:'Best '+(k+1),month:monthLabel(r.month),billed:r.total,kind:'best'})),...months.slice(-5).reverse().map((r,k)=>row({rank:'Worst '+(k+1),month:monthLabel(r.month),billed:r.total,kind:'worst'}))]);
  const sr=series('index-billed','line','Billed index vs base',fys.map(f=>f.fy),b?fys.map(f=>idx1(f.total,b.total)):[],false,true);
  return result('rpt.study.index-vs-base','Index vs a base year',{fy:focusFy(i,p),baseFy:base},[index,impact,bw,sr],i);
};

export const fyDeepDive:AnalyticsSelector=(i,p)=>{
  const fy=focusFy(i,p),prev=fyPrev(fy),fys=fyRows(i),cur=fys.find(f=>f.fy===fy),old=fys.find(f=>f.fy===prev),top=Math.max(1,Math.min(+(p.topN??10),100));
  const inFy=(d:string)=>fyOf(d)===fy;const bills=i.bills.filter(b=>inFy(b.date)),st=billStay(i);
  const pv=tbl('a-price-volume',`A Price-volume ${fyLabel(prev)} to ${fyLabel(fy)}`,[T('item','Item'),M('amount','Amount')],cur&&old?(()=>{
    const volume=(cur.chargedNights-old.chargedNights)*adr(old),price=(adr(cur)-adr(old))*cur.chargedNights,nonRoom=(net(cur)-cur.room)-(net(old)-old.room);
    return [row({item:'Total billed, previous FY',amount:old.total}),row({item:'Volume effect (nights x old ADR)',amount:volume}),row({item:'Price effect (ADR change x new nights)',amount:price}),
      row({item:'Non-room net change (F&B, other, discount)',amount:nonRoom}),row({item:'GST change and rounding',amount:cur.total-old.total-volume-price-nonRoom}),row({item:'Total billed, this FY',amount:cur.total},'total')];})():[]);
  const nb=tbl('b-neighbours','B Against neighbouring years',[T('fy','FY'),M('billed','Total billed'),I('nights','Room-nights'),M('adr','ADR'),P('occ','Occupancy %')],
    fys.filter(f=>Math.abs(fyStartYear(f.fy)-fyStartYear(fy))<=1).map(f=>row({fy:fyLabel(f.fy),billed:f.total,nights:f.sold,adr:adr(f),occ:pct1(f.sold,f.capacity)})));
  const seg=new Map<string,{n:number;rev:number}>();for(const b of bills){const s=st.get(b.stay);const t=s?roomType(i,s.room):'unassigned',r=seg.get(t)??{n:0,rev:0};r.n+=b.chargedNights;r.rev+=b.roomChargePaise;seg.set(t,r);}
  const c=tbl('c-room-type','C Segments by room type',[T('type','Room type'),I('nights','Charged nights'),M('room','Room revenue'),M('adr','ADR'),P('share','Share of room revenue %')],
    [...seg.entries()].sort((a,b)=>b[1].rev-a[1].rev||(a[0]<b[0]?-1:1)).map(([t,r])=>row({type:t,nights:r.n,room:r.rev,adr:rdiv(r.rev,r.n),share:pct1(r.rev,sum([...seg.values()].map(x=>x.rev)))})));
  const src=new Map<string,{n:number;t:number}>();for(const b of bills){const s=st.get(b.stay);const k=s?.source??'unknown',r=src.get(k)??{n:0,t:0};r.n++;r.t+=b.totalPaise;src.set(k,r);}
  const d=tbl('d-source','D Booking source',[T('source','Source'),I('bills','Bills'),M('billed','Total billed'),P('share','Share %')],
    [...src.entries()].sort((a,b)=>b[1].t-a[1].t||(a[0]<b[0]?-1:1)).map(([k,r])=>row({source:k,bills:r.n,billed:r.t,share:pct1(r.t,sum(bills.map(x=>x.totalPaise)))})));
  const pay=new Map<string,number>();for(const r of i.receipts)if(inFy(r.date))pay.set(r.method,(pay.get(r.method)??0)+r.amountPaise);
  const e=tbl('e-payment-mix','E Payment mix',[T('method','Method'),M('amount','Received'),P('share','Share %')],
    [...pay.entries()].sort((a,b)=>b[1]-a[1]||(a[0]<b[0]?-1:1)).map(([k,v])=>row({method:k,amount:v,share:pct1(v,sum([...pay.values()]))})));
  const f=tbl('f-top-bills',`F Top ${top} bills`,[T('bill','Bill'),T('guest','Guest','guestName'),T('date','Date'),M('billed','Total billed'),I('nights','Charged nights')],
    [...bills].sort((a,b)=>b.totalPaise-a.totalPaise||(a.id<b.id?-1:1)).slice(0,top).map(b=>row({bill:b.id,guest:st.get(b.stay)?.guestKey??'',date:b.date,billed:b.totalPaise,nights:b.chargedNights},'row')));
  const los=new Map<number,number>();for(const s of i.stays)if(inFy(s.from)&&!['Cancelled','No show','Held','Expired','Duplicate','Confirmed'].includes(s.status)){const k=Math.min(7,Math.max(1,s.nights));los.set(k,(los.get(k)??0)+1);}
  const g=tbl('g-length-of-stay','G Length of stay (stays by arrival)',[T('los','Nights'),I('stays','Stays'),P('share','Share %')],
    [1,2,3,4,5,6,7].map(k=>row({los:k===7?'7+':String(k),stays:los.get(k)??0,share:pct1(los.get(k)??0,sum([...los.values()]))})));
  const sm=series('months','line',`Total billed by month ${fyLabel(fy)}`,FY_MONTHS.map(m=>MON_LABEL[m]),FY_MONTHS.map(m=>revenueMonths(i).find(r=>r.month===`${m>='04'?fyStartYear(fy):fyStartYear(fy)+1}-${m}`)?.total??0),true);
  return result('rpt.study.fy-deep-dive','FY deep dive (price-volume, segments, top bills)',{fy,topN:String(top)},[pv,nb,c,d,e,f,g,sm],i);
};
void addDays;void monthOf;

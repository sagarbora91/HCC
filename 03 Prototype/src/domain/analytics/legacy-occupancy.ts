// F12 MEAS · meas-study-a · occupancy and operations family: rpt.study.occupancy-history, operations-patterns, rooms-and-rates (workbooks 5-7).
// C# twin: Analytics/LegacyOccupancy.cs.
import type {AnalyticsSelector} from './types';
import {T,I,M,P,row,tbl,series,NOTE,result,fyOf,monthStart,monthEnd,pct1,rdiv,sum,fyRows,occupancyMonths,revenueMonths,nights,focusFy,FY_MONTHS,MON_LABEL,fyStartYear,
  dow,DOW_LABEL,slept,nightsOf,billStay,roomType,inCovid,sortedUnique,addDays} from './legacy-core';

const monthLabel=(m:string)=>MON_LABEL[m.slice(5)]+' '+m.slice(0,4);
const fyLabel=(fy:string)=>'FY '+fy;

export const occupancyHistory:AnalyticsSelector=(i,p)=>{
  const fy=focusFy(i,p),om=occupancyMonths(i),fys=fyRows(i);
  const monthly=tbl('monthly',`Monthly (${fyLabel(fy)})`,[T('month','Month'),I('days','Days'),I('inService','Room-nights in service'),I('sold','Room-nights sold'),P('occ','Occupancy % (rooms in service)'),
    P('occA','Alt A % (rooms that sold)'),P('occB','Alt B % (fixed room count)'),I('stays','Stays arrived'),T('alos','ALOS (arrival month)'),I('sameDay','Same-day stays')],
    om.filter(o=>fyOf(monthStart(o.month))===fy).map(o=>row({month:monthLabel(o.month),days:o.days,inService:o.capacity,sold:o.sold,occ:pct1(o.sold,o.capacity),occA:pct1(o.sold,o.capacityA),occB:pct1(o.sold,o.capacityB),
      stays:o.stays,alos:o.stays?(Math.round(o.nightsArrived*100/o.stays)/100).toFixed(2):'',sameDay:o.sameDay})));
  const summary=tbl('fy-summary','FY summary',[T('fy','FY'),I('days','Days in data'),I('inService','Room-nights in service'),I('sold','Room-nights sold'),P('occ','Occupancy %'),P('occA','Alt A %'),P('occB','Alt B %'),T('note','Note')],
    fys.map(f=>{const o=om.filter(x=>fyOf(monthStart(x.month))===f.fy);return row({fy:fyLabel(f.fy),days:f.days,inService:f.capacity,sold:f.sold,occ:pct1(f.sold,f.capacity),occA:pct1(f.sold,sum(o.map(x=>x.capacityA))),
      occB:pct1(f.sold,sum(o.map(x=>x.capacityB))),note:f.partial?'Partial year':f.normal?'':'COVID window'});}));
  const grid=tbl('fy-month-grid','FY x Month grid (occupancy %)',[T('fy','FY'),...FY_MONTHS.map(m=>P('m'+m,MON_LABEL[m]))],
    fys.map(f=>{const c:Record<string,string|number|null>={fy:fyLabel(f.fy)};for(const m of FY_MONTHS){const o=om.find(x=>x.month===`${m>='04'?fyStartYear(f.fy):fyStartYear(f.fy)+1}-${m}`);c['m'+m]=o?pct1(o.sold,o.capacity):null;}return row(c);}));
  const ns=nights(i).filter(n=>fyOf(n.date)===fy);
  const wk=(set:number[])=>ns.filter(n=>set.includes(dow(n.date)));
  const capDays=(set:number[])=>{let c=0;const days=new Set<string>();for(const o of om.filter(x=>fyOf(monthStart(x.month))===fy))for(let d=monthStart(o.month);d<=monthEnd(o.month)&&d<=i.asOf;d=addDays(d,1))if(set.includes(dow(d))&&!days.has(d)){days.add(d);c+=i.rooms.filter(r=>r.inServiceFrom<=d&&(!r.inServiceTo||r.inServiceTo>=d)).length;}return c;};
  const weekday=tbl('weekday-weekend',`Weekday vs weekend (${fyLabel(fy)}; weekend = Fri + Sat nights)`,[T('group','Group'),I('sold','Room-nights sold'),I('capacity','Room-nights in service'),P('occ','Occupancy %')],
    [['Weekday nights (Sun-Thu)',[0,1,2,3,4]],['Weekend nights (Fri-Sat)',[5,6]]].map(([l,set])=>{const c=capDays(set as number[]),s=wk(set as number[]).length;return row({group:l as string,sold:s,capacity:c,occ:pct1(s,c)});}));
  const seasonRows=FY_MONTHS.map(m=>{const xs=om.filter(o=>o.month.slice(5)===m&&!inCovid(i,o.month)&&o.days===new Date(Date.UTC(+o.month.slice(0,4),+m,0)).getUTCDate());
    const avg=xs.length?Math.round(sum(xs.map(o=>pct1(o.sold,o.capacity)*10))/xs.length)/10:0;return row({month:MON_LABEL[m],obs:xs.length,avg});});
  const season=tbl('seasonality','Seasonality (simple mean of monthly %, normal years)',[T('month','Month'),I('obs','Months observed'),P('avg','Average occupancy %')],seasonRows);
  const types=sortedUnique(i.rooms.map(r=>roomType(i,r.id)));
  const mix=tbl('room-type-mix',`Room-type mix (${fyLabel(fy)})`,[T('type','Room type'),I('rooms','Rooms'),I('sold','Room-nights sold'),I('capacity','Room-nights in service'),P('occ','Occupancy %'),P('share','Share of room-nights %')],
    types.map(t=>{const rs=i.rooms.filter(r=>roomType(i,r.id)===t);const days=sum(om.filter(o=>fyOf(monthStart(o.month))===fy).map(o=>o.days));
      const s=ns.filter(n=>roomType(i,n.room)===t).length;return row({type:t,rooms:rs.length,sold:s,capacity:rs.length*days,occ:pct1(s,rs.length*days),share:pct1(s,ns.length)});}));
  const sr=series('occ-by-fy','bar','Occupancy % by FY',fys.map(f=>f.fy),fys.map(f=>pct1(f.sold,f.capacity)),false,true);
  const note=NOTE('caveat','The data does not record which rooms were sellable on a given day; all three denominators are approximations and true occupancy of sellable inventory is at or above the headline.');
  return result('rpt.study.occupancy-history','Occupancy history (three denominators)',{fy},[monthly,summary,grid,weekday,season,mix,sr,note],i);
};

export const operationsPatterns:AnalyticsSelector=(i,p)=>{
  const fy=focusFy(i,p),stays=i.stays.filter(s=>slept(s)&&fyOf(s.from)===fy);const n=stays.length;
  const dist=(key:string,label:string,keys:string[],f:(s:typeof stays[number])=>string)=>tbl(key,label,[T('item',label.split(' (')[0]),I('stays','Stays'),P('share','Share %')],
    keys.map(k=>{const c=stays.filter(s=>f(s)===k).length;return row({item:k,stays:c,share:pct1(c,n)});}));
  const arrDow=dist('arrivals-dow','Arrivals_DOW',DOW_LABEL,s=>DOW_LABEL[dow(s.from)]);
  const depDow=dist('departures-dow','Departures_DOW',DOW_LABEL,s=>DOW_LABEL[dow(addDays(s.from,nightsOf(s)))]);
  const losKeys=['1','2','3','4','5','6','7+'];const los=dist('length-of-stay','LengthOfStay',losKeys,s=>nightsOf(s)>=7?'7+':String(nightsOf(s)));
  const grp=new Map<string,number>();for(const s of stays){const k=s.guestKey+'|'+s.from;grp.set(k,(grp.get(k)??0)+1);}
  const gsKeys=['1 room','2 rooms','3-4 rooms','5+ rooms'];const gsOf=(c:number)=>c===1?gsKeys[0]:c===2?gsKeys[1]:c<=4?gsKeys[2]:gsKeys[3];
  const gs=tbl('group-size','GroupSize (rooms booked together: same guest, same arrival)',[T('item','Rooms'),I('parties','Parties'),P('share','Share %')],
    gsKeys.map(k=>{const c=[...grp.values()].filter(v=>gsOf(v)===k).length;return row({item:k,parties:c,share:pct1(c,grp.size)});}));
  const cnt=(f:(s:typeof stays[number])=>string)=>{const m=new Map<string,number>();for(const s of stays){const k=f(s);m.set(k,(m.get(k)??0)+1);}return [...m.entries()].sort((a,b)=>b[1]-a[1]||(a[0]<b[0]?-1:1));};
  const pur=tbl('purpose','Purpose',[T('item','Purpose'),I('stays','Stays'),P('share','Share %')],cnt(s=>s.purpose||'not recorded').map(([k,c])=>row({item:k,stays:c,share:pct1(c,n)})));
  const src=tbl('booking-source','BookingSource',[T('item','Source'),I('stays','Stays'),P('share','Share %')],cnt(s=>s.channel||s.source||'unknown').map(([k,c])=>row({item:k,stays:c,share:pct1(c,n)})));
  const allSt=i.stays.filter(s=>fyOf(s.from)===fy);
  const status=tbl('status','Status (all stays arriving in the FY)',[T('item','Status'),I('stays','Stays'),P('share','Share %')],
    sortedUnique(allSt.map(s=>s.status)).map(k=>{const c=allSt.filter(s=>s.status===k).length;return row({item:k,stays:c,share:pct1(c,allSt.length)});}));
  const need=tbl('needs-times','CheckIn_Hour, CheckOut_Hour, Turnover_EarlyLate, AdvanceLead',[T('item','Sheet'),T('state','State')],
    ['CheckIn_Hour','CheckOut_Hour','Turnover_EarlyLate','AdvanceLead'].map(k=>row({item:k,state:'Pending: the analytics input carries dates only (needs check-in / out times and booking date)'})));
  const sr=series('arrivals-dow','bar','Arrivals by weekday',DOW_LABEL,arrDow.rows.map(r=>r.cells.stays as number),false);
  return result('rpt.study.operations-patterns','Operations patterns',{fy},[arrDow,depDow,los,gs,pur,src,status,need,sr],i);
};

export const roomsAndRates:AnalyticsSelector=(i,p)=>{
  const fy=focusFy(i,p),fys=fyRows(i),st=billStay(i),om=occupancyMonths(i);
  const ids=[...i.rooms.map(r=>r.id)].sort();const ns=nights(i);
  const days=sum(om.filter(o=>fyOf(monthStart(o.month))===fy).map(o=>o.days));
  const billsFy=i.bills.filter(b=>fyOf(b.date)===fy);
  const perRoom=ids.map(id=>{const bs=billsFy.filter(b=>st.get(b.stay)?.room===id);return {id,type:roomType(i,id),nights:ns.filter(n=>n.room===id&&fyOf(n.date)===fy).length,rev:sum(bs.map(b=>b.roomChargePaise)),cn:sum(bs.map(b=>b.chargedNights))};});
  const totNights=sum(perRoom.map(r=>r.nights)),totRev=sum(perRoom.map(r=>r.rev));
  const rn=tbl('room-nights-revenue-rate',`Room Nights / Revenue / Avg Rate / Share / Occupancy (${fyLabel(fy)})`,[T('room','Room'),T('type','Type'),I('nights','Room-nights'),M('revenue','Room revenue'),M('avg','Avg rate'),P('share','Revenue share %'),P('occ','Occupancy %')],
    perRoom.map(r=>row({room:r.id,type:r.type,nights:r.nights,revenue:r.rev,avg:rdiv(r.rev,r.cn),share:pct1(r.rev,totRev),occ:pct1(r.nights,days)})));
  const ranked=[...perRoom].sort((a,b)=>b.rev-a.rev||(a.id<b.id?-1:1));
  const tb=tbl('top-bottom','Top / Bottom rooms (by room revenue)',[T('rank','Rank'),T('room','Room'),M('revenue','Room revenue'),I('nights','Room-nights')],
    [...ranked.slice(0,5).map((r,k)=>row({rank:'Top '+(k+1),room:r.id,revenue:r.rev,nights:r.nights})),...ranked.slice(-5).reverse().map((r,k)=>row({rank:'Bottom '+(k+1),room:r.id,revenue:r.rev,nights:r.nights}))]);
  const avgN=perRoom.length?totNights/perRoom.length:0;
  const low=tbl('low-sales','Low sales (below half the room average)',[T('room','Room'),I('nights','Room-nights'),I('avg','Average per room')],
    perRoom.filter(r=>r.nights*2<avgN).map(r=>row({room:r.id,nights:r.nights,avg:Math.round(avgN)})));
  const types=sortedUnique(perRoom.map(r=>r.type));
  const tf=tbl('room-type-fy','Room type by FY (room revenue)',[T('type','Room type'),...fys.map(f=>M('y'+f.fy,f.fy))],
    types.map(t=>row({type:t,...Object.fromEntries(fys.map(f=>['y'+f.fy,sum(i.bills.filter(b=>fyOf(b.date)===f.fy&&roomType(i,st.get(b.stay)?.room??'')===t).map(b=>b.roomChargePaise))]))})));
  const tt=tbl('tariff-trend','Tariff trend (ADR by FY)',[T('fy','FY'),M('adr','ADR'),P('change','Change %')],fys.map((f,k)=>row({fy:fyLabel(f.fy),adr:rdiv(f.room,f.chargedNights),change:k>0?pct1(rdiv(f.room,f.chargedNights)-rdiv(fys[k-1].room,fys[k-1].chargedNights),rdiv(fys[k-1].room,fys[k-1].chargedNights)):null})));
  const ov=billsFy.filter(b=>{const s=st.get(b.stay);return !!s&&b.chargedNights>0&&s.ratePaise>0&&rdiv(b.roomChargePaise,b.chargedNights)!==s.ratePaise;});
  const ro=tbl('rate-overrides',`Rate overrides (billed rate differs from the stay rate, ${fyLabel(fy)})`,[T('kind','Kind'),I('bills','Bills'),M('delta','Net rate effect (billed minus rate)')],
    [['Below stay rate',ov.filter(b=>rdiv(b.roomChargePaise,b.chargedNights)<st.get(b.stay)!.ratePaise)],['Above stay rate',ov.filter(b=>rdiv(b.roomChargePaise,b.chargedNights)>st.get(b.stay)!.ratePaise)]].map(([k,bs])=>
      row({kind:k as string,bills:(bs as typeof ov).length,delta:sum((bs as typeof ov).map(b=>b.roomChargePaise-st.get(b.stay)!.ratePaise*b.chargedNights))})));
  const dc=tbl('discount-control','Discount control',[T('fy','FY'),I('bills','Bills with discount'),M('discount','Discount'),P('pct','Discount % of gross')],
    fys.map(f=>{const bs=i.bills.filter(b=>fyOf(b.date)===f.fy&&b.discountPaise>0);return row({fy:fyLabel(f.fy),bills:bs.length,discount:f.discount,pct:pct1(f.discount,f.room+f.fnb+f.other)});}));
  const methods=sortedUnique(i.receipts.map(r=>r.method));
  const pm=tbl('payment-mix','Payment mix by FY (received)',[T('fy','FY'),...methods.map(m=>M('p_'+m,m))],fys.map(f=>row({fy:fyLabel(f.fy),...Object.fromEntries(methods.map(m=>['p_'+m,sum(i.receipts.filter(r=>fyOf(r.date)===f.fy&&r.method===m).map(r=>r.amountPaise))]))})));
  const bucket=(age:number)=>age<=30?'0-30':age<=90?'31-90':age<=180?'91-180':age<=365?'181-365':age<=730?'366-730':'731+';
  const B=['0-30','31-90','91-180','181-365','366-730','731+'];
  const unpaid=i.bills.filter(b=>b.totalPaise>b.paidPaise);
  const ua=tbl('unpaid-ageing','Unpaid ageing (as of the business date)',[T('bucket','Age (days)'),I('bills','Bills'),M('due','Due')],
    B.map(k=>{const bs=unpaid.filter(b=>bucket(Math.round((Date.parse(i.asOf)-Date.parse(b.date))/86400000))===k);return row({bucket:k,bills:bs.length,due:sum(bs.map(b=>b.totalPaise-b.paidPaise))});}));
  const sr=series('adr-by-fy','line','ADR by FY',fys.map(f=>f.fy),fys.map(f=>rdiv(f.room,f.chargedNights)),true);
  void revenueMonths;void monthEnd;
  return result('rpt.study.rooms-and-rates','Rooms, rates and discounts',{fy},[rn,tb,low,tf,tt,ro,dc,pm,ua,sr],i);
};

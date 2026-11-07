// F12 MEAS · meas-study-b · Revenue-management workbook family (HCC_Revenue_Management_Report + Sales & Marketing channel sheets).
// Selectors: revenueKpi, channelEconomics, pricingShape. Server twin: Hcc.Reporting/Analytics/BeyondRevenue.cs (same names, same numbers).
import type {AnalyticsSelector} from './types';
import {addDays} from '../money';
import {ctxOf,fyParam,fyMonths,fyStart,fyEnd,monthEnd,pct1,ratio,avg1,sec,tot,seriesSec,finish,noData,sum,dow,DOW,OTA_FREE,monthLabel,fyOf,type Ctx,type BM,type Cells} from './beyond-lib';

const TYPES=['Suite','Premium','Deluxe','Twin'];
const typesOf=(c:Ctx)=>{const t=[...new Set([...c.rtype.values()])];return [...TYPES.filter(x=>t.includes(x)),...t.filter(x=>!TYPES.includes(x)).sort()];};
const KPI=(k:{avail:number;sold:number;cn:number;rev:number})=>[pct1(k.sold,k.avail),ratio(k.rev,k.cn),ratio(k.rev,k.avail)];

export const revenueKpi:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),fy=fyParam(c,p);
  const month:Cells[]=fyMonths(c,fy).map(m=>{const from=m+'-01'<c.dataStart?c.dataStart:m+'-01',to=monthEnd(m)>c.end?c.end:monthEnd(m);
    const ms=c.bills.filter(b=>b.month===m),avail=c.capacity(from,to),sold=c.sleptIn(from,to),cn=sum(ms,b=>b.cn),rev=sum(ms,b=>b.rev);
    return [m,c.srcOf(from,to),Math.round((Date.parse(to)-Date.parse(from))/864e5)+1,c.roomsOn(to).length,avail,sold,pct1(sold,avail),ms.length,cn,rev,ratio(rev,cn),ratio(rev,avail)];});
  const fyRows:(Cells|{total:Cells})[]=[];let ta=0,ts=0,tr=0,tc=0,tm=0;
  for(const f of c.fys){const ms=fyMonths(c,f),from=fyStart(f)<c.dataStart?c.dataStart:fyStart(f),to=fyEnd(f)>c.end?c.end:fyEnd(f);
    const bs=c.bills.filter(b=>b.fy===f),avail=c.capacity(from,to),sold=c.sleptIn(from,to),cn=sum(bs,b=>b.cn),rev=sum(bs,b=>b.rev),adr=ratio(rev,cn);
    ta+=avail;ts+=sold;tr+=rev;tc+=cn;tm+=ms.length;
    fyRows.push([f,c.srcOf(from,to),ms.length,avail,sold,pct1(sold,avail),rev,cn,adr,ratio(rev,avail),avail-sold,adr===null?null:(avail-sold)*adr]);}
  fyRows.push(tot(['Total','',tm,ta,ts,pct1(ts,ta),tr,tc,ratio(tr,tc),ratio(tr,ta),ta-ts,null]));
  const from=fyStart(fy)<c.dataStart?c.dataStart:fyStart(fy),to=fyEnd(fy)>c.end?c.end:fyEnd(fy);
  const typeRows:Cells[]=typesOf(c).map(t=>{const bs=c.bills.filter(b=>b.fy===fy&&b.type===t),avail=c.capacity(from,to,t),sold=c.sleptIn(from,to,t),cn=sum(bs,b=>b.cn),rev=sum(bs,b=>b.rev);
    return [t,fy,c.roomsOn(to).filter(r=>c.rtype.get(r)===t).length,avail,sold,pct1(sold,avail),rev,cn,ratio(rev,cn),ratio(rev,avail)];});
  const chans=[...new Set(c.bills.filter(b=>b.fy===fy).map(b=>b.channel))].sort((a,b)=>a===OTA_FREE?-1:b===OTA_FREE?1:a<b?-1:1),fyRev=sum(c.bills.filter(b=>b.fy===fy),b=>b.rev);
  const chanRows:Cells[]=chans.map(ch=>{const bs=c.bills.filter(b=>b.fy===fy&&b.channel===ch),rev=sum(bs,b=>b.rev),cn=sum(bs,b=>b.cn);return [fy,ch,bs.length,sum(bs,b=>b.nights),rev,ratio(rev,cn),pct1(rev,fyRev)];});
  return finish('rpt.study.revenue-kpi','Occupancy, ADR, RevPAR by month, FY, type, channel',p,[
    sec('month','KPI by Month',[['month','Month','text'],['src','Source','text'],['days','Days','int'],['rooms','Rooms in service','int'],['avail','Available room-nights','int'],['sold','Room-nights sold','int'],['occ','Occupancy %','pct'],
      ['bills','Bills','int'],['cn','Charged room-nights','int'],['rev','Room revenue (before GST)','money',true],['adr','ADR','money',true],['revpar','RevPAR','money',true]],month),
    sec('fy','KPI by FY',[['fy','FY','text'],['src','Source','text'],['months','Months','int'],['avail','Available room-nights','int'],['sold','Room-nights sold','int'],['occ','Occupancy %','pct'],['rev','Room revenue (before GST)','money',true],
      ['cn','Charged room-nights','int'],['adr','ADR','money',true],['revpar','RevPAR','money',true],['unsold','Unsold room-nights','int'],['unsoldValue','Value of unsold at ADR','money',true]],fyRows),
    sec('type','KPI by Room Type',[['type','Room type','text'],['fy','FY','text'],['rooms','Rooms','int'],['avail','Available room-nights','int'],['sold','Room-nights sold','int'],['occ','Occupancy %','pct'],
      ['rev','Room revenue','money',true],['cn','Charged room-nights','int'],['adr','ADR','money',true],['revpar','RevPAR','money',true]],typeRows),
    sec('channel','KPI by Channel',[['fy','FY','text'],['channel','Channel','text'],['bills','Bills','int'],['rn','Room-nights','int'],['rev','Room revenue','money',true],['adr','ADR','money',true],['share','Share of room revenue','pct']],chanRows),
    seriesSec('occupancy-by-month','bar','Occupancy % by month',month.map(r=>monthLabel(String(r[0]))),month.map(r=>r[6] as number|null),false,true)],c.srcOf(c.dataStart,c.end)!=='HCC live');
};

const isOta=(b:BM)=>b.channel!==OTA_FREE;
export const channelEconomics:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),fy=fyParam(c,p),comm=Number(p.commissionPct??15);
  const net:Cells[]=c.fys.map(f=>{const o=c.bills.filter(b=>b.fy===f&&isOta(b)),d=c.bills.filter(b=>b.fy===f&&!isOta(b)),orev=sum(o,b=>b.rev),ocn=sum(o,b=>b.cn),oadr=ratio(orev,ocn),dadr=ratio(sum(d,b=>b.rev),sum(d,b=>b.cn));
    const netAdr=oadr===null?null:ratio(oadr*(100-comm),100);
    return [f,sum(o,b=>b.nights),orev,oadr,dadr,ratio(orev*12,100),ratio(orev*15,100),ratio(orev*18,100),ratio(orev*20,100),netAdr,netAdr!==null&&dadr!==null?dadr-netAdr:null];});
  const mo:Cells[]=fyMonths(c,fy).map(m=>{const bs=c.bills.filter(b=>b.month===m),o=bs.filter(isOta),d=bs.filter(b=>!isOta(b)),or=sum(o,b=>b.rev),dr=sum(d,b=>b.rev);
    return [m,sum(d,b=>b.nights),sum(o,b=>b.nights),pct1(sum(d,b=>b.nights),sum(bs,b=>b.nights)),dr,or,pct1(dr,dr+or)];});
  const dep:Cells[]=c.fys.map(f=>{const bs=c.bills.filter(b=>b.fy===f),o=bs.filter(isOta),d=bs.filter(b=>!isOta(b)),orev=sum(o,b=>b.rev);
    return [f,bs.length,o.length,pct1(o.length,bs.length),sum(o,b=>b.nights),orev,pct1(orev,sum(bs,b=>b.rev)),ratio(orev,sum(o,b=>b.cn)),avg1(sum(o,b=>b.nights),o.length),avg1(sum(d,b=>b.nights),d.length)];});
  return finish('rpt.study.channel-economics','Channel economics and net ADR',p,[
    sec('net','Net ADR after Commission',[['fy','FY','text'],['otaRn','OTA room-nights','int'],['otaRev','OTA room revenue','money',true],['otaAdr','OTA ADR','money',true],['directAdr','Direct ADR','money',true],
      ['c12','Commission at 12%','money',true],['c15','at 15%','money',true],['c18','at 18%','money',true],['c20','at 20%','money',true],['netAdr','Net OTA ADR at the commission %','money',true],['gap','Direct ADR above net OTA ADR','money',true]],net),
    sec('direct','Direct Share by Month',[['month','Month','text'],['directRn','Direct room-nights','int'],['otaRn','OTA room-nights','int'],['directShare','Direct share of room-nights','pct'],['directRev','Direct room revenue','money',true],
      ['otaRev','OTA room revenue','money',true],['directRevShare','Direct share of room revenue','pct']],mo),
    sec('ota','OTA Dependence',[['fy','FY','text'],['bills','Bills','int'],['otaBills','OTA bills','int'],['otaShare','OTA share of bills','pct'],['otaRn','OTA room-nights','int'],['otaRev','OTA room revenue','money',true],
      ['otaRevShare','OTA share of room revenue','pct'],['otaAdr','OTA ADR','money',true],['otaNights','OTA avg nights per bill','int'],['directNights','Direct avg nights per bill','int']],dep),
    seriesSec('direct-share','line','Direct share of room-nights by month',mo.map(r=>monthLabel(String(r[0]))),mo.map(r=>r[3] as number|null),false,true)],true);
};

const LOS:[string,number,number][]=[['1 night',1,1],['2 nights',2,2],['3 nights',3,3],['4-7 nights',4,7],['8-14 nights',8,14],['15+ nights',15,1e9]];
const recentFys=(c:Ctx)=>c.fys.slice(-3);
export const pricingShape:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),fy=fyParam(c,p),rec=recentFys(c);
  const dowRows:Cells[]=DOW.map((n,i)=>{let sold=0,cap=0;for(const s of c.slept)if(!c.covid(s.date)&&dow(s.date)===i)sold++;
    for(let d=c.dataStart;d<=c.end;d=addDays(d,1))if(dow(d)===i&&!c.covid(d))cap+=c.roomsOn(d).length;
    const arr=c.bills.filter(b=>b.st&&b.cn>0&&dow(b.st.from)===i),ar=arr.filter(b=>rec.includes(b.fy));
    return [n,sold,cap,pct1(sold,cap),ratio(sum(arr,b=>b.rev),sum(arr,b=>b.cn)),ratio(sum(ar,b=>b.rev),sum(ar,b=>b.cn))];});
  const losRows:Cells[]=LOS.map(([n,lo,hi])=>{const ms=c.bills.filter(b=>b.nights>=lo&&b.nights<=hi&&b.rev>0),r=ms.filter(b=>rec.includes(b.fy));
    return [n,ms.length,sum(ms,b=>b.nights),ratio(sum(ms,b=>b.rev),sum(ms,b=>b.cn)),r.length,ratio(sum(r,b=>b.rev),sum(r,b=>b.cn)),pct1(sum(r,b=>b.b.discountPaise),sum(r,b=>b.gross))];});
  const fb=c.bills.filter(b=>b.fy===fy),segs=[...new Set(fb.map(b=>b.seg))].sort(),fyRev=sum(fb,b=>b.rev);
  const segRows:Cells[]=segs.map(s=>{const x=fb.filter(b=>b.seg===s);return [fy,s,x.length,sum(x,b=>b.nights),sum(x,b=>b.rev),ratio(sum(x,b=>b.rev),sum(x,b=>b.cn)),pct1(sum(x,b=>b.rev),fyRev)];});
  const ladder:Cells[]=[];
  for(const t of typesOf(c)){const rates=fb.filter(b=>b.type===t&&b.rev>0&&b.nights>0).map(b=>(ratio(b.rev,b.nights)!)).map(x=>Math.floor((x+50)/100)*100).sort((a,b)=>a-b);
    if(!rates.length)continue;const cnt=new Map<number,number>();for(const r of rates)cnt.set(r,(cnt.get(r)??0)+1);
    let modal=rates[0],best=0;for(const [r,n] of [...cnt].sort((a,b)=>a[0]-b[0]))if(n>best){best=n;modal=r;}
    const below=rates.filter(x=>x*100<modal*95),n=rates.length;
    ladder.push([t,fy,n,modal,rates[Math.floor(n/10)],rates[Math.floor(n/2)],rates[Math.floor(9*n/10)],pct1(below.length,n),below.length?ratio(sum(below,x=>modal-x),below.length):null]);}
  const up:Cells[]=[];
  for(const f of c.fys){const ss=c.stays.filter(s=>fyOf(s.from)===f);const typed=ss.filter(s=>s.bookedType),rank=(t:string)=>TYPES.indexOf(t)<0?0:4-TYPES.indexOf(t);
    const ups=typed.filter(s=>rank(c.rtype.get(s.room)??'')>rank(s.bookedType!)&&rank(s.bookedType!)>0).length,downs=typed.filter(s=>rank(c.rtype.get(s.room)??'')<rank(s.bookedType!)&&rank(c.rtype.get(s.room)??'')>0).length;
    up.push([f,ss.length,typed.length,ups,pct1(ups,typed.length),downs]);}
  const firstRcp=new Map<string,string>();for(const r of inp.receipts){const b=inp.bills.find(x=>x.id===r.bill);if(!b)continue;const k=b.stay;if(!firstRcp.has(k)||r.date<firstRcp.get(k)!)firstRcp.set(k,r.date);}
  const bucket=new Map<string,number>(),B=['No receipt linked','Paid on / after arrival','1-3 days before','4-7 days before','8-30 days before','31+ days before'];
  for(const s of c.stays.filter(s=>fyOf(s.from)===fy)){const f=firstRcp.get(s.id);let k=B[0];if(f){const g=Math.round((Date.parse(s.from)-Date.parse(f))/864e5);k=g<=0?B[1]:g<=3?B[2]:g<=7?B[3]:g<=30?B[4]:B[5];}bucket.set(k,(bucket.get(k)??0)+1);}
  const nStays=sum([...bucket.values()],x=>x),win:Cells[]=B.filter(k=>bucket.has(k)).map(k=>[k,bucket.get(k)!,pct1(bucket.get(k)!,nStays)]);
  return finish('rpt.study.pricing-shape','Day-of-week, length-of-stay, segment mix, rate ladder, upgrades',p,[
    sec('dow','DOW & LOS Pricing: night of week',[['night','Night','text'],['sold','Room-nights sold (normal years)','int'],['cap','Capacity (normal years)','int'],['occ','Occupancy %','pct'],['adr','ADR by arrival day, all years','money',true],['adrRecent','ADR by arrival day, last 3 FYs','money',true]],dowRows),
    sec('los','DOW & LOS Pricing: length of stay',[['bucket','Length of stay','text'],['bills','Bills','int'],['rn','Room-nights','int'],['adr','ADR','money',true],['billsRecent','Bills, last 3 FYs','int'],['adrRecent','ADR, last 3 FYs','money',true],['discPct','Discount % of gross, last 3 FYs','pct']],losRows),
    sec('segment','Segment Mix',[['fy','FY','text'],['segment','Segment','text'],['bills','Bills','int'],['rn','Room-nights','int'],['rev','Room revenue','money',true],['adr','ADR','money',true],['share','Share of room revenue','pct']],segRows),
    sec('ladder','Rate Ladder',[['type','Room type','text'],['fy','FY','text'],['bills','Bills','int'],['modal','Modal rate (rack proxy)','money',true],['p10','P10','money',true],['p50','Median','money',true],['p90','P90','money',true],['below','Bills more than 5% below modal','pct'],['cut','Avg cut below modal','money',true]],ladder),
    sec('upgrades','Upgrades & Shifts',[['fy','FY (arrival)','text'],['stays','Stays','int'],['typed','Stays with a booked type','int'],['ups','Upgrades','int'],['upPct','Upgrade %','pct'],['downs','Downgrades','int']],up),
    sec('window','Booking Window (first receipt vs arrival)',[['bucket','First receipt vs arrival','text'],['stays','Stays','int'],['share','Share','pct']],win),
    seriesSec('dow-occupancy','bar','Occupancy % by night of week',DOW,dowRows.map(r=>r[3] as number|null),false,true)],true);
};
void noData;

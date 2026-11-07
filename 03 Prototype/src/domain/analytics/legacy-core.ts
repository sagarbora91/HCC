// F12 MEAS · meas-study-a · shared core of the legacy study ports (MEAS-CONTRACT §9, plan 7A). Pure functions over AnInput; the C# twin is
// 08 Production/src/Hcc.Reporting/Analytics/LegacyCore.cs (same names, same integer arithmetic, same numbers).
// Definitions (workbook Notes & Checks): a stay occupies `nights` nights from its ARRIVAL date, one room each (nights x rooms); same-day stays count 1;
// occupancy = distinct (room, night) pairs slept / (rooms in service x days in the data window); ADR = room charge before GST / charged nights, in the
// bill month; FY April-March; room 211 = Suite; COVID window excluded from "normal years"; halls / duplicates are already out of AnInput.
import type {RptCell,RptColumn,RptRow} from '../reports/types';
import type {AnInput,AnStay,AnBill,AnRoom} from './types';

export type Tbl={key:string;title:string;columns:RptColumn[];rows:RptRow[]};
export const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
export const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
export const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const P=(key:string,label:string):RptColumn=>({key,label,type:'pct',money:false});

// ── dates (pure UTC arithmetic, no clock) ──
export const addDays=(d:string,n:number)=>new Date(Date.parse(d+'T12:00:00Z')+n*86400000).toISOString().slice(0,10);
export const fyOf=(d:string)=>{const y=+d.slice(0,4),m=+d.slice(5,7),s=m>=4?y:y-1;return `${s}-${String((s+1)%100).padStart(2,'0')}`;};
export const fyStartYear=(fy:string)=>+fy.slice(0,4);
export const fyPrev=(fy:string)=>{const y=fyStartYear(fy)-1;return `${y}-${String(y%100+1).padStart(2,'0')}`;};
export const monthOf=(d:string)=>d.slice(0,7);
export const daysInMonth=(m:string)=>new Date(Date.UTC(+m.slice(0,4),+m.slice(5,7),0)).getUTCDate();
export const monthStart=(m:string)=>m+'-01';
export const monthEnd=(m:string)=>m+'-'+String(daysInMonth(m)).padStart(2,'0');
export const monthNext=(m:string)=>{const y=+m.slice(0,4),mo=+m.slice(5,7);return mo===12?`${y+1}-01`:`${y}-${String(mo+1).padStart(2,'0')}`;};
/** 0 = Sunday … 6 = Saturday (the workbook's weekday index order Sun-first is used only for labels). */
export const dow=(d:string)=>new Date(Date.parse(d+'T12:00:00Z')).getUTCDay();
export const DOW_LABEL=['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];
export const FY_MONTHS=['04','05','06','07','08','09','10','11','12','01','02','03'];
export const MON_LABEL:Record<string,string>={'01':'Jan','02':'Feb','03':'Mar','04':'Apr','05':'May','06':'Jun','07':'Jul','08':'Aug','09':'Sep','10':'Oct','11':'Nov','12':'Dec'};
export const sortedUnique=(xs:string[])=>[...new Set(xs)].sort();

// ── integer arithmetic shared with C# ──
/** Round-half-up integer division of non-negative-or-negative a by positive b. */
export const rdiv=(a:number,b:number)=>b===0?0:Math.floor((2*a+b)/(2*b));
/** Percent with one decimal from n / d (0 when d = 0). */
export const pct1=(n:number,d:number)=>d===0?0:Math.floor((n*2000+d)/(2*d))/10;
/** Index with one decimal: value / base x 100. */
export const idx1=(v:number,base:number)=>pct1(v,base);
export const sum=(xs:number[])=>xs.reduce((a,b)=>a+b,0);

// ── classification ──
const NOT_SLEPT=['Cancelled','No show','Held','Expired','Duplicate','Confirmed'];
export const slept=(s:AnStay)=>!NOT_SLEPT.includes(s.status);
export const nightsOf=(s:AnStay)=>Math.max(1,s.nights);
export const inCovid=(i:AnInput,m:string)=>monthEnd(m)>=i.covidWindow.from&&monthStart(m)<=i.covidWindow.to;
export const isLegacyDate=(i:AnInput,d:string)=>d<=i.legacyUntil;
export function marker(i:AnInput,from:string,to:string){const a=from<=i.legacyUntil,b=to>i.legacyUntil;return a&&b?'legacy + live':a?'legacy':'live';}
export function roomType(i:AnInput,room:string){const r=i.rooms.find(x=>x.id===room);return r?(r.suite||room==='211'?'Suite':r.type):'unassigned';}

/** Every (room, night) slept, clipped to the data window [dataStart, asOf]. */
export type Night={room:string;date:string;stay:AnStay};
export function nights(i:AnInput):Night[]{
  const out:Night[]=[];
  for(const s of i.stays)if(slept(s))for(let k=0;k<nightsOf(s);k++){const d=addDays(s.from,k);if(d<=i.asOf)out.push({room:s.room,date:d,stay:s});}
  return out;
}
export function dataStart(i:AnInput){let m='9999-12-31';for(const s of i.stays)if(slept(s)&&s.from<m)m=s.from;for(const b of i.bills)if(b.date<m)m=b.date;return m;}
export const roomsInService=(i:AnInput,d:string)=>i.rooms.filter(r=>r.inServiceFrom<=d&&(!r.inServiceTo||r.inServiceTo>=d)).length;

export type OccMonth={month:string;days:number;sold:number;capacity:number;roomsSold:number;capacityA:number;capacityB:number;stays:number;sameDay:number;nightsArrived:number};
/** Occupancy per calendar month with the workbook's three denominators: headline (rooms in service), A (rooms that sold in the month), B (fixed room count). */
export function occupancyMonths(i:AnInput):OccMonth[]{
  const ds=dataStart(i);if(ds>i.asOf)return [];
  const ns=nights(i);const by=new Map<string,Night[]>();for(const n of ns){const m=monthOf(n.date);(by.get(m)??by.set(m,[]).get(m)!).push(n);}
  const out:OccMonth[]=[];
  for(let m=monthOf(ds);m<=monthOf(i.asOf);m=monthNext(m)){
    const from=m===monthOf(ds)?ds:monthStart(m),to=monthEnd(m)>i.asOf?i.asOf:monthEnd(m);
    let days=0,capacity=0;for(let d=from;d<=to;d=addDays(d,1)){days++;capacity+=roomsInService(i,d);}
    const list=by.get(m)??[];const sold=new Set(list.map(n=>n.room+'|'+n.date)).size;const roomsSold=new Set(list.map(n=>n.room)).size;
    const arrived=i.stays.filter(s=>slept(s)&&monthOf(s.from)===m);
    out.push({month:m,days,sold,capacity,roomsSold,capacityA:roomsSold*days,capacityB:i.rooms.length*days,stays:arrived.length,sameDay:arrived.filter(s=>s.nights<=0||s.from===s.to).length,nightsArrived:sum(arrived.map(nightsOf))});
  }
  return out;
}

export type RevMonth={month:string;bills:number;room:number;fnb:number;other:number;discount:number;gst:number;total:number;chargedNights:number;received:number;paid:number};
/** Billed / received per calendar month (bill month; receipts by receipt date). */
export function revenueMonths(i:AnInput):RevMonth[]{
  const m=new Map<string,RevMonth>();const get=(k:string)=>m.get(k)??(m.set(k,{month:k,bills:0,room:0,fnb:0,other:0,discount:0,gst:0,total:0,chargedNights:0,received:0,paid:0}),m.get(k)!);
  for(const b of i.bills){const r=get(monthOf(b.date));r.bills++;r.room+=b.roomChargePaise;r.fnb+=b.fnbPaise;r.other+=b.otherPaise;r.discount+=b.discountPaise;r.gst+=b.gstPaise;r.total+=b.totalPaise;r.chargedNights+=b.chargedNights;r.paid+=b.paidPaise;}
  for(const x of i.receipts)get(monthOf(x.date)).received+=x.amountPaise;
  return [...m.values()].sort((a,b)=>a.month<b.month?-1:1);
}
export const net=(r:{room:number;fnb:number;other:number;discount:number})=>r.room+r.fnb+r.other-r.discount;
export const adr=(r:{room:number;chargedNights:number})=>rdiv(r.room,r.chargedNights);

export type FyRow=RevMonth&{fy:string;sold:number;capacity:number;days:number;normal:boolean;partial:boolean};
export function fyRows(i:AnInput):FyRow[]{
  const rev=revenueMonths(i),occ=occupancyMonths(i);const fys=sortedUnique([...rev.map(r=>fyOf(monthStart(r.month))),...occ.map(o=>fyOf(monthStart(o.month)))]);
  return fys.map(fy=>{
    const rs=rev.filter(r=>fyOf(monthStart(r.month))===fy),os=occ.filter(o=>fyOf(monthStart(o.month))===fy);
    const t:RevMonth={month:fy,bills:0,room:0,fnb:0,other:0,discount:0,gst:0,total:0,chargedNights:0,received:0,paid:0};
    for(const r of rs){t.bills+=r.bills;t.room+=r.room;t.fnb+=r.fnb;t.other+=r.other;t.discount+=r.discount;t.gst+=r.gst;t.total+=r.total;t.chargedNights+=r.chargedNights;t.received+=r.received;t.paid+=r.paid;}
    const months=sortedUnique(os.map(o=>o.month));const full=months.length===12&&os.every(o=>o.days===daysInMonth(o.month));
    return {...t,fy,sold:sum(os.map(o=>o.sold)),capacity:sum(os.map(o=>o.capacity)),days:sum(os.map(o=>o.days)),normal:!os.some(o=>inCovid(i,o.month))&&full,partial:!full};
  });
}

// ── table helpers ──
export const row=(cells:Record<string,RptCell>,kind:RptRow['kind']='row'):RptRow=>({kind,cells});
export const tbl=(key:string,title:string,columns:RptColumn[],rows:RptRow[]):Tbl=>({key,title,columns,rows});
/** A chart series block for meas-study-pack's ChartBlock: section key `series.<name>`, title `<kind>|<title>`, columns label / value (paise, counts or tenths of a percent as `pct`). */
export function series(name:string,kind:'bar'|'line'|'stacked',title:string,labels:string[],values:number[],money:boolean,pct=false):Tbl{
  return tbl('series.'+name,`${kind}|${title}`,[T('label','Label'),{key:'value',label:'Value',type:pct?'pct':money?'money':'int',money}],labels.map((l,k)=>row({label:l,value:values[k]})));
}
export const NOTE=(key:string,text:string):Tbl=>tbl('note.'+key,'Note',[T('note','Note')],[row({note:text})]);
export const legacyFooter='Legacy to 30 Oct 2026 / HCC live';
/** Wraps tables as the selector result. */
export function result(id:string,title:string,params:Record<string,string|boolean|undefined>,tables:Tbl[],i:AnInput,from?:string,to?:string):import('../reports/types').RptResult{
  const main=tables[0];
  return {id,title,params:params as never,columns:main.columns,rows:main.rows,sections:tables.map(t=>({key:t.key,title:t.title,columns:t.columns,rows:t.rows})),
    about:{runAt:'',user:'',dateBasis:`Legacy history to ${i.legacyUntil} / HCC live. ${legacyFooter}`,provisional:false,legacy:true,...(from?{from}:{}),...(to?{to}:{})}};
}
export function focusFy(i:AnInput,p:{fy?:string}){return p.fy??fyOf(i.asOf);}
export function billStay(i:AnInput):Map<string,AnStay>{return new Map(i.stays.map(s=>[s.id,s]));}
export type {AnBill,AnRoom};

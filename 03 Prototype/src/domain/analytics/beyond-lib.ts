// F12 MEAS · meas-study-b · shared derivations of the "beyond" study ports (MEAS-CONTRACT §9, plan 7A). Pure: AnInput in, tables out.
// Definitions are those of the workbooks' Notes & Checks sheets (E:\Projects\HCC\Existing Software\Reports\beyond_lib.py):
//  occupancy = distinct room-nights slept / (rooms in service x days); ADR = room charge before GST / charged nights on bills (bill month);
//  RevPAR = room revenue / available room-nights; FY April-March; COVID window excluded from "normal years"; halls and duplicate stays are
//  already excluded by the loader; room 211 = Suite. Money = integer paise. Ratios use integer half-up arithmetic so the C# twin gives identical numbers.
import type {RptColumn,RptRow,RptCell,RptResult,RptBoundParams} from '../reports/types';
import type {AnInput,AnBill,AnStay,AnParams} from './types';
import {addDays} from '../money';

/** Optional stay facts a loader may know (legacy extract or HCC masters). Absent = the table says "not in this input", it is never guessed. */
export type BStay=AnStay&{bookedType?:string;city?:string;state?:string;nationality?:string;mealPlan?:string;inTime?:string;outTime?:string;idCaptured?:boolean;phoneCaptured?:boolean;people?:number};
export type BeyondExtras={hallEvents?:{date:string;days:number;revenuePaise:number}[];expenses?:{date:string;category:string;amountPaise:number}[];
  audit?:{action:string;count:number}[];licences?:{name:string;validTo?:string}[]};
export const extrasOf=(p:AnParams):BeyondExtras=>(p.extras as BeyondExtras|undefined)??{};

// ───────────── integer maths (half-up; identical in C#) ─────────────
/** Percent to 1 dp (n/d x 100). */
export const pct1=(n:number,d:number):number|null=>d>0?Math.floor((n*2000+d)/(2*d))/10:null;
/** Integer ratio n/d, half-up (paise per night, etc.). */
export const ratio=(n:number,d:number):number|null=>d>0?Math.floor((2*n+d)/(2*d)):null;
/** Ratio to 1 dp. */
export const avg1=(n:number,d:number):number|null=>d>0?Math.floor((n*20+d)/(2*d))/10:null;
/** Index to 1 dp: part / whole x 100 where both are ratios held as numerator/denominator pairs. */
export const idx1=(an:number,ad:number,bn:number,bd:number):number|null=>ad>0&&bn>0?Math.floor((an*bd*2000+ad*bn)/(2*ad*bn))/10:null;

// ───────────── dates ─────────────
export const fyOf=(d:string)=>{const y=+d.slice(0,4),m=+d.slice(5,7),s=m>=4?y:y-1;return `${s}-${String((s+1)%100).padStart(2,'0')}`;};
export const fyStart=(fy:string)=>fy.slice(0,4)+'-04-01';
export const fyEnd=(fy:string)=>(+fy.slice(0,4)+1)+'-03-31';
export const monthEnd=(m:string)=>new Date(Date.UTC(+m.slice(0,4),+m.slice(5,7),0)).toISOString().slice(0,10);
export const nextMonth=(m:string)=>{const y=+m.slice(0,4),mo=+m.slice(5,7);return mo===12?`${y+1}-01`:`${y}-${String(mo+1).padStart(2,'0')}`;};
export const prevMonth=(m:string)=>{const y=+m.slice(0,4),mo=+m.slice(5,7);return mo===1?`${y-1}-12`:`${y}-${String(mo-1).padStart(2,'0')}`;};
/** 0 = Monday … 6 = Sunday. */
export const dow=(d:string)=>(new Date(d+'T12:00:00Z').getUTCDay()+6)%7;
export const DOW=['Mon','Tue','Wed','Thu','Fri','Sat','Sun'];
export const MON_FY=['Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec','Jan','Feb','Mar'];
export const OTA_FREE='Direct / walk-in';

// ───────────── table helpers ─────────────
export type ColSpec=[key:string,label:string,type:RptColumn['type'],money?:boolean,mask?:RptColumn['mask']];
export const colsOf=(specs:ColSpec[]):RptColumn[]=>specs.map(([key,label,type,money,mask])=>({key,label,type,money:!!money,...(mask?{mask}:{})}));
export type Sec=NonNullable<RptResult['sections']>[number];
export type Cells=RptCell[];
export const tot=(cells:Cells)=>({total:cells});
export function sec(key:string,title:string,spec:ColSpec[],rows:(Cells|{total:Cells})[]):Sec{
  const columns=colsOf(spec);
  return {key,title,columns,rows:rows.map<RptRow>(r=>{const total=!Array.isArray(r),c=total?(r as {total:Cells}).total:(r as Cells);
    const cells:Record<string,RptCell>={};columns.forEach((k,i)=>{cells[k.key]=c[i]??null;});return {kind:total?'total':'row',cells};})};
}
/** A chart series for meas-study-pack's ChartBlock: a section keyed `series:<name>` with columns label / value (kind in the title prefix `[bar]`, `[line]`, `[stack]`). */
export const seriesSec=(name:string,kind:'bar'|'line'|'stack',title:string,labels:string[],values:(number|null)[],money=false,pctValues=false):Sec=>
  sec('series:'+name,`[${kind}] ${title}`,[['label','Label','text'],['value',title,pctValues?'pct':money?'money':'int',money]],labels.map((l,i)=>[l,values[i]??null]));
export function finish(id:string,title:string,p:AnParams,secs:Sec[],legacy:boolean):RptResult{
  const params:RptBoundParams={};for(const [k,v] of Object.entries(p))if(typeof v==='string'||typeof v==='boolean')params[k]=v;
  return {id,title,params,columns:secs[0].columns,rows:secs[0].rows,sections:secs,about:{runAt:'',user:'',dateBasis:'',provisional:false,legacy}};
}
export const noData=(key:string,title:string,why:string):Sec=>sec(key,title,[['note','Note','text']],[[why]]);

// ───────────── context ─────────────
export type BM={b:AnBill;st?:BStay;date:string;month:string;fy:string;type:string;channel:string;rev:number;cn:number;nights:number;gross:number;seg:string};
export type Ctx={inp:AnInput;stays:BStay[];stayBy:Map<string,BStay>;rtype:Map<string,string>;slept:{room:string;date:string;type:string;stay:string}[];
  dataStart:string;end:string;fys:string[];bills:BM[];roomsOn:(d:string)=>string[];capacity:(from:string,to:string,type?:string,normalOnly?:boolean)=>number;
  covid:(d:string)=>boolean;srcOf:(from:string,to:string)=>string;sleptIn:(from:string,to:string,type?:string,normalOnly?:boolean)=>number};
const CACHE=new WeakMap<AnInput,Ctx>();
const NOT_SLEPT=new Set(['cancelled','canceled','no-show','noshow','no show','duplicate','expired','held','confirmed']);
export const monthsBetween=(from:string,to:string):string[]=>{const out:string[]=[];for(let m=from.slice(0,7);m<=to.slice(0,7);m=nextMonth(m))out.push(m);return out;};

export function ctxOf(inp:AnInput):Ctx{
  const hit=CACHE.get(inp);if(hit)return hit;
  const stays=inp.stays as BStay[],stayBy=new Map(stays.map(s=>[s.id,s])),rtype=new Map(inp.rooms.map(r=>[r.id,r.suite?'Suite':r.type]));
  const seen=new Set<string>(),slept:Ctx['slept']=[];
  for(const st of stays){
    if(NOT_SLEPT.has(String(st.status).toLowerCase())||!rtype.has(st.room))continue;
    const n=Math.max(st.nights,1);
    for(let i=0;i<n;i++){const date=addDays(st.from,i),k=st.room+'|'+date;if(date>inp.asOf||seen.has(k))continue;seen.add(k);slept.push({room:st.room,date,type:rtype.get(st.room)!,stay:st.id});}
  }
  let dataStart=inp.asOf;for(const s of slept)if(s.date<dataStart)dataStart=s.date;
  for(const b of inp.bills)if(b.date<dataStart&&b.roomChargePaise>0)dataStart=b.date;
  const end=inp.asOf;
  const fys:string[]=[];for(let d=dataStart;d<=end;d=addDays(d,28)){const f=fyOf(d);if(!fys.includes(f))fys.push(f);}if(!fys.includes(fyOf(end)))fys.push(fyOf(end));
  const covid=(d:string)=>d>=inp.covidWindow.from&&d<=inp.covidWindow.to;
  const onCache=new Map<string,string[]>();
  const roomsOn=(d:string)=>{let r=onCache.get(d);if(!r){r=inp.rooms.filter(x=>x.inServiceFrom<=d&&(!x.inServiceTo||d<=x.inServiceTo)).map(x=>x.id);onCache.set(d,r);}return r;};
  const capacity=(from:string,to:string,type?:string,normalOnly=false)=>{
    let n=0;for(let d=from<dataStart?dataStart:from,e=to>end?end:to;d<=e;d=addDays(d,1)){if(normalOnly&&covid(d))continue;
      const rs=roomsOn(d);n+=type?rs.filter(r=>rtype.get(r)===type).length:rs.length;}return n;};
  const sleptIn=(from:string,to:string,type?:string,normalOnly=false)=>slept.filter(s=>s.date>=from&&s.date<=to&&(!type||s.type===type)&&!(normalOnly&&covid(s.date))).length;
  const srcOf=(from:string,to:string)=>to<=inp.legacyUntil?'Legacy':from>inp.legacyUntil?'HCC live':'Legacy + live';
  // group = 3+ rooms of the same guest arriving together; used by the segment rule
  const groupSize=new Map<string,number>();for(const s of stays){const k=s.guestKey+'|'+s.from+'|'+s.to;groupSize.set(k,(groupSize.get(k)??0)+1);}
  const bills:BM[]=inp.bills.map(b=>{
    const st=stayBy.get(b.stay),channel=st?.channel&&st.channel!==''?st.channel:OTA_FREE,gross=b.roomChargePaise+b.fnbPaise+b.otherPaise;
    const seg=!st?OTA_FREE:b.chargedNights>=7?'Long stay (7+ nights)':(groupSize.get(st.guestKey+'|'+st.from+'|'+st.to)??1)>=3?'Group (3+ rooms)':st.company?'Corporate (company on bill)':
      (st.purpose??'').toLowerCase()==='official'?'Official (work purpose)':channel!==OTA_FREE?'OTA ('+channel+')':OTA_FREE;
    return {b,st,date:b.date,month:b.date.slice(0,7),fy:fyOf(b.date),type:st?rtype.get(st.room)??'':'',channel,rev:b.roomChargePaise,cn:b.roomChargePaise>0?b.chargedNights:0,nights:b.chargedNights,gross,seg};
  });
  const c:Ctx={inp,stays,stayBy,rtype,slept,dataStart,end,fys,bills,roomsOn,capacity,covid,srcOf,sleptIn};CACHE.set(inp,c);return c;
}
/** The months of `fy` that have data (first month starts at the data start, last ends at the business date). */
export const fyMonths=(c:Ctx,fy:string)=>monthsBetween(fyStart(fy),fyEnd(fy)).filter(m=>monthEnd(m)>=c.dataStart&&m+'-01'<=c.end);
export const fyParam=(c:Ctx,p:AnParams)=>p.fy??fyOf(c.end);
export const sum=<T,>(xs:T[],f:(x:T)=>number)=>xs.reduce((a,x)=>a+f(x),0);
export const hasLegacy=(c:Ctx,from:string)=>from<=c.inp.legacyUntil;
export const monthLabel=(m:string)=>MON_FY[(+m.slice(5,7)+8)%12]+' '+m.slice(2,4);
export const norm=(s:string|undefined)=>(s??'').trim();

/** Occupancy / ADR / RevPAR block shared by several reports. */
export type Kpi={avail:number;sold:number;bills:number;cn:number;rev:number};
export function kpiOf(c:Ctx,from:string,to:string,pick:(m:BM)=>boolean=()=>true,type?:string):Kpi{
  const ms=c.bills.filter(m=>m.date>=from&&m.date<=to&&pick(m));
  return {avail:c.capacity(from,to,type),sold:c.sleptIn(from,to,type),bills:ms.length,cn:sum(ms,m=>m.cn),rev:sum(ms,m=>m.rev)};
}

// ───────────── State → AnInput (prototype loader; the server twin reads the same shape from SQL + lgc) ─────────────
import type {State} from '../types';
const GST_BPS=(l:{amount:number;taxBps:number})=>Math.round(l.amount*l.taxBps/10000);
export function anInputFromState(s:State):AnInput{
  const rooms=s.rooms.map(r=>({id:r.id,type:r.type,inServiceFrom:'2000-01-01',suite:r.type.toLowerCase().includes('suite')||r.id==='211'}));
  const stays:BStay[]=[],bills:AnBill[]=[],receipts:AnInput['receipts']=[];
  const gmap=new Map((s.guests??[]).map(g=>[g.id,g]));
  for(const b of s.bookings){
    if(b.status!=='In house'&&b.status!=='Departed')continue;
    if(b.hallEventId||b.arrival>=b.departure)continue;
    const n=Math.max(1,Math.round((Date.parse(b.departure)-Date.parse(b.arrival))/86400000));
    const g=b.guestId?gmap.get(b.guestId):undefined,ch=b.source==='OTA'?(b.otaChannel||'OTA'):OTA_FREE;
    const addr=(g?.address??'').split(',').map(x=>x.trim()).filter(Boolean);
    stays.push({id:b.id,guestKey:b.guestId??b.phone??b.guest.trim().toLowerCase(),room:b.room,from:b.arrival,to:b.departure,nights:n,ratePaise:b.rate,source:b.source,channel:ch===OTA_FREE?'':ch,
      company:b.company?.name,status:b.status==='Departed'?'departed':'in house',legacy:false,mealPlan:b.plan,nationality:g?.nationality,state:addr.length>1?addr[addr.length-1]:undefined,
      city:addr.length>1?addr[addr.length-2]:undefined,idCaptured:!!(g&&(g.idLast4||g.idNumber)),phoneCaptured:!!(b.phone||g?.phone),people:b.people});
    let room=0,fnb=0,other=0,disc=0,gst=0;
    for(const l of b.lines){gst+=GST_BPS(l);if(l.kind==='room')room+=l.amount;else if(l.kind==='food')fnb+=l.amount;else if(l.kind==='discount')disc+=Math.abs(l.amount);else other+=l.amount;}
    const paid=b.receipts.filter(r=>r.kind==='payment'||r.kind==='deposit'||r.kind==='deposit-apply'||r.kind==='company-credit'||r.kind==='ota-credit').reduce((a,r)=>a+r.amount,0)
      -b.receipts.filter(r=>r.kind==='refund'||r.kind==='deposit-return').reduce((a,r)=>a+r.amount,0);
    const date=b.status==='Departed'?b.departure:s.date;
    bills.push({id:'B-'+b.id,stay:b.id,date,roomChargePaise:room,fnbPaise:fnb,otherPaise:other,discountPaise:disc,gstPaise:gst,totalPaise:room+fnb+other-disc+gst,chargedNights:room>0?n:0,paidPaise:paid,legacy:false});
    for(const r of b.receipts)if(r.kind==='payment'||r.kind==='deposit')receipts.push({id:r.id,bill:'B-'+b.id,date:r.date,method:r.method,amountPaise:r.amount,legacy:false});
  }
  return {rooms,stays,bills,receipts,asOf:s.date,legacyUntil:'2026-10-30',covidWindow:{from:'2020-03-01',to:'2021-03-31'}};
}
/** Extras the prototype can supply from State (expenses, audit counts); hall events and licences come from their modules when present. */
export function extrasFromState(s:State):BeyondExtras{
  const audit=new Map<string,number>();for(const a of s.audit){const k=String((a as {action?:string}).action??a.detail.split(' · ')[0]).slice(0,40);audit.set(k,(audit.get(k)??0)+1);}
  return {expenses:s.expenses.filter(e=>e.status==='Posted').map(e=>({date:e.date,category:e.category,amountPaise:e.amount})),
    audit:[...audit].sort((a,b)=>b[1]-a[1]||(a[0]<b[0]?-1:1)).map(([action,count])=>({action,count}))};
}

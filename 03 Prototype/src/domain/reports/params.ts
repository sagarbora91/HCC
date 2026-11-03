// F7-R1 · parameter binding by date class (RPT-ARCHITECTURE §3.1, catalogue §1.4). Defaults come from the business date (s.date), never the wall clock.
import type {State} from '../types';
import type {RptDefinition,RptBoundParams,RptDateClass} from './types';
import {HccError} from '../errors';
import {addDays} from '../money';

/** First business date of HCC's own data (FY 2026-27); anything earlier reads the legacy Lodge history (LG contract). */
export const RPT_LEGACY_BEFORE='2026-04-01';
/** Longest range per date class, in days (inclusive). */
export const RPT_MAX_DAYS:Partial<Record<RptDateClass,number>>={'D-DAY':1,'D-S':62,'D-B':366,'D-F':366,'D-M':31,'D-FY':366,'D-FWD':366};
const ISO=/^\d{4}-\d{2}-\d{2}$/;
const days=(a:string,b:string)=>Math.round((Date.parse(b)-Date.parse(a))/86400000)+1;
const bad=(m:string):never=>{throw new HccError('VALIDATION',m);};
const monthEnd=(m:string)=>{const [y,mo]=m.split('-').map(Number);return new Date(Date.UTC(y,mo,0)).toISOString().slice(0,10);};
/** FY of a date: '2026-27' for 2026-04-01 … 2027-03-31. */
export const rptFyOf=(d:string)=>{const y=+d.slice(0,4),m=+d.slice(5,7),s=m>=4?y:y-1;return `${s}-${String((s+1)%100).padStart(2,'0')}`;};

type Raw=Record<string,unknown>;
/** Binds raw input (query string / UI form) to a definition. Throws VALIDATION with a plain-English message. */
export function bindParams(def:Pick<RptDefinition,'id'|'params'|'dateClass'>,raw:Raw,s:Pick<State,'date'>):RptBoundParams&{legacy:boolean}{
  const today=s.date;const str=(k:string)=>raw[k]===undefined||raw[k]===null||raw[k]===''?undefined:String(raw[k]);
  const out:RptBoundParams={};
  const dc=def.dateClass;
  let from:string|undefined,to:string|undefined;
  if(dc==='D-M'){
    const m=str('month')??today.slice(0,7);if(!/^\d{4}-(0[1-9]|1[0-2])$/.test(m))bad('Choose a month (YYYY-MM).');
    from=m+'-01';to=monthEnd(m);if(to>today&&from<=today)to=today;out.month=m;
  }else if(dc==='D-FY'){
    const fy=str('fy')??rptFyOf(today);if(!/^\d{4}-\d{2}$/.test(fy)||(+fy.slice(0,4)+1)%100!==+fy.slice(5))bad('Choose a financial year (for example 2026-27).');
    from=fy.slice(0,4)+'-04-01';to=(+fy.slice(0,4)+1)+'-03-31';if(to>today&&from<=today)to=today;out.fy=fy;
  }else if(dc==='D-ASOF'){
    to=str('asOf')??str('to')??today;from=undefined;out.asOf=to;
  }else if(dc&&dc!=='D-NOW'){
    from=str('from')??(dc==='D-FWD'?today:today);to=str('to')??(dc==='D-FWD'?addDays(today,6):from);
  }
  for(const d of [from,to])if(d!==undefined&&(!ISO.test(d)||Number.isNaN(Date.parse(d))))bad('Dates are YYYY-MM-DD.');
  if(from&&to&&from>to)bad('The From date is after the To date.');
  if(dc!=='D-FWD'&&to&&to>today)bad(`The report cannot run for a date after the business date (${today}).`);
  if(dc==='D-DAY'&&from!==to)bad('This report runs for one business date.');
  const max=dc?RPT_MAX_DAYS[dc]:undefined;
  if(max&&from&&to&&days(from,to)>max)bad(`The range is longer than ${max} days. Narrow the range.`);
  if(from)out.from=from;if(to)out.to=to;
  for(const p of def.params){
    if(['from','to','month','fy','asOf'].includes(p.key))continue;
    const v=str(p.key)??p.default;
    if(v===undefined){if(p.required)bad(`${p.label} is required.`);continue;}
    if(p.type==='bool'){out[p.key]=raw[p.key]===true||v==='true'||v==='1'||v==='yes';continue;}
    if(p.type==='enum'&&p.options&&!p.options.includes(v))bad(`${p.label}: choose one of ${p.options.join(', ')}.`);
    if(p.type==='date'&&!ISO.test(v))bad(`${p.label}: YYYY-MM-DD.`);
    if(v.length>120)bad(`${p.label} is too long.`);
    out[p.key]=v;
  }
  return {...out,legacy:!!from&&from<RPT_LEGACY_BEFORE||(!from&&!!to&&to<RPT_LEGACY_BEFORE)};
}
/** Raw text form of bound parameters (for the run / export log and the About sheet). */
export const paramsText=(p:RptBoundParams)=>Object.fromEntries(Object.entries(p).filter(([k,v])=>v!==undefined&&k!=='legacy').map(([k,v])=>[k,String(v)])) as Record<string,string>;

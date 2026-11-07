// F12-RPT-DAILY · shared helpers of the nine daily-control measurement reports (MEAS-CONTRACT §4, catalogue §3.10). Pure; read-only over State.
// Every report keeps its exception rule in an exported pure function `…Rows(s,p)` returning typed rows (cells + `exception` text, '' = not red):
// the report's `select`, the night-audit lines (night-audit.ts) and the Owner digest provider all call the same function.
import type {State} from '../../types';
import type {RptColumn,RptRow,RptCell} from '../types';
import {reportSellableOn} from '../../streams/closing';
import {addDays} from '../../money';
import {rptDivRound} from '../computed';

export const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
export const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
export const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const P=(key:string,label:string):RptColumn=>({key,label,type:'pct',money:false});
export const D=(key:string,label:string):RptColumn=>({key,label,type:'date',money:false});
/** The red-row flag: a trailing text column; empty = not red, otherwise the rule that fired (screen = Excel = PDF). */
export const EXC=():RptColumn=>T('exception','Exception');
export const isRed=(r:{cells:Record<string,RptCell>})=>!!r.cells.exception;
export const row=(cells:Record<string,RptCell>):RptRow=>({kind:'row',cells});
/** One decimal, half away from zero: 0.35 -> 35.0. */
export const pct1=(n:number,d:number)=>d>0?Math.round(n*1000/d)/10:0;
/** F12-MEAS2-A · L2: money n / d in integer paise, half away from zero (rptDivRound = server FixedCodeEngine.DivRound); 0 without a divisor. */
export const divRound=(n:number,d:number)=>d>0?rptDivRound(n,d):0;
export const CAPTURE_NOTE=(what:string)=>` Capture needed: ${what}; with nothing captured the report shows no rows.`;

/** Rooms on the books for a night: confirmed, in-house and departed stays covering it, against the rooms in service. */
export function occOn(s:State,d:string){
  const sell=reportSellableOn(s,d).length;
  const occ=s.bookings.filter(b=>(b.status==='Confirmed'||b.status==='In house'||b.status==='Departed')&&b.arrival<=d&&d<b.departure).length;
  return {occ,sell};
}
/** F12-MEAS2-A · L8 (owner 7 Nov 2026): the same calendar date one year earlier; 29 Feb maps to 28 Feb (server DateOnly.AddYears(-1)). */
export function sameDateLastYear(d:string){
  const y=Number(d.slice(0,4))-1,md=d.slice(5,10);
  const leap=(y%4===0&&y%100!==0)||y%400===0;
  return `${String(y).padStart(4,'0')}-${md==='02-29'&&!leap?'02-28':md}`;
}
/** F12-MEAS2-A · L3: paise as rupees for a rule text (en-IN grouping), paise kept when there are any: 500050 → "5,000.50" (server MeasDailyRules.Rupees). */
export const rupeesText=(paise:number)=>(paise/100).toLocaleString('en-IN',paise%100===0?{maximumFractionDigits:0}:{minimumFractionDigits:2,maximumFractionDigits:2});
export const monthStart=(d:string)=>d.slice(0,7)+'-01';
export const inRange=(d:string,from?:string,to?:string)=>(!from||d>=from)&&(!to||d<=to);
/** Whole hours from one 'YYYY-MM-DD[T ]HH:mm' instant to another (never negative). */
const instant=(x:string)=>Date.parse(x.slice(0,10)+'T'+x.slice(11,16)+':00Z');
export const hoursBetween=(from:string,to:string)=>Math.max(0,Math.floor((instant(to)-instant(from))/3600000));
export {addDays};

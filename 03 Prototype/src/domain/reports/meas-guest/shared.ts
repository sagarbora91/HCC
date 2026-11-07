// F12-RPT-GUEST · helpers shared by the six guest-experience measurement reports (pure, read-only over State). Server twin: Hcc.Reporting/Reports/MeasGuest/MeasGuestRules.cs.
// Rounding is "half up" everywhere (roundHalf) so both sides print the same figure; money is integer paise.
import type {RptColumn} from '../types';

export const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
export const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
export const P=(key:string,label:string,money=false):RptColumn=>({key,label,type:'pct',money});
export const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const B=(key:string,label:string):RptColumn=>({key,label,type:'bool',money:false});

/** Round half up (toward +infinity) — the same rule as C# Math.Floor(x + 0.5m). */
export const roundHalf=(x:number)=>Math.floor(x+0.5);
/** Percent with one decimal (45.5), null when the denominator is 0. */
export const pct1=(num:number,den:number)=>den>0?roundHalf(num*1000/den)/10:null;
/** One-decimal text ("4.3") of sum / n, null when n is 0. */
export const avg1=(sum:number,n:number)=>n>0?(roundHalf(sum*10/n)/10).toFixed(1):null;
/** Whole minutes from a to b (both 'YYYY-MM-DD[T ]HH:mm'). */
export const minutesBetween=(a:string,b:string)=>Math.round((Date.parse(b.slice(0,10)+'T'+b.slice(11,16)+':00Z')-Date.parse(a.slice(0,10)+'T'+a.slice(11,16)+':00Z'))/60000);
/** Lower median of a list of whole numbers (no fractions: the same on both sides), null for an empty list. */
export const lowerMedian=(xs:number[])=>{if(!xs.length)return null;const a=[...xs].sort((p,q)=>p-q);return a[(a.length-1)>>1];};
export const prevMonth=(m:string)=>{const y=+m.slice(0,4),mo=+m.slice(5,7);return mo===1?`${y-1}-12`:`${y}-${String(mo-1).padStart(2,'0')}`;};
/** The entry's "Capture needed" note appended to the date basis when nothing was captured (zero rows, never an error). */
export const capNote=(base:string,what:string)=>`${base} Capture needed: ${what}. Nothing has been captured for these filters yet.`;

// F8-RPT-P2-A · helpers shared by the batch A reports (guests + rooms). No figures of their own beyond the rules written here; the server twin
// (Hcc.Reporting/Reports/P2A) uses the same rules and column keys. Money = integer paise.
import type {Booking,Line} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {HccError} from '../../errors';
import {addDays,nights} from '../../money';
import {rctLineColumn} from '../../streams/rpt-revcat';
import {gstCategoryOfLabel} from '../../gst';

export const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
export const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
export const D=(key:string,label:string):RptColumn=>({key,label,type:'date',money:false});
export const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const row=(kind:RptRow['kind'],cells:RptRow['cells'],group?:string):RptRow=>({kind,cells,...(group!==undefined?{group}:{})});
export const plural=(n:number,one:string,many=one+'s')=>`${n} ${n===1?one:many}`;
/** Whole paise, half away from zero (integer arithmetic). */
export const divRound=(a:number,b:number)=>!b?0:Math.sign(a)*Math.floor((Math.abs(a)*2+b)/(2*b));
/** Range guard of an entry (the engine's class limits are wider). */
export function rangeGuard(from:string|undefined,to:string|undefined,maxDays:number,today:string,lookAhead=0){
  if(from&&to&&nights(from,to)+1>maxDays)throw new HccError('VALIDATION',`The range is longer than ${maxDays} days. Narrow the range.`);
  if(to&&to>addDays(today,lookAhead))throw new HccError('VALIDATION',lookAhead?`This report can look at most ${lookAhead} days ahead of the business date (${today}).`:`The report cannot run for a date after the business date (${today}).`);
}
/** The room a stay is in on a night: the dated segment that covers it, else the booked room. */
export const roomOnNight=(b:Booking,d:string)=>b.segments?.find(sg=>sg.from<=d&&(!sg.to||d<sg.to))?.room??b.room;
/** A room-night line that counts as a night sold: a room line with an amount, not reversed by a later line. */
export const isLiveNight=(b:Booking,l:Line)=>l.kind==='room'&&l.amount>0&&!b.lines.some(x=>x.reversalOf===l.id);
/** Room-wise columns: tariff / extra person / other room charges (meal plan lines, discounts, corrections). Food, laundry and other service lines are not room charges. */
export function roomColumn(l:Pick<Line,'kind'|'label'>):'tariff'|'extra'|'other'|null{
  const c=rctLineColumn(l);
  return c==='roomTariff'?'tariff':c==='extraPerson'?'extra':c==='mealPlans'||c==='discounts'||c==='corrections'?'other':null;
}
export type ServiceGroup='food'|'laundry'|'other'|'extras';
/** Group of a service line: catalogue extra -> extras; POS bill -> food; else the label rule of the GST master (laundry, food words; banquet and the rest -> other). */
export function serviceGroup(l:Pick<Line,'label'|'extraId'>):ServiceGroup{
  if(l.extraId)return 'extras';
  if(/ · bill /.test(l.label))return 'food';
  const c=gstCategoryOfLabel(l.label);return c==='food'?'food':c==='laundry'?'laundry':'other';
}
export const isServiceLine=(b:Booking,l:Line)=>(l.kind==='service'||l.kind==='food')&&!b.lines.some(x=>x.reversalOf===l.id);
export const natural=(a:string,b:string)=>a.localeCompare(b,undefined,{numeric:true});

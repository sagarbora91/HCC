// F8-RPT-P2-C · helpers shared by the eight batch-c reports. No figures of their own.
import {HccError} from '../../errors';
import {nights} from '../../money';
import type {RptBoundParams} from '../types';

/** Catalogue D-B says "max 92 days"; the engine binder allows 366 for every D-B report, so the report itself enforces its own limit. */
export function cap92(p:RptBoundParams){
  if(p.from&&p.to&&nights(p.from,p.to)+1>92)throw new HccError('VALIDATION','The range is longer than 92 days. Narrow the range.');
}
/** Share of a total in percent with one decimal, half up (same formula as the server MoneyRules.Share). */
export const share1=(amount:number,total:number)=>total>0?Math.floor((amount*2000+total)/(2*total))/10:0;
/** `YYYY-MM-DDTHH:MM...` → `YYYY-MM-DD HH:MM` */
export const stamp=(t:string)=>t.replace('T',' ').slice(0,16);

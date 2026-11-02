// F4-T2 · Stubs for the T3 selectors the owner digest reads (F4-CONTRACT §3 items 8-10). The integrator replaces these imports with the
// real `qwReorderAlerts` / `fbkOverdue` / `fbkSummary` when T3 is merged; until then the digest shows zero.
import type {State} from '../../domain/types';
export const qwReorderAlerts=(_s:State):{item:string}[]=>[];
export const fbkOverdue=(_s:State,_date:string):{complaints:number;lostFound:number}=>({complaints:0,lostFound:0});
export const fbkSummary=(_s:State,_date:string):{count:number;average:number}=>({count:0,average:0});

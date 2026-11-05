// F12 MEAS · analytics selector contract (MEAS-CONTRACT §9, plan 7A). Declared by the setup commit; meas-study-a / -b / -pack import, never redeclare.
// One pure function per workbook family over ONE input, so the FY26 fixture and the real `lgc` history run through the same code.
// Money = integer paise. FY = April-March ('2025-26'). Definitions are those of the workbooks' Notes & Checks sheets (contract §9.2).
import type {RptResult} from '../reports/types';

export type AnRoom={id:string;type:string;inServiceFrom:string;inServiceTo?:string;suite?:boolean};
export type AnStay={id:string;guestKey:string;room:string;from:string;to:string;nights:number;ratePaise:number;source:string;channel?:string;company?:string;purpose?:string;status:string;complimentary?:boolean;legacy:boolean};
export type AnBill={id:string;stay:string;date:string;roomChargePaise:number;fnbPaise:number;otherPaise:number;discountPaise:number;gstPaise:number;totalPaise:number;chargedNights:number;paidPaise:number;legacy:boolean};
export type AnReceipt={id:string;bill:string;date:string;method:string;amountPaise:number;legacy:boolean};
/** The single input of every selector. `asOf` = simulated business date; `legacyUntil` = last legacy date ('2026-10-30'); halls and duplicate stays are already excluded by the loader. */
export type AnInput={rooms:AnRoom[];stays:AnStay[];bills:AnBill[];receipts:AnReceipt[];asOf:string;legacyUntil:string;covidWindow:{from:string;to:string}};
export type AnParams={fy?:string;from?:string;to?:string;baseFy?:string;topN?:number;[k:string]:unknown};
/** Every analytics selector has exactly this signature; the report registration wraps it as the report's `select`. */
export type AnalyticsSelector=(input:AnInput,params:AnParams)=>RptResult;

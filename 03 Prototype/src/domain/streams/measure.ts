// F12 MEAS · switches and targets (MEAS-CONTRACT §1). Owned by meas-settings: the key table MEAS_ITEMS, the `measure` settings group
// (registered here through registerSettingsGroup), the three-level rule (optional module AND pack AND item; core items always on), targets,
// the report-id → switch map (registerMeasReport) and the guards every surface calls (measOn, measGuard, measReportOn, measReportGuard).
// Signatures of measOn / measTarget / MeasItem are frozen (contract §1.3).
import type {State} from '../types';
import type {MeasPack} from './measure-types';
import {cfgModuleOn,registerSettingsGroup,type CfgProviderDef} from './config';
import {HccError,type ErrorCode} from '../errors';

/** Every switchable item: key = `meas.<pack>.<item>`; `core` items cannot be switched off (contract §1.3). */
export type MeasItem={key:string;pack:MeasPack;label:string;report?:string;capture?:string;core?:boolean;module?:string};

// ───────────────────────────── Packs and items (frozen keys, contract §1.2) ─────────────────────────────
export const MEAS_PACKS:{pack:MeasPack;key:string;label:string;perm:string}[]=[
  {pack:'daily',key:'meas.pack.daily',label:'Daily control',perm:'Reports (front desk / money)'},
  {pack:'revenue',key:'meas.pack.revenue',label:'Revenue management',perm:'Reports (rooms / money)'},
  {pack:'guest',key:'meas.pack.guest',label:'Guest experience',perm:'Reports (guests)'},
  {pack:'hk',key:'meas.pack.hk',label:'Housekeeping, engineering and assets',perm:'Reports (rooms / stock)'},
  {pack:'people',key:'meas.pack.people',label:'People',perm:'Reports (people, HR-001)'},
  {pack:'study',key:'meas.pack.study',label:'Management study (legacy + beyond)',perm:'Reports (Owner / Manager)'},
];
const I=(pack:MeasPack,key:string,label:string,report?:string,o:Partial<MeasItem>={}):MeasItem=>({key,pack,label,...(report?{report}:{}),...o});
/** The 56 items: 34 measures (32 reports + 2 non-report measures) and 22 study entries. Order = panel order. */
export const MEAS_ITEMS:MeasItem[]=[
  I('daily','meas.daily.flash',"Manager's flash",'rpt.guests.manager-flash',{core:true}),
  I('daily','meas.daily.regrets','Regrets and denials','rpt.guests.regrets-denials',{capture:'Turn-away log on reservation search (meas-cap-desk)'}),
  I('daily','meas.daily.rate-variance','Rate variance / rate check','rpt.guests.rate-variance'),
  I('daily','meas.daily.discrepancy','Room discrepancy','rpt.rooms.room-discrepancy'),
  I('daily','meas.daily.high-balance','High-balance and credit-limit','rpt.money.high-balance'),
  I('daily','meas.daily.adjustments','Allowances, rebates, voids and corrections','rpt.money.allowances-voids',{capture:'Allowance reason codes (meas-cap-desk)'}),
  I('daily','meas.daily.no-show','No-show, cancellation and early departure','rpt.guests.no-show-cancel'),
  I('daily','meas.daily.inventory','Complimentary, house-use and out-of-order','rpt.rooms.inventory-exceptions',{core:true}),
  I('daily','meas.daily.cashier','Cashier and shift variance','rpt.money.cashier-variance'),
  I('revenue','meas.rev.demand-calendar','Demand calendar','rpt.revenue.demand-calendar',{capture:'Event calendar (meas-forecast)'}),
  I('revenue','meas.rev.forecast','Occupancy forecast curve',undefined,{capture:'On-the-books snapshots (meas-forecast)'}),
  I('revenue','meas.rev.pickup','Pickup and pace','rpt.revenue.pickup-pace',{capture:'On-the-books snapshots (meas-forecast)'}),
  I('revenue','meas.rev.displacement','Displacement calculator','rpt.revenue.displacement'),
  I('revenue','meas.rev.overbooking','Overbooking and wash','rpt.revenue.overbooking-wash',{capture:'Walked-guest log (meas-cap-desk)'}),
  I('revenue','meas.rev.restrictions','Length-of-stay and minimum-stay controls','rpt.revenue.restriction-effect'),
  I('revenue','meas.rev.rate-shop','Rate shop and parity','rpt.revenue.rate-parity',{capture:'Competitor rate entries (meas-forecast)'}),
  I('guest','meas.guest.survey','Guest satisfaction and NPS','rpt.guests.satisfaction',{capture:'Guest survey: QR page and desk entry (meas-cap-guest)'}),
  I('guest','meas.guest.reputation','Online reputation','rpt.guests.reputation-index',{capture:'Monthly review scores (meas-cap-guest)'}),
  I('guest','meas.guest.recovery','Service recovery','rpt.guests.service-recovery',{capture:'Complaint root cause (meas-cap-guest)'}),
  I('guest','meas.guest.preferences','Guest preference profile',undefined,{capture:'Preference fields on the customer (meas-cap-desk)'}),
  I('guest','meas.guest.desk-timing','Arrival and departure experience','rpt.guests.desk-timing'),
  I('guest','meas.guest.upsell','Upsell and upgrade','rpt.guests.upsell',{capture:'Booked type and upgrade flag (meas-cap-desk)'}),
  I('guest','meas.guest.repeat','Repeat guests and loyalty base','rpt.guests.repeat-loyalty',{capture:'Loyalty tag (meas-cap-guest)'}),
  I('hk','meas.hk.productivity','Attendant productivity and turnaround','rpt.rooms.hk-productivity',{capture:'Task start / finish / inspection times (meas-cap-hk)'}),
  I('hk','meas.hk.ooo','Out-of-order and lost inventory','rpt.rooms.ooo-days',{capture:'Offline reason and expected return (meas-cap-hk)'}),
  I('hk','meas.hk.ppm','Preventive maintenance compliance','rpt.rooms.ppm-compliance',{module:'maintenance'}),
  I('hk','meas.hk.utilities','Energy, water and LPG per occupied room-night','rpt.rooms.utilities-per-room',{module:'maintenance',capture:'Meter readings (meas-cap-hk)'}),
  I('hk','meas.hk.linen','Linen, laundry and guest-supply cost per occupied room','rpt.stock.linen-cost',{module:'inventory',capture:'Discard reasons (meas-cap-hk)'}),
  I('hk','meas.hk.assets','Asset register, FF&E reserve and renovation cycle','rpt.acc.asset-life',{capture:'Asset life and condition (meas-cap-hk)'}),
  I('people','meas.people.productivity','Staff per occupied room, overtime, absenteeism','rpt.people.staff-productivity',{module:'payroll'}),
  I('people','meas.people.attrition','Attrition and tenure','rpt.people.attrition',{module:'payroll',capture:'Exit reason (meas-cap-people)'}),
  I('people','meas.people.scorecard','Performance by desk user','rpt.people.agent-scorecard',{module:'payroll'}),
  I('people','meas.people.training','Training hours and certifications','rpt.people.training',{module:'payroll',capture:'Training log (meas-cap-people)'}),
  I('people','meas.people.pulse','Staff pulse (optional)','rpt.people.staff-pulse',{module:'payroll',capture:'Anonymous quarterly pulse (meas-cap-people)'}),
  I('study','meas.study.revenue-monthly','Monthly revenue and cash (FY history)','rpt.study.revenue-monthly'),
  I('study','meas.study.year-comparison','Year comparison','rpt.study.year-comparison'),
  I('study','meas.study.covid-index','Index vs a base year','rpt.study.index-vs-base'),
  I('study','meas.study.deep-dive','FY deep dive','rpt.study.fy-deep-dive'),
  I('study','meas.study.occupancy','Occupancy history','rpt.study.occupancy-history'),
  I('study','meas.study.patterns','Operations patterns','rpt.study.operations-patterns'),
  I('study','meas.study.rooms-rates','Rooms, rates and discounts','rpt.study.rooms-and-rates'),
  I('study','meas.study.zero-rate','Zero-rate and complimentary stays','rpt.study.zero-rate-complimentary'),
  I('study','meas.study.receivables','Receivables and bad debt','rpt.study.receivables-bad-debt'),
  I('study','meas.study.rm-kpi','Occupancy, ADR, RevPAR by month, FY, type, channel','rpt.study.revenue-kpi'),
  I('study','meas.study.rm-channel','Channel economics and net ADR','rpt.study.channel-economics'),
  I('study','meas.study.rm-pricing','Pricing shape','rpt.study.pricing-shape'),
  I('study','meas.study.guest-repeat','Repeat guests and lifetime value','rpt.study.guest-repeat-value'),
  I('study','meas.study.guest-origin','Guest origin, nationality, meal plan','rpt.study.guest-origin-plan'),
  I('study','meas.study.hk-ops','Housekeeping operations','rpt.study.housekeeping-operations'),
  I('study','meas.study.fnb-halls','F&B, halls and ancillary','rpt.study.fnb-halls-ancillary'),
  I('study','meas.study.finance','Finance and cost','rpt.study.finance-cost'),
  I('study','meas.study.sales','Corporate accounts and win-back','rpt.study.corporate-accounts'),
  I('study','meas.study.compliance','Compliance and risk','rpt.study.compliance-risk'),
  I('study','meas.study.forecast','Seasonality, forecast and scenarios','rpt.study.forecast-scenarios'),
  I('study','meas.study.dashboard','Owner 12-KPI monthly dashboard','rpt.study.owner-dashboard'),
  I('study','meas.study.management-pack','Management study pack (PDF + Excel)','rpt.study.management-pack'),
];
export const measItem=(key:string)=>MEAS_ITEMS.find(i=>i.key===key);
export const measPack=(pack:MeasPack)=>MEAS_PACKS.find(p=>p.pack===pack)!;
export const measPackItems=(pack:MeasPack)=>MEAS_ITEMS.filter(i=>i.pack===pack);

/** Targets (meas.target.*), M-9 owner defaults (contract §1.2, deviation 4). kind percent = whole percent; money = paise. */
export type MeasTargetDef={key:string;label:string;kind:'percent'|'money'|'integer'|'minutes';default:number;baseline?:string;min?:number;max?:number};
export const MEAS_TARGETS:MeasTargetDef[]=[
  {key:'meas.target.occupancy',label:'Occupancy target',kind:'percent',default:35,baseline:'31 %'},
  {key:'meas.target.adr',label:'ADR target (before GST)',kind:'money',default:205000,baseline:'₹1,954'},
  {key:'meas.target.nps',label:'NPS target',kind:'integer',default:30,min:-100,max:100},
  {key:'meas.target.repeatShare',label:'Repeat-guest share target',kind:'percent',default:25},
  {key:'meas.target.lateDeparture',label:'Uncharged late departures (limit)',kind:'percent',default:10},
  {key:'meas.target.ot',label:'Overtime share (limit)',kind:'percent',default:10},
  {key:'meas.target.highBalance',label:'High balance (walk-in without a company)',kind:'money',default:500000},
  {key:'meas.target.cashVariance',label:'Cash variance per shift (limit)',kind:'money',default:10000},
  {key:'meas.target.checkinMinutes',label:'Check-in time standard',kind:'minutes',default:8},
  {key:'meas.target.checkoutMinutes',label:'Check-out time standard',kind:'minutes',default:6},
  {key:'meas.target.absenteeism',label:'Absenteeism (limit)',kind:'percent',default:5},
  {key:'meas.target.attrition',label:'Annual attrition (limit)',kind:'percent',default:30},
  {key:'meas.target.lossPct',label:'Linen / supply loss (limit)',kind:'percent',default:2},
];
/** The other settings of the group (contract §1.2). `yld.overbook.<roomTypeId>` is one per room type (built from the state). */
export const MEAS_OTHER=['meas.study.packSchedule','meas.daily.blockClose','meas.regrets.mandatory','yld.restrictionsEnforce','ffe.reservePct'] as const;
export const MEAS_PACK_SCHEDULES=['off','monthly','quarterly'] as const;

// ───────────────────────────── Effective state (contract §1.3 / §1.4) ─────────────────────────────
const vals=(s:State)=>s.configX?.values??{};
const flag=(s:State,key:string)=>vals(s)[key]!==false; // switches default on
/** Company credit (Groups & companies) is on: High-balance becomes core (contract §1.3). */
export const measCreditOn=(s:State)=>cfgModuleOn(s,'groups');
/** True when the item can never be switched off in this state ("Core — always on"). */
export function measIsCore(s:State,key:string):boolean{
  const it=measItem(key);if(!it)return false;
  return !!it.core||(key==='meas.daily.high-balance'&&measCreditOn(s));
}
export const measPackOn=(s:State,pack:MeasPack)=>flag(s,measPack(pack).key);
/** The item's own stored switch (ignores pack and module). */
export const measItemSwitch=(s:State,key:string)=>flag(s,key);
/** Why an item is off ('module' | 'pack' | 'item'), or undefined when on. */
export function measOffReason(s:State,key:string):'module'|'pack'|'item'|undefined{
  const it=measItem(key);if(!it)return undefined; // unknown keys gate nothing
  if(it.module&&!cfgModuleOn(s,it.module))return 'module';
  if(measIsCore(s,key))return undefined;
  if(!measPackOn(s,it.pack))return 'pack';
  if(!measItemSwitch(s,key))return 'item';
  return undefined;
}
/** True when the pack and the item are on (and, for items tied to an optional module, that module is on). Core items are always on. */
export function measOn(s:State,key:string):boolean{return measOffReason(s,key)===undefined;}
/** Target value (meas.target.*) as stored: percent as number, money in paise, integer as number. */
export function measTarget(s:State,key:string):number|undefined{
  const t=MEAS_TARGETS.find(x=>x.key===key);const v=vals(s)[key];
  return typeof v==='number'?v:t?.default;
}
/** "n of m on" for the panel pill. */
export function measPackCount(s:State,pack:MeasPack){const list=measPackItems(pack);return {on:list.filter(i=>measOn(s,i.key)).length,total:list.length};}
export const measOffKeys=(s:State)=>MEAS_ITEMS.filter(i=>!measOn(s,i.key)).map(i=>i.key);

export const MEAS_OFF:ErrorCode='MEAS_OFF' as ErrorCode;       // integration: add to ErrorCode + errorTitle (domain/errors.ts)
export const MEAS_PACK_OFF:ErrorCode='MEAS_PACK_OFF' as ErrorCode;
export function measOffMessage(s:State,key:string):string{
  const it=measItem(key);const r=measOffReason(s,key);
  const what=it?it.label:key;
  if(r==='module')return `MEAS_OFF: ${what} needs the ${it!.module} module, which is switched off. Its data is kept.`;
  if(r==='pack')return `MEAS_OFF: ${what} is off because the ${measPack(it!.pack).label} pack is switched off in Master configuration › Measurement reports. Its data is kept.`;
  return `MEAS_OFF: ${what} is switched off in Master configuration › Measurement reports. Its data is kept; the Owner can switch it back on.`;
}
/** Capture-form / capture-command guard: throws MEAS_OFF when the item is off (handlers stay registered, data is kept). */
export function measGuard(s:State,key:string):void{if(!measOn(s,key))throw new HccError(MEAS_OFF,measOffMessage(s,key));}

// ───────────────────────────── Report id → switch (contract §1.5, prototype twin) ─────────────────────────────
/** id → key. Seeded from MEAS_ITEMS; report lanes call registerMeasReport for anything else. */
export const MEAS_REPORT_KEY:Record<string,string>=Object.fromEntries(MEAS_ITEMS.filter(i=>i.report).map(i=>[i.report!,i.key]));
export function registerMeasReport(id:string,key:string){
  if(!measItem(key))throw new Error(`registerMeasReport: ${key} is not a measurement key.`);
  MEAS_REPORT_KEY[id]=key;
}
/** A report not in the map is always on (it is not a measurement report). */
export const measReportOn=(s:State,id:string)=>{const k=MEAS_REPORT_KEY[id];return !k||measOn(s,k);};
export function measReportGuard(s:State,id:string){const k=MEAS_REPORT_KEY[id];if(k)measGuard(s,k);}
/** Generic surface filter (search palette entries, phone tabs, scheduled runs, digest blocks): keeps items whose key is on. */
export function measFilter<T>(s:State,list:readonly T[],keyOf:(x:T)=>string|undefined):T[]{return list.filter(x=>{const k=keyOf(x);return !k||measOn(s,k);});}
/** Scheduled report runs: skip (not fail) an off report. */
export const measScheduleSkips=(s:State,reportId:string)=>!measReportOn(s,reportId);

// ───────────────────────────── Settings group `measure` (contract §1.1) ─────────────────────────────
const OWNER={approver:'Owner' as const,money:true,effectiveMode:'immediate' as const};
const AFFECTS='Measurement reports, their capture forms and the Owner digest; data already captured is kept.';
/** Pack and item switches are module-store settings: SETTING_SET → cfgApply (history row) → MEAS_PACK_SET / MEAS_ITEM_SET (the rules). */
export function measSettingDefs(s:State):CfgProviderDef[]{
  const packs:CfgProviderDef[]=MEAS_PACKS.map(p=>({key:p.key,label:`Pack: ${p.label}`,type:'boolean',unit:'on / off',default:true,...OWNER,
    help:`Switches every item of the ${p.label} pack. Core items stay on.`,affects:AFFECTS,
    read:(st:State)=>flag(st,p.key),command:(_st:State,v:unknown)=>({type:'MEAS_PACK_SET',pack:p.pack,on:v,viaHub:true})}));
  const items:CfgProviderDef[]=MEAS_ITEMS.map(i=>({key:i.key,label:i.label,type:'boolean',unit:'on / off',default:true,...OWNER,
    help:[i.report?`Report ${i.report}`:'Measure (no report of its own)',`Pack: ${measPack(i.pack).label}`,i.module?`Needs the ${i.module} module`:'',i.core?'Core — always on':'',i.capture?`Capture needed: ${i.capture}`:''].filter(Boolean).join(' · '),
    affects:AFFECTS,read:(st:State)=>flag(st,i.key),command:(_st:State,v:unknown)=>({type:'MEAS_ITEM_SET',setting:i.key,on:v,viaHub:true})}));
  const targets:CfgProviderDef[]=MEAS_TARGETS.map(t=>({key:t.key,label:t.label,type:t.kind==='money'?'money':'integer',default:t.default,...OWNER,
    suffix:t.kind==='percent'?'%':t.kind==='minutes'?'minutes':undefined,min:t.min??0,max:t.max??(t.kind==='money'?10000000:t.kind==='percent'?100:1000),
    help:t.baseline?`FY 2025-26 actual: ${t.baseline}`:'',affects:'Red rows of the measurement reports and the Owner digest.'}));
  const types=[...new Set((s.rooms||[]).map(r=>r.type))].sort();
  const other:CfgProviderDef[]=[
    {key:'meas.study.packSchedule',label:'Management study pack schedule',type:'choice',options:[...MEAS_PACK_SCHEDULES],default:'monthly',...OWNER,help:'Monthly on the 1st to Owner + Manager (M-10).'},
    {key:'meas.daily.blockClose',label:'Day close waits for open measurement exceptions',type:'boolean',unit:'on / off',default:false,...OWNER,help:'Off = a measurement never blocks the day close (contract §1.4).'},
    {key:'meas.regrets.mandatory',label:'Turn-away reason is mandatory',type:'boolean',unit:'on / off',default:false,...OWNER,help:'M-2: optional by default.'},
    ...types.map(t=>({key:`yld.overbook.${t}`,label:`Overbooking allowance · ${t}`,type:'integer' as const,default:0,min:0,max:20,suffix:'rooms',...OWNER,help:'M-6: 0 for every type.'})),
    {key:'yld.restrictionsEnforce',label:'Enforce yield restrictions (min stay / CTA)',type:'boolean',unit:'on / off',default:false,...OWNER,help:'M-7: advisory by default.'},
    {key:'ffe.reservePct',label:'FF&E reserve',type:'integer',default:3,min:0,max:20,suffix:'%',...OWNER,help:'M-5: 3 % of room revenue.'},
  ];
  return [...packs,...items,...targets,...other];
}
/** Registered lazily from config.ts builtins() (not at import): yield / services / staff import this file while config.ts is still loading. */
export function measRegisterSettings(){registerSettingsGroup({id:'measure',group:'measure',defs:measSettingDefs,summary:s=>{
  const on=MEAS_ITEMS.filter(i=>measOn(s,i.key)).length;const packs=MEAS_PACKS.filter(p=>measPackOn(s,p.pack)).length;
  return `${on} of ${MEAS_ITEMS.length} on · ${packs} of ${MEAS_PACKS.length} packs on · core always on`;}});}

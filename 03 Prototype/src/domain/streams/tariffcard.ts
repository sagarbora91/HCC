// F4-T1 · Tariff card (F4-CONTRACT.md §1, DEC-198, DEC-MA-102). A versioned view + snapshot of the hotel's published prices.
// Never a second price list: nothing reads s.tariffCardX for a price — publishing writes the existing masters (tariff schedule
// s.tariffs, occupancy charges, meal plans, extras) dated effectiveFrom, and bookings / folios / yield / OTA keep reading those.
// Money = integer paise; GST from the dated GST master at effectiveFrom (DEC-069); the card's inclusive figure splits CGST / SGST per
// half, each half rounded half away from zero (§1.4; golden: Deluxe ₹2,000 incl at 18 % → excl ₹1,694.92, display ₹2,000.00).
import type {State,TRCDaySet,TRCLine,TRCLineKind,TRCState,TRCVersion,TRCPublishWrite,Role} from '../types';
import {HccError,type ErrorCode} from '../errors';
import {gstForRoomNight,gstForFood,gstForService,gstForCharge,gstPct} from '../gst';
import {csvMinimal} from '../csv';
import {mstRows,mstMoneyOn,type MstDatedAmount,type MasterRow} from './masters';
import {MST_OCC_CHARGES,rmsTypeRow,rmsTypeName,rmsRoomTypeKeys,type RmsOccRow} from './roommasters';
import {TRC_WEEKEND_DEFAULT,trcResolveRow,trcNightKind} from './tariffcard-resolve';
export {TRC_WEEKEND_DEFAULT};
import {CAT_MEAL_PLANS,CAT_EXTRAS,type CatMealPlan,type CatExtra} from './catalogue';

// ───────────────────────────── Constants, errors, permissions ─────────────────────────────
export const TRC_APPROVAL_KIND='tariff-card';
export const TRC_DAY_NAMES=['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
export const TRC_KIND_LABEL:Record<TRCLineKind,string>={room:'Room','occupancy':'Extra person',meal:'Meal plan',extra:'Extra','hall':'Hall','day-use':'Day use'};
export const TRC_DAYSET_LABEL:Record<TRCDaySet,string>={all:'All nights',weekday:'Weekday nights',weekend:'Weekend nights'};
export const TRC_OCC_LABEL={adult:'Extra adult',child:'Extra child',bed:'Extra bed'} as const;
export const TRC_PLANS=['EP','CP','MAP','AP'];
/** Guests a CP / MAP / AP room line is priced for, when the line does not say (DEC-198: breakfast ₹200 a room = 2 guests). */
export const TRC_PLAN_BASIS=2;
export const TRC_REASONS=['TRC_PAST_EFFECTIVE','TRC_PUBLISHED_READONLY','TRC_CONFLICT','TRC_HALL_NOT_READY','TRC_SLAB_AMBIGUOUS','TRC_EMPTY','TRC_DUP_LINE'] as const;
export type TrcReason=typeof TRC_REASONS[number];
const BASE:Record<TrcReason,ErrorCode>={TRC_PAST_EFFECTIVE:'VALIDATION',TRC_PUBLISHED_READONLY:'INVALID_TRANSITION',TRC_CONFLICT:'INVALID_TRANSITION',
  TRC_HALL_NOT_READY:'INVALID_TRANSITION',TRC_SLAB_AMBIGUOUS:'VALIDATION',TRC_EMPTY:'VALIDATION',TRC_DUP_LINE:'VALIDATION'};
/** Typed refusal: an HccError (shared code) carrying the TRC reason (the server sends the same pair as code + details.reason). */
export class TrcError extends HccError{constructor(public readonly reason:TrcReason,message:string){super(BASE[reason],message);this.name='TrcError';}}
export const trcFail=(reason:TrcReason,message:string):never=>{throw new TrcError(reason,message);};
export const trcReasonOf=(e:unknown):TrcReason|undefined=>{const r=(e as {reason?:unknown}|null)?.reason;return TRC_REASONS.includes(r as TrcReason)?r as TrcReason:undefined;};

/** §1.5 PERM-TRF-001 view · -002 draft + propose · -003 publish / reject / weekend days / import (Owner only, DEC-058). Housekeeping /
 *  maintenance / restaurant never see the card (DEC-061). Prototype: by role (the server seeds the PERM ids, migration 4383). */
export type TrcAction='view'|'propose'|'publish';
const TRC_ROLES:Record<TrcAction,Role[]>={view:['Owner','Manager','Accountant','Reception','Cashier'],propose:['Owner','Manager'],publish:['Owner']};
export function trcCan(s:State,uid:string,a:TrcAction):boolean{
  const u=s.users.find(x=>x.id===uid);if(!u?.enabled||(u.expiresOn&&s.date>u.expiresOn))return false;
  return TRC_ROLES[a].includes(u.role);
}

// ───────────────────────────── Slice ─────────────────────────────
export const trcEmpty=():TRCState=>({versions:[],weekendDaysDefault:[...TRC_WEEKEND_DEFAULT]});
export const trcSlice=(s:State):TRCState=>{const x=s.tariffCardX;return x?{versions:x.versions??[],weekendDaysDefault:x.weekendDaysDefault?.length?x.weekendDaysDefault:[...TRC_WEEKEND_DEFAULT]}:trcEmpty();};
export const trcVersion=(s:State,id:string)=>trcSlice(s).versions.find(v=>v.id===id);
/** The published version in force on a date (latest effectiveFrom ≤ date among published), else undefined. */
export function trcActiveOn(s:State,date:string):TRCVersion|undefined{
  return trcSlice(s).versions.filter(v=>v.status==='published'&&v.effectiveFrom<=date).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:a.effectiveFrom>b.effectiveFrom?1:(a.id<b.id?-1:1)).slice(-1)[0];
}
/** Versions newest first (by effectiveFrom, then id). */
export const trcVersions=(s:State)=>[...trcSlice(s).versions].sort((a,b)=>a.effectiveFrom>b.effectiveFrom?-1:a.effectiveFrom<b.effectiveFrom?1:(a.id>b.id?-1:1));
/** Latest published version (any date): what the card shows by default. */
export const trcLatestPublished=(s:State)=>trcVersions(s).find(v=>v.status==='published');

// ───────────────────────────── Rounding (§1.4) ─────────────────────────────
/** One half of the tax: excl × bps / 2 / 10000 rounded half away from zero (integer arithmetic). */
export function trcHalf(excl:number,bps:number):number{const p=excl*bps,a=Math.abs(p),q=Math.floor(a/20000),r=a-q*20000,t=q+(r>=10000?1:0);return p<0&&t?-t:t;}
/** Inclusive price = excl + CGST + SGST, each half rounded on its own. */
export const trcInclusive=(excl:number,bps:number)=>excl+2*trcHalf(excl,bps);
/** Integer exclusive paise whose inclusive price is nearest `incl` (ties → the lower exclusive). */
export function trcExclusiveFromInclusive(incl:number,bps:number):number{
  const guess=Math.floor(incl*10000/(10000+bps));let best=guess,bd=Infinity;
  for(let e=Math.max(0,guess-3);e<=guess+3;e++){const d=Math.abs(trcInclusive(e,bps)-incl);if(d<bd){bd=d;best=e;}}
  return best;
}

// ───────────────────────────── GST per line (dated master at effectiveFrom) ─────────────────────────────
/** Basis points of a line at a date: rooms / day use by the room-night slab on the exclusive rate per unit per night; extra persons
 *  "other services" (as the occupancy master posts them); meal lines the plan's treatment (bundled = room slab of the add-on, separate =
 *  restaurant); extras their GST class; halls banquet. */
export function trcLineBps(s:State,l:Pick<TRCLine,'kind'|'mealPlan'|'extraId'|'plan'>,date:string,excl:number):number{
  switch(l.kind){
    case 'room':case 'day-use':return gstForRoomNight(s,date,excl).bps;
    case 'occupancy':return gstForService(s,date,'other').bps;
    case 'meal':{const p=mstRows(s,CAT_MEAL_PLANS).find(r=>r.code===l.mealPlan) as CatMealPlan|undefined;
      return p?.gst==='separate'?gstForFood(s,date).bps:gstForRoomNight(s,date,excl).bps;}
    case 'extra':{const x=mstRows(s,CAT_EXTRAS).find(r=>r.id===l.extraId||r.code===l.extraId) as CatExtra|undefined;return gstForCharge(s,date,x?.gst??'other').bps;}
    case 'hall':return gstForService(s,date,'banquet').bps;
  }
}
export type TrcLineInput=Omit<Partial<TRCLine>,'taxBps'|'displayIncl'>&{kind:TRCLineKind;daySet?:TRCDaySet;rateExcl?:number|null;enteredIncl?:number|null};
/** Prices one line for a card effective `date`: exclusive entry → snapshot bps + display; inclusive entry → the exclusive (refused
 *  TRC_SLAB_AMBIGUOUS when the room slab cannot be told from the inclusive figure). */
export function trcPriceLine(s:State,input:TrcLineInput,date:string,id:string):TRCLine{
  const l:TRCLine={...input,id:input.id||id,kind:input.kind,daySet:input.daySet??'all',rateExcl:null,taxBps:0,displayIncl:null} as TRCLine;
  delete (l as {enteredIncl?:unknown}).enteredIncl;
  const incl=input.enteredIncl;
  if(incl!==undefined&&incl!==null){
    if(!Number.isSafeInteger(incl)||incl<=0)trcFail('TRC_EMPTY','Enter a price greater than zero.');
    let excl:number;
    if(l.kind==='room'||l.kind==='day-use'||(l.kind==='meal'&&trcLineBps(s,l,date,0)!==trcLineBps(s,l,date,Number.MAX_SAFE_INTEGER))){
      // slab depends on the exclusive amount: try every distinct rate, keep the self-consistent ones
      const rates=[...new Set([trcLineBps(s,l,date,0),trcLineBps(s,l,date,Number.MAX_SAFE_INTEGER)])];
      const ok=rates.map(b=>({b,e:trcExclusiveFromInclusive(incl,b)})).filter(x=>trcLineBps(s,l,date,x.e)===x.b);
      if(ok.length!==1)trcFail('TRC_SLAB_AMBIGUOUS',`${trcLineTitle(s,l)}: the inclusive price sits on the GST slab boundary — enter the price before GST.`);
      excl=ok[0].e;
    }else excl=trcExclusiveFromInclusive(incl,trcLineBps(s,l,date,0));
    l.rateExcl=excl;l.enteredIncl=incl;
  }else{
    const e=input.rateExcl;
    if(e===null||e===undefined)l.rateExcl=null;
    else{if(!Number.isSafeInteger(e)||e<0||e>100_000_000)trcFail('TRC_EMPTY',`${trcLineTitle(s,l)}: enter a price in rupees (paise whole).`);l.rateExcl=e;}
  }
  l.taxBps=trcLineBps(s,l,date,l.rateExcl??0);
  l.displayIncl=l.rateExcl===null?null:trcInclusive(l.rateExcl,l.taxBps);
  return l;
}
/** Rounding difference shown in the editor: display − entered (₹0.00 or ±₹0.01). */
export const trcRoundingDiff=(l:TRCLine)=>l.enteredIncl!==undefined&&l.displayIncl!==null?l.displayIncl-l.enteredIncl:0;

// ───────────────────────────── Labels ─────────────────────────────
export function trcLineTitle(s:State,l:Pick<TRCLine,'kind'|'roomType'|'plan'|'occKind'|'mealPlan'|'extraId'|'hallId'>):string{
  const type=l.roomType?rmsTypeName(s,l.roomType):'';
  switch(l.kind){
    case 'room':return `${type} · ${l.plan||'EP'}`;
    case 'occupancy':return `${type} · ${TRC_OCC_LABEL[l.occKind??'adult']}`;
    case 'meal':return `Meal plan ${l.mealPlan??''}`.trim()+' · per guest';
    case 'extra':{return 'Extra · '+(l.extraId??'');}
    case 'hall':return 'Hall · '+(l.hallId??'');
    case 'day-use':return `${type} · day use`;
  }
}
/** Duplicate key: kind / type / plan / occupancy kind / meal / extra / hall / day set. */
export const trcLineKey=(l:TRCLine)=>[l.kind,l.roomType??'',l.kind==='room'?(l.plan||'EP'):'',l.occKind??'',l.mealPlan??'',l.extraId??'',l.hallId??'',l.daySet].join('|');

// ───────────────────────────── Validation ─────────────────────────────
/** Draft checks (shared by save / propose / publish): effective date not before the business date, no duplicate line, day set pairs. */
export function trcCheckDraft(s:State,v:Pick<TRCVersion,'effectiveFrom'|'lines'|'weekendDays'>){
  if(!/^\d{4}-\d{2}-\d{2}$/.test(v.effectiveFrom))trcFail('TRC_PAST_EFFECTIVE','Choose the date the card takes effect.');
  if(v.effectiveFrom<s.date)trcFail('TRC_PAST_EFFECTIVE',`The card cannot take effect before the business date ${s.date}; bookings may already use the old prices.`);
  if(!Array.isArray(v.weekendDays)||v.weekendDays.some(d=>!Number.isInteger(d)||d<0||d>6)||new Set(v.weekendDays).size!==v.weekendDays.length||v.weekendDays.length>=7)
    trcFail('TRC_EMPTY','Weekend nights: pick up to six days of the week.');
  const seen=new Set<string>();
  for(const l of v.lines){const k=trcLineKey(l);if(seen.has(k))trcFail('TRC_DUP_LINE',`${trcLineTitle(s,l)} (${TRC_DAYSET_LABEL[l.daySet]}) is on the card twice.`);seen.add(k);}
}

// ───────────────────────────── Publish plan (§1.3) ─────────────────────────────
export type TrcTariffRow={type:string;rate:number;effectiveFrom:string;days?:TRCDaySet;cardVersion?:string};
export type TrcPlanWrite=TRCPublishWrite&{label:string;field?:string};
export type TrcPublishPlan={writes:TrcPlanWrite[];tariffs:TrcTariffRow[];masters:Record<string,MasterRow[]>;printOnly:{line:string;why:string}[];skipped:{line:string;why:string}[]};
const normDays=(d?:TRCDaySet)=>d??'all';
const datedSet=(list:MstDatedAmount[]|undefined,from:string,amount:number):MstDatedAmount[]=>
  [...(list??[]).filter(x=>x.effectiveFrom!==from),{amount,effectiveFrom:from}].sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1);
/** Writes a version's publish would make (pure). Refuses: hall lines (TRC_HALL_NOT_READY), nothing to write (TRC_EMPTY), a hand-entered
 *  master row on the same key and date (TRC_CONFLICT), meal plan add-ons that differ by room type (TRC_CONFLICT). */
export function trcPublishPlan(s:State,v:TRCVersion):TrcPublishPlan{
  trcCheckDraft(s,v);
  const from=v.effectiveFrom,writes:TrcPlanWrite[]=[],printOnly:TrcPublishPlan['printOnly']=[],skipped:TrcPublishPlan['skipped']=[];
  const hall=v.lines.find(l=>l.kind==='hall');if(hall)trcFail('TRC_HALL_NOT_READY','Hall prices publish after hall booking (wave F6). Remove the hall lines or keep them on a draft.');
  // rows an earlier card version wrote (replaceable on the same date)
  const cardRefs=new Set(trcSlice(s).versions.filter(x=>x.id!==v.id&&(x.status==='published'||x.status==='superseded')).flatMap(x=>(x.publishLog??[]).map(w=>w.master+'|'+w.ref)));
  // 1 · tariff schedule
  let tariffs:TrcTariffRow[]=(s.tariffs??[]).map(t=>({...t}));
  for(const l of v.lines.filter(l=>l.kind==='room'&&(!l.plan||l.plan==='EP'))){
    if(l.rateExcl===null){skipped.push({line:trcLineTitle(s,l),why:'blank price'});continue;}
    const type=l.roomType??'';if(!s.rooms.some(r=>r.type===type)&&!rmsTypeRow(s,type))trcFail('TRC_CONFLICT',`Room type ${type} is not in the room types master.`);
    const days=normDays(l.daySet),ref=`${type}|${from}|${days}`;
    const same=tariffs.find(t=>t.type===type&&t.effectiveFrom===from&&normDays(t.days)===days);
    if(same&&!same.cardVersion&&!cardRefs.has('tariffs|'+ref))trcFail('TRC_CONFLICT',`A hand-entered ${rmsTypeName(s,type)} tariff from ${from} (${TRC_DAYSET_LABEL[days]}) already exists — withdraw it in Tariffs or pick another date.`);
    tariffs=tariffs.filter(t=>t!==same);
    tariffs.push({type,rate:l.rateExcl,effectiveFrom:from,days,cardVersion:v.id});
    writes.push({master:'tariffs',ref,before:same?.rate,after:l.rateExcl,label:`${rmsTypeName(s,type)} · ${TRC_DAYSET_LABEL[days]} from ${from}`});
  }
  tariffs.sort((a,b)=>a.type<b.type?-1:a.type>b.type?1:a.effectiveFrom<b.effectiveFrom?-1:a.effectiveFrom>b.effectiveFrom?1:0);
  // 2 · dated master money (occupancy charges, meal plans, extras)
  const masters:Record<string,MasterRow[]>={};
  const rowsOf=(id:string)=>masters[id]??=mstRows(s,id).map(r=>({...r}));
  const setMoney=(id:'occupancyCharges'|'mealPlans'|'extras',row:MasterRow,field:string,amount:number,label:string)=>{
    const list=row[field] as MstDatedAmount[]|undefined,ref=`${row.code}|${field}|${from}`;
    const had=(list??[]).find(x=>x.effectiveFrom===from),now=mstMoneyOn(list,from);
    if(now===amount&&!had)return; // already the master's value
    if(had&&had.amount===amount)return;
    if(had&&!cardRefs.has(id+'|'+ref))trcFail('TRC_CONFLICT',`${row.name}: a hand-entered amount from ${from} already exists — change it in Master configuration or pick another date.`);
    row[field]=datedSet(list,from,amount);
    writes.push({master:id,ref,before:now,after:amount,label,field});
  };
  for(const l of v.lines.filter(l=>l.kind==='occupancy')){
    if(l.rateExcl===null){skipped.push({line:trcLineTitle(s,l),why:'blank price — publishes nothing (A25)'});continue;}
    const type=l.roomType??'',code=rmsTypeRow(s,type)?.code??type,rows=rowsOf(MST_OCC_CHARGES);
    let row=(rows as RmsOccRow[]).find(r=>r.active&&r.roomType===code);
    if(!row){row={id:'occ-trc-'+code.toLowerCase(),code:('X-'+code).slice(0,20),name:`${rmsTypeName(s,type)} extras`,sortOrder:rows.reduce((m,r)=>Math.max(m,r.sortOrder),0)+10,active:true,roomType:code} as RmsOccRow;rows.push(row);}
    const field=l.occKind==='child'?'extraChild':l.occKind==='bed'?'extraBed':'extraAdult';
    if(l.ageFrom!==undefined)row.childAgeFrom=l.ageFrom;if(l.ageTo!==undefined)row.childAgeTo=l.ageTo;
    setMoney('occupancyCharges',row,field,l.rateExcl,`${trcLineTitle(s,l)} from ${from}`);
  }
  // CP / MAP / AP room lines → meal plan add-on per guest per night = (plan − EP) ÷ guests basis
  const addOns=new Map<string,{amount:number;line:string}>();
  for(const l of v.lines.filter(l=>l.kind==='room'&&l.plan&&l.plan!=='EP'&&l.rateExcl!==null)){
    const ep=v.lines.find(x=>x.kind==='room'&&(!x.plan||x.plan==='EP')&&x.roomType===l.roomType&&x.daySet===l.daySet);
    if(!ep||ep.rateExcl===null){printOnly.push({line:trcLineTitle(s,l),why:'no EP line of the same type'});continue;}
    const basis=l.minGuests??TRC_PLAN_BASIS,amt=Math.round((l.rateExcl!-ep.rateExcl)/basis),plan=l.plan!;
    if(amt<0)trcFail('TRC_CONFLICT',`${trcLineTitle(s,l)} is below the EP price.`);
    const prev=addOns.get(plan);
    if(prev&&prev.amount!==amt)trcFail('TRC_CONFLICT',`Meal plan ${plan} adds ${prev.amount/100} per guest on ${prev.line} but ${amt/100} on ${trcLineTitle(s,l)} — the meal plans master holds one add-on per plan.`);
    addOns.set(plan,{amount:amt,line:trcLineTitle(s,l)});
  }
  for(const l of v.lines.filter(l=>l.kind==='meal'&&l.rateExcl!==null))addOns.set(l.mealPlan??'',{amount:l.rateExcl!,line:trcLineTitle(s,l)});
  for(const [code,a] of addOns){
    const row=rowsOf(CAT_MEAL_PLANS).find(r=>r.code===code);
    if(!row){printOnly.push({line:a.line,why:`meal plan ${code} is not in the meal plans master`});continue;}
    setMoney('mealPlans',row,'addOn',a.amount,`Meal plan ${code} add-on per guest from ${from}`);
  }
  for(const l of v.lines.filter(l=>l.kind==='extra')){
    if(l.rateExcl===null){skipped.push({line:trcLineTitle(s,l),why:'blank price'});continue;}
    const row=rowsOf(CAT_EXTRAS).find(r=>r.id===l.extraId||r.code===l.extraId);
    if(!row){printOnly.push({line:trcLineTitle(s,l),why:'not in the extras catalogue'});continue;}
    setMoney('extras',row,'price',l.rateExcl,`${row.name} from ${from}`);
  }
  for(const l of v.lines.filter(l=>l.kind==='day-use'))printOnly.push({line:trcLineTitle(s,l),why:'day use prints only (the tariff schedule has no plan column; yield day-use plan keeps its own rule)'});
  for(const id of Object.keys(masters))if(!writes.some(w=>w.master===id))delete masters[id];
  if(!writes.length)trcFail('TRC_EMPTY','Nothing to publish: every price on this card is blank or already in the masters.');
  return {writes,tariffs,masters,printOnly,skipped};
}

// ───────────────────────────── Rate resolution (§1.3; used by adminTariffOn) ─────────────────────────────
export {trcWeekendDaysFor,trcNightKind,trcResolveRow} from './tariffcard-resolve';

// ───────────────────────────── Draft from the masters (UI "New draft") ─────────────────────────────
/** A draft that reproduces the prices in force on `date`: every room type EP (weekday / weekend rows when the schedule has them),
 *  extra adult / child / bed from the occupancy master (child row kept blank when the master has none), meal plan add-ons. */
export function trcDraftFromMasters(s:State,date:string):TrcLineInput[]{
  const out:TrcLineInput[]=[];let n=0;const id=()=>'L'+String(++n).padStart(3,'0');
  for(const type of rmsRoomTypeKeys(s)){
    const rows=(s.tariffs??[]).filter(t=>t.type===type);
    const sets:TRCDaySet[]=rows.some(t=>t.days==='weekday'||t.days==='weekend')?['weekday','weekend']:['all'];
    for(const d of sets){const probe=d==='all'?date:nextOf(date,d,trcSlice(s).weekendDaysDefault);
      const r=trcResolveRow(s,rows,probe),room=s.rooms.find(x=>x.type===type);out.push({id:id(),kind:'room',roomType:type,plan:'EP',daySet:d,rateExcl:r?.rate??room?.rate??null});}
    const code=rmsTypeRow(s,type)?.code??type;
    const occ=(mstRows(s,MST_OCC_CHARGES,{active:true}) as RmsOccRow[]).find(r=>r.roomType===code);
    for(const k of ['adult','child','bed'] as const){
      const f=k==='adult'?'extraAdult':k==='child'?'extraChild':'extraBed';const amt=mstMoneyOn(occ?.[f],date);
      if(amt!==undefined||k!=='bed')out.push({id:id(),kind:'occupancy',roomType:type,occKind:k,daySet:'all',rateExcl:amt??null,
        ...(k==='child'?{ageFrom:occ?.childAgeFrom??5,ageTo:occ?.childAgeTo??12}:{})});
    }
  }
  for(const p of mstRows(s,CAT_MEAL_PLANS,{active:true}) as CatMealPlan[]){if(p.code==='EP')continue;out.push({id:id(),kind:'meal',mealPlan:p.code,daySet:'all',rateExcl:mstMoneyOn(p.addOn,date)??null});}
  return out;
}
function nextOf(date:string,want:'weekday'|'weekend',weekend:number[]){let d=date;for(let i=0;i<7;i++){if(trcNightKind(d,weekend)===want)return d;d=new Date(Date.parse(d+'T12:00:00Z')+86400000).toISOString().slice(0,10);}return date;}

// ───────────────────────────── DEC-198 CSV import (§1.6) ─────────────────────────────
export type TrcCsvFiles={roomTypes?:string;tariffs?:string;occupancyCharges?:string;mealPlans?:string;rooms?:string};
/** Small CSV reader (quoted fields, "" escapes, comma thousands inside quotes). */
export function trcParseCsv(text:string):Record<string,string>[]{
  const rows:string[][]=[];let row:string[]=[],cell='',q=false;
  const t=text.replace(/^﻿/,'');
  for(let i=0;i<t.length;i++){const c=t[i];
    if(q){if(c==='"'){if(t[i+1]==='"'){cell+='"';i++;}else q=false;}else cell+=c;continue;}
    if(c==='"')q=true;else if(c===','){row.push(cell);cell='';}else if(c==='\n'||c==='\r'){if(c==='\r'&&t[i+1]==='\n')i++;row.push(cell);cell='';if(row.some(x=>x.trim()))rows.push(row);row=[];}else cell+=c;}
  row.push(cell);if(row.some(x=>x.trim()))rows.push(row);
  const [head,...body]=rows;if(!head)return [];const h=head.map(x=>x.trim());
  return body.map(r=>Object.fromEntries(h.map((k,i)=>[k,(r[i]??'').trim()])));
}
/** "1,694.92" → 169492 paise; blank → null. */
export function trcRupees(v:string|undefined):number|null{
  const t=(v??'').replace(/[₹,\s]/g,'');if(!t)return null;if(!/^\d+(\.\d{1,2})?$/.test(t))trcFail('TRC_EMPTY',`"${v}" is not an amount in rupees.`);
  const [r,p='']=t.split('.');return Number(r)*100+Number((p+'00').slice(0,2));
}
/** Draft lines from the five rate-card CSVs (DEC-198): tariffs (roomType, effectiveFrom, rate [, plan]) → room EP lines — a CP rate for a
 *  type is the EP rate + the CP add-on × 2 guests; occupancy charges → extra adult / child (blank child = blank row) / bed; meal plans →
 *  per-guest add-on lines. Room type codes map to the room types master (by code, then key, then name); unknown codes stay as given. */
export function trcImportLines(s:State,f:TrcCsvFiles,effectiveFrom:string):TrcLineInput[]{
  const out:TrcLineInput[]=[];let n=0;const id=()=>'L'+String(++n).padStart(3,'0');
  const types=trcParseCsv(f.roomTypes??'');
  const keyOf=(code:string)=>{const r=rmsTypeRow(s,code);if(r)return String(r.key??r.code);const t=types.find(x=>x.code===code);return t?.code??code;};
  const meals=trcParseCsv(f.mealPlans??'');
  const addOn=(code:string)=>{const m=meals.find(x=>x.code===code);return m?trcRupees(m.addOn):null;};
  const tariffs=trcParseCsv(f.tariffs??'').filter(t=>!t.effectiveFrom||t.effectiveFrom<=effectiveFrom);
  const latest=new Map<string,Record<string,string>>();
  for(const t of tariffs){const k=t.roomType+'|'+(t.plan||'EP');const had=latest.get(k);if(!had||(had.effectiveFrom??'')<=(t.effectiveFrom??''))latest.set(k,t);}
  const order=types.length?types.map(t=>t.code):[...new Set(tariffs.map(t=>t.roomType))];
  for(const code of order){
    const ep=latest.get(code+'|EP');if(!ep)continue;const type=keyOf(code),rate=trcRupees(ep.rate);
    out.push({id:id(),kind:'room',roomType:type,plan:'EP',daySet:'all',rateExcl:rate,maxGuests:Number(types.find(t=>t.code===code)?.maxOccupancy)||undefined});
    for(const plan of ['CP','MAP','AP']){
      const own=latest.get(code+'|'+plan),a=addOn(plan);
      const r=own?trcRupees(own.rate):a&&rate!==null?rate+a*TRC_PLAN_BASIS:null;
      if(r!==null&&(own||plan==='CP'))out.push({id:id(),kind:'room',roomType:type,plan,daySet:'all',rateExcl:r,minGuests:TRC_PLAN_BASIS});
    }
  }
  for(const o of trcParseCsv(f.occupancyCharges??'')){
    if(o.active&&/^(no|n|false|0)$/i.test(o.active))continue;const type=keyOf(o.roomType);
    out.push({id:id(),kind:'occupancy',roomType:type,occKind:'adult',daySet:'all',rateExcl:trcRupees(o.extraAdult)});
    out.push({id:id(),kind:'occupancy',roomType:type,occKind:'child',daySet:'all',rateExcl:trcRupees(o.extraChild),ageFrom:Number(o.childAgeFrom)||5,ageTo:Number(o.childAgeTo)||12});
    if(trcRupees(o.extraBed)!==null)out.push({id:id(),kind:'occupancy',roomType:type,occKind:'bed',daySet:'all',rateExcl:trcRupees(o.extraBed)});
  }
  for(const m of meals){if(m.code==='EP'||(m.active&&/^(no|n|false|0)$/i.test(m.active)))continue;out.push({id:id(),kind:'meal',mealPlan:m.code,daySet:'all',rateExcl:trcRupees(m.addOn)});}
  if(!out.length)trcFail('TRC_EMPTY','The files hold no prices (expected room-types / tariffs / occupancy-charges / meal-plans CSVs).');
  return out;
}

// ───────────────────────────── Rate sheet (print + CSV; print = screen, same selector) ─────────────────────────────
export type TrcSheetRow={id:string;group:'Rooms'|'Extra persons'|'Meal plans & extras'|'Day use'|'Halls';title:string;daySet:string;excl:number|null;bps:number;incl:number|null;note?:string};
export type TrcSheet={version:TRCVersion|undefined;hotel:string;validFrom:string;gstLine:string;rows:TrcSheetRow[];weekend:string;terms:string};
const GROUP:Record<TRCLineKind,TrcSheetRow['group']>={room:'Rooms',occupancy:'Extra persons',meal:'Meal plans & extras',extra:'Meal plans & extras','day-use':'Day use',hall:'Halls'};
/** The rate sheet model of a version (default: the latest published). Rows in card order, grouped; blank prices print "on request". */
export function trcRateSheet(s:State,versionId?:string):TrcSheet{
  const v=versionId?trcVersion(s,versionId):trcLatestPublished(s);
  const lines=v?.lines??[];
  const rows=lines.map(l=>({id:l.id,group:GROUP[l.kind],title:trcLineTitle(s,l),daySet:TRC_DAYSET_LABEL[l.daySet],excl:l.rateExcl,bps:l.taxBps,incl:l.displayIncl,
    note:l.kind==='occupancy'&&l.occKind==='child'&&l.ageFrom!==undefined?`Age ${l.ageFrom}–${l.ageTo}`:l.note}));
  const order=['Rooms','Extra persons','Meal plans & extras','Day use','Halls'];rows.sort((a,b)=>order.indexOf(a.group)-order.indexOf(b.group));
  const roomBps=[...new Set(lines.filter(l=>l.kind==='room'&&l.rateExcl!==null).map(l=>l.taxBps))];
  const gstLine=roomBps.length===1?`Prices include GST @ ${gstPct(roomBps[0]).replace('%',' %')}`:roomBps.length?`Prices include GST (${roomBps.map(b=>gstPct(b)).join(' / ')} by slab)`:'Prices include GST';
  const terms=String((s.settings as unknown as Record<string,unknown>).rateCardTerms??'').trim();
  return {version:v,hotel:s.settings.hotel,validFrom:v?.effectiveFrom??'',gstLine,rows,weekend:(v?.weekendDays??trcSlice(s).weekendDaysDefault).map(d=>TRC_DAY_NAMES[d]).join(', '),terms};
}
const r2=(p:number|null)=>p===null?'':(p/100).toFixed(2);
/** CSV of a card version (same rows and figures as the screen and the print). */
export function trcSheetCsv(sheet:TrcSheet):string{
  const head=['Group','Item','Nights','Price before GST','GST %','Price incl GST','Note'];
  return [head.join(','),...sheet.rows.map(r=>[r.group,r.title,r.daySet,r2(r.excl),(r.bps/100).toFixed(2),r2(r.incl),r.note??''].map(csvMinimal).join(','))].join('\r\n')+'\r\n';
}
/** CSV of the version history. */
export function trcHistoryCsv(s:State):string{
  const head=['Version','Label','Effective from','Status','Lines','Master rows written','Created by','Proposed by','Published by','Published at','Superseded by'];
  return [head.join(','),...trcVersions(s).map(v=>[v.id,v.label,v.effectiveFrom,v.status,v.lines.length,(v.publishLog??[]).length,v.by,v.proposedBy??'',v.publishedBy??'',v.publishedAt??'',v.supersededBy??''].map(x=>csvMinimal(x)).join(','))].join('\r\n')+'\r\n';
}
/** Summary counts for the page header. */
export function trcSummary(s:State){
  const vs=trcSlice(s).versions,live=trcActiveOn(s,s.date);
  return {live:live?.id,liveFrom:live?.effectiveFrom,drafts:vs.filter(v=>v.status==='draft').length,proposed:vs.filter(v=>v.status==='proposed').length,
    scheduled:vs.filter(v=>v.status==='published'&&v.effectiveFrom>s.date).length};
}

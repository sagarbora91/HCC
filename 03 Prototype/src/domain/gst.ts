// DEC-069 · GST master (replaces the synthetic 10 % test tax). Pure functions only; no imports beyond types.
// Rates were seeded from public sources on 25 Oct 2026 and are ILLUSTRATIVE — CA to confirm for this hotel:
//   · Room accommodation, per unit per night, on the actual price charged for that night (not an average or declared tariff):
//       ≤ ₹7,500 → 5 % without ITC from 22 Sep 2025 (before that 12 % with ITC); > ₹7,500 → 18 % with ITC.
//   · Restaurant / room service supplied by the hotel: 5 % without ITC, unless the hotel is "specified premises" for the financial
//       year (any accommodation unit above ₹7,500 per night in the previous FY, or the hotel opted in by declaration) → 18 % with ITC
//       (from 1 Apr 2025).
//   · Other hotel services (laundry, banquet / hall, other): one configurable dated rate each (seed 18 %).
// Intra-state supplies only: each line's tax is split into CGST + SGST halves (IGST is out of scope for the prototype).
// Rounding: tax per line in paise, half away from zero; CGST takes the odd paisa so CGST + SGST = line tax exactly.
//
// Storage: every rate is a dated value in Master configuration (group "Money and tax", Owner-only — DEC-058) under a `gst.*` key.
// The hub records each change in s.configX.changes (effective-from date, history, revert); gstValueOn() reads the value in force on
// any business date from that log and falls back to the built-in seed versions below (so dates before any hub change, e.g. history
// in FY 2025-26, use the statutory seed). Posted folio lines keep their stored taxBps snapshot; only new postings consult the master.
import type {State,Booking} from './types';

export const GST_ILLUSTRATIVE='Illustrative — CA to confirm';
export type GstCategory='room'|'food'|'laundry'|'banquet'|'other';
export type GstServiceKind='laundry'|'banquet'|'other';
export type GstRate={bps:number;itc:boolean;category:GstCategory;rule:string};
export type GstKey='gst.room.threshold'|'gst.room.lowBps'|'gst.room.lowItc'|'gst.room.highBps'|'gst.food.bps'|'gst.food.specifiedBps'|'gst.food.optIn'
  |'gst.svc.laundryBps'|'gst.svc.banquetBps'|'gst.svc.otherBps';
type SeedVersion={from:string;value:number|boolean};
type SeedDef={key:GstKey;label:string;type:'percent'|'money'|'boolean';unit:string;help:string;versions:SeedVersion[]};
/** Seed versions (oldest first). The first version applies to every earlier date. */
export const GST_SEED:SeedDef[]=[
  {key:'gst.room.threshold',label:'GST · room slab threshold (per unit per night)',type:'money',unit:'₹ per room per night',
    help:'Room nights charged at or below this take the lower slab; above it the higher slab. Tested per room per night on the actual price charged.',
    versions:[{from:'2017-07-01',value:750000}]},
  {key:'gst.room.lowBps',label:'GST · room night at or below the threshold',type:'percent',unit:'% of the night’s charge',
    help:'5 % without input tax credit from 22 Sep 2025; 12 % with ITC before that.',
    versions:[{from:'2017-07-01',value:1200},{from:'2025-09-22',value:500}]},
  {key:'gst.room.lowItc',label:'GST · input tax credit on the lower room slab',type:'boolean',unit:'available / not available',
    help:'Not available from 22 Sep 2025 (5 % slab); available before (12 % slab). Shown on documents and in the CA pack.',
    versions:[{from:'2017-07-01',value:true},{from:'2025-09-22',value:false}]},
  {key:'gst.room.highBps',label:'GST · room night above the threshold',type:'percent',unit:'% of the night’s charge',
    help:'18 % with input tax credit.',versions:[{from:'2017-07-01',value:1800}]},
  {key:'gst.food.bps',label:'GST · restaurant and room service (standard)',type:'percent',unit:'% of the bill',
    help:'5 % without input tax credit when the hotel is not “specified premises” for the financial year.',versions:[{from:'2017-07-01',value:500}]},
  {key:'gst.food.specifiedBps',label:'GST · restaurant and room service (specified premises)',type:'percent',unit:'% of the bill',
    help:'18 % with input tax credit when the hotel is “specified premises” for the financial year (from 1 Apr 2025).',versions:[{from:'2017-07-01',value:1800}]},
  {key:'gst.food.optIn',label:'GST · declared specified premises (opt-in)',type:'boolean',unit:'declared / not declared',
    help:'Owner declaration that the hotel is specified premises. Without it, the status is derived from last financial year’s tariffs (any unit above the room threshold). Declarations normally run for a whole financial year from 1 April — CA to confirm the timing.',
    versions:[{from:'2017-07-01',value:false}]},
  {key:'gst.svc.laundryBps',label:'GST · laundry and dry cleaning',type:'percent',unit:'% of the charge',help:'Seed 18 % — CA to confirm.',versions:[{from:'2017-07-01',value:1800}]},
  {key:'gst.svc.banquetBps',label:'GST · banquet, hall and conference',type:'percent',unit:'% of the charge',help:'Seed 18 % — CA to confirm.',versions:[{from:'2017-07-01',value:1800}]},
  {key:'gst.svc.otherBps',label:'GST · other hotel services',type:'percent',unit:'% of the charge',help:'Seed 18 % — CA to confirm. Used for any charge that is not room, food, laundry or banquet.',versions:[{from:'2017-07-01',value:1800}]},
];
export const GST_KEYS=GST_SEED.map(d=>d.key);
export const isGstKey=(key:unknown)=>typeof key==='string'&&key.startsWith('gst.');
/** A GST master change or withdrawal needs a reason (same rule and wording as the server, GstMasterService.Reason). */
export function gstReasonProblem(reason:unknown):string|undefined{
  const r=String(reason??'').trim();
  return r.length<3?'Give a reason for the GST change (at least 3 characters).':r.length>200?'Keep the reason to 200 characters.':undefined;
}
export const gstSeedDef=(key:string)=>GST_SEED.find(d=>d.key===key);
/** Seed value in force on a date (statutory history built into the prototype). */
export function gstSeedOn(key:GstKey,date:string):number|boolean{
  const d=gstSeedDef(key)!;let v=d.versions[0].value;
  for(const x of d.versions)if(x.from<=date)v=x.value;
  return v;
}
/** GST master value in force on a business date: the latest non-withdrawn hub change with effectiveFrom ≤ date, else the seed. */
export function gstValueOn(s:State,key:GstKey,date:string):number|boolean{
  const log=s.configX?.changes;
  if(log?.length){
    // changes are kept newest first; walk oldest → newest so a later change on the same effective date wins.
    let hit:{effectiveFrom:string;after:unknown}|undefined;
    for(let i=log.length-1;i>=0;i--){const c=log[i];
      if(c.key===key&&c.status!=='withdrawn'&&c.effectiveFrom<=date&&(!hit||c.effectiveFrom>=hit.effectiveFrom))hit=c;}
    if(hit)return hit.after as number|boolean;
  }
  return gstSeedOn(key,date);
}
const num=(s:State,key:GstKey,date:string)=>Number(gstValueOn(s,key,date));

// ───────────────────────────── Rounding and CGST / SGST split ─────────────────────────────
/** Tax on one line in paise: amount × bps / 10000, rounded half away from zero (integer arithmetic, no float drift). */
export function gstLineTax(amount:number,bps:number):number{
  const p=amount*bps,a=Math.abs(p),q=Math.floor(a/10000),r=a-q*10000,t=q+(r>=5000?1:0);
  return p<0&&t?-t:t;
}
/**
 * A whole remainder as pieces whose own taxes (each half away from zero) add up to exactly `tax` — usually `[amount]`. After a credit whose
 * tax was exactly half a paisa (₹333.30 at 5 % = 1666.5 → 1667), the rest's own tax rounds a paisa past what is left; the rest is then the
 * remainder less m small pieces (A03a-04, as the server's CreditNoteRules). null when no split of at most five pieces exists.
 */
export function gstExactPieces(amount:number,bps:number,tax:number):number[]|null{
  if(gstLineTax(amount,bps)===tax)return [amount];
  const sign=amount<0?-1:1;
  for(let m=1;m<=4;m++)for(let k=1;k<=20000;k++){
    const a=sign*(k<=10000?k:10000-k),rest=amount-m*a; // same sign first, then the opposite sign
    if(rest!==0&&gstLineTax(rest,bps)+m*gstLineTax(a,bps)===tax)return [rest,...Array<number>(m).fill(a)];
  }
  return null;
}
/** Tax of a stored folio line (its snapshot rate). */
export const lineTax=(l:{amount:number;taxBps:number})=>gstLineTax(l.amount,l.taxBps);
/** Intra-state split: SGST = half rounded toward zero, CGST = the rest (takes the odd paisa) — always sums to the line tax. */
export function gstSplit(tax:number):{cgst:number;sgst:number}{const sgst=Math.trunc(tax/2)||0;return {cgst:tax-sgst,sgst};}
/** CGST / SGST for a line, split per line (never on totals) so documents, vouchers and reports agree to the paisa. */
export function gstLineSplit(l:{amount:number;taxBps:number}){const tax=lineTax(l);return {tax,...gstSplit(tax)};}
/** Sum of per-line splits. */
export function gstSplitLines(lines:readonly {amount:number;taxBps:number}[]){
  return lines.reduce((a,l)=>{const x=gstLineSplit(l);return {tax:a.tax+x.tax,cgst:a.cgst+x.cgst,sgst:a.sgst+x.sgst};},{tax:0,cgst:0,sgst:0});
}
/** "5 %" style label for a rate in basis points; halves for CGST/SGST ("2.5 %"). */
export const gstPct=(bps:number)=>(bps/100).toLocaleString('en-IN',{maximumFractionDigits:2})+'%';
export const gstHalfPct=(bps:number)=>gstPct(bps/2);

// ───────────────────────────── Financial year and specified premises ─────────────────────────────
export function gstFy(date:string){const y=+date.slice(0,4),m=+date.slice(5,7),start=m>=4?y:y-1;
  return {label:`${start}-${String((start+1)%100).padStart(2,'0')}`,from:`${start}-04-01`,to:`${start+1}-03-31`};}
/** Highest nightly tariff of any accommodation unit in force on any day of a financial year (room rates + dated tariffs). */
export function gstMaxTariffInFy(s:State,fy:{from:string;to:string}):number{
  let max=0;
  for(const room of s.rooms){
    const list=(s.tariffs||[]).filter(t=>t.type===room.type).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:a.effectiveFrom>b.effectiveFrom?1:0);
    let start=room.rate;for(const t of list)if(t.effectiveFrom<=fy.from)start=t.rate;
    max=Math.max(max,start,...list.filter(t=>t.effectiveFrom>fy.from&&t.effectiveFrom<=fy.to).map(t=>t.rate));
  }
  return max;
}
export type GstSpecified={fy:string;prevFy:string;specified:boolean;derived:boolean;optIn:boolean;maxPrevTariff:number;threshold:number;reason:string};
/** Specified-premises status for the financial year containing `date`: derived from last FY's tariffs, or the Owner's opt-in. */
export function gstSpecifiedPremises(s:State,date:string):GstSpecified{
  const fy=gstFy(date),prev=gstFy(addDaysLocal(fy.from,-1));
  const threshold=num(s,'gst.room.threshold',prev.to),maxPrevTariff=gstMaxTariffInFy(s,prev);
  const derived=maxPrevTariff>threshold,optIn=gstValueOn(s,'gst.food.optIn',date)===true,specified=derived||optIn;
  const rs=(n:number)=>'₹'+(n/100).toLocaleString('en-IN',{maximumFractionDigits:2});
  const reason=derived?`a unit was above ${rs(threshold)} per night in FY ${prev.label} (highest ${rs(maxPrevTariff)})`
    :optIn?'declared by the Owner (opt-in)':`no unit above ${rs(threshold)} per night in FY ${prev.label} (highest ${rs(maxPrevTariff)}) and no opt-in`;
  return {fy:fy.label,prevFy:prev.label,specified,derived,optIn,maxPrevTariff,threshold,reason};
}
function addDaysLocal(d:string,n:number){return new Date(Date.parse(d+'T12:00:00Z')+n*86400000).toISOString().slice(0,10);}

// ───────────────────────────── Rates by supply ─────────────────────────────
/** Room accommodation for one unit and one night, on the actual price charged for that night. */
export function gstForRoomNight(s:State,date:string,tariffPaise:number):GstRate{
  const threshold=num(s,'gst.room.threshold',date);
  if(tariffPaise<=threshold)return {bps:num(s,'gst.room.lowBps',date),itc:gstValueOn(s,'gst.room.lowItc',date)===true,category:'room',rule:'Room night at or below the threshold'};
  return {bps:num(s,'gst.room.highBps',date),itc:true,category:'room',rule:'Room night above the threshold'};
}
/** Restaurant / room service supplied by the hotel. */
export function gstForFood(s:State,date:string):GstRate{
  const sp=gstSpecifiedPremises(s,date);
  return sp.specified?{bps:num(s,'gst.food.specifiedBps',date),itc:true,category:'food',rule:`Restaurant · specified premises FY ${sp.fy}`}
    :{bps:num(s,'gst.food.bps',date),itc:false,category:'food',rule:`Restaurant · not specified premises FY ${sp.fy}`};
}
/** Other hotel services (one dated rate each). */
export function gstForService(s:State,date:string,kind:GstServiceKind='other'):GstRate{
  const key:GstKey=kind==='laundry'?'gst.svc.laundryBps':kind==='banquet'?'gst.svc.banquetBps':'gst.svc.otherBps';
  return {bps:num(s,key,date),itc:true,category:kind,rule:kind==='laundry'?'Laundry':kind==='banquet'?'Banquet / hall':'Other hotel service'};
}
export const GST_CHARGE_CATEGORIES:{id:Exclude<GstCategory,'room'>;label:string}[]=[
  {id:'food',label:'Food & beverage (restaurant / room service / minibar)'},{id:'laundry',label:'Laundry'},{id:'banquet',label:'Banquet / hall'},{id:'other',label:'Other service'}];
/** Category of a free-text charge when none is chosen (laundry / banquet / food words; else other). */
export function gstCategoryOfLabel(label:string):Exclude<GstCategory,'room'>{
  const t=label.toLowerCase();
  if(/laund|dry.?clean|ironing|press(ing)?\b/.test(t))return 'laundry';
  if(/banquet|\bhall\b|conference|meeting room/.test(t))return 'banquet';
  if(/food|restaurant|room service|breakfast|lunch|dinner|meal|snack|minibar|mini-bar|\btea\b|coffee|chai|beverage|juice|water bottle/.test(t))return 'food';
  return 'other';
}
/** Rate for a folio charge of a category (room charges use gstForRoomNight with the night's price instead). */
export function gstForCharge(s:State,date:string,category:Exclude<GstCategory,'room'>):GstRate{
  return category==='food'?gstForFood(s,date):gstForService(s,date,category);
}
/** Discounts, credit notes and corrections reduce the room supply: they take the rate of that date's room-night line on the folio,
 *  else its latest room-night line, else the room rule at the booking rate (CA to confirm apportionment across mixed-rate folios). */
export function gstForFolioAdjustment(s:State,b:Booking,date:string):GstRate{
  const rooms=b.lines.filter(l=>l.kind==='room'&&!l.reversalOf);
  const l=rooms.find(x=>x.date===date)??rooms.filter(x=>x.date<=date).slice(-1)[0]??rooms.slice(-1)[0];
  if(l)return {bps:l.taxBps,itc:false,category:'room',rule:'Follows the room-night rate'};
  return gstForRoomNight(s,date,b.rate);
}
/** Tax on a run of nights at one nightly price (quotes, estimates): per night, per line. */
export function gstStayTax(s:State,rate:number,from:string,to:string):number{
  let t=0;if(!/^\d{4}-\d{2}-\d{2}$/.test(from)||!/^\d{4}-\d{2}-\d{2}$/.test(to))return 0;
  for(let d=from,i=0;d<to&&i<731;d=addDaysLocal(d,1),i++)t+=gstLineTax(rate,gstForRoomNight(s,d,rate).bps);return t;
}
/** Rules in force on a date, for the Master configuration panel and the phone view: [supply, rate, note]. */
export function gstMasterSummary(s:State,date:string):[string,string,string][]{
  const th=num(s,'gst.room.threshold',date),rs='₹'+(th/100).toLocaleString('en-IN',{maximumFractionDigits:2});
  const lo=gstForRoomNight(s,date,th),hi=gstForRoomNight(s,date,th+1),f=gstForFood(s,date),sp=gstSpecifiedPremises(s,date);
  const itc=(x:GstRate)=>x.itc?'with ITC':'without ITC';
  return [
    [`Room night ≤ ${rs}`,gstPct(lo.bps),`${itc(lo)} · per room per night on the actual price`],
    [`Room night > ${rs}`,gstPct(hi.bps),itc(hi)],
    ['Restaurant / room service',gstPct(f.bps),`${itc(f)} · FY ${sp.fy} ${sp.specified?'specified premises':'not specified premises'} (${sp.reason})`],
    ['Laundry',gstPct(gstForService(s,date,'laundry').bps),'with ITC'],
    ['Banquet / hall',gstPct(gstForService(s,date,'banquet').bps),'with ITC'],
    ['Other services',gstPct(gstForService(s,date,'other').bps),'with ITC'],
  ];
}

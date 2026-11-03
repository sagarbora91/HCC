// Module 27 · Hall booking quotations + T&C (F6 H2; contract §2–§3, §5; spec §6, §7, §8, §18). Pure helpers used by the QUO_* handlers
// (services/mock/handlers/hall-quote.ts), the screens and the quotation print. Money via hall-billing.ts (one truth table with the server).
//   numbering   QT/<fy>/0001 per financial year (restarts 1 April, no gaps; a cancelled / lost quotation keeps its number; versions keep it)
//   T&C         one template per event type (General where none), versioned by effective date; a version's clauses are SNAPSHOT into the
//               quotation version when it is sent and never change afterwards (a later template save makes a new template version only)
//   guard       a snapshot whose text holds "OWNER TO REPLACE" cannot be sent / printed as final unless the Owner overrides with a reason
//   discounts   Reception 0 % · Manager 10 % · Owner any, of the list value before GST; above the sender's limit → Owner approval (not self)
import type {State,Role} from '../types';
import {fdmFyCode} from './frontdesk-master';
import {hallSlice,hallHasPlaceholder,HALL_PLACEHOLDER,type HallState,type HallEventType,type TncTemplate,type TncSnapshot,type TncClause,type Quotation,
  type QuoteVersion,type QuoteLine,type AdvanceRow,type Venue,type HallSlot,type HallSetup} from './hall';
import {hallPrice,hallPlanAmounts,hallAdvancePlan,hallDiscountBps,hallDiscountLimit,hallDiscountWithin,type HallPricedLine,type HallTotals} from './hall-billing';
import {hallTncSeed} from '../../fixtures/ext/hall-seed';

// ── T&C templates ──
/** The templates in force: the stored ones, else the seed (placeholders). */
export const hallTemplates=(x:HallState):TncTemplate[]=>x.templates?.length?x.templates:hallTncSeed();
/** The template of an event type, else the General one. */
export function hallTemplateFor(x:HallState,eventType:HallEventType):TncTemplate|undefined{
  const t=hallTemplates(x);return t.find(t=>t.eventType===eventType)??t.find(t=>t.eventType==='GENERAL');
}
/** The version in force on a date (latest effectiveFrom ≤ date; the first version when none is yet in force). */
export function hallTemplateVersion(t:TncTemplate,date:string){
  const sorted=[...t.versions].sort((a,b)=>b.effectiveFrom.localeCompare(a.effectiveFrom)||b.v-a.v);
  return sorted.find(v=>v.effectiveFrom<=date)??sorted[sorted.length-1];
}
/** A deep copy of the clauses in force for an event type on a date (the snapshot a quotation version keeps). */
export function hallTncSnapshot(x:HallState,eventType:HallEventType,date:string):TncSnapshot{
  const t=hallTemplateFor(x,eventType);if(!t)return {templateId:'',v:0,clauses:[],hasPlaceholder:true};
  const v=hallTemplateVersion(t,date),clauses=v.clauses.map(c=>({n:c.n,heading:c.heading,text:c.text}));
  return {templateId:t.id,v:v.v,clauses,hasPlaceholder:!clauses.length||hallHasPlaceholder(clauses)};
}
/** Validated clauses for a template save: numbered 1…n in order, heading 2–80, text 3–2000 characters. */
export function hallTncClausesProblem(clauses:unknown):string|undefined{
  if(!Array.isArray(clauses)||!clauses.length)return 'Give at least one clause.';
  if(clauses.length>40)return 'At most 40 clauses.';
  for(const [i,c] of clauses.entries()){const h=String(c?.heading??'').trim(),t=String(c?.text??'').trim();
    if(h.length<2||h.length>80)return `Clause ${i+1}: heading 2–80 characters.`;if(t.length<3||t.length>2000)return `Clause ${i+1}: text 3–2000 characters.`;}
  return undefined;
}
export const hallTncClauses=(clauses:any[]):TncClause[]=>clauses.map((c,i)=>({n:i+1,heading:String(c.heading).trim(),text:String(c.text).trim()}));

// ── Numbering ──
/** FY code of a date: 2026-11-03 → 2627; 2027-04-01 → 2728. */
export const hallQuoFy=(date:string)=>fdmFyCode(date);
export const hallQuoFormat=(fy:string,seq:number)=>`QT/${fy}/${String(seq).padStart(4,'0')}`;
/** The next quotation number on a date: consecutive per FY over every quotation ever made (nothing is deleted, so no gaps). */
export function hallQuoNext(x:HallState,date:string):{number:string;fy:string;seq:number}{
  const fy=hallQuoFy(date);let last=0;
  for(const q of x.quotations)if(q.fy===fy){const n=Number(q.number.split('/')[2]);if(n>last)last=n;}
  return {fy,seq:last+1,number:hallQuoFormat(fy,last+1)};
}

// ── Lines ──
/** Default lines from the masters: hall rent of the slot, the menu per plate × plates, then the extras given (list = rate). */
export function hallQuoteDefaultLines(x:HallState,o:{venueId:string;slot:HallSlot;menuId?:string;plates?:number}):QuoteLine[]{
  const out:QuoteLine[]=[];const v=x.venues.find(v=>v.id===o.venueId),slot=v?.slots.find(s=>s.slot===o.slot);
  if(v&&slot)out.push({id:'L1',kind:'HALL',refId:v.id,desc:`${v.name} · ${o.slot.toLowerCase().replace('_',' ')}`,qty:1,unit:'slot',ratePaise:slot.ratePaise,listRatePaise:slot.ratePaise,discountPaise:0,gstClass:v.gstClass||'HALL_RENT'});
  const m=o.menuId?x.menus.find(m=>m.id===o.menuId):undefined;
  if(m&&o.plates)out.push({id:`L${out.length+1}`,kind:'MENU',refId:m.id,desc:`${m.name} per plate`,qty:o.plates,unit:'plate',ratePaise:m.perPlatePaise,listRatePaise:m.perPlatePaise,discountPaise:0,gstClass:'BANQUET_FOOD'});
  return out;
}
/** The list rate a line should carry from the masters (hall slot / menu); undefined when the line has no master (extras, other). */
export function hallMasterRate(x:HallState,l:Pick<QuoteLine,'kind'|'refId'>,slot:HallSlot):number|undefined{
  if(l.kind==='HALL'){const v=x.venues.find(v=>v.id===l.refId);return v?.slots.find(s=>s.slot===slot)?.ratePaise;}
  if(l.kind==='MENU')return x.menus.find(m=>m.id===l.refId)?.perPlatePaise;
  return undefined;
}

// ── Discounts ──
/** True when the lines' discount is above the role's limit (an Owner approval is then needed before sending). */
export const hallQuoteNeedsApproval=(x:HallState,role:Role,lines:readonly QuoteLine[])=>!hallDiscountWithin(lines,hallDiscountLimit(x.settings,role));

// ── Views ──
export const hallQuoteVersion=(q:Quotation,v?:number)=>q.versions.find(w=>w.v===(v??q.currentVersion));
/** The date the version is priced on: its send date once sent (the printed totals stay those), else today. */
export const hallQuotePriceDate=(s:State,v:QuoteVersion)=>v.sentAt?v.sentAt.slice(0,10):s.date;
/** True when this version may be printed / sent as final (no placeholder, or the Owner overrode it). */
export const hallQuoteFinal=(v:QuoteVersion)=>!v.tnc.hasPlaceholder||!!v.placeholderOverride;
export type HallQuotePrint={number:string;version:number;label:string;status:QuoteVersion['status'];watermark?:string;
  customer?:Quotation['customer'];header:QuoteVersion['header'];venueName:string;lines:HallPricedLine[];totals:HallTotals;discountBps:number;
  advance:AdvanceRow[];tnc:TncSnapshot;validUntil:string;sentAt?:string;placeholderOverride?:QuoteVersion['placeholderOverride'];
  signature:{acceptedBy:string;date:string;signature:string}};
/** The quotation print model (spec §18: printed totals = screen totals — both come from here). */
export function hallQuotePrint(s:State,quotationId:string,vNo?:number):HallQuotePrint|undefined{
  const x=hallSlice(s),q=x.quotations.find(q=>q.id===quotationId);if(!q)return undefined;const v=hallQuoteVersion(q,vNo);if(!v)return undefined;
  const priced=hallPrice(s,v.lines,hallQuotePriceDate(s,v)),total=priced.totals.totalPaise;
  const advance=v.advance?.length?hallPlanAmounts(total,v.advance):hallAdvancePlan(total,v.header.dates[0],hallQuotePriceDate(s,v),x.settings);
  const watermark=v.status==='DRAFT'?'DRAFT — NOT AN OFFER':!hallQuoteFinal(v)?'NOT FINAL — T&C TO BE REPLACED':v.status==='SUPERSEDED'?'SUPERSEDED':v.status==='EXPIRED'?'EXPIRED':undefined;
  return {number:q.number,version:v.v,label:`${q.number} v${v.v}`,status:v.status,...(watermark?{watermark}:{}),customer:q.customer,header:v.header,
    venueName:x.venues.find(o=>o.id===v.header.venueId)?.name??v.header.venueId,lines:priced.lines,totals:priced.totals,discountBps:hallDiscountBps(v.lines),
    advance,tnc:v.tnc,validUntil:v.validUntil,...(v.sentAt?{sentAt:v.sentAt}:{}),...(v.placeholderOverride?{placeholderOverride:v.placeholderOverride}:{}),
    signature:{acceptedBy:'',date:'',signature:''}};
}
/** Register rows (spec §18 quotation register): number, version, customer, value, status. */
export function hallQuoteRegister(s:State){
  const x=hallSlice(s);
  return x.quotations.map(q=>{const v=hallQuoteVersion(q)!;return {id:q.id,number:q.number,version:v.v,customer:q.customer?.name??'',eventType:v.header.eventType,date:v.header.dates[0],
    status:v.status,totalPaise:hallPrice(s,v.lines,hallQuotePriceDate(s,v)).totals.totalPaise,validUntil:v.validUntil};});
}

// ── CSV (deployment/import-templates/halls.csv, tnc-templates.csv) ──
const FORMULA=/^[=+@\t\r]/;
const esc=(v:unknown)=>{const t=String(v??'');return /[",\n\r]/.test(t)?`"${t.replace(/"/g,'""')}"`:t;};
/** RFC 4180 rows (quoted fields, doubled quotes, CRLF or LF). */
export function hallCsvParse(text:string):string[][]{
  const rows:string[][]=[];let row:string[]=[],f='',q=false;
  for(let i=0;i<text.length;i++){const c=text[i];
    if(q){if(c==='"'){if(text[i+1]==='"'){f+='"';i++;}else q=false;}else f+=c;continue;}
    if(c==='"')q=true;else if(c===','){row.push(f);f='';}else if(c==='\n'||c==='\r'){if(c==='\r'&&text[i+1]==='\n')i++;row.push(f);f='';if(row.some(x=>x!==''))rows.push(row);row=[];}else f+=c;}
  row.push(f);if(row.some(x=>x!==''))rows.push(row);return rows;
}
export const HALL_VENUE_CSV=['id','name','kind','gstClass','combinedOf','minPlates','capTheatre','capRound','capUshape','capFloating','capClassroom','slot','from','to','ratePaise','slotMinPlates'] as const;
export const HALL_TNC_CSV=['templateId','eventType','v','effectiveFrom','n','heading','text'] as const;
const CAP:[string,HallSetup][]=[['capTheatre','THEATRE'],['capRound','ROUND'],['capUshape','USHAPE'],['capFloating','FLOATING'],['capClassroom','CLASSROOM']];
/** One row per hall × slot. */
export function hallVenuesToCsv(venues:readonly Venue[]):string{
  const lines=[HALL_VENUE_CSV.join(',')];
  for(const v of venues)for(const s of v.slots){const r:Record<string,unknown>={id:v.id,name:v.name,kind:v.kind,gstClass:v.gstClass,combinedOf:(v.combinedOf??[]).join('|'),minPlates:v.minPlates??'',
    slot:s.slot,from:s.from,to:s.to,ratePaise:s.ratePaise,slotMinPlates:s.minPlates??''};for(const [c,k] of CAP)r[c]=v.capacity[k]??'';lines.push(HALL_VENUE_CSV.map(c=>esc(r[c])).join(','));}
  return lines.join('\n')+'\n';
}
export type HallCsvResult<T>={rows:T[];errors:{line:number;message:string}[]};
const header=(rows:string[][],want:readonly string[])=>rows.length&&rows[0].join(',')===want.join(',')?undefined:`The first line must be: ${want.join(',')}`;
export function hallVenuesFromCsv(text:string):HallCsvResult<Omit<Venue,'version'|'active'>&{active:boolean;version:number}>{
  const rows=hallCsvParse(text),errors:{line:number;message:string}[]=[];const h=header(rows,HALL_VENUE_CSV);if(h)return {rows:[],errors:[{line:1,message:h}]};
  const by=new Map<string,Venue>();
  rows.slice(1).forEach((cells,i)=>{const line=i+2,r=Object.fromEntries(HALL_VENUE_CSV.map((c,j)=>[c,(cells[j]??'').trim()]));
    if(cells.some(c=>FORMULA.test(c))){errors.push({line,message:'A cell starts with = + @ (formula) — refused.'});return;}
    if(!r.id||!r.name||!['HALL','LAWN','TERRACE'].includes(r.kind)){errors.push({line,message:'id, name and kind (HALL / LAWN / TERRACE) are required.'});return;}
    const rate=Number(r.ratePaise);if(!['MORNING','AFTERNOON','EVENING','FULL_DAY'].includes(r.slot)||!/^\d{2}:\d{2}$/.test(r.from)||!/^\d{2}:\d{2}$/.test(r.to)||!Number.isSafeInteger(rate)||rate<0){
      errors.push({line,message:'slot, from, to (HH:mm) and ratePaise (whole paise) are required.'});return;}
    let v=by.get(r.id);
    if(!v){const capacity:Venue['capacity']={};for(const [c,k] of CAP)if(r[c])capacity[k]=Number(r[c]);
      v={id:r.id,name:r.name,kind:r.kind as Venue['kind'],capacity,slots:[],gstClass:r.gstClass||'HALL_RENT',...(r.combinedOf?{combinedOf:r.combinedOf.split('|')}:{}),
        ...(r.minPlates?{minPlates:Number(r.minPlates)}:{}),active:true,version:1};by.set(r.id,v);}
    if(v.slots.some(s=>s.slot===r.slot)){errors.push({line,message:`${r.id}: slot ${r.slot} appears twice.`});return;}
    v.slots.push({slot:r.slot as HallSlot,from:r.from,to:r.to,ratePaise:rate,...(r.slotMinPlates?{minPlates:Number(r.slotMinPlates)}:{})});});
  return {rows:[...by.values()],errors};
}
/** One row per template × version × clause. */
export function hallTncToCsv(templates:readonly TncTemplate[]):string{
  const lines=[HALL_TNC_CSV.join(',')];
  for(const t of templates)for(const v of t.versions)for(const c of v.clauses)lines.push([t.id,t.eventType,v.v,v.effectiveFrom,c.n,c.heading,c.text].map(esc).join(','));
  return lines.join('\n')+'\n';
}
export function hallTncFromCsv(text:string):HallCsvResult<TncTemplate>{
  const rows=hallCsvParse(text),errors:{line:number;message:string}[]=[];const h=header(rows,HALL_TNC_CSV);if(h)return {rows:[],errors:[{line:1,message:h}]};
  const by=new Map<string,TncTemplate>();
  const types=['GENERAL','WEDDING','RECEPTION','BIRTHDAY','ENGAGEMENT','CORPORATE','CONFERENCE','RELIGIOUS','OTHER'];
  rows.slice(1).forEach((cells,i)=>{const line=i+2,[id,eventType,vs,eff,ns,heading,text]=cells.map(c=>c??'');
    if(cells.some(c=>FORMULA.test(c))){errors.push({line,message:'A cell starts with = + @ (formula) — refused.'});return;}
    const v=Number(vs),n=Number(ns);
    if(!id||!types.includes(eventType)||!Number.isInteger(v)||v<1||!/^\d{4}-\d{2}-\d{2}$/.test(eff)||!Number.isInteger(n)||n<1||!heading.trim()||!text.trim()){
      errors.push({line,message:'templateId, eventType, v, effectiveFrom (yyyy-mm-dd), n, heading and text are required.'});return;}
    let t=by.get(id);if(!t){t={id,eventType:eventType as TncTemplate['eventType'],versions:[]};by.set(id,t);}
    let ver=t.versions.find(x=>x.v===v);if(!ver){ver={v,effectiveFrom:eff,clauses:[]};t.versions.push(ver);}
    ver.clauses.push({n,heading,text});});
  return {rows:[...by.values()],errors};
}
export {HALL_PLACEHOLDER};

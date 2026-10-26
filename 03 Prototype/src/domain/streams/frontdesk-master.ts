// MA-5 · Front-desk lists (plan HCC_Customisation_Features_Plan_v1.md Part 2 §8 M9, M11, M12, M14) on the MA-0 masters framework
// (domain/streams/masters.ts). Four masters in Master configuration › Front-desk policies:
//   bookingSources (M9)  source + market segment + commission % (Owner-only money field); OTA channels and company accounts register
//                        their own rows automatically ("auto" rows, read-through until the first saved change); bookings map to a row by
//                        channel / company / name (fdmSourceOf) — Booking keeps its plain `source` text, no new Booking field.
//   idTypes (M11)        accepted on/off, needs a photo, foreigner-only. The number checks stay in guests.ts (guestIdNumberError, chosen by
//                        the first word of the name: Aadhaar / Passport / Driving / Voter / PAN / other).
//   numberSeries (M12)   per document (tax invoice, bill of supply, credit note, receipt, KOT, registration card): prefix, start, padding,
//                        reset yearly on 1 April or never, active from. Owner-only (money list). DEC-MA-5: ONE tax-invoice series for the
//                        hotel; the master lets more documents get their own later. GST rule (CGST Rules r.46(b)): consecutive, unique per
//                        financial year, no gaps — illustrative, CA to confirm. A series that has issued documents never changes; a new
//                        series starts from a date with no documents (normally 1 April of the next FY). CHECKOUT and the credit-note
//                        approval allocate through fdmAllocate (handlers/folio.ts). The seeded tax-invoice / credit-note series continue
//                        the old counters (s.invoiceSeqC / s.creditNoteSeqC): numbers stay DEMO/2026-27/NNNN and DEMO-CN/2026-27/NNNN.
//   policies (M14)       named cancellation / no-show / deposit policies (free-cancel hours, charge %, deposit %, applies to sources);
//                        "Default" = today's global behaviour (no charges, no advance) and applies to every source not named elsewhere.
//                        Owner-only (money list). Charges are computed for the screens (fdmCancelCharge, fdmNoShowCharge, fdmAdvanceDue);
//                        nothing is posted automatically.
// Bookings, figures and the FY26 history are unchanged: every seed comes from the values already in the fixture.
import type {State,Booking} from '../types';
import {nights} from '../money';
import {registerMaster,mstRows,mstByCode,mstSpec,type MasterRow,type MasterSpec,type MasterUsage} from './masters';
import {chnSlice} from './channels';
import {grpSlice} from './groups';
import {guestIdKind} from './guests';
import {HccError} from '../errors';

export const FDM_SOURCES='bookingSources',FDM_ID_TYPES='idTypes',FDM_SERIES='numberSeries',FDM_POLICIES='policies';

// ───────────────────────────── State (allocation counters only) ─────────────────────────────
/** Last number issued per series and numbering year (key `<seriesId>|<FY label or *>`) and the date of that document. The seeded
 *  tax-invoice and credit-note series keep using s.invoiceSeqC / s.creditNoteSeqC in FY 2026-27, so the fixture state is unchanged. */
export type FDMCounter={last:number;lastDate:string;
  /** Numbers issued in this year (last − start + 1 when the series started above 1). */count?:number};
export type FDMState={counters:Record<string,FDMCounter>};
const fdmSlice=(s:State):FDMState=>{const x=s.frontdeskMasterX??(s.frontdeskMasterX={counters:{}});x.counters??={};return x;};

const ISO=/^\d{4}-\d{2}-\d{2}$/;
const slug=(t:string)=>t.toUpperCase().replace(/[^A-Z0-9]+/g,'-').replace(/^-+|-+$/g,'').slice(0,20).replace(/-+$/,'')||'X';
const lc=(t:unknown)=>String(t??'').trim().toLowerCase();

// ═════════════════════════════ M9 · Booking sources and market segments ═════════════════════════════
export const FDM_SEGMENTS=[{value:'DIRECT',label:'Direct'},{value:'OTA',label:'OTA'},{value:'CORPORATE',label:'Corporate'},{value:'AGENT',label:'Travel agent'},
  {value:'GROUP',label:'Group'},{value:'OTHER',label:'Other'}];
export const fdmSegmentLabel=(v:unknown)=>FDM_SEGMENTS.find(x=>x.value===v)?.label??String(v??'—');
export type FDMSourceRow=MasterRow&{segment:string;channel?:string;commissionBps?:number;auto?:boolean};
/** Fixture values: the sources the booking forms offered (RES_SOURCES), the website, the generic OTA and agent rows. */
export const FDM_SOURCE_SEED:FDMSourceRow[]=[
  {id:'bsrc-walkin',code:'WALKIN',name:'Walk-in',segment:'DIRECT',sortOrder:10,active:true},
  {id:'bsrc-phone',code:'PHONE',name:'Phone',segment:'DIRECT',sortOrder:20,active:true},
  {id:'bsrc-whatsapp',code:'WHATSAPP',name:'WhatsApp',segment:'DIRECT',sortOrder:30,active:true},
  {id:'bsrc-direct',code:'DIRECT',name:'Direct',segment:'DIRECT',sortOrder:40,active:true},
  {id:'bsrc-website',code:'WEBSITE',name:'Website',segment:'DIRECT',sortOrder:50,active:true},
  {id:'bsrc-ota',code:'OTA',name:'OTA',segment:'OTA',sortOrder:60,active:true},
  {id:'bsrc-agent',code:'AGENT',name:'Agent',segment:'AGENT',commissionBps:0,sortOrder:70,active:true},
  {id:'bsrc-corporate',code:'CORPORATE',name:'Corporate',segment:'CORPORATE',sortOrder:80,active:true},
];
/** Old source texts that mean a master row (the fixture's "OTA · manual" is the generic OTA row). */
const SOURCE_ALIASES:Record<string,string>={'ota · manual':'OTA','ota - manual':'OTA'};
const autoId=(code:string)=>'bsrc-auto-'+code.toLowerCase();
/** Rows that OTA channels and company accounts register for themselves (not yet saved in the list). */
export function fdmAutoSources(s:State,rows:FDMSourceRow[]):FDMSourceRow[]{
  const out:FDMSourceRow[]=[];const codes=new Set(rows.map(r=>r.code));
  const chans=new Set(rows.filter(r=>r.segment==='OTA'&&r.channel).map(r=>lc(r.channel)));
  const comps=new Set(rows.filter(r=>r.segment==='CORPORATE'&&r.channel).map(r=>lc(r.channel)));
  let n=1000;
  for(const c of chnSlice(s).channels){
    if(chans.has(lc(c.name)))continue;const code=slug('OTA-'+c.id);if(codes.has(code))continue;codes.add(code);
    out.push({id:autoId(code),code,name:('OTA · '+c.name).slice(0,60),segment:'OTA',channel:c.name,commissionBps:c.commissionBps,sortOrder:n+=10,active:true,auto:true});
  }
  for(const c of grpSlice(s).companies){
    if(comps.has(lc(c.name))||comps.has(lc(c.id)))continue;const code=slug('CORP-'+c.id);if(codes.has(code))continue;codes.add(code);
    out.push({id:autoId(code),code,name:c.name.slice(0,60),segment:'CORPORATE',channel:c.name,sortOrder:n+=10,active:true,auto:true});
  }
  return out;
}
/** The row a booking belongs to: its OTA channel row, its company row, then the row named like its source (retired rows count). */
export function fdmSourceOf(s:State,b:Pick<Booking,'source'|'otaChannel'|'company'>,rows=mstRows(s,FDM_SOURCES) as FDMSourceRow[]):FDMSourceRow|undefined{
  const src=lc(b.source);
  if(b.otaChannel&&src.startsWith('ota')){const r=rows.find(x=>x.segment==='OTA'&&lc(x.channel)===lc(b.otaChannel));if(r)return r;}
  if(src.startsWith('ota · ')){const ch=src.slice(6);const r=rows.find(x=>x.segment==='OTA'&&lc(x.channel)===ch);if(r)return r;}
  if(b.company?.name){const r=rows.find(x=>x.segment==='CORPORATE'&&lc(x.channel)===lc(b.company!.name));if(r)return r;}
  const alias=SOURCE_ALIASES[src];
  return rows.find(x=>lc(x.name)===src||x.code===String(b.source??'').toUpperCase())??(alias?rows.find(x=>x.code===alias):undefined);
}
/** Segment of a booking ("OTHER" when its source is not in the list). */
export const fdmSegmentOf=(s:State,b:Booking)=>fdmSourceOf(s,b)?.segment??'OTHER';
/** Source names the booking forms offer: active rows that are not channel / company rows (those come from the OTA name and the
 *  company fields), plus the current value when it is no longer offered. */
export function fdmSourceChoices(s:State,current?:string):string[]{
  const rows=mstRows(s,FDM_SOURCES,{active:true}) as FDMSourceRow[];
  const out=rows.filter(r=>!r.channel).map(r=>r.name);
  if(current&&!out.includes(current))out.push(current);
  return out;
}
/** OTA channel names for the booking form's channel list: channel rows of the master (active) and the fixed demo names. */
export function fdmOtaChannelNames(s:State,extra:readonly string[]=[]):string[]{
  const rows=mstRows(s,FDM_SOURCES,{active:true}) as FDMSourceRow[];
  return [...new Set([...rows.filter(r=>r.segment==='OTA'&&r.channel).map(r=>String(r.channel)),...extra])];
}
/** Commission (paise) of a booking: the row's commission % (OTA rows fall back to their channel's rate) × room charges. */
export function fdmCommission(s:State,b:Booking,row=fdmSourceOf(s,b)):number{
  if(!row)return 0;let bps=Number(row.commissionBps??0);
  if(!bps&&row.segment==='OTA'&&row.channel){const c=chnSlice(s).channels.find(x=>lc(x.name)===lc(row.channel));bps=c?.commissionBps??0;}
  return Math.round(b.rate*Math.max(0,nights(b.arrival,b.departure))*bps/10000);
}
export type FDMSourceReportRow={code:string;name:string;segment:string;bookings:number;cancelled:number;nights:number;roomRevenue:number;commission:number;retired:boolean};
/** Bookings by source and segment for arrivals in [from, to]: count (cancelled / no-show separate), room nights, agreed room charges
 *  (rate × nights) and commission. Unlisted sources are grouped under "Other (not in the list)". */
export function fdmSourceReport(s:State,from:string,to:string):{rows:FDMSourceReportRow[];segments:{segment:string;label:string;bookings:number;nights:number;roomRevenue:number;commission:number}[];total:{bookings:number;nights:number;roomRevenue:number;commission:number}}{
  const all=mstRows(s,FDM_SOURCES) as FDMSourceRow[];const by=new Map<string,FDMSourceReportRow>();
  for(const b of s.bookings){
    if(b.arrival<from||b.arrival>to)continue;
    const r=fdmSourceOf(s,b,all),code=r?.code??'',k=code||'—';
    const x=by.get(k)??{code,name:r?.name??'Other (not in the list)',segment:r?.segment??'OTHER',bookings:0,cancelled:0,nights:0,roomRevenue:0,commission:0,retired:!!r&&!r.active};
    if(b.status==='Cancelled'||b.status==='No show'||b.status==='Expired')x.cancelled++;
    else{const n=Math.max(0,nights(b.arrival,b.departure));x.bookings++;x.nights+=n;x.roomRevenue+=b.rate*n;x.commission+=fdmCommission(s,b,r);}
    by.set(k,x);
  }
  const order=new Map(all.map((r,i)=>[r.code,i]));
  const rows=[...by.values()].sort((a,b)=>(order.get(a.code)??1e9)-(order.get(b.code)??1e9)||(a.name<b.name?-1:1));
  const segments=FDM_SEGMENTS.map(g=>{const l=rows.filter(r=>r.segment===g.value);return {segment:g.value,label:g.label,bookings:l.reduce((a,r)=>a+r.bookings,0),nights:l.reduce((a,r)=>a+r.nights,0),
    roomRevenue:l.reduce((a,r)=>a+r.roomRevenue,0),commission:l.reduce((a,r)=>a+r.commission,0)};}).filter(g=>g.bookings>0);
  const total={bookings:rows.reduce((a,r)=>a+r.bookings,0),nights:rows.reduce((a,r)=>a+r.nights,0),roomRevenue:rows.reduce((a,r)=>a+r.roomRevenue,0),commission:rows.reduce((a,r)=>a+r.commission,0)};
  return {rows,segments,total};
}
const sourceUsage=(s:State,row:FDMSourceRow):MasterUsage=>{
  const rows=mstRows(s,FDM_SOURCES) as FDMSourceRow[];
  const n=s.bookings.filter(b=>fdmSourceOf(s,b,rows)?.code===row.code).length;
  const pol=(mstRows(s,FDM_POLICIES) as FDMPolicyRow[]).filter(p=>(p.sources??[]).includes(row.code));
  const where=[...(n?[`Bookings ${n}`]:[]),...(pol.length?[`Policies ${pol.map(p=>p.name).join(', ')}`]:[]),...(row.auto?[`Registered by ${row.segment==='OTA'?'OTA channel':'company account'} ${row.channel}`]:[])];
  return {count:n+pol.length+(row.auto?1:0),where};
};
/** Rows stored in s.mastersX.rows.bookingSources (after the first change) or the seed, plus the auto rows. */
function sourceRead(s:State):FDMSourceRow[]{
  const stored=s.mastersX?.rows?.[FDM_SOURCES] as FDMSourceRow[]|undefined;
  const base=Array.isArray(stored)?stored:FDM_SOURCE_SEED.map(r=>({...r}));
  return [...base,...fdmAutoSources(s,base)];
}
function sourceWrite(s:State,rows:FDMSourceRow[]){
  const x=s.mastersX??(s.mastersX={rows:{},history:[]});x.rows??={};x.history??=[];
  // Saving the list keeps every row (auto rows become ordinary rows; "auto" stays as a marker of where they came from).
  x.rows[FDM_SOURCES]=rows.map(r=>({...r}));
}
export const FDM_SOURCE_SPEC:MasterSpec<FDMSourceRow>={
  id:FDM_SOURCES,label:'Booking sources',singular:'booking source',group:'frontdesk',prefix:'BSR',money:false,permission:'masters',
  help:'Where bookings come from, by market segment. OTA channels and company accounts add their own rows; commission % is Owner-only.',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'WALKIN',help:'Short code, e.g. WALKIN, OTA-BOOKNEST. Cannot change once bookings use it.'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Walk-in',help:'Shown on booking forms and reports.'},
    {key:'segment',label:'Market segment',type:'select',required:true,options:FDM_SEGMENTS,help:'Reports group sources by segment.'},
    {key:'channel',label:'OTA channel / company',type:'text',maxLength:60,help:'For an OTA or corporate row: the channel or company name it stands for (filled in automatically).'},
    {key:'commissionBps',label:'Commission',type:'percent',min:0,max:5000,money:true,help:'Agents and OTAs: % of room charges (OTA rows without one use the channel’s rate). Owner only.'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'active',label:'Active',type:'toggle'},
  ],
  inUse:sourceUsage,
  validate:(s,row,rows)=>{
    if(!FDM_SEGMENTS.some(x=>x.value===row.segment))return 'Choose a market segment.';
    if(row.channel&&!['OTA','CORPORATE','AGENT'].includes(row.segment))return 'Only OTA, corporate and agent rows name a channel or company.';
    const dup=rows.find(r=>r.id!==row.id&&r.active&&row.channel&&r.segment===row.segment&&lc(r.channel)===lc(row.channel));
    if(dup)return `${dup.name} already stands for ${row.channel}.`;
    return undefined;
  },
  store:{read:sourceRead,write:sourceWrite},
};

// ═════════════════════════════ M11 · Guest ID types ═════════════════════════════
export type FDMIdTypeRow=MasterRow&{accepted:boolean;needsPhoto:boolean;foreignerOnly:boolean};
const ID_NAMES=['Aadhaar (demo)','Passport (demo)','Driving licence (demo)','Voter ID (demo)','PAN (demo)','Other (demo)'];
/** Fixture values (guests.ts guestIdTypes) + the foreigner-only passport-and-visa type (plan M11). */
export const FDM_ID_SEED:FDMIdTypeRow[]=[
  ...['AADHAAR','PASSPORT','DL','VOTER','PAN','OTHER'].map((code,i)=>({id:'idt-'+code.toLowerCase(),code,name:ID_NAMES[i],accepted:true,needsPhoto:false,foreignerOnly:false,sortOrder:(i+1)*10,active:true})),
  {id:'idt-visa',code:'VISA',name:'Passport + visa (demo, foreign national)',accepted:true,needsPhoto:true,foreignerOnly:true,sortOrder:70,active:true},
];
/** The number check of a type (guests.ts guestIdNumberError, chosen by the first word of the name). */
export const FDM_ID_CHECK:Record<string,string>={aadhaar:'Aadhaar: 12 digits with checksum',passport:'Passport: 1 letter + 7 digits',dl:'Driving licence: 2 letters + 13 digits',
  voter:'Voter ID: 3 letters + 7 digits',pan:'PAN: 5 letters, 4 digits, 1 letter',other:'4–20 letters or digits'};
export const fdmIdCheck=(name:string)=>FDM_ID_CHECK[guestIdKind(name)];
const indian=(nationality?:string)=>!nationality||lc(nationality)==='indian'||lc(nationality)==='india';
/** ID types the registration form offers: active, accepted, foreigner-only ones only for a foreign national; the current value stays. */
export function fdmIdTypeChoices(s:State,nationality?:string,current?:string):string[]{
  const rows=(mstRows(s,FDM_ID_TYPES,{active:true}) as FDMIdTypeRow[]).filter(r=>r.accepted!==false&&(!r.foreignerOnly||!indian(nationality)));
  const out=rows.map(r=>r.name);if(current&&!out.includes(current))out.push(current);return out;
}
/** Row of an ID type name (retired rows resolve). */
export const fdmIdType=(s:State,name?:string)=>name?(mstRows(s,FDM_ID_TYPES) as FDMIdTypeRow[]).find(r=>lc(r.name)===lc(name)):undefined;
/** Why a type cannot be used for a new registration ('' when it can): switched off, retired, or foreigner-only for an Indian guest. */
export function fdmIdTypeProblem(s:State,name:string|undefined,nationality?:string):string{
  const r=fdmIdType(s,name);if(!name||!r)return '';
  if(!r.active)return `${r.name} is retired — choose another ID type.`;
  if(r.accepted===false)return `${r.name} is not accepted at check-in (switched off in Master configuration).`;
  if(r.foreignerOnly&&indian(nationality))return `${r.name} is for foreign nationals only.`;
  return '';
}
export const FDM_ID_SPEC:MasterSpec<FDMIdTypeRow>={
  id:FDM_ID_TYPES,label:'Guest ID types',singular:'ID type',group:'frontdesk',prefix:'IDT',money:false,permission:'masters',
  help:'ID documents accepted at registration. The number check comes from the first word of the name (Aadhaar, Passport, Driving, Voter, PAN; anything else: 4–20 letters or digits).',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'AADHAAR'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Aadhaar',help:'Starts with the document word so the right number check applies.'},
    {key:'accepted',label:'Accepted at check-in (on / off)',type:'toggle',help:'Off = not offered on new registrations; guests who already used it keep it.'},
    {key:'needsPhoto',label:'Needs a photo of the document',type:'toggle',help:'Registration asks for a photo capture (ID capture on the server / APK).'},
    {key:'foreignerOnly',label:'Foreign nationals only',type:'toggle',help:'Offered only when the nationality is not Indian (Form C).'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'active',label:'Active',type:'toggle'},
  ],
  inUse:(s,row)=>{const n=(s.guests??[]).filter(g=>lc(g.idType)===lc(row.name)).length;return {count:n,where:n?[`Guest profiles ${n} · check: ${fdmIdCheck(row.name)}`]:[]};},
  seed:()=>FDM_ID_SEED.map(r=>({...r})),
};

// ═════════════════════════════ M12 · Document numbering series ═════════════════════════════
export const FDM_DOCS=[{value:'INV',label:'Tax invoice',gst:true},{value:'BOS',label:'Bill of supply',gst:true},{value:'CRN',label:'Credit note',gst:true},
  {value:'RCP',label:'Receipt',gst:false},{value:'KOT',label:'Kitchen order ticket (KOT)',gst:false},{value:'REG',label:'Registration card',gst:false}] as const;
export type FDMDoc=typeof FDM_DOCS[number]['value'];
export const fdmDocLabel=(d:unknown)=>FDM_DOCS.find(x=>x.value===d)?.label??String(d??'—');
export const fdmDocIsGst=(d:unknown)=>!!FDM_DOCS.find(x=>x.value===d)?.gst;
export const FDM_RESETS=[{value:'FY',label:'Every financial year (1 April)'},{value:'NEVER',label:'Never'}];
/** GST note shown with the list (illustrative). */
export const FDM_GST_NOTE='GST invoices, bills of supply and credit notes: one consecutive series, unique per financial year, no gaps, at most 16 characters (CGST Rules r.46(b)) — illustrative, CA to confirm.';
export const FDM_NUMBER_MAX=16;
export type FDMSeriesRow=MasterRow&{document:FDMDoc;prefix:string;start:number;padding:number;reset:'FY'|'NEVER';activeFrom:string};
/** The FY of the old fixed series (folio.ts FOLIO_INVOICE_SERIES = 'DEMO/2026-27/'): the seeded series continue its counters there. */
export const FDM_LEGACY_FY='2026-27';
const LEGACY:Record<string,'invoiceSeqC'|'creditNoteSeqC'>={'nsr-inv':'invoiceSeqC','nsr-crn':'creditNoteSeqC'};
/** Seeds: the numbers the prototype already prints (DEMO/2026-27/0001, DEMO-CN/2026-27/0001) + one row per other document. */
export const FDM_SERIES_SEED:FDMSeriesRow[]=[
  {id:'nsr-inv',code:'INV',name:'Tax invoice',document:'INV',prefix:'DEMO',start:1,padding:4,reset:'FY',activeFrom:'2025-04-01',sortOrder:10,active:true},
  {id:'nsr-bos',code:'BOS',name:'Bill of supply',document:'BOS',prefix:'BOS',start:1,padding:4,reset:'FY',activeFrom:'2025-04-01',sortOrder:20,active:true},
  {id:'nsr-crn',code:'CRN',name:'Credit note',document:'CRN',prefix:'DEMO-CN',start:1,padding:4,reset:'FY',activeFrom:'2025-04-01',sortOrder:30,active:true},
  {id:'nsr-rcp',code:'RCP',name:'Receipt',document:'RCP',prefix:'RCP',start:1,padding:5,reset:'NEVER',activeFrom:'2025-04-01',sortOrder:40,active:true},
  {id:'nsr-kot',code:'KOT',name:'Kitchen order ticket',document:'KOT',prefix:'KOT',start:1,padding:4,reset:'NEVER',activeFrom:'2025-04-01',sortOrder:50,active:true},
  {id:'nsr-reg',code:'REG',name:'Registration card',document:'REG',prefix:'REG',start:1,padding:5,reset:'NEVER',activeFrom:'2025-04-01',sortOrder:60,active:true},
];
/** "2026-27" for any date from 1 April 2026 to 31 March 2027. */
export function fdmFyLabel(date:string):string{
  const y=Number(date.slice(0,4)),m=Number(date.slice(5,7)),from=m>=4?y:y-1;
  return `${from}-${String((from+1)%100).padStart(2,'0')}`;
}
export const fdmFyStart=(date:string)=>fdmFyLabel(date).slice(0,4)+'-04-01';
const yearKey=(row:FDMSeriesRow,date:string)=>row.reset==='NEVER'?'*':fdmFyLabel(date);
/** "2627" for FY 2026-27 — the financial-year part of a number (as the server's invoices module prints it). */
export const fdmFyCode=(date:string)=>{const l=fdmFyLabel(date);return l.slice(2,4)+l.slice(5,7);};
/** Formatted number: PREFIX/2627/0001 (yearly reset) or PREFIX/00001 (never). `legacy` = the old prototype form PREFIX/2026-27/0001
 *  that the seeded tax-invoice and credit-note series keep for FY 2026-27 (numbers already printed stay as they are). */
export function fdmFormat(row:Pick<FDMSeriesRow,'prefix'|'padding'|'reset'>,seq:number,date:string,legacy=false):string{
  const n=String(seq).padStart(Math.max(1,Number(row.padding)||1),'0');
  return row.reset==='NEVER'?`${row.prefix}/${n}`:`${row.prefix}/${legacy?fdmFyLabel(date):fdmFyCode(date)}/${n}`;
}
const isLegacy=(row:Pick<FDMSeriesRow,'id'>,year:string)=>!!LEGACY[row.id]&&year===FDM_LEGACY_FY;
/** Last number issued by a series in a numbering year (legacy counters for the seeded tax-invoice / credit-note series). */
export function fdmLast(s:State,row:Pick<FDMSeriesRow,'id'|'reset'>,year:string):number{
  const legacy=LEGACY[row.id];
  if(legacy&&year===FDM_LEGACY_FY)return Number(s[legacy]??0);
  return s.frontdeskMasterX?.counters?.[row.id+'|'+year]?.last??0;
}
/** Documents issued by a series, all years (legacy counters included). */
export function fdmIssued(s:State,row:Pick<FDMSeriesRow,'id'|'reset'>&{issued?:unknown}):{count:number;years:string[];lastDate?:string}{
  if(typeof row.issued==='number')return {count:row.issued,years:[]}; // production: the server counts the issued documents
  let count=0;const years=new Set<string>();let lastDate:string|undefined;
  const legacy=LEGACY[row.id];if(legacy&&Number(s[legacy]??0)>0){count+=Number(s[legacy]);years.add(FDM_LEGACY_FY);}
  for(const [k,v] of Object.entries(s.frontdeskMasterX?.counters??{})){
    const [id,year]=k.split('|');if(id!==row.id||!v.last)continue;
    if(!(legacy&&year===FDM_LEGACY_FY))count+=v.count??v.last;years.add(year);if(!lastDate||v.lastDate>lastDate)lastDate=v.lastDate;
  }
  return {count,years:[...years].sort(),lastDate};
}
/** The series of a document in force on a date: the active row with the latest activeFrom on or before it. */
export function fdmSeriesFor(s:State,doc:string,date=s.date):FDMSeriesRow|undefined{
  return (mstRows(s,FDM_SERIES,{active:true}) as FDMSeriesRow[]).filter(r=>r.document===doc&&r.activeFrom<=date).sort((a,b)=>a.activeFrom<b.activeFrom?1:-1)[0];
}
/** Next number a document would get on a date (nothing is reserved). */
export function fdmPreview(s:State,doc:string,date=s.date):string|undefined{
  const r=fdmSeriesFor(s,doc,date);if(!r)return undefined;
  if(typeof r.nextNumber==='string'&&date===s.date)return r.nextNumber; // production: the server's next number
  const year=yearKey(r,date),last=fdmLast(s,r,year);return fdmFormat(r,Math.max(last,r.start-1)+1,date,isLegacy(r,year));
}
/** Allocate the next number of a document (inside a command, on the draft state): consecutive per series and numbering year, never
 *  re-used; the seeded INV / CRN series continue s.invoiceSeqC / s.creditNoteSeqC in FY 2026-27. Refuses when no series is in force. */
export function fdmAllocate(s:State,doc:FDMDoc,date=s.date):string{
  const r=fdmSeriesFor(s,doc,date);
  if(!r)throw new HccError('INVALID_TRANSITION',`No numbering series is in force for ${fdmDocLabel(doc)} on ${date}. The Owner adds one in Master configuration › Front-desk policies › Numbering series.`);
  const year=yearKey(r,date),seq=Math.max(fdmLast(s,r,year),r.start-1)+1,legacy=LEGACY[r.id];
  if(legacy&&year===FDM_LEGACY_FY)s[legacy]=seq;
  else{const c=fdmSlice(s).counters,k=r.id+'|'+year;c[k]={last:seq,lastDate:date,count:(c[k]?.count??0)+1};}
  return fdmFormat(r,seq,date,isLegacy(r,year));
}
/** Documents of a type already issued in a numbering year / last document date of the type (every series of it). */
function docActivity(s:State,doc:string,except?:string){
  const rows=(mstRows(s,FDM_SERIES) as FDMSeriesRow[]).filter(r=>r.document===doc&&r.id!==except);
  const years=new Set<string>();let lastDate:string|undefined;let any=false;
  for(const r of rows){const i=fdmIssued(s,r);if(!i.count)continue;any=true;i.years.forEach(y=>years.add(y));
    const d=i.lastDate??(LEGACY[r.id]&&Number(s[LEGACY[r.id]]??0)>0||!i.years.length?s.date:undefined);if(d&&(!lastDate||d>lastDate))lastDate=d;}
  return {years,lastDate,any};
}
const NUMBERING_KEYS=['document','prefix','start','padding','reset','activeFrom'] as const;
const seriesUsage=(s:State,row:FDMSeriesRow):MasterUsage=>{
  const i=fdmIssued(s,row),inForce=row.active&&fdmSeriesFor(s,row.document)?.id===row.id;
  const where=[...(i.count?[`${i.count} ${fdmDocLabel(row.document).toLowerCase()} number${i.count===1?'':'s'} issued (${i.years.map(y=>y==='*'?'no reset':'FY '+y).join(', ')})`]:[]),
    ...(inForce?[`In force today · next ${fdmPreview(s,row.document)}`]:[])];
  return {count:i.count+(inForce?1:0),where};
};
function seriesProblem(s:State,row:FDMSeriesRow,rows:FDMSeriesRow[]):string|undefined{
  if(!FDM_DOCS.some(d=>d.value===row.document))return 'Choose the document.';
  if(!/^[A-Z0-9-]{1,8}$/.test(String(row.prefix??'')))return 'Prefix: 1–8 capital letters, digits or “-”.';
  if(!Number.isInteger(row.start)||row.start<1||row.start>9_999_999)return 'Start: a whole number from 1 to 9,999,999.';
  if(!Number.isInteger(row.padding)||row.padding<1||row.padding>10)return 'Digits: a whole number from 1 to 10.';
  if(row.reset!=='FY'&&row.reset!=='NEVER')return 'Choose when the numbers restart.';
  if(!ISO.test(String(row.activeFrom??''))||Number.isNaN(Date.parse(row.activeFrom)))return 'Active from: a date (YYYY-MM-DD).';
  const gst=fdmDocIsGst(row.document);
  if(gst&&row.reset!=='FY')return `${fdmDocLabel(row.document)}: GST documents restart every financial year (the number carries the FY) — CA to confirm.`;
  const old=rows.find(r=>r.id===row.id);
  const changed=!old||NUMBERING_KEYS.some(k=>String(old[k])!==String(row[k]));
  if(!changed)return undefined;
  if(old){const i=fdmIssued(s,old);if(i.count)return `${old.code} has issued ${i.count} number${i.count===1?'':'s'} — document, prefix, start, digits, reset and active-from cannot change (sequential, no gaps). Add a new series from a date with no documents instead.`;}
  if(gst){const sample=fdmFormat(row,Math.max(row.start,1),row.activeFrom);if(sample.length>FDM_NUMBER_MAX)return `The number would be ${sample.length} characters (${sample}); GST allows at most ${FDM_NUMBER_MAX}. Shorten the prefix or the digits.`;}
  if(!old||old.activeFrom!==row.activeFrom){
    if(row.activeFrom<s.date)return `Active from: ${s.date} (today's business date) or later.`;
    const a=docActivity(s,row.document,row.id);
    if(a.lastDate&&row.activeFrom<=a.lastDate)return `Active from: after ${a.lastDate}, the last ${fdmDocLabel(row.document).toLowerCase()} date — a series starts on a date with no documents.`;
    if(row.reset==='FY'&&a.years.has(fdmFyLabel(row.activeFrom)))return `FY ${fdmFyLabel(row.activeFrom)} already has ${fdmDocLabel(row.document).toLowerCase()} numbers — the new series can start from ${Number(fdmFyLabel(row.activeFrom).slice(0,4))+1}-04-01 (next financial year).`;
  }
  const clash=rows.find(r=>r.id!==row.id&&r.active&&r.document===row.document&&r.activeFrom===row.activeFrom);
  if(clash)return `${clash.code} already starts ${fdmDocLabel(row.document).toLowerCase()} numbers on ${row.activeFrom} (one series at a time — DEC-MA-5).`;
  return undefined;
}
export const FDM_SERIES_SPEC:MasterSpec<FDMSeriesRow>={
  id:FDM_SERIES,label:'Numbering series',singular:'numbering series',group:'frontdesk',prefix:'NSR',money:true,permission:'settings',
  help:'One series per document (one tax-invoice series for the hotel, DEC-MA-5). '+FDM_GST_NOTE+' A series that has issued numbers cannot change; add a new one from a date with no documents.',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'INV-2027'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Tax invoice'},
    {key:'document',label:'Document',type:'select',required:true,options:FDM_DOCS.map(d=>({value:d.value,label:d.label}))},
    {key:'prefix',label:'Prefix',type:'text',required:true,maxLength:8,placeholder:'INV',help:'Capital letters, digits or “-”, e.g. INV. The number reads PREFIX/2627/0001 (yearly reset; 2627 = FY 2026-27) or PREFIX/00001.'},
    {key:'start',label:'First number',type:'number',required:true,min:1,max:9_999_999,help:'Used at the start of the series and after every yearly reset.'},
    {key:'padding',label:'Digits',type:'number',required:true,min:1,max:10,help:'Zero-padded width of the running number.'},
    {key:'reset',label:'Restart numbers',type:'select',required:true,options:FDM_RESETS,help:'GST documents restart every financial year.'},
    {key:'activeFrom',label:'Active from',type:'text',required:true,maxLength:10,placeholder:'2027-04-01',help:'YYYY-MM-DD: a date with no documents of this kind yet (normally 1 April).'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'active',label:'Active',type:'toggle'},
  ],
  inUse:seriesUsage,
  validate:(s,row,rows)=>seriesProblem(s,row,rows),
  canRetire:(s,row)=>fdmSeriesFor(s,row.document)?.id===row.id?`${row.code} is the ${fdmDocLabel(row.document).toLowerCase()} series in force today — add the next series (from a date with no documents) before retiring it.`:undefined,
  seed:()=>FDM_SERIES_SEED.map(r=>({...r})),
};

// ═════════════════════════════ M14 · Cancellation / no-show / deposit policies ═════════════════════════════
export type FDMPolicyRow=MasterRow&{freeCancelHours:number;cancelChargeBps:number;noShowChargeBps:number;depositBps:number;sources:string[]};
/** "Default" = today's global behaviour: free cancellation up to arrival, no cancellation or no-show charge, no advance required
 *  (the refundable check-in deposit and the no-show cut-off stay in Front-desk policies settings). Applies to every source. */
export const FDM_POLICY_SEED:FDMPolicyRow[]=[{id:'pol-default',code:'DEFAULT',name:'Default',freeCancelHours:0,cancelChargeBps:0,noShowChargeBps:0,depositBps:0,sources:[],sortOrder:10,active:true}];
/** Policy of a booking: the active policy naming its source row, else the active policy that names no source (Default). */
export function fdmPolicyFor(s:State,b:Pick<Booking,'source'|'otaChannel'|'company'>):FDMPolicyRow|undefined{
  const pols=mstRows(s,FDM_POLICIES,{active:true}) as FDMPolicyRow[];const code=fdmSourceOf(s,b)?.code;
  return (code?pols.find(p=>(p.sources??[]).includes(code)):undefined)??pols.find(p=>!(p.sources??[]).length);
}
/** Hours from `clock` (YYYY-MM-DDTHH:mm) to the booking's arrival at the check-in time. */
export function fdmHoursToArrival(s:State,b:Pick<Booking,'arrival'>,clock=s.clock):number{
  const t=/^\d{2}:\d{2}$/.test(s.settings?.checkin??'')?s.settings.checkin:'12:00';
  return Math.floor((Date.parse(b.arrival+'T'+t+':00Z')-Date.parse(clock+':00Z'))/3600000);
}
/** Cancellation charge (paise, first night × charge %) if the booking were cancelled at `clock`; 0 inside the free window. */
export function fdmCancelCharge(s:State,b:Booking,clock=s.clock):{amount:number;policy?:FDMPolicyRow;free:boolean;hours:number}{
  const p=fdmPolicyFor(s,b),hours=fdmHoursToArrival(s,b,clock);
  if(!p)return {amount:0,free:true,hours};
  const free=hours>=Number(p.freeCancelHours||0);
  return {amount:free?0:Math.round(b.rate*Number(p.cancelChargeBps||0)/10000),policy:p,free,hours};
}
/** No-show charge (paise): first night × no-show %. */
export const fdmNoShowCharge=(s:State,b:Booking)=>{const p=fdmPolicyFor(s,b);return p?Math.round(b.rate*Number(p.noShowChargeBps||0)/10000):0;};
/** Advance due to confirm (paise): room charges × deposit %. */
export const fdmAdvanceDue=(s:State,b:Booking)=>{const p=fdmPolicyFor(s,b);return p?Math.round(b.rate*Math.max(0,nights(b.arrival,b.departure))*Number(p.depositBps||0)/10000):0;};
const pct=(bps:number)=>(bps/100).toLocaleString('en-IN',{maximumFractionDigits:2})+' %';
/** One line for confirmations and the booking detail. */
export function fdmPolicyText(p:FDMPolicyRow|undefined):string{
  if(!p)return 'No cancellation policy applies.';
  const cancel=Number(p.cancelChargeBps)?`${Number(p.freeCancelHours)?`free cancellation until ${p.freeCancelHours} h before arrival, then`:'cancellation charge'} ${pct(p.cancelChargeBps)} of the first night`:'free cancellation';
  return `${p.name}: ${cancel} · no-show ${Number(p.noShowChargeBps)?pct(p.noShowChargeBps)+' of the first night':'no charge'} · advance ${Number(p.depositBps)?pct(p.depositBps)+' of room charges':'not required'}.`;
}
export const FDM_POLICY_SPEC:MasterSpec<FDMPolicyRow>={
  id:FDM_POLICIES,label:'Booking policies',singular:'policy',group:'frontdesk',prefix:'POL',money:true,permission:'settings',
  help:'Named cancellation, no-show and advance-deposit terms. A policy applies to the sources it names; the policy that names none (Default) applies to every other booking. Charges are shown, not posted automatically.',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'OTA-STD'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'OTA standard'},
    {key:'freeCancelHours',label:'Free cancellation until (hours before arrival)',type:'number',required:true,min:0,max:720,help:'0 = free until the arrival time.'},
    {key:'cancelChargeBps',label:'Cancellation charge (of the first night)',type:'percent',min:0,max:10000,money:true},
    {key:'noShowChargeBps',label:'No-show charge (of the first night)',type:'percent',min:0,max:10000,money:true},
    {key:'depositBps',label:'Advance to confirm (of room charges)',type:'percent',min:0,max:10000,money:true},
    {key:'sources',label:'Applies to booking sources',type:'multiselect',master:FDM_SOURCES,help:'None = the default policy for every other source.'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'active',label:'Active',type:'toggle'},
  ],
  inUse:(s,row)=>{const n=s.bookings.filter(b=>fdmPolicyFor(s,b)?.id===row.id).length;return {count:n,where:n?[`Bookings ${n}`]:[]};},
  validate:(s,row,rows)=>{
    const src=Array.isArray(row.sources)?row.sources:[];
    if(!src.length&&row.active!==false){const d=rows.find(r=>r.id!==row.id&&r.active&&!(r.sources??[]).length);if(d)return `${d.name} is already the default policy (names no source) — choose the sources this one applies to.`;}
    for(const c of src){const o=rows.find(r=>r.id!==row.id&&r.active&&(r.sources??[]).includes(c));if(o)return `${mstByCode(s,FDM_SOURCES,c)?.name??c} already follows ${o.name}.`;}
    return undefined;
  },
  canRetire:(_s,row)=>!(row.sources??[]).length?`${row.name} is the default policy — bookings from unnamed sources need it.`:undefined,
  seed:()=>FDM_POLICY_SEED.map(r=>({...r,sources:[]})),
};

// ───────────────────────────── Registration (Master configuration › Front-desk policies › Lists) ─────────────────────────────
registerMaster(FDM_SOURCE_SPEC);
registerMaster(FDM_ID_SPEC);
registerMaster(FDM_SERIES_SPEC);
registerMaster(FDM_POLICY_SPEC);
/** The four MA-5 specs (tests and the sync layer). */
export const fdmSpecs=()=>[FDM_SOURCES,FDM_ID_TYPES,FDM_SERIES,FDM_POLICIES].map(id=>mstSpec(id)!);

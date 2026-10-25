// Stream D · Housekeeping & rooms (owns TASK, BLOCK, UNBLOCK, REPAIR_REQUEST, STAYOVER_GENERATE, BACKUP_SIMULATE)
// — pure domain helpers and selectors (re-exported by services/mock/index.ts for UI and tests). Names are prefixed `hk`.
import type {State,Room,Task,Booking} from '../types';
import {addDays} from '../money';
import {blockedDuring} from '../selectors';

/** Default assignee for repair tasks (Maintenance demo user). */
export const HK_REPAIR_ASSIGNEE='maint';
export type HkTaskKind=NonNullable<Task['kind']>;
export const hkKindLabel:Record<HkTaskKind,string>={checkout:'Checkout clean',stayover:'Stayover service',repair:'Repair',move:'Room-move clean'};
/** Task kind; M1 tasks without a kind are checkout cleans. */
export const hkTaskKind=(t:Task):HkTaskKind=>t.kind??'checkout';
/** Stayover tasks: id is stable per room and business date (idempotent generation). */
export const hkStayoverId=(room:string,date:string)=>`stayover:${room}:${date}`;

/** Bookings that hold the room's inventory: Confirmed, In house, and Held while the hold has not expired. */
export function hkHoldsInventory(s:State,b:Booking){
  if(b.status==='Confirmed'||b.status==='In house')return true;
  if(b.status==='Held'){const until=(b as Booking&{holdUntil?:string}).holdUntil;return !until||until>s.clock;}
  return false;
}
/** Active/future allocations of `room` overlapping nights [from, to). `to` undefined = open-ended. */
export function hkAllocationsOverlapping(s:State,room:string,from:string,to?:string){
  return s.bookings.filter(b=>b.room===room&&hkHoldsInventory(s,b)&&(!to||b.arrival<to)&&b.departure>from);
}
/** Is the room out of service on business date `date` (the night of `date`)? */
export const hkBlockedOn=(room:Room,date:string)=>blockedDuring(room,date,addDays(date,1));
/** A dated block whose end has passed: no current or future block remains. */
export const hkBlockExpired=(room:Room,date:string)=>room.blocked&&!!room.blockTo&&room.blockTo<=date;
/** Clears blocks that ended before `s.date` (mutates s). For the integrator: call from NEXT_DAY so `room.blocked`
 *  stays true only while a current or future block exists. Returns the room IDs released. */
export function hkExpireBlocks(s:State):string[]{
  const out:string[]=[];
  for(const r of s.rooms)if(hkBlockExpired(r,s.date)){r.blocked=false;r.blockReason=undefined;r.blockFrom=undefined;r.blockTo=undefined;out.push(r.id);}
  return out;
}
/** Human label for a room's block, e.g. "Repair · 25 Oct → 28 Oct" (ISO dates; UI formats). */
export function hkBlockSummary(room:Room){
  if(!room.blocked)return '';
  const range=room.blockFrom||room.blockTo?`${room.blockFrom??'now'} → ${room.blockTo??'open-ended'}`:'until unblocked';
  return `${room.blockReason||'Out of service'} · ${range}`;
}
/** Confirmed booking arriving today in the room → priority "Arrival today". */
export const hkArrivalToday=(s:State,room:string)=>s.bookings.find(b=>b.room===room&&b.status==='Confirmed'&&b.arrival===s.date);
/** Current (in-house) and next (confirmed, earliest arrival) guest — names and dates only, no financial data. */
export function hkGuests(s:State,room:string){
  const current=s.bookings.find(b=>b.room===room&&b.status==='In house');
  const next=s.bookings.filter(b=>b.room===room&&b.status==='Confirmed').sort((a,b)=>a.arrival.localeCompare(b.arrival))[0];
  const view=(b?:Booking)=>b&&{id:b.id,guest:b.guest,people:b.people,arrival:b.arrival,departure:b.departure};
  return {current:view(current),next:view(next)};
}
/** Rooms that need a stayover service on the business date: in-house and not departing today. */
export const hkStayoverRooms=(s:State)=>s.bookings.filter(b=>b.status==='In house'&&b.arrival<=s.date&&b.departure>s.date).map(b=>b.room).sort();
/** Task ordering for "my tasks": active work first, then to do, awaiting inspection, done. */
const order:Record<Task['status'],number>={'In progress':0,'To do':1,'Inspection':2,'Done':3};
export function hkSortTasks(s:State,tasks:Task[]){
  return [...tasks].sort((a,b)=>order[a.status]-order[b.status]||Number(!hkArrivalToday(s,a.room))-Number(!hkArrivalToday(s,b.room))||a.room.localeCompare(b.room));
}

// ── Backup simulation (SCR-19, REQ-BKP-001). Nothing is copied anywhere; all values derive from s.clock / audit. ──
export type HkBackupRow={at:string;kind:'Scheduled'|'Manual';result:'Succeeded'|'Failed';note:string};
export const HK_BACKUP_DESTINATION='Office NAS · \\\\HCC-NAS\\backups (label only) + off-site encrypted copy (production scope)';
export const HK_BACKUP_RETENTION='Daily copies kept 14 days · weekly copies 8 weeks · monthly copies 12 months (proposed policy)';
export function hkBackupHistory(s:State):HkBackupRow[]{
  const manual=s.audit.filter(a=>a.action==='BACKUP SIMULATE').map(a=>({at:a.at,kind:'Manual' as const,result:'Succeeded' as const,note:'Simulated run requested on screen — no data copied'}));
  const nightly=(d:string)=>d+'T02:00';
  const scheduled:HkBackupRow[]=[
    ...(s.clock>=nightly(s.date)?[{at:nightly(s.date),kind:'Scheduled' as const,result:'Succeeded' as const,note:'Nightly backup after day close (simulated)'}]:[]),
    {at:nightly(addDays(s.date,-1)),kind:'Scheduled',result:'Failed',note:'Destination unreachable — NAS offline (simulated example); retried next night'},
    {at:nightly(addDays(s.date,-2)),kind:'Scheduled',result:'Succeeded',note:'Nightly backup after day close (simulated)'},
  ];
  return [...manual,...scheduled].sort((a,b)=>b.at.localeCompare(a.at));
}
export function hkLastBackup(s:State){return hkBackupHistory(s).find(r=>r.result==='Succeeded');}
/** DEC-063 G9: "last backup" status for the Overview warning card (Owner and Manager; the Manager has no Backup page).
 * warn = the latest run failed, or the last good backup is older than 26 simulated hours (a missed nightly run). */
export function hkBackupStatus(s:State){
  const rows=hkBackupHistory(s),last=hkLastBackup(s),latest=rows[0];
  const hoursAgo=last?Math.max(0,Math.floor((Date.parse(s.clock+':00Z')-Date.parse(last.at+':00Z'))/3600000)):undefined;
  const failed=latest?.result==='Failed',stale=hoursAgo===undefined||hoursAgo>26;
  const recentFailures=rows.filter(r=>r.result==='Failed').length;
  return {last,latest,hoursAgo,failed,stale,warn:failed||stale,recentFailures};
}
export const HK_RESTORE_STEPS=[
  'Confirm with the Owner that a restore is required and record the reason; stop all front-desk activity.',
  'Close or note the current business day and cash shift so the last known figures are on paper.',
  'Choose the most recent successful backup from the list (never one marked Failed).',
  'Restore it into a separate test environment first and open the closing report for that date.',
  'Compare drawer cash, open folios and room status with the paper figures; resolve any difference.',
  'Only after the Owner signs off, switch the live system to the restored copy and record the restore in the audit trail.',
  'Re-enter any transactions made after the backup time from paper records, each with a reference.',
];

// ── Roadmap (PRD §3, areas 13–26). Read-only. ──
export const HK_ROADMAP:{no:string;area:string;phase:string;disposition:string;current:string}[]=[
  {no:'13',area:'Advanced tariff / yield',phase:'Phase 2',disposition:'Prototype screen: Tariff & yield (rate rules, yield suggestions)',current:'Built in prototype (simulated integrations where external)'},
  {no:'14',area:'Group / corporate',phase:'Phase 2',disposition:'Prototype screen: Groups & corporate (blocks, company billing)',current:'Built in prototype (simulated integrations where external)'},
  {no:'15',area:'Maintenance',phase:'Phase 2',disposition:'Prototype screen: Maintenance (repair requests, room blocks)',current:'Built in prototype (simulated integrations where external)'},
  {no:'16',area:'Linen / laundry / stock',phase:'Phase 2',disposition:'Prototype screen: Linen & stock (par levels, laundry, issues)',current:'Built in prototype (simulated integrations where external)'},
  {no:'17',area:'HR / payroll',phase:'Phase 2',disposition:'Prototype screen: Staff & payroll (fictional staff, simulated payroll)',current:'Built in prototype (simulated integrations where external)'},
  {no:'18',area:'Guest services / complaints / lost & found',phase:'Phase 2',disposition:'Prototype screen: Guest services (requests, complaints, lost & found)',current:'Built in prototype (simulated integrations where external)'},
  {no:'19',area:'Checklists / shift handover',phase:'Phase 2',disposition:'Prototype screen: Checklists & handover',current:'Built in prototype (simulated integrations where external)'},
  {no:'20',area:'Accounting automation',phase:'Phase 2',disposition:'Prototype screen: Accounting (journal export from source IDs, simulated)',current:'Built in prototype (simulated integrations where external)'},
  {no:'21',area:'OTA channel management',phase:'Phase 3',disposition:'Prototype screen: OTA channels — simulated channel sync, no OTA connection',current:'Built in prototype (simulated integrations where external)'},
  {no:'22',area:'Direct booking website',phase:'Phase 3',disposition:'Prototype screen: Direct booking site — simulated website, no public site',current:'Built in prototype (simulated integrations where external)'},
  {no:'23',area:'Restaurant POS / room service',phase:'Phase 3',disposition:'Prototype screen: Restaurant & room service (orders post to folio); no POS hardware',current:'Built in prototype (simulated integrations where external)'},
  {no:'24',area:'WhatsApp / notifications',phase:'Phase 3',disposition:'Prototype screen: Messages — simulated WhatsApp / SMS / email, nothing is sent',current:'Built in prototype (simulated integrations where external)'},
  {no:'25',area:'OCR / document capture',phase:'Phase 3',disposition:'Prototype screen: Document capture — simulated OCR on fictional documents',current:'Built in prototype (simulated integrations where external)'},
  {no:'26',area:'AI management assistant',phase:'Phase 3',disposition:'Prototype screen: Management assistant — simulated AI over prototype data',current:'Built in prototype (simulated integrations where external)'},
];

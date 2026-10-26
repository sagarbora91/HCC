// MA-7 · Housekeeping masters (plan Part 2 §8 M23 and M24): task types with standard minutes / inspection / checklist, and zones
// (a set of floors or rooms with a default attendant). Registered in the masters framework (domain/streams/masters.ts) in the group
// "Housekeeping and maintenance" (hkm); changed through the generic MST_MASTER_* commands (Owner + Tech admin, DEC-MA-1). No money fields.
//
// Task types feed the housekeeping board: the checklist of a task, whether a finished clean waits for inspection, the standard minutes
// behind "attendant load" and the timing on each card. Zones feed assignment (stayover generation, the unassigned-task hint) and the
// "My tasks" grouping. Seeds reproduce today's behaviour exactly (3-item checklist, departure cleans inspected, stayovers not, one zone
// per floor with the same round-robin attendant), so no fixture figure moves.
import type {State,Task} from '../types';
import {mstRows,mstFloorCode,mstFloorLabel,registerMaster,type MasterRow,type MasterSpec} from './masters';
import {mastersSeed,seedHkZones,SEED_HK_CHECKLIST} from '../../fixtures/ext/masters-seed';
import {hkTaskKind,type HkTaskKind} from './housekeeping';
import {cfgValueOn} from './config';

export const HKT='hkTaskTypes';
export const HKZ='hkZones';
export const HKT_DEFAULT_CHECKLIST=SEED_HK_CHECKLIST;
export const HKT_MAX_ITEMS=8;
export const HKT_ITEM_MAX=60;
/** What a task type is for. checkout/move/stayover are the task kinds; inspection = the supervisor's check of a finished clean; deep = catalogue only. */
export const HKT_APPLIES=[
  {value:'checkout',label:'Departure clean (checkout task)'},
  {value:'move',label:'Room-move clean'},
  {value:'stayover',label:'Stayover service'},
  {value:'inspection',label:'Supervisor inspection of a finished clean'},
  {value:'deep',label:'Deep clean (listed; no tasks generated yet)'},
];
export type HktApplies='checkout'|'move'|'stayover'|'inspection'|'deep';
export type HktRow=MasterRow&{appliesTo:string[];standardMinutes:number;needsInspection:boolean;checklist:string};
export type HkzRow=MasterRow&{floors:string[];rooms:string[];defaultAttendant?:string};

/** Built-in values used when no row of the master covers a kind (old saves, every row retired). */
export const HKT_FALLBACK:Record<'checkout'|'move'|'stayover'|'inspection',{minutes:number;needsInspection:boolean}>={
  checkout:{minutes:40,needsInspection:true},move:{minutes:40,needsInspection:true},stayover:{minutes:20,needsInspection:false},inspection:{minutes:10,needsInspection:false}};

const minutesBetween=(a:string,b:string)=>Math.round((Date.parse(b+':00Z')-Date.parse(a+':00Z'))/60000);
const first=(name?:string)=>name?.split(' · ')[0]??'';

// ───────────────────────────── Task types ─────────────────────────────
/** Checklist lines of a task-type row (one item per line, blank lines dropped). */
export const hktChecklistItems=(text:unknown):string[]=>String(text??'').split(/\r?\n/).map(x=>x.trim()).filter(Boolean);
const hktRows=(s:State):HktRow[]=>mstRows(s,HKT) as HktRow[];
/** The task type that covers a kind: the first active row (sort order) applying to it, else a retired one, else undefined. */
export function hkTypeFor(s:State,kind:string):HktRow|undefined{
  const rows=hktRows(s).filter(r=>(r.appliesTo??[]).includes(kind));
  return rows.find(r=>r.active)??rows[0];
}
/** Task type of a task (repair tasks have none). */
export const hkTypeOfTask=(s:State,t:Pick<Task,'kind'>):HktRow|undefined=>{const k=hkTaskKind(t as Task);return k==='repair'?undefined:hkTypeFor(s,k);};
/** Checklist of a task: its type's items, else the built-in three. Tasks hold one tick per item (Task.checked). */
export function hkChecklistOf(s:State,t:Pick<Task,'kind'>):string[]{
  const items=hktChecklistItems(hkTypeOfTask(s,t)?.checklist);return items.length?items:HKT_DEFAULT_CHECKLIST;
}
/** Does a finished clean of this task wait for the supervisor? (Also needs the global setting "Supervisor inspection after cleaning".) Only departure/move cleans drive room readiness. */
export function hkNeedsInspection(s:State,t:Pick<Task,'kind'>):boolean{
  const k=hkTaskKind(t as Task);if(k==='stayover'||k==='repair')return false;
  const ty=hkTypeFor(s,k);return ty?ty.needsInspection:HKT_FALLBACK[k].needsInspection;
}
/** Standard minutes of a task (undefined for repairs). */
export function hkStdMinutes(s:State,t:Pick<Task,'kind'>):number|undefined{
  const k=hkTaskKind(t as Task);if(k==='repair')return undefined;
  return hkTypeFor(s,k)?.standardMinutes??HKT_FALLBACK[k].minutes;
}
export const hkInspectionMinutes=(s:State)=>hkTypeFor(s,'inspection')?.standardMinutes??HKT_FALLBACK.inspection.minutes;

export type HkTiming={std:number;state:'waiting'|'running'|'over'|'finished';elapsed?:number;over?:number;actual?:number;label:string};
/** Board timing of a task against its standard: running (elapsed of std), over (by n min), finished (took n min). */
export function hkTaskTiming(s:State,t:Task):HkTiming|undefined{
  const std=hkStdMinutes(s,t);if(std===undefined)return undefined;
  if(t.status==='In progress'&&t.startedAt){const elapsed=Math.max(0,minutesBetween(t.startedAt,s.clock));
    return elapsed>std?{std,state:'over',elapsed,over:elapsed-std,label:`Over standard by ${elapsed-std} min (${elapsed} of ${std})`}:{std,state:'running',elapsed,label:`${elapsed} of ${std} min`};}
  if((t.status==='Inspection'||t.status==='Done')&&t.startedAt&&t.completedAt){const actual=Math.max(0,minutesBetween(t.startedAt,t.completedAt));
    return {std,state:'finished',actual,over:Math.max(0,actual-std),label:`Took ${actual} min (standard ${std})`};}
  return {std,state:'waiting',label:`Standard ${std} min`};
}
export type HkLoadRow={user:string;name:string;rooms:number;minutes:number;running:number;guide:number;over:boolean};
export const hkRoomsGuide=(s:State)=>{try{const v=Number(cfgValueOn(s,'hk.maxRoomsPerAttendant'));return Number.isFinite(v)&&v>0?v:12;}catch{return 12;}};
/** Attendant load: rooms still to clean (To do + In progress, repairs excluded) and the standard minutes behind them, per enabled housekeeper. */
export function hkAttendantLoad(s:State):HkLoadRow[]{
  const guide=hkRoomsGuide(s);
  return s.users.filter(u=>u.enabled&&u.role==='Housekeeper').sort((a,b)=>a.id<b.id?-1:1).map(u=>{
    const mine=s.tasks.filter(t=>t.assignee===u.id&&hkTaskKind(t)!=='repair'&&(t.status==='To do'||t.status==='In progress'));
    const minutes=mine.reduce((n,t)=>n+(hkStdMinutes(s,t)??0),0);
    return {user:u.id,name:first(u.name),rooms:mine.length,minutes,running:mine.filter(t=>t.status==='In progress').length,guide,over:mine.length>guide};});
}
/** Supervisor inspection queue: rooms awaiting inspection and the standard minutes behind them. */
export const hkInspectionQueue=(s:State)=>{const n=s.tasks.filter(t=>t.status==='Inspection').length;return {rooms:n,minutes:n*hkInspectionMinutes(s)};};
export const hkFmtMinutes=(m:number)=>m<60?`${m} min`:`${Math.floor(m/60)} h${m%60?` ${m%60} min`:''}`;

export const HKT_SPEC:MasterSpec<HktRow>={
  id:HKT,label:'Housekeeping task types',singular:'task type',group:'hkm',prefix:'HKT',money:false,permission:'masters',
  help:'Standard minutes, inspection and checklist of each kind of housekeeping task. The board uses them for attendant load and timing.',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'DEP',help:'Short code, e.g. DEP, STAY, DEEP. Cannot change once tasks use it.'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Departure clean'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'appliesTo',label:'Used for',type:'multiselect',required:true,options:HKT_APPLIES,help:'Each kind of task belongs to one active type. Tasks of a kind without a type use the built-in values.'},
    {key:'standardMinutes',label:'Standard minutes',type:'number',required:true,min:5,max:240,help:'Time one room should take. Drives attendant load and the on-time / over-standard tag.'},
    {key:'needsInspection',label:'Needs inspection',type:'toggle',help:'A finished clean waits for the supervisor. Applies only if “Supervisor inspection after cleaning” is on in Master configuration. Stayover services are never inspected.'},
    {key:'checklist',label:'Checklist items',type:'text',multiline:true,required:true,maxLength:HKT_MAX_ITEMS*(HKT_ITEM_MAX+1),placeholder:'Linen & bathroom\nSurfaces & amenities\nFinal room check',help:`One item per line, 1–${HKT_MAX_ITEMS} items of up to ${HKT_ITEM_MAX} characters. The attendant ticks every item to finish the task.`},
    {key:'active',label:'Active',type:'toggle'},
  ],
  inUse:(s,row)=>{
    const kinds=row.appliesTo??[];
    const n=s.tasks.filter(t=>{const k=hkTaskKind(t);return (k!=='repair'&&kinds.includes(k))||(kinds.includes('inspection')&&t.status==='Inspection');}).length;
    return {count:n,where:n?[`${n} housekeeping task${n===1?'':'s'}`]:[]};},
  validate:(_s,row,rows)=>{
    const items=hktChecklistItems(row.checklist);
    if(items.length<1||items.length>HKT_MAX_ITEMS)return `Checklist: 1–${HKT_MAX_ITEMS} items, one per line.`;
    const long=items.find(x=>x.length>HKT_ITEM_MAX);if(long)return `Checklist item “${long.slice(0,20)}…” is longer than ${HKT_ITEM_MAX} characters.`;
    if(new Set(items.map(x=>x.toLowerCase())).size!==items.length)return 'Checklist: each item once.';
    if((row.appliesTo??[]).includes('stayover')&&row.needsInspection)return 'A stayover service is never inspected (the room stays occupied and its readiness does not change) — switch “Needs inspection” off.';
    for(const k of row.appliesTo??[]){const other=rows.find(r=>r.id!==row.id&&r.active&&(r.appliesTo??[]).includes(k));
      if(other&&row.active)return `${HKT_APPLIES.find(a=>a.value===k)?.label.split(' (')[0]??k} is already handled by “${other.name}” — each kind belongs to one active type.`;}
    return undefined;},
  seed:s=>mastersSeed(HKT,s) as HktRow[],
};

// ───────────────────────────── Zones ─────────────────────────────
const zrows=(s:State,active?:boolean)=>mstRows(s,HKZ,active===undefined?{}:{active}) as HkzRow[];
/** Enabled housekeepers (sorted by id) — the pool of default attendants. */
export const hkHousekeepers=(s:State)=>s.users.filter(u=>u.enabled&&u.role==='Housekeeper').sort((a,b)=>a.id<b.id?-1:1);
/** Zone of a room among the active zones: a zone that lists the room itself wins over one that lists its floor; lower sort order first. */
export function hkzOf(s:State,roomId:string):HkzRow|undefined{
  const room=s.rooms.find(r=>r.id===roomId);if(!room)return undefined;
  const act=zrows(s,true);
  return act.find(z=>(z.rooms??[]).includes(roomId))??act.find(z=>(z.floors??[]).includes(mstFloorCode(room.floor)));
}
/** Default attendant of the room's zone, when that user is still an enabled housekeeper. */
export function hkzAttendant(s:State,roomId:string):string|undefined{
  const a=hkzOf(s,roomId)?.defaultAttendant;return a&&hkHousekeepers(s).some(u=>u.id===a)?a:undefined;
}
/** Rooms a zone covers (after the rule above). */
export const hkzRooms=(s:State,zone:Pick<HkzRow,'id'>)=>s.rooms.filter(r=>hkzOf(s,r.id)?.id===zone.id);
export const hkzLabel=(s:State,roomId:string)=>hkzOf(s,roomId)?.name;
/** Tasks grouped by zone (zone order, "No zone" last) for "My tasks" and the board filters. */
export function hkzGroups<T extends Pick<Task,'room'>>(s:State,tasks:T[]):{zone?:HkzRow;label:string;tasks:T[]}[]{
  const out:{zone?:HkzRow;label:string;tasks:T[]}[]=[];
  for(const z of zrows(s,true)){const ts=tasks.filter(t=>hkzOf(s,t.room)?.id===z.id);if(ts.length)out.push({zone:z,label:z.name,tasks:ts});}
  const rest=tasks.filter(t=>!hkzOf(s,t.room));if(rest.length)out.push({label:'No zone',tasks:rest});
  return out;
}
export const hkzAttendantOptions=(s:State)=>hkHousekeepers(s).map(u=>({value:u.id,label:first(u.name)}));
export const hkzRoomOptions=(s:State)=>s.rooms.map(r=>({value:r.id,label:`Room ${r.id} · ${mstFloorLabel(s,r)}`}));
/** One zone per floor, named from the floors master (rows: fixtures/ext/masters-seed.ts). */
export const hkzSeed=(s:State):HkzRow[]=>seedHkZones(s,n=>mstFloorLabel(s,n));
export const HKZ_SPEC:MasterSpec<HkzRow>={
  id:HKZ,label:'Housekeeping zones',singular:'zone',group:'hkm',prefix:'HKZ',money:false,permission:'masters',
  help:'A zone is a set of floors and/or rooms with a default attendant. New stayover tasks go to the zone attendant; the board and “My tasks” group rooms by zone.',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'Z1'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Ground floor section'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'floors',label:'Floors',type:'multiselect',master:'floors',help:'Every room on these floors, unless a room is listed in another zone.'},
    {key:'rooms',label:'Extra rooms',type:'multiselect',optionsFrom:hkzRoomOptions,help:'Single rooms (they win over a floor listed in another zone).'},
    {key:'defaultAttendant',label:'Default attendant',type:'select',optionsFrom:hkzAttendantOptions,help:'Gets new stayover tasks in this zone; a supervisor can still reassign any task.'},
    {key:'active',label:'Active',type:'toggle'},
  ],
  inUse:(s,row)=>{const rooms=new Set(s.rooms.filter(r=>(row.rooms??[]).includes(r.id)||(row.floors??[]).includes(mstFloorCode(r.floor))).map(r=>r.id));
    const n=s.tasks.filter(t=>t.status!=='Done'&&hkTaskKind(t)!=='repair'&&rooms.has(t.room)).length;
    return {count:n,where:n?[`${n} open task${n===1?'':'s'} in its rooms`]:[]};},
  validate:(s,row,rows)=>{
    if(!(row.floors??[]).length&&!(row.rooms??[]).length)return 'Choose at least one floor or room for the zone.';
    if(!row.active)return undefined;
    for(const z of rows){if(z.id===row.id||!z.active)continue;
      const f=(row.floors??[]).find(x=>(z.floors??[]).includes(x));if(f)return `${mstFloorLabel(s,Number(f.slice(1))||0)} is already in zone “${z.name}” — a floor belongs to one active zone.`;
      const r=(row.rooms??[]).find(x=>(z.rooms??[]).includes(x));if(r)return `Room ${r} is already in zone “${z.name}” — a room belongs to one active zone.`;}
    return undefined;},
  seed:hkzSeed,
};
registerMaster(HKT_SPEC as unknown as MasterSpec);
registerMaster(HKZ_SPEC as unknown as MasterSpec);
export type {HkTaskKind};

// Stream D · Housekeeping & rooms (owns TASK, BLOCK) — fixture extensions.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
/** Called on every new base fixture (all scenarios start from it). Add this stream's seed data here. */
export function extendBase(s:State):void{
  // Base tasks are checkout cleans; stamp kind/date so cards show them explicitly.
  s.tasks.forEach(t=>{t.kind=t.kind??'checkout';t.date=t.date??s.date;t.createdAt=t.createdAt??s.date+'T07:30';});
  // Room 308: undated block (blocks every interval, as in M1) with a reason, and an open repair task for Maintenance.
  const r308=s.rooms.find(r=>r.id==='308');
  if(r308?.blocked){
    r308.blockReason='Bathroom leak — plumber booked (fictional)';
    s.tasks.push({id:'repair-308',room:'308',assignee:'maint',status:'To do',checked:[],kind:'repair',note:'Bathroom leak under the basin',date:s.date,createdAt:s.date+'T07:45'});
  }
}
/** Extra per-scenario changes, applied after the scenario's own builder. */
export const scenarioPatches:Record<string,(s:State)=>void>={
  // S1 empties tasks and unblocks rooms: clear the leftover block reason too.
  S1:s=>{s.rooms.forEach(r=>{r.blockReason=undefined;r.blockFrom=undefined;r.blockTo=undefined;});},
  S2:s=>{const r=s.rooms.find(r=>r.id==='105');if(r){r.blockReason='Electrical fault — awaiting electrician (fictional)';r.blockFrom=s.date;}},
};
/** New scenarios, or overrides of existing scenario info (e.g. marking S4 available). */
export const scenarioInfo:ScenarioInfo[]=[];

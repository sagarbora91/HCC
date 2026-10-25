// Stream F · Cashier, closing & reports (owns NIGHT_POST, POST_NIGHTS, SHIFT_CLOSE, DAY_CLOSE, NEXT_DAY, EXPENSE, EXPENSE_APPROVE) — fixture extensions.
import type {State} from '../../domain/types';
import {gstForRoomNight} from '../../domain/gst';
import type {ScenarioInfo} from '../scenarios';
/** Called on every new base fixture (all scenarios start from it). Add this stream's seed data here. */
export function extendBase(_s:State):void{}
/** S5 · night audit interrupted on 24 Oct after posting 3 of 8 in-house nights (HCC-1041…1043). */
function s5(s:State){
  const posted=['HCC-1041','HCC-1042','HCC-1043'];
  for(const id of posted){const b=s.bookings.find(b=>b.id===id)!;
    b.lines.push({id:`night:${id}:${s.date}`,label:'Room night · '+s.date,amount:b.rate,date:s.date,kind:'room',taxBps:gstForRoomNight(s,s.date,b.rate).bps});b.version++;}
  s.closeProgress={date:s.date,posted,total:s.bookings.filter(b=>b.status==='In house').length,interrupted:true,runs:1};
}
/** Extra per-scenario changes, applied after the scenario's own builder. */
export const scenarioPatches:Record<string,(s:State)=>void>={S5:s5};
/** New scenarios, or overrides of existing scenario info (e.g. marking S4 available). */
export const scenarioInfo:ScenarioInfo[]=[
  {id:'S5',name:'Night-audit interruption',purpose:'Night audit for 24 Oct stopped after posting 3 of 8 nights (HCC-1041…1043). Resume posts the remaining 5 only.',available:true},
];

// Stream E · Access & configuration (owns USER, GRANT, SETTINGS) — fixture extensions.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {adminDefaultPolicy} from '../../domain/streams/admin';
/** Called on every new base fixture (all scenarios start from it). Seeds the RBAC matrix v1 policy (version 1). */
export function extendBase(s:State):void{
  s.policy=adminDefaultPolicy(s.clock);
  s.settings.invoicePrefix='DEMO-INV';
  s.settings.approvalExpiryHours=24;
  s.tariffs=[];s.taxSchedule=[];s.configHistory=[];
}
/** Extra per-scenario changes, applied after the scenario's own builder. */
export const scenarioPatches:Record<string,(s:State)=>void>={
  // S6 Access policy (Fixture Catalogue §3): reception granted refund.request until tomorrow; conflicting allow + deny on
  // cashier report.export (deny wins); a temporary user whose assignment has expired.
  S6:s=>{
    const p=s.policy!;
    p.exceptions.push(
      {id:'EXC-S6-1',user:'reception',perm:'PERM-PAY-003',effect:'allow',reason:'Covering cashier desk this weekend',expires:'2026-10-25',by:'owner',at:s.clock},
      {id:'EXC-S6-2',user:'cashier',perm:'PERM-RPT-003',effect:'allow',reason:'Month-end export help',expires:'2026-10-31',by:'owner',at:s.clock},
      {id:'EXC-S6-3',user:'cashier',perm:'PERM-RPT-003',effect:'deny',reason:'Exports restricted pending audit',expires:'2026-11-30',by:'owner',at:s.clock});
    s.users.push({id:'temp-rec',name:'Isha · Reception (temporary)',role:'Reception',enabled:true,expiresOn:'2026-10-20',assignedBy:'owner',assignedOn:'2026-10-01'});
  },
};
/** New scenarios, or overrides of existing scenario info (e.g. marking S4 available). */
export const scenarioInfo:ScenarioInfo[]=[];

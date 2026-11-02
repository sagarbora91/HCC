// ADM-2 · Custom roles — fixture extension (fictional data only). Two Manager-based custom roles and one user each, seeded in the FY26
// scenario (the app's default) so BASE keeps its ten demo users (tests pin them). Tests call rolSeedDemo on any state.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {rolNewRole,rolApplyFeature} from '../../domain/streams/roles';

/** "Day shift manager" (C01: Manager, every Accounting sub-function off) and "Night shift manager" (C02: Manager, Accounting vouchers /
 *  GST / TDS / Tally view only), plus users `daymgr` and `nightmgr`. Idempotent. */
export function rolSeedDemo(s:State):State{
  if(s.rolesX?.roles.some(r=>r.id==='C01'))return s;
  const x=s.rolesX??(s.rolesX={roles:[],history:[]});
  const day=rolNewRole(s,{id:'C01',name:'Day shift manager',base:'Manager',by:'owner'});
  for(const f of ['accounting.vouchers','accounting.gst','accounting.tds','accounting.tally'])rolApplyFeature(s,day,f,'off');
  const night=rolNewRole(s,{id:'C02',name:'Night shift manager',base:'Manager',by:'owner'});
  for(const f of ['accounting.vouchers','accounting.gst','accounting.tds','accounting.tally'])rolApplyFeature(s,night,f,'view');
  x.roles.push(day,night);
  x.history.unshift({version:s.policy?.version??1,at:s.clock,actor:'System',summary:'Demo custom roles C01 Day shift manager, C02 Night shift manager',before:'—',after:'2 custom roles (Manager base)',reason:'Fixture'});
  s.users.push({id:'daymgr',name:'Tanvi · Day shift manager',role:'Manager',customRole:'C01',enabled:true,assignedBy:'owner',assignedOn:s.date},
    {id:'nightmgr',name:'Rohan · Night shift manager',role:'Manager',customRole:'C02',enabled:true,assignedBy:'owner',assignedOn:s.date});
  return s;
}
export function extendBase(_s:State):void{}
export const scenarioPatches:Record<string,(s:State)=>void>={FY26:s=>{rolSeedDemo(s);}};
export const scenarioInfo:ScenarioInfo[]=[];

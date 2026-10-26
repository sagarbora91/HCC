// Production state skeleton (W4-0): the prototype State shape with NO records, so screens render before (or without) server
// data and never show demo data. Built from the BASE fixture's shape: every list is emptied — at the top level and inside each
// module slice (`…X`) — users/grants are empty (the signed-in user comes from /me/capabilities, capabilities.ts), and the
// hotel name is blank until the configuration area loads it. Settings-like objects inside module slices keep the fixture's
// defaults (thresholds, flags); an area that owns a slice replaces the WHOLE slice, so those defaults only show for areas not
// connected yet. Scalars `date`/`clock` come from the core loader (sync/core.ts).
import {seed,roles,type State} from '../../services/mock';

export const SERVER_SCENARIO='SERVER';
const emptyLists=(o:Record<string,unknown>)=>Object.fromEntries(Object.entries(o).map(([k,v])=>[k,Array.isArray(v)?[]:v]));

export function emptyState():State{
  const s=seed() as unknown as Record<string,unknown>;
  const out:Record<string,unknown>={};
  for(const [k,v] of Object.entries(s)){
    if(Array.isArray(v))out[k]=[];
    else if(/X$/.test(k)&&v&&typeof v==='object')out[k]=emptyLists(v as Record<string,unknown>);
    else out[k]=v;
  }
  const st=out as unknown as State;
  st.scenario=SERVER_SCENARIO;st.sequence=0;st.openingCash=0;st.shiftOpen=false;
  st.grants=Object.fromEntries(roles.map(r=>[r,[]])) as unknown as State['grants'];
  st.settings={...st.settings,hotel:''};
  if(st.closings)st.closings={};
  return st;
}

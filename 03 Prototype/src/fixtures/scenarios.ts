// Scenario catalogue (HCC_Fixture_and_Scenario_Catalog.md §3). Each scenario is built from the base fixture
// explicitly; no scenario depends on another having run.
import {State} from '../domain/types';
import {baseFixture,BUSINESS_DATE} from './base';
import {extensions} from './ext';
import {buildHistory} from './history/generate';
export type ScenarioInfo={id:string;name:string;purpose:string;available:boolean;availableFrom?:string};
/** Scenario the app opens on when nothing is saved (plan H-02). Tests and `seed()` keep using BASE. */
export const DEFAULT_SCENARIO='FY26';
const builtIn:ScenarioInfo[]=[
  {id:'FY26',name:'FY 2026-27 history (Apr–Oct)',purpose:'Normal operating day (BASE) after 206 generated business days, 1 Apr – 23 Oct 2026: stays, folios and receipts. Fictional data.',available:true},
  {id:'BASE',name:'Normal operating day',purpose:'Base fixture: 8 in house, 2 arrivals, dirty/inspection/blocked rooms.',available:true},
  {id:'S0',name:'Owner walkthrough',purpose:'12-step journey for Room 105 across 24–26 Oct; unrelated stays do not block day closing.',available:true},
  {id:'S1',name:'Empty hotel',purpose:'Every screen in its empty state.',available:true},
  {id:'S2',name:'Room 105 blocked',purpose:'Booking/check-in exceptions: 105 out of service, arrival assigned to dirty 108.',available:true},
  {id:'S3',name:'Exceptions day',purpose:'Overdue unpaid departure, stale discount approval, pending expense.',available:true},
  {id:'S4',name:'Company credit',purpose:'Authorised company-credit checkout.',available:false,availableFrom:'M2 (company credit, BL-020)'},
  {id:'S5',name:'Night-audit interruption',purpose:'Resume day close without reposting.',available:false,availableFrom:'M3 (close resume, BL-030)'},
  {id:'S6',name:'Access policy',purpose:'Disabled users, second owner account, reduced cashier grants.',available:true},
];
// Streams may add scenarios or override built-in info (fixtures/ext/*).
export const scenarios:ScenarioInfo[]=(()=>{const list=[...builtIn];extensions.flatMap(x=>x.scenarioInfo).forEach(i=>{const at=list.findIndex(x=>x.id===i.id);if(at>=0)list[at]=i;else list.push(i);});return list;})();
const builders:Record<string,(s:State)=>void>={
  BASE:()=>{},
  FY26:s=>{Object.assign(s,buildHistory(s));},
  S0:s=>{s.bookings.forEach(b=>{if(b.status==='In house')b.departure='2026-10-28';if(b.status==='Confirmed'){b.arrival='2026-10-27';b.departure='2026-10-29';}});},
  S1:s=>{s.bookings=[];s.tasks=[];s.expenses=[];s.approvals=[];s.rooms.forEach(r=>{r.hk='Ready';r.blocked=false;});},
  S2:s=>{
    const r105=s.rooms.find(r=>r.id==='105')!;r105.blocked=true;
    s.bookings.push({id:'HCC-1051',guest:'Meera Joshi (fictional)',email:'guest11@example.test',people:1,room:'108',arrival:BUSINESS_DATE,departure:'2026-10-25',rate:200000,source:'Phone',status:'Confirmed',registered:false,lines:[],
      receipts:[{id:'seed-pay-11',amount:100000,method:'UPI',date:BUSINESS_DATE,kind:'payment',reference:'DEMO-111',actor:'cashier'}],version:1,notes:'Assigned to a dirty room — clean before check-in'});
  },
  S3:s=>{
    const b1042=s.bookings.find(b=>b.id==='HCC-1042')!;b1042.departure=BUSINESS_DATE;b1042.notes='Departure due today with an unpaid balance';
    const b1043=s.bookings.find(b=>b.id==='HCC-1043')!;b1043.version=2;
    s.approvals.push({id:'APR-1001',booking:'HCC-1043',amount:50000,reason:'Late check-in goodwill',actor:'reception',version:1,status:'Pending'});
  },
  S6:s=>{
    s.users.push({id:'owner2',name:'Anil · Owner (second account)',role:'Owner',enabled:false});
    s.users.find(u=>u.id==='housekeeper2')!.enabled=false;
    s.grants.Cashier=s.grants.Cashier.filter(p=>p!=='reports');
  },
};
export function loadScenario(id:string):State{
  const info=scenarios.find(x=>x.id===id);
  if(!info||!info.available)throw new Error(`Scenario ${id} is not available yet.`);
  const s=baseFixture();builders[id]?.(s);extensions.forEach(x=>x.scenarioPatches[id]?.(s));s.scenario=id;
  // FY26 keeps its generated history entries (newest first) below the "Scenario loaded" line.
  s.audit=[{id:'a0',at:s.clock,actor:'System',action:'Scenario loaded',detail:`${id} · ${info.name}`},...(id==='FY26'?s.audit.filter(a=>a.id.startsWith('h-a-')):[])];
  return s;
}

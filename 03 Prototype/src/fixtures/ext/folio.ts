// Stream C · Payments & folio (owns PAYMENT, CHARGE, DISCOUNT, APPROVAL, CHECKOUT) — fixture extensions.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
/** Called on every new base fixture (all scenarios start from it). Add this stream's seed data here. */
export function extendBase(_s:State):void{}
/** Extra per-scenario changes, applied after the scenario's own builder. */
export const scenarioPatches:Record<string,(s:State)=>void>={
  // S4 · Company credit (BL-020, AC-008): in-house corporate stay due out today, balance ₹6,600, no receipts.
  S4:s=>{
    const room=s.rooms.find(r=>r.id==='203')!;
    s.bookings.push({id:'HCC-1052',guest:'Rohan Kulkarni (fictional)',email:'travel@demo-industries.example.test',people:1,room:'203',
      arrival:'2026-10-22',departure:'2026-10-24',rate:room.rate,source:'Corporate',status:'In house',registered:true,
      lines:['2026-10-22','2026-10-23'].map(d=>({id:`night:HCC-1052:${d}`,label:'Room night · '+d,amount:room.rate,date:d,kind:'room' as const,taxBps:1000})),
      receipts:[],version:1,notes:'Corporate stay billed to Demo Industries Pvt Ltd (fictional) — company credit needs approval',
      company:{name:'Demo Industries Pvt Ltd (fictional)',gstin:'27AAAAA0000A1Z5'}});
    if(!s.closedDates.includes('2026-10-22'))s.closedDates.unshift('2026-10-22');
  },
};
/** New scenarios, or overrides of existing scenario info (e.g. marking S4 available). */
export const scenarioInfo:ScenarioInfo[]=[
  {id:'S4',name:'Company credit',purpose:'Authorised company-credit checkout: HCC-1052 (Demo Industries Pvt Ltd, fictional) in Room 203 departs today with ₹6,600 due.',available:true},
];

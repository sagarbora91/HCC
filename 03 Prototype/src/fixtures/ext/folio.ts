// Stream C · Payments & folio (owns PAYMENT, CHARGE, DISCOUNT, APPROVAL, CHECKOUT) — fixture extensions.
import type {State} from '../../domain/types';
import {gstForRoomNight} from '../../domain/gst';
import type {ScenarioInfo} from '../scenarios';
import type {GRPState} from '../../domain/streams/groups';
/** Called on every new base fixture (all scenarios start from it). Add this stream's seed data here. */
export function extendBase(_s:State):void{}
/** Extra per-scenario changes, applied after the scenario's own builder. */
export const scenarioPatches:Record<string,(s:State)=>void>={
  // S4 · Company credit (BL-020, AC-008): in-house corporate stay due out today, balance ₹6,300 (₹6,000 + 5 % GST; was ₹6,600 with the synthetic 10 %), no receipts.
  // DEC-076: the stay is billed to the company account CO-4 (credit limit ₹50,000, nothing else owed), so company credit fits the limit.
  S4:s=>{
    (s.groupsX as GRPState).companies.push({id:'CO-4',name:'Demo Industries Pvt Ltd (fictional)',gstin:'27AAAAA0000A1Z5',contact:'Rohan Kulkarni',phone:'+91 90000 00044',
      email:'travel@demo-industries.example.test',city:'Pune',rates:{},creditLimit:5000000,openingOutstanding:0,limitHistory:[]});
    const room=s.rooms.find(r=>r.id==='203')!;
    s.bookings.push({id:'HCC-1052',guest:'Rohan Kulkarni (fictional)',email:'travel@demo-industries.example.test',people:1,room:'203',
      arrival:'2026-10-22',departure:'2026-10-24',rate:room.rate,source:'Corporate',status:'In house',registered:true,
      lines:['2026-10-22','2026-10-23'].map(d=>({id:`night:HCC-1052:${d}`,label:'Room night · '+d,amount:room.rate,date:d,kind:'room' as const,taxBps:gstForRoomNight(s,d,room.rate).bps})),
      receipts:[],version:1,notes:'Corporate stay billed to Demo Industries Pvt Ltd (fictional) — company credit needs approval',
      company:{name:'Demo Industries Pvt Ltd (fictional)',gstin:'27AAAAA0000A1Z5',account:'CO-4'}});
    if(!s.closedDates.includes('2026-10-22'))s.closedDates.unshift('2026-10-22');
  },
};
/** New scenarios, or overrides of existing scenario info (e.g. marking S4 available). */
export const scenarioInfo:ScenarioInfo[]=[
  {id:'S4',name:'Company credit',purpose:'Authorised company-credit checkout: HCC-1052 (Demo Industries Pvt Ltd, fictional) in Room 203 departs today with ₹6,600 due.',available:true},
];

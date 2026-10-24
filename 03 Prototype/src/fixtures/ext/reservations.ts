// Stream A · Calendar & reservations (owns BOOK, CANCEL, CONFIRM/hold commands) — fixture extensions.
// The held bookings are added per scenario (BASE, S3) rather than in extendBase so S0/S1 figures stay unchanged.
import type {State,Booking} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
const hold=(s:State,id:string,guest:string,holdUntil:string,n:number):Booking=>({id,guest,email:`guest${n}@example.test`,phone:'+91 90000 0'+String(n).padStart(4,'0'),people:2,room:'106',
  arrival:s.date,departure:'2026-10-25',rate:s.rooms.find(r=>r.id==='106')!.rate,source:'Phone',status:'Held',holdUntil,registered:false,lines:[],receipts:[],version:1,
  notes:'Fictional guest · demonstration hold'});
/** Called on every new base fixture (all scenarios start from it). Add this stream's seed data here. */
export function extendBase(_s:State):void{}
/** Extra per-scenario changes, applied after the scenario's own builder. */
export const scenarioPatches:Record<string,(s:State)=>void>={
  // Running hold: Suite 106 for tonight, expires 09:30 simulated time (clock starts 08:00).
  BASE:s=>{s.bookings.push(hold(s,'HCC-1090','Sana Kulkarni (fictional)','2026-10-24T09:30',90));},
  // Fixture Catalogue S3: "expired hold on 106" — lapsed at 07:30, so it shows Expired and does not block 106.
  S3:s=>{s.bookings.push(hold(s,'HCC-1091','Omar Siddiqui (fictional)','2026-10-24T07:30',91));},
};
/** New scenarios, or overrides of existing scenario info (e.g. marking S4 available). */
export const scenarioInfo:ScenarioInfo[]=[];

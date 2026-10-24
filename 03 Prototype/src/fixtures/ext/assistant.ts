// Module 26 · AI management assistant — fixture extensions (fictional data only).
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {aiEmpty} from '../../domain/streams/assistant';
/** Every scenario starts with an empty conversation and the default 4-hour dirty-room threshold. */
export function extendBase(s:State):void{s.assistantX=aiEmpty();}
/** AI1 · anomalies to demonstrate the assistant's flags. Adds a fictional departed stay (no money added to existing bookings). */
function ai1(s:State){
  const d=s.date;
  // A departed stay from yesterday with an unpaid balance and a large approved discount.
  s.bookings.push({id:'HCC-1091',guest:'Rohan Kulkarni (fictional)',email:'guest91@example.test',people:2,room:'107',arrival:'2026-10-22',departure:d,rate:300000,source:'Phone',status:'Departed',registered:true,
    lines:[{id:'ai1-night-1',label:'Room night · 22 Oct',amount:300000,date:'2026-10-22',kind:'room',taxBps:1000},
      {id:'ai1-night-2',label:'Room night · 23 Oct',amount:300000,date:'2026-10-23',kind:'room',taxBps:1000},
      {id:'ai1-disc',label:'Discount · Long-stay goodwill',amount:-80000,date:'2026-10-23',kind:'discount',taxBps:1000}],
    receipts:[{id:'ai1-pay',amount:400000,method:'UPI',date:'2026-10-23',kind:'payment',reference:'DEMO-AI1',actor:'cashier'}],
    version:3,notes:'Fictional · departed with a balance still due (assistant anomaly demo)'});
  // Clock later in the day so dirty rooms age past the threshold.
  s.clock=d+'T15:30';
}
export const scenarioPatches:Record<string,(s:State)=>void>={AI1:ai1};
export const scenarioInfo:ScenarioInfo[]=[
  {id:'AI1',name:'Assistant anomalies',purpose:'Base day at 15:30 plus a departed stay with an unpaid balance and a ₹800 discount — shows the assistant’s anomaly flags.',available:true},
];

// LF-ui (lane lf-tests) · scenario LF-PARITY: one day of the old Lodge software's behaviour, driven with REAL commands on the base fixture (fictional data).
//   room 105  in house, rate changed for every open night (A1, scope all) with a re-posted night;
//   room 106  departed, bill corrected after check-out (A2): credit note + revision-2 invoice, refund due;
//   room 107  a foreign guest checked in with Form C and a purpose / vehicle (A3–A5), booked on a 24-hour basis (A6);
//   room 203  a guest with an agreed rate (B1) and a credit account (B2) who leaves on guest credit at check-out;
//   a counter bill settled on Udhaar credit (A7).
// The command engine is injected (lfParityBind, bound in services/mock/lf-parity-bind.ts): a fixture must not import the command registry.
import type {State,Command} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {totals} from '../../domain/selectors';

type Exec=(s:State,uid:string,cmd:Command)=>State;
let engine:Exec|undefined;
export function lfParityBind(execute:Exec):void{engine=execute;}
export const LF_PARITY='LF-PARITY';

export const FORM_C={passportPlace:'Berlin',passportIssued:'2022-03-01',passportExpiry:'2032-02-28',visaNo:'V9988776',visaType:'Tourist',visaPlace:'Berlin',visaIssued:'2026-09-01',
  visaExpiry:'2027-03-01',arrivedFrom:'Frankfurt',arrivalInIndia:'2026-10-20',intendedStayDays:10,nextDestination:'Goa',employedInIndia:false,addressAbroad:'1 Example Str, Berlin (fictional)',sex:'F'};

export function lfParityDay(s0:State,exec:Exec|undefined=engine):State{
  if(!exec)throw new Error('LF-PARITY needs the command engine (services/mock/lf-parity-bind.ts binds it).');
  let s=s0;const run=(uid:string,cmd:Command)=>{s=exec(s,uid,cmd);return s;};
  const book=(guest:string,room:string,departure:string,extra:Record<string,unknown>={},guestRec?:Record<string,unknown>)=>{
    run('owner',{type:'BOOK',guest,room,arrival:s.date,departure,people:1,...extra});const id=s.bookings.at(-1)!.id;
    run('owner',{type:'REGISTER',id,...(guestRec?{guest:guestRec}:{})} as Command);return id;};
  // A1 rate change, scope all, after tonight's posting
  const a=book('Rate Change Guest (fictional)','105','2026-10-27');
  run('owner',{type:'CHECKIN',id:a});run('owner',{type:'NIGHT_POST'});
  run('owner',{type:'STAY_RATE_CHANGE',booking:a,rate:150000,scope:'all',reason:'Long stay (fictional)'});
  // A2 bill corrected after check-out
  const b=book('Corrected Bill Guest (fictional)','106','2026-10-26');
  run('owner',{type:'CHECKIN',id:b});run('owner',{type:'POST_NIGHTS',id:b});run('owner',{type:'CHARGE',id:b,label:'Laundry',amount:50000});
  run('cashier',{type:'PAYMENT',id:b,amount:totals(s.bookings.find(x=>x.id===b)!).balance,method:'UPI',kind:'payment',reference:'DEMO-UPI'});
  run('owner',{type:'CHECKOUT',id:b});
  const bx=s.bookings.find(x=>x.id===b)!,laundry=bx.lines.find(l=>l.label==='Laundry')!;
  run('reception',{type:'BILL_CORRECTION_REQUEST',booking:b,reason:'Wrong rent and laundry (fictional)',changes:[{kind:'room-rate',night:s.date,newRate:150000},{kind:'remove-line',lineId:laundry.id}]});
  run('owner',{type:'APPROVAL',id:s.approvals.at(-1)!.id,approve:true});
  // A3–A6 foreign guest, Form C, purpose, vehicle, 24-hour basis
  const c=book('Foreign Guest (fictional)','107','2026-10-26',{},{name:'Foreign Guest',phone:'9876500002',email:'',address:'Demo address (fictional)',nationality:'German',idType:'Passport (demo)',
    idNumber:'P1234567',preferences:'',notes:'',party:[],dob:'1990-05-05'});
  run('reception',{type:'CHECKIN',id:c,desk:true,purpose:'TOURISM',vehicle:{no:'MH24AB1234',type:'Car'},checkoutBasis:'24h',formC:FORM_C} as Command);
  // B1 + B2 agreed rate and credit account; the guest leaves on guest credit
  const G=s.guests![0].id;
  run('owner',{type:'GUEST_RATE_SET',guestId:G,roomType:s.rooms.find(r=>r.id==='203')!.type,rate:180000});
  run('owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:G,limit:1000000,note:'Regular (fictional)'});
  run('owner',{type:'BOOK',guest:s.guests![0].name,room:'203',arrival:s.date,departure:'2026-10-26',people:1});const d=s.bookings.at(-1)!.id;
  run('owner',{type:'REGISTER',id:d,guestId:G,guest:{...s.guests![0]}} as Command);run('owner',{type:'CHECKIN',id:d});run('owner',{type:'POST_NIGHTS',id:d});
  run('reception',{type:'GUEST_CREDIT_REQUEST',booking:d,reason:'Regular guest (fictional)'});
  run('manager',{type:'APPROVAL',id:s.approvals.at(-1)!.id,approve:true});run('reception',{type:'CHECKOUT',id:d});
  // A7 counter bill on Udhaar credit
  run('reception',{type:'CTR_BILL_ISSUE',party:{name:'Walk-in Customer',phone:'9876500003'},lines:[{label:'Courier',unit:15000,gstCategory:'other'},{label:'Laundry',qty:2,unit:30000,gstCategory:'laundry'}],
    settlement:{mode:'udhaar'},remark:'LF-PARITY (fictional)'});
  return s;
}

export function extendBase(_s:State):void{}
export const scenarioPatches:Record<string,(s:State)=>void>={[LF_PARITY]:s=>{Object.assign(s,lfParityDay(s));}};
export const scenarioInfo:ScenarioInfo[]=[
  {id:LF_PARITY,name:'Legacy parity day (LF)',purpose:'Rate change for every open night, a bill corrected after check-out, a foreign guest with Form C / purpose / vehicle on a 24-hour basis, an agreed rate and a guest-credit check-out, a counter bill on Udhaar credit. Fictional data.',available:true},
];

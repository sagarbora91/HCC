// Stream B · Guests & check-in (owns REGISTER, CHECKIN, MOVE) — fixture extensions.
// All guest data is fictional: example.test emails, 98xxx 0xxxx-style demo phones, DEMO-style masked ID last-4 only.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
type G=NonNullable<State['guests']>[number];
// [booking, name, phone, city, idType, idLast4, preferences, company?, gstin?, party?]
const seed:[string,string,string,string,string,string,string,string?,string?,G['party']?][]=[
  ['HCC-1041','Ananya Rao','+91 98200 01041','Bengaluru, Karnataka','Aadhaar (demo)','4101','High floor, extra pillows'],
  ['HCC-1042','Vikram Shah','+91 98200 01042','Ahmedabad, Gujarat','Passport (demo)','Z042','Late checkout requests','Shah Demo Traders','24ABCDE1234F1Z5',[{name:'Meena Shah',ageBand:'Adult',relation:'Spouse'}]],
  // Riya Mehta and Kabir Sen share a household phone number — demonstrates the duplicate-phone suggestion (AC-032).
  ['HCC-1043','Riya Mehta','+91 98200 07777','Pune, Maharashtra','Driving licence (demo)','D043','Vegetarian meals'],
  ['HCC-1044','Arjun Kapoor','+91 98200 01044','New Delhi','Aadhaar (demo)','4404','Quiet room',undefined,undefined,[{name:'Ira Kapoor',ageBand:'Child',relation:'Daughter'}]],
  ['HCC-1045','Nisha Desai','+91 98200 01045','Surat, Gujarat','Voter ID (demo)','V045','Early breakfast'],
  ['HCC-1046','Kabir Sen','098200 07777','Pune, Maharashtra','Aadhaar (demo)','4606','Feather-free bedding','Sen Demo Consulting','27ABCDE5678G1Z9',[{name:'Rhea Sen',ageBand:'Adult',relation:'Sister'}]],
  ['HCC-1047','Tara Iyer','+91 98200 01047','Chennai, Tamil Nadu','Passport (demo)','P047','Twin beds'],
  ['HCC-1048','Dev Malhotra','+91 98200 01048','Chandigarh','Aadhaar (demo)','4808','Airport pickup on departure',undefined,undefined,[{name:'Asha Malhotra',ageBand:'Senior',relation:'Mother'}]],
  ['HCC-1049','Ishaan Bose','+91 98200 01049','Kolkata, West Bengal','Driving licence (demo)','D049','Arriving late evening'],
  ['HCC-1050','Aditi Nair','+91 98200 01050','Kochi, Kerala','Aadhaar (demo)','5010','Sea-facing if possible',undefined,undefined,[{name:'Rohan Nair',ageBand:'Adult',relation:'Spouse'}]],
];
/** Called on every new base fixture (all scenarios start from it). Add this stream's seed data here. */
export function extendBase(s:State):void{
  s.guests=seed.map(([bid,name,phone,city,idType,idLast4,preferences,company,gstin,party],i)=>{
    const b=s.bookings.find(b=>b.id===bid)!;
    return {id:'G-'+String(i+1).padStart(4,'0'),name,phone,email:b.email,address:`${10+i} Demo Street, ${city} (fictional)`,nationality:'Indian',idType,idLast4,
      ...(company?{company,gstin}:{}),preferences,notes:'Fictional guest · demonstration profile',party:party||[],verified:b.status==='In house',origin:'seed',version:1};
  });
  s.guests.forEach((g,i)=>{const b=s.bookings.find(b=>b.id===seed[i][0])!;b.guestId=g.id;
    if(b.status==='In house')b.segments=[{room:b.room,from:b.arrival}];});
}
/** Extra per-scenario changes, applied after the scenario's own builder. */
export const scenarioPatches:Record<string,(s:State)=>void>={
  // S0 moves in-house departures/arrivals; keep the open segment start aligned with the arrival.
  S0:s=>{s.bookings.forEach(b=>{if(b.status==='In house'&&b.segments)b.segments=[{room:b.room,from:b.arrival}];});},
  S1:s=>{s.guests=[];},
};
/** New scenarios, or overrides of existing scenario info (e.g. marking S4 available). */
export const scenarioInfo:ScenarioInfo[]=[];

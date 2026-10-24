// Module 19 · Checklists & shift handover — fixture extensions (fictional data only).
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import type {HOVTemplate,HOVRun,HOVNote,HOVItem} from '../../domain/streams/handover';
import {hovNewRun} from '../../domain/streams/handover';

const it=(id:string,text:string,o:Partial<HOVItem>={}):HOVItem=>({id,text,mandatory:true,...o});
export function hovFixtureTemplates():HOVTemplate[]{
  const t=(id:string,name:string,desk:HOVTemplate['desk'],shift:HOVTemplate['shift'],roles:HOVTemplate['roles'],items:HOVItem[]):HOVTemplate=>({id,name,desk,shift,roles,items,active:true,version:1,updatedBy:'manager',updatedAt:'2026-10-20T10:00'});
  return [
    t('HOVT-FD-OPEN','Front desk opening','Front desk','Morning',['Reception'],[
      it('fo1','Read and acknowledge the night handover note',{due:'07:30'}),
      it('fo2','Review today’s arrivals and special requests',{due:'08:30',link:'reservations'}),
      it('fo3','Check the room board: dirty / inspection / blocked rooms',{due:'08:30',link:'rooms'}),
      it('fo4','Verify the opening cash float with the cashier',{due:'09:00',link:'cashier'}),
      it('fo5','Test the key-card encoder and printer (simulated)',{mandatory:false,due:'09:00'}),
      it('fo6','Lobby and desk presentable — photo',{due:'09:30',photo:true}),
      it('fo7','Note VIP / repeat guests for the shift',{mandatory:false})]),
    t('HOVT-FD-CLOSE','Front desk closing','Front desk','Evening',['Reception'],[
      it('fc1','All due departures settled',{due:'20:00',link:'folios'}),
      it('fc2','Pending arrivals called / no-shows marked',{due:'21:00',link:'reservations'}),
      it('fc3','Key cards reconciled against in-house rooms',{due:'22:00'}),
      it('fc4','Lost-property register updated',{mandatory:false}),
      it('fc5','Handover note written for the night shift',{due:'22:45',link:'handover'}),
      it('fc6','Desk tidy — photo',{mandatory:false,photo:true})]),
    t('HOVT-CASH-HO','Cashier handover','Front desk','Evening',['Cashier','Reception'],[
      it('ch1','Count the drawer and record the count',{due:'21:30',link:'cashier'}),
      it('ch2','Match UPI settlement report (simulated)',{due:'21:30'}),
      it('ch3','File expense vouchers for today',{mandatory:false,link:'expenses'}),
      it('ch4','Record the safe drop in the register',{due:'22:00'}),
      it('ch5','Card terminal batch closed (simulated)',{mandatory:false}),
      it('ch6','Cash float line written in the handover note',{due:'22:45',link:'handover'})]),
    t('HOVT-HK-FLOOR','Housekeeping floor check','Housekeeping','Morning',['Housekeeper','Supervisor'],[
      it('hk1','Trolley stocked (linen, amenities, chemicals)',{due:'07:45'}),
      it('hk2','Corridors floors 1–3 clean — photo',{due:'09:00',photo:true}),
      it('hk3','Lost & found items logged',{mandatory:false}),
      it('hk4','Note Do-Not-Disturb rooms for the supervisor',{due:'10:00'}),
      it('hk5','Linen par levels checked',{mandatory:false,due:'11:00'}),
      it('hk6','Repairs reported from rooms',{mandatory:false,link:'housekeeping'}),
      it('hk7','Chemicals locked in the store',{due:'14:30'})]),
    t('HOVT-NA-PRE','Night audit pre-checks','Front desk','Night',['Manager','Reception'],[
      it('na1','All arrivals checked in or cancelled',{due:'23:30',link:'reservations'}),
      it('na2','Room nights posted for in-house guests',{due:'00:30',link:'closing'}),
      it('na3','Pending approvals and expenses cleared',{due:'00:30',link:'approvals'}),
      it('na4','Cash shift counted and reconciled',{due:'01:00',link:'cashier'}),
      it('na5','Data backup taken (simulated)',{mandatory:false,due:'02:00'}),
      it('na6','Lobby and gate security round',{due:'03:00'})]),
    t('HOVT-MNT-RND','Maintenance rounds','Maintenance','Morning',['Maintenance','Supervisor'],[
      it('mr1','DG set diesel level noted',{due:'08:00'}),
      it('mr2','Overhead water tank level checked',{due:'08:30'}),
      it('mr3','Lift alarm and intercom test',{due:'09:30'}),
      it('mr4','Fire extinguisher gauges — photo',{due:'10:30',photo:true}),
      it('mr5','Pump room inspected',{mandatory:false}),
      it('mr6','Blocked room 308 status update',{mandatory:false,link:'maintenance'})]),
  ];
}

/** Apply ticks to a run: ids → [actor, HH:mm]. */
function tick(r:HOVRun,date:string,done:Record<string,[string,string]>,extra:Partial<Record<string,{note?:string;photoName?:string}>>={}){
  for(const i of r.items){const d=done[i.id];if(d){i.done=true;i.by=d[0];i.at=date+'T'+d[1];}Object.assign(i,extra[i.id]||{});}return r;}

export function extendBase(s:State):void{
  const templates=hovFixtureTemplates(),T=(id:string)=>templates.find(t=>t.id===id)!;
  const Y='2026-10-23',D=s.date;
  const sub=(r:HOVRun,by:string,at:string,late=false)=>Object.assign(r,{status:'Submitted',submittedBy:by,submittedAt:at,late});
  const runs:HOVRun[]=[
    // Yesterday (23 Oct): mostly complete history.
    sub(tick(hovNewRun(T('HOVT-FD-OPEN'),Y,'reception',Y+'T07:05'),Y,{fo1:['reception','07:10'],fo2:['reception','07:40'],fo3:['reception','07:55'],fo4:['reception','08:20'],fo6:['reception','08:40'],fo7:['reception','09:00']},{fo6:{photoName:'IMG_20261023_0840.jpg'}}),'reception',Y+'T09:05'),
    sub(tick(hovNewRun(T('HOVT-HK-FLOOR'),Y,'housekeeper',Y+'T07:10'),Y,{hk1:['housekeeper','07:20'],hk2:['housekeeper','08:50'],hk4:['housekeeper2','09:30'],hk5:['housekeeper2','10:40'],hk7:['housekeeper','14:10']},{hk2:{photoName:'IMG_20261023_0850.jpg'}}),'supervisor',Y+'T14:20'),
    sub(tick(hovNewRun(T('HOVT-MNT-RND'),Y,'maint',Y+'T07:30'),Y,{mr1:['maint','07:40'],mr2:['maint','08:10'],mr3:['maint','09:20'],mr4:['maint','10:15'],mr5:['maint','11:00']},{mr4:{photoName:'IMG_20261023_1015.jpg',note:'Extinguisher near 2nd-floor lift at the lower green band — refill booked.'}}),'maint',Y+'T11:05'),
    sub(tick(hovNewRun(T('HOVT-CASH-HO'),Y,'cashier',Y+'T21:00'),Y,{ch1:['cashier','21:10'],ch2:['cashier','21:20'],ch3:['cashier','21:25'],ch4:['cashier','21:40'],ch6:['cashier','22:30']}),'cashier',Y+'T22:35'),
    // Front desk closing on 23 Oct was started but never submitted (shows as missed/late in history).
    tick(hovNewRun(T('HOVT-FD-CLOSE'),Y,'reception',Y+'T19:00'),Y,{fc1:['reception','19:30'],fc2:['reception','21:10']}),
    sub(tick(hovNewRun(T('HOVT-NA-PRE'),Y,'manager',Y+'T23:05'),Y,{na1:['manager','23:10'],na2:['manager','23:50'],na3:['manager','23:55'],na4:['manager','23:58'],na6:['manager','23:59']}),'manager',Y+'T23:59'),
    // Today (24 Oct, clock 08:00): opening partly ticked — item fo1 (due 07:30) is overdue until the note is acknowledged.
    tick(hovNewRun(T('HOVT-FD-OPEN'),D,'reception',D+'T07:20'),D,{fo2:['reception','07:25'],fo3:['reception','07:40']},{fo3:{note:'308 blocked for AC repair; 108, 204, 208 dirty.'}}),
    tick(hovNewRun(T('HOVT-HK-FLOOR'),D,'housekeeper',D+'T07:15'),D,{hk1:['housekeeper','07:30']}),
  ];
  const notes:HOVNote[]=[
    {id:'HOVN-HOUSEKEEPING-2026-10-23-M',desk:'Housekeeping',date:Y,shift:'Morning',toShift:'Evening',author:'supervisor',at:Y+'T14:40',
      issues:'Room 206 guest asked for extra pillows at turndown. Iron in 105 sparks — removed.',keys:'Master key returned to the supervisor’s box.',guests:'206 · extra pillows',
      carry:[{id:'HOVN-HOUSEKEEPING-2026-10-23-M-c1',text:'Replace iron for room 105',done:true,doneBy:'housekeeper2',doneAt:Y+'T16:05'}],
      status:'Acknowledged',ackBy:'housekeeper2',ackAt:Y+'T15:05'},
    {id:'HOVN-MAINTENANCE-2026-10-23-M',desk:'Maintenance',date:Y,shift:'Morning',toShift:'Evening',author:'maint',at:Y+'T14:50',
      issues:'Room 308 AC compressor tripping — room stays blocked.',keys:'Terrace and pump-room keys on the maintenance board.',guests:'',
      carry:[{id:'HOVN-MAINTENANCE-2026-10-23-M-c1',text:'Room 308 AC compressor — vendor visit (Coolwave Services, fictional)',done:false},
        {id:'HOVN-MAINTENANCE-2026-10-23-M-c2',text:'Refill 2nd-floor fire extinguisher',done:false}],
      status:'Acknowledged',ackBy:'supervisor',ackAt:Y+'T15:20',ackRemark:'Will follow up with the vendor.'},
    {id:'HOVN-FRONT-2026-10-23-N',desk:'Front desk',date:Y,shift:'Night',toShift:'Morning',author:'manager',at:D+'T06:45',
      issues:'Two arrivals today (Ishaan Bose 103, Aditi Nair 207) — both registered pending. Morning Supplies expense awaiting approval.',
      keys:'14 key cards out, 6 in the drawer; spare master in the safe.',guests:'Room 202 requested a 7:00 wake-up call (done). 301 late checkout request — check with manager.',
      cash:{amount:500000,note:'Opening float in the drawer as counted at close.'},
      carry:[{id:'HOVN-FRONT-2026-10-23-N-c1',text:'Confirm 301 late checkout with the manager',done:false},
        {id:'HOVN-FRONT-2026-10-23-N-c2',text:'Call Aditi Nair to confirm arrival time',done:false}],
      status:'Submitted'},
  ];
  s.handoverX={templates,runs,notes};
}
export const scenarioPatches:Record<string,(s:State)=>void>={};
export const scenarioInfo:ScenarioInfo[]=[];

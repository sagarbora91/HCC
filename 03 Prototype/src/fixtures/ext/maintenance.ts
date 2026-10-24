// Module 15 · Maintenance — fixture extensions (fictional data only: brands, vendors and phone numbers are invented).
// Does not touch rooms, bookings, tasks or money. Stream D's repair task for room 308 is left unconverted on purpose.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import type {MNTAsset,MNTWorkOrder,MNTState} from '../../domain/streams/maintenance';

const D='2026-10-';
function seed():MNTState{
  const vendors=[
    {id:'VEN-01',name:'Sheetal Cooling Services',trade:'AC & refrigeration',phone:'+91 90000 11201'},
    {id:'VEN-02',name:'Unnati Lifts & Elevators',trade:'Lift',phone:'+91 90000 11202'},
    {id:'VEN-03',name:'Urjadhara Power Systems',trade:'Generator / electrical',phone:'+91 90000 11203'},
    {id:'VEN-04',name:'Jalsetu Plumbing Works',trade:'Plumbing & water',phone:'+91 90000 11204'}];
  const amcs=[
    {id:'AMC-01',vendorId:'VEN-02',scope:'Passenger lift — monthly service, breakdown calls',from:'2026-04-01',to:'2027-03-31',visits:12,value:4800000},
    {id:'AMC-02',vendorId:'VEN-03',scope:'62.5 kVA DG set — quarterly service',from:'2026-01-01',to:'2026-12-31',visits:4,value:3000000},
    {id:'AMC-03',vendorId:'VEN-01',scope:'All split ACs — filter, gas check, breakdowns',from:'2025-11-01',to:'2026-10-31',visits:4,value:9600000}];
  const ac=(room:string,inst:string):MNTAsset=>({id:'AC-'+room,name:`Split AC 1.5 T · ${room}`,kind:'AC',room,make:'Himsheetal',model:'HS-18 Inverter',installedOn:inst,vendorId:'VEN-01',status:'Working'});
  const gy=(room:string):MNTAsset=>({id:'GY-'+room,name:`Geyser 25 L · ${room}`,kind:'Geyser',room,make:'Garmjal',model:'GJ-25 Storage',installedOn:'2023-03-10',vendorId:'VEN-04',status:'Working'});
  const tv=(room:string):MNTAsset=>({id:'TV-'+room,name:`LED TV 43" · ${room}`,kind:'TV',room,make:'Drishya',model:'D43 Smart',installedOn:'2024-06-01',status:'Working'});
  const assets:MNTAsset[]=[ac('101','2022-04-12'),ac('103','2022-04-12'),ac('106','2022-04-12'),ac('203','2023-05-02'),ac('205','2023-05-02'),ac('306','2024-02-20'),
    gy('104'),gy('106'),gy('205'),tv('103'),tv('201'),tv('303'),
    {id:'LFT-1',name:'Passenger lift (6 persons)',kind:'Lift',area:'Lobby',make:'Unnati',model:'UL-6 MRL',installedOn:'2021-09-01',vendorId:'VEN-02',status:'Faulty'},
    {id:'DG-1',name:'Diesel generator 62.5 kVA',kind:'Generator',area:'Basement',make:'Urjadhara',model:'UD-62',installedOn:'2021-08-15',vendorId:'VEN-03',status:'Working'},
    {id:'FP-1',name:'Fire hydrant pump',kind:'Pump',area:'Basement',make:'Jalshakti',model:'FH-10',installedOn:'2021-08-15',vendorId:'VEN-04',status:'Working'},
    {id:'RO-1',name:'RO water plant 250 LPH',kind:'RO plant',area:'Kitchen',make:'Nirmalneer',model:'NN-250',installedOn:'2022-01-20',vendorId:'VEN-04',status:'Working'},
    {id:'AC-LOB',name:'Lobby ducted AC 5.5 T',kind:'AC',area:'Lobby',make:'Himsheetal',model:'HS-D55',installedOn:'2021-09-01',vendorId:'VEN-01',status:'Working'}];
  assets.find(a=>a.id==='AC-205')!.status='Faulty';assets.find(a=>a.id==='GY-106')!.status='Faulty';
  const spares=[
    {id:'SP-01',name:'AC filter mesh',unit:'pcs',stock:6,reorderAt:4,cost:35000},
    {id:'SP-02',name:'R32 gas refill',unit:'kg',stock:2,reorderAt:2,cost:180000},
    {id:'SP-03',name:'AC run capacitor 35 µF',unit:'pcs',stock:0,reorderAt:2,cost:65000},
    {id:'SP-04',name:'Geyser thermostat',unit:'pcs',stock:3,reorderAt:2,cost:65000},
    {id:'SP-05',name:'Geyser heating element 2 kW',unit:'pcs',stock:2,reorderAt:1,cost:90000},
    {id:'SP-06',name:'TV remote (universal)',unit:'pcs',stock:3,reorderAt:2,cost:45000},
    {id:'SP-07',name:'Fuse 10 A',unit:'pcs',stock:20,reorderAt:10,cost:4000},
    {id:'SP-08',name:'AC drain pipe',unit:'m',stock:9,reorderAt:5,cost:6000},
    {id:'SP-09',name:'DG diesel filter',unit:'pcs',stock:1,reorderAt:1,cost:125000}];
  const plans=[
    {id:'PM-01',assetId:'DG-1',title:'Generator weekly test run (15 min on load)',everyDays:7,nextDue:D+'24',active:true,lastDone:D+'17'},
    {id:'PM-02',assetId:'FP-1',title:'Fire pump weekly run test',everyDays:7,nextDue:D+'24',active:true,lastDone:D+'19'},
    {id:'PM-03',assetId:'AC-101',title:'AC filter cleaning',everyDays:30,nextDue:D+'25',active:true,lastDone:'2026-09-25'},
    {id:'PM-04',assetId:'GY-106',title:'Geyser descaling & anode check',everyDays:180,nextDue:D+'26',active:true,lastDone:'2026-04-29'},
    {id:'PM-05',assetId:'LFT-1',title:'Lift monthly AMC service visit',everyDays:30,nextDue:D+'28',active:true,lastDone:'2026-09-28'},
    {id:'PM-06',assetId:'RO-1',title:'RO plant membrane & filter check',everyDays:90,nextDue:'2026-11-15',active:true,lastDone:'2026-08-17'},
    {id:'PM-07',assetId:'AC-LOB',title:'Lobby AC coil cleaning',everyDays:90,nextDue:'2026-12-01',active:true,lastDone:'2026-09-02'}];
  const h=(at:string,by:string,action:string,note?:string)=>({at,by,action,...(note?{note}:{})});
  const orders:MNTWorkOrder[]=[
    {id:'WO-0901',kind:'Corrective',title:'AC not cooling — capacitor failed',priority:'P2',status:'On hold',room:'205',assetId:'AC-205',assignee:'maint',
      createdAt:D+'22T19:10',createdBy:'supervisor',startedAt:D+'23T10:00',holdReason:'Capacitor ordered from Sheetal Cooling',awaitingSpare:'SP-03',
      parts:[{spareId:'SP-02',qty:1,unitCost:180000}],
      history:[h(D+'22T19:10','Neha','Created'),h(D+'23T08:40','Neha','Assigned','Joseph'),h(D+'23T10:00','Joseph','Started'),h(D+'23T10:40','Joseph','Part used','1 × R32 gas refill'),h(D+'23T11:05','Joseph','On hold','Capacitor ordered from Sheetal Cooling')]},
    {id:'WO-0902',kind:'Corrective',title:'Lift door sensor intermittent — doors reopen',priority:'P1',status:'In progress',area:'Lobby',assetId:'LFT-1',assignee:'maint',
      createdAt:D+'24T07:20',createdBy:'manager',startedAt:D+'24T07:40',parts:[],
      history:[h(D+'24T07:20','Meera','Created'),h(D+'24T07:22','Meera','Assigned','Joseph'),h(D+'24T07:40','Joseph','Started')]},
    {id:'WO-0903',kind:'Corrective',title:'Geyser not heating',priority:'P3',status:'Open',room:'106',assetId:'GY-106',
      createdAt:D+'23T21:30',createdBy:'reception',parts:[],history:[h(D+'23T21:30','Aarav','Created','Guest complaint at check-out')]},
    {id:'WO-0904',kind:'Corrective',title:'TV shows “No signal”',priority:'P3',status:'Done',room:'103',assetId:'TV-103',assignee:'maint',
      createdAt:D+'23T18:00',createdBy:'supervisor',startedAt:D+'24T07:30',doneAt:D+'24T07:55',doneBy:'maint',note:'HDMI cable reseated; set-top box reset; channels checked',parts:[],
      history:[h(D+'23T18:00','Neha','Created'),h(D+'24T07:30','Joseph','Started'),h(D+'24T07:55','Joseph','Completed','HDMI cable reseated; set-top box reset; channels checked')]},
    {id:'WO-0905',kind:'Corrective',title:'Bathroom exhaust fan noisy',priority:'P4',status:'Verified',room:'104',
      createdAt:D+'20T10:00',createdBy:'supervisor',assignee:'maint',startedAt:D+'20T13:00',doneAt:D+'20T15:00',doneBy:'maint',verifiedAt:D+'20T17:00',verifiedBy:'supervisor',note:'Fan blade cleaned and bearing oiled',parts:[],
      history:[h(D+'20T10:00','Neha','Created'),h(D+'20T13:00','Joseph','Started'),h(D+'20T15:00','Joseph','Completed'),h(D+'20T17:00','Neha','Verified')]},
    {id:'WO-0906',kind:'Corrective',title:'AC dripping water on carpet',priority:'P2',status:'Verified',room:'203',assetId:'AC-203',
      createdAt:D+'21T09:00',createdBy:'reception',assignee:'maint',startedAt:D+'21T10:00',doneAt:D+'21T13:30',doneBy:'maint',verifiedAt:D+'21T14:00',verifiedBy:'supervisor',note:'Drain pipe replaced and sloped',
      parts:[{spareId:'SP-08',qty:1,unitCost:6000}],downtimeFrom:D+'21T09:30',downtimeTo:D+'21T14:00',
      history:[h(D+'21T09:00','Aarav','Created'),h(D+'21T09:30','Neha','Room blocked'),h(D+'21T10:00','Joseph','Started'),h(D+'21T13:30','Joseph','Completed'),h(D+'21T14:00','Neha','Verified','room 203 unblocked')]},
    {id:'WO-0907',kind:'Preventive',title:'Generator weekly test run (15 min on load)',priority:'P3',status:'Verified',area:'Basement',assetId:'DG-1',vendorId:'VEN-03',planId:'PM-01',dueDate:D+'17',
      createdAt:D+'16T09:00',createdBy:'supervisor',startedAt:D+'17T10:00',doneAt:D+'17T10:30',doneBy:'maint',verifiedAt:D+'17T12:00',verifiedBy:'supervisor',note:'Ran 15 min on load; voltage and frequency normal',parts:[],
      history:[h(D+'16T09:00','Neha','Generated from PM plan','PM-01 · due 2026-10-17'),h(D+'17T10:30','Joseph','Completed'),h(D+'17T12:00','Neha','Verified')]},
    {id:'WO-0908',kind:'Preventive',title:'Fire pump weekly run test',priority:'P3',status:'Verified',area:'Basement',assetId:'FP-1',assignee:'maint',planId:'PM-02',dueDate:D+'17',
      createdAt:D+'16T09:00',createdBy:'supervisor',startedAt:D+'19T09:00',doneAt:D+'19T09:20',doneBy:'maint',verifiedAt:D+'19T11:00',verifiedBy:'supervisor',note:'Done 2 days late — pump room key was missing',parts:[],
      history:[h(D+'16T09:00','Neha','Generated from PM plan','PM-02 · due 2026-10-17'),h(D+'19T09:20','Joseph','Completed','Done 2 days late — pump room key was missing'),h(D+'19T11:00','Neha','Verified')]},
  ];
  return {assets,vendors,amcs,spares,plans,orders};
}
export function extendBase(s:State):void{s.maintenanceX=seed();}
/** S1 empty hotel: no work orders (the register, spares and plans remain). */
export const scenarioPatches:Record<string,(s:State)=>void>={S1:s=>{if(s.maintenanceX)s.maintenanceX.orders=[];}};
export const scenarioInfo:ScenarioInfo[]=[];

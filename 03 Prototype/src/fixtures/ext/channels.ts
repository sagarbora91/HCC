// Module 21–22 · OTA channels & direct booking — fixture extensions (fictional data only).
// Fictional channels (BookNest, StayHub, TripKart, RoomRaahi) and fictional guests. No bookings or money are added to the base fixture.
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import type {CHNMessage} from '../../domain/streams/channels';

const msg=(id:string,channel:string,kind:CHNMessage['kind'],ref:string,guest:string,roomType:string,arrival:string,departure:string,adults:number,children:number,amount:number,receivedAt:string,extra:Partial<CHNMessage>={}):CHNMessage=>
  ({id,channel,kind,ref,guest,phone:'+91 90000 '+ref.replace(/\D/g,'').slice(-5),email:guest.split(' ')[0].toLowerCase()+'@example.test',roomType,arrival,departure,adults,children,amount,receivedAt,status:'Pending',...extra});

export function extendBase(s:State):void{
  const D='2026-10-24T';
  s.channelsX={
    channels:[
      {id:'booknest',name:'BookNest',status:'Connected',commissionBps:1800,markupBps:1000,mapping:{Classic:'BN-CLS',Deluxe:'BN-DLX',Suite:'BN-STE'},stopSell:[],lastSync:D+'07:45',statusNote:'Simulated connection'},
      {id:'stayhub',name:'StayHub',status:'Connected',commissionBps:1500,markupBps:800,mapping:{Classic:'SH-STD',Deluxe:'SH-DLX'},stopSell:['Deluxe'],lastSync:D+'07:45',statusNote:'Simulated connection'},
      {id:'tripkart',name:'TripKart',status:'Error',commissionBps:1200,markupBps:500,mapping:{Classic:'TK-C1',Deluxe:'TK-D1',Suite:'TK-S1'},stopSell:[],lastSync:D+'06:30',statusNote:'Simulated: channel credentials expired at 06:30 — reconnect to resume'},
      {id:'roomraahi',name:'RoomRaahi',status:'Disconnected',commissionBps:1000,markupBps:0,mapping:{},stopSell:[],statusNote:'Not yet set up'},
    ],
    messages:[
      msg('CHM-F0','booknest','New','BN-88190','Harsh Vora','Deluxe','2026-10-23','2026-10-24',2,0,354000,'2026-10-23T21:10',{status:'Rejected',reason:'Arrival date already passed when received',decidedBy:'manager',decidedAt:'2026-10-23T21:20'}),
      msg('CHM-F1','booknest','New','BN-88231','Kiran Joshi','Deluxe','2026-10-27','2026-10-29',2,0,708000,D+'06:52'),
      msg('CHM-F2','stayhub','New','SH-55102','Pooja Kulkarni','Classic','2026-10-25','2026-10-26',1,0,230000,D+'07:05'),
      msg('CHM-F3','booknest','New','BN-88240','Farah Siddiqui','Suite','2026-10-25','2026-10-27',2,1,1062000,D+'07:18'),
      msg('CHM-F4','tripkart','New','TK-30017','Rohan Pillai','Suite','2026-10-25','2026-10-26',2,0,504000,D+'06:12'),
      msg('CHM-F5','booknest','New','BN-88262','Sameer Qureshi','Classic','2026-10-26','2026-10-28',3,0,472000,D+'07:31'),
      msg('CHM-F6','stayhub','Modify','SH-55102','Pooja Kulkarni','Classic','2026-10-25','2026-10-27',1,0,460000,D+'07:40'),
    ],
    pushes:[
      {id:'CHP-F1',at:D+'07:45',actor:'manager',channel:'booknest',rows:21,closed:0,summary:'21 rate/availability rows · 0 closed'},
      {id:'CHP-F2',at:D+'07:45',actor:'manager',channel:'stayhub',rows:14,closed:7,summary:'14 rate/availability rows · 7 closed · not mapped: Suite'},
    ],
    outbox:[
      msg('CHM-O1','booknest','New','BN-88275','Lakshmi Menon','Suite','2026-10-25','2026-10-26',2,0,531000,''),
      msg('CHM-O2','booknest','Modify','BN-88231','Kiran Joshi','Deluxe','2026-10-27','2026-10-30',2,0,1062000,''),
      msg('CHM-O3','tripkart','Cancel','TK-30017','Rohan Pillai','Suite','2026-10-25','2026-10-26',2,0,0,''),
    ],
    webRequests:[],
  };
}
export const scenarioPatches:Record<string,(s:State)=>void>={};
export const scenarioInfo:ScenarioInfo[]=[];

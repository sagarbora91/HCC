// F12 MEAS · f12-int-cap: the one loader of slice s.measX (MEAS-CONTRACT §2; five capture lanes write the same slice, so it has one owner).
//   server  GET /meas/captures (Hcc.Api MeasCaptureEndpoints, contracts Hcc.Contracts/Meas/MeasCaptureContracts.cs)
// The server masks per caller: Housekeeping / Maintenance get no money (fields left out, DEC-061), training / exit only for HR-001 holders,
// pulse rows never carry identity. Server ids become prototype ids as strings (protoId); times are converted to the hotel clock.
import {protoId,utcToClock} from './map';
import type {SyncArea} from './types';
import type {MeasState,MeasTaskTimes,MeasOffline,MeasAssetLife,MeasPreference,MeasBookingExtra,MeasExit} from '../../domain/streams/measure-types';

type Opt<T>=T|null|undefined;
export type SrvMeasCaptures={
  turnaway:{id:string;at:string;by:string;reason:string;arrival:string;nights:number;roomType:string;rooms:number;quotedRate?:Opt<number>;channel:string;leadDays:number;note?:Opt<string>}[];
  walked:{id:string;at:string;by:string;booking:string;night:string;reason:string;costPaise?:Opt<number>;approvedBy:string}[];
  allowanceReasons:{code:string;label:string;active:boolean}[];
  bookingExtra:{booking:string;bookedType:Opt<string>;upgrade:Opt<string>;upgradeApprover:Opt<string>}[];
  preference:{customer:string;floor:Opt<string>;roomKind:Opt<string>;meal:Opt<string>;pillow:Opt<string>;billToCompany:Opt<string>;doNotDisturb:boolean;note:Opt<string>;
    blacklist?:Opt<{reason:string;approvedBy:string;at:string}>;loyaltyTag:Opt<string>}[];
  taskTimes:{taskId:string;startedAt:Opt<string>;finishedAt:Opt<string>;inspectedAt:Opt<string>;inspectResult:Opt<string>;rejectReason:Opt<string>;attendant:Opt<string>}[];
  offline:{room:string;reason:string;expectedReturn:Opt<string>;workOrder:Opt<string>;note:Opt<string>}[];
  meters:{id:string;name:string;kind:string;unit:string;active:boolean}[];
  readings:{id:string;meter:string;date:string;value:number;by:string;photoRef:Opt<string>}[];
  assetLife:{asset:string;expectedLifeMonths:Opt<number>;conditionGrade:Opt<string>;replacementCostPaise?:Opt<number>}[];
  discards:{id:string;item:string;qty:number;reason:string;date:string;by:string}[];
  surveys:{id:string;at:string;booking?:Opt<string>;roomType:Opt<string>;channel:Opt<string>;overall:number;cleanliness:number;staff:number;value:number;breakfast:Opt<number>;
    recommend:number;comment:Opt<string>;consent:boolean;via:string;enteredBy?:Opt<string>}[];
  reviews:{id:string;month:string;platform:string;count:number;avgScore:number;score12m:Opt<number>;responded:number;themes:Opt<string>}[];
  rootCauseList:{code:string;label:string;active:boolean}[];
  rootCause:{complaint:string;code:string}[];
  training:{id:string;staff:string;course:string;date:string;hours:number;certificateNo:Opt<string>;validTo:Opt<string>;evidenceRef:Opt<string>;mandatory:boolean}[];
  exit:{staff:string;reason:string;exitDate:string}[];
  pulse:{id:string;quarter:string;dept:string;workload:number;respect:number;tools:number;payFairness:number;recommend:number}[];
  events:{id:string;name:string;from:string;to:string;kind:string;impactPct:number;note:Opt<string>}[];
  otb:{id:string;takenOn:string;night:string;rooms:number;revenuePaise?:Opt<number>}[];
  competitors:{id:string;name:string;active:boolean}[];
  rateshop:{id:string;date:string;stayDate:string;competitor:string;roomType:string;ratePaise?:Opt<number>;withBreakfast:boolean;source:string;by:string}[];
  restrictions:{night:string;kind:string;refused:number;reshaped:number}[];
};

/** Copies only the present values (absent / null fields stay absent, as in the prototype records). */
function present<T extends object>(o:Record<string,unknown>):T{
  const out:Record<string,unknown>={};for(const [k,v] of Object.entries(o))if(v!==null&&v!==undefined&&v!=='')out[k]=v;return out as T;
}
const byKey=<R,V>(rows:R[]|undefined,key:(r:R)=>string,val:(r:R)=>V):Record<string,V>=>Object.fromEntries((rows??[]).map(r=>[key(r),val(r)]));

export function measXFromServer(v:SrvMeasCaptures):MeasState{
  return {
    turnaway:(v.turnaway??[]).map(t=>present({id:t.id,at:utcToClock(t.at),by:protoId(t.by),reason:t.reason,arrival:t.arrival,nights:t.nights,roomType:t.roomType,rooms:t.rooms,
      quotedRate:t.quotedRate,channel:t.channel,leadDays:t.leadDays,note:t.note})),
    walked:(v.walked??[]).map(w=>present({id:w.id,at:utcToClock(w.at),by:protoId(w.by),booking:protoId(w.booking),night:w.night,reason:w.reason,costPaise:w.costPaise,approvedBy:protoId(w.approvedBy)})),
    allowanceReasons:(v.allowanceReasons??[]).map(a=>({code:a.code,label:a.label,active:a.active})),
    bookingExtra:byKey(v.bookingExtra,b=>protoId(b.booking),b=>present<MeasBookingExtra>({bookedType:b.bookedType,upgrade:b.upgrade,upgradeApprover:b.upgradeApprover?protoId(b.upgradeApprover):undefined})),
    preference:byKey(v.preference,p=>p.customer,p=>present<MeasPreference>({floor:p.floor,roomKind:p.roomKind,meal:p.meal,pillow:p.pillow,billToCompany:p.billToCompany,
      doNotDisturb:p.doNotDisturb?true:undefined,note:p.note,loyaltyTag:p.loyaltyTag,
      blacklist:p.blacklist?{reason:p.blacklist.reason,approvedBy:protoId(p.blacklist.approvedBy),at:utcToClock(p.blacklist.at)}:undefined})),
    taskTimes:byKey(v.taskTimes,t=>t.taskId,t=>present<MeasTaskTimes>({startedAt:t.startedAt?utcToClock(t.startedAt):undefined,finishedAt:t.finishedAt?utcToClock(t.finishedAt):undefined,
      inspectedAt:t.inspectedAt?utcToClock(t.inspectedAt):undefined,inspectResult:t.inspectResult,rejectReason:t.rejectReason,attendant:t.attendant?protoId(t.attendant):undefined})),
    offline:byKey(v.offline,o=>o.room,o=>present<MeasOffline>({reason:o.reason,expectedReturn:o.expectedReturn,workOrder:o.workOrder,note:o.note})),
    meters:(v.meters??[]).map(m=>({id:m.id,name:m.name,kind:m.kind as never,unit:m.unit,active:m.active})),
    readings:(v.readings??[]).map(r=>present({id:r.id,meter:r.meter,date:r.date,value:Number(r.value),by:protoId(r.by),photoRef:r.photoRef})),
    assetLife:byKey(v.assetLife,a=>a.asset,a=>present<MeasAssetLife>({expectedLifeMonths:a.expectedLifeMonths,conditionGrade:a.conditionGrade,replacementCostPaise:a.replacementCostPaise})),
    discards:(v.discards??[]).map(d=>({id:d.id,item:d.item,qty:d.qty,reason:d.reason,date:d.date,by:protoId(d.by)})),
    surveys:(v.surveys??[]).map(s=>present({id:s.id,at:utcToClock(s.at),booking:s.booking?protoId(s.booking):undefined,roomType:s.roomType,channel:s.channel,overall:s.overall,
      cleanliness:s.cleanliness,staff:s.staff,value:s.value,breakfast:s.breakfast,recommend:s.recommend,comment:s.comment,consent:true,via:s.via,enteredBy:s.enteredBy?protoId(s.enteredBy):undefined})),
    reviews:(v.reviews??[]).map(r=>present({id:r.id,month:r.month,platform:r.platform,count:r.count,avgScore:Number(r.avgScore),score12m:r.score12m===null||r.score12m===undefined?undefined:Number(r.score12m),
      responded:r.responded,themes:r.themes})),
    rootCauseList:(v.rootCauseList??[]).map(r=>({code:r.code,label:r.label,active:r.active})),
    rootCause:byKey(v.rootCause,r=>protoId(r.complaint),r=>r.code),
    training:(v.training??[]).map(t=>present({id:t.id,staff:protoId(t.staff),course:t.course,date:t.date,hours:Number(t.hours),certificateNo:t.certificateNo,validTo:t.validTo,
      evidenceRef:t.evidenceRef,mandatory:t.mandatory?true:undefined})),
    exit:byKey(v.exit,e=>protoId(e.staff),e=>({reason:e.reason,exitDate:e.exitDate}) as MeasExit),
    pulse:(v.pulse??[]).map(p=>({id:p.id,quarter:p.quarter,dept:p.dept,workload:p.workload,respect:p.respect,tools:p.tools,payFairness:p.payFairness,recommend:p.recommend})),
    events:(v.events??[]).map(e=>present({id:e.id,name:e.name,from:e.from,to:e.to,kind:e.kind,impactPct:e.impactPct,note:e.note})),
    otb:(v.otb??[]).map(o=>({id:o.id,takenOn:o.takenOn,night:o.night,rooms:o.rooms,revenuePaise:o.revenuePaise??0})),
    competitors:(v.competitors??[]).map(c=>({id:c.id,name:c.name,active:c.active})),
    rateshop:(v.rateshop??[]).map(r=>({id:r.id,date:r.date,stayDate:r.stayDate,competitor:r.competitor,roomType:r.roomType,ratePaise:r.ratePaise??0,withBreakfast:r.withBreakfast,source:r.source,by:protoId(r.by)})),
    restrictions:(v.restrictions??[]).map(r=>({night:r.night,kind:r.kind as 'MIN_STAY'|'CTA',refused:r.refused,reshaped:r.reshaped})),
  } as MeasState;
}

export const measCaptures:SyncArea={
  id:'meas-captures',
  loaders:[{slice:'measX',kind:'own',reads:['GET /meas/captures'],async load(c){return measXFromServer(await c.api.get<SrvMeasCaptures>('/meas/captures'));}}],
  commands:[],
};

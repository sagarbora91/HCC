// F12 MEAS · meas-cap-hk command handlers (MEAS-CONTRACT §2): MEAS_OFFLINE_SET, MEAS_METER_SAVE, MEAS_READING_ADD, MEAS_ASSETLIFE_SET,
// MEAS_DISCARD_ADD, plus a TASK wrapper that records start / finish / inspect times into measX.taskTimes after the real TASK handler succeeded.
// Every capture first checks measOn (the module + pack + item rule from streams/measure.ts) and refuses MEAS_OFF; idempotent on cmd.key (execute());
// audited by the returned text; dates come from the simulated clock. DEC-061: nothing here shows or accepts money from Housekeeping / Maintenance.
import type {Handler,Kit} from '../commands';
import {HccError} from '../../../domain/errors';
import type {ErrorCode} from '../../../domain/errors';
import type {Role} from '../../../domain/types';
import {measOn} from '../../../domain/streams/measure';
import type {MeasOfflineReason,MeasMeter,MeasAssetLife,MeasState} from '../../../domain/streams/measure-types';
import {measRecordTaskTime,MEAS_OFFLINE_REASONS,MEAS_OFFLINE_LABEL} from '../../../domain/streams/housekeeping';
import {measMeters,measReadingError,MEAS_METER_KINDS,MEAS_METER_UNIT,MEAS_CONDITION_GRADES,mntSlice} from '../../../domain/streams/maintenance';
import {handlers as hkBase} from './housekeeping';

const ISO=/^\d{4}-\d{2}-\d{2}$/;
const text=(v:unknown)=>String(v??'').trim();
/** Contract error codes that are not yet in the prototype's ErrorCode union (integration: add them to domain/errors.ts). */
const meas=(code:'MEAS_OFF'|'MEAS_READING_NOT_MONOTONIC',msg:string):never=>{throw new HccError(code as ErrorCode,msg);};
const guard=(k:Kit,key:string)=>{if(!measOn(k.s,key))meas('MEAS_OFF',`This measurement is switched off (${key}). The Owner can switch it on in Administration > Measurement reports.`);};
const roleOk=(k:Kit,roles:Role[],what:string)=>k.assert(roles.includes(k.user.role),`Access denied: ${what}.`,'FORBIDDEN');
const slice=(k:Kit):MeasState=>(k.s.measX=k.s.measX??{});

export const KEY_PRODUCTIVITY='meas.hk.productivity',KEY_OOO='meas.hk.ooo',KEY_UTILITIES='meas.hk.utilities',KEY_ASSETS='meas.hk.assets',KEY_LINEN='meas.hk.linen';

export const handlers:Record<string,Handler>={
  /** Replaces TASK: runs the real handler, then records the times when productivity capture is on (off = skipped, never a failure). */
  TASK:k=>{
    const msg=hkBase.TASK(k);
    const action=String(k.cmd.action);
    const ev=action==='start'?'start':action==='complete'?'finish':action==='inspect'?'inspect':action==='reject'?'reject':null;
    if(ev&&measOn(k.s,KEY_PRODUCTIVITY))measRecordTaskTime(k.s,String(k.cmd.id),ev,{reason:ev==='reject'?text(k.cmd.reason):undefined});
    return msg;
  },

  /** MEAS_OFFLINE_SET{room, reason, expectedReturn?, workOrder?, note?, clear?}: why a room is offline (Manager, Supervisor, Owner). */
  MEAS_OFFLINE_SET:k=>{
    const {s,cmd,assert}=k;
    guard(k,KEY_OOO);roleOk(k,['Owner','Manager','Supervisor'],'only a Manager or Supervisor records why a room is offline');
    const room=s.rooms.find(r=>r.id===cmd.room);assert(room,'Room not found.','NOT_FOUND');
    const x=slice(k),offline={...(x.offline??{})};
    if(cmd.clear===true){assert(offline[room!.id],'No offline reason is recorded for this room.','NOT_FOUND');delete offline[room!.id];x.offline=offline;return `Room ${room!.id} · offline reason cleared`;}
    const reason=text(cmd.reason) as MeasOfflineReason;
    assert(MEAS_OFFLINE_REASONS.includes(reason),'Choose OOO, OOS, house use or renovation.');
    const back=text(cmd.expectedReturn)||undefined;
    assert(!back||(ISO.test(back)&&back>=s.date),'Enter an expected return date that is not in the past.');
    const wo=text(cmd.workOrder)||undefined;
    assert(!wo||mntSlice(s).orders.some(w=>w.id===wo),'Work order not found.','NOT_FOUND');
    const note=text(cmd.note)||undefined;assert(!note||note.length<=200,'Keep the note under 200 characters.');
    offline[room!.id]={reason,...(back?{expectedReturn:back}:{}),...(wo?{workOrder:wo}:{}),...(note?{note}:{})};x.offline=offline;
    return `Room ${room!.id} · ${MEAS_OFFLINE_LABEL[reason]}${back?' · back '+back:''}${wo?' · '+wo:''}`;
  },

  /** MEAS_METER_SAVE{id?, name, kind, unit?, active?}: the meter master (Owner, Manager). The first save keeps the four default meters. */
  MEAS_METER_SAVE:k=>{
    const {s,cmd,assert}=k;
    guard(k,KEY_UTILITIES);roleOk(k,['Owner','Manager'],'only the Owner or Manager edits the meter master');
    const name=text(cmd.name);assert(name.length>=2&&name.length<=60,'Enter a meter name (2 to 60 characters).');
    const kind=text(cmd.kind) as MeasMeter['kind'];assert(MEAS_METER_KINDS.includes(kind),'Choose a meter type.');
    const unit=text(cmd.unit)||MEAS_METER_UNIT[kind];assert(unit.length<=16,'Keep the unit short.');
    const list=[...measMeters(s)],x=slice(k);
    if(cmd.id){
      const i=list.findIndex(m=>m.id===cmd.id);assert(i>=0,'Meter not found.','NOT_FOUND');
      assert(list[i].kind===kind||!(x.readings??[]).some(r=>r.meter===list[i].id),'A meter with readings keeps its type.','INVALID_TRANSITION');
      list[i]={...list[i],name,kind,unit,active:cmd.active!==false};x.meters=list;return `${list[i].id} · ${name} saved`;
    }
    assert(!list.some(m=>m.name.toLowerCase()===name.toLowerCase()),'A meter with this name already exists.');
    const m:MeasMeter={id:k.id('MTR'),name,kind,unit,active:cmd.active!==false};list.push(m);x.meters=list;return `${m.id} · ${name} added`;
  },

  /** MEAS_READING_ADD{meter, value, date?, photoRef?}: PERM-MEAS-002 (Owner, Manager, Maintenance). Monotonic per meter, photo optional. */
  MEAS_READING_ADD:k=>{
    const {s,uid,cmd,assert}=k;
    guard(k,KEY_UTILITIES);roleOk(k,['Owner','Manager','Maintenance'],'PERM-MEAS-002 is required to enter meter readings');
    const date=text(cmd.date)||s.date,raw=cmd.value,value=typeof raw==='number'?raw:text(raw)===''?NaN:Number(text(raw));
    const err=measReadingError(s,text(cmd.meter),date,value);
    if(err){if(err.code==='MEAS_READING_NOT_MONOTONIC')meas('MEAS_READING_NOT_MONOTONIC',err.message);assert(false,err.message);}
    const photoRef=text(cmd.photoRef)||undefined;assert(!photoRef||photoRef.length<=200,'The photo reference is too long.');
    const x=slice(k),r={id:k.id('MR'),meter:text(cmd.meter),date,value,by:uid,...(photoRef?{photoRef}:{})};
    x.readings=[...(x.readings??[]),r];
    const m=measMeters(s).find(m=>m.id===r.meter)!;
    return `${m.name} · ${value} ${m.unit} on ${date}`;
  },

  /** MEAS_ASSETLIFE_SET{asset, expectedLifeMonths?, conditionGrade?, replacementCostPaise?}: Owner, Accountant (money is integer paise). */
  MEAS_ASSETLIFE_SET:k=>{
    const {s,cmd,assert}=k;
    guard(k,KEY_ASSETS);roleOk(k,['Owner','Accountant'],'only the Owner or Accountant records asset life and replacement cost');
    const asset=mntSlice(s).assets.find(a=>a.id===cmd.asset);assert(asset,'Asset not found.','NOT_FOUND');
    const v:MeasAssetLife={};
    if(cmd.expectedLifeMonths!==undefined&&cmd.expectedLifeMonths!==''){const n=Number(cmd.expectedLifeMonths);assert(Number.isInteger(n)&&n>=1&&n<=600,'Expected life is 1 to 600 months.');v.expectedLifeMonths=n;}
    if(cmd.conditionGrade){assert(MEAS_CONDITION_GRADES.includes(cmd.conditionGrade),'Condition grade is A, B, C or D.');v.conditionGrade=cmd.conditionGrade;}
    if(cmd.replacementCostPaise!==undefined&&cmd.replacementCostPaise!==''){const n=Number(cmd.replacementCostPaise);assert(Number.isSafeInteger(n)&&n>=0&&n<=100000000000,'Enter the replacement cost in whole paise.');v.replacementCostPaise=n;}
    assert(Object.keys(v).length>0,'Enter at least one of expected life, condition grade or replacement cost.');
    const x=slice(k);x.assetLife={...(x.assetLife??{}),[asset!.id]:{...(x.assetLife?.[asset!.id]??{}),...v}};
    return `${asset!.name} (${asset!.id}) · life / condition saved`;
  },

  /** MEAS_DISCARD_ADD{item, qty, reason, date?}: linen discard (Supervisor, Manager, Owner). No money. */
  MEAS_DISCARD_ADD:k=>{
    const {s,uid,cmd,assert}=k;
    guard(k,KEY_LINEN);roleOk(k,['Owner','Manager','Supervisor'],'only a Supervisor or Manager records linen discards');
    const item=text(cmd.item);assert(item.length>=2&&item.length<=60,'Enter the item (2 to 60 characters).');
    const qty=Number(cmd.qty);assert(Number.isInteger(qty)&&qty>=1&&qty<=1000,'Enter a quantity from 1 to 1000.');
    const reason=text(cmd.reason);assert(reason.length>=3&&reason.length<=60,'Choose or enter a reason.');
    const date=text(cmd.date)||s.date;assert(ISO.test(date)&&date<=s.date,'Enter a valid date that is not in the future.');
    const x=slice(k);x.discards=[...(x.discards??[]),{id:k.id('DSC'),item,qty,reason,date,by:uid}];
    return `${qty} × ${item} discarded · ${reason}`;
  },
};

// F12 MEAS · meas-cap-hk in the production sync layer (MEAS-CONTRACT §2, server mirror schema `meas`: task_times, room_offline, meter, meter_reading,
// asset_life, discard; migration 4815). Wired by f12-int-cap to the server capture module (Hcc.Api MeasCaptureEndpoints, rules Hcc.Operations/Meas/HkRules):
//   POST /commands/MeasOfflineSet | MeasMeterSave | MeasReadingAdd | MeasAssetLifeSet | MeasDiscardAdd   (each behind RequireHccMeasurement)
// s.measX is loaded by meas-captures.ts; every mapper reloads it. TASK is already mapped by the housekeeping area; the server-side task-time hook is
// still owed (MEAS-CAP-HK-DONE open items), so meas.task_times is read but not yet written by the server.
import type {SyncArea} from './types';

const text=(v:unknown)=>String(v??'').trim()||undefined;
const num=(v:unknown)=>v===undefined||v===null||v===''?undefined:Number(v);

export const measHk:SyncArea={
  id:'meas-hk',
  loaders:[],
  commands:[
    {type:'MEAS_OFFLINE_SET',reload:['measX'],async send(c){const k=c.cmd;
      await c.api.command('MeasOfflineSet',{room:text(k.room),reason:text(k.reason),expectedReturn:text(k.expectedReturn),workOrder:text(k.workOrder),note:text(k.note),clear:k.clear===true?true:undefined});}},
    {type:'MEAS_METER_SAVE',reload:['measX'],async send(c){const k=c.cmd;
      await c.api.command('MeasMeterSave',{id:text(k.id),name:text(k.name),kind:text(k.kind),unit:text(k.unit),active:k.active===undefined?undefined:k.active!==false});}},
    {type:'MEAS_READING_ADD',reload:['measX'],async send(c){const k=c.cmd;
      await c.api.command('MeasReadingAdd',{meter:text(k.meter),value:num(k.value),date:text(k.date),photoRef:text(k.photoRef)});}},
    {type:'MEAS_ASSETLIFE_SET',reload:['measX'],async send(c){const k=c.cmd;
      await c.api.command('MeasAssetLifeSet',{asset:text(k.asset),expectedLifeMonths:num(k.expectedLifeMonths),conditionGrade:text(k.conditionGrade),replacementCostPaise:num(k.replacementCostPaise)});}},
    {type:'MEAS_DISCARD_ADD',reload:['measX'],async send(c){const k=c.cmd;
      await c.api.command('MeasDiscardAdd',{item:text(k.item),qty:num(k.qty),reason:text(k.reason),date:text(k.date)});}},
  ],
};

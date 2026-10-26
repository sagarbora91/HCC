// MA-7 · Operations masters of the production sync layer: housekeeping task types and zones (server module OpsMasters, tables
// ops.hk_task_type / ops.hk_zone) and complaint / lost-and-found categories (gsv.complaint_category / gsv.lostfound_category).
// They ride on the masters area (sync/masters.ts): the five generic MST_MASTER_* commands map to the per-master server commands by the
// route table below; only the wire shape of each list and the extra fields of the save body live here.
//
//   server   GET /housekeeping/task-types, /housekeeping/zones, /guest-services/complaint-categories, /guest-services/lost-found-categories
//            (any signed-in user) + each list's /history (PERM-MST-001); POST /commands/{Save,Retire,Reactivate,Delete}{HkTaskType|HkZone|
//            ComplaintCategory|LostFoundCategory} and Reorder{HkTaskTypes|HkZones|ComplaintCategories|LostFoundCategories}: bodies
//            {id,…,expectedVersion} / {ids}. Master errors keep details.reason (MASTER_*) for the screens.
//   rows     prototype row ids = String(server id); lists travel as arrays on the wire (kinds, floors, rooms, checklist items) and are
//            kept as the prototype row shapes (checklist = one item per line, floors / rooms = code lists).
import type {MasterRow} from '../../domain/streams/masters';
import type {MstServerRoute} from './masters';
import {serverId,protoId,protoUserId} from './map';

type Wire={id:number;code:string;name:string;sortOrder:number;active:boolean;retiredOn:string|null;used:number;updatedUtc:string;version:string;[k:string]:unknown};
const base=(w:Wire):MasterRow=>{const r:MasterRow={id:protoId(w.id),code:w.code,name:w.name,sortOrder:w.sortOrder,active:w.active};if(w.retiredOn)r.retiredOn=w.retiredOn;return r;};
const strs=(v:unknown)=>Array.isArray(v)?v.map(String):[];
const items=(text:unknown)=>String(text??'').split(/\r?\n/).map(x=>x.trim()).filter(Boolean);

const taskTypeFromWire=(w:Wire):MasterRow=>({...base(w),appliesTo:strs(w.appliesTo),standardMinutes:Number(w.standardMinutes),needsInspection:!!w.needsInspection,checklist:strs(w.checklist).join('\n')});
const zoneFromWire=(w:Wire):MasterRow=>({...base(w),floors:strs(w.floors),rooms:strs(w.rooms),...(w.defaultAttendantId?{defaultAttendant:protoUserId(Number(w.defaultAttendantId))}:{})});
const complaintFromWire=(w:Wire):MasterRow=>({...base(w),department:String(w.department),slaHours:Number(w.slaHours)});
const lostFoundFromWire=(w:Wire):MasterRow=>({...base(w),department:String(w.department),slaHours:Number(w.slaHours),valuable:!!w.valuable});

const num=(v:unknown)=>v===undefined||v===null||v===''?undefined:Number(v);
const taskTypeBody=(r:Record<string,unknown>)=>({appliesTo:strs(r.appliesTo),standardMinutes:num(r.standardMinutes),needsInspection:!!r.needsInspection,checklist:items(r.checklist)});
const zoneBody=(r:Record<string,unknown>)=>({floors:strs(r.floors),rooms:strs(r.rooms),defaultAttendantId:r.defaultAttendant?serverId(String(r.defaultAttendant),'User'):undefined});
const complaintBody=(r:Record<string,unknown>)=>({department:r.department===undefined?undefined:String(r.department),slaHours:num(r.slaHours)});
const lostFoundBody=(r:Record<string,unknown>)=>({...complaintBody(r),valuable:!!r.valuable});

/** One entry per operations master; merged into MST_SERVER (sync/masters.ts). */
export const OPS_MST_SERVER:Record<string,MstServerRoute>={
  hkTaskTypes:{list:'GET /housekeeping/task-types',history:'GET /housekeeping/task-types/history',listPath:'/housekeeping/task-types',historyPath:'/housekeeping/task-types/history',kind:'hkTaskType',idField:'id',idsField:'ids',
    save:'SaveHkTaskType',retire:'RetireHkTaskType',reactivate:'ReactivateHkTaskType',reorder:'ReorderHkTaskTypes',remove:'DeleteHkTaskType',fromWire:taskTypeFromWire,toBody:taskTypeBody},
  hkZones:{list:'GET /housekeeping/zones',history:'GET /housekeeping/zones/history',listPath:'/housekeeping/zones',historyPath:'/housekeeping/zones/history',kind:'hkZone',idField:'id',idsField:'ids',
    save:'SaveHkZone',retire:'RetireHkZone',reactivate:'ReactivateHkZone',reorder:'ReorderHkZones',remove:'DeleteHkZone',fromWire:zoneFromWire,toBody:zoneBody},
  complaintCategories:{list:'GET /guest-services/complaint-categories',history:'GET /guest-services/complaint-categories/history',listPath:'/guest-services/complaint-categories',
    historyPath:'/guest-services/complaint-categories/history',kind:'complaintCategory',idField:'id',idsField:'ids',
    save:'SaveComplaintCategory',retire:'RetireComplaintCategory',reactivate:'ReactivateComplaintCategory',reorder:'ReorderComplaintCategories',remove:'DeleteComplaintCategory',fromWire:complaintFromWire,toBody:complaintBody},
  lostFoundCategories:{list:'GET /guest-services/lost-found-categories',history:'GET /guest-services/lost-found-categories/history',listPath:'/guest-services/lost-found-categories',
    historyPath:'/guest-services/lost-found-categories/history',kind:'lostFoundCategory',idField:'id',idsField:'ids',
    save:'SaveLostFoundCategory',retire:'RetireLostFoundCategory',reactivate:'ReactivateLostFoundCategory',reorder:'ReorderLostFoundCategories',remove:'DeleteLostFoundCategory',fromWire:lostFoundFromWire,toBody:lostFoundBody},
};

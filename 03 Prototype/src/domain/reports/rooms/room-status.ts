// F7-R3 · rpt.rooms.room-status — Room status today (catalogue A26 / A29; the live room board as a report). D-NOW: no date, no history. Money flag n.
// Desk right (rule V.5): PERM-HK-001 (housekeeping board) or PERM-STY-001 (the check-in desk). Guest names only for those holding PERM-RES-001
// (Owner, Manager, Reception): everyone else sees an empty Guest cell, not a masked one.
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {roomStatus,occupant} from '../../selectors';
import {mstFloorLabel} from '../../streams/masters';
import {paxOf,rptNamesVisible} from '../guests/shared';

const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
export const ROOM_FILTERS=['all','vacant','occupied','dirty','out of order','due out'];
export const ROOM_STATUS_COLS:RptColumn[]=[T('room','Room'),T('type','Type'),T('floor','Floor'),T('status','Status'),T('hk','Housekeeping status'),T('guest','Guest','guestName'),
  {key:'pax',label:'Pax',type:'int',money:false},{key:'checkIn',label:'Check-in',type:'date',money:false},{key:'checkOut',label:'Expected check-out',type:'date',money:false},
  {key:'nextArrival',label:'Next arrival',type:'date',money:false},T('notes','Notes')];

export const roomStatusReport=registerReport({
  id:'rpt.rooms.room-status',title:'Room status today',area:'rooms',priority:'P1',status:'built',
  moneyFlag:false,viewPerms:['PERM-RPT-001'],deskPerms:['PERM-HK-001','PERM-STY-001'],ownShift:true,dateClass:'D-NOW',
  params:[{key:'filter',label:'Filter',type:'enum',options:ROOM_FILTERS,default:'all'}],
  columns:ROOM_STATUS_COLS,
  dateBasis:'The state at this moment (business date, no history). Out of order = a block or a group hold tonight. Next arrival = the earliest booked arrival from today.',
  formats:['screen','xlsx','pdf'],phone:'yes',legacyIds:['A26','A29'],
  select:(s,uid,p)=>{
    const names=rptNamesVisible(s,uid),f=p.filter??'all';
    const all=s.rooms.map(r=>{const occ=occupant(s,r.id),st=roomStatus(s,r);
      const status=st==='Blocked'?'Out of order':occ?'Occupied':'Vacant';
      const next=s.bookings.filter(b=>b.room===r.id&&(b.status==='Confirmed'||b.status==='Held')&&b.arrival>=s.date).map(b=>b.arrival).sort()[0];
      return {r,occ,status,next};});
    const keep=all.filter(({r,occ,status})=>f==='all'||(f==='vacant'&&status==='Vacant')||(f==='occupied'&&status==='Occupied')||(f==='dirty'&&r.hk==='Dirty')||(f==='out of order'&&status==='Out of order')||(f==='due out'&&!!occ&&occ.departure<=s.date))
      .sort((a,b)=>a.r.id.localeCompare(b.r.id,undefined,{numeric:true}));
    const rows:RptRow[]=keep.map(({r,occ,status,next})=>({kind:'row',cells:{room:r.id,type:r.type,floor:mstFloorLabel(s,r),status,hk:r.hk,guest:occ&&names?occ.guest:'',pax:occ?paxOf(occ):null,
      checkIn:occ?occ.arrival:null,checkOut:occ?occ.departure:null,nextArrival:next??null,notes:status==='Out of order'?(r.blockReason??'Held for a group'):''}}));
    const n=(x:string)=>keep.filter(k=>k.status===x).length;
    const ins=s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival===s.date).length,outs=s.bookings.filter(b=>b.status==='In house'&&b.departure===s.date).length;
    rows.push({kind:'total',cells:{room:`${keep.length} room${keep.length===1?'':'s'}`,status:`Occupied ${n('Occupied')} · Vacant ${n('Vacant')} · Out of order ${n('Out of order')}`,notes:`Check-ins today ${ins} · Check-outs today ${outs}`}});
    return {params:p,columns:ROOM_STATUS_COLS,rows};
  },
});

// F12 MEAS · cap-desk command handlers (MEAS-CONTRACT §2). Command types start with 'MEAS_'. Each command first checks the switch and refuses
// MEAS_OFF; idempotent on the client key (execute); audited through the returned detail text. Folio items UPG / LCO1 / LCO2 go through the
// existing CHARGE handler only (no folio money logic here).
import type {Handler,Kit} from '../commands';
import {HccError} from '../../../domain/errors';
import type {MeasState,TurnawayReason} from '../../../domain/streams/measure-types';
import {MD_KEY,MD_ERR_OFF,MD_ITEMS,TURNAWAY_REASONS,mdOn,mdLeadDays,mdAllowanceReasons,mdLateCheckoutItem,type MdItemCode} from '../../../domain/streams/turnaway';
import {customerIndex} from '../../../domain/streams/customers';
import {handlers as folio} from './folio';
import {money} from '../../../domain/money';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
const slice=(k:Kit):MeasState=>{const s=k.s;if(!s.measX)s.measX={};return s.measX;};
/** Refuse with MEAS_OFF while the capture's switch is off (data kept). The code stays FORBIDDEN (ErrorCode has no MEAS_OFF; the integrator may add it); details.reason carries MEAS_OFF. */
function guard(k:Kit,key:string,what:string){
  if(!mdOn(k.s,key))throw Object.assign(new HccError('FORBIDDEN',`${MD_ERR_OFF}: ${what} is switched off in Measurement reports settings.`),{details:{reason:MD_ERR_OFF,key}});
}
const role=(k:Kit)=>k.user.role;
const need=(k:Kit,roles:string[],msg:string)=>{if(!roles.includes(role(k)))k.fail('FORBIDDEN',msg);};
const text=(v:unknown)=>String(v??'').trim();

const MEAS_TURNAWAY_LOG:Handler=k=>{
  guard(k,MD_KEY.turnaway,'The turn-away log');
  need(k,['Reception','Manager','Owner'],'Access denied: turn-aways are logged by Reception, a Manager or the Owner.');
  const {cmd,assert}=k;
  assert(TURNAWAY_REASONS.some(r=>r.id===cmd.reason),'Choose why the guest could not book.');
  assert(typeof cmd.arrival==='string'&&DATE.test(cmd.arrival),'Choose the arrival date.');
  assert(Number.isInteger(cmd.nights)&&cmd.nights>=1&&cmd.nights<=30,'Nights must be 1–30.');
  assert(text(cmd.roomType),'Choose the room type asked for.');
  const rooms=cmd.rooms===undefined?1:cmd.rooms;assert(Number.isInteger(rooms)&&rooms>=1&&rooms<=20,'Rooms must be 1–20.');
  assert(cmd.quotedRate===undefined||(Number.isSafeInteger(cmd.quotedRate)&&cmd.quotedRate>=0&&cmd.quotedRate<=100000000),'Quoted rate must be an amount in paise.');
  assert(text(cmd.channel),'Choose the channel.');
  assert(cmd.reason!=='other'||text(cmd.note),'Write a short note for "Other".');
  const id=k.id('TRN');
  const row={id,at:k.s.clock,by:k.uid,reason:cmd.reason as TurnawayReason,arrival:cmd.arrival as string,nights:cmd.nights as number,roomType:text(cmd.roomType),rooms:rooms as number,
    ...(cmd.quotedRate!==undefined?{quotedRate:cmd.quotedRate as number}:{}),channel:text(cmd.channel),leadDays:mdLeadDays(k.s.date,cmd.arrival),...(text(cmd.note)?{note:text(cmd.note)}:{})};
  (slice(k).turnaway??=[]).push(row);
  return `${id} · ${row.roomType} × ${rooms} · ${row.nights} night(s) from ${row.arrival} · ${row.reason}${row.quotedRate!==undefined?' · quoted '+money(row.quotedRate):''}`;
};

const MEAS_WALKED_LOG:Handler=k=>{
  guard(k,MD_KEY.walked,'The walked-guest log');
  need(k,['Manager','Owner'],'Access denied: a walked guest is logged by a Manager or the Owner.');
  const {cmd,assert,s}=k;
  const b=s.bookings.find(x=>x.id===cmd.booking);assert(b,'Booking not found.','NOT_FOUND');
  assert(typeof cmd.night==='string'&&DATE.test(cmd.night),'Choose the night.');
  assert(text(cmd.reason).length>=3,'Write why the guest was walked.');
  assert(cmd.costPaise===undefined||(Number.isSafeInteger(cmd.costPaise)&&cmd.costPaise>=0&&cmd.costPaise<=100000000),'Cost must be an amount in paise.');
  const id=k.id('WLK');
  (slice(k).walked??=[]).push({id,at:s.clock,by:k.uid,booking:b!.id,night:cmd.night,reason:text(cmd.reason),...(cmd.costPaise!==undefined?{costPaise:cmd.costPaise}:{}),approvedBy:k.uid});
  return `${id} · ${b!.id} walked on ${cmd.night} · ${text(cmd.reason)}${cmd.costPaise?' · cost '+money(cmd.costPaise):''}`;
};

const MEAS_ALLOWANCE_REASON_SAVE:Handler=k=>{
  guard(k,MD_KEY.allowance,'Allowance reasons');
  need(k,['Owner'],'Access denied: allowance reasons are an Owner setting.');
  const {cmd,assert}=k;
  const code=text(cmd.code).toUpperCase().replace(/[^A-Z0-9-]+/g,'-');assert(/^[A-Z0-9-]{2,24}$/.test(code),'Give the reason a short code (letters, digits, dash).');
  assert(text(cmd.label).length>=2&&text(cmd.label).length<=60,'Name the reason (2–60 letters).');
  const m=slice(k);const list=m.allowanceReasons=[...mdAllowanceReasons(k.s)];
  const i=list.findIndex(r=>r.code===code);const active=cmd.active!==false;
  if(i>=0){assert(list[i].label!==text(cmd.label)||list[i].active!==active,'No change.');list[i]={code,label:text(cmd.label),active};return `Allowance reason ${code} updated · ${text(cmd.label)}${active?'':' · retired'}`;}
  list.push({code,label:text(cmd.label),active});return `Allowance reason ${code} added · ${text(cmd.label)}`;
};

/** An enabled user other than the actor with one of the roles (an approver). */
const approverOf=(k:Kit,id:unknown,roles:string[]):string|undefined=>{
  const u=k.s.users.find(x=>x.id===id);
  return u&&u.enabled&&u.id!==k.uid&&roles.includes(u.role)?u.id:undefined;
};

const MEAS_BOOKING_EXTRA_SET:Handler=k=>{
  guard(k,MD_KEY.extra,'Upsell capture');
  need(k,['Reception','Manager','Owner'],'Access denied: upgrades and late check-out are recorded by Reception, a Manager or the Owner.');
  const {cmd,assert,s}=k;const x=k.b();
  const m=slice(k);m.bookingExtra??={};
  const cur={...(m.bookingExtra[x.id]??{})};const notes:string[]=[];
  if(cmd.bookedType!==undefined){
    assert(s.rooms.some(r=>r.type===cmd.bookedType),'Unknown room type.');
    assert(!cur.bookedType||cur.bookedType===cmd.bookedType,'The booked room type is already recorded and is kept.');
    cur.bookedType=cmd.bookedType;notes.push('booked type '+cmd.bookedType);
  }else if(!cur.bookedType){
    const t=s.rooms.find(r=>r.id===x.room)?.type;if(t)cur.bookedType=t; // booked type kept even when the assigned room later differs
  }
  const item=cmd.item as MdItemCode|undefined;
  if(item!==undefined){
    assert(item in MD_ITEMS,'Unknown upsell item.');
    if(item==='UPG'){
      assert(cmd.upgrade==='paid'||cmd.upgrade==='free','Say whether the upgrade is paid or free.');
      const t=s.rooms.find(r=>r.id===x.room)?.type;assert(cur.bookedType&&t&&cur.bookedType!==t,'The assigned room type is the same as the booked type: nothing to upgrade.');
      if(cmd.upgrade==='paid'){
        assert(Number.isSafeInteger(cmd.amount)&&cmd.amount>0,'Enter the upgrade charge in paise.');
        notes.push(folio.CHARGE({...k,cmd:{...cmd,type:'CHARGE',id:x.id,label:`${MD_ITEMS.UPG.code} · ${MD_ITEMS.UPG.label} ${cur.bookedType} to ${t}`,amount:cmd.amount,gst:'other'}}));
        cur.upgrade='paid';
      }else{
        const ap=role(k)==='Manager'||role(k)==='Owner'?k.uid:approverOf(k,cmd.approver,['Manager','Owner']);
        assert(ap,'A free upgrade needs a Manager or the Owner as approver.');
        cur.upgrade='free';cur.upgradeApprover=ap;notes.push('free upgrade approved by '+ap);
      }
    }else{
      const def=MD_ITEMS[item];
      const hh=typeof cmd.time==='string'?cmd.time:s.clock.slice(11,16);
      assert(mdLateCheckoutItem(hh)===item,item==='LCO1'?'Late check-out item LCO1 applies from 14:00 up to 18:00.':'Late check-out item LCO2 applies from 18:00.');
      if(cmd.waive===true){
        assert(role(k)==='Manager'||role(k)==='Owner','Waiving a late check-out charge needs a Manager.');
        assert(text(cmd.reason).length>=3,'Write the reason for the waiver.');
        notes.push(`${item} waived by ${k.uid}: ${text(cmd.reason)}`);
      }else{
        notes.push(folio.CHARGE({...k,cmd:{...cmd,type:'CHARGE',id:x.id,label:`${item} · ${def.label}`,amount:(def as {amount:number}).amount,gst:'other'}}));
      }
    }
  }
  assert(notes.length>0,'Nothing to record.');
  m.bookingExtra[x.id]=cur;
  return `${x.id} · ${notes.join(' · ')}`;
};

const MEAS_PREFERENCE_SET:Handler=k=>{
  guard(k,MD_KEY.preference,'Guest preferences');
  need(k,['Reception','Manager','Owner'],'Access denied: preferences are kept by Reception, a Manager or the Owner.');
  const {cmd,assert,s}=k;
  const c=customerIndex(s).find(c=>c.key===cmd.customer);assert(c,'Customer not found.','NOT_FOUND');
  const m=slice(k);m.preference??={};const cur={...(m.preference[c!.key]??{})};const notes:string[]=[];
  for(const f of ['floor','roomKind','meal','pillow','billToCompany','note'] as const){
    if(cmd[f]===undefined)continue;
    const v=text(cmd[f]);assert(v.length<=80,'Keep each preference under 80 letters.');
    if(v)cur[f]=v;else delete cur[f];notes.push(f);
  }
  if(cmd.doNotDisturb!==undefined){if(cmd.doNotDisturb)cur.doNotDisturb=true;else delete cur.doNotDisturb;notes.push('doNotDisturb');}
  if(cmd.blacklist!==undefined){
    need(k,['Manager','Owner'],'Access denied: only a Manager or the Owner can blacklist or clear a guest.');
    if(cmd.blacklist===null||cmd.blacklist===false){
      assert(cur.blacklist,'This guest is not blacklisted.');assert(text(cmd.reason).length>=3,'Write why the blacklist is cleared.');
      delete cur.blacklist;notes.push('blacklist cleared: '+text(cmd.reason));
    }else{
      assert(text(cmd.blacklist).length>=3,'Write the reason for the blacklist.');
      const ap=role(k)==='Owner'?k.uid:approverOf(k,cmd.approvedBy,['Owner']);
      assert(ap,'A Manager needs the Owner’s approval to blacklist a guest (choose the Owner as approver).');
      cur.blacklist={reason:text(cmd.blacklist),approvedBy:ap!,at:s.clock};notes.push(`blacklisted (approved by ${ap})`);
    }
  }
  assert(notes.length>0,'Nothing to record.');
  m.preference[c!.key]=cur;
  return `${c!.name} · ${notes.join(', ')}`;
};

export const handlers:Record<string,Handler>={MEAS_TURNAWAY_LOG,MEAS_WALKED_LOG,MEAS_ALLOWANCE_REASON_SAVE,MEAS_BOOKING_EXTRA_SET,MEAS_PREFERENCE_SET};

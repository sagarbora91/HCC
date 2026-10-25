// Mock command service. Every mutation goes through execute(); it validates permission, scope and state,
// returns a NEW state (input is never mutated) or throws a typed HccError. See HCC_Business_Workflow_Spec_v1.md.
import {clone} from '../../domain/clone';
import {State,Command,Permission,Receipt,Role,Booking,User,roles,permissions,assignedScopeRoles} from '../../domain/types';
import {registry} from './handlers';
import {HccError,ErrorCode} from '../../domain/errors';
import {money,addDays,nights,addMinutes,OPENING_TIME} from '../../domain/money';
import {can,available,occupant,totals,drawer,rateFor} from '../../domain/selectors';
import {gstForRoomNight,gstForCharge,gstCategoryOfLabel,gstForFolioAdjustment} from '../../domain/gst';
import {cfgBeforeCommand} from '../../domain/streams/config';

export function execute(input:State,uid:string,cmd:Command):State{
  const fail=(code:ErrorCode,msg:string):never=>{throw new HccError(code,msg);};
  const assert=(ok:unknown,msg:string,code:ErrorCode='VALIDATION')=>{if(!ok)fail(code,msg);};
  const current=input.users.find(x=>x.id===uid);
  if(!current)fail('FORBIDDEN','Unknown demo user.');
  if(!current!.enabled)fail('FORBIDDEN','This demo user is disabled. Switch to an enabled user.');
  if(current!.expiresOn&&input.date>current!.expiresOn)fail('FORBIDDEN',`This demo user’s access expired on ${current!.expiresOn}. Switch to an active user.`);
  // Idempotency: a key already executed returns the earlier result unchanged (AC-005, AC-022).
  if(cmd.key&&input.processed.includes(cmd.key))return input;
  const s=cowDraft(input),u=s.users.find(x=>x.id===uid);
  const user=u!;
  const req=(p:Permission)=>{if(!can(s,uid,p))fail('FORBIDDEN',`Access denied: ${p} permission is required.`);};
  const id=(prefix:string)=>prefix+'-'+(++s.sequence);
  const b=()=>{const x=s.bookings.find(x=>x.id===cmd.id);assert(x,'Booking not found.','NOT_FOUND');return x!;};
  const positive=(n:number)=>assert(Number.isSafeInteger(n)&&n>0&&n<=100000000,'Enter an amount greater than zero (maximum ₹10,00,000).');
  const open=()=>assert(!s.closedDates.includes(s.date),'This business day is closed. Open the next business day first.','DAY_CLOSED');
  const mutable=(x:Booking)=>assert(x.status==='In house'||x.status==='Confirmed','This booking is no longer open.','INVALID_TRANSITION');
  const financial=(x:Booking)=>{mutable(x);open();};
  const addNight=(x:Booking,date:string)=>{const lid=`night:${x.id}:${date}`;if(!x.lines.some(l=>l.id===lid||l.kind==='room'&&l.date===date))x.lines.push({id:lid,label:'Room night · '+date,amount:x.rate,date,kind:'room',taxBps:gstForRoomNight(s,date,x.rate).bps});};
  // W05-A Master configuration: apply due dated values; refuse commands of a switched-off module (DEC-057).
  {const off=cfgBeforeCommand(s,cmd);if(off)fail('FORBIDDEN',off);}
  let detail='';
  // Stream handlers (services/mock/handlers/*) take precedence: they add new commands or replace an owned one.
  const handler=registry[cmd.type];
  if(handler){detail=handler({s,uid,user,cmd,fail,assert,req,id,b,positive,open,mutable,financial,addNight});}
  else switch(cmd.type){
  case 'BOOK':{
    req('reservations');open();
    assert(cmd.guest?.trim().length>=2,'Enter the guest’s name.');
    assert(/^\d{4}-\d{2}-\d{2}$/.test(cmd.arrival)&&/^\d{4}-\d{2}-\d{2}$/.test(cmd.departure)&&nights(cmd.arrival,cmd.departure)>0&&nights(cmd.arrival,cmd.departure)<=30,'Departure must follow arrival (maximum 30 nights in this demo).');
    assert(cmd.arrival>=s.date,'Arrival cannot be before the current business date.');
    assert(Number.isInteger(cmd.people)&&cmd.people>=1&&cmd.people<=4,'Choose 1–4 guests.');
    const r=s.rooms.find(r=>r.id===cmd.room);assert(r,'Choose a room.');
    assert(available(s,cmd.room,cmd.arrival,cmd.departure),'This room is not available for those dates.','NOT_AVAILABLE');
    const bid=id('HCC');
    s.bookings.push({id:bid,guest:cmd.guest.trim(),email:cmd.email||'',people:cmd.people,room:r!.id,arrival:cmd.arrival,departure:cmd.departure,rate:rateFor(s,r!,cmd.arrival),source:cmd.source||'Direct',status:'Confirmed',registered:false,lines:[],receipts:[],version:1,notes:cmd.notes||''});
    detail=`${bid} · ${cmd.guest} · room ${cmd.room}`;break;}
  case 'REGISTER':{req('checkin');const x=b();mutable(x);x.registered=true;x.version++;detail=x.guest+' · fictional registration verified';break;}
  case 'CHECKIN':{
    req('checkin');open();const x=b(),r=s.rooms.find(r=>r.id===x.room)!;
    assert(x.status==='Confirmed','Only a confirmed reservation can check in.','INVALID_TRANSITION');
    assert(x.arrival<=s.date&&x.departure>s.date,'Check-in must fall within the booked dates.','INVALID_TRANSITION');
    assert(x.registered,'Verify the fictional guest registration first.');
    assert(!r.blocked&&r.hk==='Ready'&&!occupant(s,r.id),'The room must be vacant, ready and unblocked.','NOT_AVAILABLE');
    assert(available(s,r.id,x.arrival,x.departure,x.id),'The room allocation conflicts with another booking.','NOT_AVAILABLE');
    x.status='In house';x.version++;detail=x.guest+' → room '+r.id;break;}
  case 'CANCEL':{
    req('reservations');open();const x=b();
    assert(x.status==='Confirmed','Only a confirmed reservation may be cancelled or marked no-show.','INVALID_TRANSITION');
    assert(!totals(x).deposit&&totals(x).paid===0,'Refund the advance and return any deposit before cancelling.','INVALID_TRANSITION');
    x.status=cmd.noShow?'No show':'Cancelled';x.version++;detail=x.id;break;}
  case 'MOVE':{
    req('reservations');open();const x=b();mutable(x);
    const r=s.rooms.find(r=>r.id===cmd.room);assert(r,'Select a room.');
    const arrival=x.status==='In house'?s.date:x.arrival;
    assert(cmd.departure>arrival&&nights(arrival,cmd.departure)<=30,'Departure must follow the active arrival date (maximum 30 nights).');
    assert(available(s,r!.id,arrival,cmd.departure,x.id),'Target room is unavailable for the requested stay.','NOT_AVAILABLE');
    if(x.status==='In house'&&r!.id!==x.room){
      assert(r!.hk==='Ready'&&!occupant(s,r!.id),'Target room must be vacant and ready.','NOT_AVAILABLE');
      const old=s.rooms.find(r=>r.id===x.room)!;old.hk='Dirty';
      s.tasks.push({id:id('task'),room:old.id,assignee:'housekeeper',status:'To do',checked:[false,false,false]});
    }
    detail=`${x.id} · room ${x.room} → ${cmd.room}; departure ${cmd.departure}; agreed tariff preserved`;
    x.room=cmd.room;x.departure=cmd.departure;x.version++;break;}
  case 'POST_NIGHTS':{
    // Pre-posts every agreed night. Retained for M1 only; replaced by nightly posting in M2 (DEC-029, BL-017).
    req('billing');const x=b();financial(x);assert(x.status==='In house','Check in before posting room nights.','INVALID_TRANSITION');
    for(let d=x.arrival;d<x.departure;d=addDays(d,1)){assert(!s.closedDates.includes(d)||x.lines.some(l=>l.kind==='room'&&l.date===d),'A missing charge belongs to a closed business date.','DAY_CLOSED');addNight(x,d);}
    x.version++;detail=x.id+' · full stay charges (including future nights)';break;}
  case 'CHARGE':{req('billing');const x=b();financial(x);positive(cmd.amount);assert(cmd.label?.trim(),'Describe the service.');x.lines.push({id:id('line'),label:cmd.label.trim(),amount:cmd.amount,date:s.date,kind:'service',taxBps:gstForCharge(s,s.date,cmd.gst??gstCategoryOfLabel(cmd.label)).bps});x.version++;detail=x.id+' · '+cmd.label;break;}
  case 'DISCOUNT':{
    req('billing');const x=b();financial(x);positive(cmd.amount);assert(cmd.reason?.trim(),'A reason is required.');
    assert(cmd.amount<=totals(x).net,'Discount cannot exceed existing net charges.');
    if(cmd.amount>s.settings.discountLimit){
      assert(!s.approvals.some(a=>a.booking===x.id&&a.status==='Pending'),'A discount request is already pending.','INVALID_TRANSITION');
      s.approvals.push({id:id('APR'),booking:x.id,amount:cmd.amount,reason:cmd.reason,actor:uid,version:x.version,status:'Pending'});detail=x.id+' · approval requested';
    }else{x.lines.push({id:id('discount'),label:'Discount · '+cmd.reason,amount:-cmd.amount,date:s.date,kind:'discount',taxBps:gstForFolioAdjustment(s,x,s.date).bps});x.version++;detail=x.id+' · discount posted';}
    break;}
  case 'APPROVAL':{
    req('approve');open();const a=s.approvals.find(a=>a.id===cmd.id);
    assert(a&&a.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
    assert(a!.actor!==uid,'You cannot approve your own request. Switch to another authorized demo user.','FORBIDDEN');
    const x=s.bookings.find(b=>b.id===a!.booking)!;
    if(cmd.approve){
      financial(x);assert(x.version===a!.version,'The folio changed after this request. Reject it and request again.','STALE_VERSION');
      assert(totals(x).net>=a!.amount,'Discount exceeds the current charges.');
      x.lines.push({id:id('discount'),label:'Approved discount · '+a!.reason,amount:-a!.amount,date:s.date,kind:'discount',taxBps:gstForFolioAdjustment(s,x,s.date).bps});x.version++;
    }
    a!.status=cmd.approve?'Approved':'Rejected';detail=a!.id+' · '+a!.status;break;}
  case 'PAYMENT':{
    req('payments');const x=b();open();positive(cmd.amount);
    const kind:Receipt['kind']=cmd.kind||'payment';
    assert(['payment','deposit','deposit-return','refund'].includes(kind),'Invalid receipt type.');
    if(['payment','deposit'].includes(kind))mutable(x);
    assert(['Cash','UPI','Card','Bank transfer'].includes(cmd.method),'Select a payment method.');
    if(cmd.method==='Cash')assert(s.shiftOpen,'The cash shift is closed. Open the next business day first.','INVALID_TRANSITION');
    if(cmd.method!=='Cash')assert(cmd.reference?.trim(),'Enter a demonstration transaction reference.');
    if(kind==='deposit-return')assert(cmd.amount<=totals(x).deposit,'Return cannot exceed the deposit held.');
    if(kind==='refund'){
      req('approve');assert(cmd.amount<=totals(x).paid,'Refund cannot exceed net receipts.');
      assert(x.status!=='Departed'||cmd.amount<=Math.max(0,-totals(x).balance),'A settled departed stay requires a credit note before a refund; credit notes are outside this demo.','INVALID_TRANSITION');
      assert(cmd.reference?.trim(),'Enter a refund reason/reference.');
    }
    x.receipts.push({id:id('RCPT'),amount:cmd.amount,method:cmd.method,date:s.date,kind,reference:cmd.reference||'Demo cash',actor:uid});
    x.version++;detail=x.id+' · '+kind+' · '+money(cmd.amount);break;}
  case 'CHECKOUT':{
    req('checkout');open();const x=b();assert(x.status==='In house','This stay is not in house.','INVALID_TRANSITION');
    for(let d=x.arrival;d<x.departure;d=addDays(d,1))assert(x.lines.some(l=>l.kind==='room'&&l.date===d),'Post all agreed stay charges before checkout.','INVALID_TRANSITION');
    assert(totals(x).balance===0,'Settle the folio exactly before checkout. Refund any excess receipt.','INVALID_TRANSITION');
    assert(totals(x).deposit===0,'Return the refundable deposit before checkout.','INVALID_TRANSITION');
    assert(!s.approvals.some(a=>a.booking===x.id&&a.status==='Pending'),'Resolve the pending discount request before checkout.','INVALID_TRANSITION');
    x.status='Departed';x.version++;s.rooms.find(r=>r.id===x.room)!.hk='Dirty';
    if(!s.tasks.some(t=>t.booking===x.id))s.tasks.push({id:id('task'),room:x.room,booking:x.id,assignee:'housekeeper',status:'To do',checked:[false,false,false]});
    detail=x.id+' · departed; room dirty; housekeeping task created';break;}
  case 'TASK':{
    req('housekeeping');const t=s.tasks.find(t=>t.id===cmd.id);assert(t,'Task not found.','NOT_FOUND');
    assert(!assignedScopeRoles.includes(user.role)||t!.assignee===uid,'Only your assigned tasks are accessible.','FORBIDDEN');
    const r=s.rooms.find(r=>r.id===t!.room)!;
    if(cmd.action==='assign'){req('inspect');assert(s.users.some(u=>u.id===cmd.assignee&&u.enabled&&u.role==='Housekeeper'),'Select an enabled housekeeper.');t!.assignee=cmd.assignee;}
    else if(cmd.action==='start'){assert(t!.status==='To do','Task has already started.','INVALID_TRANSITION');t!.status='In progress';r.hk='Cleaning';}
    else if(cmd.action==='check'){assert(t!.status==='In progress','Start the task before checking items.','INVALID_TRANSITION');assert([0,1,2].includes(cmd.index),'Invalid checklist item.');t!.checked[cmd.index]=!t!.checked[cmd.index];}
    else if(cmd.action==='complete'){assert(t!.status==='In progress'&&t!.checked.every(Boolean),'Finish all checklist items first.','INVALID_TRANSITION');t!.status=s.settings.inspection?'Inspection':'Done';r.hk=s.settings.inspection?'Inspection':'Ready';}
    else if(cmd.action==='inspect'){req('inspect');assert(t!.status==='Inspection','Task is not awaiting inspection.','INVALID_TRANSITION');t!.status='Done';r.hk='Ready';}
    else if(cmd.action==='reject'){req('inspect');assert(t!.status==='Inspection','Task is not awaiting inspection.','INVALID_TRANSITION');t!.status='In progress';r.hk='Cleaning';t!.checked=[false,false,false];}
    else fail('VALIDATION','Unknown task action.');
    detail=t!.room+' · '+cmd.action;break;}
  case 'BLOCK':{
    req('inspect');const r=s.rooms.find(r=>r.id===cmd.id)!;assert(r,'Room not found.','NOT_FOUND');
    if(!r.blocked)assert(!s.bookings.some(b=>b.room===r.id&&['In house','Confirmed'].includes(b.status)),'Resolve active and future allocations before blocking this room.','NOT_AVAILABLE');
    r.blocked=!r.blocked;detail=`Room ${r.id} · ${r.blocked?'blocked for repair':'unblocked'}`;break;}
  case 'EXPENSE':{req('expenses');open();positive(cmd.amount);assert(cmd.vendor?.trim(),'Enter a vendor.');s.expenses.push({id:id('EXP'),vendor:cmd.vendor,category:cmd.category,amount:cmd.amount,method:cmd.method,date:s.date,status:'Pending',actor:uid});detail='Expense submitted · '+cmd.vendor;break;}
  case 'EXPENSE_APPROVE':{
    req('approve');open();const e=s.expenses.find(e=>e.id===cmd.id);
    assert(e&&e.status==='Pending','Expense is no longer pending.','INVALID_TRANSITION');
    assert(e!.actor!==uid,'You cannot approve your own expense.','FORBIDDEN');
    if(cmd.approve&&e!.method==='Cash')assert(s.shiftOpen,'Open cash shift required.','INVALID_TRANSITION');
    e!.status=cmd.approve?'Posted':'Rejected';e!.date=s.date;detail=e!.id+' · '+e!.status;break;}
  case 'NIGHT_POST':{req('close');open();s.bookings.filter(x=>x.status==='In house'&&x.arrival<=s.date&&x.departure>s.date).forEach(x=>{const len=x.lines.length;addNight(x,s.date);if(x.lines.length!==len)x.version++;});detail='Idempotent room-night posting for '+s.date;break;}
  case 'SHIFT_CLOSE':{
    req('close');open();assert(s.shiftOpen,'Shift already closed.','INVALID_TRANSITION');
    assert(Number.isSafeInteger(cmd.counted)&&cmd.counted>=0,'Enter a valid physical cash count.');
    const expected=drawer(s);assert(cmd.counted===expected,'Resolve the cash difference before closing this demo shift.');
    s.shiftClose={expected,counted:cmd.counted};s.shiftOpen=false;detail='Cash reconciled · '+money(expected);break;}
  case 'DAY_CLOSE':{
    req('close');open();
    assert(!s.bookings.some(b=>b.status==='Confirmed'&&b.arrival<=s.date),'Resolve pending arrivals: check in or cancel after refunding advances.','INVALID_TRANSITION');
    assert(!s.bookings.some(b=>b.status==='In house'&&b.departure<=s.date),'Resolve overdue departures.','INVALID_TRANSITION');
    assert(!s.bookings.some(b=>b.status==='In house'&&!b.lines.some(l=>l.kind==='room'&&l.date===s.date)),'Post room nights before closing.','INVALID_TRANSITION');
    assert(!s.approvals.some(a=>a.status==='Pending')&&!s.expenses.some(e=>e.status==='Pending'),'Resolve pending approvals and expenses.','INVALID_TRANSITION');
    assert(!s.shiftOpen,'Close and reconcile the cash shift first.','INVALID_TRANSITION');
    s.closedDates.push(s.date);detail='Business day closed · '+s.date;break;}
  case 'NEXT_DAY':{
    req('close');assert(s.closedDates.includes(s.date),'Close the current business day first.','INVALID_TRANSITION');
    s.openingCash=drawer(s);s.date=addDays(s.date,1);s.shiftOpen=true;s.shiftClose=undefined;
    s.clock=s.date+'T'+OPENING_TIME;detail='Opened '+s.date;break;}
  case 'USER':{
    req('admin');const target=s.users.find(x=>x.id===cmd.id);assert(target,'User not found.','NOT_FOUND');
    assert(target!.id!==uid,'Use another owner account to change your own access.','FORBIDDEN');
    assert(target!.role!=='Owner','Owner accounts are protected in this prototype.','FORBIDDEN');
    target!.enabled=!target!.enabled;detail=target!.name+' · '+(target!.enabled?'enabled':'disabled');break;}
  case 'GRANT':{
    req('admin');
    assert(roles.includes(cmd.role)&&permissions.includes(cmd.permission),'Invalid role or permission.');
    assert(cmd.role!=='Owner','Owner template is protected in this prototype.','FORBIDDEN');
    assert(cmd.role!==user.role,'You cannot change the permissions of your own role.','FORBIDDEN');
    assert(cmd.permission!=='admin','Administrator delegation is outside this prototype.','FORBIDDEN');
    const arr=s.grants[cmd.role as Role];
    s.grants[cmd.role as Role]=arr.includes(cmd.permission)?arr.filter(p=>p!==cmd.permission):[...arr,cmd.permission];
    detail=`${cmd.role} · ${cmd.permission} ${arr.includes(cmd.permission)?'removed':'granted'}`;break;}
  case 'SETTINGS':{
    req('admin');assert(cmd.hotel?.trim(),'Property name is required.');
    assert(Number.isSafeInteger(cmd.discountLimit)&&cmd.discountLimit>=0,'Invalid discount threshold.');
    s.settings={hotel:cmd.hotel.trim(),discountLimit:cmd.discountLimit,inspection:!!cmd.inspection,checkin:cmd.checkin,checkout:cmd.checkout};
    detail='Demo policy updated · existing posted line tax snapshots preserved';break;}
  default:fail('VALIDATION','Unknown action.');
  }
  if(cmd.key)s.processed.push(cmd.key);
  // Simulated clock: every successful command advances one minute (deterministic, DEC-030).
  if(cmd.type!=='NEXT_DAY')s.clock=addMinutes(s.clock,1);
  s.audit.unshift({id:id('audit'),at:s.clock,actor:user.name,action:cmd.type.split('_').join(' '),detail});
  return cowFinish(input,s);
}

// Copy-on-write per top-level slice (audit P-04). The draft starts with every object slice as a lazy getter: the first read or
// write of `s.<slice>` deep-clones that slice from the input, so handlers still mutate a private copy and a failed command
// leaves the input untouched. Slices the command never touches keep the input's object identity in the result — that is what
// lets app/storage.ts write only changed slices. Callers must treat states as immutable (all mutations go through execute()).
function cowDraft(input:State):State{
  const d={} as Record<string,unknown>,src=input as unknown as Record<string,unknown>;
  const fix=(k:string,v:unknown)=>{Object.defineProperty(d,k,{value:v,writable:true,configurable:true,enumerable:true});};
  for(const k of Object.keys(src)){
    const v=src[k];
    if(v===null||typeof v!=='object'){d[k]=v;continue;}
    Object.defineProperty(d,k,{configurable:true,enumerable:true,get(){const c=clone(v);fix(k,c);return c;},set(x){fix(k,x);}});
  }
  return d as unknown as State;
}
/** Turns the draft into a plain object: untouched slices are the input's own objects, touched ones the private copies. */
function cowFinish(input:State,draft:State):State{
  const out={} as Record<string,unknown>,d=draft as unknown as Record<string,unknown>,src=input as unknown as Record<string,unknown>;
  for(const k of Object.keys(d)){const p=Object.getOwnPropertyDescriptor(d,k)!;out[k]=p.get?src[k]:p.value;}
  return out as unknown as State;
}
/** Test/diagnostic helper: the top-level slices of `next` that are not shared with `prev`. */
export function changedSlices(prev:State,next:State):string[]{
  const a=prev as unknown as Record<string,unknown>,b=next as unknown as Record<string,unknown>;
  return [...new Set([...Object.keys(a),...Object.keys(b)])].filter(k=>a[k]!==b[k]);
}

/** Toolkit passed to stream handlers. Handlers mutate kit.s (a private clone) and return the audit detail text. */
export type Kit={s:State;uid:string;user:User;cmd:Command;
  fail:(code:ErrorCode,msg:string)=>never;assert:(ok:unknown,msg:string,code?:ErrorCode)=>void;req:(p:Permission)=>void;
  id:(prefix:string)=>string;b:()=>Booking;positive:(n:number)=>void;open:()=>void;mutable:(x:Booking)=>void;financial:(x:Booking)=>void;
  addNight:(x:Booking,date:string)=>void};
export type Handler=(k:Kit)=>string;

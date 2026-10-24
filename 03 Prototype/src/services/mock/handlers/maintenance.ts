// Module 15 · Maintenance — command handlers. Command types MUST start with 'MNT_'. Return value = audit/summary text.
// Builds on stream D: repair tasks (REPAIR_REQUEST) convert into work orders; room blocks use the same rules as BLOCK.
import type {Handler,Kit} from '../commands';
import {can} from '../../../domain/selectors';
import {addDays} from '../../../domain/money';
import {hkAllocationsOverlapping} from '../../../domain/streams/housekeeping';
import {mntSlice,mntPmDue,MNT_PRIORITIES,MNT_ASSET_KINDS,mntIsActive,type MNTState,type MNTWorkOrder,type MNTPriority} from '../../../domain/streams/maintenance';

const ISO=/^\d{4}-\d{2}-\d{2}$/;
const slice=(k:Kit):MNTState=>(k.s.maintenanceX=mntSlice(k.s));
const text=(v:unknown)=>String(v??'').trim();
const wo=(k:Kit)=>{const w=slice(k).orders.find(w=>w.id===k.cmd.id);k.assert(w,'Work order not found.','NOT_FOUND');return w!;};
const log=(k:Kit,w:MNTWorkOrder,action:string,note?:string)=>{w.history.push({at:k.s.clock,by:k.user.name.split(' · ')[0],action,...(note?{note}:{})});};
const manager=(k:Kit)=>can(k.s,k.uid,'inspect');
/** Technician actions: the assigned Maintenance user, or a supervisor/manager. Housekeepers never. */
function worker(k:Kit,w:MNTWorkOrder){
  k.req('housekeeping');
  if(manager(k))return;
  k.assert(k.user.role==='Maintenance'&&w.assignee===k.uid,'Only your assigned work orders are accessible.','FORBIDDEN');
}
const repairTask=(k:Kit,w:MNTWorkOrder)=>w.repairTaskId?k.s.tasks.find(t=>t.id===w.repairTaskId):undefined;
const priority=(k:Kit,p:unknown):MNTPriority=>{k.assert(MNT_PRIORITIES.includes(p as MNTPriority),'Choose a priority (P1–P4).');return p as MNTPriority;};
const techOk=(k:Kit,id:string)=>k.s.users.some(u=>u.id===id&&u.enabled&&u.role==='Maintenance');

export const handlers:Record<string,Handler>={
  MNT_WO_CREATE:k=>{
    const {s,uid,user,cmd,assert,req,open,id}=k;
    req('housekeeping');open();
    assert(user.role!=='Housekeeper','Housekeepers report repairs from their task card.','FORBIDDEN');
    const x=slice(k);
    const title=text(cmd.title);assert(title.length>=3,'Describe the fault (at least 3 characters).');
    const p=priority(k,cmd.priority);
    const asset=cmd.assetId?x.assets.find(a=>a.id===cmd.assetId):undefined;
    assert(!cmd.assetId||asset,'Asset not found.','NOT_FOUND');
    const room=text(cmd.room)||asset?.room||undefined,area=room?undefined:(text(cmd.area)||asset?.area||undefined);
    assert(!room||s.rooms.some(r=>r.id===room),'Room not found.','NOT_FOUND');
    assert(room||area,'Choose a room, an area or an asset.');
    let assignee:string|undefined=text(cmd.assignee)||undefined;
    if(user.role==='Maintenance')assignee=uid; // technicians raise faults for themselves
    else if(assignee){req('inspect');assert(techOk(k,assignee),'Select an enabled maintenance user.');}
    const w:MNTWorkOrder={id:id('WO'),kind:'Corrective',title,priority:p,status:'Open',room,area,assetId:asset?.id,assignee,
      createdAt:s.clock,createdBy:uid,note:text(cmd.notes)||undefined,parts:[],history:[]};
    log(k,w,'Created',text(cmd.notes)||undefined);
    if(asset&&asset.status==='Working')asset.status='Faulty';
    x.orders.push(w);
    return `${w.id} · ${title} · ${p} · ${room?'room '+room:area}`;
  },

  MNT_WO_FROM_REPAIR:k=>{
    const {s,uid,user,cmd,assert,req,id}=k;
    req('housekeeping');
    const t=s.tasks.find(t=>t.id===cmd.taskId);assert(t&&t.kind==='repair','Repair request not found.','NOT_FOUND');
    const task=t!;
    assert(manager(k)||(user.role==='Maintenance'&&task.assignee===uid),'Only a supervisor or the assigned technician can convert this repair.','FORBIDDEN');
    assert(task.status!=='Done','This repair is already done.','INVALID_TRANSITION');
    const x=slice(k);
    assert(!x.orders.some(w=>w.repairTaskId===task.id),'A work order already exists for this repair.','INVALID_TRANSITION');
    const p=cmd.priority?priority(k,cmd.priority):'P2';
    const asset=x.assets.find(a=>a.room===task.room&&a.id===cmd.assetId);
    const w:MNTWorkOrder={id:id('WO'),kind:'Corrective',title:task.note||'Repair',priority:p,status:task.status==='In progress'?'In progress':'Open',
      room:task.room,assetId:asset?.id,assignee:techOk(k,task.assignee)?task.assignee:undefined,createdAt:task.createdAt??s.clock,createdBy:uid,
      startedAt:task.startedAt,parts:[],repairTaskId:task.id,history:[]};
    const r=s.rooms.find(r=>r.id===task.room);
    if(r?.blocked){w.downtimeFrom=task.createdAt??s.clock;}
    log(k,w,'Converted from repair request',task.id);
    x.orders.push(w);
    return `${w.id} · from repair ${task.id} · room ${task.room}`;
  },

  MNT_WO_ASSIGN:k=>{
    const {cmd,assert,req}=k;req('inspect');const w=wo(k);
    assert(mntIsActive(w)||w.status==='Done','This work order is closed.','INVALID_TRANSITION');
    if(cmd.vendorId){assert(slice(k).vendors.some(v=>v.id===cmd.vendorId),'Vendor not found.','NOT_FOUND');w.vendorId=cmd.vendorId;w.assignee=undefined;}
    else{assert(techOk(k,cmd.assignee),'Select an enabled maintenance user.');w.assignee=cmd.assignee;w.vendorId=undefined;
      const t=repairTask(k,w);if(t&&t.status!=='Done')t.assignee=cmd.assignee;}
    log(k,w,'Assigned',w.vendorId?slice(k).vendors.find(v=>v.id===w.vendorId)?.name:k.s.users.find(u=>u.id===w.assignee)?.name.split(' · ')[0]);
    return `${w.id} → ${w.vendorId??w.assignee}`;
  },

  MNT_WO_START:k=>{
    const {s,assert}=k;const w=wo(k);worker(k,w);
    assert(w.status==='Open'||w.status==='On hold','Only an open or on-hold work order can start.','INVALID_TRANSITION');
    assert(w.assignee||w.vendorId,'Assign a technician or vendor first.','INVALID_TRANSITION');
    const resumed=w.status==='On hold';
    w.status='In progress';w.startedAt=w.startedAt??s.clock;w.holdReason=undefined;w.awaitingSpare=undefined;
    const t=repairTask(k,w);if(t&&t.status==='To do'){t.status='In progress';t.startedAt=s.clock;}
    log(k,w,resumed?'Resumed':'Started');
    return `${w.id} · ${resumed?'resumed':'started'}`;
  },

  MNT_WO_HOLD:k=>{
    const {cmd,assert}=k;const w=wo(k);worker(k,w);
    assert(w.status==='In progress','Only work in progress can be put on hold.','INVALID_TRANSITION');
    const reason=text(cmd.reason);assert(reason.length>=3,'Enter why the work is on hold.');
    if(cmd.spareId)assert(slice(k).spares.some(x=>x.id===cmd.spareId),'Spare not found.','NOT_FOUND');
    w.status='On hold';w.holdReason=reason;w.awaitingSpare=cmd.spareId||undefined;
    log(k,w,'On hold',reason);
    return `${w.id} · on hold · ${reason}`;
  },

  MNT_WO_PART:k=>{
    const {cmd,assert}=k;const w=wo(k);worker(k,w);
    assert(w.status==='In progress'||w.status==='On hold','Parts can be booked only while work is open.','INVALID_TRANSITION');
    const sp=slice(k).spares.find(x=>x.id===cmd.spareId);assert(sp,'Spare not found.','NOT_FOUND');
    const qty=Number(cmd.qty);assert(Number.isInteger(qty)&&qty>=1&&qty<=99,'Enter a quantity between 1 and 99.');
    assert(qty<=sp!.stock,`Only ${sp!.stock} ${sp!.unit} of ${sp!.name} in stock.`,'NOT_AVAILABLE');
    sp!.stock-=qty;
    const line=w.parts.find(p=>p.spareId===sp!.id&&p.unitCost===sp!.cost);
    if(line)line.qty+=qty;else w.parts.push({spareId:sp!.id,qty,unitCost:sp!.cost});
    if(w.awaitingSpare===sp!.id)w.awaitingSpare=undefined;
    log(k,w,'Part used',`${qty} × ${sp!.name}`);
    return `${w.id} · ${qty} × ${sp!.name}`;
  },

  MNT_WO_COMPLETE:k=>{
    const {s,uid,cmd,assert}=k;const w=wo(k);worker(k,w);
    assert(w.status==='In progress','Start the work before completing it.','INVALID_TRANSITION');
    const note=text(cmd.note);assert(note.length>=3,'Describe the work done (at least 3 characters).');
    w.status='Done';w.doneAt=s.clock;w.doneBy=uid;w.note=note;w.returnReason=undefined;
    const t=repairTask(k,w);if(t&&t.status!=='Done'){t.status='Done';t.startedAt=t.startedAt??w.startedAt;t.completedAt=s.clock;}
    log(k,w,'Completed',note);
    const r=w.room?s.rooms.find(r=>r.id===w.room):undefined;
    return `${w.id} · done${r?.blocked?' · room stays blocked until verified':''}`;
  },

  MNT_WO_VERIFY:k=>{
    const {s,uid,cmd,assert,req}=k;req('inspect');const w=wo(k);
    assert(w.status==='Done','Only completed work can be verified.','INVALID_TRANSITION');
    assert(w.doneBy!==uid&&w.assignee!==uid,'You cannot verify your own work. Switch to another supervisor.','FORBIDDEN');
    if(!cmd.approve){
      const reason=text(cmd.reason);assert(reason.length>=3,'Enter why the work is returned.');
      w.status='In progress';w.returnReason=reason;w.doneAt=undefined;w.doneBy=undefined;
      log(k,w,'Returned',reason);return `${w.id} · returned · ${reason}`;
    }
    w.status='Verified';w.verifiedAt=s.clock;w.verifiedBy=uid;
    const x=slice(k);
    const asset=x.assets.find(a=>a.id===w.assetId);if(asset&&asset.status==='Faulty'&&!x.orders.some(o=>o.id!==w.id&&o.assetId===asset.id&&mntIsActive(o)))asset.status='Working';
    if(w.planId){const p=x.plans.find(p=>p.id===w.planId);if(p&&(!p.lastDone||p.lastDone<w.doneAt!.slice(0,10)))p.lastDone=w.doneAt!.slice(0,10);}
    let extra='';
    const r=w.room?s.rooms.find(r=>r.id===w.room):undefined;
    if(cmd.unblock){
      assert(r?.blocked,'This room is not blocked.','INVALID_TRANSITION');
      r!.blocked=false;r!.blockReason=undefined;r!.blockFrom=undefined;r!.blockTo=undefined;
      if(w.downtimeFrom&&!w.downtimeTo)w.downtimeTo=s.clock;extra=` · room ${r!.id} unblocked`;
    }
    log(k,w,'Verified',extra.slice(3)||undefined);
    return `${w.id} · verified${extra}`;
  },

  MNT_WO_CANCEL:k=>{
    const {cmd,assert,req}=k;req('inspect');const w=wo(k);
    assert(w.status==='Open'||w.status==='On hold','Only open or on-hold work orders can be cancelled.','INVALID_TRANSITION');
    const reason=text(cmd.reason);assert(reason.length>=3,'Enter a cancellation reason.');
    w.status='Cancelled';w.cancelReason=reason;log(k,w,'Cancelled',reason);
    return `${w.id} · cancelled · ${reason}`;
  },

  MNT_WO_BLOCK:k=>{
    const {s,cmd,assert,req}=k;req('inspect');const w=wo(k);
    assert(w.room,'This work order is not for a guest room.');
    assert(mntIsActive(w)||w.status==='Done','This work order is closed.','INVALID_TRANSITION');
    const r=s.rooms.find(r=>r.id===w.room)!;
    if(r.blocked){w.downtimeFrom=w.downtimeFrom??s.clock;w.downtimeTo=undefined;log(k,w,'Linked existing room block');return `${w.id} · room ${r.id} already blocked — linked`;}
    const to:string|undefined=cmd.to||undefined;
    assert(!to||(ISO.test(to)&&to>s.date),'The block must end after today.');
    assert(hkAllocationsOverlapping(s,r.id,s.date,to).length===0,'Resolve active and future allocations before blocking this room.','NOT_AVAILABLE');
    r.blocked=true;r.blockReason=`Maintenance · ${w.id} · ${w.title}`;r.blockFrom=s.date;r.blockTo=to;
    w.downtimeFrom=s.clock;w.downtimeTo=undefined;
    log(k,w,'Room blocked',`${s.date} → ${to??'open-ended'}`);
    return `${w.id} · room ${r.id} blocked ${s.date} → ${to??'open-ended'}`;
  },

  MNT_PM_GENERATE:k=>{
    const {s,uid,req,open,id}=k;req('inspect');open();
    const x=slice(k);const due=mntPmDue(s);
    for(const {plan,due:d} of due){
      const asset=x.assets.find(a=>a.id===plan.assetId);
      const w:MNTWorkOrder={id:id('WO'),kind:'Preventive',title:plan.title,priority:'P3',status:'Open',assetId:plan.assetId,room:asset?.room,area:asset?.room?undefined:asset?.area,
        assignee:undefined,vendorId:asset?.vendorId&&['Lift','Generator'].includes(asset.kind)?asset.vendorId:undefined,
        createdAt:s.clock,createdBy:uid,parts:[],planId:plan.id,dueDate:d,history:[]};
      if(!w.vendorId){const t=s.users.find(u=>u.enabled&&u.role==='Maintenance');w.assignee=t?.id;}
      log(k,w,'Generated from PM plan',`${plan.id} · due ${d}`);
      x.orders.push(w);
    }
    for(const p of x.plans.filter(p=>p.active)){let n=0;while(p.nextDue<=addDays(s.date,3)&&n<12){p.nextDue=addDays(p.nextDue,p.everyDays);n++;}}
    return `${due.length} preventive work order(s) generated up to ${addDays(s.date,3)}`;
  },

  MNT_PLAN_SAVE:k=>{
    const {cmd,assert,req,id}=k;req('inspect');const x=slice(k);
    assert(x.assets.some(a=>a.id===cmd.assetId),'Choose an asset.');
    const title=text(cmd.title);assert(title.length>=3,'Enter the PM task.');
    const every=Number(cmd.everyDays);assert(Number.isInteger(every)&&every>=1&&every<=365,'Repeat every 1–365 days.');
    assert(ISO.test(cmd.nextDue),'Enter the next due date.');
    const data={assetId:cmd.assetId,title,everyDays:every,nextDue:cmd.nextDue,active:cmd.active!==false};
    if(cmd.id){const p=x.plans.find(p=>p.id===cmd.id);assert(p,'Plan not found.','NOT_FOUND');Object.assign(p!,data);return `${p!.id} · plan updated`;}
    const p={id:id('PM'),...data};x.plans.push(p);return `${p.id} · ${title} every ${every} d`;
  },

  MNT_ASSET_SAVE:k=>{
    const {s,cmd,assert,req,id}=k;req('inspect');const x=slice(k);
    const name=text(cmd.name);assert(name.length>=2,'Enter the asset name.');
    assert(MNT_ASSET_KINDS.includes(cmd.kind),'Choose an asset type.');
    const room=text(cmd.room)||undefined,area=room?undefined:text(cmd.area)||undefined;
    assert(!room||s.rooms.some(r=>r.id===room),'Room not found.','NOT_FOUND');assert(room||area,'Choose a room or enter an area.');
    assert(!cmd.installedOn||ISO.test(cmd.installedOn),'Enter a valid install date.');
    assert(!cmd.vendorId||x.vendors.some(v=>v.id===cmd.vendorId),'Vendor not found.','NOT_FOUND');
    assert(['Working','Faulty','Retired'].includes(cmd.status??'Working'),'Invalid asset status.');
    const data={name,kind:cmd.kind,room,area,make:text(cmd.make),model:text(cmd.model),installedOn:cmd.installedOn||'',vendorId:cmd.vendorId||undefined,status:cmd.status??'Working'};
    if(cmd.id){const a=x.assets.find(a=>a.id===cmd.id);assert(a,'Asset not found.','NOT_FOUND');Object.assign(a!,data);return `${a!.id} · ${name} updated`;}
    const a={id:id('AST'),...data};x.assets.push(a);return `${a.id} · ${name} added`;
  },

  MNT_VENDOR_SAVE:k=>{
    const {cmd,assert,req,id}=k;req('inspect');const x=slice(k);
    const name=text(cmd.name);assert(name.length>=3,'Enter the vendor name.');
    const phone=text(cmd.phone);assert(/^[+\d][\d\s-]{6,}$/.test(phone),'Enter a phone number.');
    const data={name,trade:text(cmd.trade)||'General',phone};
    if(cmd.id){const v=x.vendors.find(v=>v.id===cmd.id);assert(v,'Vendor not found.','NOT_FOUND');Object.assign(v!,data);return `${v!.id} · ${name} updated`;}
    const v={id:id('VEN'),...data};x.vendors.push(v);return `${v.id} · ${name} added`;
  },

  MNT_AMC_SAVE:k=>{
    const {cmd,assert,req,id,positive}=k;req('expenses');const x=slice(k);
    assert(x.vendors.some(v=>v.id===cmd.vendorId),'Choose a vendor.');
    const scope=text(cmd.scope);assert(scope.length>=3,'Describe what the AMC covers.');
    assert(ISO.test(cmd.from)&&ISO.test(cmd.to)&&cmd.to>cmd.from,'The AMC must end after it starts.');
    const visits=Number(cmd.visits);assert(Number.isInteger(visits)&&visits>=0&&visits<=52,'Visits per year: 0–52.');
    positive(cmd.value);
    const data={vendorId:cmd.vendorId,scope,from:cmd.from,to:cmd.to,visits,value:cmd.value};
    if(cmd.id){const a=x.amcs.find(a=>a.id===cmd.id);assert(a,'AMC not found.','NOT_FOUND');Object.assign(a!,data);return `${a!.id} · AMC updated`;}
    const a={id:id('AMC'),...data};x.amcs.push(a);return `${a.id} · AMC ${cmd.from} → ${cmd.to}`;
  },

  MNT_SPARE_RECEIVE:k=>{
    const {cmd,assert,req}=k;req('inspect');
    const sp=slice(k).spares.find(x=>x.id===cmd.spareId);assert(sp,'Spare not found.','NOT_FOUND');
    const qty=Number(cmd.qty);assert(Number.isInteger(qty)&&qty>=1&&qty<=999,'Enter a quantity between 1 and 999.');
    sp!.stock+=qty;return `${sp!.name} · +${qty} ${sp!.unit} (stock ${sp!.stock})`;
  },
};

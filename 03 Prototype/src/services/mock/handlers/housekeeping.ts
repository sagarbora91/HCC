// Stream D · Housekeeping & rooms (owns TASK, BLOCK; adds UNBLOCK, REPAIR_REQUEST, STAYOVER_GENERATE, BACKUP_SIMULATE).
// Register new or replacement command handlers here. A handler with the same name as a built-in command replaces it.
import type {Handler,Kit} from '../commands';
import type {Room,Permission} from '../../../domain/types';
import {assignedScopeRoles} from '../../../domain/types';
import {can} from '../../../domain/selectors';
import {hkTaskKind,hkStayoverId,hkStayoverRooms,hkAllocationsOverlapping,HK_REPAIR_ASSIGNEE} from '../../../domain/streams/housekeeping';

const ISO=/^\d{4}-\d{2}-\d{2}$/;
const room=(k:Kit,id:unknown)=>{const r=k.s.rooms.find(r=>r.id===id);k.assert(r,'Room not found.','NOT_FOUND');return r!;};
const clearBlock=(r:Room)=>{r.blocked=false;r.blockReason=undefined;r.blockFrom=undefined;r.blockTo=undefined;};

export const handlers:Record<string,Handler>={
  /** Replaces built-in TASK. Adds task kinds (stayover/repair), reject reason, timestamps. Messages of M1 actions kept. */
  TASK:k=>{
    const {s,uid,user,cmd,fail,assert,req}=k;
    req('housekeeping'); // PERM-HK-001/003
    const t=s.tasks.find(t=>t.id===cmd.id);assert(t,'Task not found.','NOT_FOUND');
    const task=t!;
    assert(!assignedScopeRoles.includes(user.role)||task.assignee===uid,'Only your assigned tasks are accessible.','FORBIDDEN');
    const r=s.rooms.find(r=>r.id===task.room)!;
    const kind=hkTaskKind(task);
    /** Only cleans after a departure/move drive room readiness; stayover and repair tasks leave hk unchanged. */
    const readiness=kind==='checkout'||kind==='move';
    let extra='';
    if(cmd.action==='assign'){
      req('inspect'); // PERM-HK-002
      if(kind==='repair')assert(s.users.some(u=>u.id===cmd.assignee&&u.enabled&&u.role==='Maintenance'),'Select an enabled maintenance user.');
      else assert(s.users.some(u=>u.id===cmd.assignee&&u.enabled&&u.role==='Housekeeper'),'Select an enabled housekeeper.');
      task.assignee=cmd.assignee;extra=' → '+cmd.assignee;
    }
    else if(cmd.action==='start'){
      assert(task.status==='To do','Task has already started.','INVALID_TRANSITION');
      task.status='In progress';task.startedAt=s.clock;if(readiness)r.hk='Cleaning';
    }
    else if(cmd.action==='check'){
      assert(kind!=='repair','Repair tasks have no cleaning checklist.');
      assert(task.status==='In progress','Start the task before checking items.','INVALID_TRANSITION');
      assert([0,1,2].includes(cmd.index),'Invalid checklist item.');task.checked[cmd.index]=!task.checked[cmd.index];
    }
    else if(cmd.action==='complete'){
      if(kind==='repair'){
        // Completing a repair never unblocks the room: the supervisor reviews and unblocks (PERM-ROOM-002).
        assert(task.status==='In progress','Start the repair before completing it.','INVALID_TRANSITION');
        task.status='Done';extra=r.blocked?' · room stays blocked until a supervisor unblocks it':'';
      }else{
        assert(task.status==='In progress'&&task.checked.every(Boolean),'Finish all checklist items first.','INVALID_TRANSITION');
        if(kind==='stayover')task.status='Done'; // stayover: no inspection, room readiness unchanged
        else{task.status=s.settings.inspection?'Inspection':'Done';r.hk=s.settings.inspection?'Inspection':'Ready';}
      }
      task.completedAt=s.clock;
    }
    else if(cmd.action==='inspect'){
      req('inspect'); // PERM-HK-004
      assert(task.status==='Inspection','Task is not awaiting inspection.','INVALID_TRANSITION');
      task.status='Done';r.hk='Ready'; // AC-010: a block, if any, stays
      if(r.blocked)extra=' · room remains blocked';
    }
    else if(cmd.action==='reject'){
      req('inspect'); // PERM-HK-004
      assert(task.status==='Inspection','Task is not awaiting inspection.','INVALID_TRANSITION');
      const reason=String(cmd.reason??'').trim();
      assert(reason.length>=3,'Enter a reason for returning the room for cleaning.');
      task.status='In progress';r.hk='Cleaning';task.checked=[false,false,false];task.rejectReason=reason;task.completedAt=undefined;extra=' · '+reason;
    }
    else fail('VALIDATION','Unknown task action.');
    return task.room+' · '+cmd.action+extra;
  },

  /** Replaces built-in BLOCK. BLOCK{id, reason, from?, to?}: dated block over nights [from, to).
   *  Backward compatibility: BLOCK{id} with no reason/dates on an already-blocked room unblocks it (the M1 toggle);
   *  on an unblocked room a reason is required (VALIDATION). */
  BLOCK:k=>{
    const {s,cmd,assert,req}=k;
    req('inspect'); // PERM-ROOM-002 room.block.apply
    const r=room(k,cmd.id);
    const reason=String(cmd.reason??'').trim();
    if(!reason&&!cmd.from&&!cmd.to&&r.blocked){clearBlock(r);return `Room ${r.id} · unblocked`;}
    assert(reason.length>=3,'Enter a reason for the block.');
    const from:string=cmd.from||s.date,to:string|undefined=cmd.to||undefined;
    assert(ISO.test(from)&&(!to||ISO.test(to)),'Enter valid block dates.');
    assert(from>=s.date,'A block cannot start before the current business date.');
    assert(!to||to>from,'The block must end after it starts.');
    assert(hkAllocationsOverlapping(s,r.id,from,to).length===0,'Resolve active and future allocations before blocking this room.','NOT_AVAILABLE');
    r.blocked=true;r.blockReason=reason;r.blockFrom=from;r.blockTo=to;
    return `Room ${r.id} · blocked for ${reason} · ${from} → ${to??'open-ended'}`;
  },

  UNBLOCK:k=>{
    k.req('inspect'); // PERM-ROOM-002 unblock
    const r=room(k,k.cmd.id);
    k.assert(r.blocked,'This room is not blocked.','INVALID_TRANSITION');
    const was=r.blockReason;clearBlock(r);
    return `Room ${r.id} · unblocked${was?' (was: '+was+')':''}`;
  },

  /** Report a repair (PERM-ROOM-001): creates a repair task for Maintenance. It is a block *request*; the
   *  supervisor applies the block (BLOCK) and unblocks after the repair. */
  REPAIR_REQUEST:k=>{
    const {s,uid,user,cmd,fail,assert,id}=k;
    if(!(['housekeeping','reservations','inspect'] as Permission[]).some(p=>can(s,uid,p)))fail('FORBIDDEN','Access denied: housekeeping, reservations or inspect permission is required.');
    const r=room(k,cmd.room);
    const reason=String(cmd.reason??'').trim();
    assert(reason.length>=3,'Describe the repair needed.');
    if(assignedScopeRoles.includes(user.role))assert(s.tasks.some(t=>t.room===r.id&&t.assignee===uid&&t.status!=='Done'),'You can report repairs only for rooms assigned to you.','FORBIDDEN');
    assert(s.users.some(u=>u.id===HK_REPAIR_ASSIGNEE&&u.enabled),'No enabled maintenance user is available.');
    assert(!s.tasks.some(t=>t.room===r.id&&t.kind==='repair'&&t.status!=='Done'),'A repair request is already open for this room.','INVALID_TRANSITION');
    const tid=id('repair');
    s.tasks.push({id:tid,room:r.id,assignee:HK_REPAIR_ASSIGNEE,status:'To do',checked:[],kind:'repair',note:reason,date:s.date,createdAt:s.clock});
    return `Room ${r.id} · repair requested (${reason}) → ${HK_REPAIR_ASSIGNEE}`;
  },

  /** One stayover task per in-house room for the business date. Idempotent per room/date. */
  STAYOVER_GENERATE:k=>{
    const {s,assert,req,open}=k;
    req('inspect'); // PERM-HK-002 (create/assign)
    open();
    const hks=s.users.filter(u=>u.role==='Housekeeper'&&u.enabled).map(u=>u.id).sort();
    assert(hks.length,'Enable at least one housekeeper first.');
    let created=0,existing=0;
    for(const roomId of hkStayoverRooms(s)){
      const tid=hkStayoverId(roomId,s.date);
      if(s.tasks.some(t=>t.id===tid)){existing++;continue;}
      const floor=s.rooms.find(r=>r.id===roomId)?.floor??1;
      s.tasks.push({id:tid,room:roomId,assignee:hks[(floor-1)%hks.length],status:'To do',checked:[false,false,false],kind:'stayover',date:s.date,createdAt:s.clock});
      created++;
    }
    return `${created} stayover task(s) created for ${s.date}${existing?`; ${existing} already existed`:''}`;
  },

  /** Audit-only simulated backup (SCR-19). No data is copied and no other state changes. */
  BACKUP_SIMULATE:k=>{
    k.req('admin'); // PERM-ADM-005 backup.run (simulated)
    return `Simulated backup at ${k.s.clock} — no data copied (demonstration)`;
  },
};

// MB-07 pure helpers for the phone housekeeping / maintenance page (no React; tested in tests/stream-mobile-ops.mjs).
import type {State,Task,Role} from '../../services/mock';
import {assignedScopeRoles,hkSortTasks,hkTaskKind} from '../../services/mock';

export type MoSegment='todo'|'cleaning'|'inspect'|'done';
/** Supervisor segmented control (M13): label → task status. */
export const moSegments:{id:MoSegment;label:string;status:Task['status']}[]=[
  {id:'todo',label:'To do',status:'To do'},
  {id:'cleaning',label:'Cleaning',status:'In progress'},
  {id:'inspect',label:'Inspect',status:'Inspection'},
  {id:'done',label:'Done',status:'Done'},
];
/** Which view of the page a role gets: housekeeper "My tasks" (M12), maintenance "My repairs" (M14) or the supervisor board (M13). */
export type MoView='mytasks'|'repairs'|'board';
export function moViewFor(role:Role):MoView{return role==='Maintenance'?'repairs':assignedScopeRoles.includes(role)?'mytasks':'board';}

/** Segment / mode the page should open on for a bottom-tab key or URL hash (#inspect, #report). */
export function moFocusFrom(tabKey:string,hash=''):{segment?:MoSegment;report?:boolean}{
  const k=(tabKey||hash.replace(/^#/,'')).toLowerCase();
  if(k==='inspect'||k==='inspection')return {segment:'inspect'};
  if(k==='board')return {segment:'todo'};
  if(k==='hkrepair'||k==='report')return {report:true};
  return {};
}

/** Own tasks first: active work, then to do (arrival-today priority), awaiting inspection, done. Other people's tasks are never listed. */
export function moMyTasks(s:State,uid:string):Task[]{return hkSortTasks(s,s.tasks.filter(t=>t.assignee===uid));}
/** Board list for one segment, arrival-today rooms first. */
export function moSegmentTasks(s:State,tasks:Task[],seg:MoSegment):Task[]{
  const st=moSegments.find(x=>x.id===seg)!.status;return hkSortTasks(s,tasks.filter(t=>t.status===st));}
export function moSegmentCounts(tasks:Task[]):Record<MoSegment,number>{
  const c={todo:0,cleaning:0,inspect:0,done:0} as Record<MoSegment,number>;
  for(const sg of moSegments)c[sg.id]=tasks.filter(t=>t.status===sg.status).length;return c;}

/** Users that may take a task (assign picker sheet): enabled housekeepers, or maintenance for a repair. */
export function moAssignPool(s:State,t:Task){const role:Role=hkTaskKind(t)==='repair'?'Maintenance':'Housekeeper';return s.users.filter(u=>u.enabled&&u.role===role);}

/** Rooms a housekeeper may report a repair for (rooms of their open cleaning tasks), with whether a repair is already open. */
export function moRepairRooms(s:State,uid:string){
  const rooms=[...new Set(s.tasks.filter(t=>t.assignee===uid&&t.status!=='Done'&&hkTaskKind(t)!=='repair').map(t=>t.room))].sort();
  return rooms.map(room=>({room,open:s.tasks.find(t=>t.room===room&&t.kind==='repair'&&t.status!=='Done')}));
}

/** Cleaning checklist (same three items as desktop / the TASK handler indexes 0–2). */
export const MO_HK_CHECKLIST=['Linen & bathroom','Surfaces & amenities','Final room check'];

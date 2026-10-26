// Shift-handover area (W4-10): checklists (runs per business date), handover notes with carry-forward and acknowledgements.
// Server: 08 Production/src/Hcc.Api/Endpoints/HandoverEndpoints.cs (module 19).
//
//   loaders   handoverX ← GET /handover/checklists (today: saved runs and unstarted active templates, already filtered to the caller's roles),
//             GET /handover/templates (managers: adds inactive templates and the versions needed to edit / toggle) and GET /handover/notes.
//             Templates of non-managers are rebuilt from their checklist cards. Runs of earlier dates are not loaded (the server list is
//             per date): the manager dashboard for a past date shows those runs as not started.
//   commands  HOV_TEMPLATE_SAVE, HOV_TEMPLATE_TOGGLE, HOV_ITEM, HOV_RUN_SUBMIT, HOV_RUN_REOPEN, HOV_NOTE_SUBMIT, HOV_NOTE_ACK, HOV_CARRY_DONE.
//   ids       template/run: server numeric id (run id = HOVR-<template>-<date> as in the prototype); checklist item = server item key;
//             note = server ref (HOVN-FRONT-2026-10-24-M); carry item = <note ref>-c<n>.
import type {Role} from '../../../domain/types';
import type {HOVState,HOVTemplate,HOVRun,HOVRunItem,HOVNote,HOVDesk,HOVShift} from '../../../domain/streams/handover';
import {HccClientError} from '../../errors';
import type {SyncArea,LoaderCtx,CommandCtx} from '../types';
import {utcToClock,serverId} from '../map';
import {ROLE_MAP} from '../capabilities';

type SrvItem={key:string;text:string;mandatory:boolean;due:string|null;photo:boolean;link:string|null};
type SrvTemplate={templateId:number;name:string;desk:string;shift:string;roles:string[];items:SrvItem[];active:boolean;templateVersion:number;version:string};
type SrvRunItem=SrvItem&{done:boolean;doneBy:number|null;doneUtc:string|null;note:string|null;photoName:string|null;overdue:boolean};
type SrvRun={runId:number|null;templateId:number;templateVersion:number;name:string;desk:string;shift:string;roles:string[];date:string;status:string;started:boolean;
  startedBy:number|null;submittedUtc:string|null;submittedBy:number|null;reopenReason:string|null;items:SrvRunItem[];progress:{late:boolean}};
type SrvCarry={carryId:number;text:string;done:boolean;doneBy:number|null;doneUtc:string|null};
type SrvNote={noteId:number;ref:string;desk:string;date:string;shift:string;toShift:string;authorId:number;writtenUtc:string;issues:string;nothing:boolean;keys:string;guests:string;
  cash:{amountPaise:number|null;note:string|null}|null;carry:SrvCarry[];status:string;ackBy:number|null;ackUtc:string|null;ackRemark:string|null;version:string};

const roleName=(code:string):Role=>(ROLE_MAP[code]??code) as Role;
const roleCode=(r:string):string=>Object.entries(ROLE_MAP).find(([,v])=>v===r)?.[0]??r;
const item=(i:SrvItem)=>({id:i.key,text:i.text,mandatory:i.mandatory,...(i.due?{due:i.due}:{}),...(i.photo?{photo:true}:{}),...(i.link?{link:i.link}:{})});

export function templateFromServer(t:SrvTemplate):HOVTemplate{
  return {id:String(t.templateId),name:t.name,desk:t.desk as HOVDesk,shift:t.shift as HOVShift,roles:t.roles.map(roleName),items:t.items.map(item),active:t.active,version:t.templateVersion};
}
export function runFromServer(r:SrvRun):HOVRun{
  const submitted=r.status==='Submitted';
  const out:HOVRun={id:`HOVR-${r.templateId}-${r.date}`,template:String(r.templateId),templateVersion:r.templateVersion,name:r.name,desk:r.desk as HOVDesk,shift:r.shift as HOVShift,
    roles:r.roles.map(roleName),date:r.date,status:submitted?'Submitted':'Open',startedBy:r.startedBy==null?'':String(r.startedBy),startedAt:'',
    items:r.items.map(i=>{const x:HOVRunItem={...item(i),done:i.done};
      if(i.doneBy!=null)x.by=String(i.doneBy);if(i.doneUtc)x.at=utcToClock(i.doneUtc);if(i.note)x.note=i.note;if(i.photoName)x.photoName=i.photoName;return x;})};
  if(submitted){out.submittedBy=r.submittedBy==null?undefined:String(r.submittedBy);out.submittedAt=r.submittedUtc?utcToClock(r.submittedUtc):undefined;out.late=r.progress.late;}
  if(r.reopenReason)out.reopenReason=r.reopenReason;
  return out;
}
export function noteFromServer(n:SrvNote):HOVNote{
  const out:HOVNote={id:n.ref,desk:n.desk as HOVDesk,date:n.date,shift:n.shift as HOVShift,toShift:n.toShift as HOVShift,author:String(n.authorId),at:utcToClock(n.writtenUtc),
    issues:n.issues,keys:n.keys,guests:n.guests,carry:n.carry.map((c,i)=>({id:`${n.ref}-c${i+1}`,text:c.text,done:c.done,
      ...(c.doneBy!=null?{doneBy:String(c.doneBy)}:{}),...(c.doneUtc?{doneAt:utcToClock(c.doneUtc)}:{})})),status:n.status as HOVNote['status']};
  if(n.nothing)out.nothing=true;
  // A masked cash line (reader without a money permission) arrives without an amount: it stays absent, never reconstructed.
  if(n.cash&&n.cash.amountPaise!=null)out.cash={amount:n.cash.amountPaise,note:n.cash.note??''};
  if(n.ackBy!=null)out.ackBy=String(n.ackBy);
  if(n.ackUtc)out.ackAt=utcToClock(n.ackUtc);
  if(n.ackRemark)out.ackRemark=n.ackRemark;
  return out;
}

async function soft<T>(p:Promise<T>,fallback:T):Promise<T>{
  try{return await p;}catch(e){if(e instanceof HccClientError&&e.wireCode==='FORBIDDEN')return fallback;throw e;}
}

async function loadHandover(c:LoaderCtx):Promise<HOVState>{
  const [cl,tl,nl]=await Promise.all([
    c.api.get<{date:string;runs:SrvRun[]}>('/handover/checklists'),
    c.mayRead('GET /handover/templates')?soft(c.api.get<{templates:SrvTemplate[]}>('/handover/templates'),null):null,
    c.mayRead('GET /handover/notes')?soft(c.api.get<{notes:SrvNote[]}>('/handover/notes'),{notes:[]}):{notes:[] as SrvNote[]},
  ]);
  const templates=new Map<string,HOVTemplate>();
  // Cards first (what this user may use)…
  for(const r of cl.runs){
    templates.set(String(r.templateId),{id:String(r.templateId),name:r.name,desk:r.desk as HOVDesk,shift:r.shift as HOVShift,roles:r.roles.map(roleName),items:r.items.map(item),
      active:true,version:r.templateVersion});
  }
  // …then the manager's full list (inactive ones, current items, versions).
  if(tl)for(const t of tl.templates){c.ids.setVersion('hovTemplate',String(t.templateId),t.version);templates.set(String(t.templateId),templateFromServer(t));}
  const runs=cl.runs.filter(r=>r.started&&r.runId!=null).map(runFromServer);
  const notes=nl.notes.map(n=>{
    c.ids.learn('hovNote',n.ref,n.noteId);
    n.carry.forEach((x,i)=>c.ids.learn('hovCarry',`${n.ref}-c${i+1}`,x.carryId));
    return noteFromServer(n);
  });
  return {templates:[...templates.values()],runs,notes};
}

const need=(c:CommandCtx,kind:string,id:unknown,what:string)=>{
  const v=c.ids.server(kind,String(id??''));
  if(v===undefined)throw new HccClientError('NOT_FOUND',`${what} not found. Refresh and try again.`);
  return v;
};
const ver=(c:CommandCtx,id:unknown):string=>{
  const v=c.ids.version('hovTemplate',String(id));
  if(!v)throw new HccClientError('VALIDATION','The template version is not loaded from the server. Refresh and try again.');
  return v;
};
const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
type Cmd=CommandCtx['cmd'];

export const handover:SyncArea={
  id:'handover',
  loaders:[{slice:'handoverX',requires:['housekeeping','payments','approve'],reads:['GET /handover/checklists'],load:loadHandover}],
  commands:[
    {type:'HOV_TEMPLATE_SAVE',reload:['handoverX'],
      precheck:(cmd:Cmd)=>txt(cmd.template?.name).length<3?'Template name must be 3–60 characters.':undefined,
      async send(c){
        const t=c.cmd.template??{};
        const body:Record<string,unknown>={name:txt(t.name),desk:t.desk,shift:t.shift,roles:(Array.isArray(t.roles)?t.roles:[]).map(roleCode),
          items:(Array.isArray(t.items)?t.items:[]).map((i:{text:string;mandatory?:boolean;due?:string;photo?:boolean;link?:string})=>({text:txt(i.text),mandatory:!!i.mandatory,due:txt(i.due)||undefined,photo:!!i.photo,link:txt(i.link)||undefined}))};
        if(t.id){body.templateId=serverId(t.id,'Template');await c.api.command('SaveHandoverTemplate',body,{expectedVersion:ver(c,t.id)});}
        else await c.api.command('SaveHandoverTemplate',body);
      }},
    {type:'HOV_TEMPLATE_TOGGLE',reload:['handoverX'],
      async send(c){await c.api.command('ToggleHandoverTemplate',{templateId:serverId(c.cmd.id,'Template')},{expectedVersion:ver(c,c.cmd.id)});}},
    {type:'HOV_ITEM',reload:['handoverX'],
      async send(c){await c.api.command('TickChecklistItem',{templateId:serverId(c.cmd.template,'Template'),item:String(c.cmd.item),
        done:c.cmd.done===undefined?undefined:!!c.cmd.done,note:c.cmd.note===undefined?undefined:txt(c.cmd.note),photoName:c.cmd.photo===undefined?undefined:txt(c.cmd.photo)});}},
    {type:'HOV_RUN_SUBMIT',reload:['handoverX'],async send(c){await c.api.command('SubmitChecklistRun',{templateId:serverId(c.cmd.template,'Template')});}},
    {type:'HOV_RUN_REOPEN',reload:['handoverX'],
      precheck:(cmd:Cmd)=>txt(cmd.reason).length>=3?undefined:'A reason is required to reopen a checklist.',
      async send(c){await c.api.command('ReopenChecklistRun',{templateId:serverId(c.cmd.template,'Template'),reason:txt(c.cmd.reason),date:txt(c.cmd.date)||undefined});}},
    {type:'HOV_NOTE_SUBMIT',reload:['handoverX'],
      async send(c){
        const k=c.cmd;const cash=k.cash&&k.cash.amount!==undefined&&k.cash.amount!==null?{amountPaise:k.cash.amount,note:txt(k.cash.note)||undefined}:undefined;
        await c.api.command('SubmitHandoverNote',{desk:k.desk,shift:k.shift||undefined,issues:txt(k.issues),nothing:!!k.nothing,keys:txt(k.keys),guests:txt(k.guests),cash,
          carry:(Array.isArray(k.carry)?k.carry:[]).map(txt).filter(Boolean)});
      }},
    {type:'HOV_NOTE_ACK',reload:['handoverX'],
      async send(c){await c.api.command('AcknowledgeHandoverNote',{noteId:need(c,'hovNote',c.cmd.id,'Handover note'),remark:txt(c.cmd.remark)||undefined});}},
    {type:'HOV_CARRY_DONE',reload:['handoverX'],
      async send(c){await c.api.command('CompleteCarryItem',{noteId:need(c,'hovNote',c.cmd.note,'Handover note'),carryId:need(c,'hovCarry',c.cmd.item,'Carry-forward item')});}},
  ],
  unmapped:{HOV_CLOCK:'demo-only simulated clock; production uses the real property clock (the server has no such command by design)'},
};

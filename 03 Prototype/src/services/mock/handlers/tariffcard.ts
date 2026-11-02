// F4-T1 · Tariff card commands (F4-CONTRACT.md §1.5). Command types start with 'TRC_'. Return value = audit text.
// TRC_DRAFT_SAVE (Owner / Manager) · TRC_PROPOSE (Owner / Manager → Approval kind 'tariff-card') · TRC_PUBLISH (Owner; writes the
// existing masters dated effectiveFrom in this one command, one audit row per master row + the TRC_PUBLISH row) · TRC_REJECT (Owner,
// reason) · TRC_DISCARD (draft only) · TRC_WEEKEND_SET (Owner) · TRC_IMPORT (Owner; DEC-198 CSVs → a draft).
import type {Handler,Kit} from '../commands';
import type {TRCState,TRCVersion} from '../../../domain/types';
import {addMinutes} from '../../../domain/money';
import {trcCan,trcEmpty,trcPriceLine,trcCheckDraft,trcPublishPlan,trcImportLines,trcFail,TRC_APPROVAL_KIND,TRC_WEEKEND_DEFAULT,type TrcAction,type TrcLineInput} from '../../../domain/streams/tariffcard';

const slice=(k:Kit):TRCState=>{const s=k.s;const x=s.tariffCardX??(s.tariffCardX=trcEmpty());x.versions??=[];if(!x.weekendDaysDefault?.length)x.weekendDaysDefault=[...TRC_WEEKEND_DEFAULT];return x;};
const gate=(k:Kit,a:TrcAction)=>{if(!trcCan(k.s,k.uid,a))k.fail('FORBIDDEN',a==='publish'?'Only the Owner publishes, rejects or imports a tariff card (DEC-058). A Manager can propose.':
  a==='propose'?'Only the Owner or a Manager drafts and proposes tariff cards.':'The tariff card is not available to your role.');};
const find=(k:Kit,id:unknown):TRCVersion=>{const v=slice(k).versions.find(x=>x.id===id);if(!v)k.fail('NOT_FOUND','Tariff card version not found.');return v!;};
const editable=(v:TRCVersion)=>{if(v.status!=='draft'&&v.status!=='proposed')trcFail('TRC_PUBLISHED_READONLY',`${v.id} is ${v.status}; published cards are never edited — make a new version with a later date.`);};
const nextId=(x:TRCState)=>'TRC-'+String(x.versions.reduce((m,v)=>Math.max(m,Number(v.id.slice(4))||0),0)+1).padStart(4,'0');
const priced=(k:Kit,lines:unknown,from:string)=>{
  if(!Array.isArray(lines))k.fail('VALIDATION','The card needs its lines.');
  return (lines as TrcLineInput[]).map((l,i)=>trcPriceLine(k.s,l,from,'L'+String(i+1).padStart(3,'0')));
};
const days=(k:Kit,v:unknown)=>{const d=Array.isArray(v)?v as number[]:undefined;
  k.assert(d&&d.every(x=>Number.isInteger(x)&&x>=0&&x<=6)&&new Set(d).size===d.length&&d.length<7,'Weekend nights: pick up to six days of the week.');return [...d!];};

const TRC_DRAFT_SAVE:Handler=k=>{
  const {s,cmd}=k;gate(k,'propose');const x=slice(k);
  const from=String(cmd.effectiveFrom??'');const label=String(cmd.label??'').trim().slice(0,60)||`Tariff card from ${from}`;
  if(cmd.id){
    const v=find(k,cmd.id);editable(v);
    const weekend=cmd.weekendDays!==undefined?days(k,cmd.weekendDays):v.weekendDays;
    const lines=priced(k,cmd.lines??v.lines,from||v.effectiveFrom);
    const next={...v,label:cmd.label!==undefined?label:v.label,effectiveFrom:from||v.effectiveFrom,weekendDays:weekend,lines,notes:cmd.notes!==undefined?String(cmd.notes).slice(0,500):v.notes};
    trcCheckDraft(s,next);
    if(v.status==='proposed'){ // an edit withdraws the proposal: the Owner decides what was proposed, never a moved target
      const a=s.approvals.find(a=>a.id===v.approvalId&&a.status==='Pending');if(a){a.status='Invalidated';a.decidedAt=addMinutes(s.clock,1);}
      next.status='draft';next.approvalId=undefined;next.proposedBy=undefined;
    }
    Object.assign(v,next);
    return `${v.id} · draft saved · from ${v.effectiveFrom} · ${v.lines.length} lines`;
  }
  const weekend=cmd.weekendDays!==undefined?days(k,cmd.weekendDays):[...x.weekendDaysDefault];
  const v:TRCVersion={id:nextId(x),label,effectiveFrom:from,status:'draft',weekendDays:weekend,lines:priced(k,cmd.lines??[],from),by:k.user.name,at:addMinutes(s.clock,1),
    ...(cmd.notes?{notes:String(cmd.notes).slice(0,500)}:{})};
  trcCheckDraft(s,v);x.versions.push(v);
  return `${v.id} · draft created · from ${v.effectiveFrom} · ${v.lines.length} lines`;
};
const TRC_PROPOSE:Handler=k=>{
  const {s,cmd,uid}=k;gate(k,'propose');const v=find(k,cmd.id);
  if(v.status!=='draft')trcFail('TRC_PUBLISHED_READONLY',`${v.id} is ${v.status}; only a draft can be proposed.`);
  const plan=trcPublishPlan(s,v); // refuse now what the Owner could not publish
  const aid=k.id('APR');
  s.approvals.push({id:aid,booking:'',amount:0,reason:String(cmd.reason??'').trim()||`Publish tariff card ${v.id} from ${v.effectiveFrom}`,status:'Pending',actor:uid,version:1,
    kind:TRC_APPROVAL_KIND,target:v.id,expiresAt:addMinutes(s.clock,(s.settings.approvalExpiryHours??24)*60),
    meta:{command:'TRC_PUBLISH',version:v.id,effectiveFrom:v.effectiveFrom,writes:plan.writes.length,summary:`Tariff card ${v.id} · ${v.label} · from ${v.effectiveFrom} · ${plan.writes.length} master rows`}});
  v.status='proposed';v.approvalId=aid;v.proposedBy=k.user.name;
  return `${v.id} · proposed for Owner approval (${aid}) · ${plan.writes.length} master rows from ${v.effectiveFrom}`;
};
const TRC_PUBLISH:Handler=k=>{
  const {s,cmd,uid}=k;gate(k,'publish');const x=slice(k);const v=find(k,cmd.id);
  if(v.status!=='draft'&&v.status!=='proposed')trcFail('TRC_PUBLISHED_READONLY',`${v.id} is already ${v.status}.`);
  const plan=trcPublishPlan(s,v);
  const at=addMinutes(s.clock,1);
  // one command = one transaction: the schedule, the dated master money and the card snapshot change together
  s.tariffs=plan.tariffs.map(t=>({type:t.type,rate:t.rate,effectiveFrom:t.effectiveFrom,...(t.days&&t.days!=='all'?{days:t.days}:{}),...(t.cardVersion?{cardVersion:t.cardVersion}:{})}));
  if(Object.keys(plan.masters).length){const mx=s.mastersX??(s.mastersX={rows:{},history:[]});mx.rows??={};mx.history??=[];
    for(const [id,rows] of Object.entries(plan.masters))mx.rows[id]=rows;
    for(const w of plan.writes.filter(w=>w.master!=='tariffs'))mx.history.unshift({id:k.id('MSTH'),master:w.master,rowId:w.ref.split('|')[0],code:w.ref.split('|')[0],field:w.field??'',
      old:w.before===undefined?'':String(w.before),new:String(w.after),by:k.user.name,at});}
  for(const w of plan.writes)s.audit.unshift({id:k.id('audit'),at,actor:k.user.name,action:'TRC PUBLISH ROW',detail:`${v.id} · ${w.master} · ${w.label} · ${w.before===undefined?'new':w.before/100} → ${w.after/100}`});
  // supersede: earlier published versions effective on or before this one's date stop being the live card from that date
  for(const o of x.versions)if(o!==v&&o.status==='published'&&o.effectiveFrom<=v.effectiveFrom){o.status='superseded';o.supersededBy=v.id;}
  if(v.approvalId){const a=s.approvals.find(a=>a.id===v.approvalId);if(a&&a.status==='Pending'){a.status='Executed';a.approver=uid;a.decidedAt=at;}}
  v.status='published';v.publishedBy=k.user.name;v.publishedAt=at;v.publishLog=plan.writes.map(({master,ref,before,after})=>({master,ref,...(before!==undefined?{before}:{}),after}));
  const po=plan.printOnly.length?` · print only: ${plan.printOnly.map(p=>p.line).join(', ')}`:'';
  return `${v.id} published · from ${v.effectiveFrom} · ${plan.writes.length} master rows written${po} · existing bookings keep their rates`;
};
const TRC_REJECT:Handler=k=>{
  const {s,cmd,uid}=k;gate(k,'publish');const v=find(k,cmd.id);
  if(v.status!=='proposed')trcFail('TRC_PUBLISHED_READONLY',`${v.id} is ${v.status}; only a proposed card can be rejected.`);
  const reason=String(cmd.reason??'').trim();k.assert(reason.length>=3,'Give the reason for rejecting.');
  const a=s.approvals.find(a=>a.id===v.approvalId);if(a&&a.status==='Pending'){a.status='Rejected';a.approver=uid;a.decidedAt=addMinutes(s.clock,1);a.meta={...a.meta,rejectReason:reason};}
  v.status='rejected';v.notes=((v.notes?v.notes+' · ':'')+'Rejected: '+reason).slice(0,500);
  return `${v.id} rejected · ${reason}`;
};
const TRC_DISCARD:Handler=k=>{
  gate(k,'propose');const x=slice(k);const v=find(k,k.cmd.id);
  if(v.status!=='draft')trcFail('TRC_PUBLISHED_READONLY',`${v.id} is ${v.status}; only a draft can be discarded.`);
  x.versions=x.versions.filter(o=>o!==v);return `${v.id} draft discarded`;
};
const TRC_WEEKEND_SET:Handler=k=>{
  gate(k,'publish');const x=slice(k);const d=days(k,k.cmd.days);
  if(k.cmd.id){const v=find(k,k.cmd.id);editable(v);v.weekendDays=d;return `${v.id} · weekend nights ${d.join(',')}`;}
  x.weekendDaysDefault=d;return `Weekend nights for new cards: ${d.join(',')}`;
};
const TRC_IMPORT:Handler=k=>{
  const {s,cmd}=k;gate(k,'publish');const x=slice(k);
  const from=String(cmd.effectiveFrom??'2026-11-01');
  const lines=priced(k,trcImportLines(s,cmd.files??{},from),from);
  const v:TRCVersion={id:nextId(x),label:String(cmd.label??'').trim().slice(0,60)||'Imported rate card (DEC-198)',effectiveFrom:from,status:'draft',weekendDays:[...x.weekendDaysDefault],
    lines,by:k.user.name,at:addMinutes(s.clock,1),notes:'Imported from the rate-card CSVs; child price blank until the Owner gives it.'};
  trcCheckDraft(s,v);x.versions.push(v);
  return `${v.id} · imported draft · from ${from} · ${lines.length} lines`;
};

export const handlers:Record<string,Handler>={TRC_DRAFT_SAVE,TRC_PROPOSE,TRC_PUBLISH,TRC_REJECT,TRC_DISCARD,TRC_WEEKEND_SET,TRC_IMPORT};

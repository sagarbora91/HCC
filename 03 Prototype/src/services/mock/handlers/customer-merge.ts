// FX merge-ui · Customers 360 merge — command handlers. Command types MUST start with 'CUSTMERGE_'. Return value = audit text.
//   CUSTMERGE_PROPOSE {source, target, reason}   source = the duplicate (folded into target); Owner / Manager ('approve'), like PERM-CRM-003 on the server
//   CUSTMERGE_DECIDE  {id, approve, note?, version?}   a DIFFERENT Owner / Manager decides (maker-checker); approval makes source an alias of target
//   CUSTMERGE_DISMISS {a, b}   "Not the same person": the pair is not suggested again (demo only; the server has no such command)
import type {Handler,Kit} from '../commands';
import {customerIndex} from '../../../domain/streams/customers';
import {cmxSlice,cmxPairId,cmxSafeName,CMX_REASON_MIN,CMX_REASON_MAX,CMX_SAME_TEXT,CMX_BUSY_TEXT,CMX_MAKER_CHECKER_TEXT,CMX_LATE_TEXT,type CMXMerge} from '../../../domain/streams/customer-merge';

const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
const own=(k:Kit)=>{k.req('approve');const x=cmxSlice(k.s);k.s.customerMergeX=x;return x;};
const clip=(t:string)=>t.length<=500?t:t.slice(0,500);

export const handlers:Record<string,Handler>={
  CUSTMERGE_PROPOSE:k=>{
    const x=own(k),source=txt(k.cmd.source),target=txt(k.cmd.target),reason=txt(k.cmd.reason);
    k.assert(reason.length>=CMX_REASON_MIN&&reason.length<=CMX_REASON_MAX,`Give a reason of ${CMX_REASON_MIN}-${CMX_REASON_MAX} characters.`,'VALIDATION');
    const idx=customerIndex(k.s),a=idx.find(c=>c.key===source),b=idx.find(c=>c.key===target);
    k.assert(a&&b,'Customer not found.','NOT_FOUND');
    k.assert(source!==target,CMX_SAME_TEXT,'VALIDATION');
    k.assert(!x.merges.some(m=>m.status==='Pending'&&[m.source,m.target].some(y=>y===source||y===target)),CMX_BUSY_TEXT,'INVALID_TRANSITION');
    const seq=x.seq+1,m:CMXMerge={id:'CM-'+seq,source,target,sourceName:a!.name,targetName:b!.name,reason,status:'Pending',proposedBy:k.uid,proposedAt:k.s.clock,version:'1'};
    k.s.customerMergeX={...x,seq,merges:[m,...x.merges]};
    return `merge ${cmxSafeName(a!.name)} (${a!.stays.length} stays) into ${cmxSafeName(b!.name)} (${b!.stays.length} stays) · ${reason}`;
  },
  CUSTMERGE_DECIDE:k=>{
    const x=own(k),note=txt(k.cmd.note);
    k.assert(note.length<=CMX_REASON_MAX,`The note is limited to ${CMX_REASON_MAX} characters.`,'VALIDATION');
    const m=x.merges.find(y=>y.id===k.cmd.id);k.assert(m,'Merge proposal not found.','NOT_FOUND');
    k.assert(k.cmd.version===undefined||String(k.cmd.version)===m!.version,'This merge proposal changed. Reload and try again.','STALE_VERSION');
    k.assert(m!.status==='Pending',`This merge was already ${m!.status.toLowerCase()}.`,'INVALID_TRANSITION');
    k.assert(m!.proposedBy!==k.uid,CMX_MAKER_CHECKER_TEXT,'FORBIDDEN');
    const sn=cmxSafeName(m!.sourceName),tn=cmxSafeName(m!.targetName);
    const done={...m!,decidedBy:k.uid,decidedAt:k.s.clock,...(note?{note}:{}),version:'2'};
    if(!k.cmd.approve){
      k.s.customerMergeX={...x,merges:x.merges.map(y=>y===m?{...done,status:'Rejected' as const}:y)};
      return `merge of ${sn} into ${tn} rejected${note?' · '+note:''}`;
    }
    const idx=customerIndex(k.s),a=idx.find(c=>c.key===m!.source),b=idx.find(c=>c.key===m!.target);
    k.assert(a&&b,CMX_LATE_TEXT,'INVALID_TRANSITION');
    // preferences: the survivor's are kept; a different text of the duplicate is kept as a note (server ApproveMerge)
    const ga=a!.guest&&k.s.guests?.find(g=>g.id===a!.guest!.id),gb=b!.guest&&k.s.guests?.find(g=>g.id===b!.guest!.id);
    let preferences:'kept'|'copied'|'noted'|'none'='none',notes=false;
    const pa=ga?.preferences?.trim()??'',pb=gb?.preferences?.trim()??'';
    if(gb&&pa){
      if(!pb){gb.preferences=pa;preferences='copied';}
      else if(pa!==pb){gb.notes=clip((gb.notes?gb.notes+' | ':'')+`Preferences before merge (${sn}): ${pa}`);preferences='noted';}
      else preferences='kept';
    }else if(pb)preferences='kept';
    if(gb&&ga?.notes?.trim()&&ga!==gb){gb.notes=clip((gb.notes?gb.notes+' | ':'')+`Notes before merge (${sn}): ${ga.notes.trim()}`);notes=true;}
    const aliases={...x.aliases};
    for(const [k2,v] of Object.entries(aliases))if(v===m!.source)aliases[k2]=m!.target;
    aliases[m!.source]=m!.target;
    k.s.customerMergeX={...x,aliases,merges:x.merges.map(y=>y===m?{...done,status:'Approved' as const,moved:{stays:a!.stays.length,legacy:a!.legacyStays.length,preferences,notes}}:y)};
    return `${sn} merged into ${tn} · history kept${note?' · '+note:''}`;
  },
  CUSTMERGE_DISMISS:k=>{
    const x=own(k),a=txt(k.cmd.a),b=txt(k.cmd.b);
    k.assert(a&&b&&a!==b,CMX_SAME_TEXT,'VALIDATION');
    const id=cmxPairId(a,b);
    k.assert(!x.dismissed.includes(id),'This pair is already dismissed.','INVALID_TRANSITION');
    k.s.customerMergeX={...x,dismissed:[...x.dismissed,id]};
    return `pair ${id.replace(/\d{8,}/g,'***')} dismissed: not the same person`;
  },
};

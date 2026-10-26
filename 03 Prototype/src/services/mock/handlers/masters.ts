// MA-0 · Masters framework — generic command handlers (plan Part 2 §9.2). One set for every registered master
// (domain/streams/masters.ts): MST_MASTER_SAVE {master,row} (create when row.id is absent, else edit), MST_MASTER_RETIRE {master,id},
// MST_MASTER_REACTIVATE {master,id}, MST_MASTER_REORDER {master,ids}, MST_MASTER_DELETE {master,id} (only when never used).
// Permission from the spec (`masters` for operational lists; Owner for money lists and for any money field, DEC-058); typed
// master errors (MstError: MASTER_CODE_TAKEN / _IN_USE / _RETIRED / _INVALID); audit "Master <label>: <field> old → new";
// one history entry per changed field; idempotency keys through execute(). Masters are configuration: no business-day check.
// A module may expose the same commands under its own prefix: `...mstHandlersFor('RMS')` → RMS_MASTER_SAVE … in its handler file.
import type {Handler,Kit} from '../commands';
import type {State} from '../../../domain/types';
import {can} from '../../../domain/selectors';
import {HccError} from '../../../domain/errors';
import {mstImportOrder,mstIoKeys,mstMergeDated,mstImportCounts,MST_IO_TOGGLE_DEFAULT,type MstIoSet,type MstIoRow,type MstImportItem,type MstImportReport} from '../../../domain/streams/mastersio';
import {
  mstSpec,mstRows,mstStored,mstClean,mstProblems,mstUsage,mstCanEdit,mstCanEditMoney,mstMoneyFields,mstNextSort,mstFail,mstShow,MST_OWNER_ONLY,
  type MasterSpec,type MasterRow,type MSTState,
} from '../../../domain/streams/masters';

const slice=(s:State):MSTState=>{const x=s.mastersX??(s.mastersX={rows:{},history:[]});x.rows??={};x.history??=[];return x;};
/** The spec of cmd.master, with the user's permission checked. */
function specFor(k:Kit):MasterSpec{
  // Coarse gate first (masters, or the Owner for money lists), so a refused user never learns about payload problems.
  if(!can(k.s,k.uid,'masters')&&!mstCanEditMoney(k.s,k.uid))k.fail('FORBIDDEN','Access denied: masters permission is required.');
  const sp=mstSpec(String(k.cmd.master??''));
  if(!sp)k.fail('VALIDATION','Choose a list (unknown master).');
  const d=mstCanEdit(k.s,k.uid,sp!);
  if(!d.ok)k.fail('FORBIDDEN',`Access denied: ${d.reason}`);
  return sp!;
}
/** Current rows for writing: the stored list, materialised from the seed on the first change. */
function rowsFor(k:Kit,sp:MasterSpec):MasterRow[]{
  if(sp.store)return sp.store.read(k.s).map(r=>({...r}));
  const x=slice(k.s);
  if(!mstStored(k.s,sp.id))x.rows[sp.id]=(sp.seed?.(k.s)??[]).map(r=>({...r}));
  return x.rows[sp.id];
}
function writeRows(k:Kit,sp:MasterSpec,rows:MasterRow[]){if(sp.store)sp.store.write(k.s,rows);else slice(k.s).rows[sp.id]=rows;}
const show=(sp:MasterSpec,key:string,v:unknown,s:State)=>{
  if(key==='active')return v?'active':'retired';
  const f=sp.fields.find(x=>x.key===key);return f?mstShow(f,v,s):v===undefined||v===null||v===''?'—':String(v);
};
const same=(a:unknown,b:unknown)=>JSON.stringify(a??null)===JSON.stringify(b??null);
/** History + audit text for the changed fields of one row. */
function record(k:Kit,sp:MasterSpec,before:MasterRow|undefined,after:MasterRow,keys:string[]):string[]{
  const x=slice(k.s),parts:string[]=[];
  for(const key of keys){
    const o=before?.[key],n=after[key];if(before&&same(o,n))continue;
    const ov=before?show(sp,key,o,k.s):'—',nv=show(sp,key,n,k.s);
    x.history.unshift({id:k.id('MSTH'),master:sp.id,rowId:after.id,code:after.code,field:key,old:ov,new:nv,by:k.uid,at:k.s.clock});
    parts.push(`${key} ${ov} → ${nv}`);
  }
  return parts;
}
const findRow=(k:Kit,sp:MasterSpec,rows:MasterRow[],id:unknown)=>{const r=rows.find(x=>x.id===id);if(!r)k.fail('NOT_FOUND',`This ${sp.singular} was not found.`);return r!;};

function save(k:Kit):string{
  const sp=specFor(k),rows=rowsFor(k,sp),input=(k.cmd.row??{}) as Record<string,unknown>;
  const old=input.id?findRow(k,sp,rows,input.id):undefined;
  if(old&&!old.active)mstFail('MASTER_RETIRED',`${old.name} is retired — reactivate it before changing it.`);
  const next=mstClean(sp,input,old);
  next.id=old?.id??k.id(sp.prefix);
  next.active=old?.active??true;
  if(next.sortOrder===undefined||Number.isNaN(next.sortOrder))next.sortOrder=old?.sortOrder??mstNextSort(rows);
  if(next.retiredOn===undefined)delete next.retiredOn;
  // Money fields: Owner only (DEC-058), also inside an operational list.
  const moneyChanged=mstMoneyFields(sp).filter(f=>old?!same(old[f.key],next[f.key]):next[f.key]!==undefined&&!(Array.isArray(next[f.key])&&!(next[f.key] as unknown[]).length));
  if(moneyChanged.length&&!mstCanEditMoney(k.s,k.uid))k.fail('FORBIDDEN',`Access denied: ${MST_OWNER_ONLY} (${moneyChanged.map(f=>f.label).join(', ')})`);
  // Code is immutable once the row is in use.
  if(old&&old.code!==next.code){const u=mstUsage(k.s,sp,old);if(u.count>0)mstFail('MASTER_IN_USE',`Code ${old.code} is used (${u.count} · ${u.where.join('; ')}) and cannot change.`,'code');}
  const p=mstProblems(sp,next,rows,k.s,old)[0];
  if(p)mstFail(p.reason,p.message,p.field);
  const keys=['code','name','sortOrder',...sp.fields.map(f=>f.key).filter(x=>!['code','name','sortOrder','active'].includes(x))];
  if(old&&keys.every(key=>same(old[key],next[key])))k.fail('VALIDATION','Nothing changed.');
  const parts=record(k,sp,old,next,keys);
  writeRows(k,sp,old?rows.map(r=>r.id===old.id?next:r):[...rows,next]);
  return old?`Master ${sp.label}: ${parts.join('; ')}`:`Master ${sp.label}: added ${next.code} · ${next.name}`;
}
function setActive(k:Kit,active:boolean):string{
  const sp=specFor(k),rows=rowsFor(k,sp),row=findRow(k,sp,rows,k.cmd.id);
  if(row.active===active)k.fail('INVALID_TRANSITION',active?`${row.name} is already active.`:`${row.name} is already retired.`);
  if(!active){const why=sp.canRetire?.(k.s,row);if(why)mstFail('MASTER_IN_USE',why);}
  else{const clash=rows.find(r=>r.id!==row.id&&r.active&&r.name.toLowerCase()===row.name.toLowerCase());if(clash)mstFail('MASTER_INVALID',`Another active ${sp.singular} is already called “${row.name}” — rename one first.`);}
  const next:MasterRow={...row,active};if(active)delete next.retiredOn;else next.retiredOn=k.s.date;
  record(k,sp,row,next,['active']);
  writeRows(k,sp,rows.map(r=>r.id===row.id?next:r));
  const u=mstUsage(k.s,sp,row);
  return `Master ${sp.label}: active ${row.active?'yes':'no'} → ${active?'yes':'no'} (${row.code} · ${row.name} ${active?'reactivated':'retired'}${!active&&u.count?`; still used: ${u.count}, old records keep the name`:''})`;
}
function reorder(k:Kit):string{
  const sp=specFor(k),rows=rowsFor(k,sp),ids=Array.isArray(k.cmd.ids)?(k.cmd.ids as unknown[]).map(String):[];
  k.assert(ids.length===rows.length&&new Set(ids).size===ids.length&&rows.every(r=>ids.includes(r.id)),`Send every ${sp.singular} once in the new order.`);
  const next=rows.map(r=>({...r,sortOrder:(ids.indexOf(r.id)+1)*10}));
  const moved:string[]=[];
  for(const r of next){const o=rows.find(x=>x.id===r.id)!;if(o.sortOrder!==r.sortOrder){record(k,sp,o,r,['sortOrder']);moved.push(r.code);}}
  k.assert(moved.length>0,'The order did not change.');
  writeRows(k,sp,next);
  return `Master ${sp.label}: sortOrder changed for ${moved.join(', ')} (new order ${[...next].sort((a,b)=>a.sortOrder-b.sortOrder).map(r=>r.code).join(', ')})`;
}
function remove(k:Kit):string{
  const sp=specFor(k),rows=rowsFor(k,sp),row=findRow(k,sp,rows,k.cmd.id);
  const u=mstUsage(k.s,sp,row);
  if(u.count>0)mstFail('MASTER_IN_USE',`${row.name} is used (${u.count} · ${u.where.join('; ')}) — retire it instead of deleting.`);
  slice(k.s).history.unshift({id:k.id('MSTH'),master:sp.id,rowId:row.id,code:row.code,field:'deleted',old:row.name,new:'—',by:k.uid,at:k.s.clock});
  writeRows(k,sp,rows.filter(r=>r.id!==row.id));
  return `Master ${sp.label}: ${row.code} · ${row.name} deleted (never used)`;
}

// ───────────────────────────── MA-8 · MST_IMPORT {sets:[{master,rows}]} (masters JSON import and "Paste rows") ─────────────────────────────
// Every row goes through the same save / reactivate logic as the list's own form (permissions, code rules, validation, history, audit).
// Rows are matched by code: a new code adds a row, a known code is updated; a file never retires an active row. All or nothing: a row error
// refuses the whole command. {dryRun:true} runs the same code and throws MstImportPreview (the report) instead of applying it.
export class MstImportPreview extends HccError{constructor(public readonly report:MstImportReport){super('VALIDATION','Preview only.');this.name='MstImportPreview';}}
const truthy=(v:unknown)=>v===true||(typeof v==='string'&&['yes','y','true','1'].includes(v.trim().toLowerCase()));
const falsy=(v:unknown)=>v===false||(typeof v==='string'&&['no','n','false','0'].includes(v.trim().toLowerCase()));
function importRows(k:Kit,sets:MstIoSet[]):MstImportReport{
  const items:MstImportItem[]=[],skippedMasters:{master:string;reason:string}[]=[];
  const order=mstImportOrder(sets.map(x=>x.master));
  for(const id of order){
    const set=sets.find(x=>x.master===id)!,sp=mstSpec(id);
    if(!sp){skippedMasters.push({master:id,reason:'Not in this registry'});continue;}
    const d=mstCanEdit(k.s,k.uid,sp);
    if(!d.ok){skippedMasters.push({master:id,reason:d.reason??'Not permitted'});continue;}
    const keys=mstIoKeys(sp),seen=new Set<string>();
    for(const [n,raw] of (Array.isArray(set.rows)?set.rows:[]).entries()){
      const row=(raw&&typeof raw==='object'?raw:{}) as MstIoRow;
      const code=String(row.code??'').trim().toUpperCase(),name=String(row.name??'').trim();
      const line=n+1;
      const put=(action:MstImportItem['action'],detail:string)=>items.push({master:id,code,name:name||code,action,detail,line});
      if(!code){put('error','Enter a code.');continue;}
      if(seen.has(code)){put('error',`Duplicate of an earlier row (${code}).`);continue;}
      seen.add(code);
      const rows=mstRows(k.s,id),old=rows.find(r=>r.code===code);
      const input:Record<string,unknown>={};
      for(const [key,v] of Object.entries(row))if(keys.has(key)&&key!=='active'&&key!=='code'&&v!==undefined)input[key]=v;
      input.code=code;
      if(!name&&sp.autoName&&!old)input.name=sp.autoName(code); // V14-7: rooms are named after their number
      if(!old)for(const f of sp.fields)if(f.type==='toggle'&&!(f.key in input)&&f.key in MST_IO_TOGGLE_DEFAULT)input[f.key]=MST_IO_TOGGLE_DEFAULT[f.key]; // a new row takes the field default
      if(!('name' in input)&&old)input.name=old.name;
      for(const f of sp.fields)if(f.type==='datedMoney'&&f.key in input)input[f.key]=mstMergeDated(old?.[f.key],input[f.key],k.s.date,set.source==='csv');
      const wantActive=truthy(row.active)?true:falsy(row.active)?false:undefined;
      const sub=(cmd:Record<string,unknown>)=>({...k,cmd:{...k.cmd,...cmd}}) as Kit;
      try{
        if(old&&old.active&&wantActive===false){put('error',`${old.name} is active. A file does not retire rows — retire it on its list, where its use is checked.`);continue;}
        let reactivated=false;
        if(old&&!old.active){
          if(wantActive===true){setActive(sub({master:id,id:old.id}),true);reactivated=true;}
          else{
            const same=Object.entries(input).every(([key,v])=>key==='code'||JSON.stringify(old[key]??null)===JSON.stringify(v??null));
            if(same){put('unchanged','Already retired.');continue;}
            put('error',`${old.name} is retired — set active to yes to reactivate it first.`);continue;
          }
        }
        let changed:string;
        try{changed=save(sub({master:id,row:old?{...input,id:old.id}:input}));}
        catch(e){if(e instanceof HccError&&e.message==='Nothing changed.'){put(reactivated?'update':'unchanged',reactivated?'Reactivated.':'Already in HCC.');continue;}throw e;}
        if(!old&&wantActive===false){const made=mstRows(k.s,id).find(r=>r.code===code)!;setActive(sub({master:id,id:made.id}),false);}
        put(old?'update':'create',changed.replace(`Master ${sp.label}: `,'')+(reactivated?'; reactivated':''));
      }catch(e){
        if(e instanceof HccError)put(e.code==='FORBIDDEN'?'skipped':'error',e.message.replace(/^Access denied: /,''));else throw e;
      }
    }
  }
  return {items,counts:mstImportCounts(items),skippedMasters};
}
const MST_IMPORT:Handler=k=>{
  if(!can(k.s,k.uid,'masters')&&!mstCanEditMoney(k.s,k.uid))k.fail('FORBIDDEN','Access denied: masters permission is required.');
  const sets=Array.isArray(k.cmd.sets)?k.cmd.sets as MstIoSet[]:[];
  k.assert(sets.length&&sets.every(x=>x&&typeof x.master==='string'&&Array.isArray(x.rows)),'Nothing to import: send the lists and their rows.');
  const rep=importRows(k,sets);
  if(k.cmd.dryRun)throw new MstImportPreview(rep);
  const bad=rep.items.filter(i=>i.action==='error');
  if(bad.length)k.fail('VALIDATION',`${bad.length} row${bad.length===1?'':'s'} refused — nothing was imported: ${bad.slice(0,3).map(i=>`${i.master} ${i.code||'row '+i.line}: ${i.detail}`).join(' · ')}${bad.length>3?` (+${bad.length-3} more)`:''}`);
  const c=rep.counts;
  k.assert(c.create+c.update>0,`Nothing to import: ${c.unchanged} unchanged, ${c.skipped} skipped.`);
  return `Masters import · ${c.create} added · ${c.update} changed · ${c.unchanged} unchanged · ${c.skipped} skipped (${rep.items.filter(i=>i.action==='create'||i.action==='update').map(i=>i.master).filter((x,i,a)=>a.indexOf(x)===i).join(', ')})`;
};

/** The five generic commands under a prefix (MST by default; modules may add aliases such as RMS_MASTER_SAVE). */
export function mstHandlersFor(prefix:string):Record<string,Handler>{
  return {[`${prefix}_MASTER_SAVE`]:save,[`${prefix}_MASTER_RETIRE`]:k=>setActive(k,false),[`${prefix}_MASTER_REACTIVATE`]:k=>setActive(k,true),
    [`${prefix}_MASTER_REORDER`]:reorder,[`${prefix}_MASTER_DELETE`]:remove};
}
export const handlers:Record<string,Handler>={...mstHandlersFor('MST'),MST_IMPORT};

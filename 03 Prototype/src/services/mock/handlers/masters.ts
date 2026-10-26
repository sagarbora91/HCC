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

/** The five generic commands under a prefix (MST by default; modules may add aliases such as RMS_MASTER_SAVE). */
export function mstHandlersFor(prefix:string):Record<string,Handler>{
  return {[`${prefix}_MASTER_SAVE`]:save,[`${prefix}_MASTER_RETIRE`]:k=>setActive(k,false),[`${prefix}_MASTER_REACTIVATE`]:k=>setActive(k,true),
    [`${prefix}_MASTER_REORDER`]:reorder,[`${prefix}_MASTER_DELETE`]:remove};
}
export const handlers:Record<string,Handler>=mstHandlersFor('MST');

// MA-0 · MasterForm: the editor of one master row, built from the spec's FieldSpec list (text, number, money, percent, select from
// another master or fixed options, toggle, multiselect, dated money). Inline validation with the same rules as the command
// (mstProblems); money fields hidden for roles without money visibility (DEC-061) and read-only unless Owner (DEC-058); status
// actions (retire / reactivate / delete when unused), "used in" and the change history. Used inline on desktop (MasterList detail
// pane) and as the phone sheet (modal 'mst-row', mobile/masters/MasterSheet.tsx). Classes mst- (styles/m-masters.css).
import React,{useState} from 'react';
import {Check,Archive,RotateCcw,Trash2,Plus,X,Lock,History} from 'lucide-react';
import {mstSpec,mstById,mstRows,mstClean,mstProblems,mstUsage,mstCanEdit,mstCanEditMoney,mstVisibleFields,mstOptions,mstHistory,mstNextSort,mstShow,mstMoneyOn,
  MST_OWNER_ONLY,type FieldSpec,type MasterRow,type MasterSpec,type MstDatedAmount} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,shortDate,clockTime} from '../../components/ui';

const isMoney=(f:FieldSpec)=>!!f.money||f.type==='money'||f.type==='datedMoney';
const BASE_KEYS=['code','name','sortOrder','active'];
/** Editor text from a row value (money/percent shown in rupees / %). */
function toText(f:FieldSpec,v:unknown):string{
  if(v===undefined||v===null)return '';
  if(f.type==='money'||f.type==='percent')return String(Number(v)/100);
  return String(v);
}
function fromText(f:FieldSpec,t:string):unknown{
  if(f.type==='money'||f.type==='percent')return t.trim()===''?undefined:Math.round(Number(t)*100);
  if(f.type==='number'||f.key==='sortOrder')return t.trim()===''?undefined:Number(t);
  return t;
}
type Draft=Record<string,unknown>;
const draftOf=(spec:MasterSpec,row:MasterRow|undefined,rows:MasterRow[]):Draft=>row?{...row}:{code:'',name:'',sortOrder:mstNextSort(rows),...Object.fromEntries(spec.fields.filter(f=>f.type==='toggle'&&f.key!=='active').map(f=>[f.key,false]))};

/** Form for one row. rowId undefined = new row. onDone(after save / status change), onCancel (close). */
export function MasterForm({specId,rowId,onDone,onCancel,sheet}:{specId:string;rowId?:string;onDone?:(id?:string)=>void;onCancel?:()=>void;sheet?:boolean}){
  const {s,uid,run,busy,modal,setDirty}=useHcc();
  const spec=mstSpec(specId);
  const rows=spec?mstRows(s,specId):[];
  const row=rowId?mstById(s,specId,rowId):undefined;
  const [d,setD]=useState<Draft>(()=>spec?draftOf(spec,row,rows):{});
  const [touched,setTouched]=useState(false);
  const {open}=useHcc(); // V14-6: Retire asks first (modal 'mst-retire', RetireConfirm.tsx)
  if(!spec)return <p className="muted">This list is not available.</p>;
  if(rowId&&!row)return <p className="muted">This {spec?.singular} no longer exists.</p>;
  const can=mstCanEdit(s,uid,spec),money=mstCanEditMoney(s,uid);
  const fields=mstVisibleFields(s,uid,spec).filter(f=>f.key!=='active');
  const editable=can.ok&&(!row||row.active);
  const clean=mstClean(spec,d,row);
  if(clean.sortOrder===undefined)clean.sortOrder=row?.sortOrder??mstNextSort(rows);
  clean.id=row?.id??'';clean.active=row?.active??true;
  const problems=mstProblems(spec,clean,rows,s,row);
  const errOf=(k:string)=>touched?problems.find(p=>p.field===k)?.message:undefined;
  const general=touched?problems.find(p=>!p.field)?.message:undefined;
  const usage=row?mstUsage(s,spec,row):{count:0,where:[]};
  const set=(k:string,v:unknown)=>{setD(x=>({...x,[k]:v}));if(sheet)setDirty(true);};
  const key=(what:string)=>sheet&&modal?`${modal.key}:${what}`:`mst-${what}:${specId}:${rowId??'new'}:${s.sequence}`;
  const save=()=>{setTouched(true);if(problems.length||!editable)return;
    const payload:Draft={};for(const k of ['code','name','sortOrder',...spec.fields.map(f=>f.key)])if(k!=='active'&&k in clean)payload[k]=clean[k];
    if(row)payload.id=row.id;
    for(const f of spec.fields)if(isMoney(f)&&!money&&row)payload[f.key]=row[f.key]; // never send a money change without the Owner's right
    void run({type:'MST_MASTER_SAVE',master:specId,row:payload,key:key('save')},row?`${spec.singular[0].toUpperCase()+spec.singular.slice(1)} saved · history recorded`:`${clean.name} added`,
      {close:!!sheet,after:()=>{if(sheet)setDirty(false);onDone?.(row?.id);}});};
  const status=(type:'MST_MASTER_REACTIVATE'|'MST_MASTER_DELETE',msg:string)=>void run({type,master:specId,id:row!.id,key:key(type)},msg,{close:!!sheet,after:()=>onDone?.(type==='MST_MASTER_DELETE'?undefined:row!.id)});
  const title=row?`${row.name}`:`New ${spec.singular}`;
  return <div className={'mst-form'+(sheet?' mst-sheet':'')}>
    <div className={sheet?'modal-heading':'mst-form-head'}><div className="eyebrow">{spec.label.toUpperCase()}{row?` · ${row.code}`:''}</div><h2>{title}</h2>
      {row&&<p className="mst-status">{row.active?<Pill tone="green">Active</Pill>:<Pill tone="neutral">Retired{row.retiredOn?` ${shortDate(row.retiredOn)}`:''}</Pill>}
        <span>Used in: {usage.count?`${usage.count} · ${usage.where.join('; ')}`:'nothing yet'}</span></p>}</div>
    {!can.ok&&<p className="mst-lock" role="note"><Lock size={14}/> {can.reason}</p>}
    {can.ok&&row&&!row.active&&<p className="mst-lock" role="note"><Lock size={14}/> Retired — reactivate it to change it. Old records keep showing its name.</p>}
    <form className="form mst-fields" noValidate onSubmit={e=>{e.preventDefault();save();}}>
      {fields.map(f=><Field key={f.key} f={f} spec={spec} v={d[f.key]} err={errOf(f.key)} set={v=>set(f.key,v)}
        disabled={!editable||(isMoney(f)&&!money)||(f.key==='code'&&!!row&&usage.count>0)}
        note={isMoney(f)&&!money?MST_OWNER_ONLY:f.key==='code'&&row&&usage.count>0?'In use — the code cannot change.':undefined}/>)}
      {general&&<p className="adm-err" role="alert">{general}</p>}
      <div className={sheet?'modal-actions':'mst-actions'}>
        {onCancel&&<button type="button" className="btn" onClick={onCancel}>{sheet?'Close':'Cancel'}</button>}
        {editable&&<button className="btn primary" disabled={busy}>{row?'Save changes':`Add ${spec.singular}`} <Check size={16}/></button>}
      </div>
    </form>
    {row&&can.ok&&<div className="mst-status-act">
      {row.active?<button type="button" className="btn" disabled={busy} onClick={()=>open({type:'mst-retire',id:`${specId}:${row.id}${sheet?':sheet':''}`})}><Archive size={15}/> Retire…</button>
        :<button type="button" className="btn" disabled={busy} onClick={()=>status('MST_MASTER_REACTIVATE',`${row.name} reactivated`)}><RotateCcw size={15}/> Reactivate</button>}
      {usage.count===0?<button type="button" className="btn danger" disabled={busy} onClick={()=>status('MST_MASTER_DELETE',`${row.name} deleted`)}><Trash2 size={15}/> Delete (never used)</button>
        :<small className="muted">In use — it can be retired, not deleted.</small>}
    </div>}
    {row&&<MasterHistory spec={spec} row={row}/>}
  </div>;
}

/** optionsFrom choices plus values already on the row that dropped out (shown as "(no longer available)"). */
const withCurrent=(opts:{value:string;label:string}[],cur:string[])=>[...opts,...cur.filter(c=>!opts.some(o=>o.value===c)).map(c=>({value:c,label:`${c} (no longer available)`}))];
function Field({f,spec,v,err,set,disabled,note}:{f:FieldSpec;spec:MasterSpec;v:unknown;err?:string;set:(v:unknown)=>void;disabled:boolean;note?:string}){
  const {s}=useHcc();
  const id=`mst-${spec.id}-${f.key}`;
  const label=<>{f.label}{f.required?' *':''}{f.type==='money'?' (₹)':f.type==='percent'?' (%)':''}</>;
  const help=<>{f.help&&<small className="mst-help">{f.help}</small>}{note&&<small className="mst-help"><Lock size={12}/> {note}</small>}{err&&<small className="mst-err" role="alert">{err}</small>}</>;
  const aria={'aria-invalid':err?true:undefined,'aria-describedby':err?id+'-err':undefined};
  if(f.type==='toggle')return <label className="mst-toggle"><input type="checkbox" checked={!!v} disabled={disabled} onChange={e=>set(e.target.checked)}/> <span>{label}</span>{help}</label>;
  if(f.type==='select'){const opts=f.master?mstOptions(s,f.master,v as string):f.optionsFrom?withCurrent(f.optionsFrom(s),v?[String(v)]:[]):(f.options??[]);
    return <label>{label}<select value={String(v??'')} disabled={disabled} onChange={e=>set(e.target.value||undefined)} {...aria}><option value="">{f.required?'Choose…':'— none —'}</option>{opts.map(o=><option key={o.value} value={o.value}>{o.label}</option>)}</select>{help}</label>;}
  if(f.type==='multiselect'){const cur=Array.isArray(v)?v.map(String):[];const opts=f.master?mstOptions(s,f.master,cur):f.optionsFrom?withCurrent(f.optionsFrom(s),cur):(f.options??[]);
    return <fieldset className="mst-multi" disabled={disabled}><legend>{label}</legend>{opts.map(o=><label key={o.value} className="mst-toggle"><input type="checkbox" checked={cur.includes(o.value)}
      onChange={e=>set(e.target.checked?[...cur,o.value]:cur.filter(x=>x!==o.value))}/> <span>{o.label}</span></label>)}{help}</fieldset>;}
  if(f.type==='datedMoney')return <DatedMoney f={f} v={Array.isArray(v)?v as MstDatedAmount[]:[]} set={set} disabled={disabled} label={label} help={help}/>;
  const num=f.type==='number'||f.type==='money'||f.type==='percent';
  const text=toText(f,v);
  if(f.type==='text'&&f.multiline)return <label>{label}<textarea id={id} rows={5} value={text} disabled={disabled} maxLength={f.maxLength??200} placeholder={f.placeholder} onChange={e=>set(e.target.value)} {...aria}/>{help}</label>;
  return <label>{label}<input id={id} value={text} disabled={disabled} type={num?'number':'text'} inputMode={f.type==='money'||f.type==='percent'?'decimal':num?'numeric':undefined}
    step={f.type==='money'||f.type==='percent'?0.01:num?1:undefined} maxLength={f.type==='text'?f.maxLength??200:undefined} placeholder={f.placeholder} enterKeyHint="done" autoComplete="off"
    autoCapitalize={f.key==='code'?'characters':undefined} onChange={e=>set(fromText(f,e.target.value))} {...aria}/>{help}</label>;
}

/** Dated money (tariff pattern): amounts in effect are kept; add a new amount from today's business date or later. */
function DatedMoney({f,v,set,disabled,label,help}:{f:FieldSpec;v:MstDatedAmount[];set:(v:unknown)=>void;disabled:boolean;label:React.ReactNode;help:React.ReactNode}){
  const {s}=useHcc();
  const [amt,setAmt]=useState('');const [from,setFrom]=useState(s.date);
  const now=mstMoneyOn(v,s.date);
  const add=()=>{const a=Math.round(Number(amt)*100);if(!Number.isSafeInteger(a)||a<0||!from||from<s.date)return;
    set([...v.filter(x=>x.effectiveFrom!==from),{amount:a,effectiveFrom:from}].sort((x,y)=>x.effectiveFrom<y.effectiveFrom?-1:1));setAmt('');};
  return <fieldset className="mst-dated" disabled={disabled}><legend>{label}</legend>
    <p className="muted">In force on {shortDate(s.date)}: <strong>{now===undefined?'—':mstShow({...f,type:'money'},now)}</strong></p>
    {v.length>0&&<ul>{v.map(x=><li key={x.effectiveFrom}><span>{mstShow({...f,type:'money'},x.amount)} from {shortDate(x.effectiveFrom)}</span>
      {x.effectiveFrom>=s.date?<button type="button" className="text-btn" aria-label={`Remove the amount from ${x.effectiveFrom}`} onClick={()=>set(v.filter(y=>y!==x))}><X size={14}/> Remove</button>:<Pill tone="green">In effect / past</Pill>}</li>)}</ul>}
    <div className="mst-dated-add"><label>Amount (₹)<input type="number" inputMode="decimal" step={0.01} min={0} value={amt} onChange={e=>setAmt(e.target.value)}/></label>
      <label>From<input type="date" min={s.date} value={from} onChange={e=>setFrom(e.target.value)}/></label>
      <button type="button" className="btn" onClick={add} disabled={amt===''}><Plus size={15}/> Add amount</button></div>
    {help}</fieldset>;
}

export function MasterHistory({spec,row}:{spec:MasterSpec;row:MasterRow}){
  const {s}=useHcc();
  const h=mstHistory(s,spec.id,row.id);
  const name=(u:string)=>s.users.find(x=>x.id===u)?.name.split(' · ')[0]??u;
  return <div className="mst-hist"><h3><History size={15}/> History</h3>
    {h.length?<ul>{h.slice(0,30).map(x=><li key={x.id}><strong>{x.field}</strong> {x.old} → {x.new} · {name(x.by)} · {shortDate(x.at.slice(0,10))} {clockTime(x.at)}</li>)}</ul>
      :<p className="muted">No changes recorded yet{row.id.startsWith('flr-')||!s.mastersX?' (fixture values)':''}.</p>}
  </div>;
}

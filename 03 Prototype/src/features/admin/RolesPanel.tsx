// ADM-3 · Administration › Roles (DEC-216…221; 02 Planning/agents/ADM_Briefs.md §4). Shared by the desktop page and the phone page.
// Owner only (DEC-218). Built-in roles are read-only templates; a custom role is a copy of one base, then adjusted on the grid:
// one Off / View / Full control per non-core sub-function (grouped by module) and one money switch. Every change is a ROL_* command.
import React,{useState} from 'react';
import {ShieldCheck,Info,Plus,Lock,AlertTriangle,Trash2} from 'lucide-react';
import {authorize,ADMIN_PERMS,adminScopeText,featureById,featureClosure,cfgModuleOn,cfgFeatureOn,
  rolList,rolHolders,rolMoneyLocked,rolGridModules,rolRoleFeatureLevel,rolPreview,rolSodBlock,rolCustomLimit,rolCredentialKind,
  roles as BUILT_IN} from '../../services/mock';
import type {CustomRole,RoleAccess,PolicyCell,State} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,Empty,field,submit} from '../../components/ui';
import {ScrollPanel,FitNote} from '../../components/fit';
import {PolicyLimitEditor} from './PolicyLimitEditor';
import {GuardedButton,firstName} from './parts';

export const ROLES_OWNER_ONLY='Roles are edited by the Owner only (DEC-218). The Tech admin creates users, assigns non-money roles and sets sign-in credentials in the Users tab.';
/** Built-in roles shown in the list (Restaurant is a production-only built-in). */
export const ROL_BUILT_INS=[...BUILT_IN,'Restaurant'] as const;
const LEVELS:{id:RoleAccess;label:string}[]=[{id:'off',label:'Off'},{id:'view',label:'View'},{id:'full',label:'Full'}];
const BUILT_IN_NOTE:Record<string,string>={Owner:'Super admin: always everything, never editable and never a base (DEC-216).'};
/** DEC-217: an Accounting approve (-003) or money (-004) id stays with Owner + Accountant unless granted explicitly. */
export const rolAccApproveId=(permId:string)=>/^PERM-ACC-00[34]$/.test(permId);
export const ROL_DEC217='DEC-217: Accounting approve and money permissions stay with the Owner and the Accountant unless you grant them explicitly. This grants one explicitly — it is recorded in the audit with this warning.';
const ownerOnly=(s:State,uid:string)=>s.users.find(u=>u.id===uid)?.role==='Owner'&&!s.users.find(u=>u.id===uid)?.customRole;
export const rolIsOwner=ownerOnly;

/** Plain-language access summary of one role (effective-access preview, `rolPreview`). */
export function RolePreviewCard({role}:{role:CustomRole}){
  const {s}=useHcc();const p=rolPreview(s,role.id);if(!p)return null;
  const off=p.features.filter(f=>f.level==='off'),view=p.features.filter(f=>f.level==='view');
  return <section className="adm-preview" aria-label={`Effective access of ${role.name}`}>
    <h3>Effective access · {role.name}</h3>
    <p className="muted">Base {p.base} · money {p.money?'on':'off'}{p.moneyLocked?' (locked)':''} · {p.users.length} user{p.users.length===1?'':'s'}</p>
    <dl className="adm-preview-rows">
      <div><dt>Pages hidden</dt><dd>{p.hiddenPages.length?p.hiddenPages.join(', '):'none'}</dd></div>
      <div><dt>Off ({off.length})</dt><dd>{off.length?off.map(f=>`${f.label} (${f.module})`).join(', '):'none'}</dd></div>
      <div><dt>View only ({view.length})</dt><dd>{view.length?view.map(f=>`${f.label} (${f.module})`).join(', '):'none'}</dd></div>
      <div><dt>Module access</dt><dd>{p.grants.length?p.grants.join(', '):'none'}</dd></div>
    </dl>
  </section>;
}

function RoleList({sel,onSelect,canEdit}:{sel?:string;onSelect:(id:string)=>void;canEdit:boolean}){
  const {s,open}=useHcc();const custom=rolList(s);
  return <ScrollPanel title="Roles" label="Roles" actions={canEdit?<button type="button" className="btn primary" onClick={()=>open({type:'admin-role-new'})}><Plus size={16}/> New role</button>:undefined}
      footer={<FitNote text="Built-in roles are read-only templates. A custom role is a copy of one base role at creation; later changes to the base never flow down."/>}>
    <ul className="adm-rolelist">
      {ROL_BUILT_INS.map(r=><li key={r} className="adm-role builtin"><div><strong>{r}</strong><small>{BUILT_IN_NOTE[r]??'Built-in · read-only'}</small></div><Pill tone="neutral">Built-in · read-only</Pill></li>)}
      {custom.map(c=><li key={c.id}><button type="button" className={'adm-role custom'+(c.id===sel?' on':'')} aria-pressed={c.id===sel} onClick={()=>onSelect(c.id)}>
        <span><strong>{c.name}</strong><small>{c.id} · base {c.base} · money {c.money?'on':'off'} · {rolHolders(s,c.id).length} user{rolHolders(s,c.id).length===1?'':'s'} · v{c.version}</small></span>
        {c.retired?<Pill tone="neutral">Retired</Pill>:<Pill tone="blue">Custom</Pill>}</button></li>)}
    </ul>
    {!custom.length&&<Empty title="No custom roles yet" body={canEdit?'Create one from a built-in base role, then adjust it on the grid.':'Only the Owner creates custom roles.'}/>}
  </ScrollPanel>;
}

function FeatureGrid({c,disabled}:{c:CustomRole;disabled:boolean}){
  const {s,run,busy}=useHcc();
  const set=(feature:string,access:RoleAccess)=>{
    const r=featureById(feature)!;const cur=rolRoleFeatureLevel(c,feature);if(cur===access)return;
    const rank={off:0,view:1,full:2};
    const deps=access==='full'?[]:featureClosure(feature).slice(1).filter(id=>rank[rolRoleFeatureLevel(c,id)]>rank[access]).map(id=>featureById(id)?.feature.label).filter(Boolean);
    if(deps.length&&!window.confirm(`Setting ${r.feature.label} to ${access==='off'?'Off':'View'} also sets: ${deps.join(', ')} to ${access==='off'?'Off':'View'}. Continue?`))return;
    void run({type:'ROL_FEATURE_SET',id:c.id,feature,access,key:`rol-feat:${c.id}:${feature}:${access}:${s.sequence}`},`${r.feature.label}: ${access==='off'?'Off':access==='view'?'View only':'Full'} for ${c.name}`);
  };
  return <div className="adm-grid" role="group" aria-label={`Access grid for ${c.name}`}>
    {rolGridModules().map(g=>{const modOn=!g.module.switch||cfgModuleOn(s,g.module.switch);
      return <section key={g.module.id} className={'adm-grid-mod'+(modOn?'':' off')}>
        <h4>{g.module.label}{!modOn&&<small> · module switched off for everyone</small>}</h4>
        {g.features.map(fid=>{const r=featureById(fid)!;const own=c.features[fid]??'full';const eff=rolRoleFeatureLevel(c,fid);const featOn=cfgFeatureOn(s,fid);
          return <div className="adm-grid-row" key={fid}>
            <div className="adm-grid-label"><strong>{r.feature.label}</strong><small>{r.feature.note}</small>
              {!featOn&&modOn&&<small className="adm-why">Switched off for everyone in Modules &amp; features</small>}
              {eff!==own&&<small className="adm-why">Limited to {eff==='off'?'Off':'View'} by a sub-function it needs</small>}</div>
            <div className="adm-seg" role="radiogroup" aria-label={`${r.feature.label} access`}>
              {LEVELS.map(l=><button type="button" key={l.id} role="radio" aria-checked={eff===l.id} className={eff===l.id?'on':''} disabled={disabled||busy} onClick={()=>set(fid,l.id)}>{l.label}</button>)}
            </div>
          </div>;})}
      </section>;})}
  </div>;
}

const CELL_OPTIONS:[string,string][]=[['none','— Not granted'],['allow','A · Allow'],['view','V · View only'],['masked','M · Masked view'],['limit','L · Allow up to limit'],['request','R · Request only'],['deny','D · Explicit deny']];
const cellValue=(c?:PolicyCell)=>!c?'none':c.effect==='allow'?(c.mode||'allow'):c.effect;
const cellOf=(v:string,scope:PolicyCell['scope']):PolicyCell|undefined=>v==='none'?undefined:v==='view'||v==='masked'?{effect:'allow',scope,mode:v}:{effect:v as PolicyCell['effect'],scope};
function AdvancedGrants({c,disabled}:{c:CustomRole;disabled:boolean}){
  const {s,run,busy}=useHcc();
  const set=(perm:string,v:string,scope?:PolicyCell['scope'])=>{
    const sc=scope||c.template[perm]?.scope||'P';
    if(v!=='none'&&v!=='deny'&&rolAccApproveId(perm)&&!window.confirm(ROL_DEC217+' Continue?'))return;
    const effect=v==='view'||v==='masked'?'allow':v;
    void run({type:'ROL_GRANT_SET',id:c.id,perm,effect,scope:sc,...(v==='view'||v==='masked'?{mode:v}:{}),key:`rol-grant:${c.id}:${perm}:${v}:${sc}:${s.sequence}`},`${perm} set for ${c.name}`);
  };
  const groups=[...new Set(ADMIN_PERMS.map(p=>p.group))];
  return <details className="adm-advanced"><summary>Advanced: permission ids</summary>
    <p className="muted">One cell per permission id, applied to this custom role only. Money, separation-of-duty (DEC-219) and Tech admin rules are checked before a change is sent.</p>
    {groups.map(g=><div key={g} className="adm-group"><h3>{g}</h3>
      {ADMIN_PERMS.filter(p=>p.group===g).map(p=>{const cell=c.template[p.id];
        const blocks=Object.fromEntries(CELL_OPTIONS.map(([v])=>[v,v==='none'||v==='deny'?undefined:rolSodBlock(s,c,p.id,cellOf(v,cell?.scope||'P'))])) as Record<string,string|undefined>;
        const lim=p.limit?(c.limits[p.id]??rolCustomLimit(s,c,p.id)):undefined;
        return <div className="adm-perm" key={p.id}>
          <div><strong>{p.action}</strong><small>{p.id}{p.financial?' · money':''}</small>
            {rolAccApproveId(p.id)&&cell&&cell.effect!=='deny'&&<small className="adm-warn-line" role="note"><AlertTriangle size={13}/> {ROL_DEC217}</small>}</div>
          <select aria-label={`${p.id} effect for ${c.name}`} disabled={disabled||busy} value={cellValue(cell)} onChange={e=>set(p.id,e.target.value)}>
            {CELL_OPTIONS.map(([v,l])=><option key={v} value={v} disabled={!!blocks[v]||(v==='limit'&&!p.limit)} title={blocks[v]}>{l}{blocks[v]?' — not allowed':''}</option>)}</select>
          <select aria-label={`${p.id} scope for ${c.name}`} disabled={disabled||busy||!cell||cell.effect==='deny'} value={cell?.scope||'P'} onChange={e=>set(p.id,cellValue(cell),e.target.value as PolicyCell['scope'])}>
            {(['P','S','A','O'] as const).map(x=><option key={x} value={x}>{x} · {adminScopeText[x]}</option>)}</select>
          {p.limit&&cell?.effect==='limit'?<PolicyLimitEditor key={`${c.id}:${p.id}:${lim}:${c.version}`} unit={p.limit.unit==='percent'?'percent':'money'} current={lim} disabled={disabled||busy}
            onSave={limit=>{void run({type:'ROL_GRANT_SET',id:c.id,perm:p.id,limit,key:`rol-limit:${c.id}:${p.id}:${limit}:${s.sequence}`},`${p.limit?.label??p.id} limit updated`);}}/>:<span/>}
          {Object.values(blocks).some(Boolean)&&<small className="adm-why">{[...new Set(Object.values(blocks).filter(Boolean))].join(' ')}</small>}
        </div>;})}
    </div>)}
  </details>;
}

export function RoleEditor({c,phone}:{c:CustomRole;phone?:boolean}){
  const {s,run,busy}=useHcc();
  const [name,setName]=useState(c.name);
  const holders=rolHolders(s,c.id);const locked=rolMoneyLocked(c.base);
  const dis=!!c.retired;
  const retireBlock=holders.length?`Still held by ${holders.map(u=>firstName(u.name)).join(', ')} — move them to another role first.`:undefined;
  const body=<>
    <div className="adm-role-head">
      <form className="adm-inline-form" onSubmit={e=>{e.preventDefault();if(name.trim()&&name.trim()!==c.name)void run({type:'ROL_UPDATE',id:c.id,name:name.trim(),key:`rol-name:${c.id}:${s.sequence}`},'Role renamed');}}>
        <label>Name<input aria-label="Role name" value={name} minLength={2} maxLength={40} disabled={dis||busy} onChange={e=>setName(e.target.value)}/></label>
        <button type="submit" className="btn small-btn" disabled={dis||busy||!name.trim()||name.trim()===c.name}>Rename</button>
      </form>
      <p className="muted">Base {c.base} · signs in with a {rolCredentialKind(c.base)==='PASSWORD'?'password':'PIN'} · {holders.length} user{holders.length===1?'':'s'}</p>
      <label className="checkbox-line adm-money"><input type="checkbox" checked={c.money} disabled={dis||busy||locked} aria-label={`Money permissions for ${c.name}`}
        onChange={e=>{void run({type:'ROL_UPDATE',id:c.id,money:e.target.checked,key:`rol-money:${c.id}:${e.target.checked}:${s.sequence}`},`Money ${e.target.checked?'on':'off'} for ${c.name}`);}}/>
        Money permissions{locked&&<span className="adm-locknote"><Lock size={13}/> locked off: a role based on {c.base} never holds money permissions (DEC-061)</span>}</label>
    </div>
    {dis&&<p className="adm-lock" role="note"><Info size={15}/> This role is retired and read-only.</p>}
    <FeatureGrid c={c} disabled={dis}/>
    {phone?<p className="adm-desktop-note" role="note"><Info size={15}/> Permission ids (Advanced) are edited on the desktop layout to prevent mistakes on a small screen.</p>:<AdvancedGrants c={c} disabled={dis}/>}
    <RolePreviewCard role={c}/>
    {!dis&&<div className="adm-retire"><GuardedButton className="btn danger" block={retireBlock} busy={busy}
      onClick={()=>{if(window.confirm(`Retire ${c.name}? It disappears from the role picker; users cannot hold it. This cannot be undone.`))void run({type:'ROL_RETIRE',id:c.id,key:`rol-retire:${c.id}`},`${c.name} retired`);}}><Trash2 size={15}/> Retire role</GuardedButton></div>}
  </>;
  if(phone)return <div className="adm-phone-role" aria-label={`Role ${c.name}`}><div className="adm-phone-title"><h2>{c.name}</h2><Pill tone={c.retired?'neutral':'blue'}>{c.retired?'Retired':`${c.id} · v${c.version}`}</Pill></div>{body}</div>;
  return <ScrollPanel title={c.name} label={`Role ${c.name}`} actions={<Pill tone={c.retired?'neutral':'blue'}>{c.retired?'Retired':`${c.id} · v${c.version}`}</Pill>}
      footer={<FitNote text="Each change is a versioned, audited policy change (who, when, before, after). Core sub-functions have no row: they follow the base role unchanged."/>}>{body}</ScrollPanel>;
}

/** The Roles tab body. Non-owners get the Owner-only explanation. */
export function RolesPanel(){
  const {s,uid}=useHcc();
  const owner=ownerOnly(s,uid);
  const custom=rolList(s);
  const [sel,setSel]=useState<string|undefined>(custom.find(c=>!c.retired)?.id);
  const c=sel?custom.find(x=>x.id===sel):undefined;
  const d=authorize(s,uid,'PERM-ADM-002');
  if(!owner)return <div className="fit-stack"><ScrollPanel title="Roles" label="Roles">
    <p className="adm-lock" role="note"><ShieldCheck size={15}/> Owner only. {ROLES_OWNER_ONLY}</p>
    <ul className="adm-rolelist">{custom.map(r=><li key={r.id} className="adm-role builtin"><div><strong>{r.name}</strong><small>{r.id} · base {r.base} · money {r.money?'on':'off'}</small></div><Pill tone="neutral">Read-only</Pill></li>)}</ul>
    {d.decision==='deny'&&<p className="muted">{d.reason}</p>}</ScrollPanel></div>;
  return <div className="fit-cols adm-roles-cols"><div><RoleList sel={sel} onSelect={setSel} canEdit/></div>
    <div>{c?<RoleEditor key={c.id} c={c}/>:<ScrollPanel title="Role editor" label="Role editor"><Empty title="Choose a custom role" body="Pick a custom role on the left, or create a new one from a built-in base."/></ScrollPanel>}</div></div>;
}

/** New role dialog (modal id admin-role-new): name, base picker without Owner, money switch (locked for the four money-free bases). */
export function NewRoleModal(){
  const {s,uid,run,modal,closeModal,busy}=useHcc();
  const bases=ROL_BUILT_INS.filter(r=>r!=='Owner');
  const [base,setBase]=useState<string>('Manager');const [money,setMoney]=useState(true);
  const locked=rolMoneyLocked(base as never);
  const isOwner=ownerOnly(s,uid);
  return <><div className="modal-heading"><div className="eyebrow">NEW ROLE</div><h2>New custom role</h2><p>A copy of one built-in role, then adjusted on the grid. Later changes to the base never flow down.</p></div>
    <form className="form" onSubmit={e=>submit(e,f=>run({type:'ROL_CREATE',name:field(f,'name').trim(),base,money:locked?false:money,key:modal!.key},'Role created',{close:true}))}>
      <label>Role name<input name="name" required minLength={2} maxLength={40} placeholder="e.g. Weekend supervisor"/></label>
      <label>Based on<select name="base" aria-label="Base role" value={base} onChange={e=>{setBase(e.target.value);if(rolMoneyLocked(e.target.value as never))setMoney(false);}}>
        {bases.map(r=><option key={r} value={r}>{r}</option>)}</select></label>
      <label className="checkbox-line"><input type="checkbox" checked={locked?false:money} disabled={locked} onChange={e=>setMoney(e.target.checked)}/>Money permissions{locked&&<span className="adm-locknote"><Lock size={13}/> locked off for a {base} base (DEC-061)</span>}</label>
      <p className="muted">Signs in with a {rolCredentialKind(base as never)==='PASSWORD'?'password':'PIN'} (follows the base).</p>
      {!isOwner&&<p className="adm-lock" role="note">Only the Owner creates roles.</p>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!isOwner}>Create role</button></div>
    </form></>;
}

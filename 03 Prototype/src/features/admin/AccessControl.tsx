// SCR-16 Access control (BL-024/025): users, role templates, effective access, exceptions, policy history.
import React,{useState} from 'react';
import {PolicyLimitEditor} from './PolicyLimitEditor';
import {ShieldCheck,Info,UserPlus} from 'lucide-react';
import {permissions,Role,money,authorize,adminPolicy,adminUserStatus,adminOtherActiveOwners,adminEffectiveAccess,adminCellCode,adminDescribeCell,adminLimitFor,adminExceptionActive,adminApprovalInbox,ADMIN_PERMS,ADMIN_ROLE_ORDER,ADMIN_FINANCIAL_MODULES,adminScopeText} from '../../services/mock';
import type {User,PolicyCell} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,shortDate,clockTime,field,submit} from '../../components/ui';
import {DecisionPill,StatusPill,GuardedButton,Tabs,firstName} from './parts';

const TABS=['Users','Role templates','Effective access','Exceptions','Policy history'] as const;
export function AccessControl(){
  const {s,uid,go}=useHcc();
  const [tab,setTab]=useState<string>('Users');
  const pol=adminPolicy(s);
  const pending=adminApprovalInbox(s).filter(r=>r.kind==='policy'&&r.status==='Pending');
  const own=authorize(s,uid,'PERM-ADM-002');
  return <><PageTitle title="Access control" sub="The right access, for the right person — role templates, scopes, limits and exceptions." action={<div className="inline"><Pill tone="blue">Policy v{pol.version}</Pill><Pill tone="orange">Simulated identity · not production security</Pill></div>}/>
    {pending.length>0&&<div className="info-note adm-banner"><Info size={16}/><span>{pending.length} policy change request{pending.length>1?'s':''} waiting for Owner approval.</span><button className="text-btn" onClick={()=>go('approvals')}>Open approvals</button></div>}
    {own.decision==='approval'&&<div className="info-note adm-banner"><ShieldCheck size={16}/><span>Your role may only <strong>request</strong> role-template and exception changes. Each change you make becomes a request the Owner approves (PERM-ADM-002).</span></div>}
    <section className="card"><Tabs tabs={TABS} value={tab} onChange={setTab} counts={{Exceptions:pol.exceptions.filter(e=>adminExceptionActive(s,e)).length,'Policy history':pol.history.length}}/>
      {tab==='Users'&&<UsersTab/>}{tab==='Role templates'&&<TemplatesTab/>}{tab==='Effective access'&&<EffectiveTab/>}{tab==='Exceptions'&&<ExceptionsTab/>}{tab==='Policy history'&&<HistoryTab/>}
    </section></>;
}

function userBlock(s:ReturnType<typeof useHcc>['s'],uid:string,u:User):string|undefined{
  const d=authorize(s,uid,'PERM-ADM-001');
  if(d.decision!=='allow')return d.reason;
  if(u.id===uid)return 'This is your own access — another administrator must change it.';
  if(u.role==='Owner'&&u.enabled&&!adminOtherActiveOwners(s,u.id).length)return 'Last active owner — cannot be disabled (DEC-033).';
  return undefined;
}
function UsersTab(){
  const {s,uid,run,open,busy}=useHcc();
  const canAdmin=authorize(s,uid,'PERM-ADM-001');
  return <div className="padded">
    <div className="adm-row-head"><p className="muted">Disabling a user denies their next command immediately. One role per user; an expiry date ends the assignment after that business day.</p>
      <GuardedButton className="btn primary" block={canAdmin.decision==='allow'?undefined:canAdmin.reason} onClick={()=>open({type:'admin-user'})}><UserPlus size={16}/> New user</GuardedButton></div>
    <div className="table-wrap"><table className="adm-table"><thead><tr><th>Person</th><th>Role</th><th>Status</th><th>Access until</th><th>Assigned</th><th>Actions</th></tr></thead><tbody>
      {s.users.map(u=>{const st=adminUserStatus(s,u),block=userBlock(s,uid,u);return <tr key={u.id}>
        <td><strong>{firstName(u.name)}</strong><small>{u.id}{u.id===uid?' · you':''}</small></td>
        <td>{u.role}</td><td><StatusPill status={st}/></td>
        <td>{u.expiresOn?shortDate(u.expiresOn):<span className="muted">No expiry</span>}</td>
        <td>{u.assignedBy?<>{firstName(s.users.find(x=>x.id===u.assignedBy)?.name||u.assignedBy)}<small>{u.assignedOn&&shortDate(u.assignedOn)}</small></>:<span className="muted">Fixture</span>}</td>
        <td className="adm-wrap"><div className="inline">
          <GuardedButton block={canAdmin.decision==='allow'?undefined:'No admin.users permission'} busy={busy} className="text-btn" onClick={()=>open({type:'admin-user',id:u.id})}>Edit</GuardedButton>
          <GuardedButton block={block} busy={busy} onClick={()=>run({type:'USER',id:u.id,key:`user:${u.id}:${u.enabled}:${s.sequence}`},u.enabled?`${firstName(u.name)} disabled — their next action is denied`:`${firstName(u.name)} enabled`)}>{u.enabled?'Disable':'Enable'}</GuardedButton>
        </div></td></tr>;})}
    </tbody></table></div></div>;
}

const CELL_OPTIONS:[string,string][]=[['none','— Not granted'],['allow','A · Allow'],['view','V · View only'],['masked','M · Masked view'],['limit','L · Allow up to limit'],['request','R · Request only'],['deny','D · Explicit deny']];
const cellValue=(c?:PolicyCell)=>!c?'none':c.effect==='allow'?(c.mode||'allow'):c.effect;
function TemplatesTab(){
  const {s,uid,user,run,busy}=useHcc();
  const [role,setRole]=useState<Role>('Reception');
  const pol=adminPolicy(s),tpl=pol.templates[role]||{};
  const d=authorize(s,uid,'PERM-ADM-002');
  const lock=d.decision==='deny'?d.reason:role==='Owner'?'The Owner template is protected in this prototype.':role===user.role?'This is your own role — another administrator must change it (no self-escalation).':undefined;
  const groups=[...new Set(ADMIN_PERMS.map(p=>p.group))];
  const set=(perm:string,v:string,scope?:string)=>{const effect=['view','masked'].includes(v)?'allow':v;run({type:'POLICY_SET',role,perm,effect,scope:scope||tpl[perm]?.scope||'P',...(['view','masked'].includes(v)?{mode:v}:{})},d.decision==='approval'?'Request sent to the Owner for approval':'Role template updated · policy version bumped');};
  return <div>
    <div className="section-title"><div><h2>Permission matrix</h2><p>Codes: A allow · L allow up to limit · R request only · V view only · M masked · D explicit deny · — not granted. Scope after “·”: S own shift, A assigned, O own submissions.</p></div></div>
    <div className="table-wrap adm-matrix-wrap"><table className="adm-matrix"><thead><tr><th>Permission</th>{ADMIN_ROLE_ORDER.map(r=><th key={r}><button className={'text-btn'+(r===role?' adm-sel':'')} onClick={()=>setRole(r)} aria-pressed={r===role}>{r}</button></th>)}</tr></thead><tbody>
      {ADMIN_PERMS.map(p=><tr key={p.id}><td><strong>{p.id}</strong><small>{p.action}</small></td>{ADMIN_ROLE_ORDER.map(r=>{const c=pol.templates[r]?.[p.id];const code=adminCellCode(c);return <td key={r} className={'adm-code adm-'+code[0]+(r===role?' adm-selcol':'')} title={`${r}: ${adminDescribeCell(c)}`}>{code}</td>;})}</tr>)}
    </tbody></table></div>
    <div className="section-title"><div><h2>Edit role template</h2><p>Permission and scope changes submit immediately, with approval where required. Save limit applies only to its edited amount. Applied changes are audited.</p></div>
      <label className="adm-inline-label">Role<select aria-label="Role to edit" value={role} onChange={e=>setRole(e.target.value as Role)}>{ADMIN_ROLE_ORDER.map(r=><option key={r}>{r}</option>)}</select></label></div>
    {lock&&<p className="adm-lock" role="note"><ShieldCheck size={15}/> {lock}</p>}
    {groups.map(g=><div key={g} className="adm-group"><h3>{g}</h3>
      {ADMIN_PERMS.filter(p=>p.group===g).map(p=>{const c=tpl[p.id];const tadFin=role==='Tech admin'&&p.financial;const lim=p.limit?adminLimitFor(s,role,p.id):undefined;
        return <div className="adm-perm" key={p.id}>
          <div><strong>{p.action}</strong><small>{p.id}{p.financial?' · financial':''}{tadFin?' · Tech admin may never hold financial actions':''}</small></div>
          <select aria-label={`${p.id} effect for ${role}`} disabled={!!lock||busy} value={cellValue(c)} onChange={e=>set(p.id,e.target.value)}>
            {CELL_OPTIONS.map(([v,l])=><option key={v} value={v} disabled={(tadFin&&!['none','deny'].includes(v))||(v==='limit'&&!p.limit)}>{l}</option>)}</select>
          <select aria-label={`${p.id} scope for ${role}`} disabled={!!lock||busy||!c||c.effect==='deny'} value={c?.scope||'P'} onChange={e=>set(p.id,cellValue(c),e.target.value)}>
            {(['P','S','A','O'] as const).map(x=><option key={x} value={x}>{x} · {adminScopeText[x]}</option>)}</select>
          {p.limit&&c?.effect==='limit'?<div>
            <PolicyLimitEditor key={`${role}:${p.id}:${lim}`} unit={p.limit.unit==='percent'?'percent':'money'} current={lim} disabled={!!lock||busy}
              onSave={limit=>run({type:'POLICY_SET',role,perm:p.id,limit},d.decision==='approval'?'Limit change sent for Owner approval':'Limit updated')}/>
            {role==='Reception'&&p.id==='PERM-FOL-003'&&pol.limits.Reception?.[p.id]===undefined&&<small>Follows Hotel configuration → discount threshold ({money(s.settings.discountLimit)})</small>}
          </div>:<span/>}
        </div>;})}
    </div>)}
    <div className="section-title"><div><h2>Module access</h2><p>The coarse module gate used by every screen and command. A module that is off denies the whole area, whatever the template says.</p></div></div>
    <div className="permission-grid">{permissions.map(p=>{const tad=role==='Tech admin'&&ADMIN_FINANCIAL_MODULES.includes(p)&&!s.grants[role].includes(p);
      return <label key={p} className="permission"><div><strong>{p==='admin'?'Administration':p[0].toUpperCase()+p.slice(1)}</strong><small>{p==='admin'?'Not delegable in this prototype':tad?'Financial — not allowed for Tech admin':p==='housekeeping'?'Assigned-task scope for housekeepers and maintenance':'Module actions in this demo'}</small></div>
        <input type="checkbox" disabled={!!lock||p==='admin'||tad||busy} checked={s.grants[role].includes(p)} onChange={()=>run({type:'GRANT',role,permission:p},d.decision==='approval'?'Module change sent for Owner approval':'Module access updated')}/></label>;})}</div>
  </div>;
}

function EffectiveTab(){
  const {s,uid}=useHcc();
  const [who,setWho]=useState(uid);const [amt,setAmt]=useState('');const [only,setOnly]=useState('All');
  const ctx=amt?{amount:Math.round(Number(amt)*100)}:{};
  const rows=adminEffectiveAccess(s,who,ctx).filter(r=>only==='All'||(only==='Allowed'?r.decision==='allow':only==='Denied'?r.decision==='deny':r.decision==='approval'));
  const u=s.users.find(x=>x.id===who);
  return <div className="padded">
    <div className="form-row adm-filters">
      <label>User<select value={who} onChange={e=>setWho(e.target.value)}>{s.users.map(x=><option key={x.id} value={x.id}>{firstName(x.name)} · {x.role} ({adminUserStatus(s,x)})</option>)}</select></label>
      <label>Try an amount for limits (₹, optional)<input type="number" min="0" step="0.01" value={amt} onChange={e=>setAmt(e.target.value)} placeholder="e.g. 500"/></label>
      <div className="filters" aria-label="Show">{['All','Allowed','Approval required','Denied'].map(x=><button key={x} className={only===x?'active':''} aria-pressed={only===x} onClick={()=>setOnly(x)}>{x}</button>)}</div>
    </div>
    {u&&<p className="muted">Evaluation order (DEC-040): disabled / expired user → scope → explicit deny (role or exception — deny wins) → no grant (default deny) → limit → allow. The module column shows the coarse gate commands also check.</p>}
    <div className="table-wrap"><table className="adm-table"><thead><tr><th>Permission</th><th>Decision</th><th>Why</th><th>Deciding rule</th><th>Module gate</th></tr></thead><tbody>
      {rows.map(r=><tr key={r.perm.id}><td><strong>{r.perm.id}</strong><small>{r.perm.action}</small></td><td><DecisionPill d={r.decision}/></td><td className="adm-wrap">{r.reason}</td><td className="adm-wrap"><small>{r.rule}</small></td>
        <td>{r.module?<>{r.module}<small>{r.moduleOn?'on':'off for role'}</small></>:<span className="muted">—</span>}</td></tr>)}
    </tbody></table>{!rows.length&&<Empty title="Nothing in this filter" body="Choose another filter."/>}</div></div>;
}

function ExceptionsTab(){
  const {s,uid,run,busy}=useHcc();
  const pol=adminPolicy(s);const d=authorize(s,uid,'PERM-ADM-002');
  const block=d.decision==='deny'?d.reason:undefined;
  return <div className="padded">
    <p className="muted">User-level allow or deny with a reason and an expiry. Deny always wins over any allow. You cannot add exceptions to your own access; Tech admin can never be given financial actions.</p>
    <form className="form adm-exc-form" onSubmit={e=>submit(e,f=>run({type:'USER_EXCEPTION',user:field(f,'user'),perm:field(f,'perm'),effect:field(f,'effect'),reason:field(f,'reason'),expires:field(f,'expires')},d.decision==='approval'?'Exception sent to the Owner for approval':'Exception added · policy version bumped'))}>
      <div className="form-row"><label>User<select name="user" required>{s.users.filter(u=>u.id!==uid).map(u=><option key={u.id} value={u.id}>{firstName(u.name)} · {u.role}</option>)}</select></label>
        <label>Permission<select name="perm" required>{ADMIN_PERMS.map(p=><option key={p.id} value={p.id}>{p.id} · {p.action}</option>)}</select></label></div>
      <div className="form-row"><label>Effect<select name="effect"><option value="deny">Deny</option><option value="allow">Allow</option></select></label>
        <label>Expires after (business date)<input type="date" name="expires" min={s.date} defaultValue={s.date} required/></label></div>
      <label>Reason (required)<input name="reason" required minLength={3} maxLength={120} placeholder="e.g. Covering the cashier desk this weekend"/></label>
      <div><GuardedButton className="btn primary" block={block} busy={busy} onClick={()=>{}}>Add exception</GuardedButton></div>
    </form>
    <div className="table-wrap"><table className="adm-table"><thead><tr><th>User</th><th>Permission</th><th>Effect</th><th>Reason</th><th>Expires</th><th>Status</th><th></th></tr></thead><tbody>
      {pol.exceptions.map(e=>{const u=s.users.find(x=>x.id===e.user);const active=adminExceptionActive(s,e);
        const rb=block||(e.user===uid?'Your own access — another administrator must change it.':undefined);
        return <tr key={e.id}><td><strong>{u?firstName(u.name):e.user}</strong><small>{u?.role}</small></td><td>{e.perm}<small>{ADMIN_PERMS.find(p=>p.id===e.perm)?.action}</small></td>
          <td><Pill tone={e.effect==='deny'?'neutral':'green'}>{e.effect==='deny'?'✕ Deny':'✓ Allow'}</Pill></td><td className="adm-wrap">{e.reason}</td><td>{shortDate(e.expires)}</td><td><StatusPill status={active?'Active':'Expired'}/></td>
          <td><GuardedButton className="text-btn" block={rb} busy={busy} onClick={()=>run({type:'USER_EXCEPTION',remove:e.id,key:'exc-remove:'+e.id},d.decision==='approval'?'Removal sent for Owner approval':'Exception removed')}>Remove</GuardedButton></td></tr>;})}
    </tbody></table>{!pol.exceptions.length&&<Empty title="No user exceptions" body="Every user follows their role template."/>}</div></div>;
}

function HistoryTab(){
  const {s}=useHcc();const pol=adminPolicy(s);
  const req=adminApprovalInbox(s).filter(r=>r.kind==='policy');
  return <div className="padded">
    {req.length>0&&<><h3 className="adm-h3">Change requests</h3><div className="table-wrap"><table className="adm-table"><thead><tr><th>Request</th><th>Change</th><th>Requested by</th><th>For version</th><th>Status</th></tr></thead><tbody>
      {req.map(r=><tr key={r.a.id}><td><strong>{r.a.id}</strong><small>{r.a.reason}</small></td><td className="adm-wrap">{String(r.a.meta?.summary||'')}<small>{String(r.a.meta?.before||'')} → {String(r.a.meta?.after||'')}</small></td><td>{r.requester}</td><td>v{r.a.version}</td><td><StatusPill status={r.status}/>{r.derived&&<small>{r.derived}</small>}</td></tr>)}
    </tbody></table></div></>}
    <h3 className="adm-h3">Policy versions</h3>
    <div className="table-wrap"><table className="adm-table"><thead><tr><th>Version</th><th>When</th><th>Who</th><th>Change</th><th>Before → after</th><th>Reason</th></tr></thead><tbody>
      {pol.history.map((h,i)=><tr key={i}><td><strong>v{h.version}</strong></td><td>{shortDate(h.at.slice(0,10))} {clockTime(h.at)}</td><td>{firstName(h.actor)}</td><td className="adm-wrap">{h.summary}</td><td className="adm-wrap">{h.before} → {h.after}</td><td className="adm-wrap">{h.reason}</td></tr>)}
    </tbody></table></div></div>;
}

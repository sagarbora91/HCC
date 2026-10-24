// M20 phone Access control (view-first): users list with enable/disable, effective-access lookup. Matrix / template / exception
// EDITING stays on desktop ("Open on desktop to edit") to prevent mistakes on a small screen.
import React,{useState} from 'react';
import {Info,Monitor,UserPlus,Pencil} from 'lucide-react';
import {authorize,adminPolicy,adminUserStatus,adminEffectiveAccess,adminExceptionActive,adminApprovalInbox,ADMIN_PERMS} from '../../services/mock';
import {useHcc} from '../../app/context';
import {ChipFilter} from '../../app/mobile/components';
import {Empty,shortDate} from '../../components/ui';
import {maUserBlock} from './logic';
import {MaTag,maTone,MaIntro,MaGuard,MaRows} from './parts';

type Sec='users'|'effective'|'policy';
const first=(n:string)=>n.split(' · ')[0];
export function MaAccess(){
  const {s}=useHcc();const [sec,setSec]=useState<Sec>('users');const pol=adminPolicy(s);
  const pending=adminApprovalInbox(s).filter(r=>r.kind==='policy'&&r.status==='Pending').length;
  return <div className="ma-page">
    <div className="ma-badges"><MaTag tone="info">Policy v{pol.version}</MaTag><MaTag tone="warn">Simulated identity · not production security</MaTag></div>
    {pending>0&&<div className="ma-note"><Info size={18}/><span>{pending} policy change request{pending>1?'s':''} waiting for Owner approval — see Approvals.</span></div>}
    <ChipFilter<Sec> label="Access control" value={sec} onChange={setSec} options={[{id:'users',label:'Users',count:s.users.length},{id:'effective',label:'Effective access'},{id:'policy',label:'Roles & exceptions'}]}/>
    {sec==='users'&&<Users/>}{sec==='effective'&&<Effective/>}{sec==='policy'&&<PolicyView/>}
  </div>;
}
function Users(){
  const {s,uid,run,open,busy}=useHcc();
  const canAdmin=authorize(s,uid,'PERM-ADM-001');const adminBlock=canAdmin.decision==='allow'?undefined:canAdmin.reason;
  return <>
    <MaIntro>Disabling a user denies their next command immediately. One role per user.</MaIntro>
    {s.users.map(u=>{const st=adminUserStatus(s,u),block=maUserBlock(s,uid,u);return <article className="ma-card" key={u.id} aria-label={first(u.name)}>
      <div className="ma-card-top"><div><strong>{first(u.name)}{u.id===uid?' (you)':''}</strong><span>{u.role} · {u.id}</span></div><MaTag tone={maTone(st)}>{st}</MaTag></div>
      <MaRows rows={[['Access until',u.expiresOn?shortDate(u.expiresOn):'No expiry']]}/>
      <div className="ma-actions">
        <MaGuard className={'btn'+(u.enabled?'':' primary')} block={block} busy={busy} onClick={()=>run({type:'USER',id:u.id,key:`user:${u.id}:${u.enabled}:${s.sequence}`},u.enabled?`${first(u.name)} disabled — their next action is denied`:`${first(u.name)} enabled`)}>{u.enabled?'Disable':'Enable'}</MaGuard>
        {!adminBlock&&<button type="button" className="btn" disabled={busy} onClick={()=>open({type:'admin-user',id:u.id})}><Pencil size={16}/>Edit</button>}
      </div></article>;})}
    {!adminBlock&&<button type="button" className="btn ma-wide" onClick={()=>open({type:'admin-user'})}><UserPlus size={18}/>New user</button>}
  </>;
}
function Effective(){
  const {s,uid}=useHcc();
  const [who,setWho]=useState(uid);const [amt,setAmt]=useState('');const [only,setOnly]=useState<'All'|'allow'|'approval'|'deny'>('All');
  const all=adminEffectiveAccess(s,who,amt?{amount:Math.round(Number(amt)*100)}:{});
  const rows=all.filter(r=>only==='All'||r.decision===only);
  const label={allow:'Allowed',approval:'Approval required',deny:'Denied'} as const;
  return <>
    <div className="form ma-form">
      <label>User<select value={who} onChange={e=>setWho(e.target.value)}>{s.users.map(x=><option key={x.id} value={x.id}>{first(x.name)} · {x.role} ({adminUserStatus(s,x)})</option>)}</select></label>
      <label>Try an amount for limits (₹, optional)<input inputMode="decimal" enterKeyHint="done" value={amt} onChange={e=>setAmt(e.target.value.replace(/[^\d.]/g,''))} placeholder="e.g. 500"/></label>
    </div>
    <ChipFilter label="Show" value={only} onChange={setOnly} options={[{id:'All',label:'All',count:all.length},{id:'allow',label:'Allowed',count:all.filter(r=>r.decision==='allow').length},{id:'approval',label:'Approval',count:all.filter(r=>r.decision==='approval').length},{id:'deny',label:'Denied',count:all.filter(r=>r.decision==='deny').length}]}/>
    <MaIntro>Evaluation order (DEC-040): disabled / expired user → scope → explicit deny → no grant → limit → allow.</MaIntro>
    {rows.length?rows.map(r=><article className="ma-card" key={r.perm.id}>
      <div className="ma-card-top"><div><strong>{r.perm.action}</strong><span>{r.perm.id}</span></div><MaTag tone={maTone(label[r.decision])}>{label[r.decision]}</MaTag></div>
      <MaRows rows={[['Why',r.reason],['Rule',r.rule],...(r.module?[['Module gate',`${r.module} · ${r.moduleOn?'on':'off for role'}`]] as [string,string][]:[])]}/>
    </article>):<Empty title="Nothing in this filter" body="Choose another filter."/>}
  </>;
}
function PolicyView(){
  const {s}=useHcc();const pol=adminPolicy(s);
  const active=pol.exceptions.filter(e=>adminExceptionActive(s,e));
  return <>
    <div className="ma-desktop-only" role="note"><Monitor size={22}/><div><strong>Open on desktop to edit</strong><p>Role templates, the permission matrix, limits and user exceptions are edited on the desktop layout to prevent mistakes on a small screen. You can review them here.</p></div></div>
    <article className="ma-card"><strong>Policy</strong><MaRows rows={[['Version','v'+pol.version],['Permissions in matrix',ADMIN_PERMS.length],['Active exceptions',active.length],['Changes recorded',pol.history.length]]}/></article>
    <h2 className="ma-h2">Active user exceptions</h2>
    {active.length?active.map(e=>{const u=s.users.find(x=>x.id===e.user);return <article className="ma-card" key={e.id}>
      <div className="ma-card-top"><div><strong>{u?first(u.name):e.user}</strong><span>{e.perm} · {ADMIN_PERMS.find(p=>p.id===e.perm)?.action}</span></div><MaTag tone={e.effect==='deny'?'err':'ok'}>{e.effect==='deny'?'Deny':'Allow'}</MaTag></div>
      <MaRows rows={[['Reason',e.reason],['Expires after',shortDate(e.expires)]]}/></article>;}):<Empty title="No active exceptions" body="Every user follows their role template."/>}
    <h2 className="ma-h2">Recent policy changes</h2>
    {pol.history.length?[...pol.history].reverse().slice(0,10).map((h,i)=><article className="ma-card" key={i}><div className="ma-card-top"><div><strong>v{h.version} · {h.summary}</strong><span>{first(h.actor)} · {shortDate(h.at.slice(0,10))}</span></div></div><MaRows rows={[['Before → after',`${h.before} → ${h.after}`],['Reason',h.reason||'—']]}/></article>)
      :<Empty title="No policy changes yet"/>}
  </>;
}

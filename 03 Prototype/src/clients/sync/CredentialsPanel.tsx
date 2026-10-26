// Access control › Sign-in & devices (production build only; Wave 4 gap G1, DEC-062). Imported by features/admin/AccessControl.tsx
// inside `process.env.HCC_BUILD==='production'` branches, so the prototype bundle never contains it. Logic: ./credentials.ts.
// • Set / reset a password (Owner, Accountant, Tech admin) or a 6-digit PIN (all other staff): typed twice into uncontrolled
//   password fields, read once on submit, sent as USER_CREDENTIAL (SetCredential) and the fields are cleared — the secret is
//   never kept in React state, the app state, the read cache or a log, and is never shown again.
// • Devices: list (GET /admin/devices), register THIS device (DEVICE_REGISTER), revoke any device (DEVICE_REVOKE, confirm first).
// Same component on the desktop and in the phone shell (cards; 48 px targets and 16 px text under .m-shell — styles/sync.css).
import React,{useCallback,useEffect,useRef,useState} from 'react';
import {KeyRound,Tablet,RefreshCw,ShieldCheck,Ban} from 'lucide-react';
import type {Command} from '../../services/mock';
import {getSyncRuntime} from './instance';
import {useSync,ADMIN_PERM} from './react';
import {deviceRegistered} from './apk';
import type {RunResult} from './runtime';
import {loadCredentialDirectory,thisDeviceTag,setCredential,registerDeviceHere,revokeDevice,credentialRules,credentialKey,PIN_LENGTH,PASSWORD_MAX,
  type CredentialDirectory,type CredentialUser,type DeviceRow,type ActionOutcome,type CredKind} from './credentials';

const nonce=()=>Math.random().toString(36).slice(2,10);
const when=(clock:string)=>clock?`${clock.slice(8,10)}/${clock.slice(5,7)}/${clock.slice(0,4)} ${clock.slice(11,16)}`:'—';
type Msg={text:string;error:boolean}|null;

/** Container: reads the directory with the administrator's session and wires the commands to the sync runtime. */
export function CredentialsPanel({uid,initialUser}:{uid:string;initialUser?:string}){
  const rt=getSyncRuntime();const snap=useSync();
  const admin=!!snap.me?.permIds.includes(ADMIN_PERM);
  const [dir,setDir]=useState<CredentialDirectory|null>(null);const [loadError,setLoadError]=useState('');
  const load=useCallback(async()=>{
    if(!admin)return;setLoadError('');
    try{setDir(await loadCredentialDirectory((p,q)=>rt.transport.get(p,q),thisDeviceTag(deviceRegistered())));}
    catch(e){setLoadError(String((e as Error)?.message??e));}
  },[admin,rt]);
  useEffect(()=>{void load();},[load]);
  const run=(cmd:Command):Promise<RunResult>=>rt.run(cmd);
  return <CredentialsView admin={admin} uid={uid} dir={dir} loadError={loadError} readOnly={snap.readOnly} initialUser={initialUser} registered={deviceRegistered()}
    onReload={load}
    onSetCredential={(u,k,secret,confirm,key)=>setCredential(run,u,k,secret,confirm,key)}
    onRegister={(name,n)=>registerDeviceHere(run,rt.transport,name,n)}
    onRevoke={d=>revokeDevice(run,d)}/>;
}

export type CredentialsViewProps={admin:boolean;uid:string;dir:CredentialDirectory|null;loadError?:string;readOnly?:boolean;initialUser?:string;registered:boolean;
  onReload:()=>Promise<void>|void;
  onSetCredential:(u:CredentialUser,kind:CredKind,secret:string,confirm:string,key:string)=>Promise<ActionOutcome>;
  onRegister:(name:string,nonce:string)=>Promise<ActionOutcome>;
  onRevoke:(d:DeviceRow)=>Promise<ActionOutcome>};

/** Pure view (tests render it with a fixed directory). */
export function CredentialsView(p:CredentialsViewProps){
  const [open,setOpen]=useState<string|undefined>(p.initialUser);
  const [msg,setMsg]=useState<Msg>(null);
  if(!p.admin)return <section className="padded cred-panel" aria-label="Sign-in and devices"><p className="adm-lock" role="note"><ShieldCheck size={15}/> Only the Owner or Tech admin sets passwords and PINs and manages hotel devices (PERM-ADM-001).</p></section>;
  const after=async(r:ActionOutcome)=>{setMsg({text:r.text,error:!r.ok});if(r.ok)await p.onReload();};
  return <section className="padded cred-panel" aria-label="Sign-in and devices">
    <p className="muted">DEC-062: Owner, Accountant and Tech admin sign in with a password; all other staff with a {PIN_LENGTH}-digit PIN on a registered hotel device. A new password or PIN is shown to nobody — not even here — after it is saved: tell the person in private.</p>
    {p.readOnly&&<p className="form-error" role="alert">The server cannot be reached: passwords, PINs and devices cannot be changed now.</p>}
    {p.loadError&&<p className="form-error" role="alert">Could not read users and devices from the server: {p.loadError}</p>}
    {msg&&<p className={msg.error?'form-error':'server-ok'} role={msg.error?'alert':'status'}>{msg.text}</p>}
    <div className="adm-row-head"><h3 className="adm-h3">Passwords and PINs</h3>
      <button type="button" className="btn" onClick={()=>{setMsg(null);void p.onReload();}}><RefreshCw size={14}/>Refresh</button></div>
    {!p.dir&&!p.loadError&&<p className="muted" role="status">Loading users from the server…</p>}
    <ul className="cred-list">{p.dir?.users.map(u=><li key={u.id} className="cred-card">
      <div className="cred-head"><div><strong>{u.displayName}</strong><small>Sign-in name {u.loginName}{u.id===p.uid?' · you':''}{!u.enabled?' · disabled':''}</small></div>
        <span className={'pill '+(u.has==='NONE'?'orange':'green')}>{u.has==='NONE'?'Not set — cannot sign in yet':u.has==='PIN'?'PIN set':'Password set'}{u.lockedOut?' · locked out':''}</span></div>
      <CredentialAction u={u} self={u.id===p.uid} open={open===u.id} disabled={!!p.readOnly} onOpen={()=>{setMsg(null);setOpen(open===u.id?undefined:u.id);}}
        onSubmit={async(kind,secret,confirm,key)=>{const r=await p.onSetCredential(u,kind,secret,confirm,key);if(r.ok)setOpen(undefined);await after(r);return r;}}/>
    </li>)}</ul>
    <DevicesSection dir={p.dir} registered={p.registered} disabled={!!p.readOnly} onRegister={async(name,n)=>{const r=await p.onRegister(name,n);await after(r);return r;}}
      onRevoke={async d=>{const r=await p.onRevoke(d);await after(r);return r;}}/>
  </section>;
}

function CredentialAction({u,self,open,disabled,onOpen,onSubmit}:{u:CredentialUser;self:boolean;open:boolean;disabled:boolean;onOpen:()=>void;
  onSubmit:(kind:CredKind,secret:string,confirm:string,key:string)=>Promise<ActionOutcome>}){
  const kind=u.needs;const word=kind==='PIN'?'PIN':'password';
  const verb=u.has==='NONE'?'Set':'Reset';
  const action=useRef({nonce:nonce(),attempt:1});
  const [err,setErr]=useState('');const [busy,setBusy]=useState(false);
  if(self)return <p className="muted cred-note">Your own password: change it from your profile (another administrator can reset it).</p>;
  if(!kind)return <p className="muted cred-note">No role assigned — give this person a role first.</p>;
  const submit=async(e:React.FormEvent<HTMLFormElement>)=>{e.preventDefault();if(busy)return;
    const form=e.currentTarget;const f=new FormData(form);
    const secret=String(f.get('secret')??''),confirm=String(f.get('confirm')??'');
    form.reset(); // the secret leaves the page only in the command body; the fields never hold it after this point
    setBusy(true);setErr('');
    const a=action.current;const r=await onSubmit(kind,secret,confirm,credentialKey(u.id,a.nonce,a.attempt));
    setBusy(false);
    if(r.ok){action.current={nonce:nonce(),attempt:1};return;}
    if(r.definitive)a.attempt++; // the server answered: a corrected secret is a new action (a lost answer keeps the key)
    setErr(r.text);
  };
  return <>
    <button type="button" className="btn cred-toggle" aria-expanded={open} disabled={disabled} onClick={onOpen}><KeyRound size={16}/>{verb} {word}</button>
    {open&&<form className="form cred-form" onSubmit={submit} noValidate aria-label={`${verb} ${word} for ${u.displayName}`}>
      <p className="muted">{credentialRules(kind)}</p>
      <label>New {word}<input name="secret" type="password" autoComplete="new-password" required
        {...(kind==='PIN'?{inputMode:'numeric' as const,maxLength:PIN_LENGTH,pattern:'[0-9]*'}:{maxLength:PASSWORD_MAX})}/></label>
      <label>Type it again<input name="confirm" type="password" autoComplete="new-password" required
        {...(kind==='PIN'?{inputMode:'numeric' as const,maxLength:PIN_LENGTH,pattern:'[0-9]*'}:{maxLength:PASSWORD_MAX})}/></label>
      {err&&<p className="form-error" role="alert">{err}</p>}
      <div className="cred-actions"><button type="submit" className="btn primary" disabled={busy||disabled}><KeyRound size={16}/>{busy?'Saving…':`Save ${word}`}</button>
        <button type="button" className="btn" onClick={onOpen}>Cancel</button></div>
    </form>}
  </>;
}

function DevicesSection({dir,registered,disabled,onRegister,onRevoke}:{dir:CredentialDirectory|null;registered:boolean;disabled:boolean;
  onRegister:(name:string,nonce:string)=>Promise<ActionOutcome>;onRevoke:(d:DeviceRow)=>Promise<ActionOutcome>}){
  const [name,setName]=useState('Front desk tablet');const [busy,setBusy]=useState(false);const [confirm,setConfirm]=useState<string|undefined>();
  const reg=useRef(nonce());
  const register=async(e:React.FormEvent)=>{e.preventDefault();if(busy)return;setBusy(true);const r=await onRegister(name,reg.current);setBusy(false);
    if(r.ok||r.definitive)reg.current=nonce();};
  const revoke=async(d:DeviceRow)=>{if(busy)return;setBusy(true);await onRevoke(d);setBusy(false);setConfirm(undefined);};
  const active=dir?.devices.filter(d=>d.active)??[];const revoked=dir?.devices.filter(d=>!d.active)??[];
  return <>
    <h3 className="adm-h3">Hotel devices for PIN sign-in</h3>
    {!registered&&<form className="form cred-form cred-register" onSubmit={register} noValidate aria-label="Register this device">
      <p className="muted"><Tablet size={15} aria-hidden="true"/> This device is not registered. Register it here so staff can pick their name and sign in with their PIN on it.</p>
      <label>Device name<input name="deviceName" value={name} maxLength={48} onChange={e=>setName(e.target.value)} required/></label>
      <div className="cred-actions"><button type="submit" className="btn primary" disabled={busy||disabled||!name.trim()}><Tablet size={16}/>{busy?'Registering…':'Register this device'}</button></div>
    </form>}
    {dir&&!active.length&&<p className="muted">No device is registered: staff cannot sign in with a PIN anywhere yet.</p>}
    <ul className="cred-list">{active.map(d=><li key={d.id} className="cred-card">
      <div className="cred-head"><div><strong>{d.label}</strong><small>Device #{d.id} · registered {when(d.registeredAt)} · last used {when(d.lastSeenAt)}</small></div>
        <span className="pill green">{d.thisDevice?'Active · this device':'Active'}</span></div>
      {confirm===d.id
        ?<div className="cred-actions" role="group" aria-label={`Confirm revoking ${d.label}`}><p className="form-error">Revoke “{d.label}”? Staff can no longer sign in with a PIN on it and its open sessions end. It must be registered again to be used.</p>
          <button type="button" className="btn danger" disabled={busy||disabled} onClick={()=>void revoke(d)}><Ban size={16}/>{busy?'Revoking…':'Yes, revoke'}</button>
          <button type="button" className="btn" onClick={()=>setConfirm(undefined)}>Keep it</button></div>
        :<div className="cred-actions"><button type="button" className="btn" disabled={busy||disabled} onClick={()=>setConfirm(d.id)}><Ban size={16}/>Revoke device</button></div>}
    </li>)}</ul>
    {revoked.length>0&&<details className="cred-revoked"><summary>Revoked devices ({revoked.length})</summary><ul className="cred-list">{revoked.map(d=><li key={d.id} className="cred-card">
      <div className="cred-head"><div><strong>{d.label}</strong><small>Device #{d.id} · revoked {when(d.revokedAt)}</small></div><span className="pill neutral">Revoked</span></div></li>)}</ul></details>}
  </>;
}

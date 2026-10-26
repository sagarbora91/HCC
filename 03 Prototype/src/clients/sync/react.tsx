// Production app glue (W4-0): boot → server sign-in → the unchanged app shell over the server state; "as of" + Refresh strip.
// Imported by app/App.tsx only inside `process.env.HCC_BUILD==='production'` branches (tree-shaken out of the prototype).
// W4-12 (APK): server address setup before the first boot, token sign-in with staff pick + PIN on a registered device
// (DEC-062: password for Owner / Accountant / Tech admin), device registration by an admin, idle sign-out, Settings panel.
import React,{useEffect,useState,useSyncExternalStore} from 'react';
import {Hotel,LogIn,RefreshCw,ShieldCheck,Server,Tablet,UserRound,KeyRound} from 'lucide-react';
import type {State,Command} from '../../services/mock';
import {ReadOnlyBanner} from '../react';
import {getSyncRuntime,getAuth,tokenMode,authReady,needsServerSetup,serverBaseUrl,probeFetch} from './instance';
import type {RunResult,SyncSnapshot} from './runtime';
import {utcToClock} from './map';
import {isNative} from '../../app/native';
import {probeServer,saveServerUrl,deviceRegistered,deviceLabel,deviceStaff,rememberStaff,registerThisDevice,refreshDeviceStaff,forgetDevice,type ProbeResult} from './apk';

/** What the app shell needs from the server side (App.tsx AppShell `server` prop). */
export type ServerBinding={state:State;uid:string;run:(cmd:Command)=>Promise<RunResult>;signOut:()=>void};

export function useSync():SyncSnapshot{
  const rt=getSyncRuntime();
  return useSyncExternalStore(rt.subscribe,rt.get,rt.get);
}

/** PERM-ADM-001 (Owner, Tech admin) registers devices and reads the staff list (DEC-062). */
export const ADMIN_PERM='PERM-ADM-001';
const OFFER_SKIPPED='hcc-device-offer-skipped';
const IDLE_CHECK_MS=30_000;
const reload=()=>{try{globalThis.location?.reload();}catch{/* ignore */}};

/** Root of the production build. `render` draws the app shell (passed in to avoid an App ↔ sync import cycle). */
export function ProductionApp({render}:{render:(b:ServerBinding)=>React.ReactNode}){
  if(needsServerSetup())return <ServerSetup onSaved={reload}/>;
  return <ConnectedApp render={render}/>;
}

function ConnectedApp({render}:{render:(b:ServerBinding)=>React.ReactNode}){
  const rt=getSyncRuntime();const snap=useSync();
  const [note,setNote]=useState<string|undefined>();
  const [skipOffer,setSkipOffer]=useState(()=>{try{return sessionStorage.getItem(OFFER_SKIPPED)==='1';}catch{return false;}});
  useEffect(()=>{let detach:(()=>void)|undefined,dead=false;
    void authReady().then(()=>{if(dead)return;void rt.boot();detach=typeof window!=='undefined'?rt.attachWindow(window):undefined;});
    return ()=>{dead=true;detach?.();};},[rt]);
  // Idle / absolute expiry (token mode): once this device's requests can no longer keep the server session alive, sign out here too.
  useEffect(()=>{const a=getAuth();if(!tokenMode(a))return;
    const id=setInterval(()=>{if(rt.get().status==='ready'&&a.expired()){setNote('You were signed out after a period without activity. Sign in again.');void rt.signOut();}},IDLE_CHECK_MS);
    return ()=>clearInterval(id);},[rt]);
  if(snap.status==='booting')return <div className="boot-loading" role="status" aria-live="polite"><Hotel size={28} aria-hidden="true"/><strong>Connecting to the hotel server…</strong></div>;
  if(snap.status==='signed-out'||!snap.me)return <ServerSignIn message={snap.signInMessage??note}/>;
  if(isNative()&&!skipOffer&&!deviceRegistered()&&snap.me.permIds.includes(ADMIN_PERM))
    return <DeviceSetup onDone={()=>{try{sessionStorage.setItem(OFFER_SKIPPED,'1');}catch{/* ignore */}setSkipOffer(true);}}/>;
  return <>{render({state:snap.state,uid:snap.me.uid,run:cmd=>rt.run(cmd),signOut:()=>{setNote(undefined);void rt.signOut();}})}</>;
}

function Brand({sub}:{sub:string}){
  return <div className="signin-brand"><span className="brand-mark"><Hotel size={28}/></span><div><strong>HOTEL CONTROL CENTRE</strong><small>{sub}</small></div></div>;
}

/** APK first run (and Settings › Change server): type the server address, test it (discovery + health), save. */
export function ServerSetup({onSaved,initial,onCancel}:{onSaved:(url:string)=>void;initial?:string;onCancel?:()=>void}){
  const [url,setUrl]=useState(initial??'');const [busy,setBusy]=useState(false);const [res,setRes]=useState<ProbeResult|null>(null);
  const test=async(e?:React.FormEvent)=>{e?.preventDefault();if(busy)return;setBusy(true);setRes(null);const r=await probeServer(url,probeFetch());setBusy(false);setRes(r);return r;};
  const save=()=>{if(res?.ok){saveServerUrl(res.url);onSaved(res.url);}};
  return <main className="signin">
    <div className="signin-card">
      <Brand sub="Connect this device to the hotel server"/>
      <h1>Hotel server</h1>
      <form className="form server-signin" onSubmit={test} noValidate>
        <label>Server address<input name="serverUrl" value={url} onChange={e=>{setUrl(e.target.value);setRes(null);}} placeholder="https://hcc-server.local:8443"
          inputMode="url" autoCapitalize="none" autoCorrect="off" spellCheck={false} required/></label>
        <p className="signin-help">The Owner or Tech admin knows it: the address the server PC shows after installation (a fixed IP is most reliable, e.g. https://192.168.1.20:5443).</p>
        {res&&!res.ok&&<p className="form-error" role="alert">{res.text}</p>}
        {res?.ok&&<p className="server-ok" role="status"><ShieldCheck size={16} aria-hidden="true"/>Found <strong>{res.serverName||'the hotel server'}</strong>{res.version?` · version ${res.version}`:''} at {res.url}.</p>}
        <button type="submit" className="btn" disabled={busy||!url.trim()}><Server size={16}/>{busy?'Testing…':'Test connection'}</button>
        <button type="button" className="btn primary" disabled={!res?.ok} onClick={save}>Save and continue</button>
        {onCancel&&<button type="button" className="btn" onClick={onCancel}>Cancel</button>}
      </form>
    </div>
  </main>;
}

/** Shown once to an Owner / Tech admin who signed in with a password on an unregistered APK device. */
function DeviceSetup({onDone}:{onDone:()=>void}){
  return <main className="signin"><div className="signin-card">
    <Brand sub="Shared device set-up (DEC-062)"/>
    <h1>Register this device?</h1>
    <p className="signin-help"><Tablet size={16} aria-hidden="true"/>Staff can sign in with a PIN only on a registered hotel device. Register it now if this tablet or phone stays at the hotel; you can also do it later in Master configuration.</p>
    <RegisterDeviceForm onDone={onDone}/>
    <button type="button" className="btn" onClick={onDone}>Not now</button>
  </div></main>;
}

function RegisterDeviceForm({onDone}:{onDone?:()=>void}){
  const rt=getSyncRuntime();
  const [name,setName]=useState('Front desk tablet');const [busy,setBusy]=useState(false);const [msg,setMsg]=useState<{text:string;error:boolean}|null>(null);
  const submit=async(e:React.FormEvent)=>{e.preventDefault();if(busy)return;setBusy(true);setMsg(null);
    const r=await registerThisDevice(rt.transport,name);setBusy(false);
    if(!r.ok){setMsg({text:r.text,error:true});return;}
    setMsg({text:`Registered as "${r.label}" (device #${r.deviceId}). ${r.staff} staff member${r.staff===1?'':'s'} can pick their name and sign in with a PIN here.`,error:false});
    onDone?.();};
  return <form className="form server-signin" onSubmit={submit} noValidate>
    <label>Device name<input name="deviceName" value={name} maxLength={48} onChange={e=>setName(e.target.value)} required/></label>
    {msg&&<p className={msg.error?'form-error':'server-ok'} role={msg.error?'alert':'status'}>{msg.text}</p>}
    <button type="submit" className="btn primary" disabled={busy||!name.trim()}><Tablet size={16}/>{busy?'Registering…':'Register this device'}</button>
  </form>;
}

/** Server sign-in (DEC-062): on a registered device staff pick their name and type their PIN; Owner / Accountant / Tech admin use a password. */
export function ServerSignIn({message}:{message?:string}){
  const rt=getSyncRuntime();
  const registered=deviceRegistered();const staff=registered?deviceStaff():[];
  const [mode,setMode]=useState<'password'|'pin'>(registered?'pin':'password');
  const [picked,setPicked]=useState<string>('');const [other,setOther]=useState(staff.length===0);
  const [busy,setBusy]=useState(false);const [err,setErr]=useState('');
  const submit=async(e:React.FormEvent<HTMLFormElement>)=>{e.preventDefault();if(busy)return;
    const f=new FormData(e.currentTarget);const loginName=mode==='pin'&&!other?picked:String(f.get('loginName')??''),secret=String(f.get('secret')??'');
    if(mode==='pin'&&!loginName){setErr('Tap your name first.');return;}
    setBusy(true);setErr('');
    const r=await rt.signIn(mode==='password'?{loginName,password:secret}:{loginName,pin:secret});
    setBusy(false);
    if(!r.ok){setErr(r.text??'Sign-in failed.');return;}
    const me=rt.get().me;if(mode==='pin'&&me)rememberStaff({loginName:loginName.trim(),displayName:me.displayName});};
  const shown=err||message;
  const a=getAuth();const token=tokenMode(a);const sessionOnly=tokenMode(a)&&a.storeKind!=='preferences';
  return <main className="signin">
    <div className="signin-card">
      <Brand sub={registered?`Hotel device · ${deviceLabel()}`:'Sign in to the hotel server'}/>
      <h1>Sign in</h1>
      {registered&&<div className="server-signin-mode" role="radiogroup" aria-label="Sign in with">
        <label><input type="radio" name="mode" checked={mode==='pin'} onChange={()=>{setMode('pin');setErr('');}}/> Staff PIN</label>
        <label><input type="radio" name="mode" checked={mode==='password'} onChange={()=>{setMode('password');setErr('');}}/> Owner / Accountant / Tech admin (password)</label>
      </div>}
      <form className="form server-signin" onSubmit={submit} noValidate key={mode}>
        {mode==='pin'&&!other&&<div className="server-staff" role="radiogroup" aria-label="Your name">
          {staff.map(x=><button type="button" key={x.loginName} role="radio" aria-checked={picked===x.loginName} className={'btn server-staff-btn'+(picked===x.loginName?' primary':'')}
            onClick={()=>{setPicked(x.loginName);setErr('');}}><UserRound size={16}/>{x.displayName}</button>)}
          <button type="button" className="btn server-staff-btn" onClick={()=>{setOther(true);setPicked('');}}>Someone else</button>
        </div>}
        {(mode==='password'||other)&&<label>Login name<input name="loginName" autoComplete="username" required autoCapitalize="none" spellCheck={false}/></label>}
        {mode==='pin'&&other&&staff.length>0&&<button type="button" className="btn" onClick={()=>setOther(false)}>Back to the staff list</button>}
        <label>{mode==='password'?'Password':'PIN'}<input name="secret" type="password" required autoComplete={mode==='password'?'current-password':'off'} inputMode={mode==='pin'?'numeric':undefined}/></label>
        {shown&&<p className="form-error" role="alert">{shown}</p>}
        <button type="submit" className="btn primary" disabled={busy}>{mode==='pin'?<KeyRound size={16}/>:<LogIn size={16}/>}{busy?'Signing in…':'Sign in'}</button>
      </form>
      {!registered&&<p className="signin-help"><Tablet size={16} aria-hidden="true"/>Staff PIN sign-in works only on a registered hotel device. An Owner or Tech admin signs in here with a password to register this device.</p>}
      <p className="signin-help"><ShieldCheck size={16} aria-hidden="true"/>{token
        ?`Your session is kept by the server and ends after a period without activity.${sessionOnly?' You sign in again after the app is closed.':''}`
        :'Your session is kept by the server. Nothing is stored on this device except a read-only copy of the last loaded data.'}</p>
      {isNative()&&<ChangeServer/>}
    </div>
  </main>;
}

/** "Server: … · Change": a new server drops this device's registration and staff list (they belong to the old server). */
function ChangeServer(){
  const [open,setOpen]=useState(false);const url=serverBaseUrl();
  if(open)return <div className="server-change"><p className="form-error" role="alert">Changing the server signs out and removes this device's registration{deviceRegistered()?' — an Owner or Tech admin must register it again':''}.</p>
    <ServerSetup initial={url} onCancel={()=>setOpen(false)} onSaved={()=>{void getSyncRuntime().signOut().finally(()=>{forgetDevice();reload();});}}/></div>;
  return <p className="signin-help"><Server size={16} aria-hidden="true"/>Server: {url||'this address'} <button type="button" className="btn link" onClick={()=>setOpen(true)}>Change</button></p>;
}

/** Master configuration (production only): server address, this device's registration and staff list. */
export function ServerDevicePanel(){
  const rt=getSyncRuntime();const snap=useSync();
  const admin=!!snap.me?.permIds.includes(ADMIN_PERM);
  const [change,setChange]=useState(false);const [,bump]=useState(0);
  const [msg,setMsg]=useState<{text:string;error:boolean}|null>(null);const [busy,setBusy]=useState(false);
  const url=serverBaseUrl();const registered=deviceRegistered();
  const updateStaff=async()=>{setBusy(true);setMsg(null);
    try{const n=await refreshDeviceStaff(rt.transport);setMsg({text:`Staff list updated: ${n} PIN user${n===1?'':'s'}.`,error:false});}
    catch(e){setMsg({text:String((e as Error)?.message??e),error:true});}
    setBusy(false);};
  if(change)return <ServerSetup initial={url} onCancel={()=>setChange(false)} onSaved={()=>{void rt.signOut().finally(()=>{forgetDevice();reload();});}}/>;
  return <section className="cfg-sec server-device" aria-label="Server and this device">
    <h2>Server and this device</h2>
    <ul>
      <li className="cfg-row"><Server size={16} aria-hidden="true"/> Hotel server: <strong>{url||'this web address (same origin)'}</strong>
        {isNative()&&admin&&<button type="button" className="btn" onClick={()=>setChange(true)}>Change server</button>}</li>
      <li className="cfg-row"><Tablet size={16} aria-hidden="true"/> {registered?<>Registered device: <strong>{deviceLabel()||'yes'}</strong> · {deviceStaff().length} staff in the PIN list</>:'This device is not registered: staff cannot sign in with a PIN here.'}</li>
    </ul>
    {admin&&!registered&&<RegisterDeviceForm onDone={()=>bump(n=>n+1)}/>}
    {admin&&registered&&<div className="server-device-actions">
      <button type="button" className="btn" disabled={busy} onClick={updateStaff}><RefreshCw size={14}/>Update staff list</button>
      <button type="button" className="btn" onClick={()=>{forgetDevice();setMsg({text:'Registration removed from this device. Revoke it on the server in Admin › Devices as well.',error:false});bump(n=>n+1);}}>Remove registration here</button>
    </div>}
    {msg&&<p className={msg.error?'form-error':'server-ok'} role={msg.error?'alert':'status'}>{msg.text}</p>}
    {!admin&&<p className="signin-help">Only the Owner or Tech admin changes the server or registers devices.</p>}
  </section>;
}

const asOfLabel=(iso?:string)=>{const c=utcToClock(iso);return c?`${c.slice(8,10)}/${c.slice(5,7)} ${c.slice(11,16)}`:'—';};
/** Production strip under the header: read-only banner when the server is unreachable, otherwise "Server data as of … · Refresh". */
export function SyncStatus(){
  const snap=useSync();const rt=getSyncRuntime();const [busy,setBusy]=useState(false);
  const failed=Object.values(snap.errors).filter(Boolean) as string[];
  if(snap.readOnly)return <><ReadOnlyBanner/>{snap.asOf&&<div className="sync-status" role="status">Showing data as of {asOfLabel(snap.asOf)} (last loaded from the server).</div>}</>;
  const refresh=()=>{setBusy(true);rt.refresh().finally(()=>setBusy(false));};
  return <div className="sync-status" role="status" aria-live="polite">
    <span>{snap.loading?'Loading from the server…':`Server data as of ${asOfLabel(snap.asOf)}`}</span>
    {failed.length>0&&<span className="sync-status-error" title={failed.join('\n')}> · {failed.length} area{failed.length>1?'s':''} could not load</span>}
    <button type="button" className="btn" disabled={busy||snap.loading} onClick={refresh}><RefreshCw size={14}/>Refresh</button>
  </div>;
}

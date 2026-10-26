// Production app glue (W4-0): boot → server sign-in → the unchanged app shell over the server state; "as of" + Refresh strip.
// Imported by app/App.tsx only inside `process.env.HCC_BUILD==='production'` branches (tree-shaken out of the prototype).
import React,{useEffect,useState,useSyncExternalStore} from 'react';
import {Hotel,LogIn,RefreshCw,ShieldCheck} from 'lucide-react';
import type {State,Command} from '../../services/mock';
import {ReadOnlyBanner} from '../react';
import {getSyncRuntime} from './instance';
import type {RunResult,SyncSnapshot} from './runtime';
import {utcToClock} from './map';

/** What the app shell needs from the server side (App.tsx AppShell `server` prop). */
export type ServerBinding={state:State;uid:string;run:(cmd:Command)=>Promise<RunResult>;signOut:()=>void};

export function useSync():SyncSnapshot{
  const rt=getSyncRuntime();
  return useSyncExternalStore(rt.subscribe,rt.get,rt.get);
}

/** Root of the production build. `render` draws the app shell (passed in to avoid an App ↔ sync import cycle). */
export function ProductionApp({render}:{render:(b:ServerBinding)=>React.ReactNode}){
  const rt=getSyncRuntime();const snap=useSync();
  useEffect(()=>{void rt.boot();const detach=typeof window!=='undefined'?rt.attachWindow(window):undefined;return ()=>detach?.();},[rt]);
  if(snap.status==='booting')return <div className="boot-loading" role="status" aria-live="polite"><Hotel size={28} aria-hidden="true"/><strong>Connecting to the hotel server…</strong></div>;
  if(snap.status==='signed-out'||!snap.me)return <ServerSignIn message={snap.signInMessage}/>;
  return <>{render({state:snap.state,uid:snap.me.uid,run:cmd=>rt.run(cmd),signOut:()=>{void rt.signOut();}})}</>;
}

/** Server sign-in (cookie session, DEC-062): password for Owner / Accountant / Tech admin, PIN for other staff on a registered device. */
export function ServerSignIn({message}:{message?:string}){
  const rt=getSyncRuntime();
  const [mode,setMode]=useState<'password'|'pin'>('password');const [busy,setBusy]=useState(false);const [err,setErr]=useState('');
  const submit=async(e:React.FormEvent<HTMLFormElement>)=>{e.preventDefault();if(busy)return;
    const f=new FormData(e.currentTarget);const loginName=String(f.get('loginName')??''),secret=String(f.get('secret')??'');
    setBusy(true);setErr('');
    const r=await rt.signIn(mode==='password'?{loginName,password:secret}:{loginName,pin:secret});
    setBusy(false);if(!r.ok)setErr(r.text??'Sign-in failed.');};
  const shown=err||message;
  return <main className="signin">
    <div className="signin-card">
      <div className="signin-brand"><span className="brand-mark"><Hotel size={28}/></span><div><strong>HOTEL CONTROL CENTRE</strong><small>Sign in to the hotel server</small></div></div>
      <h1>Sign in</h1>
      <form className="form server-signin" onSubmit={submit} noValidate>
        <label>Login name<input name="loginName" autoComplete="username" required autoCapitalize="none" spellCheck={false}/></label>
        <div className="server-signin-mode" role="radiogroup" aria-label="Sign in with">
          <label><input type="radio" name="mode" checked={mode==='password'} onChange={()=>setMode('password')}/> Password</label>
          <label><input type="radio" name="mode" checked={mode==='pin'} onChange={()=>setMode('pin')}/> PIN (registered device)</label>
        </div>
        <label>{mode==='password'?'Password':'PIN'}<input name="secret" type="password" required autoComplete={mode==='password'?'current-password':'off'} inputMode={mode==='pin'?'numeric':undefined}/></label>
        {shown&&<p className="form-error" role="alert">{shown}</p>}
        <button type="submit" className="btn primary" disabled={busy}><LogIn size={16}/>{busy?'Signing in…':'Sign in'}</button>
      </form>
      <p className="signin-help"><ShieldCheck size={16} aria-hidden="true"/>Your session is kept by the server. Nothing is stored on this device except a read-only copy of the last loaded data.</p>
    </div>
  </main>;
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

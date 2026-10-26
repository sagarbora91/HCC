// A02b-05 "Change my password / PIN" for every signed-in user: desktop dialog and phone sheet (ModalHost 'own-credential', opened from
// the profile menu / sheet and the menu drawer), and — production build — the screen forced right after a sign-in with a password / PIN
// an administrator set (A06-01, clients/sync/react.tsx ForcedCredentialChange). Both builds send OWN_CREDENTIAL_CHANGE: the demo mock
// (services/mock/handlers/credentials.ts) and the server's ChangeOwnCredential (clients/sync/admin.ts) apply the same rules
// (domain/credentials.ts). The secrets are read from the form once, the fields are cleared, and only the command body carries them —
// never the state, the read cache or the Idempotency-Key.
import React,{useRef,useState} from 'react';
import {KeyRound} from 'lucide-react';
import {useHcc} from './context';
import type {Command} from '../services/mock';
import {credentialRules,ownCredentialProblem,ownCredentialCommand,ownCredentialKey,kindOfRole,DEMO_PIN,DEMO_PASSWORD,PIN_LENGTH,PASSWORD_MAX,type CredKind} from '../domain/credentials';
import {getSyncRuntime} from '../clients/sync/instance'; // production build only

export type OwnCredentialOutcome={ok:true}|{ok:false;text?:string;/** the answer is known (a corrected try is a new action); false = lost answer, same key */definitive?:boolean};
const nonce=()=>Math.random().toString(36).slice(2,8);
export const wordOf=(kind:CredKind)=>kind==='PIN'?'PIN':'password';

/** The form: current secret, new secret twice, the rules; a client-side check first (same wording as the server). */
export function OwnCredentialForm({kind,uid,loginName='',send,forced=false,mobile=false}:{kind:CredKind;uid:string;
  /** For the "not containing the sign-in name" rule when known (demo: the user id); the server always checks it. */loginName?:string;
  send:(cmd:Command)=>Promise<OwnCredentialOutcome>;forced?:boolean;mobile?:boolean}){
  const word=wordOf(kind);const [busy,setBusy]=useState(false);const [err,setErr]=useState('');
  const action=useRef({nonce:nonce(),attempt:1});
  const input=kind==='PIN'?{inputMode:'numeric' as const,maxLength:PIN_LENGTH,pattern:'[0-9]*'}:{maxLength:PASSWORD_MAX};
  const submit=async(e:React.FormEvent<HTMLFormElement>)=>{e.preventDefault();if(busy)return;
    const form=e.currentTarget;const f=new FormData(form);
    const current=String(f.get('current')??''),next=String(f.get('next')??''),confirm=String(f.get('confirm')??'');
    const bad=ownCredentialProblem(kind,current,next,confirm,loginName);
    if(bad){setErr(bad);return;}
    form.reset(); // from here on the secrets exist only in the command body
    setBusy(true);setErr('');
    const a=action.current;
    const r=await send(ownCredentialCommand(kind,current,next,ownCredentialKey(uid,a.nonce,a.attempt)));
    setBusy(false);
    if(r.ok){action.current={nonce:nonce(),attempt:1};return;}
    if(r.definitive!==false)a.attempt++;
    if(r.text)setErr(r.text);
  };
  return <form className={'form own-cred-form'+(mobile?' m-sheet-form':'')} onSubmit={submit} noValidate aria-label={`Change my ${word}`}>
    <p className="muted">New {word}: {credentialRules(kind)} {forced?`It must not be the ${word} you were given.`:''}</p>
    <label>Current {word}<input name="current" type="password" autoComplete={kind==='PIN'?'off':'current-password'} required {...input}/></label>
    <label>New {word}<input name="next" type="password" autoComplete="new-password" required {...input}/></label>
    <label>Type the new {word} again<input name="confirm" type="password" autoComplete="new-password" required {...input}/></label>
    {err&&<p className="form-error" role="alert">{err}</p>}
    <div className="modal-actions"><button type="submit" className={'btn primary'+(mobile?' full':'')} disabled={busy}><KeyRound size={16}/>{busy?'Changing…':`Change ${word}`}</button></div>
  </form>;
}

/** The kind this session signs in with: production = what the server says (DEC-062: any password role, even an expired one); demo = by role. */
export function useOwnCredentialKind():CredKind{
  const {user}=useHcc();
  if(process.env.HCC_BUILD==='production')return getSyncRuntime().get().me?.credentialKind??kindOfRole(user.role);
  return kindOfRole(user.role);
}

/** ModalHost 'own-credential': the dialog (desktop) / sheet (phone). */
export function OwnCredentialSheet(){
  const {uid,user,run,toast,closeModal,display}=useHcc();
  const kind=useOwnCredentialKind();const word=wordOf(kind);
  const prod=process.env.HCC_BUILD==='production';
  const done=`Your ${word} is changed.${prod?' Your sessions on other devices have ended; this one continues.':''}`;
  const send=async(cmd:Command):Promise<OwnCredentialOutcome>=>{
    if(process.env.HCC_BUILD==='production'){
      const r=await getSyncRuntime().run(cmd);
      if(r.ok){closeModal();toast(done);return {ok:true};}
      return {ok:false,text:r.text,definitive:r.reason!=='server'||(r.error as {wireCode?:string}|undefined)?.wireCode!=='UNAVAILABLE'};
    }
    return (await run(cmd,done,{close:true}))?{ok:true}:{ok:false}; // demo: the refusal is shown under the form by ModalHost
  };
  return <><div className="modal-heading"><div className="eyebrow">{user.name.split(' · ')[0]} · {user.role}</div><h2>Change my {word}</h2>
    <p>Type your current {word}, then your new one twice. Nobody else — not the Owner or the Tech admin — learns it.{prod?' Your sessions on other devices end; this one continues.':''}</p></div>
    {!prod&&<p className="info-note" role="note">Demo: every demo user starts with {kind==='PIN'?`PIN ${DEMO_PIN}`:`password ${DEMO_PASSWORD}`} until they change it on this device.</p>}
    <OwnCredentialForm kind={kind} uid={uid} loginName={prod?'':uid} send={send} mobile={display.isMobile}/></>;
}

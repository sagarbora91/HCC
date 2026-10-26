// Sign-in seam (W4-0). The production web build signs in with the server's HttpOnly cookie session (POST /auth/sign-in,
// `credentials:'include'`), exactly as Wave 1. W4-12 adds the APK's token sign-in (`returnToken:true` → secure storage →
// `Authorization: Bearer …`, plus the device token header for PIN sign-in) by providing another AuthAdapter here — nothing else
// in the sync layer needs to change: the runtime asks the adapter for request headers and for sign-in / sign-out.
import type {HttpTransport} from '../http';
import {HccClientError} from '../errors';
import {isSealed,sealText,openText} from './seal';

/** POST /auth/sign-in body (Hcc.Contracts SignInRequest). Exactly one of password (Owner, Accountant, Tech admin) or pin
 *  (other staff, registered device only — DEC-062). */
export type SignInInput={loginName:string;password?:string;pin?:string};
/** SignInResult (sessionToken only when returnToken was asked for). */
export type SrvSignInResult={userId:number;displayName:string;credentialKind:string;session:{idleExpiresUtc:string;absoluteExpiresUtc:string;idleTimeoutSeconds:number};sessionToken?:string|null};

export interface AuthAdapter{
  readonly kind:'cookie'|'token';
  /** Headers added to every request (cookie: none, except the device token for PIN sign-in on a registered device). */
  headers():Record<string,string>;
  signIn(t:HttpTransport,input:SignInInput):Promise<SrvSignInResult>;
  signOut(t:HttpTransport):Promise<void>;
}

/** Device token of a registered shared device, sent as X-HCC-Device on EVERY request of this device — sign-in and every later
 *  request of the session (security review F-M07: the server checks it for PIN sessions on each request; header name agreed
 *  with the server job S2). Storage: the production app calls useDeviceTokenStore() at start (APK: Capacitor Preferences, sealed
 *  with the local wrapping key; browser: sealed localStorage) and keeps the token in memory for the synchronous header. Without
 *  that call (tests, prototype) it is read from localStorage `hcc-device-token` as before. */
export const DEVICE_HEADER='X-HCC-Device';
export const DEVICE_TOKEN_KEY='hcc-device-token';
const lsKv=()=>{try{return globalThis.localStorage;}catch{return undefined;}};
let deviceVault:{store:TokenStore;token:string}|null=null;
export function deviceToken():string{
  if(deviceVault)return deviceVault.token;
  try{return lsKv()?.getItem(DEVICE_TOKEN_KEY)||'';}catch{return '';}
}
/** Keeps (token) or drops (null) this device's token. Resolves once it is stored. */
export async function setDeviceToken(token:string|null):Promise<void>{
  if(deviceVault){deviceVault.token=token??'';await (token?deviceVault.store.save(token):deviceVault.store.clear()).catch(()=>undefined);return;}
  try{if(token)lsKv()?.setItem(DEVICE_TOKEN_KEY,token);else lsKv()?.removeItem(DEVICE_TOKEN_KEY);}catch{/* storage unavailable */}
}
/** Production start: device token from `store` (an older plain localStorage copy is moved into it and deleted). */
export async function useDeviceTokenStore(store:TokenStore):Promise<void>{
  let t=(await store.load().catch(()=>null))||'';
  let legacy='';try{legacy=lsKv()?.getItem(DEVICE_TOKEN_KEY)||'';}catch{/* ignore */}
  if(!t&&legacy){t=legacy;await store.save(legacy).catch(()=>undefined);}
  if(legacy){try{lsKv()?.removeItem(DEVICE_TOKEN_KEY);}catch{/* ignore */}}
  deviceVault={store,token:t};
}
/** Test hook: back to the localStorage default. */
export const resetDeviceTokenStore=()=>{deviceVault=null;};

function check(input:SignInInput){
  if(!input.loginName?.trim())throw new HccClientError('VALIDATION','Enter your login name.');
  if(!!input.password===!!input.pin)throw new HccClientError('VALIDATION','Enter your password or your PIN.');
}

/** Browser: HttpOnly cookie set by the server; nothing stored by the app. */
export function cookieAuth():AuthAdapter{
  return {
    kind:'cookie',
    headers(){const d=deviceToken();const h:Record<string,string>={};if(d)h[DEVICE_HEADER]=d;return h;},
    async signIn(t,input){check(input);
      return t.post<SrvSignInResult>('/auth/sign-in',{loginName:input.loginName.trim(),...(input.password?{password:input.password}:{pin:input.pin}),returnToken:false});},
    async signOut(t){await t.post('/auth/sign-out');},
  };
}

// ── APK token mode (W4-12) ──
// The APK's web view runs at https://localhost, so the server's HttpOnly cookie is never sent (cross-origin). The APK signs in
// with `returnToken:true`, keeps the session token in app storage (TokenStore) and sends `Authorization: Bearer …` (the server's
// RequireHccSession accepts the bearer token before the cookie). A 401 on a request that carried the token drops it (the runtime
// then shows the sign-in screen); sign-out revokes the session on the server (POST /auth/sign-out with the bearer) and drops it.
// Idle / absolute expiry follow the server: SignInResult.session gives idleTimeoutSeconds and absoluteExpiresUtc, and `expired()`
// says when this device's own requests can no longer keep the session alive (the app then signs out).

/** Storage key of the session token (Capacitor Preferences or sessionStorage). */
export const SESSION_TOKEN_KEY='hcc-session-token';
export type StoredSession={token:string;userId:number;absoluteExpiresUtc:string;idleTimeoutSeconds:number;lastActivity:number};
export interface TokenStore{
  /** 'preferences' = Capacitor Preferences (kept across app restarts); 'session' = sessionStorage (until the app is closed); 'memory' = tests / no storage. */
  readonly kind:'preferences'|'session'|'memory';
  load():Promise<string|null>;save(v:string):Promise<void>;clear():Promise<void>;
}
export function memoryTokenStore():TokenStore{let v:string|null=null;return {kind:'memory',async load(){return v;},async save(x){v=x;},async clear(){v=null;}};}
/** Fallback when the APK has no Preferences plugin: the web view's sessionStorage (the token is gone when Android closes the app).
 *  Also used over another Web Storage / key (e.g. the browser's sealed device token in localStorage). */
export function sessionTokenStore(storage:Pick<Storage,'getItem'|'setItem'|'removeItem'>|null|undefined=(()=>{try{return globalThis.sessionStorage;}catch{return undefined;}})(),key=SESSION_TOKEN_KEY):TokenStore{
  return {kind:storage?'session':'memory',
    async load(){try{return storage?.getItem(key)??null;}catch{return null;}},
    async save(x){try{storage?.setItem(key,x);}catch{/* storage unavailable: memory only */}},
    async clear(){try{storage?.removeItem(key);}catch{/* ignore */}}};
}
type PreferencesLike={get(o:{key:string}):Promise<{value:string|null}>;set(o:{key:string;value:string}):Promise<void>;remove(o:{key:string}):Promise<void>};
/** Capacitor Preferences (app-private SharedPreferences), reached through the native bridge (no npm package in 03 Prototype). */
export function preferencesTokenStore(p:PreferencesLike,key=SESSION_TOKEN_KEY):TokenStore{
  return {kind:'preferences',
    async load(){try{return (await p.get({key}))?.value??null;}catch{return null;}},
    async save(x){await p.set({key,value:x});},
    async clear(){try{await p.remove({key});}catch{/* ignore */}}};
}
/** F-M07: values sealed with AES-256-GCM under `key()` (the local wrapping key, seal.ts localWrapKey) before they reach the
 *  inner store, so SharedPreferences / Web Storage never hold the token in clear. A plain value written by an older build is
 *  read once and re-written sealed. Without a key (no WebCrypto / IndexedDB) values are stored unsealed (documented limit). */
export function sealedTokenStore(inner:TokenStore,key:()=>Promise<CryptoKey|null>,purpose:string):TokenStore{
  const aad='hcc-store|'+purpose;const k=()=>key().catch(()=>null);
  return {kind:inner.kind,
    async load(){
      const v=await inner.load().catch(()=>null);if(v==null||v==='')return null;
      const wk=await k();
      if(!isSealed(v)){if(wk)await inner.save(await sealText(wk,v,aad)).catch(()=>undefined);return v;}
      if(!wk)return null;
      const t=await openText(wk,v,aad);if(t===null)await inner.clear();
      return t;
    },
    async save(v){const wk=await k();await inner.save(wk?await sealText(wk,v,aad):v);},
    clear:()=>inner.clear()};
}

export type TokenAuth=AuthAdapter&{
  readonly kind:'token';readonly storeKind:TokenStore['kind'];
  /** Reads the stored session (call before the runtime boots); an expired one is dropped. */
  ready():Promise<void>;
  /** Wraps the transport's fetch: records activity on answered requests and drops the token on a 401 to a request that carried it. */
  observe<F extends (url:string,init?:{headers?:Record<string,string>})=>Promise<{status:number}>>(f:F):F;
  /** True when a stored session can no longer be alive on the server (idle timeout or absolute lifetime passed). */
  expired(now?:number):boolean;
  hasSession():boolean;
  /** Drops the token locally (no server call). */
  forget():Promise<void>;
};
const SAVE_ACTIVITY_MS=60_000;
export function tokenAuth(o:{store:TokenStore;now?:()=>number}):TokenAuth{
  const now=o.now??(()=>Date.now());
  let cur:StoredSession|null=null,savedAt=0;
  const persist=async()=>{if(cur){savedAt=now();await o.store.save(JSON.stringify(cur)).catch(()=>undefined);}};
  const forget=async()=>{cur=null;await o.store.clear();};
  const isExpired=(s:StoredSession,t:number)=>{const abs=Date.parse(s.absoluteExpiresUtc);
    return (Number.isFinite(abs)&&t>=abs)||(s.idleTimeoutSeconds>0&&t-s.lastActivity>=s.idleTimeoutSeconds*1000);};
  const bearer=()=>cur?'Bearer '+cur.token:'';
  return {
    kind:'token',storeKind:o.store.kind,
    async ready(){
      const raw=await o.store.load().catch(()=>null);cur=null;
      if(!raw)return;
      try{const s=JSON.parse(raw) as StoredSession;if(s&&typeof s.token==='string'&&s.token&&!isExpired(s,now()))cur=s;}catch{/* unreadable: dropped */}
      if(!cur)await o.store.clear();
    },
    headers(){const h:Record<string,string>={};const d=deviceToken();if(d)h[DEVICE_HEADER]=d;if(cur)h.Authorization=bearer();return h;},
    async signIn(t,input){check(input);
      await forget();
      const r=await t.post<SrvSignInResult>('/auth/sign-in',{loginName:input.loginName.trim(),...(input.password?{password:input.password}:{pin:input.pin}),returnToken:true});
      if(!r||typeof r.sessionToken!=='string'||!r.sessionToken)throw new HccClientError('PROTOCOL','The server did not return a session token for this app. Update the hotel server.');
      cur={token:r.sessionToken,userId:r.userId,absoluteExpiresUtc:r.session?.absoluteExpiresUtc??'',idleTimeoutSeconds:r.session?.idleTimeoutSeconds??0,lastActivity:now()};
      await persist();
      return r;
    },
    async signOut(t){
      try{if(cur)await t.post('/auth/sign-out');}
      finally{await forget();}
    },
    observe(f){
      const wrapped=async(url:string,init?:{headers?:Record<string,string>})=>{
        const sent=init?.headers?.Authorization;
        const res=await f(url,init);
        if(cur&&sent===bearer()){
          if(res.status===401)await forget();
          else{cur.lastActivity=now();if(now()-savedAt>=SAVE_ACTIVITY_MS)await persist();}
        }
        return res;
      };
      return wrapped as unknown as typeof f;
    },
    expired(t=now()){return !!cur&&isExpired(cur,t);},
    hasSession:()=>!!cur,
    forget,
  };
}

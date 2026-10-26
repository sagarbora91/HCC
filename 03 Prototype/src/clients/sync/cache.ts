// Read cache of the last server data (W4-0; encrypted since the 2026-10-26 security review, F-H01). One record per loaded
// slice + a meta record + a lock record, in its OWN IndexedDB database ("hcc-server-cache") — never the prototype's demo store
// ("hcc", app/storage.ts). It is only a cache for read-only mode (server unreachable: screens keep showing the last loaded data
// with its "as of" time). Nothing is ever queued here: commands are refused while the server is unreachable.
//
// • Encryption: every slice and the meta record are sealed with AES-256-GCM (seal.ts) under a random per-sign-in data key.
//   The data key is never stored in clear; the lock record keeps it only wrapped twice:
//     - by the sign-in secret (PBKDF2-SHA256 of the password / PIN, random salt) → offline re-authentication;
//     - by a random "resume" key kept OUTSIDE this database (runtime option keyStore: Capacitor Preferences / sessionStorage),
//       so the same signed-in session can reopen it after an app restart without typing the secret again.
// • Shown only to a signed-in user: a session the server confirms (same user), or — server unreachable — a still-valid local
//   session (resume key + idle / absolute expiry from the lock record), or re-authentication with the PIN / password.
// • Wiped on sign-out, on a session end (401), when another user signs in, on idle or absolute session expiry, after
//   CACHE_MAX_UNLOCK_FAILURES wrong offline attempts, and when an old unencrypted cache (format 1) is found.
import type {SyncMe} from './types';
import type {IdSnapshot} from './map';
import {subtle,randomBytes,toB64,fromB64,importAesKey,pbkdf2Key,sealText,openText} from './seal';

export const CACHE_DB='hcc-server-cache',CACHE_STORE='slices',CACHE_FORMAT='hcc-server-cache-2';
export const LOCK_RECORD='lock',META_RECORD='meta',SLICE_PREFIX='s:';
/** PBKDF2 iterations for the offline unlock (same order as the server's PIN hashing). Tests pass a small number. */
export const CACHE_KDF_ITERATIONS=210_000;
export const CACHE_MAX_UNLOCK_FAILURES=5;
export type CachedSlice={value:unknown;asOf:string};
export type CacheMeta={me:SyncMe;asOf:string;ids:IdSnapshot};
export type CacheLock={format:string;userId:number;displayName:string;credentialKind:'password'|'pin';
  absoluteExpiresUtc:string;idleTimeoutSeconds:number;/** ms epoch of the last answered server request */lastActivity:number;
  salt:string;iterations:number;/** data key wrapped by the PBKDF2 key of the secret */bySecret:string;/** data key wrapped by the resume key */byResume:string;failures:number};
/** Raw record store (name → string). Values other than the lock record are sealed text. */
export interface ReadCache{
  readAll():Promise<Record<string,string>>;
  /** Writes the given records in one transaction. */
  write(records:Record<string,string>):Promise<void>;
  clear():Promise<void>;
}

/** Tests and browsers without IndexedDB. `dump()` = the stored records (sealed) or null when empty. */
export function createMemoryCache():ReadCache&{dump():Record<string,string>|null}{
  let recs:Record<string,string>={};
  return {
    async readAll(){return {...recs};},
    async write(r){recs={...recs,...r};},
    async clear(){recs={};},
    dump:()=>Object.keys(recs).length?{...recs}:null,
  };
}

export function createIdbCache(idb:IDBFactory):ReadCache{
  let dbp:Promise<IDBDatabase>|null=null;
  const db=()=>dbp??=new Promise<IDBDatabase>((res,rej)=>{
    const r=idb.open(CACHE_DB,1);
    r.onupgradeneeded=()=>{r.result.createObjectStore(CACHE_STORE);};
    r.onsuccess=()=>res(r.result);r.onerror=()=>{dbp=null;rej(r.error);};r.onblocked=()=>{dbp=null;rej(new Error('IndexedDB blocked'));};
  });
  const tx=async(mode:IDBTransactionMode,fn:(st:IDBObjectStore)=>void)=>{const d=await db();
    return new Promise<void>((res,rej)=>{const t=d.transaction(CACHE_STORE,mode);fn(t.objectStore(CACHE_STORE));t.oncomplete=()=>res();t.onerror=()=>rej(t.error);t.onabort=()=>rej(t.error);});};
  return {
    async readAll(){
      let keys:IDBValidKey[]=[],values:unknown[]=[];
      try{await tx('readonly',st=>{const r=st.getAll(),k=st.getAllKeys();r.onsuccess=()=>{values=r.result;};k.onsuccess=()=>{keys=k.result;};});}
      catch{return {};}
      const out:Record<string,string>={};keys.forEach((k,i)=>{out[String(k)]=String(values[i]);});return out;
    },
    async write(records){
      try{await tx('readwrite',st=>{for(const [k,v] of Object.entries(records))st.put(v,k);});}
      catch{/* cache only: a failed write is not an error for the user */}
    },
    async clear(){try{await tx('readwrite',st=>{st.clear();});}catch{/* ignore */}},
  };
}

export function defaultCache():ReadCache{
  let idb:IDBFactory|undefined;try{idb=typeof indexedDB!=='undefined'?indexedDB:undefined;}catch{/* blocked */}
  return idb?createIdbCache(idb):createMemoryCache();
}

// ── Keys and sealing ──
export const cacheCryptoAvailable=()=>!!subtle();
const wrapPurpose=(userId:number)=>`hcc-cache-v2|key|${userId}`;
const recordPurpose=(userId:number,name:string)=>`hcc-cache-v2|${userId}|${name}`;

export function parseLock(raw:Record<string,string>):CacheLock|null{
  const v=raw[LOCK_RECORD];if(!v)return null;
  try{const l=JSON.parse(v) as CacheLock;
    return l&&l.format===CACHE_FORMAT&&typeof l.userId==='number'&&typeof l.bySecret==='string'&&typeof l.byResume==='string'&&typeof l.salt==='string'?l:null;}
  catch{return null;}
}
/** Idle (no answered server request for idleTimeoutSeconds) or absolute lifetime passed. */
export function lockExpired(l:CacheLock,t:number):boolean{
  const abs=Date.parse(l.absoluteExpiresUtc);
  return (Number.isFinite(abs)&&t>=abs)||(l.idleTimeoutSeconds>0&&t-l.lastActivity>=l.idleTimeoutSeconds*1000);
}
export type CacheKey={key:CryptoKey;lock:CacheLock;/** base64 resume key for the keyStore */resume:string};
/** New data key. `secret` '' = resume-only (no offline re-authentication). Null without WebCrypto (no cache is then written). */
export async function createCacheKey(o:{userId:number;displayName:string;credentialKind:'password'|'pin';secret:string;
  absoluteExpiresUtc:string;idleTimeoutSeconds:number;now:number;iterations?:number}):Promise<CacheKey|null>{
  if(!subtle())return null;
  const raw=randomBytes(32),salt=randomBytes(16),resume=randomBytes(32),iterations=o.iterations??CACHE_KDF_ITERATIONS;
  const p=wrapPurpose(o.userId),rawB64=toB64(raw);
  const bySecret=o.secret?await sealText(await pbkdf2Key(o.secret,salt,iterations),rawB64,p):'';
  const byResume=await sealText(await importAesKey(resume),rawB64,p);
  const lock:CacheLock={format:CACHE_FORMAT,userId:o.userId,displayName:o.displayName,credentialKind:o.credentialKind,
    absoluteExpiresUtc:o.absoluteExpiresUtc,idleTimeoutSeconds:o.idleTimeoutSeconds,lastActivity:o.now,
    salt:toB64(salt),iterations,bySecret,byResume,failures:0};
  return {key:await importAesKey(raw),lock,resume:toB64(resume)};
}
type Unwrapped={key:CryptoKey;raw:Uint8Array};
async function unwrap(l:CacheLock,kek:CryptoKey,sealed:string):Promise<Unwrapped|null>{
  const b=await openText(kek,sealed,wrapPurpose(l.userId));if(!b)return null;
  try{const raw=fromB64(b);return raw.length===32?{key:await importAesKey(raw),raw}:null;}catch{return null;}
}
export async function keyFromResume(l:CacheLock,resume:string):Promise<Unwrapped|null>{
  if(!subtle()||!resume)return null;
  try{return await unwrap(l,await importAesKey(fromB64(resume)),l.byResume);}catch{return null;}
}
export async function keyFromSecret(l:CacheLock,secret:string):Promise<Unwrapped|null>{
  if(!subtle()||!secret||!l.bySecret)return null;
  try{return await unwrap(l,await pbkdf2Key(secret,fromB64(l.salt),l.iterations),l.bySecret);}catch{return null;}
}
/** After an offline unlock: a fresh resume key for the rest of this session. */
export async function rewrapResume(l:CacheLock,raw:Uint8Array):Promise<{lock:CacheLock;resume:string}>{
  const resume=randomBytes(32);
  return {lock:{...l,byResume:await sealText(await importAesKey(resume),toB64(raw),wrapPurpose(l.userId))},resume:toB64(resume)};
}
export const sealRecord=async(key:CryptoKey,userId:number,name:string,value:unknown)=>sealText(key,JSON.stringify(value),recordPurpose(userId,name));
export async function openRecord<T>(key:CryptoKey,userId:number,name:string,sealed:unknown):Promise<T|undefined>{
  const t=await openText(key,sealed,recordPurpose(userId,name));if(t===null)return undefined;
  try{return JSON.parse(t) as T;}catch{return undefined;}
}

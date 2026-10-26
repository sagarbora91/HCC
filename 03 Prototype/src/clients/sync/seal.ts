// Client-side sealing helpers (security review 2026-10-26: F-H01 read cache, F-M07 token storage, F-L09 staff roster).
// WebCrypto AES-256-GCM only (no npm package). Every sealed value is bound to a purpose string (AAD), so a record cannot be
// moved to another key name or user. Without WebCrypto (an insecure http origin) the callers store nothing sensitive in clear:
// the read cache is switched off, the staff roster is not kept, and token stores fall back to their unsealed form (documented).
const SEALED='hccs1.';

export function subtle():SubtleCrypto|undefined{try{return globalThis.crypto?.subtle;}catch{return undefined;}}
export const randomBytes=(n:number)=>globalThis.crypto.getRandomValues(new Uint8Array(n));
export function toB64(u:Uint8Array):string{let s='';for(let i=0;i<u.length;i+=0x8000)s+=String.fromCharCode(...u.subarray(i,i+0x8000));return btoa(s);}
export function fromB64(s:string):Uint8Array{const b=atob(s);const u=new Uint8Array(b.length);for(let i=0;i<b.length;i++)u[i]=b.charCodeAt(i);return u;}
const enc=new TextEncoder(),dec=new TextDecoder();
const buf=(u:Uint8Array)=>u as unknown as BufferSource;

/** AES-256-GCM key from 32 raw bytes; non-extractable unless asked. */
export function importAesKey(raw:Uint8Array,extractable=false):Promise<CryptoKey>{
  return subtle()!.importKey('raw',buf(raw),{name:'AES-GCM'},extractable,['encrypt','decrypt']);
}
/** PBKDF2-HMAC-SHA256 → AES-256-GCM key (the sign-in secret never leaves this function). */
export async function pbkdf2Key(secret:string,salt:Uint8Array,iterations:number):Promise<CryptoKey>{
  const s=subtle()!;const base=await s.importKey('raw',buf(enc.encode(secret)),'PBKDF2',false,['deriveKey']);
  return s.deriveKey({name:'PBKDF2',hash:'SHA-256',salt:buf(salt),iterations},base,{name:'AES-GCM',length:256},false,['encrypt','decrypt']);
}
/** HKDF-SHA256 → AES-256-GCM key from a high-entropy secret (e.g. the device token). */
export async function hkdfKey(secret:string,info:string):Promise<CryptoKey>{
  const s=subtle()!;const base=await s.importKey('raw',buf(enc.encode(secret)),'HKDF',false,['deriveKey']);
  return s.deriveKey({name:'HKDF',hash:'SHA-256',salt:buf(enc.encode('hcc-hkdf-v1')),info:buf(enc.encode(info))},base,{name:'AES-GCM',length:256},false,['encrypt','decrypt']);
}
export const isSealed=(v:unknown):v is string=>typeof v==='string'&&v.startsWith(SEALED);
/** "hccs1.<iv>.<ciphertext+tag>" (base64), AAD = purpose. */
export async function sealText(key:CryptoKey,text:string,purpose:string):Promise<string>{
  const iv=randomBytes(12);
  const ct=new Uint8Array(await subtle()!.encrypt({name:'AES-GCM',iv:buf(iv),additionalData:buf(enc.encode(purpose))},key,buf(enc.encode(text))));
  return SEALED+toB64(iv)+'.'+toB64(ct);
}
/** The plaintext, or null (wrong key, wrong purpose, tampered or not sealed). Never throws. */
export async function openText(key:CryptoKey,sealed:unknown,purpose:string):Promise<string|null>{
  if(!isSealed(sealed))return null;
  const [iv,ct]=sealed.slice(SEALED.length).split('.');
  try{return dec.decode(await subtle()!.decrypt({name:'AES-GCM',iv:buf(fromB64(iv)),additionalData:buf(enc.encode(purpose))},key,buf(fromB64(ct))));}
  catch{return null;}
}

// ── Local wrapping key (F-M07) ──
// A non-extractable AES key kept in its own IndexedDB database ("hcc-keys"), used to seal values written to Capacitor
// Preferences / Web Storage. Script running in the app can still use it, but the stored values are no longer readable
// as plain text from the SharedPreferences / Web Storage files. Android Keystore-backed storage needs a native plugin
// (not added: no new packages) — see 04 Android/README.md "Production release APK".
const KEY_DB='hcc-keys',KEY_STORE='keys',KEY_ID='local-wrap-v1';
let localKey:Promise<CryptoKey|null>|undefined;
export function localWrapKey(idb:IDBFactory|undefined=(()=>{try{return globalThis.indexedDB;}catch{return undefined;}})()):Promise<CryptoKey|null>{
  return localKey??=(async()=>{
    const s=subtle();if(!s||!idb)return null;
    try{
      const db=await new Promise<IDBDatabase>((res,rej)=>{const r=idb.open(KEY_DB,1);r.onupgradeneeded=()=>{r.result.createObjectStore(KEY_STORE);};r.onsuccess=()=>res(r.result);r.onerror=()=>rej(r.error);r.onblocked=()=>rej(new Error('blocked'));});
      const get=()=>new Promise<CryptoKey|undefined>((res,rej)=>{const t=db.transaction(KEY_STORE,'readonly');const q=t.objectStore(KEY_STORE).get(KEY_ID);q.onsuccess=()=>res(q.result as CryptoKey|undefined);q.onerror=()=>rej(q.error);});
      const have=await get();if(have)return have;
      const k=await s.generateKey({name:'AES-GCM',length:256},false,['encrypt','decrypt']);
      await new Promise<void>((res,rej)=>{const t=db.transaction(KEY_STORE,'readwrite');t.objectStore(KEY_STORE).put(k,KEY_ID);t.oncomplete=()=>res();t.onerror=()=>rej(t.error);});
      return k;
    }catch{return null;}
  })();
}
/** Test hook. */
export const resetLocalWrapKey=()=>{localKey=undefined;};

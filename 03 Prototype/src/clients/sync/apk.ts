// APK ↔ hotel server (W4-12): native HTTP, server address setup, device registration (DEC-062). Framework-free (tests drive
// it with a fake fetch); the screens are in react.tsx. Production build only (reached from sync/instance.ts and react.tsx).
//
// • Native HTTP: the web view runs at https://localhost and the server has no CORS, so the APK sends its requests through
//   Capacitor's built-in CapacitorHttp plugin (native HttpURLConnection: no CORS, and it uses the app's network security
//   config = the hotel CA bundled at build time, 04 Android/README.md). In a browser the normal fetch is used.
// • Server address: `hcc-server-url` (origin only, https only), checked with GET /system/discovery + /system/health first.
// • Device: an Owner / Tech admin (PERM-ADM-001) signs in with a password on the tablet and registers it (RegisterDevice);
//   the device token is shown once and kept by auth.ts (production: sealed Capacitor Preferences; sent as X-HCC-Device). The
//   admin's session also reads the staff list once (GET /admin/users) so staff can pick their name and type their PIN here.
// • Staff roster (security review F-L09): only the display name and an opaque id are kept (localStorage `hcc-device-staff`).
//   The opaque id is the login name sealed with a key derived from the device token (HKDF), so the stored list names no
//   login; it becomes unreadable — and is deleted — when the registration is forgotten or revoked.
import {createHttpTransport,type FetchLike,type HttpTransport} from '../http';
import {HccClientError,errorText} from '../errors';
import {deviceToken,setDeviceToken} from './auth';
import {subtle,hkdfKey,sealText,openText} from './seal';
import {fitKey} from './runtime';

export const SERVER_URL_KEY='hcc-server-url';
export const DEVICE_ID_KEY='hcc-device-id';
export const DEVICE_LABEL_KEY='hcc-device-label';
export const DEVICE_STAFF_KEY='hcc-device-staff';
export const HCC_PRODUCT='Hotel Control Centre';

type Kv=Pick<Storage,'getItem'|'setItem'|'removeItem'>;
const ls=():Kv|undefined=>{try{return globalThis.localStorage;}catch{return undefined;}};
const get=(k:string,s=ls())=>{try{return s?.getItem(k)??'';}catch{return '';}};
const put=(k:string,v:string,s=ls())=>{try{s?.setItem(k,v);}catch{/* storage unavailable */}};
const del=(k:string,s=ls())=>{try{s?.removeItem(k);}catch{/* ignore */}};

// ── Native HTTP (CapacitorHttp) ──
export type CapacitorHttpLike={request(o:{url:string;method:string;headers:Record<string,string>;data?:string;responseType?:string;connectTimeout?:number;readTimeout?:number}):Promise<{status:number;data:unknown;headers?:Record<string,string>}>};
/** FetchLike over CapacitorHttp.request. JSON bodies go as the raw string; JSON answers (parsed by the plugin) are re-serialised. */
export function capacitorFetch(http:CapacitorHttpLike,timeoutMs=15000):FetchLike{
  return async(url,init={})=>{
    const call=http.request({url,method:init.method??'GET',headers:init.headers??{},data:init.body,responseType:'text',connectTimeout:timeoutMs,readTimeout:timeoutMs});
    const signal=init.signal;
    const r=await (signal?Promise.race([call,new Promise<never>((_,rej)=>{if(signal.aborted)rej(new Error('aborted'));signal.addEventListener?.('abort',()=>rej(new Error('aborted')));})]):call);
    const text=r.data==null?'':typeof r.data==='string'?r.data:JSON.stringify(r.data);
    // Response headers (Retry-After, Content-Disposition) are looked up case-insensitively; the plugin returns a plain object.
    const hs=r.headers&&typeof r.headers==='object'?r.headers:{};
    const headers={get:(n:string)=>{const k=Object.keys(hs).find(x=>x.toLowerCase()===n.toLowerCase());return k?String(hs[k]):null;}};
    return {status:r.status,ok:r.status>=200&&r.status<300,text:async()=>text,headers};
  };
}

// ── Server address ──
export const readServerUrl=(s?:Kv)=>get(SERVER_URL_KEY,s??ls());
export const saveServerUrl=(url:string,s?:Kv)=>put(SERVER_URL_KEY,url,s??ls());
/** Origin only ("https://hcc-server.local:8443"): adds https:// when no scheme is typed, drops a trailing "/" or "/api/v1"; http is refused. */
export function normaliseServerUrl(raw:string):{ok:true;url:string}|{ok:false;text:string}{
  let v=String(raw??'').trim();
  if(!v)return {ok:false,text:'Enter the server address, for example https://hcc-server.local:8443.'};
  if(!/^[a-z][a-z0-9+.-]*:\/\//i.test(v))v='https://'+v;
  let u:URL;try{u=new URL(v);}catch{return {ok:false,text:'That is not a valid address.'};}
  if(u.protocol!=='https:')return {ok:false,text:'Use an https:// address. This app never talks to the hotel server over plain http.'};
  if(u.username||u.password||u.search||u.hash)return {ok:false,text:'Enter only the server address (no user name, ? or #).'};
  const path=u.pathname.replace(/\/+$/,'');
  if(path&&path!=='/api/v1')return {ok:false,text:'Enter only the server address, without a path.'};
  return {ok:true,url:u.origin};
}
export type ProbeResult={ok:true;url:string;serverName:string;version:string;database?:string}|{ok:false;text:string};
/** Checks an address before it is saved: GET /system/discovery must say "Hotel Control Centre", then GET /system/health. */
export async function probeServer(raw:string,fetch:FetchLike,timeoutMs=8000):Promise<ProbeResult>{
  const n=normaliseServerUrl(raw);if(!n.ok)return n;
  const t=createHttpTransport({baseUrl:n.url,fetch,commandRetries:0,timeoutMs});
  try{
    const d=await t.get<{product?:string;serverName?:string;version?:string}>('/system/discovery');
    if(d?.product!==HCC_PRODUCT)return {ok:false,text:`${n.url} answers, but it is not a Hotel Control Centre server.`};
    const h=await t.get<{status?:string;database?:string|null}>('/system/health');
    if(h?.status!=='ok')return {ok:false,text:`The server at ${n.url} answers, but it is not ready (status: ${h?.status??'unknown'}).`};
    return {ok:true,url:n.url,serverName:d.serverName??'',version:d.version??'',database:h.database??undefined};
  }catch(e){
    if(e instanceof HccClientError&&e.wireCode==='UNAVAILABLE'){
      const cause=String((e.details as {cause?:string}|undefined)?.cause??'');
      const tls=/trust|certif|ssl|handshake|hostname/i.test(cause);
      return {ok:false,text:tls?`The server at ${n.url} was found, but its certificate is not trusted by this app (${cause}). The app must be built with the hotel CA certificate, and the server certificate must name this address.`
        :`Cannot reach ${n.url}. Check the address and port, that this device is on the hotel Wi-Fi, and that the server PC is on.${cause?` (${cause})`:''}`};
    }
    if(e instanceof HccClientError&&e.wireCode==='NOT_FOUND')return {ok:false,text:`${n.url} answers, but it is not a Hotel Control Centre server.`};
    return {ok:false,text:errorText(e)};
  }
}

// ── Device ──
/** Generated once per installation (kept across sign-ins and server changes). */
export function deviceId(s?:Kv):string{
  const st=s??ls();let id=get(DEVICE_ID_KEY,st);
  if(!id){id=(globalThis.crypto?.randomUUID?.()??`${Date.now().toString(16)}-${Math.random().toString(16).slice(2,14)}`).toLowerCase();put(DEVICE_ID_KEY,id,st);}
  return id;
}
export const deviceRegistered=()=>!!deviceToken();
export const deviceLabel=(s?:Kv)=>get(DEVICE_LABEL_KEY,s??ls());
/** Stored roster entry: display name + opaque id (sealed login name). No login name in clear. */
export type DeviceStaff={id:string;displayName:string};
/** A PIN user as read from the server or after a sign-in (login name only in memory). */
export type StaffLogin={loginName:string;displayName:string};
const ROSTER_PURPOSE='hcc-roster-v1';
const rosterKey=async():Promise<CryptoKey|null>=>{const t=deviceToken();if(!t||!subtle())return null;try{return await hkdfKey(t,ROSTER_PURPOSE);}catch{return null;}};
export function deviceStaff(s?:Kv):DeviceStaff[]{
  try{const v=JSON.parse(get(DEVICE_STAFF_KEY,s??ls())||'[]');
    return Array.isArray(v)?v.filter(x=>x&&typeof x.id==='string'&&x.id&&typeof x.displayName==='string').map(x=>({id:x.id,displayName:x.displayName})):[];}catch{return [];}
}
/** The login name behind a roster id (null: not this device's roster, or the registration was forgotten). */
export async function resolveStaffLogin(id:string):Promise<string|null>{const k=await rosterKey();return k?openText(k,id,ROSTER_PURPOSE):null;}
async function saveStaff(list:StaffLogin[],s?:Kv):Promise<number>{
  const st=s??ls();const k=await rosterKey();
  if(!k){del(DEVICE_STAFF_KEY,st);return 0;} // no device key: keep no roster (staff type their login name)
  const uniq=[...new Map(list.map(x=>[x.loginName.toLowerCase(),x])).values()].sort((a,b)=>a.displayName.localeCompare(b.displayName));
  const out:DeviceStaff[]=[];for(const x of uniq)out.push({id:await sealText(k,x.loginName,ROSTER_PURPOSE),displayName:x.displayName||'Staff member'});
  put(DEVICE_STAFF_KEY,JSON.stringify(out),st);return out.length;
}
async function staffLogins(s?:Kv):Promise<StaffLogin[]>{
  const out:StaffLogin[]=[];for(const x of deviceStaff(s)){const loginName=await resolveStaffLogin(x.id);if(loginName)out.push({loginName,displayName:x.displayName});}
  return out;
}
/** Adds a staff member who signed in with a PIN here (so the name appears in the list next time). */
export async function rememberStaff(x:StaffLogin,s?:Kv):Promise<void>{
  const list=await staffLogins(s);
  if(!list.some(y=>y.loginName.toLowerCase()===x.loginName.toLowerCase()))await saveStaff([...list,x],s);
}
/** Keeps a new registration on this device: the device token (shown once by RegisterDevice) and its server label. */
export async function adoptDeviceRegistration(token:string,label:string,s?:Kv){const st=s??ls();del(DEVICE_STAFF_KEY,st);await setDeviceToken(token);put(DEVICE_LABEL_KEY,label,st);}
/** Drops the registration on this device (the server keeps the device until it is revoked in Admin › Devices), with the roster. */
export async function forgetDevice(s?:Kv){const st=s??ls();del(DEVICE_LABEL_KEY,st);del(DEVICE_STAFF_KEY,st);await setDeviceToken(null);}

type SrvUser={loginName:string;displayName:string;enabled:boolean;credentialKind:string};
type SrvPage<T>={items:T[];page:number;pageSize:number;total:number};
/** Staff who sign in by PIN (enabled users with a PIN credential), read with the admin's session. Returns how many. */
export async function refreshDeviceStaff(t:HttpTransport,s?:Kv):Promise<number>{
  const all:SrvUser[]=[];
  for(let page=1;page<=50;page++){
    const r=await t.get<SrvPage<SrvUser>>('/admin/users',{page,pageSize:100});
    all.push(...(r?.items??[]));
    if(!r?.items?.length||all.length>=r.total)break;
  }
  const pin=all.filter(u=>u.enabled&&u.credentialKind==='PIN').map(u=>({loginName:u.loginName,displayName:u.displayName||u.loginName}));
  return saveStaff(pin,s);
}
const LABEL_MAX=60;
/** Server label: "<name> · <first 8 of the device id>" (≤ 60 characters). */
export const deviceServerLabel=(name:string,id:string)=>{const tag=' · '+id.slice(0,8);return name.trim().slice(0,LABEL_MAX-tag.length)+tag;};
export type RegisterResult={ok:true;deviceId:number;label:string;staff:number}|{ok:false;text:string};
/** RegisterDevice with the signed-in admin's session; stores the device token (shown once by the server) and the staff list. */
export async function registerThisDevice(t:HttpTransport,name:string,s?:Kv):Promise<RegisterResult>{
  const st=s??ls();
  if(!name.trim())return {ok:false,text:'Give this device a name, for example "Front desk tablet".'};
  const label=deviceServerLabel(name,deviceId(st));
  let r:{deviceId:number;deviceToken?:string|null};
  try{r=await t.command('RegisterDevice',{label},{idempotencyKey:fitKey(`RegisterDevice:${deviceId(st)}:${Date.now().toString(36)}`)});}
  catch(e){return {ok:false,text:errorText(e)};}
  if(!r?.deviceToken)return {ok:false,text:`The server registered device #${r?.deviceId??'?'} but did not return its key (repeated request). Revoke it in Admin › Devices and register again.`};
  await adoptDeviceRegistration(r.deviceToken,label,st);
  let staff=0;try{staff=await refreshDeviceStaff(t,st);}catch{/* the list can be updated later from Settings */}
  return {ok:true,deviceId:r.deviceId,label,staff};
}

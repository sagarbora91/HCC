// The production app's single sync runtime (W4-0). Only reached from `process.env.HCC_BUILD==='production'` branches, so the
// prototype bundle drops it (and with it the registry, every area and the HTTP adapter).
// W4-12: inside the APK (Capacitor native platform) the runtime uses the token adapter (Bearer, Preferences / sessionStorage)
// and native HTTP (CapacitorHttp: no CORS, hotel CA trust from the APK's network security config). A browser keeps the cookie.
// Security review 2026-10-26 (F-M07, F-H01): the session token, the device token and the read cache's resume key are sealed
// (AES-GCM, non-extractable local wrapping key in IndexedDB "hcc-keys") before they reach Capacitor Preferences / Web Storage.
import {createSyncRuntime,type SyncRuntime} from './runtime';
import {AREAS} from './registry';
import {defaultCache} from './cache';
import {cookieAuth,tokenAuth,preferencesTokenStore,sessionTokenStore,sealedTokenStore,useDeviceTokenStore,type AuthAdapter,type TokenAuth,type TokenStore} from './auth';
import {localWrapKey} from './seal';
import {SERVER_URL_KEY,capacitorFetch,type CapacitorHttpLike} from './apk';
import type {FetchLike} from '../http';
import {isNative,nativePlugin,nativePluginAvailable} from '../../app/native';

/** Server origin: '' = same origin (the API host serves the web app). The APK sets `hcc-server-url` (W4-12 setup screen). */
export {SERVER_URL_KEY};
export function serverBaseUrl(){try{return globalThis.localStorage?.getItem(SERVER_URL_KEY)||'';}catch{return '';}}
/** The APK needs a server address before it can boot (a browser falls back to its own origin). */
export const needsServerSetup=()=>isNative()&&!serverBaseUrl();

/** Storage keys of the sealed device token and of the read cache's resume key. */
export const DEVICE_TOKEN_STORE_KEY='hcc-device-token-sealed',CACHE_RESUME_KEY='hcc-cache-resume';
const wrapKey=()=>localWrapKey();
const prefsPlugin=()=>isNative()&&nativePluginAvailable('Preferences')?nativePlugin('Preferences') as never:undefined;
const webStorage=(k:'localStorage'|'sessionStorage')=>{try{return globalThis[k];}catch{return undefined;}};
let auth:AuthAdapter|undefined;
export function getAuth():AuthAdapter{
  if(auth)return auth;
  if(!isNative())return auth=cookieAuth();
  // Preferences keeps the session across app restarts; without the plugin the token lives in sessionStorage (gone when Android closes the app).
  const prefs=prefsPlugin();
  return auth=tokenAuth({store:prefs?sealedTokenStore(preferencesTokenStore(prefs),wrapKey,'session-token'):sealedTokenStore(sessionTokenStore(),wrapKey,'session-token')});
}
/** Device token: sealed Preferences in the APK, sealed localStorage in a browser. */
const deviceStore=():TokenStore=>{const prefs=prefsPlugin();
  return sealedTokenStore(prefs?preferencesTokenStore(prefs,DEVICE_TOKEN_STORE_KEY):sessionTokenStore(webStorage('localStorage'),DEVICE_TOKEN_STORE_KEY),wrapKey,'device-token');};
/** Resume key of the read cache (F-H01): kept with the session — sealed Preferences while the APK's token lives there, otherwise
 *  sessionStorage (a browser tab / an APK without Preferences re-authenticates after it is closed). Never in the cache database. */
const cacheKeyStore=():TokenStore=>{const a=getAuth();const prefs=tokenMode(a)&&a.storeKind==='preferences'?prefsPlugin():undefined;
  return sealedTokenStore(prefs?preferencesTokenStore(prefs,CACHE_RESUME_KEY):sessionTokenStore(webStorage('sessionStorage'),CACHE_RESUME_KEY),wrapKey,'cache-resume');};
export const tokenMode=(a:AuthAdapter=getAuth()):a is TokenAuth=>a.kind==='token';
/** Native HTTP in the APK (CapacitorHttp is part of the Capacitor bridge itself); undefined = the web view's fetch. */
export function nativeFetch():FetchLike|undefined{
  const http=nativePluginAvailable('CapacitorHttp')?nativePlugin('CapacitorHttp'):undefined;
  return http?capacitorFetch(http as unknown as CapacitorHttpLike):undefined;
}
/** Resolves once the stored session token (APK) and the device token have been read — the runtime must not boot before. */
let ready:Promise<void>|undefined;
export const authReady=():Promise<void>=>ready??=(async()=>{const a=getAuth();await useDeviceTokenStore(deviceStore());if(tokenMode(a))await a.ready();})();

let rt:SyncRuntime|undefined;
export function getSyncRuntime():SyncRuntime{
  if(rt)return rt;
  const a=getAuth();
  const base:FetchLike|undefined=tokenMode(a)?(nativeFetch()??((u,i)=>(globalThis as unknown as {fetch:FetchLike}).fetch(u,i))):undefined;
  return rt=createSyncRuntime({areas:AREAS,baseUrl:serverBaseUrl(),cache:defaultCache(),keyStore:cacheKeyStore(),auth:a,fetch:base&&tokenMode(a)?a.observe(base):undefined});
}
/** For the server-address screen (before the runtime exists): native HTTP in the APK, else fetch. */
export const probeFetch=():FetchLike=>nativeFetch()??((u,i)=>(globalThis as unknown as {fetch:FetchLike}).fetch(u,i));

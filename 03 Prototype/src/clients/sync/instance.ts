// The production app's single sync runtime (W4-0). Only reached from `process.env.HCC_BUILD==='production'` branches, so the
// prototype bundle drops it (and with it the registry, every area and the HTTP adapter).
// W4-12: inside the APK (Capacitor native platform) the runtime uses the token adapter (Bearer, Preferences / sessionStorage)
// and native HTTP (CapacitorHttp: no CORS, hotel CA trust from the APK's network security config). A browser keeps the cookie.
import {createSyncRuntime,type SyncRuntime} from './runtime';
import {AREAS} from './registry';
import {defaultCache} from './cache';
import {cookieAuth,tokenAuth,preferencesTokenStore,sessionTokenStore,type AuthAdapter,type TokenAuth} from './auth';
import {SERVER_URL_KEY,capacitorFetch,type CapacitorHttpLike} from './apk';
import type {FetchLike} from '../http';
import {isNative,nativePlugin,nativePluginAvailable} from '../../app/native';

/** Server origin: '' = same origin (the API host serves the web app). The APK sets `hcc-server-url` (W4-12 setup screen). */
export {SERVER_URL_KEY};
export function serverBaseUrl(){try{return globalThis.localStorage?.getItem(SERVER_URL_KEY)||'';}catch{return '';}}
/** The APK needs a server address before it can boot (a browser falls back to its own origin). */
export const needsServerSetup=()=>isNative()&&!serverBaseUrl();

let auth:AuthAdapter|undefined;
export function getAuth():AuthAdapter{
  if(auth)return auth;
  if(!isNative())return auth=cookieAuth();
  // Preferences keeps the session across app restarts; without the plugin the token lives in sessionStorage (gone when Android closes the app).
  const prefs=nativePluginAvailable('Preferences')?nativePlugin('Preferences'):undefined;
  return auth=tokenAuth({store:prefs?preferencesTokenStore(prefs as never):sessionTokenStore()});
}
export const tokenMode=(a:AuthAdapter=getAuth()):a is TokenAuth=>a.kind==='token';
/** Native HTTP in the APK (CapacitorHttp is part of the Capacitor bridge itself); undefined = the web view's fetch. */
export function nativeFetch():FetchLike|undefined{
  const http=nativePluginAvailable('CapacitorHttp')?nativePlugin('CapacitorHttp'):undefined;
  return http?capacitorFetch(http as unknown as CapacitorHttpLike):undefined;
}
/** Resolves once the stored session token (APK) has been read — the runtime must not boot before. */
export const authReady=():Promise<void>=>{const a=getAuth();return tokenMode(a)?a.ready():Promise.resolve();};

let rt:SyncRuntime|undefined;
export function getSyncRuntime():SyncRuntime{
  if(rt)return rt;
  const a=getAuth();
  const base:FetchLike|undefined=tokenMode(a)?(nativeFetch()??((u,i)=>(globalThis as unknown as {fetch:FetchLike}).fetch(u,i))):undefined;
  return rt=createSyncRuntime({areas:AREAS,baseUrl:serverBaseUrl(),cache:defaultCache(),auth:a,fetch:base&&tokenMode(a)?a.observe(base):undefined});
}
/** For the server-address screen (before the runtime exists): native HTTP in the APK, else fetch. */
export const probeFetch=():FetchLike=>nativeFetch()??((u,i)=>(globalThis as unknown as {fetch:FetchLike}).fetch(u,i));

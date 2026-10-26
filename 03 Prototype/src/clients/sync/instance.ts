// The production app's single sync runtime (W4-0). Only reached from `process.env.HCC_BUILD==='production'` branches, so the
// prototype bundle drops it (and with it the registry, every area and the HTTP adapter).
import {createSyncRuntime,type SyncRuntime} from './runtime';
import {AREAS} from './registry';
import {defaultCache} from './cache';
import {cookieAuth} from './auth';

/** Server origin: '' = same origin (the API host serves the web app). The APK sets `hcc-server-url` (W4-12 setup screen). */
export const SERVER_URL_KEY='hcc-server-url';
export function serverBaseUrl(){try{return globalThis.localStorage?.getItem(SERVER_URL_KEY)||'';}catch{return '';}}

let rt:SyncRuntime|undefined;
export function getSyncRuntime():SyncRuntime{
  return rt??=createSyncRuntime({areas:AREAS,baseUrl:serverBaseUrl(),cache:defaultCache(),auth:cookieAuth()});
}

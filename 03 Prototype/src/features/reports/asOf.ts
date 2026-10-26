// "as of dd/mm hh:mm" label for server data (shared by prototype-safe screens; no sync-layer imports besides the pure clock mapper).
import {utcToClock} from '../../clients/sync/map';
export const asOfText=(iso?:string)=>{const c=utcToClock(iso);return c?`${c.slice(8,10)}/${c.slice(5,7)} ${c.slice(11,16)}`:'—';};
/** Idle result used as the non-production branch of a server-fetch hook (kept here so prototype screens import nothing from the sync layer). */
export const NO_FETCH:{status:'idle';data?:undefined;error?:undefined}={status:'idle'};

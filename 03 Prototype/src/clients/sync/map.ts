// Mapping helpers for area loaders and command mappers (W4-0). Conventions (W4 brief "Mapping rules"):
//   • ids: prototype id = String(server numeric id) — `protoId(1201)` → "1201", `serverId("1201")` → 1201.
//     Exceptions kept from Wave 1: rooms use the room number ("105") and room types the type code ("Deluxe"); learn those
//     pairs in the IdDirectory (kind 'room' / 'roomType') and look them up with ids.server(kind, protoId).
//   • money: integer paise both ways, unchanged (the transport already refuses unsafe / over-₹10-crore values).
//   • dates: YYYY-MM-DD both ways. Server event times are ISO-8601 UTC; the prototype keeps hotel-local clock strings
//     "YYYY-MM-DDTHH:mm" (Asia/Kolkata, +05:30, no DST) — convert with utcToClock / clockToUtc.
import {HccClientError} from '../errors';
import {isIsoDate,isPaise} from '../wire';

export const protoId=(id:number|string)=>String(id);
/** Prototype id → server numeric id. Throws VALIDATION (before any network call) for an id the server cannot have issued. */
export function serverId(id:string|number|undefined|null,what='Record'):number{
  const n=typeof id==='number'?id:Number(String(id??'').trim());
  if(!Number.isSafeInteger(n)||n<=0)throw new HccClientError('VALIDATION',`${what} ${id??''} is not a server record. Refresh and try again.`.replace('  ',' '));
  return n;
}
/** Prototype user id for a server user id (same rule as every other id; used by the capabilities overlay). */
export const protoUserId=(userId:number)=>protoId(userId);

/** Response money field → paise (PROTOCOL error when the server sent something that is not safe integer paise). */
export function paise(v:unknown,field='amount'):number{
  if(!isPaise(v))throw new HccClientError('PROTOCOL',`${field} is not a safe paise amount.`,{field});return v;
}
/** Response date field → YYYY-MM-DD (PROTOCOL error otherwise). */
export function isoDate(v:unknown,field='date'):string{
  if(!isIsoDate(v))throw new HccClientError('PROTOCOL',`${field} must be a date in YYYY-MM-DD.`,{field});return v;
}

const IST_MS=330*60_000;
/** ISO-8601 UTC instant → hotel clock "YYYY-MM-DDTHH:mm" (Asia/Kolkata). '' for a missing/invalid value. */
export function utcToClock(iso:string|null|undefined):string{
  const t=iso?Date.parse(iso):NaN;if(!Number.isFinite(t))return '';
  return new Date(t+IST_MS).toISOString().slice(0,16);
}
/** Hotel clock "YYYY-MM-DDTHH:mm" → ISO-8601 UTC. */
export function clockToUtc(clock:string):string{
  const t=Date.parse(clock+':00Z');if(!Number.isFinite(t))throw new HccClientError('VALIDATION',`${clock} is not a time.`);
  return new Date(t-IST_MS).toISOString();
}
/** Hotel-local calendar date of a UTC instant. */
export const utcToDate=(iso:string|null|undefined)=>utcToClock(iso).slice(0,10);

export type IdSnapshot={ids:Record<string,Record<string,number>>;versions:Record<string,Record<string,string>>};
export interface IdDirectory{
  /** Remember protoId ↔ serverId for a kind ('room', 'roomType', 'booking', …). */
  learn(kind:string,protoId:string,serverId:number):void;
  server(kind:string,protoId:string):number|undefined;
  proto(kind:string,serverId:number):string|undefined;
  /** Prototype ids known for a kind (e.g. bookings created on this device before a list endpoint exists). */
  list(kind:string):string[];
  /** Server record version (opaque base64 rowversion) as last loaded — used for expectedVersion. */
  setVersion(kind:string,protoId:string,version:string):void;
  version(kind:string,protoId:string):string|undefined;
  snapshot():IdSnapshot;
}
export function createIdDirectory(from?:IdSnapshot|null):IdDirectory{
  const ids:IdSnapshot['ids']=structuredCopy(from?.ids),versions:IdSnapshot['versions']=structuredCopy(from?.versions);
  const k=<T,>(m:Record<string,Record<string,T>>,kind:string)=>m[kind]??=({} as Record<string,T>);
  return {
    learn(kind,p,s){k(ids,kind)[p]=s;},
    server:(kind,p)=>ids[kind]?.[p],
    proto(kind,s){const m=ids[kind];if(!m)return undefined;for(const [p,v] of Object.entries(m))if(v===s)return p;return undefined;},
    list:kind=>Object.keys(ids[kind]??{}),
    setVersion(kind,p,v){k(versions,kind)[p]=v;},
    version:(kind,p)=>versions[kind]?.[p],
    snapshot:()=>({ids:structuredCopy(ids),versions:structuredCopy(versions)}),
  };
}
function structuredCopy<T>(x:Record<string,Record<string,T>>|undefined):Record<string,Record<string,T>>{
  const out:Record<string,Record<string,T>>={};for(const [a,m] of Object.entries(x??{}))out[a]={...m};return out;
}

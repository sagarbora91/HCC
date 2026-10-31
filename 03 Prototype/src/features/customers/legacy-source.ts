// LG-UI · where Customers 360 gets the legacy (old Lodge app) stays of a customer.
// Prototype: the mock read in services/mock/handlers/customers-legacy.ts (six fictional stays, no State slice, nothing persisted).
// Production: the server's `LegacyStays` of GET /customers/profile, mapped by clients/sync/customers-legacy.ts and remembered here per
// customer key once the profile is opened (`setLegacyFetcher` is wired by the production composition; the prototype never sets it).
import {legacyProfileRead} from '../../services/mock/handlers/customers-legacy';
import type {LegacyLookup,LegacyStay} from '../../domain/streams/customers';

const remembered=new Map<string,LegacyStay[]>();
let fetcher:((key:string)=>Promise<LegacyStay[]>)|undefined;
const make=():LegacyLookup=>key=>remembered.get(key)??(fetcher?undefined:legacyProfileRead(key));
let current:LegacyLookup=make();

/** The lookup to hand to customerIndex. A NEW function after every `rememberLegacy`, so the cached index is rebuilt. */
export const getLegacyLookup=():LegacyLookup=>current;
export function rememberLegacy(key:string,stays:LegacyStay[]){remembered.set(key,stays);current=make();}
export function setLegacyFetcher(f:((key:string)=>Promise<LegacyStay[]>)|undefined){fetcher=f;current=make();}
/** Production only: loads the legacy stays of one customer (once) and tells the caller to re-read. Resolves false when nothing changed. */
export async function ensureLegacy(key:string):Promise<boolean>{
  if(!fetcher||remembered.has(key))return false;
  try{rememberLegacy(key,await fetcher(key));return true;}catch{return false;}
}

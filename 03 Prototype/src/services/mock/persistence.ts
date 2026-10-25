// Versioned browser persistence format (DEC-022). Pure functions; the browser adapter lives in app/storage.ts.
import {State,SCHEMA} from '../../domain/types';
import {loadScenario} from '../../fixtures/scenarios';
import {hrMigrateLegacy} from '../../domain/streams/staff';
import {extendBase as cmpSeed} from '../../fixtures/ext/compliance';
/** Oldest saved schema that is migrated instead of reset (W05-B review F-04). */
export const MIGRATE_FROM_SCHEMA=7;
/** One-time state migrations keyed on the saved schema; each is a no-op on current-shape state. */
function migrate(s:State&{schema:number}){
  if(s.schema<8){
    hrMigrateLegacy(s);// W05-B payroll: `rates` → `rateVersions`, old run lines filled
    if(!s.complianceX)cmpSeed(s);// W05-C review F14: saves from before the Compliance calendar get the fictional obligations
  }
  s.schema=SCHEMA;
}
export const STORAGE_KEY='hcc-prototype-v2';
export const LEGACY_KEYS=['hcc-prototype-v1'];
export type Restored={state:State;reset:boolean;reason?:string};
/** Result of reading a save without building a fallback: `state` is undefined when the fallback scenario must be loaded
 *  (nothing saved, legacy key, older format or unreadable data). `migrated` is true when a one-time migration ran. */
export type Inspected={state?:State;reset:boolean;reason?:string;migrated?:boolean};
/** `raw` is the saved JSON text (the single-blob `hcc-prototype-v2` / IndexedDB "current" format) or an already assembled
 *  object (the per-slice IndexedDB format, app/storage.ts); both go through the same checks and SCHEMA migrations. */
export function inspectSaved(raw:string|Record<string,unknown>|null,legacyPresent=false,fallback='BASE'):Inspected{
  const name=fallback==='BASE'?'base scenario':'default demo scenario';
  if(raw){
    try{
      const s=typeof raw==='string'?JSON.parse(raw):raw;
      if(typeof s?.schema==='number'&&s.schema>=MIGRATE_FROM_SCHEMA&&s.schema<=SCHEMA&&typeof s.clock==='string'&&s.settings&&s.grants&&Array.isArray(s.bookings)){
        const migrated=s.schema<SCHEMA;migrate(s);return {state:s as State,reset:false,migrated};}
      return {reset:true,reason:`Saved demo data used an older format and was reset to the ${name}.`};
    }catch{return {reset:true,reason:`Saved demo data could not be read and was reset to the ${name}.`};}
  }
  if(legacyPresent)return {reset:true,reason:`Demo data from the earlier prototype version was replaced with the new ${name}.`};
  return {reset:false};
}
/** `fallback` is the scenario loaded when nothing usable is saved. The app passes DEFAULT_SCENARIO (FY26, plan H-02);
 *  the pure default stays BASE so tests and tools get the small fixture. */
export function restoreState(raw:string|Record<string,unknown>|null,legacyPresent=false,fallback='BASE'):Restored{
  const r=inspectSaved(raw,legacyPresent,fallback);
  return {state:r.state??loadScenario(fallback),reset:r.reset,...(r.reason?{reason:r.reason}:{})};
}

// Versioned browser persistence format (DEC-022). Pure functions; the browser adapter lives in app/storage.ts.
import {State,SCHEMA} from '../../domain/types';
import {loadScenario} from '../../fixtures/scenarios';
import {hrMigrateLegacy} from '../../domain/streams/staff';
/** Oldest saved schema that is migrated instead of reset (W05-B review F-04). */
export const MIGRATE_FROM_SCHEMA=7;
/** One-time state migrations keyed on the saved schema; each is a no-op on current-shape state. */
function migrate(s:State&{schema:number}){
  if(s.schema<8)hrMigrateLegacy(s);// W05-B payroll: `rates` → `rateVersions`, old run lines filled (lead bumps SCHEMA 7 → 8 at the integration merge)
  s.schema=SCHEMA;
}
export const STORAGE_KEY='hcc-prototype-v2';
export const LEGACY_KEYS=['hcc-prototype-v1'];
export type Restored={state:State;reset:boolean;reason?:string};
/** `fallback` is the scenario loaded when nothing usable is saved. The app passes DEFAULT_SCENARIO (FY26, plan H-02);
 *  the pure default stays BASE so tests and tools get the small fixture. */
export function restoreState(raw:string|null,legacyPresent=false,fallback='BASE'):Restored{
  const name=fallback==='BASE'?'base scenario':'default demo scenario';
  if(raw){
    try{
      const s=JSON.parse(raw);
      if(typeof s?.schema==='number'&&s.schema>=MIGRATE_FROM_SCHEMA&&s.schema<=SCHEMA&&typeof s.clock==='string'&&s.settings&&s.grants&&Array.isArray(s.bookings)){migrate(s);return {state:s as State,reset:false};}
      return {state:loadScenario(fallback),reset:true,reason:`Saved demo data used an older format and was reset to the ${name}.`};
    }catch{return {state:loadScenario(fallback),reset:true,reason:`Saved demo data could not be read and was reset to the ${name}.`};}
  }
  if(legacyPresent)return {state:loadScenario(fallback),reset:true,reason:`Demo data from the earlier prototype version was replaced with the new ${name}.`};
  return {state:loadScenario(fallback),reset:false};
}

// Versioned browser persistence format (DEC-022). Pure functions; the browser adapter lives in app/storage.ts.
import {State,SCHEMA} from '../../domain/types';
import {loadScenario} from '../../fixtures/scenarios';
import {hrMigrateLegacy,hrMigrateLeaveDays} from '../../domain/streams/staff';
import {extendBase as cmpSeed} from '../../fixtures/ext/compliance';
import {mstRows,mstNextSort,type MasterRow} from '../../domain/streams/masters';
import {HRM_HOLIDAYS,HRM_LEAVE_TYPES} from '../../domain/streams/people-masters';
import {cmpIsLegacy,cmpFromLegacy} from '../../domain/streams/compliance';
/** Oldest saved schema that is migrated instead of reset (W05-B review F-04). */
export const MIGRATE_FROM_SCHEMA=7;
/** One-time state migrations keyed on the saved schema; each is a no-op on current-shape state. Returns true when a shape-detected
 *  migration (no schema bump) changed the state, so the first save writes it. */
function migrate(s:State&{schema:number}):boolean{
  let changed=false;
  if(s.schema<8){
    hrMigrateLegacy(s);// W05-B payroll: `rates` → `rateVersions`, old run lines filled
    if(!s.complianceX)cmpSeed(s);// W05-C review F14: saves from before the Compliance calendar get the fictional obligations
  }
  // 8 → 9 (masters wave MA-1…MA-7): only optional slices and fields were added; every master reads its seed while its rows are
  // absent (mstRows). One rewrite: a Wave B save kept its DEC-083 paid holidays in staffX.holidays — they become active paid rows of
  // MA-6's Holiday calendar (one holidays concept), and the legacy list is dropped.
  if(s.schema<9){
    const legacy=s.staffX?.holidays;
    if(legacy?.length){
      const rows:MasterRow[]=mstRows(s,HRM_HOLIDAYS).map(r=>({...r}));
      for(const h of legacy){
        const i=rows.findIndex(r=>r.code===h.date);
        if(i>=0){const r={...rows[i],name:h.name,kind:'paid',active:true};delete r.retiredOn;rows[i]=r;}
        else rows.push({id:'HOL-'+h.date,code:h.date,name:h.name,kind:'paid',sortOrder:mstNextSort(rows),active:true});
      }
      s.mastersX={rows:{...(s.mastersX?.rows??{}),[HRM_HOLIDAYS]:rows},history:s.mastersX?.history??[]};
    }
    if(s.staffX&&'holidays' in s.staffX)delete s.staffX.holidays;
  }
  // SCC tax port (DEC-116, no SCHEMA bump): a compliance slice in the W05-C shape (obligations × periods) becomes SCC's (records per FY and
  // card); every filing, not-applicable mark, switched-off obligation and reopened filing is carried over. Detects the old shape itself, so it
  // is idempotent and runs on any schema.
  if(cmpIsLegacy(s.complianceX))s.complianceX=cmpFromLegacy(s.complianceX as never,s);
  // SCC leave port (DEC-116 / DEC-119; no SCHEMA bump — the shape is detected, so this is idempotent): old leave SPANS (no `date`) become one
  // record per counted date (staff.ts hrMigrateLeaveDays), and a stored Leave types list gets SCC's "Other" category.
  if(hrMigrateLeaveDays(s))changed=true;
  const lt=s.mastersX?.rows?.[HRM_LEAVE_TYPES];
  if(lt&&!lt.some(r=>r.code==='OTH')){s.mastersX!.rows[HRM_LEAVE_TYPES]=[...lt,{id:'lvt-OTH',code:'OTH',name:'Other',sortOrder:mstNextSort(lt),active:true,paid:true}];changed=true;}
  s.schema=SCHEMA;
  return changed;
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
        const older=s.schema<SCHEMA,changed=migrate(s);return {state:s as State,reset:false,migrated:older||changed};}
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

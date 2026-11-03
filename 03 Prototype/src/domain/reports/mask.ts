// F7-R1 · permissions (rule V), money removal (rule M) and masking (§1.6) — all applied to the selector output before any renderer.
import type {State} from '../types';
import type {RptDefinition,RptColumn,RptRow,RptMask} from './types';
import {can} from '../selectors';
import {authorize} from '../streams/admin';
import {moneyColumns} from './computed';
import type {Permission} from '../types';

/** Roles that never pass a money gate (DEC-061): the static check in the tests uses this list. */
export const RPT_NO_MONEY_ROLES=['Supervisor','Housekeeper','Maintenance','Stock'];
// F7-R5
/** PERM-INV-001 invoices.view: Owner, Manager, Reception, Cashier, Accountant. PERM-ACC-001 accounting.view: Owner, Manager, Accountant (Cashier removed by migration 3700). */
export const RPT_V2_GRANTS:Record<string,string[]>={'PERM-INV-001':['Owner','Manager','Reception','Cashier','Accountant'],'PERM-ACC-001':['Owner','Manager','Accountant'],/** F7: PERM-ACC-013 accounting.tax — Owner, Accountant (migration 4249); tax reports per DEC-202. */'PERM-ACC-013':['Owner','Accountant']};
// F7-R5 end
// F7-R6
/** PERM-ACC-006 accounting.tally.export (simulated): Owner + Accountant (G13). Same grants as the server seed 1001. */
RPT_V2_GRANTS['PERM-ACC-006']=['Owner','Accountant'];
// F7-R6 end
// F7-R8
/** Server grants (migrations 3700 / 3921 / 0901 seeds) of the module codes the moved built reports use; the prototype's v1 matrix has no row for them.
 *  Wide grants (scope P, or A that is not own-only) and the own-shift scoped ones (POS-004 Cashier S) are separate: a scoped grant passes only for `ownShift` reports. */
export const RPT_V2_R8:Record<string,string[]>={'PERM-STK-001':['Owner','Manager','Accountant','Supervisor','Housekeeper'],'PERM-STK-004':['Owner','Manager','Accountant'],
  'PERM-POS-004':['Owner','Manager','Accountant'],'PERM-CFG-001':['Owner','Tech admin']};
export const RPT_V2_R8_SCOPED:Record<string,string[]>={'PERM-POS-004':['Cashier']};
// F7-R8 end
/** One permission: PERM-* codes through the access matrix (scoped cells such as the Cashier's own-shift V·S pass only for `ownShift` reports),
 *  anything else is a page permission through can(). */
export function rptHolds(s:State,uid:string,perm:string,ownShift=false):boolean{
  const u=s.users.find(x=>x.id===uid);if(!u)return false;
  // F7-R5: server permissions of matrix v2 that the prototype's v1 matrix has no row for (same grants as the server seed: 2401 / 3700).
  const v2=RPT_V2_GRANTS[perm];if(v2)return u.enabled&&!(u.expiresOn&&s.date>u.expiresOn)&&v2.includes(u.role);
  // F7-R5 end
  // F7-R8
  const live=u.enabled&&!(u.expiresOn&&s.date>u.expiresOn);
  if(RPT_V2_R8[perm]||RPT_V2_R8_SCOPED[perm])return live&&(RPT_V2_R8[perm]?.includes(u.role)||(ownShift&&!!RPT_V2_R8_SCOPED[perm]?.includes(u.role)));
  // F7-R8 end
  if(perm.startsWith('PERM-')){
    if(RPT_NO_MONEY_ROLES.includes(u.role)&&/^PERM-RPT-00[23]$/.test(perm))return false;
    const d=authorize(s,uid,perm,ownShift?{}:{shiftUser:'',assignee:'',ownerId:''});return d.decision==='allow';
  }
  return can(s,uid,perm as Permission);
}
type Def=Pick<RptDefinition,'viewPerms'|'deskPerms'|'exportPerm'|'moneyGate'|'moneyFlag'>&{ownShift?:boolean};
/** View = any of viewPerms or deskPerms (rule V.5). Print = view. */
export const canViewReport=(s:State,uid:string,d:Def)=>[...d.viewPerms,...(d.deskPerms??[])].some(p=>rptHolds(s,uid,p,d.ownShift));
/** Excel / PDF / CSV = view and the export permission (PERM-RPT-003 unless the file product has its own code). */
export const canExportReport=(s:State,uid:string,d:Def)=>canViewReport(s,uid,d)&&rptHolds(s,uid,d.exportPerm??'PERM-RPT-003');
/** Holds the report's money gate (always false for housekeeping / maintenance / stock roles). */
export const holdsMoneyGate=(s:State,uid:string,d:Def)=>!d.moneyGate||rptHolds(s,uid,d.moneyGate,d.ownShift);

/** Rule M: money columns (and computed columns depending on them) removed — not blanked. Returns the kept columns and the removed keys. */
export function removeMoney(columns:RptColumn[],rows:RptRow[]):{columns:RptColumn[];rows:RptRow[];removed:string[]}{
  const m=moneyColumns(columns);if(!m.size)return {columns,rows,removed:[]};
  const keep=columns.filter(c=>!m.has(c.key));
  return {columns:keep,removed:[...m],rows:rows.map(r=>({...r,cells:Object.fromEntries(Object.entries(r.cells).filter(([k])=>!m.has(k)))}))};
}
/** `J•••` / `XXXX-XXXX-1234` / `XXXXXX1234`. */
export function maskValue(kind:RptMask,v:string):string{
  const t=String(v??'');if(!t)return t;
  if(kind==='guestName')return t.split(/\s+/).filter(Boolean).map(w=>w[0].toUpperCase()+'•••').join(' ');
  const last=t.replace(/[^0-9A-Za-z]/g,'').slice(-4);
  return kind==='idNumber'?'XXXX-XXXX-'+last:'XXXXXX'+last;
}
/** Masks cells by column kind. Guest names stay visible with billing or reservations; ID numbers are masked in every format, always;
 *  bank accounts are masked here (a bank file product unmasks on its own path, never through the renderer). */
export function applyMasks(s:State,uid:string,columns:RptColumn[],rows:RptRow[]):RptRow[]{
  const mc=columns.filter(c=>c.mask);if(!mc.length)return rows;
  const namesOk=can(s,uid,['billing','reservations'] as Permission[]);
  return rows.map(r=>{const cells={...r.cells};
    for(const c of mc){const v=cells[c.key];if(typeof v!=='string')continue;if(c.mask==='guestName'&&namesOk)continue;cells[c.key]=maskValue(c.mask!,v);}
    return {...r,cells};});
}

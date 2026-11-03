// F7-R1 · permissions (rule V), money removal (rule M) and masking (§1.6) — all applied to the selector output before any renderer.
import type {State} from '../types';
import type {RptDefinition,RptColumn,RptRow,RptMask} from './types';
import {can} from '../selectors';
import {authorize} from '../streams/admin';
import {moneyColumns} from './computed';
import type {Permission} from '../types';

/** Roles that never pass a money gate (DEC-061): the static check in the tests uses this list. */
export const RPT_NO_MONEY_ROLES=['Supervisor','Housekeeper','Maintenance','Stock'];
/** One permission: PERM-* codes through the access matrix (scoped cells such as the Cashier's own-shift V·S pass only for `ownShift` reports),
 *  anything else is a page permission through can(). */
export function rptHolds(s:State,uid:string,perm:string,ownShift=false):boolean{
  const u=s.users.find(x=>x.id===uid);if(!u)return false;
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

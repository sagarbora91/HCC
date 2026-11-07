// F7-R1 · report definition registry (RPT-ARCHITECTURE §2). One registry per side; ids never renamed. Server mirror: Hcc.Reporting ReportCatalogue.
import type {RptDefinition,RptBoundParams} from './types';

const REG=new Map<string,RptDefinition>();
const ID=/^rpt\.(guests|rooms|money|tax|acc|people|stock|rest|admin|revenue|study)\.[a-z0-9-]+$/;

/** Registers a report definition. A second registration of the same id replaces the first only when it is the same object (module reload). */
export function registerReport<P extends RptBoundParams>(def:RptDefinition<P>):RptDefinition<P>{
  if(!ID.test(def.id))throw new Error(`Report id ${def.id} must be rpt.<area>.<slug>.`);
  if(def.id.split('.')[1]!==def.area)throw new Error(`Report ${def.id}: area ${def.area} does not match the id.`);
  const keys=new Set<string>();
  for(const c of def.columns){if(keys.has(c.key))throw new Error(`Report ${def.id}: duplicate column ${c.key}.`);keys.add(c.key);}
  for(const c of def.columns)for(const d of c.computed?.deps??[])if(!keys.has(d))throw new Error(`Report ${def.id}: computed column ${c.key} needs unknown column ${d}.`);
  if(def.moneyFlag&&!def.moneyGate)throw new Error(`Report ${def.id}: a money report names its money gate.`);
  if(!def.viewPerms.length)throw new Error(`Report ${def.id}: at least one view permission.`);
  REG.set(def.id,def as unknown as RptDefinition);
  return def;
}
export const reportDef=(id:string)=>REG.get(id);
/** Every registered definition, sorted by id (stable order for the catalogue JSON and the hub). */
export const allReports=()=>[...REG.values()].sort((a,b)=>a.id<b.id?-1:a.id>b.id?1:0);

/** The cross-side catalogue record (no selector): what tools/rpt-catalogue.mjs writes to catalogue.json for the server cross-check. */
export function catalogueRecord(d:RptDefinition){
  return {id:d.id,title:d.title,area:d.area,priority:d.priority,status:d.status,moneyFlag:d.moneyFlag,moneyGate:d.moneyGate??null,
    viewPerms:d.viewPerms,deskPerms:d.deskPerms??[],exportPerm:d.exportPerm??'PERM-RPT-003',dateClass:d.dateClass??null,
    params:d.params.map(p=>p.key),columns:d.columns.map(c=>({key:c.key,type:c.type,money:c.money,mask:c.mask??null,computed:c.computed?.expr??null})),
    formats:d.formats,phone:d.phone,layout:d.layout??'table',side:d.serverMirror===false?'prototype':'both'};
}

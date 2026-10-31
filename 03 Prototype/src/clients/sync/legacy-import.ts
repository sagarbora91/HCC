// LG-UI · production client of the legacy importer (LG-HISTORY-CONTRACT §5, permission PERM-LGC-001 = Owner).
//   POST /commands/legacy.import {folder, parts, dryRun, expectedTotals} → LegacyImportResult
//   GET  /legacy/runs → the import runs (newest first)
// Not a SyncArea: no State slice. The page (features/admin/LegacyImport.tsx) calls this through setLegacyImportClient (wired by the
// production composition; the integrator adds the one line). It calls nothing but the two contract routes.
import type {LegacyImportClient,LegacyImportResult,LegacyRunRequest,LegacyRunRow} from '../../features/admin/legacyImportModel';

type Api={get<T>(path:string):Promise<T>;command<T>(name:string,body:Record<string,unknown>):Promise<T>};
const s=(v:unknown)=>typeof v==='string'?v:v==null?'':String(v);
/** Tolerant mapping of one `GET /legacy/runs` row (camelCase; extras ignored). */
export function legacyRunFromServer(o:Record<string,unknown>):LegacyRunRow{
  const parts=Array.isArray(o.parts)?o.parts.map(s):s(o.parts).split(',').map(x=>x.trim()).filter(Boolean);
  const row:LegacyRunRow={runId:s(o.runId??o.id),at:s(o.startedUtc??o.createdUtc??o.at),dryRun:!!o.dryRun,parts,committed:!!o.committed};
  if(o.extractorVersion)row.extractorVersion=s(o.extractorVersion);
  if(o.by??o.userName)row.by=s(o.by??o.userName);
  return row;
}
export function createLegacyImportClient(api:Api):LegacyImportClient{
  return {
    run:(req:LegacyRunRequest)=>api.command<LegacyImportResult>('legacy.import',{folder:req.folder.trim(),parts:req.parts,dryRun:req.dryRun,expectedTotals:req.expectedTotals}),
    async runs(){const r=await api.get<Record<string,unknown>[]|{runs?:Record<string,unknown>[]}>('/legacy/runs');
      const list=Array.isArray(r)?r:(r?.runs??[]);return list.map(legacyRunFromServer);},
  };
}

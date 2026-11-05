// AF2-HUB-SERVER · React hooks of the production Report library (catalogue, run, export log, files from the server). Imported only from
// `process.env.HCC_BUILD==='production'` branches (features/reports/hubServer.tsx), so the prototype bundle never contains them.
import {getSyncRuntime} from './instance';
import {useServerFetch,type Fetched} from './reportsHooks';
import {loadHubCatalogue,runHubReport,loadHubExportLog,exportHubFile,type HubDef,type HubFile} from './reports-hub-run';
import type {RptResult,RptExportLogEntry} from '../../domain/reports/types';

export type {Fetched};
const keyOf=(raw:Record<string,string>)=>Object.keys(raw).sort().map(k=>k+'='+raw[k]).join('&');
export const useServerHubCatalogue=():Fetched<HubDef[]>=>useServerFetch('rpt-catalogue',loadHubCatalogue);
/** One server run per (report, parameters); null id = nothing to run (a report that is not on the server). */
export const useServerHubRun=(id:string|null,raw:Record<string,string>):Fetched<RptResult>=>
  useServerFetch(id?'rpt-run|'+id+'|'+keyOf(raw):null,api=>runHubReport(api,id as string,raw));
export const useServerHubExportLog=():Fetched<RptExportLogEntry[]>=>useServerFetch('rpt-export-log',loadHubExportLog);
/** The server builds the file (and logs it); the caller saves it with app/native. */
export const fetchHubFile=(id:string,format:'xlsx'|'csv'|'pdf',raw:Record<string,string>,fallbackName:string):Promise<HubFile>=>
  exportHubFile(getSyncRuntime().api,id,format,raw,fallbackName);

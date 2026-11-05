// AF2-HUB-SERVER (audit "Missed A") · production-only pieces of the Report library: the catalogue, every run, the Excel / CSV files and the export
// log come from the hotel server (clients/sync/reportsHubHooks.ts), never from the slices this device happens to hold. Reached only from
// `process.env.HCC_BUILD==='production'` branches (tree-shaken out of the prototype). The demo build keeps the in-browser engine unchanged.
import React from 'react';
import {useServerHubCatalogue,useServerHubRun,useServerHubExportLog,fetchHubFile,type Fetched} from '../../clients/sync/reportsHubHooks';
import {mergeHubDefs,NOT_ON_SERVER,NOT_ON_SERVER_TEXT,type HubDef} from '../../clients/sync/reports-hub-run';
import type {RptDefinition,RptResult,RptExportLogEntry} from '../../domain/reports/types';

export {NOT_ON_SERVER,NOT_ON_SERVER_TEXT,fetchHubFile,type HubDef};

/** The production hub list: server catalogue + the prototype-only reports the role may open (labelled, never run). */
export function useServerHubDefs(protoOnly:readonly RptDefinition[]):{defs:HubDef[];f:Fetched<HubDef[]>}{
  const f=useServerHubCatalogue();
  return {defs:f.data?mergeHubDefs(f.data,protoOnly):[],f};
}
export const isOnServer=(d:RptDefinition)=>(d as HubDef).onServer!==false;

/** One run on the server; the last answer of the same parameters stays on screen while the server is unreachable. */
export function useServerReportRun(def:RptDefinition,raw:Record<string,string>):{result:RptResult|null;error:string;loading:boolean}{
  const f=useServerHubRun(isOnServer(def)?def.id:null,raw);
  return {result:f.data??null,error:f.status==='error'&&!f.data?(f.error||'The report could not be loaded from the server.'):'',loading:f.status==='loading'&&!f.data};
}
export function useServerExportLog():RptExportLogEntry[]{return useServerHubExportLog().data??[];}

/** Loading / error line of the server catalogue (desktop list header and phone list). */
export function HubServerNote({f}:{f:Fetched<unknown>}){
  if(f.status==='idle')return null;
  if(f.status==='loading'&&!f.data)return <p className="sync-report-note" role="status" aria-live="polite">Loading the report list from the server{'…'}</p>;
  if(f.status==='error')return <p className="sync-report-note" role="alert">{f.data?'The server could not be reached. Showing the report list loaded earlier.':'The report list could not be loaded from the server.'}{f.error?' '+f.error:''}</p>;
  return null;
}
/** Under a report's notes: where the figures came from. */
export function ServerRunNote({result,loading}:{result:RptResult|null;loading:boolean}){
  if(loading)return <p className="sync-report-note" role="status" aria-live="polite">Running the report on the server{'…'}</p>;
  if(!result)return null;
  return <p className="sync-report-note" role="status">Figures from the hotel server{result.about.runAt?' · run '+result.about.runAt:''}. The server applies the money and name rules for your role.</p>;
}

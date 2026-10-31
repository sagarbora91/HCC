// LG-UI · Legacy import (Owner, PERM-LGC-001): the contract's request / result shapes (LG-HISTORY-CONTRACT §5), the client seam the page
// calls, and a fictional demo client for the prototype. Pure data; no React, no State slice. Domain logic of the importer lives on the server.
export type LegacyPart='guests'|'history'|'cutover';
export const LEGACY_PARTS:{id:LegacyPart;label:string;note:string}[]=[
  {id:'guests',label:'Guests',note:'Match old guests to HCC guests, set date of birth, seal ID numbers, copy photos, add tags'},
  {id:'history',label:'History',note:'Old stays, bills and receipts per financial year, checked against the expected totals'},
  {id:'cutover',label:'Cut-over',note:'Opening balances: receivables, customer openings and advances (the Owner approves them in Accounting)'},
];
export type LegacyDifference={figure:string;expected:number;actual:number};
export type LegacyReconciliation={billsCount:number;netPaise:number;roomGstPaise:number;fbPaise:number;receiptsCount:number;receiptsPaise:number;refundsCount:number;refundsPaise:number;match:boolean;differences:LegacyDifference[]};
export type LegacyFyResult={fy:string;stays:number;segments:number;bills:number;receipts:number;duplicatesDropped:number;outOfFy:number;reconciliation:LegacyReconciliation};
export type LegacyImportResult={runId:string;dryRun:boolean;parts:LegacyPart[];extractorVersion:string;
  guests:{csvRows:number;created:number;matchedExisting:number;skippedInvalidPhone:number;merged:number;secretsSealed:number;dobSet:number;photosCopied:number;idProofsCopied:number;tagsAdded:number;errors:string[]};
  history:{perFy:LegacyFyResult[];errors:string[]};
  cutover:{customerOpeningRows:number;receivablesRows:number;advancesRows:number;inhouseRows:number;note:string};
  warnings:string[];committed:boolean};
export type LegacyRunRequest={folder:string;parts:LegacyPart[];dryRun:boolean;expectedTotals:string};
/** One row of GET /legacy/runs. Only the fields the list shows; unknown extras are ignored. */
export type LegacyRunRow={runId:string;at:string;dryRun:boolean;parts:string[];committed:boolean;extractorVersion?:string;by?:string};

export interface LegacyImportClient{run(req:LegacyRunRequest):Promise<LegacyImportResult>;runs():Promise<LegacyRunRow[]>;readonly demo?:boolean}

/** Why the Run button is blocked, or undefined when it may run. The server validates again (absolute path, totals.json, no UNC). */
export function legacyRunBlock(folder:string,parts:LegacyPart[],totals:string):string|undefined{
  const f=folder.trim();
  if(!f)return 'Enter the run folder first.';
  if(!/^[A-Za-z]:[\\/]/.test(f))return 'The folder must be a full path on the server PC, like E:\\… (network paths are refused).';
  if(!parts.length)return 'Choose at least one part.';
  if(!totals.trim())return 'Choose the totals.json of the run (the expected figures).';
  return undefined;
}
export const legacyFyOk=(f:LegacyFyResult)=>f.reconciliation.match;
export const legacyAllMatch=(r:LegacyImportResult)=>r.history.perFy.every(legacyFyOk);
/** Differences named `...Paise` are money; counts are not. */
export const legacyFigureIsMoney=(figure:string)=>/paise$/i.test(figure);

const rec=(over:Partial<LegacyReconciliation>):LegacyReconciliation=>({billsCount:0,netPaise:0,roomGstPaise:0,fbPaise:0,receiptsCount:0,receiptsPaise:0,refundsCount:0,refundsPaise:0,match:true,differences:[],...over});
/** Fictional run shown by the prototype: two financial years match, one does not (so the red state is visible). Nothing is read or written. */
export function demoLegacyResult(req:LegacyRunRequest):LegacyImportResult{
  const has=(p:LegacyPart)=>req.parts.includes(p);
  return {runId:'demo-0001',dryRun:req.dryRun,parts:req.parts,extractorVersion:'demo-1',committed:false,
    guests:{csvRows:has('guests')?420:0,created:0,matchedExisting:has('guests')?398:0,skippedInvalidPhone:has('guests')?14:0,merged:has('guests')?8:0,secretsSealed:has('guests')?221:0,dobSet:has('guests')?187:0,photosCopied:0,idProofsCopied:0,tagsAdded:has('guests')?36:0,errors:[]},
    history:{perFy:has('history')?[
      {fy:'2024-25',stays:612,segments:640,bills:598,receipts:733,duplicatesDropped:3,outOfFy:1,reconciliation:rec({billsCount:598,netPaise:41250000,roomGstPaise:4310000,receiptsCount:733,receiptsPaise:44980000,refundsCount:9,refundsPaise:410000})},
      {fy:'2025-26',stays:655,segments:671,bills:640,receipts:790,duplicatesDropped:2,outOfFy:0,reconciliation:rec({billsCount:640,netPaise:46890000,roomGstPaise:4760000,receiptsCount:790,receiptsPaise:50210000,refundsCount:11,refundsPaise:520000})},
      {fy:'2026-27',stays:301,segments:309,bills:288,receipts:350,duplicatesDropped:0,outOfFy:0,reconciliation:rec({billsCount:288,netPaise:21780000,roomGstPaise:2230000,receiptsCount:350,receiptsPaise:23310000,refundsCount:4,refundsPaise:90000,match:false,
        differences:[{figure:'netPaise',expected:21780500,actual:21780000},{figure:'receiptsCount',expected:351,actual:350}]})},
    ]:[],errors:[]},
    cutover:{customerOpeningRows:has('cutover')?64:0,receivablesRows:has('cutover')?17:0,advancesRows:has('cutover')?5:0,inhouseRows:has('cutover')?3:0,note:has('cutover')?'In-house guests are listed for hand re-entry; nothing is posted for them.':''},
    warnings:['Demo data: these figures are fictional and nothing was read from any folder.'],
  };
}
export const demoLegacyClient:LegacyImportClient={demo:true,
  async run(req){return demoLegacyResult(req);},
  async runs(){return [
    {runId:'demo-0001',at:'2026-11-05T21:30:00Z',dryRun:true,parts:['guests','history','cutover'],committed:false,extractorVersion:'demo-1',by:'Owner'},
    {runId:'demo-0000',at:'2026-11-04T20:10:00Z',dryRun:true,parts:['history'],committed:false,extractorVersion:'demo-1',by:'Owner'}];},
};
let client:LegacyImportClient=demoLegacyClient;
export const legacyImportClient=():LegacyImportClient=>client;
/** The production composition replaces the demo client (clients/sync/legacy-import.ts). */
export function setLegacyImportClient(c:LegacyImportClient|undefined){client=c??demoLegacyClient;}

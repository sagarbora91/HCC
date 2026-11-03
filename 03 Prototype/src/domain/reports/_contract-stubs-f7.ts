// F7-R2 · CONTRACT STUBS. R1 (rpt-engine) builds the real engine in src/domain/reports/engine.ts with these exact names (F7-CONTRACT §2).
// Until it merges the hub imports through features/reports/hub-engine.ts, which re-exports this file; f7-int repoints that ONE line to ../../domain/reports/engine
// and deletes this file. The stubs return "nothing registered" and refuse to run, so the hub shows its empty state instead of wrong figures.
import type {State} from '../types';
import type {RptDefinition,RptResult,RptPrintModel,RptBoundParams} from './types';
import type {XSheet} from '../../app/xlsx';
import {HccError} from '../errors';

export function reportCatalogue(_s:State,_uid:string):RptDefinition[]{return [];}
export function canViewReport(_s:State,_uid:string,_def:RptDefinition):boolean{return false;}
export function canExportReport(_s:State,_uid:string,_def:RptDefinition):boolean{return false;}
export function runReport(_s:State,_uid:string,id:string,_raw:Record<string,string|boolean|undefined>):RptResult{throw new HccError('NOT_FOUND','Report '+id+' is not registered yet.');}
export function toXlsx(_r:RptResult):XSheet[]{return [];}
export function toCsv(_r:RptResult):string{return '';}
export function toPrintModel(_r:RptResult):RptPrintModel{throw new HccError('NOT_FOUND','The print engine is not registered yet.');}
export function printHtml(_m:RptPrintModel):string{return '';}
export type {RptBoundParams};

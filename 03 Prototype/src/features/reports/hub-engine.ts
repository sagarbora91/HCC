// F7 · the hub's one seam to the report engine (R1). Adapts the engine's signatures to the hub's.
import type {State} from '../../domain/types';
import type {RptDefinition,RptResult} from '../../domain/reports/types';
import {allReports,canViewReport,canExportReport,runReport,toXlsx as engineXlsx,toCsv,toPrintModel as enginePrintModel,printHtml as enginePrintHtml} from '../../domain/reports/engine';
import {printablePage} from '../../app/native';
import {measReportOn} from '../../domain/streams/measure';

export {canViewReport,canExportReport,runReport,toCsv};
/** Full definitions the caller may view (the hub filters and groups them). */
export function reportCatalogue(s:State,uid:string):RptDefinition[]{return allReports().filter(d=>measReportOn(s,d.id)&&canViewReport(s,uid,d));}
const property=(s?:State)=>s?.settings?.hotel||'HCC';
export const toXlsx=(r:RptResult,s?:State)=>engineXlsx(r,property(s));
export const toPrintModel=(r:RptResult,s?:State)=>enginePrintModel(r,property(s));
/** A complete printable page for native printPage(title, page). */
export function printHtml(m:ReturnType<typeof enginePrintModel>):string{const p=enginePrintHtml(m);return printablePage(p.title,p.body,p.css);}

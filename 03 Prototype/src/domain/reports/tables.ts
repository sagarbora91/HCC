// F7-R1 · a result's tables: `sections` when the report has several blocks (e.g. revenue A / collections B / bridge C), else the one main table.
import type {RptResult,RptColumn,RptRow} from './types';
export type RptTable={key:string;title?:string;columns:RptColumn[];rows:RptRow[]};
export const rptTables=(r:RptResult):RptTable[]=>r.sections?.length?r.sections:[{key:'main',columns:r.columns,rows:r.rows}];

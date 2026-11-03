// F7 report module — contract types (02 Planning/agents/F7-CONTRACT.md §1). Declared by the setup commit; lanes import, never redeclare.
// rpt-engine (R1) may add optional members; nobody renames. Money = integer paise.
import type {State} from '../types';

export type RptArea='guests'|'rooms'|'money'|'tax'|'acc'|'people'|'stock'|'rest'|'admin';
export type RptStatus='built'|'partial'|'ACC-3'|'planned';
export type RptPriority='P1'|'P2'|'P3';
/** Catalogue §1.4 date classes. */
export type RptDateClass='D-DAY'|'D-S'|'D-B'|'D-F'|'D-M'|'D-FY'|'D-ASOF'|'D-NOW'|'D-FWD';
export type RptFormat='screen'|'xlsx'|'pdf'|'csv'|'zip'|'xml';
export type RptPhone='yes'|'summary'|'no';
export type RptColType='text'|'int'|'money'|'date'|'pct'|'bool';
/** Catalogue §1.6. */
export type RptMask='guestName'|'idNumber'|'bankAccount';
export type RptParamType='date'|'range'|'month'|'fy'|'enum'|'text'|'bool'|'party'|'room'|'user';
export type RptParam={key:string;label:string;type:RptParamType;dateClass?:RptDateClass;options?:string[];default?:string;required?:boolean};
/** OP-13 hook: `expr` names a fixed-code function today (e.g. `pct(occupied,sellable)`). */
export type RptComputed={expr:string;deps:string[]};
export type RptColumn={key:string;label:string;type:RptColType;money:boolean;mask?:RptMask;computed?:RptComputed;width?:number};
export type RptCell=string|number|null;
export type RptRow={kind:'row'|'subtotal'|'total';cells:Record<string,RptCell>;group?:string;/** source-rows drill key (rpt.money.source-rows). */drill?:string};
export type RptAbout={runAt:string;user:string;dateBasis:string;provisional:boolean;legacy:boolean;from?:string;to?:string};
export type RptBoundParams=Record<string,string|boolean|undefined>&{from?:string;to?:string};
export type RptResult={id:string;title:string;params:RptBoundParams;columns:RptColumn[];rows:RptRow[];about:RptAbout;
  /** Columns removed by rule M for this caller (keys). */ removed?:string[];page?:{index:number;size:number;total:number}};
export type RptDefinition<P extends RptBoundParams=RptBoundParams>={id:string;title:string;area:RptArea;priority:RptPriority;status:RptStatus;
  moneyFlag:boolean;moneyGate?:string;viewPerms:string[];deskPerms?:string[];exportPerm?:string;dateClass?:RptDateClass;
  params:RptParam[];columns:RptColumn[];dateBasis:string;formats:RptFormat[];phone:RptPhone;layout?:'table'|'document';legacyIds?:string[];
  select:(s:State,uid:string,p:P)=>Omit<RptResult,'about'|'title'|'id'>&{about?:Partial<RptAbout>}};
export type RptPrintTable={columns:RptColumn[];rows:RptRow[]};
export type RptPrintModel={orientation:'portrait'|'landscape';header:{property:string;title:string;params:[string,string][];runAt:string;user:string};
  pages:{tables:RptPrintTable[]}[];footer:{basis:string;provisional:boolean}};
export type RptSchedule={id:string;reportId:string;params:Record<string,string>;format:Exclude<RptFormat,'screen'>;
  cadence:{kind:'daily'|'weekly'|'monthly';day?:number;time:string};owner:string;recipients:string[];enabled:boolean;lastRun?:string};
export type RptRun={id:string;scheduleId?:string;reportId:string;params:Record<string,string>;by:string;startedAt:string;finishedAt?:string;
  status:'ok'|'failed'|'held';rows?:number;fileRef?:string;error?:string};
export type RptExportLogEntry={id:string;at:string;by:string;reportId:string;params:Record<string,string>;format:'print'|Exclude<RptFormat,'screen'>;
  rows:number;fileName?:string;provisional:boolean};
export type RptState={favourites:Record<string,string[]>;schedules:RptSchedule[];runs:RptRun[];exportLog:RptExportLogEntry[]};   // s.rptX

// F5-A1 · ACC-4 year end and bank reconciliation — production sync (server: Hcc.Api/Endpoints/AccountingYearBankEndpoints.cs).
//   commands  ACC_YEAR_CARRY_FORWARD {fy}            → POST /accounting/year/{fy}/carry-forward
//             ACC_YEAR_LOCK {fy, carryForwardId}     → POST /accounting/year/{fy}/lock {carryForwardId}
//             ACC_YEAR_REOPEN {fy, reason}           → POST /accounting/year/{fy}/reopen {reason}
//             ACC_YEAR_RELOCK {fy}                   → POST /accounting/year/{fy}/relock
//             BANK_LAYOUT_UPSERT {layout}            → POST /accounting/bank/layouts
//             BANK_IMPORT {bankLedger, layoutId, fileName, text, keepDuplicates?} → POST /accounting/bank/import
//             BANK_AUTO_MATCH {bankLedger} · BANK_MATCH {rowIds, refs} · BANK_UNMATCH {matchId} · BANK_IGNORE {rowId, reason}
//             BANK_CREATE_VOUCHER {rowId, ledger?, narration?} · BANK_SETTLEMENT_POST {rowId, clearing, gross?, mdr, mdrGst, itc}
//   loader    accYearX ← GET /accounting/year (years + carry-forwards). The bank rows are read per ledger by the bank page
//             (GET /accounting/bank/rows?ledger, /recon, /clearing) — no slice loader (the ledger is a page choice).
//   Plain POST routes (no Idempotency-Key): the year / bank commands are guarded by state (draft / status / file hash) on the server.
import type {AccYearSlice,AccYearState,AccCarryForward} from '../../domain/streams/accounting-year';
import type {SyncArea,LoaderCtx,CommandCtx} from './types';
import {protoUserId,utcToClock} from './map';

type SrvYear={fy:string;status:string;lockedUtc?:string|null;lockedBy?:number|null;reopenedUtc?:string|null;reopenedBy?:number|null;reopenReason?:string|null;provisionalExports:boolean;carryForwardId?:string|null};
type SrvCfLine={ledger:string;party?:string|null;dr:number;cr:number};
type SrvCf={id:string;fromFy:string;toFy:string;status:string;lines:SrvCfLine[];profitToCapital:number;drawingsToCapital:number;close:{account:string;debitPaise:number;creditPaise:number}[];
  generatedUtc:string;generatedBy:number;approvedBy?:number|null;approvedUtc?:string|null;supersededBy?:string|null};
const utc=(v:string|null|undefined)=>!v?'':/(Z|[+-]\d\d:\d\d)$/i.test(v)?v:v+'Z';

export function yearFromServer(y:SrvYear):AccYearState{
  return {fy:y.fy,status:y.status as AccYearState['status'],provisionalExports:!!y.provisionalExports,
    ...(y.lockedUtc?{lockedAt:utcToClock(utc(y.lockedUtc))}:{}),...(y.lockedBy!=null?{lockedBy:protoUserId(y.lockedBy)}:{}),
    ...(y.reopenedUtc?{reopenedAt:utcToClock(utc(y.reopenedUtc))}:{}),...(y.reopenedBy!=null?{reopenedBy:protoUserId(y.reopenedBy)}:{}),
    ...(y.reopenReason?{reopenReason:y.reopenReason}:{}),...(y.carryForwardId?{carryForwardId:y.carryForwardId}:{})};
}
export function carryForwardFromServer(c:SrvCf):AccCarryForward{
  return {id:c.id,fromFy:c.fromFy,toFy:c.toFy,status:c.status as AccCarryForward['status'],lines:c.lines.map(l=>({ledger:l.ledger,...(l.party?{party:l.party}:{}),dr:l.dr,cr:l.cr})),
    profitToCapital:c.profitToCapital,drawingsToCapital:c.drawingsToCapital,close:c.close.map(e=>({account:e.account,debit:e.debitPaise,credit:e.creditPaise})),
    generatedAt:utcToClock(utc(c.generatedUtc)),generatedBy:protoUserId(c.generatedBy),...(c.approvedBy!=null?{approvedBy:protoUserId(c.approvedBy)}:{}),
    ...(c.approvedUtc?{approvedAt:utcToClock(utc(c.approvedUtc))}:{}),...(c.supersededBy?{supersededBy:c.supersededBy}:{})};
}
async function loadYears(c:LoaderCtx):Promise<AccYearSlice>{
  const r=await c.api.get<{years:SrvYear[];carryForwards:SrvCf[]}>('/accounting/year');
  return {years:r.years.map(yearFromServer),carryForwards:r.carryForwards.map(carryForwardFromServer)};
}
const fy=(c:CommandCtx)=>encodeURIComponent(String(c.cmd.fy??''));
const post=(c:CommandCtx,path:string,body:unknown={})=>c.api.transport.post('/accounting'+path,body);

export const accountingYear:SyncArea={
  id:'accounting-year',
  loaders:[{slice:'accYearX',requires:'reports',reads:['GET /accounting/year'],load:loadYears}],
  commands:[
    {type:'ACC_YEAR_PREVIEW',reload:[],async send(c){await c.api.get(`/accounting/year/${fy(c)}/preview`);}},
    {type:'ACC_YEAR_CARRY_FORWARD',reload:['accYearX'],async send(c){await post(c,`/year/${fy(c)}/carry-forward`);}},
    {type:'ACC_YEAR_LOCK',reload:['accYearX','accountingX'],async send(c){await post(c,`/year/${fy(c)}/lock`,{carryForwardId:String(c.cmd.carryForwardId??'')});}},
    {type:'ACC_YEAR_REOPEN',reload:['accYearX'],precheck:cmd=>String(cmd.reason??'').trim().length>=10?undefined:'Give a reason of at least 10 characters (DEC-077).',
      async send(c){await post(c,`/year/${fy(c)}/reopen`,{reason:String(c.cmd.reason??'').trim()});}},
    {type:'ACC_YEAR_RELOCK',reload:['accYearX','accountingX'],async send(c){await post(c,`/year/${fy(c)}/relock`);}},
    {type:'BANK_LAYOUT_UPSERT',reload:[],async send(c){await post(c,'/bank/layouts',c.cmd.layout);}},
    {type:'BANK_IMPORT',reload:[],async send(c){const m=c.cmd;await post(c,'/bank/import',{bankLedger:m.bankLedger,layoutId:m.layoutId??'generic',fileName:m.fileName??null,text:m.text,keepDuplicates:m.keepDuplicates===true});}},
    {type:'BANK_AUTO_MATCH',reload:[],async send(c){await post(c,'/bank/auto-match',{bankLedger:c.cmd.bankLedger});}},
    {type:'BANK_MATCH',reload:[],async send(c){await post(c,'/bank/match',{rowIds:c.cmd.rowIds,refs:c.cmd.refs});}},
    {type:'BANK_UNMATCH',reload:[],async send(c){await post(c,'/bank/unmatch',{id:c.cmd.matchId});}},
    {type:'BANK_IGNORE',reload:[],async send(c){await post(c,'/bank/ignore',{id:c.cmd.rowId,reason:c.cmd.reason??null});}},
    {type:'BANK_CREATE_VOUCHER',reload:['accountingX'],async send(c){await post(c,'/bank/voucher',{rowId:c.cmd.rowId,ledger:c.cmd.ledger??null,narration:c.cmd.narration??null});}},
    {type:'BANK_SETTLEMENT_POST',reload:['accountingX'],async send(c){const m=c.cmd;
      await post(c,'/bank/settlement',{rowId:m.rowId,clearing:m.clearing,gross:m.gross??null,mdr:Number(m.mdr??0),mdrGst:Number(m.mdrGst??0),itc:m.itc===true});}},
  ],
};

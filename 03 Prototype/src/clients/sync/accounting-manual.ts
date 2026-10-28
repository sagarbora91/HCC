// ACC-2 lane acc2-manual · manual vouchers and loan party ledgers on the server (Hcc.Api AccountingManualEndpoints; posting contract §2, §9).
// Plain object literals and functions only (no top-level calls).
//
//   POST /commands/SaveManualVoucher   {voucherId, date, type, narration, reference, template, lines:[{account, debitPaise, creditPaise, note}]}
//                                                                                    ← ACC_MANUAL_SAVE (PERM-ACC-010: Owner approved at once, Accountant pending)
//   POST /commands/DecideManualVoucher {voucherId, approve, reason, expectedVersion}  ← ACC_MANUAL_DECIDE (PERM-ACC-011)
//   POST /commands/VoidManualVoucher   {voucherId, reason, expectedVersion}           ← ACC_MANUAL_VOID (PERM-ACC-011)
//   POST /commands/AddLoan             {code, name, lender, accountLast4, note}       ← ACC_LOAN_ADD (PERM-ACC-007, Owner)
//   GET  /accounting/manual            (PERM-ACC-001) → vouchers (newest first) + loans
// accountingX.manual (and the loan rows of accountingX.chart.ledgers): ACC_MANUAL_LOADER below — NOT registered in the area yet: the
// integrator adds it with the READS line in sync/reads.ts and the route in tests/_role-reads.json (as ACC-1 did for the opening loader).
import type {ACCManualVoucher,ACCManualLine,ACCManualTemplate,ACCState} from '../../domain/streams/accounting';
import type {ACCLoanLedger} from '../../domain/streams/accounting-manual';
import {HccClientError} from '../errors';
import type {SyncArea,SliceLoader,LoaderCtx,CommandCtx} from './types';
import type {ReadRoute} from './reads';
import {protoId,protoUserId,serverId,utcToClock,paise,isoDate} from './map';

type Wire=Record<string,unknown>;
export type SrvManualLine={account:string;name:string;debitPaise:number;creditPaise:number;note?:string|null};
export type SrvManualVoucher={voucherId:number;date:string;type:string;narration:string;reference?:string|null;template?:string|null;status:string;
  lines:SrvManualLine[];enteredBy:number;enteredUtc:string;decidedBy?:number|null;decidedUtc?:string|null;reason?:string|null;voidOf?:number|null;
  voidedBy?:number|null;bookedOn?:string|null;version:string};
export type SrvLoan={account:string;name:string;lender:string;accountLast4?:string|null;active:boolean;note?:string|null};
export type SrvManualState={vouchers:SrvManualVoucher[];loans:SrvLoan[]};

const txt=(v:unknown)=>v==null?'':String(v).trim();
const utc=(v:string|null|undefined)=>!v?'':/(Z|[+-]\d\d:\d\d)$/i.test(v)?v:v+'Z';
const TYPES=['Journal','Contra','Payment','Receipt'],STATUS=['pending','approved','rejected','void'];

/** Server voucher → the prototype's ACCManualVoucher (id = String(server id), paise, IST clock). */
export function manualFromServer(v:SrvManualVoucher):ACCManualVoucher{
  if(!TYPES.includes(v.type)||!STATUS.includes(v.status))throw new HccClientError('PROTOCOL','Unknown manual voucher type or status.');
  const lines:ACCManualLine[]=v.lines.map(l=>({account:l.account,debit:paise(l.debitPaise,'debitPaise'),credit:paise(l.creditPaise,'creditPaise'),...(l.note?{note:l.note}:{})}));
  const o:ACCManualVoucher={id:protoId(v.voucherId),date:isoDate(v.date),type:v.type as ACCManualVoucher['type'],narration:v.narration,lines,
    status:v.status as ACCManualVoucher['status'],by:protoUserId(v.enteredBy),at:utcToClock(utc(v.enteredUtc))};
  if(v.reference)o.reference=v.reference;if(v.template)o.template=v.template as ACCManualTemplate;
  if(v.decidedBy!=null)o.decidedBy=protoUserId(v.decidedBy);if(v.decidedUtc)o.decidedAt=utcToClock(utc(v.decidedUtc));
  if(v.reason)o.reason=v.reason;if(v.voidOf!=null)o.voidOf=protoId(v.voidOf);if(v.voidedBy!=null)o.voidedBy=protoUserId(v.voidedBy);
  if(v.bookedOn)o.bookedOn=isoDate(v.bookedOn,'bookedOn');
  return o;
}
/** Server loan → the chart row the prototype keeps (contract §2: `2400/LN-<code>`, kind loan). */
export const loanFromServer=(l:SrvLoan):ACCLoanLedger=>({code:l.account,name:l.name,group:'secured-loans',system:false,gst:'none',active:l.active,kind:'loan',lender:l.lender,
  ...(l.accountLast4?{accountLast4:l.accountLast4}:{}),...(l.note?{note:l.note}:{})});

/** SaveManualVoucher body (the voucher type travels as `voucherType` in the prototype command: its `type` is the command name). */
export function manualSaveBody(cmd:Wire){
  if(!Array.isArray(cmd.lines)||cmd.lines.length<2)throw new HccClientError('VALIDATION','Enter 2 to 100 lines.');
  const id=cmd.id==null||cmd.id===''?null:serverId(cmd.id as string,'Manual voucher');
  return {voucherId:id,date:txt(cmd.date),type:txt(cmd.voucherType),narration:txt(cmd.narration),reference:txt(cmd.reference)||null,template:txt(cmd.template)||null,
    lines:(cmd.lines as Partial<ACCManualLine>[]).map(l=>({account:txt(l?.account),debitPaise:Number(l?.debit??0),creditPaise:Number(l?.credit??0),note:txt(l?.note)||null}))};
}
const version=(c:CommandCtx,id:string)=>c.ids.version('accManual',id)??null;

async function loadManual(c:LoaderCtx):Promise<ACCState>{
  const r=await c.api.get<SrvManualState>('/accounting/manual');
  const cur:ACCState={...(c.current('accountingX')??{})};
  cur.manual=r.vouchers.map(v=>{c.ids.setVersion('accManual',protoId(v.voucherId),v.version);return manualFromServer(v);}).reverse();
  const chart=cur.chart??{ledgers:[],groups:[],history:[]};
  cur.chart={...chart,ledgers:[...chart.ledgers.filter(l=>(l as Partial<ACCLoanLedger>).kind!=='loan'),...r.loans.map(loanFromServer)]};
  return cur;
}
/** Patch loader for accountingX.manual + the loan rows (to be registered at the ACC-2 integration: READS + tests/_role-reads.json). */
export const ACC_MANUAL_LOADER:SliceLoader={slice:'accountingX',kind:'patch',requires:'reports',reads:['GET /accounting/manual' as unknown as ReadRoute],load:loadManual}; // the route joins READS at the integration (cast until then)

const reason=(cmd:Wire)=>{const r=txt(cmd.reason);return r.length>=3&&r.length<=200?undefined:'Enter a reason (3–200 characters).';};

export const accountingManual:SyncArea={id:'accounting-manual',
  loaders:[],
  commands:[
    {type:'ACC_MANUAL_SAVE',reload:['accountingX'],precheck:cmd=>Array.isArray((cmd as Wire).lines)&&((cmd as Wire).lines as unknown[]).length>=2?undefined:'Enter 2 to 100 lines.',
      async send(c){await c.api.command('SaveManualVoucher',manualSaveBody(c.cmd as Wire));}},
    {type:'ACC_MANUAL_DECIDE',reload:['accountingX'],precheck:cmd=>(cmd as Wire).approve===true?undefined:reason(cmd as Wire),
      async send(c){const id=txt(c.cmd.id);
        await c.api.command('DecideManualVoucher',{voucherId:serverId(id,'Manual voucher'),approve:c.cmd.approve===true,reason:txt(c.cmd.reason)||null,expectedVersion:version(c,id)});}},
    {type:'ACC_MANUAL_VOID',reload:['accountingX'],precheck:cmd=>reason(cmd as Wire),
      async send(c){const id=txt(c.cmd.id);
        await c.api.command('VoidManualVoucher',{voucherId:serverId(id,'Manual voucher'),reason:txt(c.cmd.reason),expectedVersion:version(c,id)});}},
    {type:'ACC_LOAN_ADD',reload:['accountingX'],precheck:(_cmd,_s,me)=>me.permIds.includes('PERM-ACC-007')?undefined:'Only the Owner adds a loan to the chart of accounts.',
      async send(c){const x=c.cmd as Wire;
        await c.api.command('AddLoan',{code:txt(x.code)||null,name:txt(x.name),lender:txt(x.lender),accountLast4:txt(x.accountLast4)||null,note:txt(x.note)||null});}},
  ]};

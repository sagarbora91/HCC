// Cashier area (W4 fix F3, D4; Wave B1 DEC-073 / DEC-078): opening your own shift, and cash leaving the drawer.
//
//   loaders  cashOutX ← GET /cashier/transfers?from&to (bank deposits, till transfers, Owner drawings: the last 93 business days plus every pending one)
//                       + GET /cashier/desk when the role holds it (desk till = the till whose open shift is mine, else the first with a shift; the
//                       other tills with their open shift's owner and expected cash; the desk shift's opener and handover count, DEC-078).
//   commands SHIFT_OPEN → OpenShift {tillId, openingFloatPaise:null, handoverCountPaise, handoverReason}: the server carries the float from the last
//                       count; the opener's own count of the drawer they take over is the handover count (DEC-078; required by the server when
//                       the last count was someone else's). The day close no longer opens a shift (APK12-3, sync/closing.ts).
//            CASH_BANK → RecordCashBanking {tillId, amountPaise, bank, slipNo, slipImageBase64, slipContentType, note} (photo compressed on the
//                       device to under 100 KB, app/slipPhoto.ts) · CASH_BANK_DECIDE → ConfirmCashBanking (expectedVersion = the request's version)
//            TILL_TRANSFER → TransferTillCash {fromTillId, toTillId, amountPaise, reason} · TILL_TRANSFER_DECIDE → AcceptTillTransfer (version)
//            OWNER_DRAWING → RecordOwnerDrawing {tillId, amountPaise, reason}
// The server re-checks everything (own shift, maker-checker, below zero, locked day, photo size and type); prechecks repeat the cheap ones.
import type {SyncArea,LoaderCtx,CommandCtx,SliceKey} from './types';
import type {CXRecord,CXState,CXTill} from '../../services/mock';
import {addDays,cxDataUrlBytes,cxDataUrlType,cxSlice,CX_SLIP_MAX_BYTES} from '../../services/mock';
import {CASH_SLICES,CASHBOOK_DAYS} from './money';
import {deskTill,type SrvCashDesk} from './money/wire';
import {HccClientError} from '../errors';
import {protoId,protoUserId,paise,isoDate,utcToClock,serverId} from './map';

/** PERM id the server's OpenShift requires (BillingPermissions.ShiftCount). */
const SHIFT_PERM='PERM-SHF-001';

/** GET /cashier/transfers item (Hcc.Contracts Billing `CashTransferView`). */
export type SrvCashTransfer={transferId:number;kind:'banking'|'till'|'drawing';status:CXRecord['status'];businessDate:string;amountPaise:number;fromTillId:number;
  fromTillCode:string;toTillId:number|null;toTillCode:string|null;bank:string|null;slipNo:string|null;reason:string|null;recordedBy:number;recordedUtc:string;
  decidedBy:number|null;decidedUtc:string|null;decidedDate:string|null;decisionReason:string|null;slipStored:boolean;slipNote:string|null;approvalId:number|null;version:string};
export type SrvCashTransferList={businessDate:string;items:SrvCashTransfer[]};

/** One server record → the prototype's CXRecord (money stays paise; dates YYYY-MM-DD; users = protoUserId). */
export function cashTransferFromServer(t:SrvCashTransfer):CXRecord{
  const r:CXRecord={id:protoId(t.transferId),kind:t.kind,status:t.status,amount:paise(t.amountPaise,'amountPaise'),date:isoDate(t.businessDate,'businessDate'),
    actor:protoUserId(t.recordedBy),at:utcToClock(t.recordedUtc),fromTill:t.fromTillCode,slipStored:t.slipStored};
  if(t.toTillCode)r.toTill=t.toTillCode;
  if(t.bank)r.bank=t.bank;if(t.slipNo)r.slip=t.slipNo;if(t.reason)r.reason=t.reason;if(t.slipNote)r.slipNote=t.slipNote;
  if(t.decidedBy!=null)r.decidedBy=protoUserId(t.decidedBy);if(t.decidedUtc)r.decidedAt=utcToClock(t.decidedUtc);
  if(t.decidedDate)r.decidedDate=isoDate(t.decidedDate,'decidedDate');if(t.decisionReason)r.decisionReason=t.decisionReason;
  if(t.approvalId!=null)r.approvalId=protoId(t.approvalId);
  return r;
}
/** The desk read → desk till code / name, the other tills (transfer partners) and the desk shift's opener (DEC-078). */
export function cashDeskFromServer(d:SrvCashDesk,userId?:number):Pick<CXState,'desk'|'deskName'|'tills'|'opener'>{
  const t=deskTill(d,userId);
  const tills:CXTill[]=d.tills.filter(x=>x!==t).map(x=>{
    const a=x.active,open=!!a&&a.shift.status==='Open'&&a.shift.businessDate===d.businessDate;
    const out:CXTill={code:x.till.code,name:x.till.name,shiftOpen:open};
    if(a?.openedBy!=null)out.openedBy=protoUserId(a.openedBy);
    if(open&&x.expected)out.expected=paise(x.expected.expectedPaise,'expectedPaise');
    return out;});
  const a=t?.active;
  const opener=a&&a.openedBy!=null&&a.shift.status==='Open'?{by:protoUserId(a.openedBy),date:a.shift.businessDate,at:'',
    ...(a.handoverCountPaise!=null?{handover:paise(a.handoverCountPaise,'handoverCountPaise'),difference:paise(a.handoverDifferencePaise??0,'handoverDifferencePaise')}:{}),
    ...(a.handoverReason?{reason:a.handoverReason}:{})}:undefined;
  return {desk:t?.till.code??'',deskName:t?.till.name,tills,opener};
}

/** A read the server does not have yet (404: an older server) or the role may not send (403) leaves that part empty. */
async function optional<T>(p:Promise<T>):Promise<T|null>{
  try{return await p;}catch(e){const w=e instanceof HccClientError?e.wireCode:undefined;if(w==='FORBIDDEN'||w==='NOT_FOUND')return null;throw e;}
}
async function loadCashOut(c:LoaderCtx):Promise<CXState>{
  const to=c.current('date'),from=addDays(to,-(CASHBOOK_DAYS-1));
  const list=await optional(c.api.get<SrvCashTransferList>('/cashier/transfers',{from,to}))??{businessDate:to,items:[]};
  let deskPart:Pick<CXState,'desk'|'deskName'|'tills'|'opener'>={desk:'',tills:[],opener:undefined};
  const d=c.mayRead('GET /cashier/desk')?await optional(c.api.get<SrvCashDesk>('/cashier/desk')):null;
  if(d){
    for(const x of d.tills)c.ids.learn('tillCode',x.till.code,x.till.tillId);
    deskPart=cashDeskFromServer(d,c.me.userId);
  }
  const records=list.items.map(t=>{const r=cashTransferFromServer(t);c.ids.learn('cashTransfer',r.id,t.transferId);c.ids.setVersion('cashTransfer',r.id,t.version);
    c.ids.learn('tillCode',t.fromTillCode,t.fromTillId);if(t.toTillCode&&t.toTillId!=null)c.ids.learn('tillCode',t.toTillCode,t.toTillId);return r;});
  records.sort((a,b)=>Number(a.id)-Number(b.id));
  const out:CXState={desk:deskPart.desk,tills:deskPart.tills,records};
  if(deskPart.deskName)out.deskName=deskPart.deskName;
  if(deskPart.opener)out.opener=deskPart.opener;
  return out;
}

const RELOAD:SliceKey[]=[...new Set([...CASH_SLICES,'cashOutX'] as SliceKey[])];
const invalid=(m:string)=>new HccClientError('INVALID_TRANSITION',m);
const positive=(v:unknown)=>Number.isSafeInteger(v)&&(v as number)>0;
const text=(v:unknown)=>String(v??'').trim();
function tillId(c:CommandCtx,code:string|undefined,what:string){
  const id=code?c.ids.server('tillCode',code):undefined;
  if(id===undefined)throw invalid(`${what} is not loaded yet. Refresh and try again.`);
  return id;
}
const deskId=(c:CommandCtx)=>tillId(c,cxSlice(c.state).desk||undefined,'The cash till');
function transferVersion(c:CommandCtx,id:string){
  const v=c.ids.version('cashTransfer',id);if(!v)throw new HccClientError('NOT_FOUND','This record is no longer pending. Refresh and try again.');return v;
}

export const cashier:SyncArea={
  id:'cashier',
  loaders:[{slice:'cashOutX',needs:['date'],reads:['GET /cashier/transfers'],load:loadCashOut}],
  commands:[
    {type:'SHIFT_OPEN',reload:RELOAD,
      precheck:(cmd,s,me)=>!me.permIds.includes(SHIFT_PERM)?'Opening a cash shift needs the cashier permission.':s.shiftOpen?'A shift is already open.'
        :cmd.handover!=null&&!(Number.isSafeInteger(cmd.handover)&&cmd.handover>=0)?'Enter a valid handover count.':undefined,
      async send(c){
        const till=c.ids.server('till','desk');
        if(till===undefined)throw new HccClientError('INVALID_TRANSITION','No cash till is set up yet. The owner imports the till-cash file (deployment/import-templates, README step 8), then refresh.');
        const r=await c.api.command('OpenShift',{tillId:till,openingFloatPaise:null,handoverCountPaise:c.cmd.handover??null,handoverReason:text(c.cmd.reason)||null});
        return {result:r};}},
    {type:'CASH_BANK',reload:RELOAD,
      precheck:cmd=>{if(!positive(cmd.amount))return 'Enter the amount deposited.';if(!text(cmd.bank))return 'Enter the bank and account.';
        if(!text(cmd.slip))return 'Enter the deposit-slip number.';const p=String(cmd.photo??'');if(!p)return 'Take a photo of the deposit slip.';
        if(!cxDataUrlType(p))return 'The slip photo must be a JPEG or PNG image.';if(cxDataUrlBytes(p)>CX_SLIP_MAX_BYTES)return 'The slip photo must be under 100 KB. Take it again (the app compresses it).';
        return undefined;},
      async send(c){const p=String(c.cmd.photo);
        return {result:await c.api.command('RecordCashBanking',{tillId:deskId(c),amountPaise:c.cmd.amount,bank:text(c.cmd.bank),slipNo:text(c.cmd.slip),
          slipImageBase64:p.slice(p.indexOf(',')+1),slipContentType:cxDataUrlType(p),note:text(c.cmd.note)||null})};}},
    {type:'CASH_BANK_DECIDE',reload:RELOAD,precheck:cmd=>!cmd.confirm&&!text(cmd.reason)?'A reason is required to reject a bank deposit.':undefined,
      async send(c){const id=String(c.cmd.id??'');
        return {result:await c.api.command('ConfirmCashBanking',{transferId:serverId(id,'Bank deposit'),confirm:!!c.cmd.confirm,reason:text(c.cmd.reason)||null},
          {expectedVersion:transferVersion(c,id)})};}},
    {type:'TILL_TRANSFER',reload:RELOAD,
      precheck:cmd=>!positive(cmd.amount)?'Enter the amount.':!text(cmd.to)?'Choose the till that receives the cash.':text(cmd.from)&&text(cmd.from)===text(cmd.to)?'Choose a different till to receive the cash.':undefined,
      async send(c){const from=text(c.cmd.from)||cxSlice(c.state).desk;
        return {result:await c.api.command('TransferTillCash',{fromTillId:tillId(c,from,'The cash till'),toTillId:tillId(c,text(c.cmd.to),'The receiving till'),
          amountPaise:c.cmd.amount,reason:text(c.cmd.reason)||null})};}},
    {type:'TILL_TRANSFER_DECIDE',reload:RELOAD,precheck:cmd=>!cmd.accept&&!text(cmd.reason)?'A reason is required to reject a till transfer.':undefined,
      async send(c){const id=String(c.cmd.id??'');
        return {result:await c.api.command('AcceptTillTransfer',{transferId:serverId(id,'Till transfer'),accept:!!c.cmd.accept,reason:text(c.cmd.reason)||null},
          {expectedVersion:transferVersion(c,id)})};}},
    {type:'OWNER_DRAWING',reload:RELOAD,precheck:cmd=>!positive(cmd.amount)?'Enter the amount.':!text(cmd.reason)?'Give the reason for the drawing.':undefined,
      async send(c){return {result:await c.api.command('RecordOwnerDrawing',{tillId:deskId(c),amountPaise:c.cmd.amount,reason:text(c.cmd.reason)})};}},
  ],
  unmapped:{},
};

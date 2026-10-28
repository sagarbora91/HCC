// ACC-2 · lane acc2-purchase — production sync of vendor payments, returns and the purchase fields (02 Planning/agents/ACC2-POSTING-CONTRACT.md
// §3; server Hcc.Application/Expenses/VendorPaymentCommands.cs, Hcc.Api/Endpoints/ExpenseVendorEndpoints.cs).
//
//   commands  EXP_VENDOR_PAY {vendor, amount, method, reference, date?, allocations?} → RecordVendorPayment {vendor, amountPaise, method,
//               reference, businessDate?, allocations:[{expenseId, amountPaise}]?, tillId? (cash: the till with the open shift)}
//             EXP_VENDOR_PAY_VOID {id, reason} → VoidVendorPayment {paymentId, reason, tillId? (cash)}
//             EXP_VENDOR_RETURN {expense, amount, gstPaise, reason} → RecordExpenseReturn {expenseId, amountPaise, gstPaise, reason}
//             each reloads the expense slices (a cash payment / void also the cash desk).
//   wire      EXPENSE / EXP_EDIT carry gstKind and rcmBps (accounting.ts sync spreads `expPurchaseWire`); expenseFromServer maps the purchase
//             fields back (gstKind, rcm, rcmBps, capital, assetNo, tdsPaise, tdsSection).
//   loaders   expenseVendorLoader (patch on expensesX ← GET /expenses/vendor-payments) and accountingAssetsLoader (patch on accountingX ←
//             GET /accounting/assets) are exported but NOT in the area yet: their reads need READS lines and tests/_role-reads.json from
//             the real server (integrator — ACC2-PURCHASE-DONE.md "Integration needed").
import type {Command,Expense} from '../../services/mock';
import type {EXPState,ExpVendorPayment} from '../../domain/streams/expenses-scc';
import type {ACCState,ACCAsset} from '../../domain/streams/accounting';
import {mnyIsDrawer} from '../../domain/streams/money-master';
import {HccClientError} from '../errors';
import type {SyncArea,LoaderCtx,CommandCtx,SliceLoader} from './types';
import {serverId,paise,isoDate,protoUserId,utcToClock} from './map';
import {CASH_SLICES} from './money';

const txt=(v:unknown)=>typeof v==='string'?v.trim():'';

/** GET /expenses/vendor-payments (Hcc.Contracts.Expenses.VendorPaymentView). */
export type SrvVendorPayment={paymentId:number;vendorCode:string;vendor:string;businessDate:string;amountPaise:number;method:string;reference:string;recordedBy:number;recordedUtc:string;
  allocations:{expenseId:number;amountPaise:number}[];voided:boolean;voidReason?:string|null;voidedBy?:number|null;voidedDate?:string|null};
/** GET /accounting/assets (Hcc.Contracts.Accounting.FixedAssetView). */
export type SrvFixedAsset={assetId:number;assetNo:string;ledger:string;name:string;costPaise:number;itcPaise:number;purchaseDate:string;vendor:string;expenseId:number;
  putToUse?:string|null;disposedDate?:string|null;disposedAmountPaise?:number|null;disposedVoucher?:string|null};

/** Server payment → the prototype's ExpVendorPayment (ids as strings; voidAt = the business date of the void, as the prototype keeps it). */
export function vendorPaymentFromServer(p:SrvVendorPayment):ExpVendorPayment{
  const v:ExpVendorPayment={id:String(p.paymentId),vendor:p.vendorCode||p.vendor,date:isoDate(p.businessDate,'businessDate'),amount:paise(p.amountPaise,'amountPaise'),method:p.method,
    reference:p.reference??'',by:protoUserId(p.recordedBy),at:utcToClock(p.recordedUtc),
    allocations:(p.allocations??[]).map(a=>({expense:String(a.expenseId),amount:paise(a.amountPaise,'amountPaise')}))};
  if(p.voided){v.void=true;if(p.voidReason)v.voidReason=p.voidReason;if(p.voidedBy!=null)v.voidBy=protoUserId(p.voidedBy);if(p.voidedDate)v.voidAt=isoDate(p.voidedDate,'voidedDate');}
  return v;
}
export function assetFromServer(a:SrvFixedAsset):ACCAsset{
  const x:ACCAsset={id:String(a.assetId),no:a.assetNo,ledger:a.ledger,name:a.name,cost:paise(a.costPaise,'costPaise'),date:isoDate(a.purchaseDate,'purchaseDate'),vendor:a.vendor,
    expense:String(a.expenseId),itc:paise(a.itcPaise,'itcPaise')};
  if(a.putToUse)x.putToUse=isoDate(a.putToUse,'putToUse');
  if(a.disposedDate)x.disposed={date:isoDate(a.disposedDate,'disposedDate'),amount:paise(a.disposedAmountPaise??0,'disposedAmountPaise'),voucher:a.disposedVoucher??''};
  return x;
}
/** Purchase fields of a server expense (SrvExpense gains them, all optional) → the prototype record's fields. */
export function purchaseFieldsFromServer(e:{gstKind?:string|null;rcm?:boolean|null;rcmBps?:number|null;capital?:boolean|null;assetNo?:string|null;tdsPaise?:number|null;tdsSection?:string|null}):Partial<Expense>{
  const f:Partial<Expense>={};
  if(e.gstKind==='inter'||e.gstKind==='intra')f.gstKind=e.gstKind;
  if(e.rcm){f.rcm=true;f.rcmBps=e.rcmBps??0;}
  if(e.capital)f.capital=true;if(e.assetNo)f.assetNo=e.assetNo;
  if(e.tdsPaise){f.tdsPaise=paise(e.tdsPaise,'tdsPaise');if(e.tdsSection)f.tdsSection=e.tdsSection;}
  return f;
}
/** EXPENSE / EXP_EDIT: the purchase fields the command carries (gstKind intra | inter, rcmBps). */
export const expPurchaseWire=(m:Command)=>({...(m.gstKind==='intra'||m.gstKind==='inter'?{gstKind:m.gstKind}:{}),...(Number.isSafeInteger(m.rcmBps)?{rcmBps:m.rcmBps}:{})});

/** Till with the open cash shift (as accounting.ts: ids 'till' → 'current', else the only till known). */
function currentTill(c:CommandCtx):number{
  const cur=c.ids.server('till','current');if(cur)return cur;
  const known=c.ids.list('till');if(known.length===1){const t=c.ids.server('till',known[0]);if(t)return t;}
  throw new HccClientError('VALIDATION','Open cash shift required: the till with the open shift is not known on this device yet. Open Cashier, refresh and try again.');
}
/** EXP_VENDOR_PAY body (allocations with server expense ids; omitted = the server allocates oldest first). */
export function vendorPayBody(cmd:Command,cash:boolean,till?:number):Record<string,unknown>{
  const body:Record<string,unknown>={vendor:txt(cmd.vendor),amountPaise:cmd.amount,method:txt(cmd.method),reference:txt(cmd.reference)||null};
  if(typeof cmd.date==='string'&&cmd.date)body.businessDate=cmd.date;
  if(Array.isArray(cmd.allocations))body.allocations=(cmd.allocations as {expense:string;amount:number}[]).map(a=>({expenseId:serverId(a.expense,'Bill'),amountPaise:a.amount}));
  if(cash)body.tillId=till;
  return body;
}

async function loadVendorPayments(c:LoaderCtx):Promise<EXPState>{
  const cur:EXPState={...(c.current('expensesX')??{})};
  const r=await c.api.get<{items:SrvVendorPayment[]}>('/expenses/vendor-payments');cur.vendorPayments=r.items.map(vendorPaymentFromServer);return cur;
}
async function loadAssets(c:LoaderCtx):Promise<ACCState>{
  const cur:ACCState={...(c.current('accountingX')??{})};
  const r=await c.api.get<{items:SrvFixedAsset[]}>('/accounting/assets');cur.assets=r.items.map(assetFromServer);return cur;
}
/** Not registered yet (see the header): add to `loaders` with the READS lines at integration. */
export const expenseVendorLoader:SliceLoader={slice:'expensesX',kind:'patch',requires:'expenses',load:loadVendorPayments};
export const accountingAssetsLoader:SliceLoader={slice:'accountingX',kind:'patch',requires:'reports',load:loadAssets};

export const expensesVendor:SyncArea={
  id:'expenses-vendor',
  loaders:[],
  commands:[
    {type:'EXP_VENDOR_PAY',reload:['expenses','expensesX'],
      precheck:cmd=>!txt(cmd.vendor)?'Choose a vendor.':!(Number.isSafeInteger(cmd.amount)&&cmd.amount>0)?'Enter the amount paid.':!txt(cmd.method)?'Choose how the vendor was paid.':undefined,
      async send(c){const cash=mnyIsDrawer(c.state,txt(c.cmd.method));
        await c.api.command('RecordVendorPayment',vendorPayBody(c.cmd,cash,cash?currentTill(c):undefined));
        return cash?{reload:CASH_SLICES}:undefined;}},
    {type:'EXP_VENDOR_PAY_VOID',reload:['expensesX'],precheck:cmd=>txt(cmd.reason).length>=3?undefined:'A reason is required to void a vendor payment.',
      async send(c){const p=(c.state.expensesX?.vendorPayments??[]).find(x=>x.id===String(c.cmd.id)),cash=!!p&&mnyIsDrawer(c.state,p.method);
        await c.api.command('VoidVendorPayment',{paymentId:serverId(String(c.cmd.id),'Vendor payment'),reason:txt(c.cmd.reason),...(cash?{tillId:currentTill(c)}:{})});
        return cash?{reload:CASH_SLICES}:undefined;}},
    {type:'EXP_VENDOR_RETURN',reload:['expenses','expensesX'],precheck:cmd=>txt(cmd.reason).length>=3?undefined:'A reason is required for a return.',
      async send(c){await c.api.command('RecordExpenseReturn',{expenseId:serverId(String(c.cmd.expense),'Bill'),amountPaise:c.cmd.amount,
        gstPaise:Number.isSafeInteger(c.cmd.gstPaise)?c.cmd.gstPaise:0,reason:txt(c.cmd.reason)});}},
  ],
};

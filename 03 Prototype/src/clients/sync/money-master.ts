// MA-3 money masters on the server (module MoneyMasters, migrations 2830–2831): routes, wire mappers and the area's patch loader.
// Plain object literals and functions only (no top-level calls). No runtime import of sync/masters.ts (it imports this file).
//
//   GET  /masters/payment-methods | /bank-accounts | /expense-categories | /expense-vendors   (any signed-in user: pick lists; the
//        opening balance and the PAN arrive null without PERM-MST-002) and …/history (PERM-MST-002)
//   POST /commands/{Save|Retire|Reactivate|Delete}{PaymentMethod|BankAccount|ExpenseCategory|ExpenseVendor},
//        Reorder{PaymentMethods|BankAccounts|ExpenseCategories|ExpenseVendors} (PERM-MST-002, the Owner — DEC-058)
//   loader   mastersX (patch, after the MA-0 owner) ← the four lists + their history; commands = the generic MST_MASTER_* mappers of
//            sync/masters.ts through the MST_SERVER entries below (spread into MST_SERVER).
// Prototype field ↔ wire: surchargePct (bp) ↔ surchargeBps; openingBalance (paise) ↔ openingBalancePaise; approvalLimit ↔ approvalLimitPaise.
import type {MasterRow,MSTHistory,MSTState} from '../../domain/streams/masters';
import type {SyncArea,LoaderCtx} from './types';
import type {MstServerRoute} from './masters';
import {protoId,protoUserId,utcToClock} from './map';

type Wire=Record<string,unknown>;
const MNY_EV='expenseVendors';
type SrvPage<T>={page:number;pageSize:number;total:number;items:T[]};
type SrvHistory={historyId:number;rowId:number;code:string|null;field:string;oldValue:string|null;newValue:string|null;changedBy:number;changedByName:string|null;changedUtc:string};
function str(v:unknown){return v===undefined||v===null?'':String(v).trim();}
/** The base row fields every money master shares (id = String(server id)). */
function baseRow(x:Wire,idField:string):MasterRow{
  const r:MasterRow={id:protoId(Number(x[idField])),code:String(x.code??''),name:String(x.name??''),sortOrder:Number(x.sortOrder??0),active:!!x.active};
  if(x.retiredOn)r.retiredOn=String(x.retiredOn);
  return r;
}
/** Copies a wire field that is set (no undefined keys: rows survive a JSON round trip unchanged). */
function put(r:MasterRow,k:string,v:unknown){if(v!==undefined&&v!==null&&v!=='')r[k]=v;}
function list(v:unknown){return Array.isArray(v)?(v as unknown[]).map(String):[];}

export function pmFromServer(x:Wire):MasterRow{
  const r=baseRow(x,'paymentMethodId');
  r.kind=String(x.kind??'');put(r,'settlesTo',x.settlesTo);r.needsReference=!!x.needsReference;r.usableAt=list(x.usableAt);
  if(Number(x.surchargeBps))r.surchargePct=Number(x.surchargeBps);
  put(r,'ledger',x.ledger);return r;
}
export function pmToServer(row:Wire):Wire{
  return {kind:str(row.kind),settlesTo:str(row.settlesTo)||null,needsReference:!!row.needsReference,usableAt:list(row.usableAt),
    surchargeBps:Number(row.surchargePct??0)||0,ledger:str(row.ledger)||null};
}
export function baFromServer(x:Wire):MasterRow{
  const r=baseRow(x,'bankAccountId');
  r.kind=String(x.kind??'');put(r,'bank',x.bank);put(r,'accountLast4',x.accountLast4);put(r,'ifsc',x.ifsc);
  if(Number(x.openingBalancePaise))r.openingBalance=Number(x.openingBalancePaise);
  const d=list(x.defaultFor);if(d.length)r.defaultFor=d;
  return r;
}
export function baToServer(row:Wire):Wire{
  return {kind:str(row.kind),bank:str(row.bank)||null,accountLast4:str(row.accountLast4)||null,ifsc:str(row.ifsc).toUpperCase()||null,
    openingBalancePaise:Number(row.openingBalance??0)||0,defaultFor:list(row.defaultFor)};
}
export function ecFromServer(x:Wire):MasterRow{
  const r=baseRow(x,'expenseCategoryId');
  put(r,'ledger',x.ledger);r.needsBill=!!x.needsBill;
  if(x.approvalLimitPaise!==undefined&&x.approvalLimitPaise!==null)r.approvalLimit=Number(x.approvalLimitPaise);
  // ACC-2 acc2-purchase (contract §3): purchase flags (absent when off, as the prototype rows keep them).
  if(x.itcBlocked)r.itcBlocked=true;if(x.rcm){r.rcm=true;if(x.rcmBps!=null)r.rcmBps=Number(x.rcmBps);}if(x.capital)r.capital=true;
  if(x.transport)r.transport=true; // ACC-2 integration (ruling 10): goods transport, ₹35,000 cash limit
  return r;
}
export function ecToServer(row:Wire):Wire{
  const lim=row.approvalLimit;
  return {ledger:str(row.ledger),needsBill:!!row.needsBill,approvalLimitPaise:lim===undefined||lim===null||lim===''?null:Number(lim),
    itcBlocked:!!row.itcBlocked,rcm:!!row.rcm,rcmBps:row.rcm&&row.rcmBps!=null&&row.rcmBps!==''?Number(row.rcmBps):null,capital:!!row.capital,
    transport:!!row.transport}; // ACC-2 (+ integration ruling 10)
}
export function evFromServer(x:Wire):MasterRow{
  const r=baseRow(x,'expenseVendorId');
  r.type=String(x.type??'other');put(r,'gstin',x.gstin);put(r,'pan',x.pan);r.tds=!!x.tds;put(r,'contact',x.contact);put(r,'terms',x.terms);
  put(r,'state',x.state);put(r,'tdsSection',x.tdsSection); // ACC-2 acc2-purchase
  if(x.payeeType==='individual'||x.payeeType==='other')r.payeeType=x.payeeType; // ACC-2 integration (ruling 9): the TDS rate column
  return r;
}
export function evToServer(row:Wire):Wire{
  return {type:str(row.type)||'other',gstin:str(row.gstin).toUpperCase()||null,pan:str(row.pan).toUpperCase()||null,tds:!!row.tds,
    contact:str(row.contact)||null,terms:str(row.terms)||null,state:str(row.state).toUpperCase()||null,tdsSection:str(row.tdsSection)||null, // ACC-2
    payeeType:row.payeeType==='individual'||row.payeeType==='other'?row.payeeType:null}; // ACC-2 integration (ruling 9)
}

/** The four money masters in MST_SERVER (sync/masters.ts spreads this object; ownLoader = rows come from the patch loader below). */
export const MNY_MST_SERVER:Record<string,MstServerRoute>={
  paymentMethods:{list:'GET /masters/payment-methods',history:'GET /masters/payment-methods/history',listPath:'/masters/payment-methods',historyPath:'/masters/payment-methods/history',
    kind:'paymentMethod',idField:'paymentMethodId',idsField:'paymentMethodIds',save:'SavePaymentMethod',retire:'RetirePaymentMethod',reactivate:'ReactivatePaymentMethod',
    reorder:'ReorderPaymentMethods',remove:'DeletePaymentMethod',toBody:pmToServer,ownLoader:true},
  bankAccounts:{list:'GET /masters/bank-accounts',history:'GET /masters/bank-accounts/history',listPath:'/masters/bank-accounts',historyPath:'/masters/bank-accounts/history',
    kind:'bankAccount',idField:'bankAccountId',idsField:'bankAccountIds',save:'SaveBankAccount',retire:'RetireBankAccount',reactivate:'ReactivateBankAccount',
    reorder:'ReorderBankAccounts',remove:'DeleteBankAccount',toBody:baToServer,ownLoader:true},
  expenseCategories:{list:'GET /masters/expense-categories',history:'GET /masters/expense-categories/history',listPath:'/masters/expense-categories',historyPath:'/masters/expense-categories/history',
    kind:'expenseCategory',idField:'expenseCategoryId',idsField:'expenseCategoryIds',save:'SaveExpenseCategory',retire:'RetireExpenseCategory',reactivate:'ReactivateExpenseCategory',
    reorder:'ReorderExpenseCategories',remove:'DeleteExpenseCategory',toBody:ecToServer,ownLoader:true},
  expenseVendors:{list:'GET /masters/expense-vendors',history:'GET /masters/expense-vendors/history',listPath:'/masters/expense-vendors',historyPath:'/masters/expense-vendors/history',
    kind:'expenseVendor',idField:'expenseVendorId',idsField:'expenseVendorIds',save:'SaveExpenseVendor',retire:'RetireExpenseVendor',reactivate:'ReactivateExpenseVendor',
    reorder:'ReorderExpenseVendors',remove:'DeleteExpenseVendor',toBody:evToServer,ownLoader:true},
};
const FROM:Record<string,(x:Wire)=>MasterRow>={paymentMethods:pmFromServer,bankAccounts:baFromServer,expenseCategories:ecFromServer,expenseVendors:evFromServer};

async function pages<T>(c:LoaderCtx,path:string):Promise<T[]>{
  let rows:T[]=[];
  for(let page=1;page<=50;page++){const p=await c.api.get<SrvPage<T>>(path,{page,pageSize:200});rows=rows.concat(p.items);if(!p.items.length||rows.length>=p.total)break;}
  return rows;
}
function historyOf(master:string,h:SrvHistory):MSTHistory{
  return {id:protoId(h.historyId),master,rowId:protoId(h.rowId),code:h.code??'',field:h.field,old:h.oldValue??'—',new:h.newValue??'—',by:protoUserId(h.changedBy),at:utcToClock(h.changedUtc)};
}
/** Patch: adds the money lists (and their history when the user may read it) to the mastersX the MA-0 loader built. A list the user
 *  may not read stays on the prototype seed (the old fixed values), so pick lists never go empty. */
async function loadMoneyMasters(c:LoaderCtx):Promise<MSTState>{
  const cur=c.current('mastersX');
  const out:MSTState={rows:{...(cur?.rows??{})},history:[...(cur?.history??[])]};
  for(const [master,r] of Object.entries(MNY_MST_SERVER)){
    if(!c.mayRead(r.list))continue;
    const rows=await pages<Wire>(c,r.listPath);
    for(const f of rows)c.ids.setVersion(r.kind,protoId(Number(f[r.idField])),String(f.version));
    // An empty money list (never seeded) keeps the prototype seed for methods / accounts / categories so pick lists never go empty.
    if(rows.length||master===MNY_EV)out.rows[master]=rows.map(FROM[master]);
    if(r.history&&r.historyPath&&c.mayRead(r.history))out.history.push(...(await pages<SrvHistory>(c,r.historyPath)).map(h=>historyOf(master,h)));
  }
  out.history.sort((a,b)=>a.at<b.at?1:a.at>b.at?-1:Number(b.id)-Number(a.id));
  return out;
}

export const moneyMasters:SyncArea={
  id:'money-master',
  loaders:[{slice:'mastersX',kind:'patch',reads:['GET /masters/payment-methods'],load:loadMoneyMasters}],
  commands:[],
};

// LF-ui · money commands of the LF waves in the production sync layer (contract §2–§7).
//   server  POST /commands/ChangeStayRate | SetGuestCreditAccount | RequestGuestCredit | IssueCounterBill | RequestCounterBillVoid | RequestBillCorrection
//   GUEST_RATE_SET → SetGuestAgreedRate, GUEST_RATE_CLEAR → ClearGuestAgreedRate (keyed by room-type id: roomTypeIdOf learns it from the masters slice or GET /masters/room-types).
import {HccClientError} from '../errors';
import {serverId,protoId,protoUserId,utcToClock} from './map';
import type {BLCState,BLCCorrection,CTRState,CTRBill,STRState,GuestAgreedRate} from '../../domain/types';
import type {SyncArea,SyncApi,CommandCtx} from './types';
import {mnyIsDrawer,mnyMethod} from '../../domain/streams/money-master';
import {shiftClosedText} from './money';

const text=(v:unknown)=>String(v??'').trim();
const stayOf=(c:CommandCtx,id:string)=>{const s=c.ids.server('stay',id);if(s===undefined)throw new HccClientError('VALIDATION','This stay is not in house on the server. Refresh and try again.');return s;};

/** Server room-type id for a prototype room-type code: IdDirectory, then the loaded room-type master, then one read of GET /masters/room-types. */
async function roomTypeIdOf(c:CommandCtx,code:string):Promise<number>{
  const known=c.ids.server('roomType',code);if(known!==undefined)return known;
  const row=(c.state.mastersX?.rows?.roomTypes??[]).find(x=>x.code===code);
  let id=Number(row?.id);
  if(!Number.isSafeInteger(id)||id<=0){
    const res=await c.api.get<unknown>('/masters/room-types');
    const list=(Array.isArray(res)?res:((res as {items?:unknown[]})?.items??[])) as {roomTypeId:number;code:string}[];
    id=Number(list.find(x=>x.code===code)?.roomTypeId);
  }
  if(!Number.isSafeInteger(id)||id<=0)throw new HccClientError('VALIDATION',`Room type ${code} is not on the server. Refresh and try again.`);
  c.ids.learn('roomType',code,id);return id;
}

type SrvCorrection={correctionId:number;oldInvoiceNo:string;creditNoteNo:string|null;newInvoiceNo:string|null;status:string;oldTotalPaise:number;newTotalPaise:number;
  deltaPaise:number;settlement:string|null;reason:string;requestedUtc:string};
export function billCorrectionsFromServer(rows:SrvCorrection[],bookings:{id:string;invoiceNo?:string}[]):BLCState{
  const bookingOf=(inv:string)=>bookings.find(b=>b.invoiceNo===inv)?.id??'';
  return {corrections:rows.map(r=>({id:String(r.correctionId),booking:bookingOf(r.oldInvoiceNo),oldInvoiceNo:r.oldInvoiceNo,
    ...(r.creditNoteNo?{creditNoteNo:r.creditNoteNo}:{}),...(r.newInvoiceNo?{newInvoiceNo:r.newInvoiceNo}:{}),changes:[],reason:r.reason,requestedBy:'',at:utcToClock(r.requestedUtc),
    approvalId:'',status:r.status as BLCCorrection['status'],oldTotal:r.oldTotalPaise,newTotal:r.newTotalPaise,delta:r.deltaPaise,
    ...(r.settlement?{settlement:r.settlement as NonNullable<BLCCorrection['settlement']>}:{})}))};
}

type SrvCounterLine={seq:number;label:string;extraId:string|null;menuItemId:string|null;qty:number;unitPaise:number;amountPaise:number;gstCategory:string;taxBps:number;sac:string};
type SrvCounterBill={billId:number;invoiceNo:string;businessDate:string;issuedUtc:string;issuedBy:number;partyName:string;partyPhone:string|null;partyGstin:string|null;partyAddress:string|null;
  guestId:number|null;companyId:number|null;discountPaise:number;discountReason:string|null;netPaise:number;taxPaise:number;cgstPaise:number;sgstPaise:number;totalPaise:number;remark:string|null;
  settlementMode:string;method:string|null;reference:string|null;udhaarId:number|null;status:string;creditNoteNo:string|null;creditNoteReason:string|null;voidApprovalId:number|null;lines:SrvCounterLine[]};
/** GET /counter-bills → s.counterX (newest first on the server; the prototype list is oldest first). */
export function counterBillsFromServer(rows:SrvCounterBill[]):CTRState{
  const bills:CTRBill[]=rows.map(r=>{
    const b:CTRBill={id:protoId(r.billId),no:r.invoiceNo,date:String(r.businessDate).slice(0,10),at:utcToClock(r.issuedUtc),by:protoUserId(r.issuedBy),
      party:{name:r.partyName,...(r.partyPhone?{phone:r.partyPhone}:{}),...(r.partyGstin?{gstin:r.partyGstin}:{}),...(r.partyAddress?{address:r.partyAddress}:{}),
        ...(r.guestId?{guestId:protoId(r.guestId)}:{}),...(r.companyId?{companyId:protoId(r.companyId)}:{})},
      lines:r.lines.map(l=>({id:protoId(r.billId)+'-'+l.seq,label:l.label,...(l.extraId?{extraId:l.extraId}:{}),...(l.menuItemId?{menuItemId:l.menuItemId}:{}),qty:l.qty,unit:l.unitPaise,amount:l.amountPaise,
        gstCategory:l.gstCategory,taxBps:l.taxBps,sac:l.sac})),
      ...(r.discountPaise>0?{discount:{amount:r.discountPaise,reason:r.discountReason??''}}:{}),
      net:r.netPaise,tax:r.taxPaise,cgst:r.cgstPaise,sgst:r.sgstPaise,total:r.totalPaise,...(r.remark?{remark:r.remark}:{}),
      settlement:{mode:r.settlementMode as CTRBill['settlement']['mode'],...(r.method?{method:r.method}:{}),...(r.reference?{reference:r.reference}:{}),
        ...(r.udhaarId?{udhaarId:protoId(r.udhaarId)}:{}),...(r.companyId&&r.settlementMode==='company'?{companyId:protoId(r.companyId)}:{})},
      status:r.status==='Credited'?'Credited':'Issued',...(r.creditNoteNo?{creditNoteNo:r.creditNoteNo}:{}),...(r.creditNoteReason?{creditNoteReason:r.creditNoteReason}:{}),
      ...(r.voidApprovalId?{voidApprovalId:protoId(r.voidApprovalId)}:{})};
    return b;}).reverse();
  return {bills};
}

type SrvRateChange={changeId:number;stayId:number;reservationId:number;roomNo:string;businessDate:string;changedUtc:string;changedBy:number;approvalId:number|null;approvedBy:number|null;
  scope:string;reason:string;oldRatePaise:number;newRatePaise:number;oldPeople:number;newPeople:number;oldChildren:number;newChildren:number;effectiveFrom:string;repostedJson:string};
const repostedOf=(j:string):{reversal:string;line:string;night:string}[]=>{try{const a=JSON.parse(j);return Array.isArray(a)?a.map(x=>({reversal:String(x?.reversal??''),line:String(x?.line??''),night:String(x?.night??'')})):[];}catch{return [];}};
/** GET /stays/rate-changes → s.stayRateX (applied changes, oldest first). */
export function rateChangesFromServer(rows:SrvRateChange[]):STRState{
  return {changes:rows.map(r=>({id:protoId(r.changeId),booking:protoId(r.reservationId),room:r.roomNo,date:String(r.businessDate).slice(0,10),at:utcToClock(r.changedUtc),by:protoUserId(r.changedBy),
    ...(r.approvalId?{approvalId:protoId(r.approvalId)}:{}),...(r.approvedBy?{approvedBy:protoUserId(r.approvedBy)}:{}),scope:r.scope as STRState['changes'][number]['scope'],reason:r.reason,
    oldRate:r.oldRatePaise,newRate:r.newRatePaise,oldPeople:r.oldPeople,newPeople:r.newPeople,oldChildren:r.oldChildren,newChildren:r.newChildren,effectiveFrom:String(r.effectiveFrom).slice(0,10),
    reposted:repostedOf(r.repostedJson)})).reverse()};
}

type SrvCreditGuest={guestId:number;name:string;phone:string|null;limitPaise:number;active:boolean;customerKey:string;note:string|null;openPaise:number;oldestOpenDate:string|null};
export type CreditAccountView={limit:number;active:boolean;customerKey:string;setBy:string;at:string;note?:string};
/** GET /guests/credit → the creditAccount of each guest (merged into the guests slice by the frontdesk loader). */
export function creditAccountsFromServer(rows:SrvCreditGuest[]):Map<string,CreditAccountView>{
  return new Map(rows.map(r=>[protoId(r.guestId),{limit:r.limitPaise,active:r.active,customerKey:r.customerKey,setBy:'',at:'',...(r.note?{note:r.note}:{})}]));
}
export type {GuestAgreedRate};

/** HIS-01: an older bill by invoice number and/or date range (GET /counter-bills?invoiceNo=&from=&to=); same rows as the list, oldest first. */
export async function counterBillLookup(api:SyncApi,q:{invoiceNo?:string;from?:string;to?:string}):Promise<CTRBill[]>{
  const query:Record<string,string>={};
  if(text(q.invoiceNo))query.invoiceNo=text(q.invoiceNo);if(text(q.from))query.from=text(q.from);if(text(q.to))query.to=text(q.to);
  return counterBillsFromServer(await api.get<SrvCounterBill[]>('/counter-bills',query)).bills;
}

export const lfmoney:SyncArea={
  id:'lfmoney',
  // Read side: the correction register (GET /invoices/corrections, the A2 "Bill updation report"). The list row carries totals, invoice numbers, status,
  // reason and the request time; the per-change detail, requester and approval id are on the request screen only (not in the register row).
  // Still not listed on the server: per-guest agreed rates for all guests (GET /guests/{id}/money, one guest at a time).
  // LF-finish list reads: GET /counter-bills (counterX), GET /stays/rate-changes (stayRateX), GET /guests/credit (creditAccount, merged by the guests loader in frontdesk.ts).
  loaders:[{slice:'counterX',reads:['GET /counter-bills'],async load(c){return counterBillsFromServer(await c.api.get<SrvCounterBill[]>('/counter-bills'));}},
    {slice:'stayRateX',reads:['GET /stays/rate-changes'],async load(c){return rateChangesFromServer(await c.api.get<SrvRateChange[]>('/stays/rate-changes'));}},
    {slice:'billCorrectionX',reads:['GET /invoices/corrections'],needs:['bookings'],async load(c){
    const rows=await c.api.get<SrvCorrection[]>('/invoices/corrections');
    return billCorrectionsFromServer(rows,c.current('bookings')??[]);}}],
  commands:[
    {type:'STAY_RATE_CHANGE',reload:['bookings'],async send(c){const k=c.cmd;
      await c.api.command('ChangeStayRate',{stayId:stayOf(c,text(k.booking)),ratePaise:k.rate===undefined?undefined:Number(k.rate),people:k.people===undefined?undefined:Number(k.people),
        children:k.children===undefined?undefined:Number(k.children),scope:text(k.scope),reason:text(k.reason)});}},
    {type:'GUEST_RATE_SET',reload:[],async send(c){const k=c.cmd;
      await c.api.command('SetGuestAgreedRate',{guestId:serverId(k.guestId,'Guest'),roomTypeId:await roomTypeIdOf(c,text(k.roomType)),ratePaise:Number(k.rate),
        planCode:text(k.plan)||undefined,validTo:text(k.validTo)||undefined,note:text(k.note)||undefined});}},
    {type:'GUEST_RATE_CLEAR',reload:[],async send(c){const k=c.cmd;
      await c.api.command('ClearGuestAgreedRate',{guestId:serverId(k.guestId,'Guest'),roomTypeId:await roomTypeIdOf(c,text(k.roomType))});}},
    {type:'GUEST_CREDIT_ACCOUNT_SET',reload:[],async send(c){const k=c.cmd;
      await c.api.command('SetGuestCreditAccount',{guestId:serverId(k.guestId,'Guest'),limitPaise:Number(k.limit),active:k.active!==false,note:text(k.note)||undefined});}},
    {type:'GUEST_CREDIT_REQUEST',reload:['bookings'],async send(c){const k=c.cmd;
      await c.api.command('RequestGuestCredit',{stayId:stayOf(c,text(k.booking??k.id)),reason:text(k.reason)});}},
    {type:'CTR_BILL_ISSUE',reload:[],
      // AF-CTR-UI (BIL-01 / UX-01): the method must be a front-desk row of the payment-methods master, a reference when the row needs one, and a cash (drawer)
      // method needs the open shift — the phone says so before any request. The server (CounterBillService) re-checks all of it.
      precheck(cmd,s){const k=cmd as Record<string,any>;const st=k.settlement??{}; // eslint-disable-line @typescript-eslint/no-explicit-any
        if(text(st.mode)!=='paid')return undefined;
        const method=text(st.method);if(!method)return 'Choose the payment method.';
        if(mnyMethod(s,method)?.needsReference&&!text(st.reference))return 'Enter the payment reference.';
        if(mnyIsDrawer(s,method)&&!s.shiftOpen)return shiftClosedText(s);
        return undefined;},
      async send(c){const k=c.cmd as Record<string,any>; // eslint-disable-line @typescript-eslint/no-explicit-any
      const lines=(Array.isArray(k.lines)?k.lines:[]).map((l:Record<string,unknown>)=>({label:text(l.label),qty:Number(l.qty??1),unitPaise:Number(l.unit),gstCategory:l.gstCategory?text(l.gstCategory):undefined,
        extraId:l.extraId?text(l.extraId):undefined,menuItemId:l.menuItemId?text(l.menuItemId):undefined}));
      const st=k.settlement??{};
      // BIL-01: a cash (drawer) counter bill carries the till of the open shift; the server refuses cash without one.
      let tillId:number|undefined;
      if(text(st.mode)==='paid'&&mnyIsDrawer(c.state,text(st.method))){
        tillId=c.ids.server('till','current');
        if(tillId===undefined)throw new HccClientError('INVALID_TRANSITION','The cash shift is closed. Open a cashier shift first, then issue the cash bill (or settle by UPI / card).');}
      await c.api.command('IssueCounterBill',{party:{name:text(k.party?.name),phone:text(k.party?.phone)||undefined,gstin:text(k.party?.gstin)||undefined,address:text(k.party?.address)||undefined,
        guestId:k.party?.guestId?serverId(k.party.guestId,'Guest'):undefined},lines,settlement:{mode:text(st.mode),method:text(st.method)||undefined,reference:text(st.reference)||undefined,tillId,
        companyId:st.companyId?serverId(st.companyId,'Company'):undefined},discountPaise:Number(k.discount?.amount??0),discountReason:text(k.discount?.reason)||undefined,remark:text(k.remark)||undefined});}},
    {type:'CTR_BILL_VOID_REQUEST',reload:[],async send(c){await c.api.command('RequestCounterBillVoid',{billId:serverId(c.cmd.id,'CounterBill'),reason:text(c.cmd.reason)});}},
    {type:'BILL_CORRECTION_REQUEST',reload:['bookings'],async send(c){const k=c.cmd;const b=c.state.bookings.find(x=>x.id===text(k.booking));
      if(!b?.invoiceNo)throw new HccClientError('VALIDATION','This stay has no invoice to correct.');
      const changes=(Array.isArray(k.changes)?k.changes:[]).map((x:Record<string,unknown>)=>x.kind==='room-rate'?{kind:'room-rate',night:x.night,newRatePaise:Number(x.newRate)}
        :x.kind==='remove-line'?{kind:'remove-line',chargeId:serverId(x.lineId as string,'Charge')}:{kind:'add-line',label:x.label,amountPaise:Number(x.amount),gstCategory:x.gstCategory,serviceDate:x.date});
      await c.api.command('RequestBillCorrection',{invoiceNo:b.invoiceNo,changes,reason:text(k.reason)});}},
  ],
};

// LF-ui · money commands of the LF waves in the production sync layer (contract §2–§7).
//   server  POST /commands/ChangeStayRate | SetGuestCreditAccount | RequestGuestCredit | IssueCounterBill | RequestCounterBillVoid | RequestBillCorrection
//   GUEST_RATE_SET → SetGuestAgreedRate, GUEST_RATE_CLEAR → ClearGuestAgreedRate (keyed by room-type id: roomTypeIdOf learns it from the masters slice or GET /masters/room-types).
import {HccClientError} from '../errors';
import {serverId,utcToClock} from './map';
import type {BLCState,BLCCorrection} from '../../domain/types';
import type {SyncArea,CommandCtx} from './types';

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

export const lfmoney:SyncArea={
  id:'lfmoney',
  // Read side: the correction register (GET /invoices/corrections, the A2 "Bill updation report"). The list row carries totals, invoice numbers, status,
  // reason and the request time; the per-change detail, requester and approval id are on the request screen only (not in the register row).
  // Not readable yet (the server has no list route): counter bills, credit-guest ageing, stay rate-change history, per-guest agreed rates for all guests.
  loaders:[{slice:'billCorrectionX',reads:['GET /invoices/corrections'],needs:['bookings'],async load(c){
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
    {type:'CTR_BILL_ISSUE',reload:[],async send(c){const k=c.cmd as Record<string,any>; // eslint-disable-line @typescript-eslint/no-explicit-any
      const lines=(Array.isArray(k.lines)?k.lines:[]).map((l:Record<string,unknown>)=>({label:text(l.label),qty:Number(l.qty??1),unitPaise:Number(l.unit),gstCategory:l.gstCategory?text(l.gstCategory):undefined,
        extraId:l.extraId?text(l.extraId):undefined,menuItemId:l.menuItemId?text(l.menuItemId):undefined}));
      const st=k.settlement??{};
      await c.api.command('IssueCounterBill',{party:{name:text(k.party?.name),phone:text(k.party?.phone)||undefined,gstin:text(k.party?.gstin)||undefined,address:text(k.party?.address)||undefined,
        guestId:k.party?.guestId?serverId(k.party.guestId,'Guest'):undefined},lines,settlement:{mode:text(st.mode),method:text(st.method)||undefined,reference:text(st.reference)||undefined,
        companyId:st.companyId?serverId(st.companyId,'Company'):undefined},discountPaise:Number(k.discount?.amount??0),discountReason:text(k.discount?.reason)||undefined,remark:text(k.remark)||undefined});}},
    {type:'CTR_BILL_VOID_REQUEST',reload:[],async send(c){await c.api.command('RequestCounterBillVoid',{billId:serverId(c.cmd.id,'CounterBill'),reason:text(c.cmd.reason)});}},
    {type:'BILL_CORRECTION_REQUEST',reload:['bookings'],async send(c){const k=c.cmd;const b=c.state.bookings.find(x=>x.id===text(k.booking));
      if(!b?.invoiceNo)throw new HccClientError('VALIDATION','This stay has no invoice to correct.');
      const changes=(Array.isArray(k.changes)?k.changes:[]).map((x:Record<string,unknown>)=>x.kind==='room-rate'?{kind:'room-rate',night:x.night,newRatePaise:Number(x.newRate)}
        :x.kind==='remove-line'?{kind:'remove-line',chargeId:serverId(x.lineId as string,'Charge')}:{kind:'add-line',label:x.label,amountPaise:Number(x.amount),gstCategory:x.gstCategory,serviceDate:x.date});
      await c.api.command('RequestBillCorrection',{invoiceNo:b.invoiceNo,changes,reason:text(k.reason)});}},
  ],
};

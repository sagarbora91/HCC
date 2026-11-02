// LF-ui · money commands of the LF waves in the production sync layer (contract §2–§7).
//   server  POST /commands/ChangeStayRate | SetGuestCreditAccount | RequestGuestCredit | IssueCounterBill | RequestCounterBillVoid | RequestBillCorrection
//   unmapped  GUEST_RATE_SET / GUEST_RATE_CLEAR: the server keys an agreed rate by room-type id, which no loader of this client holds yet.
import {HccClientError} from '../errors';
import {serverId} from './map';
import type {SyncArea,CommandCtx} from './types';

const text=(v:unknown)=>String(v??'').trim();
const stayOf=(c:CommandCtx,id:string)=>{const s=c.ids.server('stay',id);if(s===undefined)throw new HccClientError('VALIDATION','This stay is not in house on the server. Refresh and try again.');return s;};

export const lfmoney:SyncArea={
  id:'lfmoney',
  loaders:[],
  commands:[
    {type:'STAY_RATE_CHANGE',reload:['bookings'],async send(c){const k=c.cmd;
      await c.api.command('ChangeStayRate',{stayId:stayOf(c,text(k.booking)),ratePaise:k.rate===undefined?undefined:Number(k.rate),people:k.people===undefined?undefined:Number(k.people),
        children:k.children===undefined?undefined:Number(k.children),scope:text(k.scope),reason:text(k.reason)});}},
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
  unmapped:{GUEST_RATE_SET:'Server keys the agreed rate by room-type id; needs a room-type loader (carried).',GUEST_RATE_CLEAR:'As GUEST_RATE_SET.'},
};

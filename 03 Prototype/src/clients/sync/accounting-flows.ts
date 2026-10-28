// ACC-2 lane acc2-flows · production sync (server: Hcc.Api/Endpoints/OtaPayoutEndpoints.cs; contract §4, §8). Plain object literals and
// functions only (no top-level calls).
//
//   commands  DEPOSIT_APPLY {booking, amount}                 → ApplyDeposit {folioId, amountPaise, tillId}         (PERM-PAY-001, open shift)
//             CHN_PAYOUT {channel, statementRef, gross, …}    → RecordOtaPayout {channelId, statementRef, grossPaise, commissionPaise,
//                                                                commissionGstPaise, tcsPaise, netPaise, bankAccount, reservationIds}
//                                                                (PERM-OTA-004)
//             EXP_UD_PAY_REVERSE {receivable, settlement, reason} → ReverseUdhaarPayment {receivableId, settlementId, reason, tillId?}
//                                                                (Owner)
//   loader    OTA_PAYOUTS_LOADER (patch on channelsX ← GET /ota/payouts, PERM-OTA-004) fills `channelsX.payouts`; registered at the ACC-2
//             integration (READS line in sync/reads.ts, routes in tests/_role-reads.json — see ACC2-FLOWS-DONE.md "Integration needed").
//   CHECKOUT {otaCredit} and GRP_COMPANY_SAVE {active} belong to the front-desk / money mappers (W4-1 CheckoutStay, W4-3 SaveCompanyAccount):
//   the body fields are listed in the DONE file.
import type {CHNPayout,CHNState} from '../../domain/streams/channels';
import {mnyIsDrawer} from '../../domain/streams/money-master';
import {HccClientError} from '../errors';
import type {SyncArea,SliceLoader,LoaderCtx,CommandCtx} from './types';
import {protoId,protoUserId,serverId,utcToClock,paise,isoDate} from './map';

/** GET /ota/payouts (Hcc.Contracts.Ota.OtaPayoutView). */
export type SrvOtaPayout={payoutId:number;channelId:number;businessDate:string;statementRef:string;grossPaise:number;commissionPaise:number;commissionGstPaise:number;
  tcsPaise:number;netPaise:number;bankAccount:string;reservationIds:number[];recordedBy:number;recordedUtc:string};

export function payoutFromServer(p:SrvOtaPayout):CHNPayout{
  return {id:protoId(p.payoutId),channel:protoId(p.channelId),date:isoDate(p.businessDate,'businessDate'),statementRef:p.statementRef,gross:paise(p.grossPaise,'grossPaise'),
    commission:paise(p.commissionPaise,'commissionPaise'),commissionGst:paise(p.commissionGstPaise,'commissionGstPaise'),tcs:paise(p.tcsPaise,'tcsPaise'),net:paise(p.netPaise,'netPaise'),
    bank:p.bankAccount,bookings:(p.reservationIds??[]).map(protoId),by:protoUserId(p.recordedBy),at:utcToClock(p.recordedUtc)};
}
const txt=(v:unknown)=>v===undefined||v===null?'':String(v).trim();
const int=(v:unknown,field:string)=>{if(!Number.isSafeInteger(v)||(v as number)<0)throw new HccClientError('VALIDATION',`${field}: enter an amount of ₹0 or more.`);return v as number;};

/** RecordOtaPayout body from the prototype command (amounts in paise; ids are server ids). */
export function payoutBody(cmd:Record<string,unknown>){
  const ref=txt(cmd.statementRef);if(!ref)throw new HccClientError('VALIDATION','Enter the OTA statement reference.');
  return {channelId:serverId(cmd.channel as string,'Channel'),statementRef:ref,grossPaise:int(cmd.gross,'Gross'),commissionPaise:int(cmd.commission,'Commission'),
    commissionGstPaise:int(cmd.commissionGst,'GST on the commission'),tcsPaise:int(cmd.tcs,'TCS'),netPaise:int(cmd.net,'Net received'),bankAccount:txt(cmd.bank),
    reservationIds:(Array.isArray(cmd.bookings)?cmd.bookings:[]).map(b=>serverId(b as string,'Booking'))};
}
/** The open desk till (cash leaving / entering the drawer), null when the method is not drawer cash. */
function till(c:CommandCtx,method:string){
  if(!mnyIsDrawer(c.state,method))return null;
  const t=c.ids.server('till','desk');if(t===undefined)throw new HccClientError('INVALID_TRANSITION','The cash shift is closed. Open a cashier shift first.');return t;
}

async function loadPayouts(c:LoaderCtx):Promise<CHNState>{
  const cur=c.current('channelsX') as CHNState;
  const rows=await c.api.get<SrvOtaPayout[]>('/ota/payouts');
  return {...cur,payouts:rows.slice().sort((a,b)=>a.payoutId-b.payoutId).map(payoutFromServer)};
}
/** Patch loader for channelsX.payouts (registered at the ACC-2 integration with its READS line). */
export const OTA_PAYOUTS_LOADER:SliceLoader={slice:'channelsX',kind:'patch',requires:['payments','reports'],load:loadPayouts};

export const accountingFlows:SyncArea={
  id:'accounting-flows',
  loaders:[],
  commands:[
    {type:'DEPOSIT_APPLY',reload:['bookings'],
      precheck:cmd=>Number.isSafeInteger(cmd.amount)&&(cmd.amount as number)>0?undefined:'Enter an amount greater than zero.',
      async send(c){
        const id=txt(c.cmd.booking??c.cmd.id),folioId=c.ids.server('folio',id);
        if(folioId===undefined)throw new HccClientError('INVALID_TRANSITION','This booking has no folio on the server yet — it opens at check-in. Refresh and try again.');
        await c.api.command('ApplyDeposit',{folioId,amountPaise:c.cmd.amount,expectedVersion:c.ids.version('folio',id)??null});}},
    // channelsX is owned by the W4-11 channels area (absent when the OTA module's area is left out): the reload is returned, not declared.
    {type:'CHN_PAYOUT',reload:[],
      async send(c){await c.api.command('RecordOtaPayout',payoutBody(c.cmd));return {reload:['channelsX']};}},
    {type:'EXP_UD_PAY_REVERSE',reload:['expensesX'],
      precheck:cmd=>txt(cmd.reason).length>=3?undefined:'Give the reason for the reversal (at least 3 characters).',
      async send(c){
        const r=(c.state.expensesX?.udhaar??[]).find(u=>u.id===txt(c.cmd.receivable)),p=r?.settlements.find(z=>z.id===txt(c.cmd.settlement));
        if(!r||!p)throw new HccClientError('NOT_FOUND','Payment not found on this credit. Refresh and try again.');
        const t=p.desk?till(c,p.mode):null;
        await c.api.command('ReverseUdhaarPayment',{receivableId:serverId(r.id,'Credit'),settlementId:serverId(p.id,'Payment'),reason:txt(c.cmd.reason),...(t!==null?{tillId:t}:{})});
        return t!==null?{reload:['shift','shiftClose','openingCash','cashbookX']}:undefined;}},
  ],
};

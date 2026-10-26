// Wave B1 · Cash leaving the drawer (DEC-073) and the shared-till handover (DEC-078); audit A03b-01 / A03b-06. Exported names start with
// 'cx' / 'CX' (shared mock namespace). State slice s.cashOutX (optional; absent in old saves → no records, the demo's second till only).
// Same rules as the server (08 Production CashOutService / CashOutRules):
//   • bank deposit: Cashier or Manager, from their own open shift, slip number + slip photo (compressed on the device to under 100 KB);
//     expected cash drops when it is RECORDED (the cash has left the drawer); a DIFFERENT Manager or Owner confirms it, or rejects it with a
//     reason — the cash then goes back into the drawer (a banking line with the opposite sign, dated the rejection's business date);
//   • till transfer: the giver records it from their own open shift (giver's expected drops), the receiver accepts it on their own open shift
//     on the receiving till (receiver's expected rises) or sends it back; a pending transfer holds up the day close, a pending deposit does not;
//   • Owner drawing: Owner only, from an open shift, reason required; nothing may take the drawer below zero; nothing on a locked day (DEC-065);
//   • DEC-078: the shift belongs to whoever opened it (`opener`); taking over a drawer someone else counted needs a handover count, and a count
//     that differs from the carried float needs a reason (flagged; the float stays the counted cash, DEC-035).
// The demo has one drawer (the front desk, `desk`) plus one other till for transfers; the server has as many tills as the hotel creates.
import type {State} from '../types';
import {isProductionBuild} from '../build';

export type CXKind='banking'|'till'|'drawing';
export type CXStatus='Pending'|'Confirmed'|'Accepted'|'Rejected'|'Posted';
export type CXRecord={id:string;kind:CXKind;status:CXStatus;amount:number;
  /** Business date it was recorded (the cash left the drawer that day). */ date:string;actor:string;at:string;
  fromTill:string;toTill?:string;bank?:string;slip?:string;reason?:string;
  /** Demo build only: the compressed slip photo as a data URL (production keeps it on the server; viewed on demand). */ photo?:string;
  /** The slip photo is still kept (false once purged after 600 days). */ slipStored?:boolean;
  /** "Slip photo purged after 600 days." once purged. */ slipNote?:string;
  decidedBy?:string;decidedAt?:string;
  /** Business date of the decision (a rejection's return and an acceptance are dated it). */ decidedDate?:string;decisionReason?:string;
  /** Production: the server approval request behind a deposit / transfer (DecideApproval). */ approvalId?:string};
export type CXTill={code:string;name:string;shiftOpen:boolean;openedBy?:string;
  /** Expected cash of that till's open shift (production: from GET /cashier/desk; demo: kept by the mock commands). */ expected?:number};
export type CXOpener={by:string;date:string;at:string;handover?:number;difference?:number;reason?:string};
/** desk = the till the cashier screens show (the demo's one shared drawer); tills = the OTHER tills (transfer partners). */
export type CXState={desk:string;deskName?:string;tills:CXTill[];records:CXRecord[];opener?:CXOpener};

export const CX_DESK='DESK';
/** 100 KB = 102,400 bytes; a slip photo must be under it (server CashOutRules.SlipMaxBytes = 102,399). */
export const CX_SLIP_MAX_BYTES=102399;
export const CX_SLIP_RETENTION_DAYS=600;
export const CX_SLIP_PURGED='Slip photo purged after 600 days.';
/** Roles (prototype) per server grant: PERM-DRW-001 record (CSH, MGR · own shift), -002 confirm (OWN, MGR), -003 view slip (OWN, MGR, ACC),
 *  -004 till transfer (cash-shift holders · own shift), -005 Owner drawing (OWN). */
export const CX_BANK_ROLES=['Cashier','Manager'];
export const CX_CONFIRM_ROLES=['Owner','Manager'];
export const CX_SLIP_VIEW_ROLES=['Owner','Manager','Accountant'];
export const CX_TRANSFER_ROLES=['Owner','Manager','Reception','Cashier'];
export const CX_DRAWING_ROLES=['Owner'];

/** The slice, or (absent: old saves and BASE/FY26) the demo's second till in the prototype build and nothing in the production build. Read-only:
 *  handlers take a private copy (services/mock/handlers/cashout.ts). */
export function cxSlice(s:State):CXState{
  const x=s.cashOutX as Partial<CXState>|undefined;
  const d=x||isProductionBuild()?undefined:CX_DEMO_STATE;
  return {desk:x?.desk||CX_DESK,deskName:x?.deskName??d?.deskName,tills:x?.tills??d?.tills??[],records:x?.records??[],opener:x?.opener};
}
const roleOf=(s:State,uid:string)=>s.users.find(u=>u.id===uid)?.role;
const hasRole=(s:State,uid:string,roles:string[])=>{const r=roleOf(s,uid);return !!r&&roles.includes(r);};
export const cxCanBank=(s:State,uid:string)=>hasRole(s,uid,CX_BANK_ROLES);
export const cxCanConfirm=(s:State,uid:string)=>hasRole(s,uid,CX_CONFIRM_ROLES);
export const cxCanViewSlip=(s:State,uid:string)=>hasRole(s,uid,CX_SLIP_VIEW_ROLES);
export const cxCanTransfer=(s:State,uid:string)=>hasRole(s,uid,CX_TRANSFER_ROLES);
export const cxCanDraw=(s:State,uid:string)=>hasRole(s,uid,CX_DRAWING_ROLES);

/** DEC-078: the open desk shift is `uid`'s own (or its opener is not known: a shift opened before the rule, shared). */
export const cxOwnsDesk=(s:State,uid:string)=>{const o=cxSlice(s).opener;return !o||o.date!==s.date||o.by===uid;};
/** Why `uid` cannot take or move CASH on the desk shift now (undefined = allowed). Reception / Cashier need their own open shift (S scope). */
export function cxCashBlocked(s:State,uid:string):string|undefined{
  if(!s.shiftOpen)return undefined; // the callers' own "shift closed" message applies
  const r=roleOf(s,uid);if(r!=='Reception'&&r!=='Cashier')return undefined;
  return cxOwnsDesk(s,uid)?undefined:'Cash needs your own open cashier shift — use UPI/card or ask the cashier.';
}

export type CXMovement={id:string;kind:string;label:string;reference:string;amount:number;actor:string};
/** Signed desk-drawer movements of business date `d` (+ in, − out): what each record did to the desk till that day. */
export function cxDeskMovements(s:State,d:string):CXMovement[]{
  const x=cxSlice(s),out:CXMovement[]=[];
  for(const r of x.records){
    if(r.fromTill===x.desk&&r.date===d){
      if(r.kind==='banking')out.push({id:r.id,kind:'banking',label:`Bank deposit · ${r.bank??''} · slip ${r.slip??''}`,reference:'Slip '+(r.slip??''),amount:-r.amount,actor:r.actor});
      else if(r.kind==='till')out.push({id:r.id,kind:'transfer-out',label:`Till transfer to ${cxTillName(s,r.toTill)}`,reference:r.reason||'Till transfer',amount:-r.amount,actor:r.actor});
      else out.push({id:r.id,kind:'owner-drawing',label:'Owner drawing · '+(r.reason??''),reference:r.reason??'',amount:-r.amount,actor:r.actor});
    }
    if(r.status==='Rejected'&&r.fromTill===x.desk&&r.decidedDate===d)
      out.push(r.kind==='banking'
        ?{id:r.id+':ret',kind:'banking',label:`Bank deposit rejected · cash back in the drawer · slip ${r.slip??''}`,reference:r.decisionReason??'',amount:r.amount,actor:r.decidedBy??''}
        :{id:r.id+':ret',kind:'transfer-out',label:`Till transfer returned · ${r.id}`,reference:r.decisionReason??'',amount:r.amount,actor:r.decidedBy??''});
    if(r.kind==='till'&&r.status==='Accepted'&&r.toTill===x.desk&&r.decidedDate===d)
      out.push({id:r.id+':in',kind:'transfer-in',label:`Till transfer received from ${cxTillName(s,r.fromTill)}`,reference:r.reason||'Till transfer',amount:r.amount,actor:r.decidedBy??''});
  }
  return out;
}
/** Net desk-drawer effect of the cash-out records on business date `d` (part of drawer(s) and of expected cash). */
export const cxDeskNet=(s:State,d:string)=>cxDeskMovements(s,d).reduce((a,m)=>a+m.amount,0);
/** Bank deposits recorded on `d` net of rejections dated `d` (the cash book's "banked during the day"). */
export const cxDeposited=(s:State,d:string)=>-cxDeskMovements(s,d).filter(m=>m.kind==='banking').reduce((a,m)=>a+m.amount,0);
export function cxTillName(s:State,code?:string){const x=cxSlice(s);if(!code)return '—';if(code===x.desk)return x.deskName||'Front desk';return x.tills.find(t=>t.code===code)?.name??code;}

/** Records that still need someone: deposits awaiting confirmation, transfers awaiting the receiver. */
export const cxPending=(s:State)=>cxSlice(s).records.filter(r=>r.status==='Pending');
export const cxPendingTransfers=(s:State)=>cxPending(s).filter(r=>r.kind==='till');
/** Who can decide a pending record now (undefined = `uid` can), with the reason when not. */
export function cxDecideBlocked(s:State,uid:string,r:CXRecord,approve:boolean):string|undefined{
  if(r.status!=='Pending')return 'Already decided.';
  if(r.actor===uid)return r.kind==='banking'?'A different Manager or Owner confirms a bank deposit (you recorded it).':'The receiver accepts a till transfer, not the giver.';
  const x=cxSlice(s);
  if(r.kind==='banking'){
    if(!cxCanConfirm(s,uid))return 'A Manager or Owner confirms bank deposits.';
    if(!approve&&r.fromTill===x.desk&&!s.shiftOpen)return 'Open the cashier shift first: a rejected deposit’s cash goes back into the drawer.';
    return undefined;
  }
  // till transfer
  const intoDesk=r.toTill===x.desk;
  const receiverHere=intoDesk?s.shiftOpen&&cxOwnsDesk(s,uid)&&cxCanTransfer(s,uid):x.tills.some(t=>t.code===r.toTill&&t.shiftOpen&&t.openedBy===uid);
  if(approve)return receiverHere?undefined:`Only the receiver accepts it, on their own open shift on ${cxTillName(s,r.toTill)}.`;
  if(!receiverHere&&!cxCanConfirm(s,uid))return 'Only the receiver, or a Manager or Owner, can send it back.';
  if(r.fromTill===x.desk&&!s.shiftOpen)return 'Open the cashier shift first: the returned cash goes back into the drawer.';
  return undefined;
}
/** Decoded size in bytes of a base64 data URL (padding aware). */
export function cxDataUrlBytes(dataUrl:string):number{
  const i=dataUrl.indexOf(',');const b64=i<0?dataUrl:dataUrl.slice(i+1);
  const pad=b64.endsWith('==')?2:b64.endsWith('=')?1:0;return Math.floor(b64.length*3/4)-pad;
}
/** The content type of a slip photo data URL: image/jpeg or image/png, else undefined. */
export function cxDataUrlType(dataUrl:string):'image/jpeg'|'image/png'|undefined{
  const m=/^data:(image\/(?:jpeg|png));base64,/.exec(dataUrl);return m?m[1] as 'image/jpeg'|'image/png':undefined;
}
/** Demo second till for transfers (prototype build, slice absent): the Manager's back-office cash, shift open, ₹10,000 expected. */
export const CX_DEMO_STATE:CXState={desk:CX_DESK,deskName:'Front desk',tills:[{code:'BACK',name:'Back office cash',shiftOpen:true,openedBy:'manager',expected:1000000}],records:[]};

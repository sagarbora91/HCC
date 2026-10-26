// Wave B1 · Cash leaving the drawer (DEC-073) and the shared-till handover (DEC-078): SHIFT_OPEN, CASH_BANK, CASH_BANK_DECIDE, TILL_TRANSFER,
// TILL_TRANSFER_DECIDE, OWNER_DRAWING. Same rules and wording as the server (08 Production CashOutService, CashierService.OpenShift); the
// figures are derived in domain/streams/cashout.ts (drawer(s), shiftMovements, cash book). The demo keeps one cash shift per business day.
import type {Handler,Kit} from '../commands';
import {money} from '../../../domain/money';
import {clone} from '../../../domain/clone';
import {drawer} from '../../../domain/selectors';
import {cbIsLocked,cbLockedMessage} from '../../../domain/streams/cashbook';
import {cxSlice,cxCanBank,cxCanConfirm,cxCanTransfer,cxCanDraw,cxOwnsDesk,cxDataUrlBytes,cxDataUrlType,cxTillName,CX_SLIP_MAX_BYTES,
  type CXRecord,type CXState} from '../../../domain/streams/cashout';

const text=(v:unknown)=>String(v??'').trim();
const amountOf=(k:Kit)=>{const a=k.cmd.amount;k.assert(Number.isSafeInteger(a)&&a>0&&a<=1_000_000_000,'Amount must be more than ₹0 and at most ₹10 crore.');return a as number;};
/** The slice as a private, mutable copy: an existing s.cashOutX is already the draft's own copy (copy-on-write); the default (absent slice)
 *  shares the demo constants, so it is cloned once and stored. */
const slice=(k:Kit):CXState=>{const had=!!k.s.cashOutX,x=cxSlice(k.s);k.s.cashOutX=had?x:clone(x);return k.s.cashOutX;};
const covered=(k:Kit,available:number,amount:number,what:string)=>
  k.assert(amount<=available,`${what} of ${money(amount)} would take the drawer below zero: the expected cash is ${money(available)}.`,'INVALID_TRANSITION');
const notLocked=(k:Kit,date:string,what:string)=>k.assert(!cbIsLocked(k.s,date),cbLockedMessage(date,what),'DAY_CLOSED'); // DEC-065
/** The desk shift must be open and (DEC-078) the caller's own: what PERM-DRW-001 / -004 with scope S mean on the server. */
const ownDesk=(k:Kit)=>{k.assert(k.s.shiftOpen,'No cash shift is open on this till. Open a shift first.','INVALID_TRANSITION');
  if(!cxOwnsDesk(k.s,k.uid))k.fail('FORBIDDEN','Access denied: this is not your own open shift (DEC-078: the shift belongs to whoever opened it).');};
const find=(k:Kit,kind:CXRecord['kind'])=>{const r=slice(k).records.find(x=>x.id===String(k.cmd.id??'')&&x.kind===kind);
  k.assert(r,kind==='banking'?'Bank deposit not found.':'Till transfer not found.','NOT_FOUND');return r!;};

export const handlers:Record<string,Handler>={
  // PERM-SHF-001 · DEC-078: open your own shift; taking over a drawer someone else counted needs your handover count (a difference needs a reason).
  SHIFT_OPEN:k=>{
    const {s,cmd,uid,req,open,assert,fail}=k;req('payments');open();
    assert(!s.shiftOpen,'A shift is already open on this till.','INVALID_TRANSITION');
    assert(!(s.shift?.date===s.date&&s.shift.status==='Submitted'),'The last count on this till is awaiting difference approval.','INVALID_TRANSITION');
    if(s.shiftClose)fail('INVALID_TRANSITION','Today’s shift is already counted. The demo keeps one cash shift per business day: open the next business day.');
    const prevDate=[...s.closedDates].filter(d=>d<s.date).sort().pop();
    const counter=prevDate?s.closings?.[prevDate]?.cash.countedBy:undefined;
    let handover:number|undefined,reason='';
    if(cmd.handover!==undefined&&cmd.handover!==null&&cmd.handover!==''){
      assert(Number.isSafeInteger(cmd.handover)&&cmd.handover>=0&&cmd.handover<=1_000_000_000,'Enter a valid handover count.');
      handover=cmd.handover as number;reason=text(cmd.reason).slice(0,200);
      assert(handover===s.openingCash||reason,`Your handover count differs from the cash counted at the last close (${money(s.openingCash)}). Give a reason.`);
    }else if(counter!==uid)fail('VALIDATION',`Count the drawer you are taking over and enter the handover count (the last close counted ${money(s.openingCash)}).`);
    const x=slice(k);x.opener={by:uid,date:s.date,at:s.clock,...(handover!==undefined?{handover,difference:handover-s.openingCash}:{}),...(reason?{reason}:{})};
    s.shiftOpen=true;
    const diff=handover===undefined?0:handover-s.openingCash;
    return `Shift opened · float ${money(s.openingCash)}`+(handover===undefined?'':diff===0?` · handover count ${money(handover)} (matches)`:` · handover count ${money(handover)}, difference ${money(diff)} · ${reason}`);
  },
  // PERM-DRW-001 · bank deposit from your own open shift: slip number + slip photo under 100 KB; expected cash drops now; a different person confirms.
  CASH_BANK:k=>{
    const {s,cmd,uid,open,assert,fail,id}=k;open();
    if(!cxCanBank(s,uid))fail('FORBIDDEN','Access denied: a Cashier or Manager records bank deposits.');
    ownDesk(k);notLocked(k,s.date,'a bank deposit');
    const amount=amountOf(k),bank=text(cmd.bank),slip=text(cmd.slip),photo=String(cmd.photo??'');
    assert(bank&&bank.length<=100,'Enter the bank and account the cash was deposited into.');
    assert(slip&&slip.length<=40,'Enter the deposit-slip number.');
    assert(/^[A-Za-z0-9 /-]+$/.test(slip),'A deposit-slip number uses letters, digits, spaces, \'-\' and \'/\' only.');
    assert(photo,'Take a photo of the deposit slip.');
    assert(cxDataUrlType(photo),'The slip photo must be a JPEG or PNG image.');
    const bytes=cxDataUrlBytes(photo);assert(bytes>0&&bytes<=CX_SLIP_MAX_BYTES,'The slip photo must be under 100 KB. Take it again (the app compresses it).');
    covered(k,drawer(s),amount,'A bank deposit');
    const r:CXRecord={id:id('BNK'),kind:'banking',status:'Pending',amount,date:s.date,actor:uid,at:s.clock,fromTill:slice(k).desk,bank,slip,photo,slipStored:true};
    const note=text(cmd.note);if(note)r.reason=note.slice(0,200);
    slice(k).records.push(r);
    return `${r.id} · bank deposit ${money(amount)} · ${bank} · slip ${slip} · confirmation by a different Manager or Owner pending`;
  },
  // PERM-DRW-002 · a DIFFERENT Manager or Owner confirms, or rejects with a reason (the cash goes back into the drawer).
  CASH_BANK_DECIDE:k=>{
    const {s,cmd,uid,open,assert,fail}=k;open();
    if(!cxCanConfirm(s,uid))fail('FORBIDDEN','Access denied: a Manager or Owner confirms bank deposits.');
    const r=find(k,'banking');const confirm=!!cmd.confirm,reason=text(cmd.reason);
    if(!confirm)assert(reason,'A reason is required to reject a bank deposit.');
    notLocked(k,r.date,(confirm?'confirming':'rejecting')+' this bank deposit');
    if(!confirm&&r.fromTill===slice(k).desk)assert(s.shiftOpen,`Open a shift on till ${cxTillName(s,r.fromTill)} first: a rejected deposit's cash goes back into that drawer.`,'INVALID_TRANSITION');
    assert(r.status==='Pending','This bank deposit has already been decided.','INVALID_TRANSITION');
    if(r.actor===uid)fail('FORBIDDEN','A different Manager or Owner must confirm a bank deposit (maker-checker): you recorded this one.');
    r.status=confirm?'Confirmed':'Rejected';r.decidedBy=uid;r.decidedAt=s.clock;r.decidedDate=s.date;if(reason)r.decisionReason=reason.slice(0,200);
    return confirm?`${r.id} · bank deposit ${money(r.amount)} confirmed`:`${r.id} · bank deposit rejected · ${money(r.amount)} back in the drawer · ${reason}`;
  },
  // PERM-DRW-004 · the giver moves cash from their own open shift to another till; it waits for the receiver.
  TILL_TRANSFER:k=>{
    const {s,cmd,uid,open,assert,fail,id}=k;open();
    if(!cxCanTransfer(s,uid))fail('FORBIDDEN','Access denied: a till transfer is given from your own cash shift.');
    const x=slice(k),from=text(cmd.from)||x.desk,to=text(cmd.to);
    assert(to&&to!==from,'Choose a different till to receive the cash.');
    assert(to===x.desk||x.tills.some(t=>t.code===to),'Till not found.','NOT_FOUND');
    notLocked(k,s.date,'a till transfer');
    const amount=amountOf(k),reason=text(cmd.reason).slice(0,200);
    if(from===x.desk){ownDesk(k);covered(k,drawer(s),amount,'A till transfer');}
    else{const t=x.tills.find(z=>z.code===from);assert(t,'Till not found.','NOT_FOUND');
      assert(t!.shiftOpen,'No cash shift is open on this till. Open a shift first.','INVALID_TRANSITION');
      if(t!.openedBy!==uid)fail('FORBIDDEN','Access denied: this is not your own open shift (DEC-078: the shift belongs to whoever opened it).');
      covered(k,t!.expected??0,amount,'A till transfer');t!.expected=(t!.expected??0)-amount;}
    const r:CXRecord={id:id('TRF'),kind:'till',status:'Pending',amount,date:s.date,actor:uid,at:s.clock,fromTill:from,toTill:to};
    if(reason)r.reason=reason;
    x.records.push(r);
    return `${r.id} · till transfer ${money(amount)} ${cxTillName(s,from)} → ${cxTillName(s,to)} · awaiting the receiver`;
  },
  // PERM-DRW-004 · the receiver accepts on their own open shift on the receiving till, or the receiver / a Manager or Owner sends it back.
  TILL_TRANSFER_DECIDE:k=>{
    const {s,cmd,uid,open,assert,fail}=k;open();
    if(!cxCanTransfer(s,uid)&&!cxCanConfirm(s,uid))fail('FORBIDDEN','Access denied: a till transfer is accepted on your own cash shift.');
    const r=find(k,'till'),x=slice(k);const accept=!!cmd.accept,reason=text(cmd.reason);
    if(!accept)assert(reason,'A reason is required to reject a till transfer.');
    notLocked(k,r.date,(accept?'accepting':'rejecting')+' this till transfer');
    const intoDesk=r.toTill===x.desk,target=x.tills.find(t=>t.code===r.toTill);
    const receiver=intoDesk?s.shiftOpen&&cxOwnsDesk(s,uid)&&cxCanTransfer(s,uid):!!target&&target.shiftOpen&&target.openedBy===uid;
    if(accept){
      if(intoDesk)assert(s.shiftOpen,`Open your own shift on till ${cxTillName(s,r.toTill)} first: a till transfer is accepted on the receiver's open shift.`,'INVALID_TRANSITION');
      if(!receiver)fail('FORBIDDEN',`Access denied: only the receiver accepts it, on their own open shift on ${cxTillName(s,r.toTill)}.`);
    }else{
      if(!receiver&&!cxCanConfirm(s,uid))fail('FORBIDDEN','Only the receiver (on their own open shift on the receiving till) or a Manager or Owner can reject this transfer.');
      const back=r.fromTill===x.desk?s.shiftOpen:!!x.tills.find(t=>t.code===r.fromTill)?.shiftOpen;
      assert(back,`Open a shift on till ${cxTillName(s,r.fromTill)} first: the returned cash goes back into that drawer.`,'INVALID_TRANSITION');
    }
    assert(r.status==='Pending','This till transfer has already been decided.','INVALID_TRANSITION');
    if(r.actor===uid)fail('FORBIDDEN','The giver cannot accept or reject their own till transfer: the receiver does.');
    if(accept&&target)target.expected=(target.expected??0)+r.amount;
    if(!accept&&r.fromTill!==x.desk){const from=x.tills.find(t=>t.code===r.fromTill)!;from.expected=(from.expected??0)+r.amount;}
    r.status=accept?'Accepted':'Rejected';r.decidedBy=uid;r.decidedAt=s.clock;r.decidedDate=s.date;if(reason)r.decisionReason=reason.slice(0,200);
    return accept?`${r.id} · till transfer ${money(r.amount)} accepted into ${cxTillName(s,r.toTill)}`:`${r.id} · till transfer returned to ${cxTillName(s,r.fromTill)} · ${reason}`;
  },
  // PERM-DRW-005 · Owner only, from an open shift (any opener), reason required.
  OWNER_DRAWING:k=>{
    const {s,cmd,uid,open,assert,fail,id}=k;open();
    if(!cxCanDraw(s,uid))fail('FORBIDDEN','Access denied: only the Owner records an Owner drawing.');
    const amount=amountOf(k),reason=text(cmd.reason);
    assert(reason,'Give the reason for the drawing.');
    assert(s.shiftOpen,'No cash shift is open on this till. Open a shift first.','INVALID_TRANSITION');
    notLocked(k,s.date,'an Owner drawing');
    covered(k,drawer(s),amount,'An Owner drawing');
    const r:CXRecord={id:id('DRW'),kind:'drawing',status:'Posted',amount,date:s.date,actor:uid,at:s.clock,fromTill:slice(k).desk,reason:reason.slice(0,200)};
    slice(k).records.push(r);
    return `${r.id} · Owner drawing ${money(amount)} · ${r.reason}`;
  },
};

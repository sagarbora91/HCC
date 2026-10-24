// H03: the HAIP refund-remainder scenarios (captured − Σ|refund children|) run against HCC's EXISTING refund commands.
// No donor code is imported: HAIP's helper needs decimal.js and HCC already has folioRefundable() with integer paise.
import {describe,it,expect} from 'vitest';
import {seed,execute,totals,folioRefundable} from '../../../03 Prototype/src/services/mock/index';
import type {State} from '../../../03 Prototype/src/domain/types';

function paid(amount=100000):[State,string,string]{
  const s0=seed();let s=execute(s0,'owner',{type:'BOOK',guest:'Refund Lab',room:'105',arrival:s0.date,departure:'2026-10-26',people:2});
  const id=s.bookings.at(-1)!.id;s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
  s=execute(s,'cashier',{type:'PAYMENT',id,amount,method:'Cash',kind:'payment'});
  return [s,id,s.bookings.find(b=>b.id===id)!.receipts.at(-1)!.id];
}
const request=(s:State,id:string,receiptId:string,amount:number)=>execute(s,'cashier',{type:'REFUND_REQUEST',id,receiptId,amount,reason:'Lab refund'});
function refund(s:State,id:string,receiptId:string,amount:number){
  s=request(s,id,receiptId,amount);const aid=s.approvals.at(-1)!.id;
  s=execute(s,'manager',{type:'APPROVAL',id:aid,approve:true});
  return execute(s,'owner',{type:'REFUND_EXECUTE',approvalId:aid,method:'Cash'});
}
const code=(fn:()=>unknown)=>{try{fn();return 'none';}catch(e){return (e as {code?:string}).code??'other';}};
/** HAIP semantics re-stated (not copied): remaining = captured − Σ |executed refund children|. */
const donorRemaining=(captured:number,children:number[])=>captured-children.reduce((a,x)=>a+Math.abs(x),0);

describe('H03 refund remainder on HCC',()=>{
  it('₹1,000 receipt: ₹250 then ₹750 refunds succeed; the next paisa is rejected',()=>{
    let [s,id,rid]=paid();
    s=refund(s,id,rid,25000);s=refund(s,id,rid,75000);
    const x=s.bookings.find(b=>b.id===id)!;
    expect(folioRefundable(s,x,rid)).toBe(0);
    expect(code(()=>request(s,id,rid,1))).toBe('VALIDATION');
  });
  it('negative, zero and fractional-paise refunds are rejected',()=>{
    const [s,id,rid]=paid();
    for(const amt of [-100,0,0.5,25000.25])expect(code(()=>request(s,id,rid,amt))).toBe('VALIDATION');
  });
  it('outstanding requests reserve the remainder (serial analogue of two concurrent refunds)',()=>{
    let [s,id,rid]=paid();s=request(s,id,rid,60000);
    expect(code(()=>request(s,id,rid,50000))).toBe('VALIDATION');
    s=request(s,id,rid,40000); // exactly the rest is still allowed
    expect(folioRefundable(s,s.bookings.find(b=>b.id===id)!,rid)).toBe(0);
  });
  it('HCC remainder equals the donor formula when nothing is pending, and is stricter when requests are pending',()=>{
    let [s,id,rid]=paid();s=refund(s,id,rid,30000);
    const x=s.bookings.find(b=>b.id===id)!;
    const children=x.receipts.filter(r=>r.kind==='refund'&&r.refundOf===rid).map(r=>r.amount);
    expect(folioRefundable(s,x,rid)).toBe(donorRemaining(100000,children));
    s=request(s,id,rid,20000);
    const y=s.bookings.find(b=>b.id===id)!;
    expect(folioRefundable(s,y,rid)).toBe(50000);
    expect(donorRemaining(100000,children)).toBe(70000);
  });
  it('original receipt is retained; refund is an appended linked row; net paid falls by the refund',()=>{
    let [s,id,rid]=paid();const before=totals(s.bookings.find(b=>b.id===id)!).paid;
    s=refund(s,id,rid,25000);const x=s.bookings.find(b=>b.id===id)!;
    expect(x.receipts.find(r=>r.id===rid)!.amount).toBe(100000);
    expect(x.receipts.filter(r=>r.refundOf===rid).length).toBe(1);
    expect(totals(x).paid).toBe(before-25000);
  });
  it('refundable deposit cannot be refunded as a payment (deposit return is separate)',()=>{
    let [s,id]=paid();s=execute(s,'cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit'});
    const dep=s.bookings.find(b=>b.id===id)!.receipts.at(-1)!.id;
    expect(code(()=>request(s,id,dep,10000))).toBe('INVALID_TRANSITION');
  });
  it('requester cannot execute their own refund (DEC-031); approver ≠ requester',()=>{
    let [s,id,rid]=paid();s=request(s,id,rid,10000);const aid=s.approvals.at(-1)!.id;
    expect(code(()=>execute(s,'cashier',{type:'APPROVAL',id:aid,approve:true}))).toBe('FORBIDDEN');
    s=execute(s,'manager',{type:'APPROVAL',id:aid,approve:true});
    expect(code(()=>execute(s,'cashier',{type:'REFUND_EXECUTE',approvalId:aid,method:'Cash'}))).toBe('FORBIDDEN');
  });
  it('executing the same approval twice is rejected',()=>{
    let [s,id,rid]=paid();s=request(s,id,rid,10000);const aid=s.approvals.at(-1)!.id;
    s=execute(s,'manager',{type:'APPROVAL',id:aid,approve:true});s=execute(s,'owner',{type:'REFUND_EXECUTE',approvalId:aid,method:'Cash'});
    expect(code(()=>execute(s,'owner',{type:'REFUND_EXECUTE',approvalId:aid,method:'Cash'}))).toBe('INVALID_TRANSITION');
  });
});

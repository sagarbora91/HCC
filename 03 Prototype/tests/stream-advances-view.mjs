// F8 QW-ADV - Cashier step "Advances held" (domain advHeld + wiring). Existing commands only.
import {readFileSync} from 'node:fs';
export function register({test,M,assert}){
  const {execute,seed,totals,advHeld,advTotals,advCsv,ADV_RED_DAYS,HccError,addDays}=M;
  const code=fn=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  /** New confirmed booking (room 105, today -> 26 Oct) with a 1,000 rupee advance and a 500 rupee deposit. */
  function fixture(){
    let s=seed();
    s=execute(s,'reception',{type:'BOOK',guest:'Advance Test (fictional)',room:'105',arrival:s.date,departure:'2026-10-26',people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:100000,method:'UPI',reference:'DEMO-UPI-ADV',kind:'payment',key:'adv-1'});
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit',key:'adv-dep'});
    return [s,id];
  }
  const row=(s,id,uid='cashier')=>advHeld(s,uid).find(r=>r.booking===id);

  test('[F8-QW-ADV] rows and figures: advance, deposit, held, refunded, age, folio A',()=>{
    const [s,id]=fixture(),r=row(s,id);
    assert.ok(r,'row exists');
    assert.equal(r.advance,100000);assert.equal(r.deposit,50000);assert.equal(r.held,150000);assert.equal(r.refunded,0);
    assert.equal(r.ageDays,0);assert.equal(r.folios,'A');assert.equal(r.status,'Confirmed');assert.equal(r.red,false);
    assert.equal(r.refundable.length,1);assert.equal(r.refundable[0].remaining,100000);
    const rows=advHeld(s,'cashier'),t=advTotals(rows);
    assert.equal(t.count,rows.length);assert.equal(rows.reduce((a,x)=>a+x.held,0),t.held);
    assert.ok(rows.every(x=>x.held>0),'no empty rows');
  });
  test('[F8-QW-ADV] a bill larger than the payments leaves no advance; a deposit alone still lists',()=>{
    let [s,id]=fixture();
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:200000});
    const r=row(s,id);assert.equal(r.advance,0);assert.equal(r.deposit,50000);assert.equal(r.held,50000);
    assert.ok(totals(bk(s,id)).balance>0);
  });
  test('[F8-QW-ADV] red boundaries: cancelled / no-show / departed with money held, advance older than 90 days',()=>{
    for(const st of ['Cancelled','No show','Departed']){
      const [s0,id]=fixture(),s=structuredClone(s0);bk(s,id).status=st;
      const r=row(s,id);assert.equal(r.red,true,st);assert.match(r.redReason,new RegExp(st));
    }
    const [s0,id]=fixture();
    const aged=days=>{const s=structuredClone(s0);const d=addDays(s.date,-days);bk(s,id).receipts.filter(r=>r.kind==='payment').forEach(r=>{r.date=d;});return row(s,id);};
    assert.equal(ADV_RED_DAYS,90);
    assert.equal(aged(89).red,false);assert.equal(aged(90).red,false);assert.equal(aged(90).ageDays,90);
    assert.equal(aged(91).red,true);assert.match(aged(91).redReason,/90 days/);
    // an old DEPOSIT alone is not an "old advance": only the status turns it red
    const s2=structuredClone(s0);bk(s2,id).receipts=bk(s2,id).receipts.filter(r=>r.kind==='deposit');bk(s2,id).receipts.forEach(r=>{r.date=addDays(s2.date,-200);});
    const r2=row(s2,id);assert.equal(r2.advance,0);assert.equal(r2.red,false);
    // red rows sort first
    const s3=structuredClone(s0);bk(s3,id).status='Cancelled';assert.equal(advHeld(s3,'cashier')[0].booking,id);
  });
  test('[F8-QW-ADV] refund path uses REFUND_REQUEST, needs approval, and the requester cannot execute it',()=>{
    let [s,id]=fixture();const rid=row(s,id).refundable[0].receiptId;
    s=execute(s,'cashier',{type:'REFUND_REQUEST',id,receiptId:rid,amount:40000,reason:'Guest cancelled part of stay'});
    const a=s.approvals.at(-1);assert.equal(a.kind,'refund');assert.equal(a.status,'Pending');
    let r=row(s,id);assert.deepEqual(r.pending.map(p=>[p.id,p.status,p.amount]),[[a.id,'Pending',40000]]);
    assert.equal(r.refundable[0].remaining,60000,'a pending request reserves the amount');
    assert.equal(code(()=>execute(s,'manager',{type:'REFUND_EXECUTE',approvalId:a.id})),'INVALID_TRANSITION','not approved yet');
    s=execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true});
    assert.equal(row(s,id).pending[0].status,'Approved');
    assert.equal(code(()=>execute(s,'cashier',{type:'REFUND_EXECUTE',approvalId:a.id,method:'UPI',reference:'R1'})),'FORBIDDEN','DEC-031: the requester never executes');
    s=execute(s,'manager',{type:'REFUND_EXECUTE',approvalId:a.id,method:'UPI',reference:'DEMO-REF'});
    r=row(s,id);assert.equal(r.refunded,40000);assert.equal(r.advance,60000);assert.equal(r.pending.length,0);
  });
  test('[F8-QW-ADV] deposit return is the existing PAYMENT deposit-return; the deposit row sits on folio A',()=>{
    let [s,id]=fixture();
    assert.equal(bk(s,id).receipts.find(r=>r.kind==='deposit').folio,undefined,'absent = A (SF-CONTRACT section 3)');
    assert.equal(row(s,id).folios,'A');
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit-return',key:'adv-ret'});
    const r=row(s,id);assert.equal(r.deposit,0);assert.equal(r.held,100000);
    // on a split stay a payment receipt carries its own folio letter
    const s2=structuredClone(s);bk(s2,id).receipts.find(x=>x.kind==='payment').folio='B';
    assert.equal(row(s2,id).folios,'B');
  });
  test('[F8-QW-ADV] role gate: payments roles see rows, housekeeping and maintenance see none',()=>{
    const [s]=fixture();
    for(const u of s.users)assert.equal(advHeld(s,u.id).length>0,M.can(s,u.id,'payments'),u.id+' sees rows exactly when the cashier page opens (payments)');
    for(const u of ['owner','manager','cashier'])assert.ok(advHeld(s,u).length>0,u);
    for(const u of ['housekeeper','maint'])assert.equal(advHeld(s,u).length,0,u);
    const nav=readFileSync('src/features/closing/register.tsx','utf8');assert.ok(/id:'cashier'[^}]*p:'payments'/.test(nav),'page gate is payments');
  });
  test('[F8-QW-ADV] CSV: header, one line per row, rupee amounts, formula-neutral guest',()=>{
    const [s,id]=fixture();bk(s,id).guest='=cmd()';
    const rows=advHeld(s,'cashier'),csv=advCsv(rows),lines=csv.split('\n');
    assert.equal(lines[0],'Booking,Guest,Status,Arrival,Departure,Advance,Deposit,Refunded,Held,Age (days),Folio,Flag');
    assert.equal(lines.length,rows.length+1);
    assert.ok(csv.includes("'=cmd()")&&csv.includes('1000.00,500.00,0.00,1500.00'));
  });
  test('[F8-QW-ADV] wiring: step after credit-guests, sub-screen id, actions open the existing dialogs only',()=>{
    const c=readFileSync('src/features/closing/Cashier.tsx','utf8');
    assert.ok(/'credit-guests','advances-held'\]/.test(c));
    assert.ok(readFileSync('src/app/subscreens.ts','utf8').includes("{page:'cashier',id:'advances-held',label:'Advances held'"));
    const u=readFileSync('src/features/cashier/AdvancesHeld.tsx','utf8');
    for(const m of ["type:'refund'","type:'refund-execute'","type:'payment'"])assert.ok(u.includes(m),m);
    assert.ok(!/run\(/.test(u),'the view never posts a command itself');
    assert.ok(u.includes('saveCsv'));
  });
}

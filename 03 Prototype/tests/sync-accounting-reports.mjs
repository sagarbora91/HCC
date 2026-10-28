// ACC-2 · lane acc2-parity: the production readers of the statement, six-column trial balance and day book (src/clients/sync/accounting-reports.ts)
// read ANY range from the server (no 62-day device window, no device derivation), against a fake API shaped like the .NET views
// (08 Production/src/Hcc.Contracts/Accounting/AccountingReportContracts.cs). Names prefixed [ACC2-PARITY-SYNC].
import {loadSync} from './_sync-harness.mjs';

export async function register({test,assert}){
  const S=await loadSync();
  const calls=[];
  const api=routes=>({get:async(path,query)=>{calls.push({path,query});const f=routes[path];if(!f)throw new Error('no route '+path);return f(query);}});
  const settle=async p=>{try{return {ok:true,value:await p};}catch(error){return {ok:false,error};}};
  const st={account:'1110/CMP-7',name:'Company receivables (city ledger) · Deccan Agro',from:'2025-04-01',to:'2026-03-31',openingPaise:4000000,debitPaise:6300000,creditPaise:4000000,closingPaise:6300000,
    rows:[{date:'2025-04-06',no:'JRN/20250406/001',type:'Journal',narration:'Company credit',counter:'Guest ledger (in-house folios)',debitPaise:6300000,creditPaise:0,runningPaise:10300000},
      {date:'2025-12-10',no:'RCT/20251210/001',type:'Receipt',narration:'Company payment',counter:'Bank accounts · HDFC',debitPaise:0,creditPaise:4000000,runningPaise:6300000}]};
  const cols=(od,oc,d,c,cd,cc)=>({openingDebitPaise:od,openingCreditPaise:oc,debitPaise:d,creditPaise:c,closingDebitPaise:cd,closingCreditPaise:cc});
  const tb={from:'2025-04-01',to:'2026-03-31',detail:true,rows:[{account:'1110',name:'Company receivables',group:'debtors',control:null,...cols(0,0,0,0,0,0)},
    {account:'1110/CMP-7',name:'Company receivables · Deccan Agro',group:'debtors',control:'1110',...cols(4000000,0,6300000,4000000,6300000,0)},
    {account:'4000',name:'Room revenue',group:'sales',...cols(0,4000000,0,2300000,0,6300000)}],totals:cols(4000000,4000000,6300000,6300000,6300000,6300000),balanced:true,openingPending:false};
  const db={from:'2025-04-01',to:'2026-03-31',rows:[{date:'2025-04-06',no:'JRN/20250406/001',type:'Journal',narration:'x',account:'1110/CMP-7',ledger:'Company receivables · Deccan Agro',debitPaise:6300000,creditPaise:0,source:'company-credit:9'},
    {date:'2025-04-06',no:'JRN/20250406/001',type:'Journal',narration:'x',account:'1100',ledger:'Guest ledger',debitPaise:0,creditPaise:6300000,source:'company-credit:9'}],vouchers:1,debitPaise:6300000,creditPaise:6300000,balanced:true};
  const A=api({'/accounting/statement':()=>st,'/accounting/trial-balance':()=>tb,'/accounting/day-book':()=>db});

  const r1=await settle(S.loadAccStatement(A,' 1110/CMP-7 ','2025-04-01','2026-03-31'));
  const r2=await settle(S.loadAccTrialBalance6(A,'2025-04-01','2026-03-31',true));
  const r3=await settle(S.loadAccDayBook(A,'2025-04-01','2026-03-31'));
  test('[ACC2-PARITY-SYNC] a whole financial year (365 days) is ONE server read per report — any range, no 62-day window, no device derivation',()=>{
    assert.ok(r1.ok&&r2.ok&&r3.ok,JSON.stringify([r1,r2,r3].filter(r=>!r.ok).map(r=>String(r.error))));
    assert.deepEqual(calls.map(c=>[c.path,c.query]),[['/accounting/statement',{account:'1110/CMP-7',from:'2025-04-01',to:'2026-03-31'}],
      ['/accounting/trial-balance',{from:'2025-04-01',to:'2026-03-31',detail:true}],['/accounting/day-book',{from:'2025-04-01',to:'2026-03-31'}]]);
  });
  test('[ACC2-PARITY-SYNC] the views map to the contract §6 shapes of the prototype selectors (paise, signed opening / running / closing)',()=>{
    const s=r1.value;assert.deepEqual(Object.keys(s).sort(),['account','closing','credit','debit','from','name','opening','rows','to']);
    assert.deepEqual([s.opening,s.debit,s.credit,s.closing],[4000000,6300000,4000000,6300000]);
    assert.deepEqual(s.rows[1],{date:'2025-12-10',no:'RCT/20251210/001',type:'Receipt',narration:'Company payment',counter:'Bank accounts · HDFC',debit:0,credit:4000000,running:6300000});
    const t=r2.value;assert.equal(t.detail,true);assert.equal(t.balanced,true);assert.equal(t.openingPending,false);
    assert.equal('control' in t.rows[0],false,'a control row carries no control');assert.equal(t.rows[1].control,'1110');
    assert.deepEqual(t.totals,{openingDebit:4000000,openingCredit:4000000,debit:6300000,credit:6300000,closingDebit:6300000,closingCredit:6300000});
    assert.deepEqual(Object.keys(t.rows[2]).sort(),['account','closingCredit','closingDebit','credit','debit','group','name','openingCredit','openingDebit']);
    const d=r3.value;assert.equal(d.rows.length,2);assert.equal(d.vouchers,1);assert.equal(d.rows[0].source,'company-credit:9');assert.equal(d.debit,d.credit);
  });
  calls.length=0;
  const bad1=await settle(S.loadAccDayBook(A,'2026-03-31','2025-04-01'));
  const bad2=await settle(S.loadAccStatement(A,'  ','2025-04-01','2025-04-30'));
  const sentBefore=calls.length;
  const P=api({'/accounting/statement':()=>({...st,rows:[{...st.rows[0],debitPaise:12.5}]}),'/accounting/day-book':()=>({...db,rows:[{...db.rows[0],type:'Memo'}]})});
  const bad3=await settle(S.loadAccStatement(P,'1110','2025-04-01','2025-04-30'));
  const bad4=await settle(S.loadAccDayBook(P,'2025-04-01','2025-04-30'));
  test('[ACC2-PARITY-SYNC] a reversed range or no ledger is refused before any request; non-paise money or an unknown voucher type is a PROTOCOL error',()=>{
    assert.equal(bad1.ok,false);assert.equal(bad1.error.code,'VALIDATION');assert.equal(bad2.ok,false);assert.equal(bad2.error.code,'VALIDATION');
    assert.equal(sentBefore,0,'nothing sent for the refused requests');
    assert.equal(bad3.ok,false);assert.equal(bad3.error.wireCode,'PROTOCOL');assert.equal(bad4.ok,false);assert.equal(bad4.error.wireCode,'PROTOCOL');
  });
}

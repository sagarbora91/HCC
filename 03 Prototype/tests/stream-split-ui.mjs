// F8 sf-ui · split folio screens: the pure logic behind them (src/features/folio/SplitLogic.ts, bundled here) driven against the real commands.
// Fictional data on the BASE fixture. Components are checked by tsc + the fit check; this file checks what they send and show.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
export async function register({test,M,assert}){
  await build({entryPoints:['src/features/folio/SplitLogic.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/split-logic.mjs',logLevel:'error'});
  const L=await import('./.generated/split-logic.mjs?'+Date.now());
  const {seed,execute,totals}=M;
  const addDays=(d,n)=>{const x=new Date(d+'T00:00:00Z');x.setUTCDate(x.getUTCDate()+n);return x.toISOString().slice(0,10);};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const codeOf=fn=>{try{fn();}catch(e){return e.details?.reason??e.code;}return 'none';};
  function stay(){
    let s=seed();
    s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'Deccan Agro Ltd (fictional)',gstin:'27AAACD1111A1Z1',city:'Latur',creditLimit:5000000});
    const co=s.groupsX.companies.at(-1).id;
    s=execute(s,'owner',{type:'BOOK',guest:'Split UI Guest',room:'105',arrival:s.date,departure:addDays(s.date,3),people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    return {s,id,co};
  }
  const split=(s,id,co)=>execute(s,'owner',L.sfOpenCommand(bk(s,id),{kind:'company',ref:co,routes:['room'],reason:'Company pays the room'},'k-open'));

  test('[F8-SF-UI] the switcher appears only on split stays; labels carry the payer and each folio balance',()=>{
    let {s,id,co}=stay();
    assert.deepEqual(L.sfSwitcher(bk(s,id)),[]);
    assert.equal(L.sfCurrent(bk(s,id),'B'),'A');
    s=split(s,id,co);s=execute(s,'owner',{type:'POST_NIGHTS',id});
    const b=bk(s,id),tabs=L.sfSwitcher(b);
    assert.deepEqual(tabs.map(t=>t.id),['A','B']);
    assert.equal(tabs[0].label,'A · Split UI Guest');assert.equal(tabs[1].label,'B · Deccan Agro Ltd (fictional) (company)');
    assert.equal(tabs[1].balance,totals(M.sfView(b,'B')).balance);assert.ok(tabs[1].balance>0);
    assert.equal(L.sfCurrent(b,'B'),'B');assert.equal(L.sfCurrent(b,'D'),'A');
  });

  test('[F8-SF-UI] a single-folio stay sends exactly today\'s commands (no folio field, no room hint)',()=>{
    const {s,id}=stay(),b=bk(s,id);
    const cmd={type:'PAYMENT',id,amount:100,method:'Cash'};
    assert.equal(L.sfWithFolio(b,'A',cmd),cmd);
    assert.deepEqual(L.sfOpen(b,'payment','A'),{type:'payment',id});
    assert.equal(L.sfFolioOfModal(b,'A'),undefined);
  });

  test('[F8-SF-UI] every folio action carries its folio and lands on it (payment, charge, discount, company credit, credit note)',()=>{
    let {s,id,co}=stay();
    s=split(s,id,co);s=execute(s,'owner',{type:'POST_NIGHTS',id});
    const b=bk(s,id);
    assert.deepEqual(L.sfOpen(b,'payment','B'),{type:'payment',id,room:'B'});assert.equal(L.sfFolioOfModal(b,'B'),'B');assert.equal(L.sfFolioOfModal(b,'Z'),undefined);
    s=execute(s,'owner',L.sfWithFolio(bk(s,id),'A',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'}));
    assert.equal(bk(s,id).lines.at(-1).folio,'A');
    s=execute(s,'owner',L.sfWithFolio(bk(s,id),'B',{type:'CHARGE',id,label:'Conference tea',amount:20000,gst:'food'}));
    assert.equal(bk(s,id).lines.at(-1).folio,'B','a charge sent with folio B lands on B');
    s=execute(s,'owner',L.sfWithFolio(bk(s,id),'B',{type:'DISCOUNT',id,amount:10000,reason:'Corporate rate'}));
    assert.equal(bk(s,id).lines.at(-1).folio,'B');
    const tA=totals(M.sfView(bk(s,id),'A')).balance;
    s=execute(s,'owner',L.sfWithFolio(bk(s,id),'A',{type:'PAYMENT',id,amount:tA,method:'UPI',reference:'UPI-A'}));
    assert.equal(bk(s,id).receipts.at(-1).folio,'A');
    s=execute(s,'reception',L.sfWithFolio(bk(s,id),'B',{type:'COMPANY_CREDIT_REQUEST',id}));
    assert.equal(s.approvals.at(-1).meta.folio,'B');
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    s=execute(s,'owner',{type:'CHECKOUT',id});
    s=execute(s,'manager',L.sfWithFolio(bk(s,id),'B',{type:'CREDIT_NOTE',id,amount:100000,reason:'One night not used'}));
    assert.equal(s.approvals.at(-1).meta.folio,'B');
  });

  test('[F8-SF-UI] check-out sheet: one row per folio, total = Σ folio balances, settles and lists one invoice per folio',()=>{
    let {s,id,co}=stay();
    s=split(s,id,co);s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    let b=bk(s,id),sh=L.sfSettleSheet(s,b);
    assert.equal(sh.rows.length,2);
    assert.equal(sh.balance,sh.rows.reduce((a,r)=>a+r.balance,0));
    assert.equal(sh.balance,totals(M.sfView(b,'A')).balance+totals(M.sfView(b,'B')).balance);
    assert.equal(sh.total,totals(b).total,'Σ folio totals = the stay');
    assert.deepEqual(sh.rows.map(r=>r.status),['pay','pay']);assert.equal(sh.clear,false);assert.ok(sh.blockers.some(x=>/Folio B/.test(x)));
    s=execute(s,'owner',{type:'PAYMENT',id,folio:'A',amount:sh.rows[0].balance,method:'UPI',reference:'UPI-A'});
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id,folio:'B'});
    sh=L.sfSettleSheet(s,bk(s,id));assert.equal(sh.rows[1].status,'pay','pending approval does not clear the folio');
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    sh=L.sfSettleSheet(s,bk(s,id));
    assert.deepEqual(sh.rows.map(r=>r.status),['settled','company-credit']);assert.equal(sh.clear,true,JSON.stringify(sh.blockers));
    const owed=sh.balance;
    s=execute(s,'owner',{type:'CHECKOUT',id});b=bk(s,id);
    const inv=L.sfInvoiceRows(b);
    assert.equal(inv.length,2);assert.ok(inv.every(r=>/\d$/.test(r.no)),'both folios have an invoice number');assert.notEqual(inv[0].no,inv[1].no);
    assert.equal(inv.reduce((a,r)=>a+r.total,0),totals(b).total);
    assert.equal(L.sfSettleSheet(s,b).invoices.length,2);assert.ok(owed>0);
  });

  test('[F8-SF-UI] Split bill commands: open, route, remove — and the refusals keep their message',()=>{
    let {s,id,co}=stay();
    s=split(s,id,co);const b=bk(s,id);
    assert.deepEqual(L.sfFreeRoutes(b),['extras'],'room is taken by folio B');
    let err;try{execute(s,'owner',L.sfOpenCommand(b,{kind:'company',ref:co,routes:[]},'k2'));}catch(e){err=e;}
    assert.equal(err.details.reason,'SF_COMPANY_FOLIO');assert.ok(/at most one company folio/.test(err.message));
    s=execute(s,'owner',L.sfRouteCommand(b,'B',['room','extras'],'Company pays everything','k3'));
    assert.deepEqual(bk(s,id).folios[1].routes,['room','extras']);
    s=execute(s,'owner',L.sfRouteCommand(bk(s,id),'B',[],'Back to the guest','k4'));
    s=execute(s,'owner',L.sfRemoveCommand(bk(s,id),'B','k5'));
    assert.equal(L.sfSwitcher(bk(s,id)).length,0,'after removing the only extra folio the stay is single-folio again');
    assert.equal(codeOf(()=>execute(s,'owner',L.sfRemoveCommand(bk(s,id),'A','k6'))),'SF_FOLIO_NOT_EMPTY');
  });

  test('[F8-SF-UI] DEC-061 walk: Housekeeping, Supervisor and Maintenance get no folio money from any split action; the screens gate on billing / payments',()=>{
    let {s,id,co}=stay();
    s=split(s,id,co);s=execute(s,'owner',{type:'POST_NIGHTS',id});
    for(const u of ['housekeeper','supervisor','maintenance']){
      if(!s.users.some(x=>x.id===u))continue;
      assert.equal(M.rbacCanViewFolios(s,u),false,u+' cannot open folios');
      for(const c of [L.sfOpenCommand(bk(s,id),{kind:'company',ref:co,routes:[]},'x'),L.sfRouteCommand(bk(s,id),'B',[],'reason here','x'),L.sfRemoveCommand(bk(s,id),'B','x'),{type:'PAYMENT',id,folio:'B',amount:100,method:'UPI',reference:'x'},{type:'COMPANY_CREDIT_REQUEST',id,folio:'B'}])
        assert.throws(()=>execute(s,u,c),e=>e.code==='FORBIDDEN'||/Access denied|not permitted/i.test(e.message),u+' '+c.type);
    }
    const src=readFileSync('src/features/folio/Split.tsx','utf8');
    assert.ok(/act\('billing'/.test(src),'Split bill entry is permission-gated (billing)');
    assert.ok(/permission\('payments'\)/.test(src)&&/permission\('checkout'\)/.test(src),'sheet actions are gated like today');
    const modals=readFileSync('src/features/folio/modals.tsx','utf8');
    assert.ok(/rbacCanViewFolios\(s,uid\)\)return <Empty title="Access denied"/.test(modals),'folio dialog still refuses roles without folio rights');
  });

  test('[F8-SF-UI] one document per folio: doc.folio for "<booking>#B" is drawn from that folio',()=>{
    let {s,id,co}=stay();
    s=split(s,id,co);s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    const b=bk(s,id),dA=M.folioDocument(s,'owner',L.sfDocBooking(b,'A'),'folio'),dB=M.folioDocument(s,'owner',L.sfDocBooking(b,'B'),'folio');
    const net=d=>d.tables[0].rows.at(-1).net;
    assert.equal(net(dA),totals(M.sfView(b,'A')).net);assert.equal(net(dB),totals(M.sfView(b,'B')).net);
    assert.equal(net(dA)+net(dB),totals(b).net);
    assert.ok(dB.pairs.some(p=>p[0]==='Bill to'&&/Deccan/.test(p[1])));
    const single=stay();
    assert.equal(L.sfDocBooking(bk(single.s,single.id),'A'),single.id);
  });
}

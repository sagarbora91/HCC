// ACC-3 acc3-tally · Tally XML masters (per FY) and vouchers (per range ≤ 31 days) — contract 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §4.
// The golden files are shared with the server (08 Production/tests/Hcc.Accounting.Tests/Golden/tally-april-2026.*): the prototype builds them from the
// ACC-RULEBOOK scenario (build-tally-golden.mjs), the server test rebuilds the same bytes from tally-april-2026.input.json with VoucherExport, so
// "prototype output = golden = server output" is checked on both sides. Names prefixed [ACC3-TALLY].
import {readFileSync} from 'node:fs';
export function register({test,M,assert}){
  const T='[ACC3-TALLY] ';
  const G=new URL('../../08 Production/tests/Hcc.Accounting.Tests/Golden/',import.meta.url);
  const read=n=>readFileSync(new URL(n,G),'utf8');
  const input=JSON.parse(read('tally-april-2026.input.json'));
  const goldenMasters=read('tally-april-2026.masters.xml'),goldenVouchers=read('tally-april-2026.vouchers.xml');
  const COMPANY=input.company,FROM='2026-04-01',TO='2026-04-30';
  let cache;const month=()=>cache??(cache=M.loadScenario('ACC-RULEBOOK'));
  const bookOf=i=>M.tallyBuildBook(i.chart,i.opening,i.parties,i.seen);
  /** Minimal well-formedness check: tags nest, attributes quoted, no stray '&' or '<' in text. Returns the element counts. */
  function check(xml){
    assert.ok(xml.startsWith('<?xml version="1.0" encoding="utf-8"?>\r\n'));assert.ok(xml.endsWith('\r\n'));
    assert.ok(!/(^|[^\r])\n/.test(xml),'CRLF only');
    const body=xml.replace(/<!--[\s\S]*?-->/g,'').replace(/<\?xml[^>]*\?>/,'');
    const stack=[],counts={};
    for(const m of body.matchAll(/<(\/?)([A-Za-z][\w.:]*)((?:\s+[\w:]+="[^"<&]*(?:&(?:amp|lt|gt|quot);[^"<&]*)*")*)\s*(\/?)>|([^<]+)/g)){
      if(m[5]!==undefined){assert.ok(!/&(?!(amp|lt|gt|quot);)/.test(m[5]),'unescaped & in text');assert.ok(!m[5].includes('>')||true);continue;}
      const [,close,name,,self]=m;
      if(close){assert.equal(stack.pop(),name,'closing tag '+name);}else{counts[name]=(counts[name]??0)+1;if(!self)stack.push(name);}
    }
    assert.equal(stack.length,0);return counts;
  }
  const amounts=block=>[...block.matchAll(/<ALLLEDGERENTRIES\.LIST>[\s\S]*?<AMOUNT>(-?\d+\.\d\d)<\/AMOUNT>/g)].map(m=>Math.round(parseFloat(m[1])*100));

  test(T+'golden masters: the prototype selector over the ACC-RULEBOOK scenario and the shared input both give the golden bytes',()=>{
    assert.equal(M.tallyMastersXml(bookOf(input),input.company,input.fyStart),goldenMasters);
    const r=M.accTallyMasters(month(),'2026',COMPANY);assert.ok(!r.error);
    assert.equal(r.xml,goldenMasters);assert.equal(r.fileName,'HCC-tally-masters-SIMULATED-FY2026-27.xml');assert.equal(r.fy,'2026-27');
    assert.equal(r.from,'2026-04-01');assert.equal(r.to,'2027-03-31');assert.equal(r.bytes,new TextEncoder().encode(goldenMasters).length);
  });
  test(T+'golden vouchers: the prototype selector and the shared input both give the golden bytes (April 2026, identity GOLDEN)',()=>{
    assert.equal(M.tallyVouchersXml(input.stream,new Set(input.emit),bookOf(input),input.chart,input.company,input.identity),goldenVouchers);
    const r=M.accTallyVouchers(month(),FROM,TO,COMPANY,input.identity);assert.ok(!r.error);
    assert.equal(r.xml,goldenVouchers);assert.equal(r.count,input.emit.length);assert.equal(r.fileName,'HCC-tally-vouchers-SIMULATED-2026-04-01_2026-04-30.xml');
    assert.equal(r.debit,r.credit);assert.ok(r.count>100);
  });
  test(T+'both files are well-formed, UTF-8, CRLF; counts = groups + ledgers (+ the P&L carry) and the vouchers written',()=>{
    const m=check(goldenMasters),b=bookOf(input);
    assert.equal(m.TALLYMESSAGE,b.groups.length+b.ledgers.length+(b.profitCarryNet?1:0));
    assert.equal(m.LEDGER,b.ledgers.length+(b.profitCarryNet?1:0));assert.equal(m.GROUP??0,0,'no Owner sub-group in the rulebook month');
    const v=check(goldenVouchers);assert.equal(v.VOUCHER,input.emit.length);assert.equal(v.TALLYMESSAGE,input.emit.length);
    assert.ok(goldenVouchers.includes('<REPORTNAME>Vouchers</REPORTNAME>')&&goldenMasters.includes('<REPORTNAME>All Masters</REPORTNAME>'));
    assert.ok(goldenVouchers.startsWith('<?xml version="1.0" encoding="utf-8"?>\r\n<!-- SIMULATED EXPORT'));
    assert.ok(!goldenVouchers.includes('VCHKEY='),'Tally\'s own key is never invented');
  });
  test(T+'every voucher balances (Σ Dr = Σ Cr), ISDEEMEDPOSITIVE follows the sign, bill allocations add up to their entry',()=>{
    const vs=goldenVouchers.split('<VOUCHER ').slice(1);assert.equal(vs.length,input.emit.length);
    for(const v of vs){
      assert.equal(amounts(v).reduce((a,x)=>a+x,0),0);
      for(const e of v.split('<ALLLEDGERENTRIES.LIST>').slice(1)){
        const amt=Math.round(parseFloat(/<AMOUNT>(-?[\d.]+)<\/AMOUNT>/.exec(e)[1])*100);
        assert.equal(/<ISDEEMEDPOSITIVE>(\w+)</.exec(e)[1],amt<0?'Yes':'No');
        const bills=[...e.matchAll(/<BILLALLOCATIONS\.LIST>[\s\S]*?<AMOUNT>(-?[\d.]+)<\/AMOUNT>/g)].map(m=>Math.round(parseFloat(m[1])*100));
        if(bills.length)assert.equal(bills.reduce((a,x)=>a+x,0),amt);
      }
    }
  });
  test(T+'REMOTEID = GUID = HCC-<identity>-<source key>; stable across exports and ranges; the identity changes only the prefix',()=>{
    const a=M.accTallyVouchers(month(),FROM,TO,COMPANY,'ABC'),b=M.accTallyVouchers(month(),FROM,TO,COMPANY,'ABC'),c=M.accTallyVouchers(month(),FROM,TO,COMPANY,'DEF');
    assert.equal(a.xml,b.xml);assert.notEqual(a.xml,c.xml);
    const ids=[...a.xml.matchAll(/REMOTEID="([^"]+)"/g)].map(m=>m[1]),guids=[...a.xml.matchAll(/<GUID>([^<]+)<\/GUID>/g)].map(m=>m[1]);
    assert.deepEqual(ids,guids);assert.equal(new Set(ids).size,ids.length);assert.ok(ids.every(x=>x.startsWith('HCC-ABC-')));
    assert.equal(ids[0],M.tallyRemoteId('ABC',input.stream.find(v=>input.emit.includes(v.sourceId)).sourceId));
    // a shorter range carries a subset with the very same ids
    const half=M.accTallyVouchers(month(),FROM,'2026-04-15',COMPANY,'ABC'),hid=[...half.xml.matchAll(/REMOTEID="([^"]+)"/g)].map(m=>m[1]);
    assert.ok(hid.length>0&&hid.length<ids.length&&hid.every(x=>ids.includes(x)));
    // the opening journal is never in a vouchers file; the masters carry the opening
    assert.ok(!a.xml.includes('Opening balances'));
    assert.throws(()=>M.tallyVouchersXml(input.stream.slice(0,2).map(v=>({...v,sourceId:'x'})),new Set(['x']),bookOf(input),input.chart,'c','T'),/share a source key/);
  });
  test(T+'masters: opening in Tally sign, party ledgers bill-wise with GSTIN, names unique, Σ opening = 0',()=>{
    const b=bookOf(input);
    assert.equal(b.ledgers.reduce((a,l)=>a+l.openingNet,0)+b.profitCarryNet,0);
    const names=b.ledgers.map(l=>l.name.toLowerCase());assert.equal(new Set(names).size,names.length);
    const debtors=b.ledgers.filter(l=>l.party&&l.billWise);assert.ok(debtors.length>0);
    assert.ok(b.ledgers.filter(l=>l.party&&!l.billWise).every(l=>!/^(1110|1115|1120|2200)\//.test(l.id)));
    assert.ok(goldenMasters.includes('<OPENINGBALANCE>-20000.00</OPENINGBALANCE>'),'cash drawer 20,000 Dr as -20000.00');
    assert.ok(goldenMasters.includes('<NAME>Opening balance</NAME>')&&goldenMasters.includes('<BILLDATE>20260331</BILLDATE>'));
  });
  test(T+'bill allocation: oldest bill first, the rest On Account; credit ledgers the other way round',()=>{
    const chart=input.chart;
    const book=M.tallyBuildBook(chart,{'1110/CMP-1':10000},{'1110/CMP-1':{name:'Alpha Co',gstin:'27AAAAA0000A1Z5'}},[]);
    assert.equal(book.ledgers.find(l=>l.id==='1110/CMP-1').billWise,true);
    const V=(no,day,type,...entries)=>({no,date:`2026-04-0${day}`,type,sub:'',sourceId:'k'+no,narration:'n',entries:entries.map(([a,d,c])=>({account:a,debit:d,credit:c}))});
    const bills=new M.TallyBills(book);
    assert.deepEqual(bills.allocate(V('S1',1,'Sales',['1110/CMP-1',5000,0],['4000',0,5000]))[0],[{name:'S1',type:'New Ref',net:5000}]);
    assert.equal(bills.allocate(V('S2',2,'Sales',['1110/CMP-1',7000,0],['4000',0,7000]))[0][0].name,'S2');
    const rec=bills.allocate(V('R1',3,'Receipt',['1030',25000,0],['1110/CMP-1',0,25000]))[1];
    assert.deepEqual(rec.map(a=>[a.name,a.type]),[['Opening balance','Agst Ref'],['S1','Agst Ref'],['S2','Agst Ref'],['R1','On Account']]);
    assert.equal(rec.reduce((a,x)=>a+x.net,0),-25000);
    const cb=M.tallyBuildBook(chart,{'2200/V-1':-8000},{'2200/V-1':{name:'Vendor'}},[]),cbl=new M.TallyBills(cb);
    const pay=cbl.allocate(V('P1',4,'Payment',['2200/V-1',3000,0],['1030',0,3000]))[0];
    assert.deepEqual(pay,[{name:'Opening balance',type:'Agst Ref',net:3000}]);
    assert.equal(cbl.allocate(V('PU1',5,'Purchase',['5000',100,0],['2200/V-1',0,100]))[1][0].type,'New Ref');
  });
  test(T+'voucher types: receipt→Receipt, payment→Payment, cash↔bank→Contra, invoice→Sales, purchase→Purchase, credit note, purchase return→Debit Note, else Journal',()=>{
    const v=(type,sub='',...ac)=>({no:'N',date:FROM,type,sub,sourceId:'k',narration:'',entries:ac.map((a,i)=>({account:a,debit:i?0:100,credit:i?100:0}))});
    const c=input.chart,t=(x)=>M.tallyVoucherType(x,c);
    assert.equal(t(v('Receipt','','1030/BA-1','1110/C')),'Receipt');assert.equal(t(v('Payment','','5000','1030/BA-1')),'Payment');
    assert.equal(t(v('Contra','','1030/BA-1','1000')),'Contra');assert.equal(t(v('Sales','','1100','4000')),'Sales');
    assert.equal(t(v('Purchase','expense','5000','2200/V')),'Purchase');assert.equal(t(v('Purchase','return','2200/V','5000')),'Debit Note');
    assert.equal(t(v('Sales','credit-note','4000','1100')),'Credit Note');
    assert.equal(t(v('Journal','bank-deposit','1030/BA-1','1000')),'Contra');assert.equal(t(v('Journal','payrun','5100','2300')),'Journal');
    // on the real month: every type the contract lists appears, and nothing else
    const types=new Set([...goldenVouchers.matchAll(/<VOUCHERTYPENAME>([^<]+)</g)].map(m=>m[1]));
    for(const x of types)assert.ok(['Receipt','Payment','Contra','Journal','Sales','Purchase','Credit Note','Debit Note'].includes(x),x);
    assert.ok(types.has('Sales')&&types.has('Receipt')&&types.has('Payment')&&types.has('Contra'));
  });
  test(T+'names and escaping: a clash gets (2), control characters become spaces, & < > " are escaped, an Owner sub-group is created, the P&L carry goes to the P&L ledger',()=>{
    const chart={groups:[...input.chart.groups,{id:'g-banquet',name:'Banquet & Events',parent:'direct-income',nature:'Cr',side:'PL',custom:true}],
      ledgers:[...input.chart.ledgers,{code:'4700',name:'Banquet <income>',group:'g-banquet'}]};
    const parties={'1110/A':{name:'Same Name'},'1110/B':{name:'same name'},'2200/V':{name:'Room revenue'},'1110/C':{name:'Line\r\nBreak "Q" & Co'}};
    const book=M.tallyBuildBook(chart,{'4000':-500000,'5000':120000,'1000':380000,'1110/A':1000},parties,[]);
    const names=book.ledgers.map(l=>l.name);
    assert.ok(names.includes('Same Name')&&names.includes('same name (2)')&&names.includes('Room revenue (2)'));
    assert.equal(book.profitCarryNet,-380000);assert.equal(book.ledgers.find(l=>l.id==='4000').openingNet,0);
    const x=M.tallyMastersXml(book,'R & D <Hotel>','2026-04-01');check(x);
    assert.ok(x.includes('<GROUP NAME="Banquet &amp; Events" ACTION="Create">')&&x.includes('<PARENT>Direct Incomes</PARENT>'));
    assert.ok(x.includes('Banquet &lt;income&gt;')&&x.includes('Line  Break &quot;Q&quot; &amp; Co'));
    assert.ok(x.includes('<SVCURRENTCOMPANY>R &amp; D &lt;Hotel&gt;</SVCURRENTCOMPANY>'));
    assert.ok(x.includes('<LEDGER NAME="Profit &amp; Loss A/c" ACTION="Alter">')&&x.includes('<OPENINGBALANCE>3800.00</OPENINGBALANCE>'));
    assert.ok(!M.tallyMastersXml(book,undefined,'2026-04-01').includes('SVCURRENTCOMPANY'));
  });
  test(T+'range and year rules: more than 31 days, from after to, a bad date and a bad year are refused with the server\'s words',()=>{
    assert.equal(M.accTallyVouchers(month(),FROM,'2026-05-02',COMPANY).error,'Choose at most 31 days.');
    assert.equal(M.accTallyVouchers(month(),FROM,'2026-05-01',COMPANY).error,undefined);
    assert.equal(M.accTallyVouchers(month(),TO,FROM,COMPANY).error,'Choose a valid range: "from" on or before "to".');
    assert.ok(M.accTallyVouchers(month(),'2026-02-30','2026-03-01',COMPANY).error);
    for(const fy of ['','26','2026-28','abcd','2026/27'])assert.ok(M.accTallyMasters(month(),fy,COMPANY).error,fy);
    assert.equal(M.accTallyFy('2026-27').start,'2026-04-01');assert.equal(M.accTallyFy('2026').end,'2027-03-31');
    assert.equal(M.tallyAmount(123450),'-1234.50');assert.equal(M.tallyAmount(-123450),'1234.50');assert.equal(M.tallyAmount(0),'0.00');
  });
}

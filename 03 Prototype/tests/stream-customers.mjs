// Step 4 tests: Customer 360 index (grouped by mobile number), search rules (name / mobile, never booking ID), service links, booking-ID lookup.
import {build} from 'esbuild';
await build({entryPoints:['src/domain/streams/customers.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/customers.mjs',logLevel:'error'});
const C=await import('./.generated/customers.mjs');

export function register({test,M,assert}){
  const s=M.loadScenario('FY26'),all=C.customerIndex(s);
  test('[CUST] every booking belongs to exactly one customer',()=>{
    assert.equal(all.reduce((n,c)=>n+c.stays.length,0),s.bookings.length);
    const ids=new Set();for(const c of all)for(const b of c.stays){assert.ok(!ids.has(b.id),'duplicate '+b.id);ids.add(b.id);}
  });
  test('[CUST] customers are matched by mobile number, even when the name is spelled differently',()=>{
    const base=M.baseFixture();const a=base.bookings[0],b={...a,id:'X-1',guest:'Somebody Else',phone:'+91 98200 99999'},c={...a,id:'X-2',guest:'S. Else',phone:'098200 99999'};
    const t={...base,guests:[],bookings:[b,c]};const idx=C.customerIndex(t);
    assert.equal(idx.length,1);assert.equal(idx[0].stays.length,2);
  });
  test('[CUST] a customer lists stays newest first with totals only over stays that happened',()=>{
    const rep=all.find(c=>c.completed.length>=3);assert.ok(rep,'FY26 has repeat customers');
    for(let i=1;i<rep.stays.length;i++)assert.ok(rep.stays[i-1].arrival>=rep.stays[i].arrival);
    assert.equal(rep.lifetime,rep.completed.reduce((t,b)=>t+M.totals(b).total,0));
    assert.equal(rep.nights,rep.completed.reduce((t,b)=>t+M.nights(b.arrival,b.departure),0));
    assert.ok(rep.completed.every(b=>['Departed','In house'].includes(b.status)));
    assert.equal(rep.lastStay,rep.completed[0].arrival);
  });
  test('[CUST] saved guest profiles attach to their customer (preferences, masked ID source)',()=>{
    const withProfile=all.filter(c=>c.guest);assert.ok(withProfile.length>=1);
    for(const c of withProfile)assert.ok(c.stays.length>=1);
  });
  test('[CUST] search by name finds the customer; every typed word must match',()=>{
    const c=all.find(x=>x.name.includes(' '));const [first,last]=c.name.split(' ');
    assert.ok(C.customerSearch(all,c.name).some(x=>x.key===c.key));
    assert.ok(C.customerSearch(all,last+' '+first).some(x=>x.key===c.key));
    assert.equal(C.customerSearch(all,first+' zzzzqq').length,0);
    assert.equal(C.customerSearch(all,'').length,all.length);
  });
  test('[CUST] search by mobile digits needs 3+ digits and a role that sees phones',()=>{
    const c=all.find(x=>x.phone);const d=c.phone.replace(/\D/g,'');
    assert.ok(C.customerSearch(all,d.slice(-6)).some(x=>x.key===c.key));
    assert.equal(C.customerSearch(all,d.slice(-2)).length,0);
    assert.equal(C.customerSearch(all,d.slice(-6),false).length,0,'masked roles cannot search by number');
  });
  test('[CUST] booking IDs are NOT matched by customer search (owner decision)',()=>{
    const b=s.bookings.find(x=>x.id.startsWith('HCC-H05'));
    assert.equal(C.customerSearch(all,b.id).length,0);
  });
  test('[CUST] requests and complaints are linked through the stay booking',()=>{
    const x=s.servicesX;const r=x.requests[0],k=x.complaints[0];assert.ok(r&&k,'FY26 has guest services history');
    const cr=all.find(c=>c.stays.some(b=>b.id===r.booking));const svc=C.customerService(s,cr);
    assert.ok(svc.requests.some(q=>q.id===r.id));
    const ck=all.find(c=>c.stays.some(b=>b.id===k.booking));assert.ok(C.customerService(s,ck).complaints.some(q=>q.id===k.id));
  });
  test('[CUST] booking-ID lookup: partial ID, any status, 3+ characters, newest first',()=>{
    const b=s.bookings.find(x=>x.status==='Departed');
    assert.equal(C.bookingIdLookup(s,b.id)[0].id,b.id);
    assert.equal(C.bookingIdLookup(s,b.id.toLowerCase())[0].id,b.id);
    assert.equal(C.bookingIdLookup(s,'HC').length,0);
    assert.ok(C.bookingIdLookup(s,'hcc-h').length<=8);
    const conf=s.bookings.find(x=>x.status==='Confirmed');assert.ok(C.bookingIdLookup(s,conf.id).some(x=>x.status==='Confirmed'));
  });
  // AF-CRM (AF-DEC-4): Unicode-aware names, fictional Marathi guests
  const devaBase=()=>{const base=M.baseFixture();const a=base.bookings[0];return {base,a};};
  test('[AF-CRM] two Devanagari phone-less bookings are two customers',()=>{
    const {base,a}=devaBase();
    const b1={...a,id:'DV-1',guest:'राम पाटील',phone:''},b2={...a,id:'DV-2',guest:'सीता जोशी',phone:''};
    const idx=C.customerIndex({...base,guests:[],bookings:[b1,b2]});
    assert.equal(idx.length,2);assert.deepEqual(idx.map(c=>c.stays.length),[1,1]);
  });
  test('[AF-CRM] Devanagari search by one word finds the guest',()=>{
    const {base,a}=devaBase();
    const b1={...a,id:'DV-1',guest:'राम पाटील',phone:''},b2={...a,id:'DV-2',guest:'सीता जोशी',phone:''};
    const idx=C.customerIndex({...base,guests:[],bookings:[b1,b2]});
    assert.deepEqual(C.customerSearch(idx,'राम').map(c=>c.name),['राम पाटील']);
    assert.deepEqual(C.customerSearch(idx,'जोशी').map(c=>c.name),['सीता जोशी']);
    assert.equal(C.customerSearch(idx,'गीता').length,0);
  });
  test('[AF-CRM] ASCII normalisation unchanged (table)',()=>{
    const {base,a}=devaBase();
    const table=[['Asha Rao','asha rao'],['Asha Rao (fictional)','asha rao'],['ASHA  RAO','asha rao'],["O'Brien-Smith",'o brien smith'],['Dr. A. P. J. Kalam','dr a p j kalam'],['  leading space','leading space'],['trailing!!!','trailing'],['Room 101 Guest','room 101 guest'],['A_B','a b'],['Mr. & Mrs. Patil','mr mrs patil'],['R2-D2','r2 d2'],['x@y.com','x y com']];
    for(const [n,exp] of table){
      const idx=C.customerIndex({...base,guests:[],bookings:[{...a,id:'T-1',guest:n,phone:''}]});
      assert.equal(idx.length,1);assert.equal(idx[0].key,'n:'+exp,n);
    }
  });
  // AF2 phone digits: the mock audit trail (the prototype has no CRM merge or outbox) never carries a customer's raw mobile number, even for Devanagari names
  test('[AF2-PHONE] mock audit rows of a Devanagari-named customer with a mobile number hold no raw phone digits',()=>{
    let s=M.baseFixture();
    s=M.execute(s,'reception',{type:'BOOK',guest:'राम पाटील',phone:'+91 98200 77777',room:'105',arrival:s.date,departure:'2026-10-26',people:1,source:'Direct'});
    const bk=s.bookings.at(-1);
    s=M.execute(s,'reception',{type:'CANCEL',id:bk.id});
    const rows=s.audit.filter(a=>/राम|BOOK|CANCEL/i.test(a.action+a.detail));
    assert.ok(rows.length>=1,'the booking was audited');
    for(const a of rows){const txt=a.action+' '+a.detail;assert.ok(!/\d{10}/.test(txt)&&!txt.includes('98200'),'audit row has raw phone digits: '+txt);}
    const idx=C.customerIndex(s);
    const c=idx.find(x=>x.name==='राम पाटील');assert.ok(c);
    assert.ok(!JSON.stringify(s.audit).includes('9820077777'));
  });
}
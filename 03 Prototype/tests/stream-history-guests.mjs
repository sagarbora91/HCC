// History guest profiles: every distinct FY26 history customer has one fictional profile, linked by guestId; deterministic; BASE untouched.
import {build} from 'esbuild';
await build({entryPoints:['src/domain/streams/customers.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/customers-hg.mjs',logLevel:'error'});
const C=await import('./.generated/customers-hg.mjs');

export function register({test,M,assert}){
  const s=M.loadScenario('FY26'),base=M.baseFixture(),baseIds=new Set(base.bookings.map(b=>b.id));
  const hist=s.bookings.filter(b=>!baseIds.has(b.id));
  const key=p=>String(p||'').replace(/\D/g,'').slice(-10);
  test('[HGUEST] every history booking links to an existing guest profile',()=>{
    const ids=new Set(s.guests.map(g=>g.id));assert.equal(ids.size,s.guests.length,'unique profile ids');
    for(const b of hist)assert.ok(b.guestId&&ids.has(b.guestId),'no profile for '+b.id);
  });
  test('[HGUEST] same mobile shares one profile; different mobiles never do',()=>{
    const m=new Map();for(const b of hist){const k=key(b.phone);if(m.has(k))assert.equal(m.get(k),b.guestId);else m.set(k,b.guestId);}
    assert.equal(new Set(m.values()).size,m.size);
    assert.ok(hist.length>m.size,'FY26 has repeat customers');
  });
  test('[HGUEST] Customers 360: every customer has a profile; profiles are fictional and masked',()=>{
    const all=C.customerIndex(s);assert.ok(all.every(c=>c.guest),'customers without profile: '+all.filter(c=>!c.guest).length);
    for(const g of s.guests){assert.ok(/example\.test$/.test(g.email)||g.origin==='seed');assert.ok(g.idLast4.length<=4);assert.ok(/fictional/.test(g.address));}
  });
  test('[HGUEST] generation is deterministic and BASE is untouched',()=>{
    const a=M.buildHistory(M.baseFixture()),b=M.buildHistory(M.baseFixture());
    assert.deepEqual(a.guests,b.guests);assert.equal(JSON.stringify(a.bookings.map(x=>x.guestId)),JSON.stringify(b.bookings.map(x=>x.guestId)));
    assert.equal((base.guests||[]).length,10);assert.ok(base.bookings.every(x=>!x.guestId||/^G-\d{4}$/.test(x.guestId)));
  });
}

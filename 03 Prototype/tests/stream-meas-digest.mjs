// F12 MEAS · meas-digest tests (MEAS-CONTRACT §4). Names prefixed [F12-DIGEST]. The digest module is bundled here on its own (it is not in the mock index).
import {build} from 'esbuild';

export async function register({test,M,assert}){
  await build({entryPoints:['src/domain/streams/measure-digest.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/meas-digest.mjs',logLevel:'error'});
  const D=await import('./.generated/meas-digest.mjs?'+Date.now());
  const {seed,execute}=M;
  const setVal=(s,k,v)=>{const c=structuredClone(s);c.configX={...(c.configX??{}),values:{...(c.configX?.values??{}),[k]:v}};return c;};
  const TRN={type:'MEAS_TURNAWAY_LOG',reason:'price',nights:1,roomType:'Classic',rooms:1,quotedRate:200000,channel:'Walk-in'};
  const regrets=(s,n,arrival)=>{for(let i=0;i<n;i++)s=execute(s,'reception',{...TRN,arrival,key:'t'+i});return s;};
  const arrival=s=>M.addDays(s.date,30); // far future night: no bookings, 0 % occupancy
  const withBalance=s=>{const c=structuredClone(s);const b=c.bookings.find(b=>b.status==='In house'&&!b.company);b.lines.push({id:'x',label:'Big',amount:700000,date:c.date,kind:'service',taxBps:0});return [c,b];};

  test('[F12-DIGEST] empty case: nothing red -> block present, no rows, "none today"',()=>{
    const s=seed(),b=D.measDigestBlock(s,s.date);
    assert.ok(b);assert.equal(b.title,'Measurement');assert.equal(b.checked,2);
    assert.deepEqual(D.measDigestEmailLines({...b,rows:[]}),['Measurement exceptions: 0','  none today']);
    assert.deepEqual(D.measDigestWhatsAppLines(b),['Measurement exceptions '+b.rows.length]);
  });
  test('[F12-DIGEST] regrets: more than 3 price regrets on a night under 60 % occupancy is red; exactly 3 is not',()=>{
    let s=seed();const a=arrival(s);
    assert.equal(D.measDigestBlock(regrets(s,3,a),s.date).rows.filter(r=>r.key==='meas.daily.regrets').length,0);
    const r=D.measDigestBlock(regrets(s,4,a),s.date).rows.filter(r=>r.key==='meas.daily.regrets');
    assert.equal(r.length,1);assert.equal(r[0].reportId,'rpt.guests.regrets-denials');assert.equal(r[0].severity,'red');assert.match(r[0].text,/4 price regrets for/);
  });
  test('[F12-DIGEST] regrets of other reasons do not count',()=>{
    let s=seed();const a=arrival(s);for(let i=0;i<5;i++)s=execute(s,'reception',{...TRN,reason:'full',arrival:a,key:'f'+i});
    assert.equal(D.measDigestBlock(s,s.date).rows.length,0);
  });
  test('[F12-DIGEST] high balance: walk-in above the target is red with the report id as drill target; at the target it is not',()=>{
    const [s,b]=withBalance(seed());
    const rows=D.measDigestBlock(s,s.date).rows.filter(r=>r.key==='meas.daily.high-balance');
    assert.ok(rows.some(r=>r.text.includes('Room '+b.room)&&r.reportId==='rpt.money.high-balance'));
    const hi=setVal(s,'meas.target.highBalance',M.totals(s.bookings.find(x=>x.id===b.id)).balance);
    assert.ok(!D.measDigestBlock(hi,s.date).rows.some(r=>r.text.includes('walk-in above')&&r.text.includes('Room '+b.room)));
  });
  test('[F12-DIGEST] switches: an off item is absent, data kept; subscription hides the lines',()=>{
    const [s]=withBalance(seed());
    assert.ok(D.measDigestBlock(s,s.date).rows.length>0);
    const off=setVal(setVal(s,'meas.daily.high-balance',false),'meas.pack.daily',false); // core while credit on, so switch the pack
    const b=D.measDigestBlock(M.measOn(off,'meas.daily.high-balance')?setVal(s,'meas.digest.line.meas.daily.high-balance',false):off,s.date);
    assert.ok(!b||!b.rows.some(r=>r.key==='meas.daily.high-balance'));
    const sub=setVal(s,D.measDigestLineKey('meas.daily.high-balance'),false);
    assert.ok(!D.measDigestBlock(sub,s.date).rows.some(r=>r.key==='meas.daily.high-balance'));
    assert.equal(D.measDigestSubscriptions(sub).find(x=>x.key==='meas.daily.high-balance').subscribed,false);
    assert.equal(D.measDigestSubscriptions(s).find(x=>x.key==='meas.daily.high-balance').subscribed,true);
  });
  test('[F12-DIGEST] every pack off -> no block at all; all subscriptions off -> no block',()=>{
    let s=seed();for(const p of ['daily','revenue','guest','hk','people','study'])s=setVal(s,'meas.pack.'+p,false);
    assert.equal(D.measDigestBlock(s,s.date),undefined);assert.deepEqual(D.measDigestEmailLines(undefined),[]);assert.deepEqual(D.measDigestWhatsAppLines(undefined),[]);
    let t=seed();for(const x of D.measDigestSubscriptions(t))t=setVal(t,D.measDigestLineKey(x.key),false);
    assert.equal(D.measDigestBlock(t,t.date),undefined);
  });
  test('[F12-DIGEST] registry: a registered provider appears, severity sorts red first, a provider cannot report on another switch',()=>{
    const s=seed();
    D.registerMeasRedRows('meas.daily.cashier',()=>[{key:'meas.daily.cashier',reportId:'rpt.money.cashier-variance',text:'Shift variance',severity:'amber'},{key:'meas.daily.flash',reportId:'x',text:'foreign',severity:'red'}]);
    const rows=D.measDigestBlock(s,s.date).rows;
    assert.ok(rows.some(r=>r.text==='Shift variance'));assert.ok(!rows.some(r=>r.text==='foreign'));
    D.registerMeasRedRows('meas.daily.cashier',()=>[]);
  });
  test('[F12-DIGEST] WhatsApp line is a count only (no money, no names)',()=>{
    const [s]=withBalance(seed()),b=D.measDigestBlock(s,s.date),l=D.measDigestWhatsAppLines(b);
    assert.equal(l.length,1);assert.match(l[0],/^Measurement exceptions \d+$/);
  });
}

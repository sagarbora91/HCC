// F8 QW-KDS · kitchen display: card order, elapsed / levels (QWQ-3: amber 10, red 20), bump = POS_KOT_STATUS, no money, role gate. Names prefixed [F8-QW-KDS].
import {writeFile} from 'node:fs/promises';
import {build} from 'esbuild';
await writeFile('tests/.generated/kds-entry.ts',"export {navItems} from '../../src/app/nav';export {PAGE_CATEGORY} from '../../src/app/navmap';");
await build({entryPoints:['tests/.generated/kds-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/kds.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'}});
const X=await import('./.generated/kds.mjs');
export function register({test,M,assert}){
  const {execute,seed,kdsCards,KDS_AMBER_MINUTES,KDS_RED_MINUTES,can}=M;
  const code=fn=>{try{fn();}catch(e){return e&&e.code?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const at=(s,clock)=>{const c=structuredClone(s);c.clock=clock;return c;};
  const plus=(clock,m)=>{const d=new Date(clock.slice(0,16)+':00Z');d.setUTCMinutes(d.getUTCMinutes()+m);return d.toISOString().slice(0,16);};
  const last=s=>M.posSlice(s).orders.at(-1);
  /** Send one check's items to the kitchen at the given clock (tables T3, T4... each its own KOT). */
  const send=(s,clock,tbl,items,outlet='restaurant')=>{
    s=at(s,clock);s=execute(s,'cashier',{type:'POS_ORDER_OPEN',outlet,table:tbl,covers:2});const id=last(s).id;
    for(const [item,qty] of items)s=execute(s,'cashier',{type:'POS_ITEM_ADD',id,item,qty});
    return [execute(s,'cashier',{type:'POS_KOT_SEND',id}),id];
  };
  const base=()=>{const s=seed();return s.clock;};
  const MONEY=/price|amount|total|gross|net|tax|value|rate|paise|rs|₹/i;
  test('[F8-QW-KDS] thresholds are the QWQ-3 defaults (10 / 20 minutes)',()=>{assert.equal(KDS_AMBER_MINUTES,10);assert.equal(KDS_RED_MINUTES,20);});
  test('[F8-QW-KDS] cards are oldest first, with label, spot, items and quantities',()=>{
    const t0=base();let s=seed();let id1,id2;
    [s,id1]=send(s,t0,'T3',[['m-dal',1],['m-roti',2]]);
    [s,id2]=send(s,plus(t0,3),'T4',[['m-dal',2]]);
    const mine=kdsCards(at(s,plus(t0,5))).filter(c=>c.orderId===id1||c.orderId===id2);
    assert.deepEqual(mine.map(c=>c.orderId),[id1,id2]);
    assert.ok(/KOT/.test(mine[0].label));assert.equal(mine[0].spot,'Table T3');
    assert.deepEqual(mine[0].items.map(i=>[i.qty]),[[1],[2]]);assert.ok(mine[0].items.every(i=>typeof i.name==='string'&&i.name));
    const all=kdsCards(at(s,plus(t0,5)));
    for(let i=1;i<all.length;i++)assert.ok(all[i-1].sentAt<=all[i].sentAt,'oldest first');
  });
  test('[F8-QW-KDS] elapsed minutes use the simulated clock; level is ok below 10, amber at 10, red at 20',()=>{
    let [s,id]=send(seed(),base(),'T3',[['m-dal',1]]);const t0=kdsCards(s).find(c=>c.orderId===id).sentAt; // every command moves the simulated clock a minute
    const lv=m=>kdsCards(at(s,plus(t0,m))).find(c=>c.orderId===id);
    assert.equal(lv(0).minutes,0);assert.equal(lv(9).level,'ok');assert.equal(lv(9).levelText,'On time');
    assert.equal(lv(10).level,'amber');assert.equal(lv(10).levelText,'Slow');assert.equal(lv(19).level,'amber');
    assert.equal(lv(20).level,'red');assert.equal(lv(20).levelText,'Late');assert.equal(lv(35).minutes,35);
  });
  test('[F8-QW-KDS] bump moves the status Preparing to Ready to Served; Served leaves the board; no skipping',()=>{
    const t0=base();let [s,id]=send(seed(),t0,'T3',[['m-dal',1]]);
    const card=()=>kdsCards(s).find(c=>c.orderId===id);const kot=card().kotId;
    assert.equal(card().status,'Preparing');assert.equal(card().next,'Ready');
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_KOT_STATUS',id,kot,status:'Served'})),'INVALID_TRANSITION');
    s=execute(s,'cashier',{type:'POS_KOT_STATUS',id,kot,status:'Ready'});
    assert.equal(card().status,'Ready');assert.equal(card().next,'Served');assert.ok(card().minutes<=1);
    const readyAt=M.posSlice(s).orders.find(o=>o.id===id).kots[0].readyAt;assert.equal(kdsCards(at(s,plus(readyAt,20))).find(c=>c.orderId===id).level,'red','a ready ticket waiting for service ages from Ready');
    s=execute(s,'cashier',{type:'POS_KOT_STATUS',id,kot,status:'Served'});
    assert.equal(card(),undefined,'served tickets leave the display');
  });
  test('[F8-QW-KDS] outlet and station filters',()=>{
    const t0=base();let a,b;let s=seed();
    [s,a]=send(s,t0,'T3',[['m-dal',1]]);
    const outlets=M.posOutletList(s);const other=outlets.find(o=>o.id!=='restaurant');
    assert.ok(kdsCards(s,'restaurant').some(c=>c.orderId===a));
    if(other)assert.ok(!kdsCards(s,other.id).some(c=>c.orderId===a));
    assert.ok(kdsCards(s,'').some(c=>c.orderId===a));
    const st=kdsCards(s).find(c=>c.orderId===a).station;
    assert.ok(kdsCards(s,'',st).some(c=>c.orderId===a));assert.ok(!kdsCards(s,'','No-such-station').some(c=>c.orderId===a));
  });
  test('[F8-QW-KDS] no money key in any card',()=>{
    const t0=base();let [s]=send(seed(),t0,'T3',[['m-dal',1],['m-roti',2]]);
    const cards=kdsCards(s);assert.ok(cards.length>0);
    const keys=o=>Object.entries(o).flatMap(([k,v])=>[k,...(v&&typeof v==='object'?keys(v):[])]);
    for(const c of cards)for(const k of keys(c))if(isNaN(+k))assert.ok(!MONEY.test(k)||k==='station','money-like key: '+k);
    assert.ok(!/₹|Rs\b/.test(JSON.stringify(cards)));
  });
  test('[F8-QW-KDS] role gate: page needs billing (Owner, Manager, Cashier, Restaurant); Housekeeper cannot open it or bump',()=>{
    const s=seed();const n=X.navItems.find(x=>x.id==='kds');assert.ok(n,'nav item kds');assert.deepEqual([].concat(n.p),['billing']);
    assert.equal(X.PAGE_CATEGORY.kds,'cash');
    for(const u of ['owner','manager'])assert.equal(can(s,u,'billing'),true,u);
    assert.equal(can(s,'housekeeper','billing'),false);
    const t0=base();const [t,id]=send(s,t0,'T3',[['m-dal',1]]);const kot=kdsCards(t).find(c=>c.orderId===id).kotId;
    assert.equal(code(()=>execute(t,'housekeeper',{type:'POS_KOT_STATUS',id,kot,status:'Ready'})),'FORBIDDEN');
    const r=s.users.find(u=>u.role==='Restaurant');
    if(r){const t2=execute(t,r.id,{type:'POS_KOT_STATUS',id,kot,status:'Ready'});assert.equal(kdsCards(t2).find(c=>c.orderId===id).status,'Ready');}
  });
}

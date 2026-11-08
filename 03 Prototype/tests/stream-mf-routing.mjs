// F8C phone · bill routing at check-in (src/mobile/frontdesk/routing.ts): the plan of commands is driven against the real SF commands.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
export async function register({test,M,assert}){
  await build({entryPoints:['src/mobile/frontdesk/routing.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mf-routing.mjs',logLevel:'error'});
  const R=await import('./.generated/mf-routing.mjs?'+Date.now());
  const {seed,execute,totals}=M;
  const addDays=(d,n)=>{const x=new Date(d+'T00:00:00Z');x.setUTCDate(x.getUTCDate()+n);return x.toISOString().slice(0,10);};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const reason=fn=>{try{fn();}catch(e){return e.details?.reason??e.code;}return 'none';};
  function stay(){
    let s=seed();
    s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'Deccan Agro Ltd (fictional)',gstin:'27AAPFU0939F1ZV',city:'Latur',creditLimit:5000000});
    const co='company:'+s.groupsX.companies.at(-1).id;
    s=execute(s,'owner',{type:'BOOK',guest:'Phone Route Guest',room:'105',arrival:s.date,departure:addDays(s.date,3),people:1});
    const id=s.bookings.at(-1).id;s=execute(s,'owner',{type:'REGISTER',id});
    return {s,id,co};
  }
  const apply=(s,id,want,k='t')=>{for(const c of R.mfRoutingPlan(bk(s,id),want,k))s=execute(s,'reception',c);return s;};

  test('[F8C-PHONE] a single-folio stay shows guest for both and an unchanged choice plans nothing',()=>{
    const {s,id}=stay();
    assert.deepEqual(R.mfCurrentRouting(bk(s,id)),{room:'guest',extras:'guest'});
    assert.deepEqual(R.mfRoutingPlan(bk(s,id),{room:'guest',extras:'guest'},'k'),[]);
  });

  test('[F8C-PHONE] company pays the room: one SF_FOLIO_OPEN routes room; extras stay with the guest; postings follow',()=>{
    let {s,id,co}=stay();
    const plan=R.mfRoutingPlan(bk(s,id),{room:co,extras:'guest'},'k');
    assert.deepEqual(plan.map(c=>c.type),['SF_FOLIO_OPEN']);assert.deepEqual(plan[0].routes,['room']);
    s=apply(s,id,{room:co,extras:'guest'});
    assert.deepEqual(R.mfCurrentRouting(bk(s,id)),{room:co,extras:'guest'});
    s=execute(s,'reception',{type:'CHECKIN',id});s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'reception',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    const b=bk(s,id);
    assert.ok(b.lines.filter(l=>l.kind==='room').every(l=>l.folio==='B'),'nights on the company folio');
    assert.equal(b.lines.at(-1).folio,'A','extras on the guest folio');
    assert.ok(totals(M.sfView(b,'B')).balance>0);
  });

  test('[F8C-PHONE] company pays both: a single folio with both routes; back to guest removes the empty folio',()=>{
    let {s,id,co}=stay();
    const plan=R.mfRoutingPlan(bk(s,id),{room:co,extras:co},'k');
    assert.equal(plan.length,1);assert.deepEqual(plan[0].routes,['room','extras']);
    s=apply(s,id,{room:co,extras:co});
    assert.equal(bk(s,id).folios.filter(f=>!f.removedAt).length,2);
    const back=R.mfRoutingPlan(bk(s,id),{room:'guest',extras:'guest'},'k2');
    assert.deepEqual(back.map(c=>c.type),['SF_ROUTE_SET','SF_ROUTE_SET','SF_FOLIO_REMOVE']);
    s=apply(s,id,{room:'guest',extras:'guest'},'k2');
    assert.equal(bk(s,id).folios.filter(f=>!f.removedAt).length,1);
    assert.deepEqual(R.mfCurrentRouting(bk(s,id)),{room:'guest',extras:'guest'});
  });

  test('[F8C-PHONE] moving extras from the company to the guest keeps the room on the company',()=>{
    let {s,id,co}=stay();
    s=apply(s,id,{room:co,extras:co});
    s=apply(s,id,{room:co,extras:'guest'},'k3');
    assert.deepEqual(R.mfCurrentRouting(bk(s,id)),{room:co,extras:'guest'});
  });

  test('[F8C-PHONE] two different companies are refused before any command (desk wording); a group member is detected',()=>{
    let {s,id,co}=stay();
    assert.equal(R.mfRoutingProblem(bk(s,id),{room:co,extras:co}),'');
    assert.match(R.mfRoutingProblem(bk(s,id),{room:co,extras:'company:other'}),/at most one company folio/);
    assert.equal(R.mfIsGroupMember(s,bk(s,id)),false);
  });

  test('[F8C-PHONE] the commands still refuse over-limit and check the billing permission (housekeeping / supervisor / maintenance cannot route)',()=>{
    let {s,id,co}=stay();
    const plan=R.mfRoutingPlan(bk(s,id),{room:co,extras:'guest'},'k');
    for(const u of ['housekeeper','supervisor','maintenance']){
      assert.equal(M.can(s,u,'billing'),false,u+' has no billing permission');
      assert.throws(()=>execute(s,u,plan[0]),u+' cannot open a folio');
    }
    assert.equal(M.can(s,'reception','billing'),true);
    // credit limit: a company with a tiny limit refuses the room route (D7 estimate)
    let t=seed();t=execute(t,'owner',{type:'GRP_COMPANY_SAVE',name:'Tiny Co (fictional)',gstin:'27AAPFU0939F1ZV',city:'Latur',creditLimit:100});
    const tc='company:'+t.groupsX.companies.at(-1).id;
    t=execute(t,'owner',{type:'BOOK',guest:'Tiny Guest',room:'105',arrival:t.date,departure:addDays(t.date,3),people:1});
    const tid=t.bookings.at(-1).id;
    assert.throws(()=>execute(t,'reception',R.mfRoutingPlan(bk(t,tid),{room:tc,extras:'guest'},'k')[0]),/credit|limit/i);
  });

  test('[F8C-PHONE] the phone review step mounts the card; the card is gated on billing and stays out of the walk-in flow',()=>{
    const a=readFileSync('src/mobile/frontdesk/Arrivals.tsx','utf8'),c=readFileSync('src/mobile/frontdesk/BillRouting.tsx','utf8');
    assert.ok(/<MfBillRouting b=\{b\}\/>/.test(a));
    assert.ok(/!permission\('billing'\)/.test(c));
    assert.ok(/mfIsGroupMember/.test(c)&&/MF_GROUP_MESSAGE/.test(c));
  });
  test('[F8C-PHONE] an empty company folio is replaced by another company; one with postings is not',()=>{
    let {s,id,co}=stay();
    s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'Second Co (fictional)',gstin:'27AAPFU0939F1ZV',city:'Latur',creditLimit:5000000});
    const co2='company:'+s.groupsX.companies.at(-1).id;
    s=apply(s,id,{room:co,extras:'guest'});
    assert.equal(R.mfRoutingProblem(bk(s,id),{room:co2,extras:'guest'}),'');
    s=apply(s,id,{room:co2,extras:'guest'},'k5');
    assert.deepEqual(R.mfCurrentRouting(bk(s,id)),{room:co2,extras:'guest'});
    s=execute(s,'reception',{type:'CHECKIN',id});s=execute(s,'owner',{type:'POST_NIGHTS',id});
    assert.match(R.mfRoutingProblem(bk(s,id),{room:co,extras:'guest'}),/already has a company folio/);
  });

  test('[F8D-PHONE] another guest in the party: extras to a named person (desk rules), GSTIN optional, round trip, group member untouched',()=>{
    let {s,id,co}=stay();
    const person=R.mfPersonKey('Meera Shah','');
    assert.equal(R.mfRoutingProblem(bk(s,id),{room:'guest',extras:person}),'');
    const plan=R.mfRoutingPlan(bk(s,id),{room:'guest',extras:person},'k');
    assert.deepEqual(plan.map(c=>c.type),['SF_FOLIO_OPEN']);
    assert.deepEqual(plan[0].payer,{kind:'guest',name:'Meera Shah'});assert.deepEqual(plan[0].routes,['extras']);
    s=apply(s,id,{room:'guest',extras:person});
    assert.deepEqual(R.mfCurrentRouting(bk(s,id)),{room:'guest',extras:person});
    assert.equal(R.mfRoutingPlan(bk(s,id),{room:'guest',extras:person},'k').length,0,'unchanged choice plans nothing');
    // company for the room next to the person for extras: two folios, both open
    s=apply(s,id,{room:co,extras:person},'k2');
    assert.equal(bk(s,id).folios.filter(f=>!f.removedAt).length,3);
    // back to the guest removes the empty person folio
    s=apply(s,id,{room:co,extras:'guest'},'k3');
    assert.deepEqual(R.mfCurrentRouting(bk(s,id)),{room:co,extras:'guest'});
    // GSTIN rules and name rules are the desk's
    assert.match(R.mfRoutingProblem(bk(s,id),{room:'guest',extras:R.mfPersonKey('','')}),/name of the person/);
    assert.match(R.mfRoutingProblem(bk(s,id),{room:'guest',extras:R.mfPersonKey('Meera','BADGSTIN')}),/15-character GSTIN/);
    const f2=stay();const gp=R.mfRoutingPlan(bk(f2.s,f2.id),{room:R.mfPersonKey('Meera Shah','27aapfu0939f1zv'),extras:'guest'},'k');
    assert.equal(gp[0].payer.gstin,'27AAPFU0939F1ZV');
  });
  test('[F8D-PHONE] the walk-in flow has the same routing step (billing-gated, opened after BOOK and REGISTER, before CHECKIN)',()=>{
    const a=readFileSync('src/mobile/frontdesk/Arrivals.tsx','utf8');
    assert.ok(/const canRoute=permission\('billing'\)/.test(a)&&/MfPayerFields/.test(a)&&/mfRoutingPlan\(cur,w,/.test(a));
    assert.ok(a.indexOf("if(await doRoute(bid))await doCheckin(bid)")>a.indexOf("'Walk-in guest registered.'"));
  });
  test('[F8D-PHONE] walk-in: a plan built on the fresh reservation (Confirmed) opens the company folio before check-in',()=>{
    let {s,id,co}=stay();
    s=apply(s,id,{room:co,extras:R.mfPersonKey('Meera Shah')},'w');
    s=execute(s,'reception',{type:'CHECKIN',id});s=execute(s,'owner',{type:'POST_NIGHTS',id});
    assert.ok(bk(s,id).lines.filter(l=>l.kind==='room').every(l=>l.folio==='B'));
  });
}


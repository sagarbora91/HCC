// W0-C · stay-restriction UI state (DEC-053): rstState (desktop dialogs, rooming list), the phone wizard's restriction step and
// BOOK payload, and the booking-notes override line. Fixture: YLDR-1 all types, arrivals 7–9 Nov, min 2 nights; YLDR-2 Suite CTA 8 Nov.
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/rst-entry.ts',"export * from '../../src/features/reservations/restriction';\nexport * from '../../src/mobile/frontdesk/logic';");
  await build({entryPoints:['tests/.generated/rst-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/rst.mjs',logLevel:'error'});
  const X=await import('./.generated/rst.mjs');
  const code=fn=>{try{fn();return 'none';}catch(e){return e.code??'other';}};

  test('[A2UI] no violations → submit, no override sent',()=>{
    const st=X.rstState(M.seed(),'reception','Classic','2026-11-07','2026-11-09',null);
    assert.deepEqual(st.violations,[]);assert.equal(st.canSubmit,true);assert.equal(st.restrictionOverride,undefined);
    assert.equal(X.rstState(M.seed(),'reception','All','2026-11-07','2026-11-08').canSubmit,true,'unknown type is not checked');
  });
  test('[A2UI] violations + reception → cannot submit and cannot override',()=>{
    const s=M.seed(),st=X.rstState(s,'reception','Classic','2026-11-07','2026-11-08','Regular guest');
    assert.deepEqual(st.violations.map(v=>v.code),['MIN_STAY']);assert.equal(st.canOverride,false);assert.equal(st.canSubmit,false);assert.equal(st.restrictionOverride,undefined);
    assert.deepEqual(X.rstState(s,'reception','Suite','2026-11-08','2026-11-10').violations.map(v=>v.code),['CTA']);
  });
  test('[A2UI] violations + manager: ticked with reason ≥ 3 → submit with override; short reason or unticked → cannot',()=>{
    const s=M.seed();
    const okSt=X.rstState(s,'manager','Classic','2026-11-07','2026-11-08','  Regular corporate guest ');
    assert.equal(okSt.canOverride,true);assert.equal(okSt.canSubmit,true);assert.equal(okSt.restrictionOverride,'Regular corporate guest');
    assert.equal(X.rstState(s,'manager','Classic','2026-11-07','2026-11-08','ab').canSubmit,false);
    assert.equal(X.rstState(s,'manager','Classic','2026-11-07','2026-11-08',null).canSubmit,false);
    assert.equal(X.rstState(s,'owner','Suite','2026-11-08','2026-11-10','Owner guest').canSubmit,true);
    const n=M.execute(s,'manager',{type:'BOOK',guest:'Override Guest',room:'105',arrival:'2026-11-07',departure:'2026-11-08',people:1,restrictionOverride:okSt.restrictionOverride});
    assert.deepEqual(X.rstOverrideNotes(n.bookings.at(-1)),[{rules:'MIN_STAY YLDR-1',reason:'Regular corporate guest'}]);
    assert.deepEqual(X.rstOverrideNotes({notes:'Late arrival'}),[]);
  });
  test('[A2UI] group rooming in a restricted window: block-level state, override passes through GRP_ROOMING',()=>{
    let s=M.execute(M.seed(),'owner',{type:'GRP_BLOCK_CREATE',name:'Diwali family group',billTo:'guest',arrival:'2026-11-07',departure:'2026-11-08',cutoff:'2026-11-05',allot:{Classic:1,Suite:1}});
    const blk=M.grpSlice(s).blocks.at(-1),rows=[{guest:'Desai Family',type:'Classic',people:2},{guest:'Kale Family',type:'Classic',people:2}];
    const rec=X.rstState(s,'reception',rows.map(r=>r.type),blk.arrival,blk.departure,null);
    assert.equal(rec.violations.length,1,'same rule listed once for repeated types');assert.equal(rec.canSubmit,false);
    const mgr=X.rstState(s,'manager',['Classic'],blk.arrival,blk.departure,'Contracted group rate');assert.equal(mgr.canSubmit,true);
    const n=M.execute(s,'manager',{type:'GRP_ROOMING',block:blk.id,rows:rows.slice(0,1),restrictionOverride:mgr.restrictionOverride});
    assert.match(n.bookings.at(-1).notes,/restriction overridden/);
  });
  test('[A2UI] phone wizard: restricted room step blocks Next for reception; manager override clears it; BOOK payload end-to-end',()=>{
    const s=M.seed();
    const d={...X.mfDraft(s,'105','2026-11-07'),guest:'Phone Override',phone:'+91 90000 44444'};
    assert.equal(d.departure,'2026-11-08');
    assert.ok(X.mfWizardErrors(s,1,d,'reception').restriction);
    assert.ok(X.mfWizardErrors(s,3,d,'reception').restriction,'review re-checks');
    assert.ok(X.mfWizardErrors(s,1,{...d,rstOverride:true,rstReason:'VIP'},'manager').restriction===undefined);
    assert.ok(X.mfWizardErrors(s,1,{...d,rstOverride:true,rstReason:'no'},'manager').restriction,'short reason');
    // Reception's payload carries no override → RESTRICTED.
    const plain=X.mfBookCommand(s,d,false,'rst-1');assert.equal(plain.restrictionOverride,undefined);
    assert.equal(code(()=>M.execute(s,'reception',plain)),'RESTRICTED');
    // Unticked override is never sent, even with a reason typed.
    assert.equal(X.mfBookCommand(s,{...d,rstOverride:false,rstReason:'Typed'},false).restrictionOverride,undefined);
    const od={...d,rstOverride:true,rstReason:'Regular guest'};
    const cmd=X.mfBookCommand(s,od,false,'rst-2');assert.equal(cmd.restrictionOverride,'Regular guest');
    const n=M.execute(s,'manager',cmd);assert.match(n.bookings.at(-1).notes,/restriction overridden \(MIN_STAY YLDR-1\): Regular guest/);
    // Unrestricted stay: override fields ignored, nothing sent.
    const free={...od,arrival:'2026-11-12',departure:'2026-11-13'};
    assert.equal(X.mfBookCommand(s,free,false).restrictionOverride,undefined);
    assert.ok(X.mfWizardErrors(s,1,free,'reception').restriction===undefined);
    // Suite CTA on 8 Nov.
    const suite={...X.mfDraft(s,undefined,'2026-11-08'),type:'Suite',departure:'2026-11-10'};
    assert.ok(X.mfWizardErrors(s,1,suite,'reception').restriction);
  });
}

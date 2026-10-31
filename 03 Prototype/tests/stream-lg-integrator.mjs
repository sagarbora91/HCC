// LG integrator · coordinator rulings LG-R6 (a guest save that omits dob keeps it) and LG-R8 (an imported opening draft may exceed the
// 500-line hand-entry cap: paged, approved whole). Names prefixed [LG-INT]. Fictional data only.
export function register({test,M,assert}){
  const full={name:'Sonal Kulkarni',phone:'+91 98200 05555',email:'sonal@example.test',address:'5 Demo Lane, Latur (fictional)',nationality:'Indian',idType:'Passport (demo)',idLast4:'K555',preferences:'',notes:'',party:[]};
  const code=f=>{try{f();return 'OK';}catch(e){return e.code;}};
  test('[LG-INT] LG-R6: dob saved, kept by a save that omits it, cleared only by an explicit empty value; bad dates refused',()=>{
    let s=M.seed();
    assert.equal(code(()=>M.execute(s,'reception',{type:'GUEST_SAVE',guest:{...full,dob:'12/03/1985'}})),'VALIDATION');
    assert.equal(code(()=>M.execute(s,'reception',{type:'GUEST_SAVE',guest:{...full,dob:'1899-12-31'}})),'VALIDATION');
    s=M.execute(s,'reception',{type:'GUEST_SAVE',guest:{...full,dob:'1985-03-12'}});
    const g=s.guests.find(x=>x.phone===full.phone);assert.equal(g.dob,'1985-03-12');
    s=M.execute(s,'reception',{type:'GUEST_SAVE',id:g.id,guest:{...full,notes:'edited'}});
    let h=s.guests.find(x=>x.id===g.id);assert.equal(h.dob,'1985-03-12');assert.equal(h.notes,'edited');
    s=M.execute(s,'reception',{type:'GUEST_SAVE',id:g.id,guest:{...full,dob:''}});
    h=s.guests.find(x=>x.id===g.id);assert.equal('dob' in h,false);
    assert.equal(M.guestFieldLabels.dob,'Date of birth');
    assert.equal(M.guestDobCleared({dob:' '}),true);assert.equal(M.guestDobCleared({}),false);
  });
  const many=n=>Array.from({length:n},(_,i)=>({account:`1120/UD-DEMO${String(i).padStart(4,'0')}`,debit:100*(i+1),credit:0}));
  test('[LG-INT] LG-R8: hand entry keeps the 500-line cap; an imported 650-line draft is paged, read-only and approved whole',()=>{
    let s=M.seed();
    assert.equal(code(()=>M.execute(s,'manager',{type:'ACC_OPENING_SAVE',asOf:'2026-04-01',lines:many(501)})),'VALIDATION');
    const lines=many(650),total=lines.reduce((a,l)=>a+l.debit,0);
    lines.push({account:'3100',debit:0,credit:total});
    // as the importer writes it (server: IOpeningEntryStore.SaveDraftAsync, outside the hand-entry validation)
    s=structuredClone(s);s.accountingX={...(s.accountingX??{}),opening:{asOf:'2026-04-01',lines,status:'draft',by:'owner',at:s.clock}};
    assert.ok(M.accOpening(s),'opening slice');
    assert.equal(M.accOpeningOverHandCap(M.accOpening(s)),true);
    assert.ok(!M.accOpeningProblems(s).some(p=>p.severity==='error'),'no error on the saved oversized draft');
    const p0=M.accOpeningPageOf(lines,0),p6=M.accOpeningPageOf(lines,99);
    assert.equal(p0.pages,7);assert.equal(p0.items.length,100);assert.equal(p6.page,6);assert.equal(p6.items.length,51);
    s=M.execute(s,'owner',{type:'ACC_OPENING_APPROVE'});
    assert.equal(M.accOpening(s).status,'approved');assert.equal(M.accOpening(s).lines.length,651);
  });
}

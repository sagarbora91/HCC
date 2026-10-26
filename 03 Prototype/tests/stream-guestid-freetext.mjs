// Audit A07-05: a full ID number typed into free text (guest notes, preferences, address; a capture discard reason) is refused with one clear
// message. Same table of cases as the server (08 Production/tests/Hcc.GstIds.Tests/GuestIds/GuestIdUnitTests.cs
// Free_text_ID_shapes_full_Aadhaar_or_passport_is_caught_but_phones_and_booking_refs_are_not, and Hcc.Stays.Tests RulesGoldenTests).
export function register({test,M,assert}){
  const execute=M.execute;
  // 11 digits + Verhoeff check digit; the leading 1 marks it fictional.
  const body='12345678901',AAD=body+M.guestVerhoeffDigit(body);
  const spaced=AAD.slice(0,4)+' '+AAD.slice(4,8)+' '+AAD.slice(8);
  const MSG='This looks like an ID number. Store ID numbers only in the ID section.';
  const badChecksum=AAD.slice(0,11)+String((+AAD[11]+1)%10);
  const full={name:'Free Text Guest',phone:'+91 98200 05555',email:'ft@example.test',address:'5 Demo Lane, Latur (fictional)',nationality:'Indian',idType:'Passport (demo)',idNumber:'Z7654321'};
  const code=f=>{try{f();}catch(e){return [e.code,e.message];}return ['none',''];};

  test('[GIDTXT] full Aadhaar (spaced or not) or a passport number is caught; phones, booking refs and GSTINs are not',()=>{
    for(const t of [AAD,spaced,AAD.slice(0,4)+'-'+AAD.slice(4,8)+'-'+AAD.slice(8),'Guest showed Aadhaar '+spaced+' at the desk','aadhaar:'+AAD+'.',
      'Z1234567','passport z7654321 copied','(Z7654321)'])assert.equal(M.guestLooksLikeIdNumber(t),true,t);
    for(const t of [null,undefined,'','Prefers a high floor','9820005555','Call 98200 05555 after 6 pm','+91 98200 05555','+'+AAD,'Booking HCC-1041','OTA ref 4471029384',
      'GSTIN 27ABCDE1234F1Z5',badChecksum,'9'+AAD,AAD+'7','Z123456','ZZ1234567','Z12345678','Room 105, 2 adults'])assert.equal(M.guestLooksLikeIdNumber(t),false,String(t));
    assert.equal(M.GUEST_FREE_TEXT_ID_MESSAGE,MSG);
  });
  test('[GIDTXT] guestValidate refuses an ID number in notes, preferences and address (server GuestRules.Validate, same order)',()=>{
    assert.equal(M.guestValidate({...full,notes:'Aadhaar '+AAD},true).notes,MSG);
    assert.equal(M.guestValidate({...full,preferences:spaced}).preferences,MSG);
    assert.equal(M.guestValidate({...full,address:'5 Demo Lane, passport Z1234567'},true).address,MSG);
    assert.deepEqual(Object.keys(M.guestValidate({...full,notes:spaced,preferences:AAD})),['preferences','notes']);
    assert.equal(M.guestFieldLabels.notes,'Notes');assert.equal(M.guestFieldLabels.preferences,'Preferences');
    assert.deepEqual(M.guestValidate({...full,notes:'Call 9820005555 before arrival · booking HCC-1041',preferences:'Bill to GSTIN 27ABCDE1234F1Z5'},true),{});
    assert.deepEqual(M.guestValidate({...full,notes:'Second phone +91 98200 05556'},true),{});
  });
  test('[GIDTXT] GUEST_SAVE and REGISTER refuse it with VALIDATION and store nothing; the message never echoes the number',()=>{
    const s=M.seed();const before=JSON.stringify(s.guests);
    const [c,m]=code(()=>execute(s,'reception',{type:'GUEST_SAVE',guest:{...full,notes:'ID '+spaced}}));
    assert.equal(c,'VALIDATION');assert.equal(m,MSG);assert.ok(!m.includes(AAD.slice(-4)));
    assert.equal(JSON.stringify(s.guests),before);
    const [c2,m2]=code(()=>execute(s,'reception',{type:'GUEST_SAVE',guest:{...full,notes:spaced,preferences:AAD}}));
    assert.equal(c2,'VALIDATION');assert.equal(m2,MSG+' (1 more: Notes)');
    const id=s.bookings.find(b=>b.status==='Confirmed').id;
    assert.equal(code(()=>execute(s,'reception',{type:'REGISTER',id,guest:{...full,address:'Flat 2, Z7654321 Road'}}))[0],'VALIDATION');
    const ok=execute(s,'reception',{type:'GUEST_SAVE',guest:{...full,notes:'Call 9820005555 · booking HCC-1041'}});
    assert.equal(ok.guests.find(g=>g.name==='Free Text Guest').notes,'Call 9820005555 · booking HCC-1041');
  });
  test('[GIDTXT] a capture discard reason may not carry an ID number',()=>{
    let s=execute(M.seed(),'reception',{type:'MSG_DOC_EXTRACT',sample:'S-ID-1'});const d=s.messagingX.docs.at(-1);
    assert.deepEqual(code(()=>execute(s,'reception',{type:'MSG_DOC_DISCARD',id:d.id,reason:'Wrong number '+spaced})),['VALIDATION',MSG]);
    s=execute(s,'reception',{type:'MSG_DOC_DISCARD',id:d.id,reason:'Wrong guest, call 9820005555'});assert.equal(s.messagingX.docs.at(-1).status,'Discarded');
  });
  test('[GIDTXT] no fixture guest trips the rule (seeded profiles stay complete)',()=>{
    for(const g of M.seed().guests||[])for(const k of ['address','preferences','notes'])assert.equal(M.guestLooksLikeIdNumber(g[k]),false,g.id+' '+k);
  });
}

// DEC-193 (ACC-3 lane partyid, contract §8): every guest on a registration has their own ID; the photo is optional everywhere.
// Rules (Adult / Senior required, Child / Infant not — one constant), REGISTER / CHECKIN refusal "ID missing for <name>", per-member masking,
// audited per-member "Show ID" (the seq, never the number), edits keep a member's stored number, the form's party rows, the check-in chips.
// Fixtures use only obviously fictional numbers (Aadhaar starting with 1, passport Z…).
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
await mkdir('tests/.generated',{recursive:true});
await writeFile('tests/.generated/guestid-party-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can} from '../../src/services/mock';
import {GuestForm,emptyDraft,draftOf} from '../../src/features/guests/GuestForm';
import {PartyIdChips} from '../../src/features/guests/CheckIn';
import {GuestPartyIdValue} from '../../src/features/guests/GuestId';
const ctx=(s:any,uid:string,mobile:boolean)=>({s,uid,user:s.users.find((u:any)=>u.id===uid),busy:false,run:async()=>true,open(){},closeModal(){},toast(){},go(){},booking(){},setDirty(){},search:'',setSearch(){},modal:{key:'k'},permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile}} as any);
const wrap=(s:any,uid:string,mobile:boolean,el:any)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile)}>{el}</HccContext.Provider>);
export const form=(s:any,uid:string,party:any[],mobile=false,gid?:string)=>wrap(s,uid,mobile,<GuestForm initial={gid?draftOf(s.guests.find((g:any)=>g.id===gid)):emptyDraft({party})} guestId={gid} registration={true} submitLabel="Save" onSubmit={()=>{}}/>);
export const chips=(s:any,uid:string,g:any)=>wrap(s,uid,false,<PartyIdChips g={g}/>);
export const partyId=(s:any,uid:string,g:any,seq:number)=>wrap(s,uid,false,<GuestPartyIdValue g={g} seq={seq}/>);
`);
await build({entryPoints:['tests/.generated/guestid-party-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/guestid-party.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
const UI=await import('./.generated/guestid-party.mjs');

export function register({test,M,assert}){
  const execute=M.execute;
  const err=f=>{try{f();}catch(e){return {code:e.code||'?',msg:e.message};}return {code:'none',msg:''};};
  const body='10293847561',AAD=body+M.guestVerhoeffDigit(body);   // fictional: starts with 1
  const PASS='Z7000123';                                            // fictional: Z is never issued
  const main={name:'Party Lead',phone:'+91 98200 07777',email:'pl@example.test',address:'7 Demo Lane, Latur (fictional)',nationality:'Indian',idType:'Passport (demo)',idNumber:'Z1000007'};
  const adult=(over={})=>({name:'Asha Lead',ageBand:'Adult',relation:'Spouse',...over});
  const child={name:'Om Lead',ageBand:'Child',relation:'Son'};
  const confirmed=s=>s.bookings.find(b=>b.status==='Confirmed'&&b.arrival<=s.date&&!b.registered);

  test('[ACC3-PARTYID] one constant: Adult / Senior need an ID, Child / Infant do not (OP-21 default)',()=>{
    assert.deepEqual([...M.GUEST_PARTY_ID_REQUIRED_BANDS],['Adult','Senior']);
    assert.deepEqual(M.guestAgeBands.map(M.guestPartyIdRequired),[true,false,false,true]);
    assert.deepEqual(M.guestPartyIdMissing([adult(),child,{name:'Nana',ageBand:'Senior',relation:'',idType:'Passport (demo)',idLast4:'0123'}]),['Asha Lead']);
    assert.equal(M.guestPartyIdMessage([adult()]),'ID missing for Asha Lead');
    assert.equal(M.guestPartyIdMessage([child]),'');
  });

  test('[ACC3-PARTYID] validation per member: type needed, per-type format, messages never echo the number',()=>{
    const v=party=>M.guestValidate({...main,party},true).party||'';
    assert.equal(v([adult()]),'ID missing for Asha Lead');
    assert.equal(v([child]),'');
    assert.equal(v([adult({idNumber:PASS})]),'Party member 1: choose the ID type.');
    const bad=v([adult({idType:'Aadhaar (demo)',idNumber:AAD.slice(0,11)+String((+AAD[11]+1)%10)})]);
    assert.match(bad,/^Party member 1: .*checksum/);assert.ok(!bad.includes(AAD.slice(0,8)));
    assert.equal(v([adult({idType:'Aadhaar (demo)',idNumber:AAD})]),'');
    assert.equal(v([adult({idType:'Passport (demo)',idLast4:'0123'})]),'');   // last 4 only (like pre-DEC-068 profiles)
    // not a registration: a missing ID is not an error yet (profiles may be saved incomplete)
    assert.equal(M.guestValidate({...main,party:[adult()]},false).party,undefined);
  });

  test('[ACC3-PARTYID] REGISTER is refused while an adult has no ID; stored per member (last 4 derived), children need none',()=>{
    let s=M.seed();const b=confirmed(s);assert.ok(b);
    const r=err(()=>execute(s,'reception',{type:'REGISTER',id:b.id,guest:{...main,party:[adult(),child]},key:'p-r1'}));
    assert.deepEqual(r,{code:'VALIDATION',msg:'ID missing for Asha Lead'});
    s=execute(s,'reception',{type:'REGISTER',id:b.id,guest:{...main,party:[adult({idType:'Aadhaar (demo)',idNumber:AAD.slice(0,4)+' '+AAD.slice(4)}),child]},key:'p-r2'});
    const g=s.guests.find(x=>x.id===s.bookings.find(x=>x.id===b.id).guestId);
    assert.deepEqual(g.party[0],{name:'Asha Lead',ageBand:'Adult',relation:'Spouse',idType:'Aadhaar (demo)',idLast4:AAD.slice(-4),idNumber:AAD});
    assert.deepEqual(g.party[1],{name:'Om Lead',ageBand:'Child',relation:'Son'});
    assert.ok(!JSON.stringify(s.audit).includes(AAD),'audit holds no number');
  });

  test('[ACC3-PARTYID] CHECKIN is refused with "ID missing for <name>" for a registered party without IDs (old data)',()=>{
    let s=M.seed();const b=confirmed(s);
    s=execute(s,'reception',{type:'REGISTER',id:b.id,guest:{...main,party:[adult({idType:'Passport (demo)',idNumber:PASS})]},key:'p-c1'});
    const g=s.guests.find(x=>x.id===s.bookings.find(x=>x.id===b.id).guestId);
    // a profile saved before DEC-193: the member has no ID on file
    const old=structuredClone(s);const og=old.guests.find(x=>x.id===g.id);delete og.party[0].idType;delete og.party[0].idLast4;delete og.party[0].idNumber;
    const room=old.rooms.find(r=>r.id===b.room);room.hk='Ready';room.blocked=false;
    assert.deepEqual(err(()=>execute(old,'reception',{type:'CHECKIN',id:b.id})),{code:'VALIDATION',msg:'ID missing for Asha Lead'});
    const ok=structuredClone(s);const r2=ok.rooms.find(r=>r.id===b.room);r2.hk='Ready';r2.blocked=false;
    const c=err(()=>execute(ok,'reception',{type:'CHECKIN',id:b.id}));
    assert.ok(c.msg!=='ID missing for Asha Lead',c.msg);
  });

  test('[ACC3-PARTYID] edit without retyping keeps a member\'s number; a different person at that seq loses it',()=>{
    let s=M.seed();
    s=execute(s,'reception',{type:'GUEST_SAVE',guest:{...main,party:[adult({idType:'Passport (demo)',idNumber:PASS})]},verified:true});
    const g=s.guests.find(x=>x.name==='Party Lead');const cur=()=>s.guests.find(x=>x.id===g.id);
    const d=M.guestClean({...main,idNumber:'',party:[{name:'Asha Lead',ageBand:'Adult',relation:'Wife',idType:'Passport (demo)',idLast4:PASS.slice(-4)}]});
    s=execute(s,'reception',{type:'GUEST_SAVE',id:g.id,version:cur().version,guest:{...d,notes:'edited'}});
    assert.equal(cur().party[0].idNumber,PASS);assert.equal(cur().party[0].relation,'Wife');
    s=execute(s,'reception',{type:'GUEST_SAVE',id:g.id,version:cur().version,guest:{...main,idNumber:'',party:[{name:'Someone Else',ageBand:'Child',relation:''}]}});
    assert.equal(cur().party[0].idNumber,undefined);assert.equal(cur().party[0].idLast4,undefined);
  });

  test('[ACC3-PARTYID] masking per member and audited per-member Show ID (the seq, never the number)',()=>{
    let s=M.seed();
    s=execute(s,'reception',{type:'GUEST_SAVE',guest:{...main,party:[child,adult({idType:'Passport (demo)',idNumber:PASS})]},verified:true});
    const g=s.guests.find(x=>x.name==='Party Lead');
    const vo=M.guestView(s,'owner',g),vc=M.guestView(s,'cashier',g);
    assert.equal(vo.party[1].idMasked,'XXXX-XXXX-'+PASS.slice(-4));assert.equal(vo.party[1].idOnFile,true);assert.equal(vo.party[1].idRequired,true);
    assert.equal(vo.party[0].idRequired,false);
    assert.equal(vc.party[1].idMasked,'Restricted');assert.equal(vc.party[1].idLast4,'Restricted');
    assert.ok(!JSON.stringify([vo,vc]).includes(PASS),'views never carry the number');
    assert.equal(err(()=>execute(s,'cashier',{type:'GUEST_ID_VIEW',id:g.id,seq:2})).code,'FORBIDDEN');
    assert.equal(err(()=>execute(s,'owner',{type:'GUEST_ID_VIEW',id:g.id,seq:1})).code,'NOT_FOUND');
    assert.equal(err(()=>execute(s,'owner',{type:'GUEST_ID_VIEW',id:g.id,seq:7})).code,'NOT_FOUND');
    const n=s.audit.length;const s2=execute(s,'manager',{type:'GUEST_ID_VIEW',id:g.id,seq:2});
    assert.equal(s2.audit.length,n+1);assert.ok(s2.audit[0].detail.includes('party member 2'));
    assert.ok(!JSON.stringify(s2.audit).includes(PASS)&&!s2.audit[0].detail.includes(PASS.slice(-4)));
    assert.equal(M.guestRevealPartyId(s2,'manager',g,2),PASS);assert.equal(M.guestRevealPartyId(s2,'cashier',g,2),undefined);
  });

  test('[ACC3-PARTYID] GuestForm party rows: ID type + number on desktop and phone (48 px), photo optional; chips and masked value',()=>{
    let s=M.loadScenario('BASE');
    for(const mobile of [false,true]){
      const h=UI.form(s,'reception',[adult(),child],mobile);
      assert.ok(h.includes('name="party-0-idType"')&&h.includes('name="party-0-idNumber"')&&h.includes('name="party-1-idNumber"'),'ID fields per member');
      assert.ok(h.includes('min-height:48px'),'48 px targets');
      assert.ok(h.includes('A photo is optional')&&h.includes('An ID photo is optional'),'photo labelled optional');
      assert.ok(h.includes('ID number (optional)'),'child ID optional');
    }
    s=execute(s,'reception',{type:'GUEST_SAVE',guest:{...main,party:[adult({idType:'Passport (demo)',idNumber:PASS}),{name:'Ravi Lead',ageBand:'Senior',relation:'Father'}]}});
    const g=s.guests.find(x=>x.name==='Party Lead');
    const edit=UI.form(s,'reception',[],false,g.id);
    assert.ok(edit.includes('ID number (blank = keep)')&&edit.includes('XXXX-XXXX-'+PASS.slice(-4))&&!edit.includes(PASS),'edit form masks and never carries the number');
    const c=UI.chips(s,'reception',g);
    assert.ok(c.includes('ID missing · Ravi Lead')&&!c.includes('Asha Lead')&&c.includes('pill red'));
    assert.equal(UI.chips(s,'reception',{...g,party:[child]}),'');
    const v=UI.partyId(s,'reception',g,1);assert.ok(v.includes('XXXX-XXXX-'+PASS.slice(-4))&&v.includes('Show ID')&&!v.includes(PASS));
    assert.ok(UI.partyId(s,'reception',g,2).includes('ID missing'));
    assert.equal(UI.partyId(s,'cashier',g,1),'Restricted');
  });

  test('[ACC3-PARTYID] older saves round-trip unchanged (no SCHEMA bump); no rule anywhere requires a photo',()=>{
    const s=M.loadScenario('BASE');const before=JSON.stringify(s.guests.map(g=>g.party));
    assert.equal(JSON.stringify(s.guests.map(g=>M.guestClean(g).party)),before);
    const rows=(s.frontDeskMasters?.idTypes)||[];for(const r of rows)if(r.needsPhoto)assert.equal(M.guestValidate({...main,idType:r.name,idLast4:'0123',idNumber:''},true).idType,undefined);
  });
}

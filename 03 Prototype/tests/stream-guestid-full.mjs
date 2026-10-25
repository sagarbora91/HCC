// DEC-068: full guest ID number — per-type validation, storage, strict display masking, audited "Show ID", no leaks, old last-4 profiles.
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
await mkdir('tests/.generated',{recursive:true});
await writeFile('tests/.generated/guestid-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can} from '../../src/services/mock';
import {GuestDetails,RegistrationCard} from '../../src/features/guests/GuestProfile';
import {MfGuestProfile} from '../../src/mobile/frontdesk/Guests';
import {GuestForm,emptyDraft} from '../../src/features/guests/GuestForm';
const ctx=(s:any,uid:string,mobile:boolean)=>({s,uid,user:s.users.find((u:any)=>u.id===uid),busy:false,run:async()=>true,open(){},closeModal(){},toast(){},go(){},booking(){},setDirty(){},search:'',setSearch(){},modal:{key:'k'},permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile}} as any);
const wrap=(s:any,uid:string,mobile:boolean,el:any)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile)}>{el}</HccContext.Provider>);
export const details=(s:any,uid:string,g:any)=>wrap(s,uid,false,<GuestDetails g={g}/>);
export const regcard=(s:any,uid:string,id:string)=>wrap(s,uid,false,<RegistrationCard id={id}/>);
export const phone=(s:any,uid:string,id:string)=>wrap(s,uid,true,<MfGuestProfile id={id}/>);
export const form=(s:any,uid:string,type:string,gid?:string)=>wrap(s,uid,false,<GuestForm initial={emptyDraft({idType:type})} guestId={gid} registration={true} submitLabel="Save" onSubmit={()=>{}}/>);
`);
await build({entryPoints:['tests/.generated/guestid-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/guestid-ui.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
const UI=await import('./.generated/guestid-ui.mjs');

export function register({test,M,assert}){
  const execute=M.execute;
  const code=f=>{try{f();}catch(e){return e.code||e.message;}return 'none';};
  const full={name:'Test Guest',phone:'+91 98200 05555',email:'tg@example.test',address:'5 Demo Lane, Latur (fictional)',nationality:'Indian',idType:'Aadhaar (demo)'};
  // 11 digits + Verhoeff check digit; the leading 1 marks it fictional (real Aadhaar numbers never start with 0 or 1).
  const body='12345678901',AAD=body+M.guestVerhoeffDigit(body);
  const mk=(s,uid,over={},extra={})=>execute(s,uid,{type:'GUEST_SAVE',guest:{...full,...over},...extra});
  const find=(s,name)=>s.guests.find(g=>g.name===name);

  test('[GID] Verhoeff: known vector, round trip, single-digit errors caught',()=>{
    assert.equal(M.guestVerhoeff('2363'),true);assert.equal(M.guestVerhoeff('2364'),false);
    assert.equal(M.guestVerhoeff(AAD),true);
    for(let i=0;i<12;i++){const bad=AAD.slice(0,i)+String((+AAD[i]+1)%10)+AAD.slice(i+1);assert.equal(M.guestVerhoeff(bad),false,'digit '+i);}
  });
  test('[GID] validation per ID type',()=>{
    const err=(t,n)=>M.guestIdNumberError(t,n);
    assert.equal(err('Aadhaar (demo)',AAD),'');assert.equal(err('Aadhaar (demo)',AAD.slice(0,4)+' '+AAD.slice(4,8)+'-'+AAD.slice(8)),'');
    assert.match(err('Aadhaar (demo)','12345678901'),/12 digits/);assert.match(err('Aadhaar (demo)','1234567890AB'),/12 digits/);
    assert.match(err('Aadhaar (demo)',AAD.slice(0,11)+String((+AAD[11]+1)%10)),/checksum/);
    assert.equal(err('Passport (demo)','z1234567'),'');assert.match(err('Passport (demo)','12345678'),/Passport/);assert.match(err('Passport (demo)','Z123456'),/Passport/);
    assert.equal(err('Driving licence (demo)','MH-12 20110012345'),'');assert.match(err('Driving licence (demo)','MH12'),/licence/);
    assert.equal(err('Voter ID (demo)','ABC1234567'),'');assert.match(err('Voter ID (demo)','AB12345678'),/Voter/);
    assert.equal(err('PAN (demo)','ABCDE1234F'),'');assert.match(err('PAN (demo)','ABCDE12345'),/PAN/);
    assert.equal(err('Other (demo)','REF-9876'),'');assert.match(err('Other (demo)','AB'),/4 to 20/);
    assert.equal(err('Aadhaar (demo)',''),'');
    assert.ok(M.guestValidate({...full,idNumber:'123'},true).idNumber);
    assert.equal(code(()=>mk(M.seed(),'reception',{idNumber:'123'})),'VALIDATION');
    assert.equal(code(()=>mk(M.seed(),'reception',{idType:'PAN (demo)',idNumber:AAD})),'VALIDATION');
  });
  test('[GID] registration needs an ID number; the full number is stored normalised, last 4 derived',()=>{
    assert.ok(M.guestValidate({...full,idType:'Aadhaar (demo)'},true).idNumber,'registration requires the number');
    let s=mk(M.seed(),'reception',{idNumber:AAD.slice(0,4)+' '+AAD.slice(4,8)+' '+AAD.slice(8)});
    const g=find(s,'Test Guest');assert.equal(g.idNumber,AAD);assert.equal(g.idLast4,AAD.slice(-4));
    for(const [t,n] of [['Passport (demo)','Z7654321'],['Driving licence (demo)','ZZ0120110012345'],['Voter ID (demo)','ZZZ1234567'],['PAN (demo)','ZZZZZ1234Z'],['Other (demo)','FICT0001']]){
      s=mk(s,'owner',{name:'G '+t,idType:t,idNumber:n});const x=find(s,'G '+t);assert.equal(x.idNumber,n);assert.equal(x.idLast4,n.slice(-4));
    }
  });
  test('[GID] REGISTER stores the full number; editing without retyping keeps it; changed type needs a new number',()=>{
    let s=M.seed();const id=s.bookings.find(b=>b.status==='Confirmed').id;
    s=execute(s,'reception',{type:'REGISTER',id,guest:{...full,idNumber:AAD},key:'gid-reg'});
    const gid=s.bookings.find(b=>b.id===id).guestId,cur=()=>s.guests.find(x=>x.id===gid);const g=cur();assert.equal(g.idNumber,AAD);
    s=execute(s,'reception',{type:'GUEST_SAVE',id:g.id,version:g.version,guest:{...full,notes:'edited',idLast4:g.idLast4,idNumber:''}});
    assert.equal(cur().idNumber,AAD);assert.equal(cur().idLast4,AAD.slice(-4));
    const g2=cur();
    assert.equal(code(()=>execute(s,'reception',{type:'GUEST_SAVE',id:g2.id,version:g2.version,guest:{...full,idType:'PAN (demo)',idLast4:g2.idLast4}})),'VALIDATION');
    s=execute(s,'reception',{type:'GUEST_SAVE',id:g2.id,version:g2.version,guest:{...full,idType:'PAN (demo)',idNumber:'ZZZZZ1234Z'}});
    assert.equal(cur().idNumber,'ZZZZZ1234Z');
  });
  test('[GID] fictional generator: obviously fake, valid, deterministic; BASE and FY26 profiles carry valid full numbers',()=>{
    for(let n=1;n<40;n++){const a=M.guestFictionalId('Aadhaar (demo)',n);assert.ok(/^1\d{11}$/.test(a)&&M.guestVerhoeff(a),a);assert.equal(a,M.guestFictionalId('Aadhaar (demo)',n));}
    assert.equal(M.guestFictionalId('Aadhaar (demo)',3,'4101').slice(-4),'4101');
    for(const sc of ['BASE','FY26']){
      const s=M.loadScenario(sc);assert.ok(s.guests.length>=10);
      for(const g of s.guests){
        assert.ok(g.idNumber,sc+' '+g.id);assert.equal(M.guestIdNumberError(g.idType,g.idNumber),'',g.idType+' '+g.idNumber);assert.equal(g.idLast4,g.idNumber.slice(-4));
        assert.ok(/^(1\d{11}|Z\d{7}|ZZ\d{13}|ZZZ\d{7}|ZZZZZ\d{4}Z|FICT\d{8})$/.test(g.idNumber),'not an obviously fake pattern: '+g.idNumber);
        assert.equal(M.guestComplete(g),true);
      }
    }
  });
  test('[GID] guestView: masked last 4 for ID-view roles, Restricted for others; the full number is never in the view',()=>{
    const s=M.loadScenario('BASE');const g=s.guests[0];const n=g.idNumber;
    for(const u of s.users){
      const v=M.guestView(s,u.id,g);assert.equal('idNumber' in v,false);assert.ok(!JSON.stringify(v).includes(n),u.id);
      if(M.can(s,u.id,'checkin')){assert.equal(v.idMasked,'XXXX-XXXX-'+g.idLast4);assert.equal(v.idOnFile,true);assert.equal(v.canShowId,true);assert.equal(M.guestRevealId(s,u.id,g),n);}
      else{assert.equal(v.idMasked,'Restricted');assert.equal(v.idLast4,'Restricted');assert.equal(v.canShowId,false);assert.equal(M.guestRevealId(s,u.id,g),undefined);}
    }
    for(const r of ['owner','manager','reception'])assert.equal(M.can(s,r,'checkin'),true);
    for(const r of ['cashier','housekeeper','supervisor'])assert.equal(M.can(s,r,'checkin'),false);
  });
  test('[GID] pages (desktop details, registration card, phone profile) show only the masked ID for every role',()=>{
    const s=M.loadScenario('BASE');const b=s.bookings.find(b=>b.guestId&&b.registered)||s.bookings.find(b=>b.guestId);const g=s.guests.find(x=>x.id===b.guestId);
    for(const u of s.users){
      const outs=[UI.details(s,u.id,g),UI.regcard(s,u.id,b.id),UI.phone(s,u.id,g.id)];
      for(const h of outs){assert.ok(!h.includes(g.idNumber),'full number rendered for '+u.id);assert.ok(!h.includes(g.idNumber.replace(/(\d{4})(?=\d)/g,'$1 ')),'grouped number rendered for '+u.id);}
      if(M.can(s,u.id,'checkin')){assert.ok(outs[0].includes('XXXX-XXXX-'+g.idLast4),u.id);assert.ok(outs[0].includes('Show ID'));assert.ok(outs[1].includes('XXXX-XXXX-'+g.idLast4));assert.ok(outs[2].includes('Show ID'));}
      else{assert.ok(!outs[0].includes('Show ID')&&!outs[0].includes(g.idLast4),u.id);assert.ok(outs[0].includes('Restricted'));assert.ok(!outs[1].includes('XXXX-XXXX-'));}
    }
  });
  test('[GID] Aadhaar note on the form (non-blocking), only for Aadhaar',()=>{
    const s=M.loadScenario('BASE');
    const a=UI.form(s,'owner','Aadhaar (demo)');assert.ok(a.includes('Stored in full by owner decision DEC-068 — confirm legality with your CA/lawyer.'));assert.ok(a.includes('name="idNumber"'));assert.ok(!a.includes('name="idLast4"'));
    assert.ok(!UI.form(s,'owner','Passport (demo)').includes('DEC-068 — confirm legality'));
    const e=UI.form(s,'owner','Aadhaar (demo)',s.guests[0].id);assert.ok(e.includes('XXXX-XXXX-'+s.guests[0].idLast4));assert.ok(!e.includes(s.guests[0].idNumber));
  });
  test('[GID] Show ID: permission and audit entry (who/when, never the number)',()=>{
    const s0=M.loadScenario('BASE'),g=s0.guests[0];
    for(const u of ['cashier','housekeeper','supervisor'])assert.equal(code(()=>execute(s0,u,{type:'GUEST_ID_VIEW',id:g.id})),'FORBIDDEN');
    for(const u of ['owner','manager','reception']){
      const n=s0.audit.length,s=execute(s0,u,{type:'GUEST_ID_VIEW',id:g.id});const a=s.audit[0];
      assert.equal(s.audit.length,n+1);assert.equal(a.action,'GUEST ID VIEW');assert.ok(a.detail.includes(g.id));assert.equal(a.actor,s0.users.find(x=>x.id===u).name);assert.ok(a.at);
      assert.ok(!JSON.stringify(s.audit).includes(g.idNumber)&&!a.detail.includes(g.idLast4));
    }
    assert.equal(code(()=>execute(s0,'owner',{type:'GUEST_ID_VIEW',id:'G-NOPE'})),'NOT_FOUND');
    const old=M.loadScenario('BASE');delete old.guests[0].idNumber;assert.equal(code(()=>execute(old,'owner',{type:'GUEST_ID_VIEW',id:old.guests[0].id})),'NOT_FOUND');
  });
  test('[GID] no full number in audit, exports, messages, assistant answers (FY26 + new registrations)',()=>{
    let s=M.loadScenario('FY26');
    s=mk(s,'reception',{idNumber:AAD});
    const id=s.bookings.find(b=>b.status==='Confirmed'&&b.arrival<=s.date&&!b.registered)?.id;
    if(id)s=execute(s,'reception',{type:'REGISTER',id,guest:{...full,name:'Second Guest',phone:'+91 98200 06666',idNumber:'Z7654321',idType:'Passport (demo)'},key:'leak-reg'});
    s=execute(s,'owner',{type:'GUEST_ID_VIEW',id:find(s,'Test Guest').id});
    const nums=s.guests.map(g=>g.idNumber).filter(Boolean);assert.ok(nums.length>100);
    const leak=(text,what)=>{for(const n of nums)if(text.includes(n))assert.fail(what+' contains a full ID number '+n);};
    const {guests,...rest}=s;leak(JSON.stringify(rest),'state outside guest profiles');
    leak(JSON.stringify(s.audit),'audit');
    leak(M.cbCsv(s,'2026-04-01','2026-10-25'),'cash-book CSV');leak(JSON.stringify(M.reportPack(s,'2026-04-01','2026-10-25')),'report pack');
    for(const b of s.bookings.slice(0,300))for(const t of M.msgTemplates(s))leak(M.msgRender(t.body+' '+t.subject,M.msgValues(s,b)).text,'message '+t.id);
    for(const u of ['owner','manager','reception','cashier'])for(const t of M.aiTopics)leak(JSON.stringify(M.aiAnswer(s,u,t.question)),'assistant '+t.intent);
    leak(JSON.stringify(M.aiBriefing(s,'owner')),'briefing');
    for(const u of s.users.map(u=>u.id))for(const g of s.guests.slice(0,5))leak(JSON.stringify(M.guestView(s,u,g)),'guestView '+u);
  });
  test('[GID] old last-4-only profiles stay valid and show "full number not on file"',()=>{
    let s=M.loadScenario('BASE');const g=s.guests.find(g=>g.idType.startsWith('Aadhaar'));delete g.idNumber;g.idLast4='4101';
    const b=s.bookings.find(b=>b.guestId===g.id);
    assert.equal(M.guestComplete(g),true);const v=M.guestView(s,'reception',g);assert.equal(v.idOnFile,false);assert.equal(v.canShowId,false);assert.equal(v.idMasked,'XXXX-XXXX-4101');
    const h=UI.details(s,'reception',g);assert.ok(h.includes('XXXX-XXXX-4101'));assert.ok(h.includes('full number not on file'));assert.ok(!h.includes('Show ID'));
    assert.ok(UI.regcard(s,'reception',b.id).includes('XXXX-XXXX-4101'));
    const conf=s.bookings.find(x=>x.status==='Confirmed'&&x.guestId===g.id);
    if(conf)s=execute(s,'reception',{type:'REGISTER',id:conf.id,guestId:g.id,guest:{...full,idLast4:'4101'}});
    let g1=s.guests.find(x=>x.id===g.id);assert.equal(g1.idNumber,undefined);assert.equal(g1.idLast4,'4101');
    s=execute(s,'reception',{type:'GUEST_SAVE',id:g1.id,version:g1.version,guest:{...full,idLast4:'4101'}});
    const g2=s.guests.find(x=>x.id===g.id);assert.equal(g2.idNumber,undefined);assert.equal(g2.idLast4,'4101');
    s=execute(s,'reception',{type:'GUEST_SAVE',id:g2.id,version:g2.version,guest:{...full,idNumber:AAD}});
    assert.equal(s.guests.find(x=>x.id===g.id).idNumber,AAD);
    const rt=JSON.parse(JSON.stringify(s));assert.equal(rt.guests.find(x=>x.id===g.id).idNumber,AAD);
  });
}

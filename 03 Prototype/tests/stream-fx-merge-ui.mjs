// FX lane merge-ui · Customers 360 "Possible duplicates" (training finding F18): pairs with their reason, compare, merge request + second approver, dismiss,
// who sees what, desktop + phone screens, production mapping. Names prefixed [FX-MERGE-UI].
import {build} from 'esbuild';
import {writeFile,mkdir,readFile} from 'node:fs/promises';
import {loadSync,fakeServer,startRuntime,capabilities} from './_sync-harness.mjs';

const ENTRY=`
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {FitNavContext} from '../../src/components/fit';
import {can,metrics} from '../../src/services/mock';
import {MergePanel} from '../../src/features/customers/Merge';
import {MergeMobile} from '../../src/features/customers/MergeMobile';
import {CustomersPage} from '../../src/features/customers/Customers';
export function render(name:string,s:any,uid:string,mobile=false){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page:'customers',go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>true,toast:noop,search:'',setSearch:noop,
    permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop,openSearch:noop,signOut:noop,taskList:[]};
  const C=name==='panel'?MergePanel:name==='mobile'?MergeMobile:CustomersPage;
  return renderToStaticMarkup(<HccContext.Provider value={ctx}><FitNavContext.Provider value={{desktop:!mobile,page:'customers',tabKey:'',seq:1,setTabCrumb:noop}}><C/></FitNavContext.Provider></HccContext.Provider>);
}`.replace("import {cmxPairsFromServerRef} from '../../src/features/customers/_noop';\n",'');

export async function register({test,M,assert}){
  await mkdir('tests/.generated',{recursive:true});
  await writeFile('tests/.generated/fx-merge-ui-entry.tsx',ENTRY);
  await build({entryPoints:['tests/.generated/fx-merge-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fx-merge-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
  const X=await import(new URL('./.generated/fx-merge-ui.mjs',import.meta.url).href);
  await writeFile('tests/.generated/fx-merge-ui-prof-entry.ts',`export {ensurePairProfiles,getPairProfile,setPairProfileFetcher} from '../../src/features/customers/profile-source';
export {pairProfileFromServer} from '../../src/clients/sync/customer-merge';`);
  await build({entryPoints:['tests/.generated/fx-merge-ui-prof-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fx-merge-ui-prof.mjs',logLevel:'error',define:{'process.env.HCC_BUILD':'"production"'}});
  const {execute,seed}=M;
  const code=f=>{try{f();return 'ok';}catch(e){return e.code??e.message;}};

  // ── fixture: Asha (phone, e-mail), Asha (no phone, same name), Ravi A / Ravi B (same e-mail, different phones), Meena (alone) ──
  const mk=(base,id,guest,over)=>({...base,id,guest,email:'',status:'Departed',registered:true,lines:[],receipts:[],people:1,notes:'',version:1,...over});
  function fixture(){
    const s=seed(),b0=s.bookings[0];
    s.bookings=[
      mk(b0,'FX-1','Asha Rao',{phone:'+91 98200 11111',email:'asha@example.test',arrival:'2026-08-01',departure:'2026-08-03',guestId:'G-FA'}),
      mk(b0,'FX-2','Asha Rao',{phone:'',email:'',arrival:'2026-09-01',departure:'2026-09-02',guestId:undefined}),
      mk(b0,'FX-3','Ravi Kulkarni',{phone:'+91 98200 22222',email:'Ravi.K+stay@Gmail.com',arrival:'2026-07-01',departure:'2026-07-02',guestId:'G-FR1'}),
      mk(b0,'FX-4','R. Kulkarni',{phone:'+91 98200 33333',email:'ravik@gmail.com',arrival:'2026-07-10',departure:'2026-07-13',guestId:'G-FR2'}),
      mk(b0,'FX-5','R. Kulkarni',{phone:'+91 98200 33333',email:'ravik@gmail.com',arrival:'2026-07-20',departure:'2026-07-21',guestId:'G-FR2'}),
      mk(b0,'FX-6','Meena Joshi',{phone:'+91 98200 44444',email:'meena@example.test',arrival:'2026-07-05',departure:'2026-07-06',guestId:undefined}),
      ...s.bookings.filter(b=>false)];
    const g=(id,name,phone,email,preferences,notes)=>({id,name,phone,email,address:'',nationality:'Indian',idType:'',idLast4:'',preferences,notes,party:[],verified:false,origin:'seed'});
    s.guests=[g('G-FA','Asha Rao','+91 98200 11111','asha@example.test','High floor',''),g('G-FR1','Ravi Kulkarni','+91 98200 22222','Ravi.K+stay@Gmail.com','Quiet room','Likes tea'),
      g('G-FR2','R. Kulkarni','+91 98200 33333','ravik@gmail.com','Quiet room','')];
    return s;
  }
  const K={asha:'9820011111',ashaN:'n:asha rao',r1:'9820022222',r2:'9820033333',meena:'9820044444'};
  const pairs=(s,uid='owner')=>M.cmxView(s,uid).pairs;
  const pairOf=(s,x,y,uid)=>pairs(s,uid).find(p=>p.id===M.cmxPairId(x,y));
  const run=(s,uid,cmd)=>execute(s,uid,cmd);
  const merged=(s)=>{ // Owner proposes (Asha-no-phone into Asha), Manager approves
    let t=run(s,'owner',{type:'CUSTMERGE_PROPOSE',source:K.ashaN,target:K.asha,reason:'Same person, walk-in without a number'});
    return run(t,'manager',{type:'CUSTMERGE_DECIDE',id:t.customerMergeX.merges[0].id,approve:true,version:'1'});
  };

  test('[FX-MERGE-UI] pairs: same e-mail (Gmail dots / plus ignored) and same name without a mobile number, each with its reason; a lone customer is in no pair',()=>{
    const s=fixture(),p=pairs(s);
    const email=pairOf(s,K.r1,K.r2),name=pairOf(s,K.asha,K.ashaN);
    assert.ok(email&&name);assert.equal(email.reason,'email');assert.equal(name.reason,'name-without-phone');
    assert.equal(name.suggested,K.asha,'the record with a mobile number is the suggested survivor');
    assert.equal(email.suggested,K.r2,'more stays wins when both have a number');
    assert.ok(!p.some(x=>x.a.key===K.meena||x.b.key===K.meena));
    const rows=M.cmxRows(email,true),by=l=>rows.find(r=>r.label===l);
    assert.ok(by('Name').differs&&by('Stays').differs);assert.equal(by('Stays').a+'/'+by('Stays').b,'1/2');
    assert.equal(email.a.preferences,'Quiet room');assert.equal(email.a.notes,'Likes tea');
  });
  test('[FX-MERGE-UI] merge is maker-checker: the proposer cannot decide, a role without the right can neither propose, decide nor dismiss',()=>{
    const s=fixture();
    const t=run(s,'owner',{type:'CUSTMERGE_PROPOSE',source:K.ashaN,target:K.asha,reason:'Same person, walk-in without a number'});
    const id=t.customerMergeX.merges[0].id;
    assert.equal(t.customerMergeX.merges[0].status,'Pending');assert.equal(code(()=>run(t,'owner',{type:'CUSTMERGE_DECIDE',id,approve:true})),'FORBIDDEN');
    for(const u of ['reception','cashier','accountant','supervisor','housekeeper']){
      assert.equal(code(()=>run(s,u,{type:'CUSTMERGE_PROPOSE',source:K.ashaN,target:K.asha,reason:'Same person, no number'})),'FORBIDDEN',u);
      assert.equal(code(()=>run(t,u,{type:'CUSTMERGE_DECIDE',id,approve:true})),'FORBIDDEN',u);
      assert.equal(code(()=>run(s,u,{type:'CUSTMERGE_DISMISS',a:K.asha,b:K.ashaN})),'FORBIDDEN',u);
      const v=M.cmxView(t,u);assert.deepEqual([v.canMerge,v.pairs.length,v.pending.length],[false,0,0],u+' sees no list');
    }
    assert.equal(M.cmxView(t,'manager').pending.length,1);
  });
  test('[FX-MERGE-UI] proposal rules: reason 5-300 characters, two different known customers, one pending merge per customer',()=>{
    const s=fixture(),base={type:'CUSTMERGE_PROPOSE',source:K.ashaN,target:K.asha};
    assert.equal(code(()=>run(s,'owner',{...base,reason:'no'})),'VALIDATION');
    assert.equal(code(()=>run(s,'owner',{...base,reason:'x'.repeat(301)})),'VALIDATION');
    assert.equal(code(()=>run(s,'owner',{...base,target:K.ashaN,reason:'same one twice'})),'VALIDATION');
    assert.equal(code(()=>run(s,'owner',{...base,target:'9999999999',reason:'unknown person'})),'NOT_FOUND');
    const t=run(s,'owner',{...base,reason:'Same person, no number'});
    assert.equal(code(()=>run(t,'manager',{...base,source:K.asha,target:K.r1,reason:'busy customer'})),'INVALID_TRANSITION');
    assert.ok(!M.cmxView(t,'owner').pairs.some(p=>p.id===M.cmxPairId(K.asha,K.ashaN)),'a pair with a pending request leaves the list');
  });
  test('[FX-MERGE-UI] approved merge moves everything: stays, nights and lifetime join the survivor, the merged-away customer is gone, the pair does not come back',()=>{
    const s=fixture(),before=M.customerIndex(s),a0=before.find(c=>c.key===K.asha),n0=before.find(c=>c.key===K.ashaN);
    const t=merged(s),idx=M.customerIndex(t),a=idx.find(c=>c.key===K.asha);
    assert.equal(idx.length,before.length-1);assert.ok(!idx.some(c=>c.key===K.ashaN));
    assert.deepEqual(a.stays.map(b=>b.id).sort(),['FX-1','FX-2']);assert.equal(a.completed.length,a0.completed.length+n0.completed.length);
    assert.equal(a.nights,a0.nights+n0.nights);assert.equal(a.lifetime,a0.lifetime+n0.lifetime);assert.equal(a.name,'Asha Rao');assert.equal(a.phone,'+91 98200 11111');
    const m=t.customerMergeX.merges[0];assert.equal(m.status,'Approved');assert.equal(m.decidedBy,'manager');assert.equal(m.moved.stays,1);
    assert.equal(pairOf(t,K.asha,K.ashaN),undefined);assert.equal(M.cmxView(t,'owner').pending.length,0);
    assert.equal(code(()=>run(t,'manager',{type:'CUSTMERGE_DECIDE',id:m.id,approve:false})),'INVALID_TRANSITION','a decided request is final');
    assert.ok(t.audit.some(x=>/CUSTMERGE DECIDE/.test(x.action)&&/history kept/.test(x.detail)));
    // the original input is untouched (copy on write)
    assert.equal(M.customerIndex(s).length,before.length);assert.equal(s.customerMergeX,undefined);
  });
  test('[FX-MERGE-UI] merged-away legacy stays follow the survivor',()=>{
    let t=fixture();
    t=run(t,'owner',{type:'CUSTMERGE_PROPOSE',source:K.r1,target:K.r2,reason:'Same e-mail, same man'});
    t=run(t,'manager',{type:'CUSTMERGE_DECIDE',id:t.customerMergeX.merges[0].id,approve:true});
    const leg={[K.r1]:[{fy:'25-26',bookingId:7,arrival:'2025-05-01',nights:2,room:'101',roomCount:1,status:'Departed'}]};
    const idx=M.customerIndex(t,k=>leg[k]),r2=idx.find(c=>c.key===K.r2);
    assert.equal(r2.legacyStays.length,1);assert.equal(r2.legacyCount,1);assert.equal(r2.stays.length,3);
    assert.equal(r2.name,'R. Kulkarni','the survivor decides name and profile');
    assert.equal(t.guests.find(g=>g.id==='G-FR2').preferences,'Quiet room');
  });
  test('[FX-MERGE-UI] a chain A into B, then B into C re-points the earlier alias: aliases {A:C,B:C}, C holds every stay, neither A nor B is left',()=>{
    let t=fixture();
    const go=(src,tgt)=>{t=run(t,'owner',{type:'CUSTMERGE_PROPOSE',source:src,target:tgt,reason:'Same man, chain step'});
      t=run(t,'manager',{type:'CUSTMERGE_DECIDE',id:t.customerMergeX.merges[0].id,approve:true});};
    go(K.r1,K.r2);assert.deepEqual(t.customerMergeX.aliases,{[K.r1]:K.r2});
    go(K.r2,K.meena);
    assert.deepEqual(t.customerMergeX.aliases,{[K.r1]:K.meena,[K.r2]:K.meena});
    const idx=M.customerIndex(t),c=idx.find(x=>x.key===K.meena);
    assert.deepEqual(c.stays.map(b=>b.id).sort(),['FX-3','FX-4','FX-5','FX-6']);
    assert.ok(!idx.some(x=>x.key===K.r1||x.key===K.r2));
    assert.equal(code(()=>run(t,'owner',{type:'CUSTMERGE_PROPOSE',source:K.r1,target:K.asha,reason:'A is gone already'})),'NOT_FOUND');
  });
  test('[FX-MERGE-UI] the confirm counts every stay that moves (upcoming and cancelled too), not only completed ones',()=>{
    const s=fixture();s.bookings.find(b=>b.id==='FX-5').status='Cancelled';s.bookings.find(b=>b.id==='FX-4').status='Confirmed';
    const p=pairOf(s,K.r1,K.r2),drop=p.a.key===K.r1?p.a:p.b;
    assert.equal(drop.stays,1);assert.equal(drop.allStays,1);
    const other=p.a.key===K.r2?p.a:p.b;assert.equal(other.stays,0);assert.equal(other.allStays,2);
    assert.match(M.cmxMoves(drop,other).join(' '),/all 2 stays \(upcoming and cancelled too\)/);
  });
  test('[FX-MERGE-UI] preferences follow the server rule: survivor kept, a different text of the other is saved as a note, empty is copied',()=>{
    let s=fixture();
    s.guests.find(g=>g.id==='G-FR2').preferences='Early breakfast';
    let t=run(s,'owner',{type:'CUSTMERGE_PROPOSE',source:K.r1,target:K.r2,reason:'Same e-mail, same man'});
    t=run(t,'manager',{type:'CUSTMERGE_DECIDE',id:t.customerMergeX.merges[0].id,approve:true,note:'Checked the ID'});
    const g=t.guests.find(g=>g.id==='G-FR2');
    assert.equal(g.preferences,'Early breakfast');assert.match(g.notes,/Preferences before merge \(Ravi Kulkarni\): Quiet room/);assert.match(g.notes,/Notes before merge \(Ravi Kulkarni\): Likes tea/);
    assert.equal(t.customerMergeX.merges[0].moved.preferences,'noted');assert.equal(t.customerMergeX.merges[0].note,'Checked the ID');
    s=fixture();s.guests.find(g=>g.id==='G-FR2').preferences='';
    let u=run(s,'owner',{type:'CUSTMERGE_PROPOSE',source:K.r1,target:K.r2,reason:'Same e-mail, same man'});
    u=run(u,'manager',{type:'CUSTMERGE_DECIDE',id:u.customerMergeX.merges[0].id,approve:true});
    assert.equal(u.guests.find(g=>g.id==='G-FR2').preferences,'Quiet room');
  });
  test('[FX-MERGE-UI] the credit account is not touched by a merge',()=>{
    const s=fixture();s.guests.find(g=>g.id==='G-FR1').creditAccount={limit:500000,active:true,customerKey:K.r1,setBy:'owner',at:s.clock};
    let t=run(s,'owner',{type:'CUSTMERGE_PROPOSE',source:K.r1,target:K.r2,reason:'Same e-mail, same man'});
    t=run(t,'manager',{type:'CUSTMERGE_DECIDE',id:t.customerMergeX.merges[0].id,approve:true});
    assert.deepEqual(t.guests.find(g=>g.id==='G-FR1').creditAccount,s.guests.find(g=>g.id==='G-FR1').creditAccount);
    assert.ok(M.cmxMoves({name:'A',stays:1},{name:'B',stays:2}).some(x=>/Credit account: not changed/.test(x)));
  });
  test('[FX-MERGE-UI] reject keeps both customers; a rejected pair can be proposed again',()=>{
    const s=fixture();
    let t=run(s,'owner',{type:'CUSTMERGE_PROPOSE',source:K.ashaN,target:K.asha,reason:'Same person, no number'});
    t=run(t,'manager',{type:'CUSTMERGE_DECIDE',id:t.customerMergeX.merges[0].id,approve:false,note:'Different person'});
    assert.equal(t.customerMergeX.merges[0].status,'Rejected');assert.equal(M.customerIndex(t).length,M.customerIndex(s).length);
    assert.ok(pairOf(t,K.asha,K.ashaN),'the pair is suggested again');
    assert.doesNotThrow(()=>run(t,'owner',{type:'CUSTMERGE_PROPOSE',source:K.ashaN,target:K.asha,reason:'Second try, same person'}));
  });
  test('[FX-MERGE-UI] "Not the same person" dismisses a pair for good, for both users, through later merges',()=>{
    let t=run(fixture(),'manager',{type:'CUSTMERGE_DISMISS',a:K.ashaN,b:K.asha});
    assert.equal(pairOf(t,K.asha,K.ashaN),undefined);assert.equal(pairOf(t,K.asha,K.ashaN,'manager'),undefined);
    assert.ok(pairOf(t,K.r1,K.r2),'other pairs stay');
    t=run(t,'owner',{type:'CUSTMERGE_PROPOSE',source:K.r1,target:K.r2,reason:'Same e-mail, same man'});
    t=run(t,'manager',{type:'CUSTMERGE_DECIDE',id:t.customerMergeX.merges[0].id,approve:true});
    assert.equal(pairOf(t,K.asha,K.ashaN),undefined);assert.deepEqual(t.customerMergeX.dismissed,[M.cmxPairId(K.asha,K.ashaN)]);
    assert.equal(code(()=>run(t,'owner',{type:'CUSTMERGE_DISMISS',a:K.asha,b:K.ashaN})),'INVALID_TRANSITION');
  });
  test('[FX-MERGE-UI] numbers stay masked: a reader without contact rights sees •••• numbers in the pairs and no number in the audit text',()=>{
    const s=fixture();s.grants.Cashier=[...s.grants.Cashier.filter(p=>p!=='checkin'),'approve'];
    const p=pairOf(s,K.r1,K.r2,'cashier'),all=JSON.stringify({a:{...p.a,key:''},b:{...p.b,key:''},reason:p.reason}); // keys are internal ids, never rendered
    assert.ok(p);assert.ok(/•••/.test(p.a.phone));assert.ok(!/9820022222|98200 22222|98200 33333|9820033333/.test(all));
    assert.ok(!/ravik@gmail|Ravi\.K/.test(all),'e-mail masked');
    const own=pairOf(s,K.r1,K.r2,'owner');assert.ok(/98200 \d{5}/.test(own.a.phone),'the Owner sees the number');
    assert.equal(M.cmxSafeName('Walk-in 98200 11111'),'Walk-in ***');assert.equal(M.cmxSafeName('Asha Rao'),'Asha Rao');
    const t=run(s,'cashier',{type:'CUSTMERGE_DISMISS',a:K.r1,b:K.r2});assert.ok(!/98200/.test(t.audit[0].detail));
  });
  test('[FX-MERGE-UI] money in the compare table only for roles with billing; credit account only for the Owner',()=>{
    const s=fixture();s.guests.find(g=>g.id==='G-FR1').creditAccount={limit:500000,active:true,customerKey:K.r1,setBy:'owner',at:s.clock};
    const o=pairOf(s,K.r1,K.r2,'owner'),mgr=pairOf(s,K.r1,K.r2,'manager');
    assert.ok(M.cmxRows(o,true).some(r=>r.label==='Credit account'));assert.ok(!M.cmxRows(mgr,true).some(r=>r.label==='Credit account'));
    assert.ok(!M.cmxRows(o,false).some(r=>r.label==='Lifetime spend'));
  });

  // ── screens ──
  test('[FX-MERGE-UI] desktop panel: pairs with reasons, merge and dismiss actions after choosing, nothing for a role without the right',()=>{
    const s=fixture(),h=X.render('panel',s,'owner');
    assert.match(h,/Possible duplicates/);assert.match(h,/Same e-mail address/);assert.match(h,/Same name, one record has no mobile number/);assert.match(h,/Choose a pair/);
    const r=X.render('panel',s,'reception');assert.match(r,/Only an Owner or Manager can merge customers/);assert.ok(!/Same e-mail address/.test(r));
    const t=run(s,'owner',{type:'CUSTMERGE_PROPOSE',source:K.ashaN,target:K.asha,reason:'Same person, no number'});
    assert.match(X.render('panel',t,'manager'),/Waiting for approval \(1\)[\s\S]*Approve/);
    assert.match(X.render('panel',t,'owner'),/Waiting for another approver/);
  });
  test('[FX-MERGE-UI] Customers page: the "Possible duplicates" tab and the phone button exist for Owner / Manager only',()=>{
    const s=fixture();
    for(const mobile of [false,true]){
      const own=X.render('page',s,'owner',mobile),rec=X.render('page',s,'reception',mobile);
      assert.match(own,/Possible duplicates/,'owner '+(mobile?'phone':'desktop'));assert.ok(!/Possible duplicates/.test(rec),'reception '+(mobile?'phone':'desktop'));
    }
  });
  test('[FX-MERGE-UI] phone sheet: cards (no table), 48 px buttons by class, no number leaks, role without the right gets a plain message',()=>{
    const s=fixture();s.grants.Cashier=[...s.grants.Cashier.filter(p=>p!=='checkin'),'approve'];
    const h=X.render('mobile',s,'owner',true);
    assert.match(h,/Possible duplicates/);assert.match(h,/Ravi Kulkarni · R\. Kulkarni|R\. Kulkarni · Ravi Kulkarni/);assert.ok(!/<table/.test(h));
    assert.match(X.render('mobile',s,'reception',true),/Only an Owner or Manager can merge customers/);
    assert.ok(!/98200|9820/.test(X.render('mobile',s,'cashier',true)));
  });
  test('[FX-MERGE-UI] the pair rows of the compare table tell the reader what differs (marked rows) and the confirm text names what moves and that it cannot be undone',()=>{
    const s=fixture(),p=pairOf(s,K.asha,K.ashaN);
    const rows=M.cmxRows(p,true);assert.ok(rows.find(r=>r.label==='Mobile').differs&&rows.find(r=>r.label==='Name').differs===false);
    const text=M.cmxMoves(p.a,p.b).join(' ');
    for(const w of ['Stays','Tags and notes','Preferences','Credit account'])assert.match(text,new RegExp(w));
    assert.match(M.CMX_UNDO_TEXT,/cannot be undone from this screen/);assert.match(M.CMX_UNDO_TEXT,/second Owner or Manager/);
  });
  test('[FX-MERGE-UI] CSS: the screen styles sit in v22-fixes.css between the FX-MERGE-UI markers, token colours only',async()=>{
    const css=await readFile('src/styles/v22-fixes.css','utf8'),m=/\/\* FX-MERGE-UI[\s\S]*?\/\* \/FX-MERGE-UI \*\//.exec(css);
    assert.ok(m&&/\.cmx-table/.test(m[0])&&/\.cmx-m-card/.test(m[0]));
    assert.match(m[0],/\.cmx-m \.cmx-field input\{min-height:48px;font-size:16px\}/,'the phone reason field is a 48 px target');
    assert.ok(!/#[0-9a-fA-F]{3,8}\b/.test(m[0].replace(/\/\*[\s\S]*?\*\//g,'')),'no hex colours');
  });
  test('[FX-MERGE-UI] FIT guard: the new phone file has its FROM_MAIN allow-list line',async()=>{
    const t=await readFile('tests/stream-fit.mjs','utf8');assert.match(t,/'03 Prototype\/src\/features\/customers\/MergeMobile\.tsx'/);
  });

  // ── production: the sync area ──
  const S=await loadSync();
  const sum=(key,name,over={})=>({key,name,phone:'••••••2222',email:null,masked:true,stays:1,completedStays:1,nights:2,lifetimePaise:null,lastStay:'2026-07-02',inHouse:false,tags:[],legacyStays:0,firstStay:'2026-07-01',...over});
  const grp=(reason,cs,sug)=>({reason,customers:cs,suggestedSurvivor:sug});
  const mergeRow=(over={})=>({mergeId:41,sourceKey:'ck_aaa',targetKey:'ck_bbb',sourceName:'Asha Rao',targetName:'Asha R',reason:'Same e-mail address',status:'Pending',proposedBy:3,proposedUtc:'2026-10-24T05:00:00.000Z',
    decidedBy:null,decidedUtc:null,decisionNote:null,version:'AAAAAAAAB9E=',...over});
  const routes=()=>({'GET /customers/duplicates':()=>({groups:[grp('email',[sum('ck_aaa','Asha Rao'),sum('ck_bbb','Asha R',{completedStays:3,tags:['VIP']}),sum('ck_ccc','A Rao')],'ck_bbb')]}),
    'GET /customers/merges':()=>({merges:[mergeRow(),mergeRow({mergeId:40,status:'Approved',decidedBy:2,decidedUtc:'2026-10-23T05:00:00.000Z',version:'AAAAAAAAB9D='})]}),
    'GET /customers/profile':({query})=>({preferences:query.key==='ck_aaa'?'High floor':null,notes:[{body:'Likes tea'}],tags:['Repeat']}),
    'POST /commands/ProposeCustomerMerge':()=>mergeRow({mergeId:42}),'POST /commands/DecideCustomerMerge':()=>mergeRow({status:'Approved'})});
  const srv=fakeServer(routes());
  const rt=await startRuntime(S,srv);
  const x=rt.get().state.customerMergeX;
  const prop=await rt.run({type:'CUSTMERGE_PROPOSE',source:'ck_aaa',target:'ck_bbb',reason:'Same e-mail address',key:'k-prop'});
  const dec=await rt.run({type:'CUSTMERGE_DECIDE',id:'41',approve:true,note:'ok',key:'k-dec'});
  const rej=await rt.run({type:'CUSTMERGE_DECIDE',id:'999',approve:false,key:'k-dec2'});
  const dis=await rt.run({type:'CUSTMERGE_DISMISS',a:'ck_aaa',b:'ck_bbb',key:'k-dis'});
  const same=await rt.run({type:'CUSTMERGE_PROPOSE',source:'ck_aaa',target:'ck_aaa',reason:'same one',key:'k-same'});
  const lowSrv=fakeServer({...routes(),'GET /me/capabilities':()=>capabilities({userId:7,displayName:'Reception',roles:['RCP'],grants:[['PERM-RES-001','A'],['PERM-GST-001','A']]})});
  const low=await startRuntime(S,lowSrv);

  test('[FX-MERGE-UI] production loader: the server groups become pairs (suggested survivor against each member), merges are read, numbers stay masked, and no profile is read by the load',()=>{
    assert.equal(x.server.canMerge,true);assert.equal(x.server.pairs.length,2);
    const p=x.server.pairs.find(q=>q.id==='ck_aaa|ck_bbb');
    assert.equal(p.reason,'email');assert.equal(p.suggested,'ck_bbb');assert.equal(p.a.phone,'••••••2222');assert.equal(p.a.preferences,undefined,'preferences are not loaded with the list');
    assert.deepEqual(p.b.tags,['VIP']);assert.equal(p.b.allStays,1,'all stays = the summary Stays + legacy');assert.equal(p.b.stays,3);assert.equal(p.a.lifetime,undefined);
    assert.deepEqual(x.merges.map(m=>[m.id,m.status]),[['41','Pending'],['40','Approved']]);assert.equal(x.merges[0].proposedBy,'3');
    assert.equal(srv.gets('/customers/profile').length,0,'a full load (sign-in) sends no GET /customers/profile');
    assert.equal(x.merges[0].proposedAt,'2026-10-24T10:30','production times use the same clock format as the other mappers');
    const v=M.cmxView(rt.get().state,rt.get().state.users?.[0]?.id??'owner');assert.equal(v.server,true);
  });
  test('[FX-MERGE-UI] opening a pair reads exactly two profiles (preferences, notes, tags), remembered; a failed read leaves "Not loaded"',async()=>{
    const P=await import(new URL('./.generated/fx-merge-ui-prof.mjs',import.meta.url).href);
    const calls=[];
    P.setPairProfileFetcher(async key=>{calls.push(key);if(key==='ck_bad')throw new Error('x');return P.pairProfileFromServer({preferences:key==='ck_aaa'?'High floor':null,notes:[{body:'Likes tea'}],tags:['Repeat']});});
    assert.equal(await P.ensurePairProfiles(['ck_aaa','ck_bbb']),true);assert.deepEqual(calls,['ck_aaa','ck_bbb']);
    assert.deepEqual(P.getPairProfile('ck_aaa'),{preferences:'High floor',notes:'Likes tea',tags:['Repeat']});assert.equal(P.getPairProfile('ck_bbb').preferences,'');
    assert.equal(await P.ensurePairProfiles(['ck_aaa','ck_bbb']),false);assert.equal(calls.length,2,'remembered: no second read');
    assert.equal(await P.ensurePairProfiles(['ck_bad','ck_aaa']),false);assert.equal(P.getPairProfile('ck_bad'),undefined);
    P.setPairProfileFetcher(undefined);calls.length=0;assert.equal(await P.ensurePairProfiles(['ck_zzz']),false);assert.equal(calls.length,0,'the demo never reads');
  });
  test('[FX-MERGE-UI] production wording: the screen says the main Customers list still shows both records until refreshed; the demo does not say it',()=>{
    const s=fixture(),prod={...s,customerMergeX:{aliases:{},dismissed:[],seq:0,server:{canMerge:true,pairs:[]},
      merges:[{id:'41',source:'ck_a',target:'ck_b',sourceName:'Asha Rao',targetName:'Asha R',reason:'Same e-mail address',status:'Approved',proposedBy:'3',proposedAt:'2026-10-24T10:30',decidedBy:'2',version:'x'}]}};
    for(const [name,mobile] of [['panel',false],['mobile',true]]){
      const h=X.render(name,prod,'owner',mobile);assert.match(h,/still shows both records until it is refreshed from the server/,name);
      assert.ok(!/still shows both records/.test(X.render(name,fixture(),'owner',mobile)),name+' demo');
    }
    assert.match(M.cmxApproveToast(true,{sourceName:'A',targetName:'B'}),/merged into B on the server\. The Customers list on this device still shows both records/);
    assert.equal(M.cmxApproveToast(false,{sourceName:'A',targetName:'B'}),'A merged into B');
  });
  test('[FX-MERGE-UI] production commands: ProposeCustomerMerge sends the tokens and the reason; DecideCustomerMerge sends the loaded row version; nothing else is sent',()=>{
    assert.equal(prop.ok,true);assert.deepEqual(srv.commands('ProposeCustomerMerge')[0].body,{sourceKey:'ck_aaa',targetKey:'ck_bbb',reason:'Same e-mail address'});assert.equal(srv.commands('ProposeCustomerMerge')[0].key,'k-prop');
    assert.equal(dec.ok,true);assert.deepEqual(srv.commands('DecideCustomerMerge')[0].body,{mergeId:41,approve:true,note:'ok',expectedVersion:'AAAAAAAAB9E='});
    assert.equal(rej.ok,false);assert.equal(srv.commands('DecideCustomerMerge').length,1,'an unknown request id is refused before any call');
    assert.equal(same.ok,false);assert.equal(srv.commands('ProposeCustomerMerge').length,1);
  });
  test('[FX-MERGE-UI] production: "Not the same person" is unmapped (no server command) and the screen hides it; a role without PERM-CRM-003 sends no GET and sees no action',()=>{
    assert.equal(dis.ok,false);assert.equal(dis.reason,'unmapped');
    assert.deepEqual(['/customers/duplicates','/customers/merges','/customers/profile'].map(p=>lowSrv.gets(p).length),[0,0,0]);
    const lx=low.get().state.customerMergeX;assert.equal(lx.server.canMerge,false);assert.equal(lx.server.pairs.length,0);
    const st=low.get().state,uid=st.users[0]?.id;
    const v=M.cmxView(st,uid);assert.deepEqual([v.canMerge,v.pairs.length,v.pending.length],[false,0,0]);
    assert.equal(M.cmxView(rt.get().state,rt.get().state.users[0].id).server,true,'the panel hides the dismiss button when the lists come from the server');
  });
}

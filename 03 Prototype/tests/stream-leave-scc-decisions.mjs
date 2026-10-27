// SCC decisions wave (owner, 27 Oct 2026 — "apply your recommendations for all 12"), leave rows of 02 Planning\HCC_Decision_Register.md:
//   DEC-122  approving a leave date re-checks the person's balance (SCC re-checks nothing at approval): approved days by date + this date over the
//            entitlement (per-staff override included) → SCC's over-quota warning, approved only once confirmed; rejecting needs no check; LOP and
//            the untracked categories have no quota.
//   DEC-123  the leave calendar goes back to April 2026 (start of FY 2026–27; SCC stopped at May 2026).
// Names prefixed [LV-DEC].
export function register({test,M,assert}){
  const {seed,execute}=M;
  const x=s=>M.hrSlice(s);
  const base=()=>seed();// BASE: business date 24 Oct 2026; Ravi Shinde (HR-S05) has 2 CL approved in September
  const req=(s,uid,cmd)=>execute(s,uid,{type:'HR_LEAVE_REQUEST',approver:'owner',reason:'',...cmd});
  const decide=(s,id,approve,more={})=>execute(s,'owner',{type:'HR_LEAVE_DECIDE',id,approve,...more});
  const last=s=>x(s).leaves.at(-1);
  const warnText=(name,used,quota,cat,fy,add)=>`${name} has used ${used} of ${quota} ${cat} day(s) this ${fy}.\nThis request adds ${add} more and EXCEEDS the balance.\n\nApprove it anyway?`;
  const refusedWith=text=>e=>e.code==='INVALID_TRANSITION'&&e.message===text;
  const rng=seedN=>{let a=seedN>>>0;return ()=>{a=(a+0x6D2B79F5)>>>0;let t=a;t=Math.imul(t^(t>>>15),t|1);t^=t+Math.imul(t^(t>>>7),t|61);return ((t^(t>>>14))>>>0)/4294967296;};};

  // Screens bundle (built once, shared by the screens tests): the leave screens rendered with a stub context.
  let uiP;
  const ui=()=>uiP??=(async()=>{
    const {build}=await import('esbuild');const {writeFile}=await import('node:fs/promises');
    await writeFile('tests/.generated/leave-dec-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';import {can,hrSlice} from '../../src/services/mock';
import {useLeaveDecide,LeaveDeskView,LeaveReportView} from '../../src/features/staff/Leave';
function ctx(s:any,uid:string,mobile:boolean,run:any){const user=s.users.find((u:any)=>u.id===uid);return {s,uid,user,busy:false,run,open(){},closeModal(){},toast(){},setDirty(){},permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile}} as any;}
export function decideVia(s:any,id:string,approve:boolean,answer:boolean,mobile:boolean){
  const sent:any[]=[],asked:string[]=[],g:any=globalThis,had=g.window;g.window={confirm:(t:string)=>{asked.push(t);return answer;}};
  const run=async(c:any,msg:string,o:any)=>{sent.push([c,msg,o]);return true;};let p:any;
  function Probe(){const d=useLeaveDecide();p=d(hrSlice(s).leaves.find((l:any)=>l.id===id),approve,{close:true});return null;}
  try{renderToStaticMarkup(<HccContext.Provider value={ctx(s,'owner',mobile,run)}><Probe/></HccContext.Provider>);}finally{g.window=had;}
  return {sent,asked,p};
}
export function desk(s:any,ym:string,mobile:boolean){const f=()=>{};return renderToStaticMarkup(<HccContext.Provider value={ctx(s,'owner',mobile,async()=>true)}><LeaveDeskView phone={mobile} ym={ym} setYm={f} asOf={s.date} setAsOf={f}/></HccContext.Provider>);}
export function report(s:any,ym:string,mobile:boolean){const f=()=>{};return renderToStaticMarkup(<HccContext.Provider value={ctx(s,'owner',mobile,async()=>true)}><LeaveReportView phone={mobile} ym={ym} setYm={f}/></HccContext.Provider>);}
`);
    await build({entryPoints:['tests/.generated/leave-dec-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/leave-dec-ui.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __lvReq} from 'node:module';const require=__lvReq(import.meta.url);"}});
    return import('./.generated/leave-dec-ui.mjs');
  })();

  test('[LV-DEC] DEC-122 two pending requests that together exceed the quota: no warning when recorded, the first date approves silently, the second warns with SCC’s text and approves only when confirmed',()=>{
    let s=execute(base(),'owner',{type:'HR_LEAVE_ENT_OVERRIDE',staffId:'HR-S05',ent:{CL:3}});// Ravi: CL 3, 2 used (September)
    s=req(s,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-01',to:'2026-12-01'});const a=last(s);
    s=req(s,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-08',to:'2026-12-08'});const b=last(s);// 2 + 1 ≤ 3 each: SCC records both without a warning
    assert.deepEqual([a.status,b.status,M.hrLeaveApproveOverBalance(s,a),M.hrLeaveApproveOverBalance(s,b)],['Pending','Pending',undefined,undefined]);
    s=decide(s,a.id,true);// 2 + 1 = 3: within the quota, no confirmation asked
    assert.equal(x(s).leaves.find(l=>l.id===a.id).status,'Approved');assert.doesNotMatch(s.audit[0].detail,/over the quota/);
    const want=warnText('Ravi Shinde',3,3,'Casual Leave','FY 2026–27',1);
    assert.equal(M.hrLeaveApproveOverBalance(s,b),want);
    assert.throws(()=>decide(s,b.id,true),refusedWith(want));// 3 + 1 > 3: refused until confirmed
    assert.throws(()=>decide(s,b.id,true,{confirmOverBalance:false}),refusedWith(want));
    assert.equal(x(s).leaves.find(l=>l.id===b.id).status,'Pending');
    const ok=decide(s,b.id,true,{confirmOverBalance:true});
    assert.equal(x(ok).leaves.find(l=>l.id===b.id).status,'Approved');assert.match(ok.audit[0].detail,/Leave approved — now live for floor gate & payroll · over the quota \(confirmed\)/);
    assert.deepEqual(M.hrLeaveBalance(ok,'HR-S05','2026-12-08').find(q=>q.type==='CL'),{type:'CL',entitled:3,used:4,left:0,over:1});
    // rejecting the same date needs no check (and nothing is confirmed)
    const rej=decide(s,b.id,false);assert.equal(x(rej).leaves.find(l=>l.id===b.id).status,'Rejected');assert.doesNotMatch(rej.audit[0].detail,/over the quota/);
    // HCC safeguards stay: nobody decides their own request, even with the confirmation
    assert.throws(()=>execute(s,'manager',{type:'HR_LEAVE_DECIDE',id:b.id,approve:true,confirmOverBalance:true}),e=>e.code==='FORBIDDEN');
  });

  test('[LV-DEC] DEC-122 the re-check: half days add 0.5, the per-staff override and the default quota, the date’s own leave year, approved days only; LOP and untracked categories never warn',()=>{
    let s=execute(base(),'owner',{type:'HR_LEAVE_ENT_OVERRIDE',staffId:'HR-S05',ent:{CL:2.5}});// override with a half: 2 used of 2.5
    s=req(s,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-01',to:'2026-12-01',half:'AM'});const am=last(s);
    assert.equal(M.hrLeaveApproveOverBalance(s,am),undefined);// 2 + 0.5 = 2.5
    s=decide(s,am.id,true);
    s=req(s,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-01',to:'2026-12-01',half:'PM',confirmOverBalance:true});const pm=last(s);// recorded after the request warning (SCC)
    assert.throws(()=>decide(s,pm.id,true),refusedWith(warnText('Ravi Shinde','2.5',2.5,'Casual Leave','FY 2026–27','0.5')));
    // the next leave year starts again at the quota (each date is checked against its own FY, as the balance counts it)
    s=req(s,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2027-04-05',to:'2027-04-06'});const [n1,n2]=x(s).leaves.slice(-2);
    s=decide(s,n1.id,true);s=decide(s,n2.id,true);// 0 + 1, 1 + 1 ≤ 2.5 in FY 2027–28
    assert.deepEqual([n1,n2].map(l=>x(s).leaves.find(q=>q.id===l.id).status),['Approved','Approved']);
    // default quota for staff without an override (HR-S07): the SL default cut to 0 makes any SL approval warn — 0 used of 0
    let t=execute(base(),'owner',{type:'HR_LEAVE_POLICY_SET',ent:{CL:12,SL:0,EL:15}});
    t=req(t,'manager',{staffId:'HR-S07',leaveType:'SL',from:'2026-11-30',to:'2026-11-30',confirmOverBalance:true});const sl=last(t);
    assert.throws(()=>decide(t,sl.id,true),refusedWith(warnText(x(t).staff.find(q=>q.id==='HR-S07').name,0,0,'Sick Leave','FY 2026–27',1)));
    // LOP, Maternity / Paternity, Comp-Off and Other: no quota → approved without any confirmation, even with every quota at 0
    let u=execute(base(),'owner',{type:'HR_LEAVE_POLICY_SET',ent:{CL:0,SL:0,EL:0}});
    for(const c of ['LOP','ML','CO','OTH']){u=req(u,'manager',{staffId:'HR-S11',leaveType:c,from:'2026-12-14',to:'2026-12-14'});const l=last(u);
      assert.equal(M.hrLeaveApproveOverBalance(u,l),undefined,c);u=decide(u,l.id,true);assert.equal(x(u).leaves.find(q=>q.id===l.id).status,'Approved',c);
      u=execute(u,'owner',{type:'HR_LEAVE_CANCEL',id:l.id});}// frees the date for the next category
    // pending, rejected and cancelled records are not "used": only approved ones count (SCC fyUsage)
    let v=execute(base(),'owner',{type:'HR_LEAVE_ENT_OVERRIDE',staffId:'HR-S05',ent:{CL:3}});
    v=req(v,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-01',to:'2026-12-03',confirmOverBalance:true});const [p1,p2,p3]=x(v).leaves.slice(-3);
    v=decide(v,p1.id,false);v=execute(v,'manager',{type:'HR_LEAVE_CANCEL',id:p2.id});
    assert.equal(M.hrLeaveApproveOverBalance(v,p3),undefined);// 2 approved + this 1 = 3
  });

  test('[LV-DEC] DEC-122 random histories: 500 approvals warn exactly when the approved days of that FY (by date) + this date exceed the person’s quota; confirmed ones always go through',()=>{
    const r=rng(122);let s=base(),warned=0,silent=0;
    s=execute(s,'owner',{type:'HR_LEAVE_POLICY_SET',ent:{CL:4,SL:2.5,EL:3}});
    s=execute(s,'owner',{type:'HR_LEAVE_ENT_OVERRIDE',staffId:'HR-S05',ent:{CL:1.5,EL:6}});
    const staff=['HR-S02','HR-S05','HR-S07','HR-S11'],cats=['CL','SL','EL','LOP','ML'];
    const fyOf=d=>{const y=+d.slice(0,4),m=+d.slice(5,7),f=m>=4?y:y-1;return {from:`${f}-04-01`,to:`${f+1}-03-31`};};
    for(let i=0;i<900&&warned+silent<500;i++){
      const sid=staff[Math.floor(r()*staff.length)],d=M.addDays('2026-10-26',Math.floor(r()*240)),half=r()<0.35?(r()<0.5?'AM':'PM'):undefined,c=cats[Math.floor(r()*cats.length)];
      try{s=req(s,'manager',{staffId:sid,leaveType:c,from:d,to:d,...(half?{half}:{}),confirmOverBalance:true});}catch{continue;}
      const l=last(s);if(r()<0.15){s=decide(s,l.id,false);continue;}
      // oracle: approved records of this person, category and FY, by date; quota = override, else the saved default; untracked = no quota
      const fy=fyOf(l.date),used=x(s).leaves.filter(q=>q.staffId===sid&&q.type===c&&q.status==='Approved'&&q.date>=fy.from&&q.date<=fy.to).reduce((a,q)=>a+(q.half?0.5:1),0);
      const ov=sid==='HR-S05'?{CL:1.5,EL:6}:{},quota=['CL','SL','EL'].includes(c)?(ov[c]??{CL:4,SL:2.5,EL:3}[c]):undefined,over=quota!==undefined&&used+(half?0.5:1)>quota;
      const w=M.hrLeaveApproveOverBalance(s,l);
      assert.equal(!!w,over,`${i} ${sid} ${c} ${l.date} used ${used} quota ${quota}`);
      if(over){warned++;assert.throws(()=>decide(s,l.id,true),e=>e.code==='INVALID_TRANSITION'&&e.message===w);s=decide(s,l.id,true,{confirmOverBalance:true});}
      else{silent++;s=decide(s,l.id,true);}
      assert.equal(x(s).leaves.find(q=>q.id===l.id).status,'Approved');
    }
    assert.ok(warned>60&&silent>150,`warned ${warned} · silent ${silent}`);
  });

  test('[LV-DEC] DEC-122 screens: the approve buttons (approvals list, day view, leave detail, decide dialog — one hook, desktop and phone) ask SCC’s warning in a confirm dialog; Cancel sends nothing, OK sends confirmOverBalance; rejecting and within-quota approvals never ask',async()=>{
    let s=execute(base(),'owner',{type:'HR_LEAVE_ENT_OVERRIDE',staffId:'HR-S05',ent:{CL:2}});// 2 used of 2
    s=req(s,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-01',to:'2026-12-01',confirmOverBalance:true});const l=last(s);
    s=req(s,'manager',{staffId:'HR-S02',leaveType:'CL',from:'2026-12-01',to:'2026-12-01'});const ok=last(s);
    const want=warnText('Ravi Shinde',2,2,'Casual Leave','FY 2026–27',1);
    // the pure helper behind the hook
    const asked=[];const ask=a=>t=>{asked.push(t);return a;};
    assert.equal(M.hrLeaveDecideCmd(s,l,true,ask(false)),undefined);
    assert.deepEqual(M.hrLeaveDecideCmd(s,l,true,ask(true),'Peak week'),{type:'HR_LEAVE_DECIDE',id:l.id,approve:true,note:'Peak week',confirmOverBalance:true});
    assert.deepEqual(asked,[want,want]);
    assert.deepEqual(M.hrLeaveDecideCmd(s,l,false,ask(true)),{type:'HR_LEAVE_DECIDE',id:l.id,approve:false});
    assert.deepEqual(M.hrLeaveDecideCmd(s,ok,true,ask(true)),{type:'HR_LEAVE_DECIDE',id:ok.id,approve:true});assert.equal(asked.length,2);
    // the screens' hook (useLeaveDecide) with window.confirm and the app's run
    const UI=await ui();
    for(const phone of [false,true]){
      const no=UI.decideVia(s,l.id,true,false,phone);assert.deepEqual([no.asked,no.sent.length,await no.p],[[want],0,false]);// Cancel: nothing sent
      const yes=UI.decideVia(s,l.id,true,true,phone);assert.deepEqual(yes.asked,[want]);
      assert.deepEqual(yes.sent,[[{type:'HR_LEAVE_DECIDE',id:l.id,approve:true,confirmOverBalance:true},'Leave approved — now live for floor gate & payroll',{close:true}]]);
      assert.equal(x(execute(s,'owner',yes.sent[0][0])).leaves.find(q=>q.id===l.id).status,'Approved');// the confirmed command goes through
      const rej=UI.decideVia(s,l.id,false,true,phone);assert.deepEqual([rej.asked,rej.sent[0][0]],[[],{type:'HR_LEAVE_DECIDE',id:l.id,approve:false}]);
      const fine=UI.decideVia(s,ok.id,true,true,phone);assert.deepEqual([fine.asked,fine.sent[0][0]],[[],{type:'HR_LEAVE_DECIDE',id:ok.id,approve:true}]);
    }
  });

  test('[LV-DEC] DEC-123 the leave calendar starts at April 2026 (start of FY 2026–27): month arrows, month jumper, clamp, report — FY26 April leave shows on the calendar, March 2026 is still out',async()=>{
    assert.equal(M.HR_LEAVE_START_YM,'2026-04');
    assert.deepEqual(['2025-12','2026-03','2026-04','2026-05','2026-10'].map(M.hrLeaveClampYm),['2026-04','2026-04','2026-04','2026-05','2026-10']);
    const j=M.hrLeaveMonthJumper('2026-10');assert.deepEqual([j[0],j[1],j.at(-1),j.length],['2026-04','2026-05','2027-12',21]);
    assert.equal(M.hrLeaveMonthJumper('2027-06')[0],'2026-04');assert.equal(M.hrLeaveFyLabel('2026-04'),'FY 2026–27');
    const fy=M.loadScenario('FY26'),cal=M.hrLeaveMonthView(fy,'2026-04');
    const chips=cal.cells.flatMap(c=>c.chips);assert.ok(chips.length>0,'FY26 has approved leave in April 2026');
    const UI=await ui();
    for(const phone of [false,true]){
      const apr=UI.desk(fy,'2026-04',phone),may=UI.desk(fy,'2026-05',phone);
      assert.match(apr,/April 2026/);assert.doesNotMatch(apr,/May 2026<\/strong>/);
      assert.match(apr,/<button class="btn" aria-label="Previous month" disabled=""/);// April = the first month: no way back to March
      assert.doesNotMatch(may,/aria-label="Previous month" disabled=""/);// May can go back to April now
      assert.match(apr,/<option value="2026-04" selected="">Apr 2026<\/option>/);
      assert.doesNotMatch(UI.desk(fy,'2026-03',phone),/March 2026/);// before April: clamped to April
      if(!phone)assert.ok(chips.some(c=>apr.includes(c.name)),'an April leave chip is on the desktop grid');
      const rep=UI.report(fy,'2026-04',phone);assert.match(rep,/April 2026/);
    }
  });
}

// DEC-116 / DEC-119 · SCC LeaveDesk in full (owner, 27 Oct 2026). SCC's own functions, copied below from
// E:\Projects\HCC\Migration data on 25 oct\HCC-Migration-2026-10-25\reference-inputs\SCC-modules\leave\index.html (extracted script leave.js; Lnnn),
// are the oracle: HCC (one record per date, staff by ID) must give the same capacity, the same per-date outcome of a request, the same counts,
// balances, stats, report and CSV, and the same import merge. Names prefixed [LV-SCC-PORT].
export function register({test,M,assert}){
  const {seed,execute}=M;
  const code=c=>e=>e.code===c;
  const x=s=>M.hrSlice(s);
  // ── SCC, verbatim apart from taking `data`, the week-off set and the entitlements as arguments instead of localStorage ──
  const MAX_SHIFT=4,MAX_DAY=3;
  const LEAVE_STATUSES=['pending','approved','rejected'];
  function leaveStatus(l){return (l&&LEAVE_STATUSES.includes(l.status))?l.status:'approved';}
  function isActiveLeave(l){return leaveStatus(l)!=='rejected';}
  function isCountedLeave(l){return leaveStatus(l)==='approved';}
  function leaveDaysOf(l){return l.type==='full_day'?1:0.5;}
  const TRACKED_CATS=['Casual Leave','Sick Leave','Earned Leave'];
  const ENT_DEFAULTS={'Casual Leave':12,'Sick Leave':7,'Earned Leave':15};
  function parseKey(k){const [y,m,d]=k.split('-').map(Number);return {year:y,month:m-1,day:d};}
  function isWeekOffDate(k,weekOff){const {year,month,day}=parseKey(k);return weekOff.includes(new Date(year,month,day).getDay());}
  function entFor(name,ent){
    const ov=ent.perEmployee[String(name||'').trim().toLowerCase()]||{};const out={};
    TRACKED_CATS.forEach(c=>{const v=(ov[c]!=null)?ov[c]:(ent.defaults[c]!=null?ent.defaults[c]:ENT_DEFAULTS[c]);out[c]=Math.max(0,Number(v)||0);});
    return out;
  }
  function fyRangeOf(year,month){const s=month>=3?year:year-1;return {from:`${s}-04-01`,to:`${s+1}-03-31`,label:`FY ${s}–${String(s+1).slice(-2)}`};}
  function fyUsage(data,name,fy){
    const key=String(name||'').trim().toLowerCase(),used={};
    Object.keys(data.leaves).forEach(k=>{if(k<fy.from||k>fy.to)return;
      (data.leaves[k]||[]).forEach(l=>{if(!isCountedLeave(l))return;if(String(l.name||'').trim().toLowerCase()!==key)return;const c=l.category||'Other';used[c]=(used[c]||0)+leaveDaysOf(l);});});
    return used;
  }
  function getCapacity(data,key){
    const leaves=(data.leaves[key]||[]).filter(isActiveLeave);
    const amUsed=leaves.filter(l=>l.type==='full_day'||l.type==='half_day_am').length;
    const pmUsed=leaves.filter(l=>l.type==='full_day'||l.type==='half_day_pm').length;
    const dayUsed=leaves.length;const dayFull=dayUsed>=MAX_DAY;
    return {amUsed,pmUsed,dayUsed,dayFull,amFull:amUsed>=MAX_SHIFT||dayFull,pmFull:pmUsed>=MAX_SHIFT||dayFull,
      bothFull:(amUsed>=MAX_SHIFT&&pmUsed>=MAX_SHIFT)||dayFull,canAddFullDay:amUsed<MAX_SHIFT&&pmUsed<MAX_SHIFT&&!dayFull,
      canAddAM:amUsed<MAX_SHIFT&&!dayFull,canAddPM:pmUsed<MAX_SHIFT&&!dayFull};
  }
  function canAddLeave(data,key,leaveType){const cap=getCapacity(data,key);
    if(leaveType==='full_day')return cap.canAddFullDay;if(leaveType==='half_day_am')return cap.canAddAM;if(leaveType==='half_day_pm')return cap.canAddPM;return false;}
  function requestedLeaveDays(from,to,type,weekOff){
    if(!from||!to||from>to)return 0;
    let d=new Date(from),end=new Date(to),n=0,guard=0;
    while(d<=end&&guard++<400){const k=d.toISOString().slice(0,10);if(!isWeekOffDate(k,weekOff))n++;d.setDate(d.getDate()+1);}
    return n*(type==='full_day'?1:0.5);
  }
  /** submitLeave L1376-1406 (the loop and the toast). */
  function submitLoop(data,{name,from,to,type,category,reason,approver},weekOff){
    let d=new Date(from),end=new Date(to);let added=0,blocked=0,dupCount=0,weekendSkipped=0;
    while(d<=end){
      const key=d.toISOString().slice(0,10);
      if(!isWeekOffDate(key,weekOff)){
        if(!data.leaves[key])data.leaves[key]=[];
        const isDup=data.leaves[key].some(l=>leaveStatus(l)!=='rejected'&&l.name.toLowerCase()===name.toLowerCase()&&(l.type===type||l.type==='full_day'||type==='full_day'));
        if(isDup)dupCount++;else if(!canAddLeave(data,key,type))blocked++;
        else{data.leaves[key].push({id:'x',name,type,category,reason,approvedBy:approver,leaveFrom:from,leaveTo:to,status:'pending'});added++;}
      }else weekendSkipped++;
      d.setDate(d.getDate()+1);
    }
    const parts=[];
    if(added>0)parts.push(`${added} day(s) submitted — pending approval`);
    if(blocked>0)parts.push(`${blocked} skipped — day/shift full (max ${MAX_DAY}/day)`);
    if(dupCount>0)parts.push(`${dupCount} skipped — duplicate`);
    if(weekendSkipped>0&&added===0&&blocked===0&&dupCount===0)parts.push(`${weekendSkipped} day(s) skipped — weekend/weekly-off`);
    return {added,blocked,dupCount,weekendSkipped,toast:parts.join(', ')||'Nothing added'};
  }
  /** renderStats L838-853. */
  function stats(data,year,month){
    let fullDays=0,halfDays=0,atLimitDays=0,leaveDaysPayroll=0,pendingCount=0;const dim=new Date(year,month+1,0).getDate();
    const key=d=>`${year}-${String(month+1).padStart(2,'0')}-${String(d).padStart(2,'0')}`;
    for(let d=1;d<=dim;d++){const k=key(d);(data.leaves[k]||[]).forEach(l=>{const st=leaveStatus(l);if(st==='pending'){pendingCount++;return;}if(st==='rejected')return;
      if(l.type==='full_day'){fullDays++;leaveDaysPayroll+=1;}else{halfDays++;leaveDaysPayroll+=0.5;}});if(getCapacity(data,k).bothFull)atLimitDays++;}
    return {leaveDaysPayroll,pendingCount,fullDays,halfDays,atLimitDays};
  }
  /** exportReportCSV L1784-1808. */
  function reportCsv(data,year,month,ent){
    const rows=[['Employee','Full Days','Half AM','Half PM','Leave Days','Pending','Categories','FY Balance (used/quota)']];
    const dim=new Date(year,month+1,0).getDate();const empMap={};
    for(let d=1;d<=dim;d++){const k=`${year}-${String(month+1).padStart(2,'0')}-${String(d).padStart(2,'0')}`;
      (data.leaves[k]||[]).forEach(l=>{const st=leaveStatus(l);if(st==='rejected')return;if(!empMap[l.name])empMap[l.name]={name:l.name,full:0,halfAM:0,halfPM:0,pending:0,cats:{}};
        const em=empMap[l.name];if(st==='pending'){em.pending++;return;}if(l.type==='full_day')em.full++;else if(l.type==='half_day_am')em.halfAM++;else em.halfPM++;
        if(l.category)em.cats[l.category]=(em.cats[l.category]||0)+1;});}
    Object.values(empMap).sort((a,b)=>a.name.localeCompare(b.name)).forEach(r=>{const ld=r.full+(r.halfAM+r.halfPM)*0.5;
      const fy=fyRangeOf(year,month),used=fyUsage(data,r.name,fy),quota=entFor(r.name,ent);
      const bal=TRACKED_CATS.map(c=>c+' '+(used[c]||0)+'/'+quota[c]).join('; ');
      rows.push([r.name,r.full,r.halfAM,r.halfPM,ld,(r.pending||0),Object.keys(r.cats).join('; '),bal]);});
    return rows.map(r=>r.map(v=>`"${String(v).replace(/"/g,'""')}"`).join(',')).join('\n');
  }

  // ── HCC → SCC data (the oracle's input): cancelled records are SCC's removed ones; legacy records carry no status ──
  const typeOf=l=>l.half==='AM'?'half_day_am':l.half==='PM'?'half_day_pm':'full_day';
  const name=(s,id)=>x(s).staff.find(t=>t.id===id).name;
  function sccData(s){
    const data={leaves:{},agendas:{},employees:[]};
    for(const l of x(s).leaves){if(l.status==='Cancelled')continue;
      (data.leaves[l.date]??=[]).push({id:l.id,name:name(s,l.staffId),type:typeOf(l),category:M.HR_SCC_CATEGORY[l.type],reason:l.reason,approvedBy:l.approver,leaveFrom:l.from,leaveTo:l.to,
        ...(l.legacy?{}:{status:l.status.toLowerCase()})});}
    return data;
  }
  const sccEnt=s=>{const p=M.hrLeavePolicy(s),cat=e=>Object.fromEntries(['CL','SL','EL'].filter(c=>e[c]!=null).map(c=>[M.HR_SCC_CATEGORY[c],e[c]]));
    return {defaults:cat(p.ent),perEmployee:Object.fromEntries(Object.entries(p.perStaff??{}).map(([id,e])=>[name(s,id).toLowerCase(),cat(e)]))};};
  // Deterministic pseudo-random draws (no Math.random: the grid is the same on every run).
  const rng=seedN=>{let a=seedN>>>0;return ()=>{a=(a+0x6D2B79F5)>>>0;let t=a;t=Math.imul(t^(t>>>15),t|1);t^=t+Math.imul(t^(t>>>7),t|61);return ((t^(t>>>14))>>>0)/4294967296;};};
  const req=(s,uid,cmd)=>execute(s,uid,{type:'HR_LEAVE_REQUEST',approver:'owner',reason:'',...cmd});
  const add=(d,n)=>M.addDays(d,n);
  const base=()=>seed();// BASE: business date 24 Oct 2026, 12 staff, five leave requests (one record per date)

  test('[LV-SCC-PORT] getCapacity / canAddLeave: every mix of up to 7 records on a date (full / AM / PM × pending / approved / rejected) gives SCC’s figures',()=>{
    const kinds=[undefined,'AM','PM'],sts=['Pending','Approved','Rejected'],S0=base();let n=0;
    for(let mask=0;mask<10**4;mask+=1){
      const recs=[];let m=mask;for(let i=0;i<4;i++){const v=m%10;m=Math.floor(m/10);if(v===9)continue;recs.push({k:kinds[v%3],st:sts[Math.floor(v/3)]});}
      const extra=mask%7===0?[{k:undefined,st:'Pending'},{k:'AM',st:'Approved'},{k:'PM',st:'Pending'}]:[];
      const all=[...recs,...extra],date='2026-11-10';
      const ls=all.map((r,i)=>({id:'L'+i,staffId:'S'+i,date,type:'CL',from:date,to:date,days:r.k?0.5:1,reason:'',...(r.k?{half:r.k}:{}),status:r.st,requestedBy:'',requestedAt:''}));
      const data={leaves:{[date]:all.map((r,i)=>({id:'L'+i,name:'S'+i,type:r.k==='AM'?'half_day_am':r.k==='PM'?'half_day_pm':'full_day',status:r.st.toLowerCase()}))}};
      assert.deepEqual(M.hrCapacity(S0,date,ls),getCapacity(data,date));
      for(const k of kinds)assert.equal(M.hrCanAddLeave(S0,date,k,ls),canAddLeave(data,date,k==='AM'?'half_day_am':k==='PM'?'half_day_pm':'full_day'));
      // MAX_SHIFT (4) never decides: MAX_DAY (3) is reached first (owner question).
      const c=getCapacity(data,date);if(!c.dayFull)assert.ok(c.canAddFullDay&&c.canAddAM&&c.canAddPM,'a shift can be full only when the day is');
      n++;
    }
    assert.ok(n>=10000,`cases ${n}`);assert.deepEqual([M.HR_MAX_DAY,M.HR_MAX_SHIFT],[3,4]);
  });

  test('[LV-SCC-PORT] Add Leave: 600 random requests on a growing state — every date is added / full / duplicate / week-off exactly as SCC’s submitLeave, with SCC’s toast',()=>{
    const r=rng(116);let s=base(),n=0,adds=0,fulls=0,dups=0,offs=0;
    const staff=x(s).staff.filter(t=>t.active).map(t=>t.id),types=['full_day','half_day_am','half_day_pm'],cats=['CL','SL','EL','ML','CO','OTH','LOP'];
    for(let i=0;i<600;i++){
      if(i===200)s=execute(s,'owner',{type:'HR_LEAVE_POLICY_SET',weekOff:[0,6]});// SCC's default week-off from here on
      if(i===400)s=execute(s,'owner',{type:'HR_LEAVE_POLICY_SET',weekOff:[3]});
      const sid=staff[Math.floor(r()*staff.length)],from=add('2026-10-26',Math.floor(r()*70)),to=add(from,Math.floor(r()*4)),t=types[Math.floor(r()*3)],cat=cats[Math.floor(r()*7)];
      const half=t==='half_day_am'?'AM':t==='half_day_pm'?'PM':undefined,wo=M.hrLeaveWeekOff(s);
      // occasionally decide a random pending record (approve / reject) so rejected rows free their slot, as in SCC
      const pend=x(s).leaves.filter(l=>l.status==='Pending'&&x(s).staff.find(t=>t.id===l.staffId).userId!=='owner'&&l.requestedBy!=='owner');
      if(pend.length&&r()<0.3){const l=pend[Math.floor(r()*pend.length)];s=execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:l.id,approve:r()<0.6});}
      const data=sccData(s),o=submitLoop(data,{name:name(s,sid),from,to,type:t,category:M.HR_SCC_CATEGORY[cat],reason:'',approver:'owner'},wo);
      const plan=M.hrLeaveRequestPlan(s,sid,from,to,half);
      assert.deepEqual([plan.added,plan.blocked,plan.duplicates,plan.weekOff],[o.added,o.blocked,o.dupCount,o.weekendSkipped],`${i} ${sid} ${from}…${to} ${t}`);
      assert.equal(M.hrLeaveRequestToast(plan).text,o.toast);
      const own=x(s).staff.find(t=>t.id===sid).userId==='owner';
      const cmd={staffId:sid,leaveType:cat,from,to,...(half?{half}:{}),approver:own?'manager':'owner',confirmOverBalance:true,confirmBlackout:true};
      if(o.added===0){assert.throws(()=>req(s,'manager',cmd),e=>e.code==='NOT_AVAILABLE'&&e.message===o.toast);}
      else{s=req(s,'manager',cmd);
        // the HCC records now equal SCC's data after the same submit (date, person, type, category, request range, status)
        const mine=sccData(s),norm=d=>Object.keys(d.leaves).sort().map(k=>[k,d.leaves[k].filter(isActiveLeave).map(l=>[l.name,l.type,l.category,l.leaveFrom,l.leaveTo,leaveStatus(l)]).sort()]).filter(v=>v[1].length);
        assert.deepEqual(norm(mine),norm(data),`state after request ${i}`);}
      adds+=o.added;fulls+=o.blocked;dups+=o.dupCount;offs+=o.weekendSkipped;n++;
    }
    assert.ok(adds>200&&fulls>50&&dups>20&&offs>20,`added ${adds} · full ${fulls} · duplicate ${dups} · week-off ${offs}`);
  });

  test('[LV-SCC-PORT] AM + PM coexist, a half clashes with a full day, a rejected record frees the person and the slot; the 4th record of a date is skipped while the others are added',()=>{
    let s=req(base(),'manager',{staffId:'HR-S07',leaveType:'CL',from:'2026-11-17',to:'2026-11-17',half:'AM'});
    s=req(s,'manager',{staffId:'HR-S07',leaveType:'CL',from:'2026-11-17',to:'2026-11-17',half:'PM'});
    assert.deepEqual(M.hrLeavesOn(s,'2026-11-17').map(l=>l.half),['AM','PM']);
    assert.throws(()=>req(s,'manager',{staffId:'HR-S07',leaveType:'CL',from:'2026-11-17',to:'2026-11-17'}),e=>e.message==='1 skipped — duplicate');
    assert.throws(()=>req(s,'manager',{staffId:'HR-S07',leaveType:'SL',from:'2026-11-17',to:'2026-11-17',half:'AM'}),e=>e.message==='1 skipped — duplicate');
    s=req(s,'manager',{staffId:'HR-S08',leaveType:'CL',from:'2026-11-17',to:'2026-11-17'});// 3 records → full
    assert.equal(M.hrCapacity(s,'2026-11-17').dayFull,true);
    const r=req(s,'manager',{staffId:'HR-S09',leaveType:'CL',from:'2026-11-16',to:'2026-11-18'});
    assert.deepEqual(M.hrLeavesOn(r,'2026-11-17').filter(l=>l.staffId==='HR-S09').length,0);
    assert.deepEqual(['2026-11-16','2026-11-18'].map(d=>M.hrLeavesOn(r,d).some(l=>l.staffId==='HR-S09')),[true,true]);
    assert.match(r.audit[0].detail,/2 day\(s\) submitted — pending approval, 1 skipped — day\/shift full \(max 3\/day\)/);
    const am=x(s).leaves.find(l=>l.staffId==='HR-S07'&&l.half==='AM'),rej=execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:am.id,approve:false});
    assert.equal(M.hrCapacity(rej,'2026-11-17').dayUsed,2);
    const again=req(rej,'manager',{staffId:'HR-S07',leaveType:'CL',from:'2026-11-17',to:'2026-11-17',half:'AM'});
    assert.equal(M.hrLeavesOn(again,'2026-11-17').filter(l=>l.staffId==='HR-S07'&&M.hrLeaveActive(l)).length,2);
  });

  test('[LV-SCC-PORT] week-off: HCC default none (a 7-day hotel) records Saturday and Sunday; SCC’s default Sat + Sun skips them with SCC’s toast; a half day may cover a range',()=>{
    assert.deepEqual([M.HR_LEAVE_WEEKOFF_DEFAULT,M.HR_SCC_WEEKOFF_DEFAULT,M.hrLeaveWeekOff(base())],[[],[0,6],[]]);
    let s=req(base(),'manager',{staffId:'HR-S11',leaveType:'CL',from:'2026-11-13',to:'2026-11-16'});// Fri … Mon
    assert.deepEqual(x(s).leaves.filter(l=>l.staffId==='HR-S11').map(l=>l.date),['2026-11-13','2026-11-14','2026-11-15','2026-11-16']);
    const w=execute(base(),'owner',{type:'HR_LEAVE_POLICY_SET',weekOff:[0,6]});
    assert.throws(()=>req(w,'manager',{staffId:'HR-S11',leaveType:'CL',from:'2026-11-14',to:'2026-11-15'}),e=>e.code==='NOT_AVAILABLE'&&e.message==='2 day(s) skipped — weekend/weekly-off');
    s=req(w,'manager',{staffId:'HR-S11',leaveType:'CL',from:'2026-11-13',to:'2026-11-16',half:'PM'});
    assert.deepEqual(x(s).leaves.filter(l=>l.staffId==='HR-S11').map(l=>[l.date,l.half,l.days,l.from,l.to]),[['2026-11-13','PM',0.5,'2026-11-13','2026-11-16'],['2026-11-16','PM',0.5,'2026-11-13','2026-11-16']]);
    assert.equal(M.hrLeaveDays(w,'2026-11-13','2026-11-16','PM'),requestedLeaveDays('2026-11-13','2026-11-16','half_day_pm',[0,6]));
    for(const [f,t,h] of [['2026-11-01','2026-11-30'],['2026-11-02','2026-11-02','AM'],['2026-11-03','2026-11-01'],['2026-10-01','2028-01-01']])
      for(const wo of [[],[0,6],[1,2,3]]){const ss=execute(base(),'owner',{type:'HR_LEAVE_POLICY_SET',weekOff:wo});
        assert.equal(M.hrLeaveDays(ss,f,t,h),requestedLeaveDays(f,t,h==='AM'?'half_day_am':'full_day',wo),`${f} ${t} ${wo}`);}// incl. SCC's 400-day guard
  });

  test('[LV-SCC-PORT] entitlements: SCC entFor (override > saved default > 12 · 7 · 15, never below 0) and fyUsage (approved only, by date) on random histories; going over WARNS with SCC’s text and records after confirmation',()=>{
    const r=rng(119);let s=base();
    s=execute(s,'owner',{type:'HR_LEAVE_POLICY_SET',ent:{CL:'10.5',SL:'',EL:20}});
    assert.deepEqual(M.hrLeavePolicy(s).ent,{CL:10.5,SL:0,EL:20});// SCC: any number ≥ 0, fractions, a blank field saves 0
    s=execute(s,'owner',{type:'HR_LEAVE_ENT_OVERRIDE',staffId:'HR-S05',ent:{CL:'3',SL:'',EL:''}});
    s=execute(s,'owner',{type:'HR_LEAVE_ENT_OVERRIDE',staffId:'HR-S06',ent:{CL:2,SL:2,EL:2}});
    s=execute(s,'owner',{type:'HR_LEAVE_ENT_OVERRIDE',staffId:'HR-S06',ent:{CL:'',SL:'',EL:''}});// all blank → override removed
    assert.deepEqual([M.hrEntFor(s,'HR-S05'),M.hrEntFor(s,'HR-S06'),M.hrLeavePolicy(s).perStaff],[{CL:3,SL:0,EL:20},{CL:10.5,SL:0,EL:20},{'HR-S05':{CL:3}}]);
    for(let i=0;i<120;i++){const sid=['HR-S02','HR-S05','HR-S07','HR-S11'][Math.floor(r()*4)],d=add('2026-10-26',Math.floor(r()*170)),half=r()<0.3?'AM':undefined;
      try{s=req(s,'manager',{staffId:sid,leaveType:['CL','SL','EL'][Math.floor(r()*3)],from:d,to:d,...(half?{half}:{}),confirmOverBalance:true});}catch{continue;}
      const l=x(s).leaves.at(-1);if(r()<0.7)s=execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:l.id,approve:r()<0.8});}
    const data=sccData(s),ent=sccEnt(s);
    for(const sid of ['HR-S02','HR-S05','HR-S07','HR-S11'])for(const fyStart of [2026,2027]){
      const fy=fyRangeOf(fyStart,4),o=fyUsage(data,name(s,sid),fy),h=M.hrFyUsage(s,sid,fy);
      for(const c of ['CL','SL','EL'])assert.equal(h[c]??0,o[M.HR_SCC_CATEGORY[c]]??0,`${sid} ${fy.label} ${c}`);
      const e=entFor(name(s,sid),ent);assert.deepEqual(M.hrEntFor(s,sid),{CL:e['Casual Leave'],SL:e['Sick Leave'],EL:e['Earned Leave']});
      // SCC submitLeave balance check + renderBalanceHint text, for a fresh 3-day request
      for(const c of ['CL','SL','EL']){const from=`${fyStart}-12-21`,to=`${fyStart}-12-23`,used=o[M.HR_SCC_CATEGORY[c]]||0,q=e[M.HR_SCC_CATEGORY[c]],rq=requestedLeaveDays(from,to,'full_day',[]),fmt=n=>(n%1===0?n:n.toFixed(1));
        const want=used+rq>q?`${name(s,sid)} has used ${fmt(used)} of ${q} ${M.HR_SCC_CATEGORY[c]} day(s) this ${fy.label}.\nThis request adds ${fmt(rq)} more and EXCEEDS the balance.\n\nRecord it anyway (it will still need approval)?`:undefined;
        assert.equal(M.hrLeaveOverBalance(s,{id:sid,name:name(s,sid)},c,from,to),want);
        const after=used+rq;assert.equal(M.hrLeaveBalanceHint(s,sid,c,from,to).text,`${M.HR_SCC_CATEGORY[c]}: used ${fmt(used)} of ${q} this ${fy.label}`+(rq?` — this request adds ${fmt(rq)} → ${fmt(after)}${after>q?' (EXCEEDS balance)':''}`:''));}
    }
    assert.equal(M.hrLeaveBalanceHint(s,'HR-S05','ML','2026-12-01','2026-12-01').text,'Maternity / Paternity: 0 day(s) used this FY 2026–27 (no annual quota tracked)');
    // Ravi (2 CL approved in September) with CL 6: a 5-day CL request warns (refused until confirmed), then records with confirmOverBalance — never a hard block
    const z=execute(base(),'owner',{type:'HR_LEAVE_ENT_OVERRIDE',staffId:'HR-S05',ent:{CL:6}});
    assert.throws(()=>req(z,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-01',to:'2026-12-05'}),e=>e.code==='INVALID_TRANSITION'&&/has used 2 of 6 Casual Leave day\(s\)[^]*adds 5 more and EXCEEDS the balance/.test(e.message));
    assert.equal(x(req(z,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-01',to:'2026-12-05',confirmOverBalance:true})).leaves.filter(l=>l.date>='2026-12-01'&&l.staffId==='HR-S05').length,5);
    // pending leave is not "used" (SCC; owner question): two pending 3-day CL requests together pass the quota without a warning
    const p1=req(z,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-01',to:'2026-12-03'});
    assert.equal(M.hrLeaveOverBalance(p1,{id:'HR-S05',name:'Ravi Shinde'},'CL','2026-12-08','2026-12-10'),undefined);
  });

  test('[LV-SCC-PORT] blackouts: hard refuses with SCC’s toast, soft and store holiday need confirmation (SCC’s confirm text), scope = all departments or one; week-off dates are not checked',()=>{
    let s=execute(base(),'owner',{type:'HR_LEAVE_BLACKOUT_ADD',from:'2026-12-25',to:'2026-12-20',dept:'',kind:'blackout',hard:true,label:'Christmas rush'});
    s=execute(s,'owner',{type:'HR_LEAVE_BLACKOUT_ADD',from:'2026-12-10',to:'2026-12-11',dept:'HK',kind:'blackout',hard:false,label:'HK deep clean'});
    s=execute(s,'owner',{type:'HR_LEAVE_BLACKOUT_ADD',from:'2026-12-15',to:'2026-12-15',dept:'',kind:'holiday',hard:true,label:''});
    const b=M.hrLeavePolicy(s).blackouts;assert.deepEqual(b.map(e=>[e.from,e.to,e.dept,e.kind,e.hard]),[['2026-12-20','2026-12-25','','blackout',true],['2026-12-10','2026-12-11','HK','blackout',false],['2026-12-15','2026-12-15','','holiday',false]]);
    assert.throws(()=>req(s,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-19',to:'2026-12-21'}),e=>e.code==='NOT_AVAILABLE'&&e.message==='Leave falls in a hard blackout window (Christmas rush) — blocked');
    assert.throws(()=>req(s,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-11',to:'2026-12-11'}),e=>e.message==='These dates fall in a blackout window (HK deep clean): Casual Leave leave is discouraged.\nRecord it anyway?');
    assert.ok(req(s,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-11',to:'2026-12-11',confirmBlackout:true}));
    assert.ok(req(s,'manager',{staffId:'HR-S02',leaveType:'CL',from:'2026-12-11',to:'2026-12-11'}));// FO: the HK blackout does not apply
    assert.throws(()=>req(s,'manager',{staffId:'HR-S02',leaveType:'SL',from:'2026-12-15',to:'2026-12-15'}),e=>/Store holiday\): Sick Leave leave is discouraged/.test(e.message));// Q3: a store holiday warns
    const w=execute(s,'owner',{type:'HR_LEAVE_POLICY_SET',weekOff:[0,6]});// 19 / 20 Dec = Sat / Sun: skipped, so the hard window (20–25) still bites from Mon 21
    assert.equal(M.hrBlackoutHits(w,'2026-12-19','2026-12-20','FO').hits.length,0);
    assert.equal(M.hrLeaveBlackoutWarning(M.hrBlackoutHits(s,'2026-12-10','2026-12-21','HK')),'⛔ Hard block — blackout window: HK deep clean, Store holiday, Christmas rush');
    const id=b[0].id;s=execute(s,'owner',{type:'HR_LEAVE_BLACKOUT_REMOVE',id});assert.ok(req(s,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-12-21',to:'2026-12-21'}));
    assert.throws(()=>execute(s,'manager',{type:'HR_LEAVE_BLACKOUT_ADD',from:'2026-12-01',to:'2026-12-01'}),code('FORBIDDEN'));
  });

  test('[LV-SCC-PORT] per-date decisions: each date approved or rejected on its own (pending only, SCC’s message); nobody decides their own leave or request (DEC-119); no reason needed',()=>{
    let s=req(base(),'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-11-23',to:'2026-11-25'});
    const ids=x(s).leaves.filter(l=>l.staffId==='HR-S05'&&l.date>='2026-11-23').map(l=>l.id);assert.equal(ids.length,3);
    s=execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:ids[0],approve:true});s=execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:ids[1],approve:false});
    assert.deepEqual(ids.map(id=>x(s).leaves.find(l=>l.id===id).status),['Approved','Rejected','Pending']);
    assert.throws(()=>execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:ids[0],approve:false}),e=>e.message==='This leave is already approved');
    assert.throws(()=>execute(s,'manager',{type:'HR_LEAVE_DECIDE',id:ids[2],approve:true}),code('FORBIDDEN'));// manager raised it
    const own=req(base(),'manager',{staffId:'HR-S01',leaveType:'CL',from:'2026-11-26',to:'2026-11-26',approver:'owner'});
    assert.throws(()=>req(base(),'manager',{staffId:'HR-S01',leaveType:'CL',from:'2026-11-26',to:'2026-11-26',approver:'manager'}),/approver cannot be the person on leave/);
    assert.throws(()=>req(base(),'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-11-26',to:'2026-11-26',approver:''}),e=>e.message==='Approver name is required');
    assert.throws(()=>req(base(),'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-11-26',to:'2026-11-26',approver:'housekeeper'}),/Owner or a Manager/);
    assert.throws(()=>execute(own,'manager',{type:'HR_LEAVE_DECIDE',id:x(own).leaves.at(-1).id,approve:true}),code('FORBIDDEN'));
    assert.deepEqual(M.hrApproverChoices(base()).map(a=>[a.id,a.role]),[['owner','Owner'],['manager','Manager']]);
    const ok=execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:ids[2],approve:false});assert.match(ok.audit[0].detail,/Leave rejected — kept for audit/);
    assert.deepEqual(M.hrPendingLeaves(base()).map(l=>l.date),['2026-10-27','2026-10-28','2026-10-29','2026-11-06']);// the approvals list: one row per pending date
  });

  test('[LV-SCC-PORT] remove = cancel with an audit record (DEC-119): hidden from capacity, calendar, stats, report and files; never deleted; an approved past day goes through DEC-075',()=>{
    let s=req(base(),'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-11-23',to:'2026-11-23'});const id=x(s).leaves.at(-1).id,n=x(s).leaves.length;
    s=execute(s,'housekeeper',{type:'HR_LEAVE_CANCEL',id});
    const l=x(s).leaves.find(q=>q.id===id);assert.deepEqual([l.status,l.cancelledBy,x(s).leaves.length],['Cancelled','housekeeper',n]);
    assert.equal(M.hrCapacity(s,'2026-11-23').dayUsed,0);assert.equal(M.hrLeaveMonthView(s,'2026-11').cells.find(c=>c.date==='2026-11-23').chips.length,0);
    assert.ok(!JSON.stringify(JSON.parse(M.hrLeaveExportJson(s).json).leaves).includes(id));
    assert.throws(()=>execute(s,'manager',{type:'HR_LEAVE_CANCEL',id}),code('INVALID_TRANSITION'));
    const past=x(base()).leaves.find(q=>q.id==='HR-LV-1');assert.equal(past.status,'Approved');
    assert.throws(()=>execute(base(),'manager',{type:'HR_LEAVE_CANCEL',id:'HR-LV-1'}),/attendance correction/);
    assert.throws(()=>execute(base(),'housekeeper',{type:'HR_LEAVE_CANCEL',id:'HR-LV-2'}),code('FORBIDDEN'));// approved: a manager removes it
    assert.ok(execute(base(),'manager',{type:'HR_LEAVE_CANCEL',id:'HR-LV-2'}));
  });

  test('[LV-SCC-PORT] stats strip, report and CSV equal SCC’s renderStats / exportReportCSV on a random month; print rows and the 7-day strip follow SCC',()=>{
    const r=rng(42);let s=base();
    for(let i=0;i<80;i++){const sid=x(s).staff[Math.floor(r()*12)].id,d=add('2026-11-01',Math.floor(r()*30)),h=r()<0.4?(r()<0.5?'AM':'PM'):undefined;
      try{s=req(s,'manager',{staffId:sid,leaveType:['CL','SL','EL','ML','CO','OTH','LOP'][Math.floor(r()*7)],from:d,to:add(d,Math.floor(r()*2)),...(h?{half:h}:{}),confirmOverBalance:true});}catch{continue;}
      for(const l of x(s).leaves.filter(q=>q.status==='Pending'&&q.requestedBy==='manager'&&q.staffId!=='HR-S01'))if(r()<0.5)s=execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:l.id,approve:r()<0.8});}
    const data=sccData(s),st=stats(data,2026,10),h=M.hrLeaveStats(s,'2026-11');
    assert.deepEqual(h.slice(0,5).map(c=>c.value),[st.leaveDaysPayroll%1===0?String(st.leaveDaysPayroll):st.leaveDaysPayroll.toFixed(1),String(st.pendingCount),String(st.fullDays),String(st.halfDays),String(st.atLimitDays)]);
    assert.equal(h[3].sub,'= '+(st.halfDays/2).toFixed(1)+' payroll days');
    const csv=M.hrLeaveReportCsv(s,'2026-11');assert.equal(csv.name,'leave_report_November_2026.csv');
    // SCC writes the categories in first-seen order of its date walk with its category names; HCC uses HCC's list names on screen and SCC's in the CSV
    assert.equal(csv.csv,reportCsv(data,2026,10,sccEnt(s)));
    const rep=M.hrLeaveReport(s,'2026-11');assert.ok(rep.rows.length>5);assert.equal(rep.total.leaveDays,rep.rows.reduce((n,x)=>n+x.leaveDays,0));
    const pr=M.hrLeavePrintRows(s,'2026-11');assert.equal(pr.leaves.length,Object.keys(data.leaves).filter(k=>k.startsWith('2026-11')).reduce((n,k)=>n+data.leaves[k].length,0));// SCC prints every record, rejected too
    assert.match(pr.title,/Leave Calendar — November 2026/);
    const strip=M.hrLeaveStrip(s,'2026-11-10');assert.equal(strip.length,7);assert.deepEqual(strip.map(d=>d.onLeave),strip.map(d=>getCapacity(data,d.date).dayUsed));
    assert.equal(strip[0].avail,`${Math.max(0,12-strip[0].onLeave)}/12 avail`);
    // advisory department caps (SCC per-store caps): over = used > cap, strictly; they never block
    const c=execute(s,'owner',{type:'HR_LEAVE_POLICY_SET',deptCap:{HK:0,FO:''}});assert.deepEqual([M.hrDeptCap(c,'HK'),M.hrDeptCap(c,'FO')],[0,3]);
    const day=Object.keys(data.leaves).find(k=>k.startsWith('2026-11')&&data.leaves[k].some(l=>isActiveLeave(l)&&['Neha Gaikwad','Ravi Shinde','Kavya Patil'].includes(l.name)));
    if(day)assert.equal(M.hrDeptTally(c,day).depts.find(d=>d.dept==='HK').over,true);
  });

  test('[LV-SCC-PORT] JSON import = SCC importJSON: legacy rows count as approved, week-off keys / bad rows skipped, unknown category → Other, same id ignored, same person or a full date skipped — same counts as SCC’s merge',()=>{
    const file={leaves:{
      '2026-11-30':[{id:'a1',name:'ravi shinde',type:'full_day',category:'Casual Leave',approvedBy:'Sagar',leaveFrom:'2026-11-30',leaveTo:'2026-12-01'},
        {id:'a2',name:'Aarav Joshi',type:'half_day_am',category:'Bereavement',approvedBy:'Meera',status:'pending'},
        {id:'a3',name:'Nobody Here',type:'full_day',category:'Sick Leave',approvedBy:'Sagar'},{id:'a4',name:'Priya Nair',type:'quarter_day',approvedBy:'Sagar'},
        {id:'a5',name:'Priya Nair',type:'full_day',category:'Leave without pay',approvedBy:'Unknown Boss',status:'rejected'},{id:'a6',name:'Neha Gaikwad',type:'full_day',approvedBy:''}],
      '2026-12-05':[{id:'b1',name:'Kavya Patil',type:'full_day',category:'Earned Leave',approvedBy:'Sagar',status:'approved'}],
      '2026-12-06':[{id:'b2',name:'Kavya Patil',type:'full_day',category:'Earned Leave',approvedBy:'Sagar'}],
      'bad-key':[{}],'2026-12-07':'not a list'},
      agendas:{'2026-11-30':[{id:'g1',title:'Staff meeting',type:'meeting',time:'10:00',description:'Monthly'},{id:'g2',title:'',type:'meeting'}]},
      entitlements:{defaults:{'Casual Leave':11,'Sick Leave':-1},perEmployee:{'ravi shinde':{'Earned Leave':18}}},weekOff:{days:[0,9]}};
    let s=execute(base(),'owner',{type:'HR_LEAVE_POLICY_SET',weekOff:[0]});// 6 Dec 2026 is a Sunday → that key is skipped
    const plan=M.hrLeaveImportParse(s,file);
    assert.deepEqual([plan.leaves.length,plan.agendas.length,plan.skipped],[4,1,7]);// a3 (unknown staff), a4 (type), a6 (approver), the Sunday key, bad key, not a list, g2
    assert.deepEqual(plan.leaves.map(l=>[l.staffId,l.type,l.status,l.legacy,l.approver??l.approverName]),[['HR-S05','CL','Approved',true,'owner'],['HR-S02','OTH','Pending',false,'manager'],['HR-S03','LOP','Rejected',false,'Unknown Boss'],['HR-S06','EL','Approved',false,'owner']]);
    assert.match(M.hrLeaveImportPreview(plan),/• 4 valid leave record\(s\)\n• 1 valid agenda item\(s\)\n• 7 record\(s\) skipped/);
    s=req(s,'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-11-30',to:'2026-11-30',half:'PM'});// Ravi already has a PM: import skips (any half, stricter than the screen — Q8)
    const t=execute(s,'owner',{type:'HR_LEAVE_IMPORT',data:file});
    assert.equal(t.audit[0].detail,'Imported: 3 leaves, 1 agendas. Skipped: 8');
    const imp=x(t).leaves.filter(l=>l.sccId);assert.deepEqual(imp.map(l=>[l.sccId,l.status,!!l.legacy]),[['a2','Pending',false],['a5','Rejected',false],['b1','Approved',false]]);
    assert.deepEqual([M.hrLeavePolicy(t).ent.CL,M.hrLeavePolicy(t).ent.SL,M.hrEntFor(t,'HR-S05').EL,M.hrLeaveWeekOff(t)],[11,7,18,[0]]);
    const again=execute(t,'owner',{type:'HR_LEAVE_IMPORT',data:file});assert.equal(again.audit[0].detail,'Imported: 0 leaves, 0 agendas. Skipped: 8');// same ids ignored (not counted, SCC)
    assert.throws(()=>execute(base(),'owner',{type:'HR_LEAVE_IMPORT',data:{leaves:{}}}),e=>e.message==='Import failed: Missing "agendas" object');
    assert.throws(()=>execute(base(),'manager',{type:'HR_LEAVE_IMPORT',data:file}),code('FORBIDDEN'));
    // a legacy record (no status) is approved and counted in the balance, as SCC; a locked pay month is skipped (HCC)
    const leg=execute(base(),'owner',{type:'HR_LEAVE_IMPORT',data:{leaves:{'2026-12-02':[{id:'z',name:'Ravi Shinde',type:'full_day',category:'Casual Leave',approvedBy:'Sagar'}],
      '2026-08-04':[{id:'y',name:'Ravi Shinde',type:'full_day',category:'Casual Leave',approvedBy:'Sagar'}]},agendas:{}}});
    assert.equal(leg.audit[0].detail,'Imported: 1 leaves, 0 agendas. Skipped: 1');assert.equal(M.hrFyUsage(leg,'HR-S05',M.hrFy('2026-12-02')).CL,3);
  });

  test('[LV-SCC-PORT] JSON backup = SCC exportJSON (leaves by date, agendas, entitlements, week-off; no caps or blackouts) and imports back into an empty desk unchanged',()=>{
    let s=execute(base(),'manager',{type:'HR_AGENDA_ADD',date:'2026-11-02',title:'Owner visit',time:'',agendaType:'reminder',description:''});
    s=execute(s,'manager',{type:'HR_AGENDA_ADD',date:'2026-11-02',title:'Briefing',time:'09:30',agendaType:'meeting',description:'All staff'});
    assert.deepEqual(M.hrAgendasOn(s,'2026-11-02').map(a=>a.title),['Briefing','Owner visit']);// SCC: by time, blank times last
    s=execute(s,'owner',{type:'HR_LEAVE_ENT_OVERRIDE',staffId:'HR-S07',ent:{SL:9}});
    const f=M.hrLeaveExportJson(s),j=JSON.parse(f.json);assert.equal(f.name,'leavedesk_backup_2026-10-24.json');
    assert.deepEqual(Object.keys(j),['leaves','agendas','employees','entitlements','weekOff']);
    assert.deepEqual(j.leaves['2026-10-28'][0],{id:'HR-LV-3',name:'Kavya Patil',type:'full_day',category:'Casual Leave',reason:'Personal work (fictional)',approvedBy:'Meera',
      leaveFrom:'2026-10-28',leaveTo:'2026-10-29',status:'pending',staffId:'HR-S06'});
    assert.deepEqual(j.entitlements,{defaults:{'Casual Leave':12,'Sick Leave':7,'Earned Leave':15},perEmployee:{'farhan qureshi':{'Sick Leave':9}}});
    const cleared=execute(s,'owner',{type:'HR_LEAVE_CLEAR',confirm:'DELETE'});
    assert.equal(cleared.audit[0].detail,'All data cleared · 8 leave record(s) cancelled (kept for audit) · 2 agenda item(s) removed · entitlements and week-off back to the defaults');
    assert.equal(x(cleared).leaves.filter(l=>l.status!=='Cancelled'&&l.date>='2026-10-01').length,0);
    const back=execute(cleared,'owner',{type:'HR_LEAVE_IMPORT',data:j});
    const key=t=>x(t).leaves.filter(l=>l.status!=='Cancelled'&&l.date>='2026-10-01').map(l=>[l.staffId,l.date,l.type,l.half??'',l.status]).sort();
    assert.deepEqual(key(back),key(s));assert.deepEqual(M.hrAgendasOn(back,'2026-11-02').map(a=>a.title),['Briefing','Owner visit']);assert.equal(M.hrEntFor(back,'HR-S07').SL,9);
  });

  test('[LV-SCC-PORT] Clear All keeps a locked pay month: a record of a locked month stays as it is',()=>{
    const s=base();x(s).leaves.push({id:'AUG-1',staffId:'HR-S05',date:'2026-08-04',type:'CL',from:'2026-08-04',to:'2026-08-04',days:1,reason:'',status:'Approved',requestedBy:'manager',requestedAt:''});
    const c=execute(s,'owner',{type:'HR_LEAVE_CLEAR',confirm:'DELETE'});
    assert.equal(x(c).leaves.find(l=>l.id==='AUG-1').status,'Approved');assert.match(c.audit[0].detail,/· 1 in locked pay months kept/);
  });
  test('[LV-SCC-PORT] Clear All: type DELETE; leave is cancelled and kept for audit (a locked pay month keeps its records), agendas go, entitlements and week-off reset; caps and blackouts survive (SCC)',()=>{
    let s=execute(base(),'owner',{type:'HR_LEAVE_POLICY_SET',ent:{CL:5,SL:5,EL:5},weekOff:[0],deptCap:{HK:1}});
    s=execute(s,'owner',{type:'HR_LEAVE_BLACKOUT_ADD',from:'2026-12-01',to:'2026-12-02',dept:'',kind:'blackout',hard:false,label:'x'});
    s=execute(s,'manager',{type:'HR_AGENDA_ADD',date:'2026-11-02',title:'T',agendaType:'agenda'});
    assert.throws(()=>execute(s,'owner',{type:'HR_LEAVE_CLEAR',confirm:'delete'}),e=>e.message==='Type DELETE (uppercase) to confirm');
    assert.throws(()=>execute(s,'manager',{type:'HR_LEAVE_CLEAR',confirm:'DELETE'}),code('FORBIDDEN'));
    const c=execute(s,'owner',{type:'HR_LEAVE_CLEAR',confirm:'DELETE'});
    assert.deepEqual([M.hrLeavePolicy(c).ent,M.hrLeaveWeekOff(c),M.hrDeptCap(c,'HK'),M.hrLeavePolicy(c).blackouts.length,(x(c).agendas??[]).length],[{CL:12,SL:7,EL:15},[],1,1,0]);
    assert.ok(x(c).leaves.length===x(s).leaves.length&&x(c).leaves.every(l=>l.status==='Cancelled'));
    assert.deepEqual(M.hrLeaveClearList(base())[0],'8 leave record(s) across all dates (cancelled and kept for audit; a locked pay month keeps its records)');
  });

  test('[LV-SCC-PORT] old saves: leave SPANS become one record per counted date on restore (idempotent; ids kept, request range kept); a stored Leave types list gets "Other"',()=>{
    const s=base(),old=JSON.parse(JSON.stringify(s));
    old.staffX.leaves=[{id:'OLD-1',staffId:'HR-S05',type:'CL',from:'2026-11-12',to:'2026-11-16',days:4,reason:'Trip',status:'Approved',requestedBy:'housekeeper',requestedAt:'2026-11-01T10:00'},
      {id:'OLD-2',staffId:'HR-S03',type:'SL',from:'2026-11-18',to:'2026-11-18',days:0.5,half:'PM',reason:'Clinic',status:'Pending',requestedBy:'cashier',requestedAt:'2026-11-01T10:00'}];
    old.mastersX={rows:{leaveTypes:M.mstRows(s,'leaveTypes').filter(r=>r.code!=='OTH')},history:[]};
    const r=M.inspectSaved(JSON.stringify(old));assert.equal(r.migrated,true);
    // Ravi's weekly off is Friday (13 Nov): the old span counted 12, 14, 15, 16 Nov
    assert.deepEqual(x(r.state).leaves.map(l=>[l.id,l.date,l.days,l.half??'',l.from,l.to]),[['OLD-1','2026-11-12',1,'','2026-11-12','2026-11-16'],['OLD-1-2','2026-11-14',1,'','2026-11-12','2026-11-16'],
      ['OLD-1-3','2026-11-15',1,'','2026-11-12','2026-11-16'],['OLD-1-4','2026-11-16',1,'','2026-11-12','2026-11-16'],['OLD-2','2026-11-18',0.5,'PM','2026-11-18','2026-11-18']]);
    assert.ok(M.mstRows(r.state,'leaveTypes').some(t=>t.code==='OTH'));
    const twice=M.inspectSaved(JSON.stringify(r.state));assert.equal(twice.migrated,false);assert.deepEqual(x(twice.state).leaves,x(r.state).leaves);
  });

  test('[LV-SCC-PORT] payroll feed (DEC-115 / SCC hrCounts): approved per-day records count by date — paid categories as leave, LOP as absent; a record on a rostered weekly off is leave; pending / rejected / cancelled are not',()=>{
    let s=req(base(),'manager',{staffId:'HR-S07',leaveType:'ML',from:'2026-11-08',to:'2026-11-09'});// Farhan: weekly off Sunday 8 Nov
    s=req(s,'manager',{staffId:'HR-S07',leaveType:'LOP',from:'2026-11-10',to:'2026-11-10',half:'AM'});
    s=req(s,'manager',{staffId:'HR-S07',leaveType:'OTH',from:'2026-11-11',to:'2026-11-12'});
    const mine=x(s).leaves.filter(l=>l.staffId==='HR-S07');
    for(const l of mine.slice(0,3))s=execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:l.id,approve:true});
    s=execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:mine[3].id,approve:false});
    const st=x(s).staff.find(t=>t.id==='HR-S07'),c=M.hrCounts(s,st,'2026-11');
    assert.deepEqual([c.paidLeave,c.lopLeave],[2,0.5]);// 8 Nov (weekly off, but a leave record) + 9 Nov ML; ½ LOP; 11 Nov rejected, 12 Nov pending
    assert.equal(M.hrDayStatus(s,st,'2026-11-08').kind,'leave');
    s=execute(s,'manager',{type:'HR_LEAVE_CANCEL',id:mine[1].id});assert.equal(M.hrCounts(s,st,'2026-11').paidLeave,1);
    assert.deepEqual(M.HR_LEAVE_TYPES,['CL','SL','EL','ML','CO','OTH','LOP']);assert.deepEqual(M.HR_LEAVE_TYPES.map(t=>M.hrPaidLeave(t)),[true,true,true,true,true,true,false]);
  });

  test('[LV-SCC-PORT] HCC rules removed (SCC has none): no 15-day maximum, a request may cross 31 March, the reason is optional (≤ 500), any category for daily-wage staff, no carry-forward or encashment',()=>{
    let s=req(base(),'manager',{staffId:'HR-S10',leaveType:'CL',from:'2027-03-25',to:'2027-04-15',reason:'',confirmOverBalance:true});
    assert.equal(x(s).leaves.filter(l=>l.staffId==='HR-S10'&&l.date>='2027-03-25').length,22);
    assert.throws(()=>req(base(),'manager',{staffId:'HR-S05',leaveType:'CL',from:'2026-11-02',to:'2026-11-02',reason:'x'.repeat(501)}),/500/);
    assert.throws(()=>req(base(),'manager',{staffId:'HR-S05',leaveType:'BL',from:'2026-11-02',to:'2026-11-02'}),/category/);
    assert.throws(()=>req(base(),'housekeeper',{staffId:'HR-S05',leaveType:'CL',from:'2026-10-20',to:'2026-10-20'}),/business date/);// DEC-075 kept
    assert.equal(M.hrLeaveCarryCap,undefined);assert.equal(M.hrElEncashment,undefined);assert.equal(M.hrLeaveCarried,undefined);
    assert.deepEqual(M.mstSpec('leaveTypes').fields.map(f=>f.key),['code','name','paid','yearlyQuota','sortOrder','active']);
    assert.throws(()=>execute(base(),'owner',{type:'MST_MASTER_SAVE',master:'leaveTypes',row:{code:'BL',name:'Bereavement',paid:true}}),/fixed/);
  });

  test('[LV-SCC-PORT] screens: desk (stats, 7-day strip, calendar), approvals, report, settings, day view, detail, Add Leave, print, import and clear render on desktop and phone',async()=>{
    const {build}=await import('esbuild');const {writeFile}=await import('node:fs/promises');
    await writeFile('tests/.generated/leave-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';import {can} from '../../src/services/mock';
import {LeaveDeskView,LeaveApprovalsView,LeaveReportView,LeaveSettingsView} from '../../src/features/staff/Leave';
import {registration} from '../../src/features/staff/register';
function ctx(s:any,uid:string,mobile:boolean){const user=s.users.find((u:any)=>u.id===uid);return {s,uid,user,busy:false,run:async()=>true,open(){},closeModal(){},toast(){},setDirty(){},permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile}} as any;}
const wrap=(s:any,uid:string,mobile:boolean,el:any)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile)}>{el}</HccContext.Provider>);
export function views(s:any,uid:string,phone:boolean,asOf:string){const f=()=>{};return [wrap(s,uid,phone,<LeaveDeskView phone={phone} ym="2026-10" setYm={f} asOf={asOf} setAsOf={f}/>),
  wrap(s,uid,phone,<LeaveApprovalsView phone={phone} asOf={asOf}/>),wrap(s,uid,phone,<LeaveReportView phone={phone} ym="2026-09" setYm={f}/>),wrap(s,uid,phone,<LeaveSettingsView phone={phone} asOf={asOf}/>)];}
export function modal(s:any,uid:string,type:string,id?:string,room?:string){const C=(registration.modals as any)[type];return wrap(s,uid,false,<C id={id} room={room}/>);}
`);
    await build({entryPoints:['tests/.generated/leave-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/leave-ui.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __lvReq} from 'node:module';const require=__lvReq(import.meta.url);"}});
    const UI=await import('./.generated/leave-ui.mjs');const s=base();
    for(const phone of [false,true]){const [desk,appr,rep,set]=UI.views(s,'owner',phone,s.date);
      assert.match(desk,/Leave Days/);assert.match(desk,/Limit Reached/);assert.match(desk,/on leave/);assert.match(desk,/Add Leave/);assert.match(desk,/FY 2026–27/);
      assert.match(appr,/4 request\(s\) awaiting a decision/);assert.match(rep,/Ravi Shinde/);assert.match(set,/Leave week-off days/);
      if(phone)for(const h of [desk,appr,rep,set])assert.ok(!h.includes('<table'),'phone uses cards');
      else{assert.match(desk,/\? Kavya Patil/);assert.match(rep,/FY Balance \(used\/quota\)/);}}
    const past=UI.views(s,'owner',false,'2026-10-01')[0];assert.match(past,/\(past date\) — read-only/);assert.ok(!/Add Leave/.test(past));
    assert.match(UI.modal(s,'manager','hr-leave-day','2026-10-28'),/AM Shift \(Morning, till 2 PM\)/);
    assert.match(UI.modal(s,'manager','hr-leave-day','2026-10-28'),/1\/3 leaves today — 2 remaining \(max 3 per day\)/);
    assert.match(UI.modal(s,'owner','hr-leave-detail','HR-LV-3'),/Leave From/);
    const add=UI.modal(s,'manager','hr-leave',undefined,'2026-11-02');assert.match(add,/Approver \(Manager \/ Owner\)/);assert.match(add,/Half Day — Morning \(till 2 PM\)/);assert.match(add,/Other/);
    assert.match(UI.modal(s,'owner','hr-leave-print','2026-09'),/Total leaves: 2/);
    for(const m of ['hr-leave-policy','hr-leave-import','hr-leave-clear','hr-agenda','hr-leave-blackout','hr-leave-approvals'])assert.ok(UI.modal(s,'owner',m).length>100,m);
    assert.match(UI.modal(s,'owner','hr-leave-ent','HR-S05'),/Blank = use the defaults/);
  });
}

// Phase C tests: module 13–26 history in FY26 is consistent with the stays, folios, closings and each other.
const FROM='2026-04-01',TO='2026-10-23';
const addDays=(d,n)=>new Date(Date.parse(d+'T12:00:00Z')+n*86400000).toISOString().slice(0,10);
const range=(a,b)=>{const out=[];for(let d=a;d<=b;d=addDays(d,1))out.push(d);return out;};
const nightsBetween=(a,b)=>Math.round((Date.parse(b)-Date.parse(a))/86400000);

export function register({test,M,assert}){
  const {loadScenario,baseFixture,scenarios,metrics,drawer,totals,grpPicked,grpSlice,mntSla,invConsumption,invSlice,hrComputeLine,hrRunTotals,hrSlice,hrApplyRecoveries,
    posDaySummary,posSlice,chnCommissionReport,chnSlice,msgPendingTriggers,msgSlice,accLiveVouchers,accExceptions,accDayStatus,accSlice,accLockBlocker,accTotals,gsvSlice,hovSlice,yldSlice,
    mntSlice,reportPack,execute}=M;
  const s=loadScenario('FY26'),base=baseFixture(),hist=s.bookings.filter(b=>b.id.startsWith('HCC-H')),departed=hist.filter(b=>b.status==='Departed');
  const live=['Departed','In house','Confirmed','Held'];
  const typeOf=room=>s.rooms.find(r=>r.id===room).type;
  const stayNights=b=>range(b.arrival,addDays(b.departure,-1));

  test('[HIST-C] yield: historic seasons exist and weekly publishes equal the rates the stays were sold at',()=>{
    const y=yldSlice(s);assert.ok(y.seasons.filter(x=>x.id.startsWith('YLDS-H')).length>=5);assert.ok(y.seasons.some(x=>x.id==='YLDS-1'));
    assert.ok(y.publishes.filter(p=>p.id.startsWith('YLDP-H')).length>=28);
    const rate=new Map();for(const p of y.publishes)for(const r of p.rows)rate.set(r.type+'|'+r.date,r.after);
    let checked=0;
    for(const b of departed){
      if(b.source==='Corporate'||/discount|Group block/.test(b.notes))continue;
      const k=typeOf(b.room)+'|'+b.arrival;if(!rate.has(k))continue;
      // a moved stay keeps the rate of its original room
      const orig=b.segments?typeOf(b.segments[0].room):typeOf(b.room);
      assert.equal(b.rate,rate.get(orig+'|'+b.arrival),b.id);checked++;
    }
    assert.ok(checked>600,'checked '+checked);
    for(const p of y.publishes.filter(p=>p.id.startsWith('YLDP-H')))assert.ok(p.rows.every(r=>r.after>=100000&&r.date>=p.from&&r.date<=p.to));
  });
  test('[HIST-C] groups: four past blocks whose members are real stays that match the block',()=>{
    const g=grpSlice(s),blocks=g.blocks.filter(b=>b.id.startsWith('GB-H'));assert.equal(blocks.length,4);
    assert.ok(g.blocks.some(b=>b.id==='GB-1')&&g.blocks.some(b=>b.id==='GB-4'));
    for(const blk of blocks){
      assert.equal(blk.status,'Released');assert.ok(blk.cutoff<blk.arrival);
      assert.equal(grpPicked(s,blk),blk.members.length);assert.ok(blk.members.length>=6);
      for(const m of blk.members){const b=s.bookings.find(x=>x.id===m.booking);
        assert.ok(b&&b.status==='Departed'&&b.arrival===blk.arrival&&b.departure===blk.departure&&typeOf(b.room)===m.type&&/Group block/.test(b.notes),m.booking);
        if(blk.billTo==='company')assert.equal(b.company.name,g.companies.find(c=>c.id===blk.company).name);}
      for(const t of Object.keys(blk.allot))assert.equal(blk.allot[t],blk.members.filter(m=>m.type===t).length,blk.id+t);
      for(const t of Object.keys(blk.rates))assert.ok(blk.rates[t]>0);
    }
    const co3=g.companies.find(c=>c.id==='CO-3');assert.equal(co3.limitHistory.at(-1).to,co3.creditLimit);assert.ok(g.requests.some(r=>r.id==='GLR-H1'&&r.status==='Approved'));
  });
  test('[HIST-C] maintenance: corrective ~1 per 3 days, preventive per plan, every work order verified within sequence',()=>{
    const x=mntSlice(s),h=x.orders.filter(w=>w.id.startsWith('WO-H'));
    const corr=h.filter(w=>w.kind==='Corrective'),prev=h.filter(w=>w.kind==='Preventive');
    assert.ok(corr.length>=60&&corr.length<=110,'corrective '+corr.length);assert.ok(prev.length>=60,'preventive '+prev.length);
    for(const w of h){assert.equal(w.status,'Verified');assert.ok(w.createdAt<w.startedAt&&w.startedAt<w.doneAt&&w.doneAt<w.verifiedAt,w.id);assert.ok(w.history.length>=5);
      for(const p of w.parts)assert.ok(x.spares.some(q=>q.id===p.spareId));if(w.assetId)assert.ok(x.assets.some(a=>a.id===w.assetId));}
    const met=h.filter(w=>mntSla(w,s.clock)==='Met').length/h.length;assert.ok(met>0.85&&met<1,'SLA met '+met);
    for(const p of x.plans){const dues=prev.filter(w=>w.planId===p.id).map(w=>w.dueDate).sort();
      for(let i=1;i<dues.length;i++)assert.equal(nightsBetween(dues[i-1],dues[i]),p.everyDays,p.id);
      if(dues.length)assert.ok(dues.at(-1)<=p.lastDone);}
    assert.equal(new Set(x.orders.map(w=>w.id)).size,x.orders.length);
  });
  test('[HIST-C] maintenance downtime: the nights of every downtime work order have no stay in that room',()=>{
    const dt=mntSlice(s).orders.filter(w=>w.downtimeFrom&&w.id.startsWith('WO-H'));assert.ok(dt.length>=8,'downtime WOs '+dt.length);
    for(const w of dt){const a=w.downtimeFrom.slice(0,10),z=w.downtimeTo.slice(0,10);
      for(const b of s.bookings.filter(b=>live.includes(b.status)))
        for(const g of (b.segments??[{room:b.room,from:b.arrival,to:b.departure}]))
          assert.ok(!(g.room===w.room&&a<(g.to??b.departure)&&z>g.from),`${w.id}: room ${w.room} ${a}–${z} overlaps ${b.id}`);}
  });
  test('[HIST-C] inventory: consumption ≈ standard × occupied room-nights (shampoo flagged, others within 10 %)',()=>{
    let rowsChecked=0;
    for(const [a,z] of [['2026-04-01','2026-04-30'],['2026-06-01','2026-06-30'],['2026-08-01','2026-08-31'],['2026-09-24','2026-10-22']]){
      const c=invConsumption(s,a,z);assert.ok(c.rn>300);
      for(const r of c.rows){
        if(r.item.id==='AMN-SH'){assert.ok(r.variance>=12&&r.variance<=40&&r.flag,'shampoo '+r.variance);continue;}
        if((r.item.stdX100??0)<40)continue;
        assert.ok(Math.abs(r.variance)<=10,`${r.item.id} ${a}: ${r.variance}%`);rowsChecked++;}
    }
    assert.ok(rowsChecked>30);
    const x=invSlice(s);assert.equal(x.moves.filter(m=>m.date>='2026-10-23'&&(m.kind==='use'||m.kind==='room-change')).length,invSlice(base).moves.filter(m=>m.date>='2026-10-23'&&(m.kind==='use'||m.kind==='room-change')).length);
  });
  test('[HIST-C] inventory: laundry batches reconcile with moves and losses; counts and receipts reconcile',()=>{
    const x=invSlice(s),rate=id=>x.items.find(i=>i.id===id).laundryRate;
    const hb=x.batches.filter(b=>b.id.startsWith('LB-H'));assert.ok(hb.length>=50);
    for(const b of hb){
      assert.equal(nightsBetween(b.sent,b.returned),1);
      assert.equal(b.charge,b.lines.reduce((a,l)=>a+(l.received+l.damaged)*rate(l.item),0));
      for(const l of b.lines){const missing=l.sent-l.received-l.damaged;assert.ok(missing>=0&&l.damaged>=0);
        const lost=x.losses.filter(q=>q.ref===b.id&&q.item===l.item).reduce((a,q)=>a+q.qty,0);assert.equal(lost,missing+l.damaged,b.id+l.item);
        const out=x.moves.filter(m=>m.ref===b.id&&m.item===l.item&&m.kind==='laundry-out')[0],inn=x.moves.filter(m=>m.ref===b.id&&m.item===l.item&&m.kind==='laundry-in')[0];
        assert.ok(out&&out.qty===l.sent&&inn&&inn.qty===l.received);}
      assert.equal(b.status,b.lines.some(l=>l.sent!==l.received+l.damaged)?'Returned short':'Returned');}
    for(const c of x.counts.filter(c=>c.id.startsWith('CNT-H'))){assert.equal(c.status,'Posted');
      for(const l of c.lines.filter(l=>l.counted!==l.system)){const loss=x.losses.find(q=>q.ref===c.id&&q.item===l.item);assert.ok(loss&&loss.qty===l.system-l.counted);}}
    for(const g of x.grns.filter(g=>g.id.startsWith('GRN-H')))for(const l of g.lines)assert.equal(x.moves.filter(m=>m.ref===g.id&&m.item===l.item&&m.kind==='grn').reduce((a,m)=>a+m.qty,0),l.qty);
    for(const q of x.losses.filter(q=>q.id.startsWith('LOSS-H')))assert.equal(q.value,q.qty*x.items.find(i=>i.id===q.item).cost);
    assert.equal(x.counts.filter(c=>c.id.startsWith('CNT-H')).length,6);
  });
  test('[HIST-C] staff: Apr–Sep payroll runs are locked and equal their recalculation; advances recovered',()=>{
    const x=hrSlice(s);assert.deepEqual(x.runs.map(r=>r.month),['2026-04','2026-05','2026-06','2026-07','2026-08','2026-09']);
    for(const r of x.runs){assert.equal(r.status,'Locked');assert.ok(r.lockedBy&&r.lockedAt>r.preparedAt.slice(0,10));
      const t=hrRunTotals(r);assert.equal(t.net,r.lines.reduce((a,l)=>a+l.net,0));assert.equal(t.gross-t.deductions,t.net);
      for(const l of r.lines){const st=x.staff.find(p=>p.id===l.staffId);
        const again=hrComputeLine(st,l.counts,r.rates,l.advance?{installment:l.advance,balance:l.advance}:undefined);assert.deepEqual(again,l,r.month+' '+l.staffId);
        assert.ok(l.net>=0);}}
    assert.equal(x.runs.find(r=>r.month==='2026-08').id,'HR-PAY-1');
    assert.ok(x.runs.find(r=>r.month==='2026-05').lines.length>=12-1&&x.runs.find(r=>r.month==='2026-05').lines.length<=12);
    assert.equal(x.runs.find(r=>r.month==='2026-04').lines.length,x.staff.filter(p=>p.joined<='2026-04-30').length);
    for(const a of x.advances)assert.equal(a.recoveries.reduce((n,r)=>n+r.amount,0),a.recovered);
    const h1=x.advances.find(a=>a.id==='HR-ADV-H1');assert.equal(h1.recovered,h1.amount);
    // raw marks kept only for the open months; approved leave days carry no attendance
    for(const k of Object.keys(x.att))assert.ok(k.slice(0,7)>='2026-09',k);
    for(const l of x.leaves.filter(l=>l.status==='Approved'&&l.from.startsWith('2026-09')))for(const d of range(l.from,l.to))assert.equal(x.att[d+'|'+l.staffId],undefined);
    assert.ok(x.leaves.filter(l=>l.id.startsWith('HR-LV-H')).length>=8);
  });
  test('[HIST-C] guest services: ~0.3 requests per occupied night, SLAs mostly met, complaints and recoveries tie to folio lines',()=>{
    const g=gsvSlice(s),reqs=g.requests.filter(r=>r.id.startsWith('SR-H')),nightsTotal=departed.reduce((a,b)=>a+nightsBetween(b.arrival,b.departure),0);
    assert.ok(reqs.length>nightsTotal*0.24&&reqs.length<nightsTotal*0.36,`${reqs.length} vs ${nightsTotal}`);
    let met=0;
    for(const r of reqs){const b=s.bookings.find(x=>x.id===r.booking);assert.ok(b&&b.room===r.room&&r.createdAt.slice(0,10)>=b.arrival&&r.createdAt.slice(0,10)<b.departure,r.id);
      if(r.status==='Done'){assert.ok(r.createdAt<=r.startedAt&&r.startedAt<=r.doneAt);if(r.doneAt<=r.dueAt)met++;}}
    assert.ok(met/reqs.filter(r=>r.status==='Done').length>0.8);
    const cs=g.complaints.filter(c=>c.id.startsWith('CMP-H'));assert.ok(cs.length>=departed.length/40*0.9&&cs.length<=departed.length/40*1.6,'complaints '+cs.length);
    const rec=hist.flatMap(b=>b.lines.filter(l=>l.kind==='discount'&&/Service recovery/.test(l.label)).map(l=>({b,l})));assert.ok(rec.length>=3);
    for(const {b,l} of rec){const c=cs.find(c=>c.booking===b.id&&c.recovery?.lineId===l.id);assert.ok(c,b.id);assert.equal(c.recovery.amount,-l.amount);if(l.approvalId)assert.equal(c.recovery.approvalId,l.approvalId);}
    for(const c of cs){assert.equal(c.status,'Closed');assert.ok(c.followUp&&c.closedAt>c.createdAt);}
    for(const it of g.items.filter(i=>i.id.startsWith('LF-H'))){if(it.status==='Disposed')assert.ok(it.disposal.at.slice(0,10)>=addDays(it.foundAt.slice(0,10),90));
      if(it.status==='Claimed'||it.status==='Handed over')assert.ok(it.claim);if(it.status==='Handed over')assert.ok(it.handover);}
  });
  test('[HIST-C] checklists & handover: 30 days of runs for every template; older days summarised',()=>{
    const h=hovSlice(s),runs=h.runs.filter(r=>/^HOVR-/.test(r.id)&&r.date<'2026-10-23'),from=addDays(TO,-30);
    assert.equal(runs.length,30*h.templates.length-0);assert.ok(runs.every(r=>r.date>=from&&r.date<='2026-10-22'));
    assert.equal(new Set(h.runs.map(r=>r.id)).size,h.runs.length);assert.equal(new Set(h.notes.map(n=>n.id)).size,h.notes.length);
    for(const r of runs){assert.equal(r.status,'Submitted');assert.ok(r.items.some(i=>i.done));
      if(!r.late)assert.ok(r.items.filter(i=>i.mandatory).every(i=>i.done),r.id);}
    assert.ok(runs.filter(r=>r.late).length<=10);
    const fd=h.notes.filter(n=>n.desk==='Front desk'&&n.date<'2026-10-23');assert.ok(fd.length>=50&&fd.every(n=>n.cash?.amount===500000));
    assert.equal(s.historyX.days.filter(d=>d.checklists===h.templates.length).length,206);
  });
  test('[HIST-C] POS: bills reconcile with receipts and folio lines; numbering and KOTs are consistent',()=>{
    const p=posSlice(s),hist2=p.orders.filter(o=>o.id.startsWith('POS-H'));assert.ok(hist2.length>800,'orders '+hist2.length);
    const bills=new Set(),taxOf=net=>Math.round(net*1000/10000);
    for(const o of hist2){
      const st=o.settlement,net=o.lines.filter(l=>!l.voided||o.status==='Void').reduce((a,l)=>a+l.price*l.qty,0);
      assert.ok(st&&/^POS\/\d{4}\/\d{4}$/.test(st.billNo)&&!bills.has(st.billNo));bills.add(st.billNo);
      if(o.status==='Settled'){assert.equal(st.net,o.lines.filter(l=>!l.voided).reduce((a,l)=>a+l.price*l.qty,0));}
      else assert.equal(st.net,net);
      assert.equal(st.tax,taxOf(st.net));assert.equal(st.total,st.net+st.tax);
      if(st.mode==='folio'){const b=s.bookings.find(x=>x.id===st.booking),l=b.lines.find(x=>x.id===st.folioLineId);
        assert.ok(l&&l.amount===st.net&&l.taxBps===st.taxBps&&l.kind==='service'&&l.label.includes(st.billNo),o.id);const seg=(b.segments??[{room:b.room,from:b.arrival,to:b.departure}]).find(g=>g.from<=st.date&&st.date<(g.to??b.departure));assert.ok(st.date>=b.arrival&&st.date<b.departure&&seg&&seg.room===o.room,o.id);}
      else assert.ok(p.receipts.some(r=>r.id===st.receiptId&&r.amount===st.total&&r.method===st.method&&r.date===st.date));}
    const nums=[...hist2].sort((a,b)=>a.openedAt<b.openedAt?-1:1).map(o=>Number(o.settlement.billNo.slice(-4)));
    for(let i=1;i<nums.length;i++)assert.equal(nums[i],nums[i-1]+1);
    assert.equal(p.billSeq,Math.max(...nums,2));
    for(const k of Object.keys(p.kotSeq)){const [out,d]=k.split(':');const mx=Math.max(...p.orders.filter(o=>o.outlet===out&&o.date===d).flatMap(o=>o.kots.map(q=>q.no)));assert.equal(p.kotSeq[k],mx,k);}
    // outlet receipts net per (date, method) equal the settled outlet checks (void receipts offset)
    const by=new Map();for(const r of p.receipts)by.set(r.date+'|'+r.method,(by.get(r.date+'|'+r.method)??0)+r.amount);
    const exp=new Map();for(const o of p.orders)if(o.settlement?.mode==='outlet'&&o.status==='Settled')exp.set(o.settlement.date+'|'+o.settlement.method,(exp.get(o.settlement.date+'|'+o.settlement.method)??0)+o.settlement.total);
    for(const [k,v] of exp)assert.equal(by.get(k)??0,v,k);
    for(const d of ['2026-05-15','2026-09-19','2026-10-20']){const sum=posDaySummary(s,d);assert.equal(Object.values(sum.byMode).reduce((a,v)=>a+v,0),sum.gross,d);assert.ok(sum.checks>=3);}
  });
  test('[HIST-C] POS: voids carry approvals; room-service folio lines are in the stay and the closing records',()=>{
    const p=posSlice(s),voids=p.orders.filter(o=>o.id.startsWith('POS-H')&&o.status==='Void');assert.ok(voids.length>=4);
    for(const o of voids){const a=s.approvals.find(a=>a.kind==='pos-void'&&a.target===o.id);assert.ok(a&&a.status==='Executed'&&a.approver==='manager'&&a.amount===o.settlement.total&&a.amount>50000);
      assert.ok(o.lines.every(l=>l.voided&&l.voidApproval===a.id));
      const vr=p.receipts.find(r=>r.order===o.id&&r.kind==='void');assert.ok(vr&&vr.amount===-o.settlement.total);}
    const roomLines=hist.flatMap(b=>b.lines.filter(l=>l.id.startsWith('posline:')));assert.ok(roomLines.length>100);
    const d='2026-09-20',expect=hist.flatMap(b=>b.lines.filter(l=>l.date===d&&l.kind==='service')).reduce((a,l)=>a+l.amount,0);
    assert.equal(s.closings[d].revenue.service,expect);
  });
  test('[HIST-C] channels: OTA bookings arrive as applied messages; commission report equals the channel rates',()=>{
    const c=chnSlice(s),ota=hist.filter(b=>b.otaChannel&&['Departed','Cancelled','No show'].includes(b.status));assert.ok(ota.length>250);
    for(const b of ota){const ms=c.messages.filter(m=>m.booking===b.id);
      assert.equal(ms.filter(m=>m.kind==='New'&&m.status==='Applied').length,1,b.id);assert.equal(ms.filter(m=>m.kind==='Cancel').length,b.status==='Departed'?0:1,b.id);
      const n=ms.find(m=>m.kind==='New');assert.ok(n.receivedAt.slice(0,10)<=b.arrival&&n.room===b.room&&n.arrival===b.arrival);}
    const rep=chnCommissionReport(s);
    const want=hist.filter(b=>b.status==='Departed'&&b.otaChannel).reduce((a,b)=>a+Math.round(b.rate*nightsBetween(b.arrival,b.departure)*c.channels.find(x=>x.name===b.otaChannel).commissionBps/10000),0);
    const baseOta=chnCommissionReport(base).total.commission;
    assert.equal(rep.total.commission,want+baseOta);assert.equal(rep.byChannel.reduce((a,x)=>a+x.commission,0),rep.total.commission);
    const web=hist.filter(b=>b.source==='Website'&&b.status!=='No show'&&b.status!=='Confirmed');
    for(const b of web){const w=c.webRequests.find(w=>w.booking===b.id);assert.ok(w,b.id);assert.equal(w.status,b.status==='Departed'?'Confirmed':'Declined');assert.equal(w.total,w.net+w.tax);}
    assert.ok(c.pushes.filter(p=>p.id.startsWith('CHP-H')).length>=80);
  });
  test('[HIST-C] messages & documents: 60 days of outbox, no false pending triggers, documents point at real records',()=>{
    const m=msgSlice(s),h=m.outbox.filter(x=>x.id.startsWith('MSG-H'));assert.ok(h.length>500);
    assert.ok(h.every(x=>x.queuedAt.slice(0,10)>=addDays(TO,-59)&&x.queuedAt.slice(0,10)<=TO&&s.bookings.some(b=>b.id===x.booking)));
    assert.equal(new Set(m.outbox.map(x=>x.id)).size,m.outbox.length);
    const pend=msgPendingTriggers(s);assert.ok(pend.every(t=>!t.booking.id.startsWith('HCC-H')),'only real triggers pending');
    assert.deepEqual(pend.map(t=>t.key).sort(),msgPendingTriggers(base).map(t=>t.key).sort());
    const docs=m.docs.filter(d=>d.id.startsWith('DOC-H'));assert.ok(docs.filter(d=>d.status==='Applied').length>=10);
    for(const d of docs.filter(d=>d.status==='Applied')){assert.ok(s.expenses.some(e=>e.id===d.appliedTo)||s.bookings.some(b=>b.id===d.appliedTo),d.id);
      for(const f of d.fields)assert.ok(!/\d{8,}/.test(f.value.replace(/[\s-]/g,''))||f.key==='amount'||f.key==='billDate',d.id+f.key);}
    for(const d of docs.filter(d=>d.kind==='vendor-bill'&&d.status==='Applied')){const e=s.expenses.find(e=>e.id===d.appliedTo);assert.equal(Math.round(parseFloat(d.fields.find(f=>f.key==='amount').value)*100),e.amount);}
  });
  test('[HIST-C] accounting: every day of April–September is locked, balanced and free of drift; October is closed but open for locking',()=>{
    const a=accSlice(s),lockDates=Object.keys(a.locks).sort(),want=range(FROM,'2026-09-30');
    assert.deepEqual(lockDates,want);assert.equal(a.lockHistory.length,want.length);
    for(const d of want){const l=a.locks[d];assert.equal(l.debit,l.credit,d);assert.equal(l.count,l.vouchers.length);assert.equal(l.lockedBy,'accountant');assert.ok(l.lockedAt.slice(0,10)>d);
      assert.equal(accDayStatus(s,d),'Locked');}
    for(const d of ['2026-04-14','2026-07-03','2026-09-30']){const live=accLiveVouchers(s,d),t=accTotals(live);assert.equal(t.debit,a.locks[d].debit);assert.deepEqual(accExceptions(s,d).filter(e=>e.severity!=='info'),[],d);}
    assert.equal(accDayStatus(s,'2026-10-05'),'Closed');assert.equal(accLockBlocker(s,'2026-10-05'),'');assert.equal(accLockBlocker(s,'2026-04-05'),'This date is already locked.');
    assert.equal(accDayStatus(s,'2026-10-24'),'Open');
  });
  test('[HIST-C] the whole scenario still joins up: today equals BASE, reports and a normal day work, size and speed budgets hold',()=>{
    assert.deepEqual(metrics(s),metrics(base));assert.equal(drawer(s),1075000);
    const sizes={};for(const k of Object.keys(s))sizes[k]=JSON.stringify(s[k]).length;
    const top=Object.entries(sizes).sort((a,b)=>b[1]-a[1]).slice(0,8).map(([k,v])=>`${k} ${(v/1e6).toFixed(2)}MB`).join(', ');
    const runs=[];for(let i=0;i<3;i++){const t=performance.now();loadScenario('FY26');runs.push(performance.now()-t);}runs.sort((a,b)=>a-b);
    const t0=performance.now();reportPack(s,FROM,TO);const pack=performance.now()-t0;
    let x=loadScenario('FY26');const t1=performance.now();x=execute(x,'owner',{type:'NIGHT_POST'});const cmd=performance.now()-t1;
    const bytes=JSON.stringify(s).length;
    console.log(`  [HIST-C] generate ${runs[1].toFixed(0)} ms, JSON ${(bytes/1e6).toFixed(2)} MB (${top}), FY pack ${pack.toFixed(0)} ms, one command ${cmd.toFixed(0)} ms`);
    assert.ok(runs[1]<=1500&&bytes<=12e6&&pack<=1000&&cmd<=500);
    assert.equal(scenarios[0].id,'FY26');
  });
}

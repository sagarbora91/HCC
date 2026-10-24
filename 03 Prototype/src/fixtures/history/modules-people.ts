// FY history Phase C (part 2): module 17 staff & payroll, 18 guest services, 19 checklists & handover.
import type {State} from '../../domain/types';
import {addDays,nights} from '../../domain/money';
import {hrKey,hrShiftFor,hrMonthDates,hrComputeRun,hrApplyRecoveries,hrLeaveDays,hrMonthEnd,type HRStaff,type HRRun,type HRLeave} from '../../domain/streams/staff';
import {gsvSlaMinutes,gsvDefaultDept,gsvAssignable,GSV_REQUEST_TYPES,type GSVRequest,type GSVComplaint,type GSVItem,type GSVComplaintCategory,type GSVSeverity} from '../../domain/streams/services';
import {hovNewRun,hovAt,type HOVNote,type HOVRun,type HOVTemplate} from '../../domain/streams/handover';
import type {Rng} from './prng';
import {pad,FIRST_OF,type HistoryContext,type HistoryWindow} from './util';

export function addPeopleHistory(s:State,rng:Rng,o:HistoryWindow,_ctx:HistoryContext):void{
  staffHistory(s,rng,o);
  servicesHistory(s,rng,o);
  handoverHistory(s,rng,o);
}

// ───────────── Module 17 · Staff & payroll ─────────────
const START:Record<string,string>={M:'07:00',E:'15:00',N:'23:00',G:'09:00'};
function staffHistory(s:State,rng:Rng,o:HistoryWindow){
  const x=s.staffX;if(!x)return;
  const people=x.staff;
  // Approved leave in Apr–Sep (entered before attendance so those days are not marked present).
  const leaves:HRLeave[]=[];let ln=0;
  const leaveMonths=['2026-04','2026-05','2026-06','2026-07','2026-08'];
  for(const ym of leaveMonths)for(let k=0;k<3;k++){
    const st=rng.pick(people.filter(p=>p.joined<=ym+'-01'));
    const from=ym+'-'+pad(rng.int(3,24),2),len=rng.int(1,2),to=addDays(from,len-1);
    if(leaves.some(l=>l.staffId===st.id&&l.from<=to&&l.to>=from))continue;
    const type=rng.weighted([['CL',50],['SL',35],['EL',15]] as const);
    leaves.push({id:'HR-LV-H'+pad(++ln,2),staffId:st.id,type,from,to,days:hrLeaveDays(s,st,from,to),reason:type==='SL'?'Unwell (fictional)':type==='CL'?'Personal work (fictional)':'Family visit (fictional)',
      status:'Approved',requestedBy:st.userId??'manager',requestedAt:addDays(from,-3)+'T10:00',decidedBy:'manager',decidedAt:addDays(from,-2)+'T12:00'});
  }
  x.leaves=[...leaves.filter(l=>l.days>0),...x.leaves];
  // An earlier salary advance, fully recovered over two runs (before the August advance of the base fixture).
  x.advances=[{id:'HR-ADV-H1',staffId:people[3].id,amount:200000,installment:100000,recovered:0,date:'2026-04-12',reason:'Medical advance (fictional)',by:'owner',recoveries:[]},...x.advances];

  const genAtt=(dates:string[])=>people.forEach(st=>dates.forEach(d=>{
    if(hrShiftFor(s,st,d)==='OFF'||d<st.joined)return;
    if(x.leaves.some(l=>l.staffId===st.id&&l.status==='Approved'&&l.from<=d&&l.to>=d))return;
    const r=rng.next();
    const status=r<0.03?'A':r<0.06?'H':r<0.12?'L':'P';
    const ot=status==='P'&&rng.chance(0.1)?(rng.chance(0.5)?60:120):0;
    x.att[hrKey(d,st.id)]={status,at:d+'T'+(status==='L'?START[st.defaultShift].replace(':00',':25'):START[st.defaultShift]),otMin:ot,by:'manager'};
  }));
  const runs:HRRun[]=[];let rn=0;
  for(const ym of ['2026-04','2026-05','2026-06','2026-07','2026-09']){
    if(ym!=='2026-09')genAtt(hrMonthDates(ym));
    const c=hrComputeRun(s,ym),next=addDays(hrMonthEnd(ym),1);
    const run:HRRun={id:'HR-PAY-H'+(++rn),month:ym,status:'Locked',rates:{...x.rates},lines:c.lines,warnings:c.warnings,preparedBy:'accountant',preparedAt:addDays(next,1)+'T11:10',lockedBy:'owner',lockedAt:addDays(next,1)+'T17:40'};
    runs.push(run);hrApplyRecoveries(x,run);x.runs=[...x.runs,run].sort((a,b)=>a.month<b.month?-1:1);
    if(ym!=='2026-09')for(const k of Object.keys(x.att))if(k.startsWith(ym))delete x.att[k]; // raw marks dropped once locked (as for August)
  }
}

// ───────────── Module 18 · Guest services ─────────────
const REQ_WEIGHTS:readonly (readonly [typeof GSV_REQUEST_TYPES[number],number])[]=[['Extra towels',22],['Drinking water',20],['Iron & board',10],['Wake-up call',12],['Taxi',9],['Toiletries & amenities',12],['Minor repair',9],['Other',6]];
const CMP_CATS:readonly (readonly [GSVComplaintCategory,number])[]=[['Room cleanliness',25],['Maintenance',20],['Noise',15],['Staff behaviour',8],['Food & beverage',12],['Billing',8],['Amenities',10],['Other',2]];
function servicesHistory(s:State,rng:Rng,o:HistoryWindow){
  const x=s.servicesX;if(!x)return;
  const stays=s.bookings.filter(b=>b.id.startsWith('HCC-H')&&b.status==='Departed');
  const requests:GSVRequest[]=[];let rn=0;
  const staffFor=(dept:ReturnType<typeof gsvDefaultDept>)=>gsvAssignable(s,dept);
  for(const b of stays){
    const n=nights(b.arrival,b.departure);
    for(let night=0;night<n;night++){
      if(!rng.chance(0.3))continue;
      const date=addDays(b.arrival,night),type=rng.weighted(REQ_WEIGHTS),dept=gsvDefaultDept(type);
      const pri=rng.chance(0.08)?'Urgent' as const:'Normal' as const,sla=gsvSlaMinutes(s,type,pri);
      const hh=rng.int(7*60,22*60),at=(m:number)=>date+'T'+pad(Math.floor(m/60)%24,2)+':'+pad(m%60,2);
      const cancelled=rng.chance(0.02),late=rng.chance(0.12);
      const startM=Math.min(hh+rng.int(1,Math.max(2,Math.floor(sla/3))),23*60+58),doneM=Math.min(Math.max(hh+Math.round(sla*(late?1.2+rng.next()*0.6:0.3+rng.next()*0.6)),startM+1),23*60+59);
      const created=at(hh),due=at(Math.min(hh+sla,23*60+59)),started=at(startM);
      const who=rng.pick(staffFor(dept)),by=dept==='Front desk'?'reception':'reception';
      const r:GSVRequest={id:'SR-H'+pad(++rn,5),booking:b.id,room:b.room,guest:b.guest,type,detail:'',dept,assignee:who.id,priority:pri,status:cancelled?'Cancelled':'Done',createdAt:created,dueAt:due,by,
        log:[{at:created,by,what:'Logged · assigned to '+(FIRST_OF[who.id]??who.id)}]};
      if(cancelled){r.cancelReason='Guest no longer needed it';r.log.push({at:at(hh+10),by,what:'Cancelled'});}
      else{r.startedAt=started;r.doneAt=at(doneM);r.note='Done.';r.log.push({at:r.doneAt,by:who.id,what:'Completed'});}
      requests.push(r);
    }
  }
  requests.sort((a,b)=>a.createdAt<b.createdAt?-1:a.createdAt>b.createdAt?1:a.id<b.id?-1:1);
  requests.forEach((r,i)=>{r.id='SR-H'+pad(i+1,5);});
  // Complaints: every "Service recovery" discount has one with its recovery; others fill up to about one per 40 stays.
  const complaints:GSVComplaint[]=[];let cn=0;
  const owners=['manager','reception'];
  const mk=(b:typeof stays[number],date:string,category:GSVComplaintCategory,severity:GSVSeverity,rec?:GSVComplaint['recovery']):GSVComplaint=>{
    const at=date+'T'+pad(rng.int(8,21),2)+':'+pad(rng.int(0,59),2),owner=rng.pick(owners);
    const c:GSVComplaint={id:'CMP-H'+pad(++cn,3),booking:b.id,room:b.room,guest:b.guest,category,severity,description:`${category} concern raised by the guest during the stay (fictional).`,owner,status:'Closed',
      rootCause:'Process gap found and briefed to the team.',recoveryAction:rec?'Goodwill discount approved and apologised in person.':'Apology and corrective action explained to the guest.',followUpOn:addDays(date,1),
      followUp:{outcome:'Guest satisfied with the follow-up.',satisfied:rng.chance(0.85),at:addDays(date,1)+'T12:00',by:owner},createdAt:at,resolvedAt:date+'T22:30',closedAt:addDays(date,1)+'T12:00',by:'reception',
      log:[{at,by:'reception',what:'Logged · owner '+(FIRST_OF[owner]??owner)},{at:addDays(date,1)+'T12:00',by:owner,what:'Follow-up · closed'}]};
    if(rec)c.recovery=rec;return c;
  };
  for(const b of stays)for(const l of b.lines.filter(l=>l.kind==='discount'&&/Service recovery/.test(l.label))){
    complaints.push(mk(b,l.date,rng.weighted(CMP_CATS),rng.weighted([['Medium',60],['High',30],['Critical',10]] as const),
      {amount:-l.amount,reason:l.label.replace(/^(Approved )?[Dd]iscount · /,''),...(l.approvalId?{approvalId:l.approvalId}:{}),lineId:l.id,at:l.date+'T21:00',by:'manager'}));
  }
  while(complaints.length<Math.round(stays.length/40)){
    const b=rng.pick(stays),d=addDays(b.arrival,rng.int(0,Math.max(0,nights(b.arrival,b.departure)-1)));
    complaints.push(mk(b,d,rng.weighted(CMP_CATS),rng.weighted([['Low',55],['Medium',35],['High',10]] as const)));
  }
  complaints.sort((a,b)=>a.createdAt<b.createdAt?-1:1);complaints.forEach((c,i)=>{c.id='CMP-H'+pad(i+1,3);});
  // Lost & found: some claimed or handed over, older unclaimed items disposed after the retention period.
  const items:GSVItem[]=[];
  const things:[string,GSVItem['category'],boolean][]=[['Black phone charger','Electronics',true],['Blue umbrella','Other',false],['Reading glasses in a case','Personal care',false],['Gold-coloured bangle','Jewellery',true],['Cotton shawl','Clothing',false],
    ['Power bank','Electronics',true],['Passport-size photo folder','Documents',true],['Water bottle','Other',false],['Wallet with a few cards','Cash / wallet',true],['Kids’ toy car','Other',false]];
  for(let k=0;k<22;k++){
    const [what,category,valuable]=rng.pick(things),b=rng.pick(stays),found=addDays(b.departure,0)+'T'+pad(rng.int(11,15),2)+':'+pad(rng.int(0,59),2),storage=valuable?'Front office safe':'Housekeeping store';
    const fate=rng.weighted([['claimed',45],['handed',20],['disposed',20],['stored',15]] as const);
    const old=found.slice(0,10)<=addDays(o.to,-(valuable?181:91));
    const it:GSVItem={id:'LF-H'+pad(k+1,2),description:what,category,valuable,foundWhere:`Room ${b.room}`,room:b.room,foundAt:found,foundBy:rng.pick(['housekeeper','housekeeper2']),storage,booking:b.id,status:'Stored',
      log:[{at:found,by:'housekeeper',what:'Logged · stored in '+storage}]};
    if(fate==='disposed'&&old){it.status='Disposed';it.disposal={at:addDays(found.slice(0,10),valuable?185:95)+'T11:00',by:'manager',method:valuable?'Handed to police':'Donated',note:'Unclaimed after the retention period.'};it.log.push({at:it.disposal.at,by:'manager',what:'Disposed · '+it.disposal.method});}
    else if(fate==='claimed'||fate==='handed'||(fate==='disposed')){
      const at=addDays(found.slice(0,10),rng.int(1,12))+'T10:00';
      it.claim={name:b.guest,phone:b.phone??'',idType:'Aadhaar (fictional)',idNote:'Name matched · ends '+pad(rng.int(0,9999)),matchNote:'Described the item correctly.',at,by:'reception'};
      it.status='Claimed';it.log.push({at,by:'reception',what:'Claimed by '+b.guest+' · ID checked'});
      if(fate!=='claimed'||rng.chance(0.5)){it.status='Handed over';it.handover={at:addDays(at.slice(0,10),0)+'T10:10',by:'reception',to:b.guest,note:'Collected in person.'};it.log.push({at:it.handover.at,by:'reception',what:'Handed over to '+b.guest});}
    }
    items.push(it);
  }
  items.sort((a,b)=>a.foundAt<b.foundAt?-1:1);items.forEach((it,i)=>{it.id='LF-H'+pad(i+1,2);});
  x.requests=[...requests,...x.requests];x.complaints=[...complaints,...x.complaints];x.items=[...items,...x.items];
}

// ───────────── Module 19 · Checklists & shift handover (last 30 days in full) ─────────────
const ACTORS:Record<string,[string,string]>={'HOVT-FD-OPEN':['reception','reception'],'HOVT-FD-CLOSE':['reception','reception'],'HOVT-CASH-HO':['cashier','cashier'],'HOVT-HK-FLOOR':['housekeeper','housekeeper2'],'HOVT-NA-PRE':['manager','reception'],'HOVT-MNT-RND':['maint','maint']};
const ISSUES=['All quiet. Rooms handed over as per the board.','One guest asked for a late checkout; manager informed.','Room key card jammed once; replaced.','Corridor light flickering on floor 2; maintenance told.','Two walk-ins registered; one more expected tomorrow.','Water pressure low on floor 3 for an hour; fixed.'];
function handoverHistory(s:State,rng:Rng,o:HistoryWindow){
  const x=s.handoverX;if(!x)return;
  const templates:HOVTemplate[]=x.templates;
  const winFrom=addDays(o.to,-30);
  const runs:HOVRun[]=[],notes:HOVNote[]=[];
  for(let d=winFrom;d<=addDays(o.to,-1);d=addDays(d,1)){
    for(const t of templates){
      const [a,b]=ACTORS[t.id]??['manager','manager'];
      const r=hovNewRun(t,d,a,hovAt(d,t.shift,'07:00'));
      const miss=rng.chance(0.02);
      r.items.forEach(i=>{
        const skip=!i.mandatory&&rng.chance(0.3);
        if(skip||(miss&&i.mandatory&&rng.chance(0.4)))return;
        i.done=true;i.by=rng.chance(0.8)?a:b;i.at=hovAt(d,t.shift,i.due??'09:00');
        if(i.photo)i.photoName=`IMG_${d.split('-').join('')}_${pad(rng.int(700,2100))}.jpg`;
      });
      const late=miss;
      Object.assign(r,{status:'Submitted',submittedBy:a,submittedAt:hovAt(d,t.shift,t.items.slice(-1)[0]?.due??'12:00'),late});
      runs.push(r);
    }
    const note=(desk:HOVNote['desk'],shift:HOVNote['shift'],toShift:HOVNote['toShift'],author:string,key:string,ack:string):HOVNote=>{
      const id=`HOVN-${key}-${d}-${shift[0]}`,at=hovAt(d,shift,shift==='Morning'?'14:40':shift==='Evening'?'22:40':'06:45');
      const n:HOVNote={id,desk,date:d,shift,toShift,author,at:shift==='Night'?addDays(d,1)+'T06:45':at,issues:rng.pick(ISSUES),keys:'Keys returned to the board.',guests:'',carry:[],status:'Acknowledged',ackBy:ack,ackAt:addDays(at.slice(0,10),shift==='Night'?0:0)+'T'+(shift==='Morning'?'15:05':shift==='Evening'?'23:05':'07:05')};
      if(desk==='Front desk')n.cash={amount:500000,note:'Opening float in the drawer as counted at close.'};
      if(rng.chance(0.2))n.carry=[{id:id+'-c1',text:'Follow up the item from this shift',done:true,doneBy:ack,doneAt:n.ackAt}];
      return n;
    };
    notes.push(note('Housekeeping','Morning','Evening','supervisor','HOUSEKEEPING','housekeeper2'),note('Maintenance','Morning','Evening','maint','MAINTENANCE','supervisor'),
      note('Front desk','Evening','Night','reception','FRONT','manager'),note('Front desk','Night','Morning','manager','FRONT','reception'));
  }
  x.runs=[...runs,...x.runs];x.notes=[...notes,...x.notes];
  s.historyX?.days.forEach(day=>{day.checklists=templates.length;});
}

// F4-T3 qw-feedback · feedback at check-out, complaint on low stars, 7-day follow-ups, summary/overdue selectors. Names prefixed [FBK].
import {tillClear} from './_till.mjs';
export function register({test,M,assert}){
  const {seed,execute,fbkSummary,fbkOverdue,fbkSlice,fbkBillLine,fbkOverdueLine,gsvSlice,can}=M;
  const code=c=>e=>e.code===c;
  const stay=()=>{let s=seed();s=execute(s,'owner',{type:'BOOK',guest:'Feedback Test',room:'105',arrival:s.date,departure:'2026-10-26',people:2});
    const id=s.bookings.at(-1).id;s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});return [s,id];};
  test('[FBK] 4 stars is recorded with no complaint; a second entry for the stay is refused; absent slice reads empty',()=>{
    let [s,id]=stay();delete s.feedbackX;assert.equal(fbkSlice(s).entries.length,0);
    s=execute(s,'reception',{type:'FBK_RECORD',booking:id,stars:4,comment:'Lovely stay',key:'f1'});
    const e=fbkSlice(s).entries[0];assert.equal(e.stars,4);assert.equal(e.complaintId,undefined);
    assert.throws(()=>execute(s,'reception',{type:'FBK_RECORD',booking:id,stars:5,key:'f2'}),code('INVALID_TRANSITION'));
    const x=fbkSummary(s,s.clock.slice(0,10));assert.equal(x.count,1);assert.equal(x.avg,4);
    assert.deepEqual(execute(s,'reception',{type:'FBK_RECORD',booking:id,stars:4,comment:'Lovely stay',key:'f1'}),s);});
  test('[FBK] stars <= 2 open a complaint in module 18; a comment with a category does too; bad input and roles refused',()=>{
    let [s,id]=stay();const before=gsvSlice(s).complaints.length;
    s=execute(s,'manager',{type:'FBK_RECORD',booking:id,stars:1,comment:'Room was noisy',key:'f1'});
    const e=fbkSlice(s).entries[0];assert.ok(e.complaintId);const c=gsvSlice(s).complaints.at(-1);
    assert.equal(c.id,e.complaintId);assert.equal(gsvSlice(s).complaints.length,before+1);assert.equal(c.status,'Open');assert.equal(c.booking,id);
    let [t,id2]=stay();t=execute(t,'owner',{type:'FBK_RECORD',booking:id2,stars:4,comment:'AC was weak',category:'Maintenance',key:'f2'});assert.ok(fbkSlice(t).entries[0].complaintId);
    let [u,id3]=stay();assert.throws(()=>execute(u,'owner',{type:'FBK_RECORD',booking:id3,stars:6,key:'f3'}),code('VALIDATION'));
    assert.throws(()=>execute(u,'housekeeper',{type:'FBK_RECORD',booking:id3,stars:5,key:'f4'}),code('FORBIDDEN'));});
  test('[FBK] 7-day unresolved complaint: overdue count, one Owner follow-up (idempotent), done when resolved; digest lines',()=>{
    let [s,id]=stay();s=execute(s,'owner',{type:'FBK_RECORD',booking:id,stars:2,key:'f1'});
    const later='2026-11-05';const o=fbkOverdue(s,later);
    assert.ok(o.complaints>=1);assert.match(fbkOverdueLine(o),/unresolved for 7\+ days/);
    const mine=r=>r.ref===fbkSlice(s).entries[0].complaintId;assert.ok(o.rows.some(mine));
    s.date=later;s=execute(s,'owner',{type:'FBK_FOLLOWUP_SWEEP',key:'sw1'});
    const n=fbkSlice(s).followUps.length;assert.equal(n,o.total);assert.ok(fbkSlice(s).followUps.every(f=>f.status==='open'));
    s=execute(s,'owner',{type:'FBK_FOLLOWUP_SWEEP',key:'sw2'});assert.equal(fbkSlice(s).followUps.length,n,'no duplicate');
    const f=fbkSlice(s).followUps[0];s=execute(s,'owner',{type:'FBK_FOLLOWUP_DONE',id:f.id,key:'d1'});assert.equal(fbkSlice(s).followUps.find(x=>x.id===f.id).status,'done');});
  test('[FBK] bill line is hidden until the owner sets the address',()=>{
    let s=seed();assert.equal(fbkBillLine(s),'');
    s=execute(s,'owner',{type:'FBK_URL_SET',url:'hotel.example/feedback',key:'u1'});assert.equal(fbkBillLine(s),'Tell us how we did: hotel.example/feedback');
    assert.throws(()=>execute(s,'reception',{type:'FBK_URL_SET',url:'x',key:'u2'}),code('FORBIDDEN'));
    s=execute(s,'owner',{type:'FBK_URL_SET',url:'',key:'u3'});assert.equal(fbkBillLine(s),'');});
  test('[FBK] day close runs the follow-up sweep (no extra command): seeded items older than 7 days get one open follow-up each',()=>{
    let s=seed();for(const b of s.bookings.filter(b=>b.status==='Confirmed')){s=execute(s,'owner',{type:'REGISTER',id:b.id});s=execute(s,'owner',{type:'CHECKIN',id:b.id});}
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});s=execute(s,'owner',{type:'NIGHT_POST'});
    s=execute(s,'owner',{type:'SHIFT_CLOSE',counted:M.drawer(s)});s=tillClear(M,s);
    const want=fbkOverdue(s,s.date).total;assert.ok(want>0);
    s=execute(s,'owner',{type:'DAY_CLOSE'});assert.equal(fbkSlice(s).followUps.filter(f=>f.status==='open').length,want);});
}

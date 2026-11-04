// LF-finish (L6): list loaders for counter bills (GET /counter-bills → counterX), credit guests (GET /guests/credit → guest.creditAccount),
// rate changes (GET /stays/rate-changes → stayRateX), role gating (no money for a role without the permission) and GUEST_PHOTO_REMOVE → RemoveGuestPhoto.
import {loadSync,fakeServer,startRuntime,capabilities,reply} from './_sync-harness.mjs';

const guest=(id)=>({guestId:id,name:'Asha Rao',phone:'+91 98200 01111',email:'a@example.test',address:'x',nationality:'Indian',idType:'Aadhaar (demo)',idLast4:'1234',
  company:null,gstin:null,party:[],verified:true,masked:false,version:'GV'+id,idMasked:'XXXX-XXXX-1234',idOnFile:true,canRevealId:true,preferences:null,notes:null,origin:'manual'});
const bill=(id,over={})=>({billId:id,invoiceNo:'INV/2627/000'+id,businessDate:'2026-10-24',issuedUtc:'2026-10-24T05:30:00.000Z',issuedBy:3,partyName:'Walk-in (fictional)',partyPhone:null,partyGstin:null,partyAddress:null,
  guestId:null,companyId:null,discountPaise:0,discountReason:null,netPaise:20000,taxPaise:1000,cgstPaise:500,sgstPaise:500,totalPaise:21000,remark:null,settlementMode:'paid',method:'UPI',reference:'R1',
  udhaarId:null,status:'Issued',creditNoteNo:null,creditNoteReason:null,voidApprovalId:null,
  lines:[{seq:1,label:'Laundry',extraId:null,menuItemId:null,qty:2,unitPaise:10000,amountPaise:20000,gstCategory:'laundry',taxBps:500,sac:'999711'}],...over});
const change=(id)=>({changeId:id,stayId:501,reservationId:1201,roomNo:'101',businessDate:'2026-10-24',changedUtc:'2026-10-24T06:00:00.000Z',changedBy:2,approvalId:null,approvedBy:null,scope:'today-next',
  reason:'Regular guest',oldRatePaise:200000,newRatePaise:180000,oldPeople:2,newPeople:2,oldChildren:0,newChildren:0,effectiveFrom:'2026-10-24',repostedJson:'[{"reversal":"7","line":"8","night":"2026-10-24"}]'});

export async function register({test,assert}){
  const S=await loadSync();
  const mk=(caps,extra={})=>fakeServer({'GET /me/capabilities':()=>caps,'GET /guests':()=>({total:1,page:1,pageSize:500,items:[guest(9)]}),
    'GET /counter-bills':()=>[bill(2,{status:'Credited',creditNoteNo:'CRN/2627/0001',creditNoteReason:'Wrong item',voidApprovalId:40}),bill(1)],
    'GET /guests/credit':()=>[{guestId:9,name:'Asha Rao',phone:null,limitPaise:1000000,active:true,customerKey:'9820001111',note:'Regular',openPaise:25000,oldestOpenDate:'2026-10-20'}],
    'GET /stays/rate-changes':()=>[change(2),change(1)],...extra});
  const rt=await startRuntime(S,mk(capabilities()));
  const st=rt.get().state;
  const bills=st.counterX?.bills??[];
  const rcs=st.stayRateX?.changes??[];
  const g9=st.guests.find(g=>g.id==='9');
  // a role without the money permissions: the loaders are skipped and no GET is sent
  const lowSrv=mk(capabilities({userId:7,displayName:'Housekeeper',roles:['HKP'],grants:[['PERM-HK-001','A'],['PERM-GST-001','V']]}));
  const low=await startRuntime(S,lowSrv);
  const lowState=low.get().state;
  const lowGets=['/counter-bills','/guests/credit','/stays/rate-changes'].map(p=>lowSrv.gets(p).length);
  // GUEST_PHOTO_REMOVE
  const photoSrv=mk(capabilities(),{'GET /documents/id-captures':()=>[
      {captureId:30,guestId:9,docKind:'photo',partySeq:null,capturedUtc:'2026-10-24T05:00:00.000Z',imageAvailable:true,status:'Confirmed'},
      {captureId:31,guestId:9,docKind:'photo',partySeq:null,capturedUtc:'2026-10-24T05:10:00.000Z',imageAvailable:true,status:'Rejected'}],
    'POST /commands/RemoveGuestPhoto':()=>({captureId:30,status:'Rejected'})});
  const prt=await startRuntime(S,photoSrv);
  const rm=await prt.run({type:'GUEST_PHOTO_REMOVE',id:'9',seq:0,key:'gphoto-rm:9:0:t1'});
  const rmCalls=photoSrv.commands('RemoveGuestPhoto');
  const none=await prt.run({type:'GUEST_PHOTO_REMOVE',id:'9',seq:3,key:'gphoto-rm:9:3:t1'});

  test('[LF-finish sync] GET /counter-bills → counterX (oldest first, lines, discount-free, credit note fields, ids as strings)',()=>{
    assert.deepEqual(bills.map(b=>b.id),['1','2']);
    assert.equal(bills[0].no,'INV/2627/0001');assert.equal(bills[0].total,21000);assert.equal(bills[0].lines[0].unit,10000);assert.equal(bills[0].settlement.method,'UPI');
    assert.equal(bills[1].status,'Credited');assert.equal(bills[1].creditNoteNo,'CRN/2627/0001');assert.equal(bills[1].voidApprovalId,'40');
    assert.equal(bills[0].discount,undefined);
  });
  test('[LF-finish sync] GET /stays/rate-changes → stayRateX (oldest first, booking = reservation id, reposted parsed)',()=>{
    assert.deepEqual(rcs.map(c=>c.id),['1','2']);
    assert.equal(rcs[0].booking,'1201');assert.equal(rcs[0].room,'101');assert.equal(rcs[0].oldRate,200000);assert.equal(rcs[0].newRate,180000);
    assert.deepEqual(rcs[0].reposted,[{reversal:'7',line:'8',night:'2026-10-24'}]);
  });
  test('[LF-finish sync] GET /guests/credit → the guest creditAccount (limit, active, key, note)',()=>{
    assert.equal(g9.creditAccount.limit,1000000);assert.equal(g9.creditAccount.active,true);assert.equal(g9.creditAccount.customerKey,'9820001111');assert.equal(g9.creditAccount.note,'Regular');
  });
  test('[LF-finish sync] a role without PERM-CTR-001 / FOL-010 / STY-010 sends none of the three GETs and gets no money slices',()=>{
    assert.deepEqual(lowGets,[0,0,0]);
    assert.equal((lowState.counterX?.bills??[]).length,0);assert.equal((lowState.stayRateX?.changes??[]).length,0);
    assert.equal(lowState.guests.find(g=>g.id==='9')?.creditAccount,undefined);
  });
  test('[LF-finish sync] GUEST_PHOTO_REMOVE → RemoveGuestPhoto {captureId of the newest live photo, reason}; a missing photo is refused before sending',()=>{
    assert.equal(rm.ok,true);assert.equal(rmCalls.length,1);
    assert.deepEqual(rmCalls[0].body,{captureId:30,reason:'Removed from the guest profile'});
    assert.equal(none.ok,false);assert.equal(photoSrv.commands('RemoveGuestPhoto').length,1);
  });
  // AF-INT (CTL-02): the correction register row carries requester, approval, decider and the stored changes
  const corr=await startRuntime(S,mk(capabilities(),{'GET /invoices/corrections':()=>[
    {correctionId:3,oldInvoiceNo:'INV/2627/0001',creditNoteNo:'CRN/2627/0001',newInvoiceNo:'INV/2627/0002',status:'Issued',oldTotalPaise:210000,newTotalPaise:157500,deltaPaise:-52500,settlement:'refund-due',
      reason:'Wrong rate',requestedUtc:'2026-10-24T04:00:00Z',requestedBy:2,approvalId:9,decidedBy:1,decidedUtc:'2026-10-24T05:00:00Z',
      changes:[{seq:1,kind:'room-rate',night:'2026-10-23',oldRatePaise:200000,newRatePaise:150000,chargeId:null,label:null,amountPaise:null,gstCategory:null,serviceDate:null}]},
    {correctionId:2,oldInvoiceNo:'INV/2627/0003',creditNoteNo:null,newInvoiceNo:null,status:'Pending',oldTotalPaise:100000,newTotalPaise:90000,deltaPaise:-10000,settlement:null,
      reason:'Remove line',requestedUtc:'2026-10-24T03:00:00Z',requestedBy:2,approvalId:8,decidedBy:null,decidedUtc:null,changes:[]}]}));
  const cs=corr.get().state.billCorrectionX?.corrections??[];
  test('[AF-INT CTL-02] the correction register maps requester, approval, approver (Issued only), decision time and the stored changes',()=>{
    assert.equal(cs.length,2);
    const c3=cs.find(c=>c.id==='3'),c2=cs.find(c=>c.id==='2');
    assert.equal(c3.changes.length,1);assert.equal(c3.changes[0].kind,'room-rate');assert.equal(c3.changes[0].newRate,150000);assert.equal(c3.changes[0].night,'2026-10-23');
    assert.ok(c3.requestedBy&&c3.approvalId&&c3.approvedBy&&c3.decidedAt,'requester, approval, approver and decision time are set');
    assert.deepEqual(c2.changes,[]);assert.equal(c2.approvedBy,undefined);assert.equal(c2.decidedAt,undefined);assert.ok(c2.requestedBy);
  });
}

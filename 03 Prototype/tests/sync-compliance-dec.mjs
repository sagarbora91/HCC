// SCC decisions wave, compliance — production client (sync/accounting.ts): DEC-125 (GET /compliance/state carries the effective FY and the pick
// in force; commands without an FY send the effective FY) and DEC-133 (licence numbers / expiry dates both ways). Names prefixed [DEC-125] / [DEC-133].
import {loadSync,fakeServer,startRuntime,capabilities} from './_sync-harness.mjs';

export async function register({test,assert,M}){
  const S=await loadSync();const P=M.seed(),C=M.cmpSlice(P);
  const caps=()=>capabilities({grants:[['PERM-RPT-001','A'],['PERM-CMP-001','A'],['PERM-CMP-002','A']]});
  const state=over=>({asOf:P.date,profile:{...C.profile,updatedBy:1,updatedUtc:'2026-04-01T03:30:00Z',version:'AAAAAAAAP1='},fyStart:2026,fyChosenIn:null,records:[],custom:[],
    archived:[],archivedCats:[],evidence:[],reopened:[],settings:{...C.settings,version:'AAAAAAAAE1='},...over});
  let cur=state({});
  const srv=fakeServer({'GET /me/capabilities':caps,
    'GET /accounting/days':()=>[],'GET /accounting/trial-balance':()=>({rows:[]}),'GET /accounting/mapping':()=>[],'GET /accounting/mapping/history':()=>[],'GET /accounting/locks/history':()=>[],
    'GET /expenses':()=>({items:[],page:1,pageSize:100,total:0,ownOnly:false}),'GET /expenses/registers':()=>({businessDate:P.date,budgets:[],recurring:[],petty:[],udhaar:[],months:[]}),
    'GET /compliance/state':()=>cur,'GET /compliance/ca-packs':()=>[],
    'POST /commands/MarkComplianceNotApplicable':({body})=>({fyStart:body.fyStart,itemId:body.itemId,state:'na'}),
    'POST /commands/SaveComplianceProfile':()=>({version:'AAAAAAAAP2='})});
  const rt=await startRuntime(S,srv,{areas:S.AREAS.filter(a=>['core','accounting'].includes(a.id))});
  const fyNow=M.cmpFyOfDate(rt.get().state.date);
  const run=async cmd=>{srv.reset();const r=await rt.run(cmd);return {r,body:srv.commands(cmd.type==='CMP_NA'?'MarkComplianceNotApplicable':'SaveComplianceProfile').at(-1)?.body};};
  // A pick of FY 2028–29 in force (made in the business date's FY): the client keeps it and sends it.
  cur=state({fyStart:2028,fyChosenIn:fyNow});await rt.refresh();
  const inForce={x:rt.get().state.complianceX,fy:M.cmpSlice(rt.get().state).fyStart,na:await run({type:'CMP_NA',itemId:'oc2'})};
  // A stored pick without one in force (null): the client follows the business date, whatever fyStart says.
  cur=state({fyStart:2028,fyChosenIn:null});await rt.refresh();
  const lapsed={fy:M.cmpSlice(rt.get().state).fyStart,na:await run({type:'CMP_NA',itemId:'oc2'})};
  // DEC-133: licence fields from the server, and both ways on SaveComplianceProfile.
  cur=state({profile:{...C.profile,fssaiNo:'DEMO-FSSAI-0002',fssaiExpiry:'2026-11-15',lodgingNo:null,lodgingExpiry:null,updatedBy:1,updatedUtc:'2026-04-01T03:30:00Z',version:'AAAAAAAAP1='}});
  await rt.refresh();const licState=rt.get().state;
  const base={type:'CMP_PROFILE_SET',name:'HCC Demo Residency',pan:'',gstin:'',entity:'partnership',gstType:'regular',hasEmployees:true,qrmp:false,notes:''};
  const withLic=await run({...base,fssaiNo:' DEMO-FSSAI-0002 ',fssaiExpiry:'2026-11-15',lodgingNo:'',lodgingExpiry:''}),without=await run(base);

  test('[DEC-125] client: complianceX keeps the server’s effective FY and the pick in force; a command without an FY sends the effective FY (a lapsed pick → the business date’s FY)',()=>{
    assert.deepEqual([inForce.x.fyStart,inForce.x.fyChosenIn,inForce.fy],[2028,fyNow,2028]);
    assert.ok(inForce.na.r.ok,inForce.na.r.text);assert.deepEqual(inForce.na.body,{fyStart:2028,itemId:'oc2'});
    assert.equal(lapsed.fy,M.cmpBusinessFy(rt.get().state.date));assert.deepEqual(lapsed.na.body,{fyStart:lapsed.fy,itemId:'oc2'});
  });
  test('[DEC-133] client: licence numbers and expiry dates map from GET /compliance/state (the card shows) and go to SaveComplianceProfile (\'\' clears, left out = kept)',()=>{
    const p=licState.complianceX.profile;
    assert.deepEqual([p.fssaiNo,p.fssaiExpiry,'lodgingNo' in p,'lodgingExpiry' in p],['DEMO-FSSAI-0002','2026-11-15',false,false]);
    assert.ok(M.cmpMonthPage(licState,'November','all',2026).all.some(v=>v.card.id==='lic_fssai'&&v.due==='2026-11-15'));
    assert.ok(withLic.r.ok,withLic.r.text);
    assert.deepEqual(withLic.body,{name:'HCC Demo Residency',pan:'',gstin:'',entity:'partnership',gstType:'regular',hasEmployees:true,qrmp:false,notes:'',
      fssaiNo:'DEMO-FSSAI-0002',fssaiExpiry:'2026-11-15',lodgingNo:'',lodgingExpiry:'',expectedVersion:'AAAAAAAAP1='});
    assert.equal(['fssaiNo','fssaiExpiry','lodgingNo','lodgingExpiry'].some(k=>k in without.body),false);
  });
}

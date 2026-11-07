// F12 f12-int-cap: the shared s.measX loader (clients/sync/meas-captures.ts) and the MEAS capture command mappers (desk, hk, guest, people, forecast)
// against the server routes POST /commands/<Name> + GET /meas/captures (Hcc.Api MeasCaptureEndpoints).
import {loadSync,fakeServer,startRuntime} from './_sync-harness.mjs';

const EMPTY={turnaway:[],walked:[],allowanceReasons:[],bookingExtra:[],preference:[],taskTimes:[],offline:[],meters:[],readings:[],assetLife:[],discards:[],surveys:[],reviews:[],
  rootCauseList:[],rootCause:[],training:[],exit:[],pulse:[],events:[],otb:[],competitors:[],rateshop:[],restrictions:[]};
const CAPTURES={...EMPTY,
  turnaway:[{id:'5',at:'2026-10-24T04:30:00.000Z',by:'3',reason:'full',arrival:'2026-10-30',nights:2,roomType:'DLX',rooms:1,quotedRate:250000,channel:'Walk-in',leadDays:6}],
  preference:[{customer:'G-7',floor:'High',roomKind:null,meal:null,pillow:null,billToCompany:null,doNotDisturb:true,note:null,loyaltyTag:'Gold',
    blacklist:{reason:'Damaged room',approvedBy:'1',at:'2026-10-24T04:30:00.000Z'}}],
  offline:[{room:'105',reason:'OOO',expectedReturn:'2026-10-27',workOrder:null,note:null}],
  meters:[{id:'MTR-1',name:'Main electricity',kind:'electricity',unit:'kWh',active:true}],
  readings:[{id:'9',meter:'MTR-1',date:'2026-10-24',value:1234.5,by:'4',photoRef:null}],
  assetLife:[{asset:'12',expectedLifeMonths:96,conditionGrade:'B'}], // replacement cost masked by the server (left out)
  pulse:[{id:'1',quarter:'2026-27-Q3',dept:'HK',workload:4,respect:5,tools:3,payFairness:3,recommend:8}],
  events:[{id:'EVT-A',name:'Wedding season',from:'2026-11-20',to:'2026-12-10',kind:'wedding-season',impactPct:20,note:null}],
  rootCause:[{complaint:'44',code:'NOISE'}],
};
const NAMES=['LogTurnaway','LogWalkedGuest','SaveAllowanceReason','SetBookingExtra','SetCustomerPreference','MeasSurveyAdd','MeasReviewSave','MeasRootCauseListSave','MeasRootCauseSet',
  'MeasLoyaltySet','SaveMeasTraining','SetMeasExit','AddMeasPulse','MeasOfflineSet','MeasMeterSave','MeasReadingAdd','MeasAssetLifeSet','MeasDiscardAdd','MeasEventSave','MeasEventDelete',
  'MeasOtbSnapshot','MeasCompetitorSave','MeasRateShopAdd'];

export async function register({test,assert}){
  const S=await loadSync();
  const routes={'GET /meas/captures':()=>CAPTURES,...Object.fromEntries(NAMES.map(n=>[`POST /commands/${n}`,()=>({id:'1',message:'ok'})]))};
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv);
  const m=rt.get().state.measX;
  const before=srv.gets('/meas/captures').length;
  const off=await rt.run({type:'MEAS_OFFLINE_SET',room:'105',reason:'OOS',expectedReturn:'2026-10-28',note:'Leak',key:'k-off'});
  const rd=await rt.run({type:'MEAS_READING_ADD',meter:'MTR-1',value:'1240.25',date:'2026-10-25',key:'k-rd'});
  const ms=await rt.run({type:'MEAS_METER_SAVE',name:'Water main',kind:'water',key:'k-ms'});
  const al=await rt.run({type:'MEAS_ASSETLIFE_SET',asset:'12',expectedLifeMonths:'120',conditionGrade:'A',replacementCostPaise:4500000,key:'k-al'});
  const dc=await rt.run({type:'MEAS_DISCARD_ADD',item:'Bath towel',qty:'3',reason:'Torn',key:'k-dc'});
  const ev=await rt.run({type:'MEAS_EVENT_SAVE',name:'Exam week',from:'2026-11-02',to:'2026-11-06',kind:'exam',impactPct:'-10',key:'k-ev'});
  const ed=await rt.run({type:'MEAS_EVENT_DELETE',id:'EVT-A',key:'k-ed'});
  const ot=await rt.run({type:'MEAS_OTB_SNAPSHOT',backfillFrom:'2026-09-01',key:'k-ot'});
  const cs=await rt.run({type:'MEAS_COMPETITOR_SAVE',name:'Hotel Rival',active:true,key:'k-cs'});
  const rs=await rt.run({type:'MEAS_RATESHOP_ADD',stayDate:'2026-11-01',competitor:'CMP-1',roomType:'DLX',ratePaise:240000,withBreakfast:true,key:'k-rs'});
  const pl=await rt.run({type:'MEAS_PULSE_ADD',dept:'HK',workload:4,respect:5,tools:3,payFairness:3,recommend:8,key:'k-pl'});
  const after=srv.gets('/meas/captures').length;

  test('[F12-INT-CAP] Loader: GET /meas/captures fills s.measX (lists, keyed side tables, hotel clock, masked money left out)',()=>{
    assert.equal(m.turnaway.length,1);assert.equal(m.turnaway[0].at,'2026-10-24T10:00');assert.equal(m.turnaway[0].quotedRate,250000);assert.equal(m.turnaway[0].note,undefined);
    assert.deepEqual(m.preference['G-7'],{floor:'High',doNotDisturb:true,loyaltyTag:'Gold',blacklist:{reason:'Damaged room',approvedBy:'1',at:'2026-10-24T10:00'}});
    assert.deepEqual(m.offline['105'],{reason:'OOO',expectedReturn:'2026-10-27'});
    assert.deepEqual(m.readings[0],{id:'9',meter:'MTR-1',date:'2026-10-24',value:1234.5,by:'4'});
    assert.deepEqual(m.assetLife['12'],{expectedLifeMonths:96,conditionGrade:'B'});assert.ok(!('replacementCostPaise' in m.assetLife['12']));
    assert.deepEqual(Object.keys(m.pulse[0]).sort(),['dept','id','payFairness','quarter','recommend','respect','tools','workload'],'pulse rows carry no identity');
    assert.equal(m.events[0].id,'EVT-A');assert.deepEqual(m.rootCause,{'44':'NOISE'});
  });
  test('[F12-INT-CAP] HK mappers (no longer unmapped): MeasOfflineSet / MeasReadingAdd / MeasMeterSave / MeasAssetLifeSet / MeasDiscardAdd',()=>{
    for(const r of [off,rd,ms,al,dc])assert.equal(r.ok,true,JSON.stringify(r));
    assert.deepEqual(srv.commands('MeasOfflineSet')[0].body,{room:'105',reason:'OOS',expectedReturn:'2026-10-28',note:'Leak'});
    assert.deepEqual(srv.commands('MeasReadingAdd')[0].body,{meter:'MTR-1',value:1240.25,date:'2026-10-25'});
    assert.deepEqual(srv.commands('MeasMeterSave')[0].body,{name:'Water main',kind:'water'});
    assert.deepEqual(srv.commands('MeasAssetLifeSet')[0].body,{asset:'12',expectedLifeMonths:120,conditionGrade:'A',replacementCostPaise:4500000});
    assert.deepEqual(srv.commands('MeasDiscardAdd')[0].body,{item:'Bath towel',qty:3,reason:'Torn'});
    assert.equal(srv.commands('MeasReadingAdd')[0].key,'k-rd');
  });
  test('[F12-INT-CAP] Forecast mappers: MeasEventSave / MeasEventDelete / MeasOtbSnapshot / MeasCompetitorSave / MeasRateShopAdd',()=>{
    for(const r of [ev,ed,ot,cs,rs])assert.equal(r.ok,true,JSON.stringify(r));
    assert.deepEqual(srv.commands('MeasEventSave')[0].body,{name:'Exam week',from:'2026-11-02',to:'2026-11-06',kind:'exam',impactPct:-10});
    assert.deepEqual(srv.commands('MeasEventDelete')[0].body,{id:'EVT-A'});
    assert.deepEqual(srv.commands('MeasOtbSnapshot')[0].body,{backfillFrom:'2026-09-01'});
    assert.deepEqual(srv.commands('MeasCompetitorSave')[0].body,{name:'Hotel Rival',active:true});
    assert.deepEqual(srv.commands('MeasRateShopAdd')[0].body,{clientId:'k-rs',stayDate:'2026-11-01',competitor:'CMP-1',roomType:'DLX',ratePaise:240000,withBreakfast:true});
  });
  test('[F12-INT-CAP] Pulse sends the department and answers only (no user / staff id); every MEAS command reloads measX',()=>{
    assert.equal(pl.ok,true);
    assert.deepEqual(srv.commands('AddMeasPulse')[0].body,{departmentId:'HK',workload:4,respect:5,tools:3,payFairness:3,recommend:8});
    assert.ok(after>=before+11,`measX reloaded after each command (${before} → ${after})`);
    const meas=S.AREAS.flatMap(a=>a.commands).filter(c=>c.type.startsWith('MEAS_')&&!['MEAS_PACK_SET','MEAS_ITEM_SET'].includes(c.type));
    assert.equal(meas.length,23,meas.map(c=>c.type).join(','));
    for(const c of meas)assert.ok(c.reload.includes('measX'),c.type);
    const gaps=S.AREAS.flatMap(a=>Object.keys(a.unmapped??{})).filter(t=>/^MEAS_(OFFLINE|METER|READING|ASSETLIFE|DISCARD|EVENT|OTB|COMPETITOR|RATESHOP)/.test(t));
    assert.deepEqual(gaps,[],'no MEAS capture left unmapped');
    assert.equal(S.AREAS.filter(a=>a.loaders.some(l=>l.slice==='measX')).length,1,'one owner of measX');
  });
}

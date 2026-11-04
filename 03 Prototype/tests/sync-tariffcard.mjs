// AF-SYNC-OPS (OPS-02): TRC_* commands and the tariffCardX loader in the production sync layer (clients/sync/tariffcard.ts).
import {loadSync,fakeServer,startRuntime,capabilities} from './_sync-harness.mjs';

const line=(no,over={})=>({lineNo:no,kind:'room',roomType:'Deluxe',plan:'EP',occKind:null,mealPlan:null,extra:null,hall:null,daySet:'all',rateExcl:200000,enteredIncl:null,
  minGuests:null,maxGuests:null,ageFrom:null,ageTo:null,note:null,taxBps:500,displayIncl:210000,...over});
const version=(id,over={})=>({versionId:id,code:'TC-00'+id,label:'Tariff card '+id,effectiveFrom:'2026-11-05',status:'Draft',weekendDays:[5,6,0],lines:[line(1),line(2,{kind:'meal',roomType:null,plan:null,mealPlan:'CP',rateExcl:20000,displayIncl:21000})],
  createdBy:2,createdUtc:'2026-10-24T05:30:00.000Z',proposedBy:null,publishedBy:null,publishedUtc:null,supersededBy:null,notes:null,publishLog:[],...over});

export async function register({test,assert}){
  const S=await loadSync();
  const names=['SaveTariffCardDraft','ProposeTariffCard','PublishTariffCard','RejectTariffCard','DiscardTariffCard','SetTariffCardWeekend'];
  const srv=fakeServer({'GET /tariff-card':()=>({versions:[version(3,{status:'Published',publishedBy:1,publishedUtc:'2026-10-24T06:00:00.000Z',supersededBy:4,publishLog:[{master:'tariffs',ref:'Deluxe',before:190000,after:200000,label:'Deluxe'}]}),version(4)],weekendDaysDefault:[5,6,0]}),
    ...Object.fromEntries(names.map(n=>['POST /commands/'+n,()=>({})]))});
  const rt=await startRuntime(S,srv);
  const st=rt.get().state.tariffCardX;
  const edit=await rt.run({type:'TRC_DRAFT_SAVE',id:'TC-004',lines:[{id:'L001',kind:'room',roomType:'Deluxe',plan:'EP',daySet:'all',rateExcl:210000,taxBps:500,displayIncl:220500,enteredIncl:220500}],key:'t1'});
  const fresh=await rt.run({type:'TRC_DRAFT_SAVE',label:'New card',effectiveFrom:'2026-12-01',lines:[{id:'L001',kind:'occupancy',roomType:'Deluxe',occKind:'adult',daySet:'weekend',rateExcl:50000,taxBps:0,displayIncl:null}],key:'t2'});
  const prop=await rt.run({type:'TRC_PROPOSE',id:'TC-004',key:'t3'});
  const pub=await rt.run({type:'TRC_PUBLISH',id:'TC-004',key:'t4'});
  const rej=await rt.run({type:'TRC_REJECT',id:'TC-004',reason:'Prices too low',key:'t5'});
  const dis=await rt.run({type:'TRC_DISCARD',id:'TC-004',key:'t6'});
  const wk=await rt.run({type:'TRC_WEEKEND_SET',id:'TC-004',days:[5,6],key:'t7'});
  const imp=await rt.run({type:'TRC_IMPORT',files:{},effectiveFrom:'2026-11-05',key:'t8'});
  const body=n=>srv.commands(n).map(c=>c.body);
  const lowSrv=fakeServer({'GET /me/capabilities':()=>capabilities({userId:7,displayName:'Housekeeper',roles:['HKP'],grants:[['PERM-HK-001','A']]}),'GET /tariff-card':()=>({versions:[],weekendDaysDefault:[]})});
  await startRuntime(S,lowSrv);

  test('[AF-TRC] GET /tariff-card → tariffCardX (versions by code, lowercase status, lines with prototype ids, publish log, weekend default)',()=>{
    assert.deepEqual(st.versions.map(v=>v.id),['TC-003','TC-004']);
    const v=st.versions[0];assert.equal(v.status,'published');assert.equal(v.supersededBy,'TC-004');assert.equal(v.publishedBy,'1');assert.equal(v.effectiveFrom,'2026-11-05');
    assert.deepEqual(v.publishLog,[{master:'tariffs',ref:'Deluxe',before:190000,after:200000}]);
    assert.equal(v.lines[0].id,'L001');assert.equal(v.lines[1].mealPlan,'CP');assert.equal(v.lines[0].rateExcl,200000);assert.equal(v.lines[0].displayIncl,210000);
    assert.deepEqual(st.weekendDaysDefault,[5,6,0]);assert.equal(st.versions[1].status,'draft');
    assert.equal(lowSrv.gets('/tariff-card').length,0);
  });
  test('[AF-TRC] TRC_DRAFT_SAVE edit → SaveTariffCardDraft with the loaded version id, label and date; a new draft sends versionId null',()=>{
    assert.equal(edit.ok,true);
    assert.deepEqual(body('SaveTariffCardDraft')[0],{versionId:4,label:'Tariff card 4',effectiveFrom:'2026-11-05',weekendDays:[5,6,0],
      lines:[{lineNo:1,kind:'room',roomType:'Deluxe',plan:'EP',daySet:'all',rateExcl:210000,enteredIncl:220500,minGuests:null,maxGuests:null,ageFrom:null,ageTo:null,taxBps:500,displayIncl:220500}]});
    assert.equal(fresh.ok,true);
    assert.deepEqual(body('SaveTariffCardDraft')[1],{versionId:null,label:'New card',effectiveFrom:'2026-12-01',
      lines:[{lineNo:1,kind:'occupancy',occKind:'adult',roomType:'Deluxe',daySet:'weekend',rateExcl:50000,enteredIncl:null,minGuests:null,maxGuests:null,ageFrom:null,ageTo:null,taxBps:0,displayIncl:null}]});
  });
  test('[AF-TRC] TRC_PROPOSE / PUBLISH / DISCARD → {versionId}; TRC_REJECT → {versionId, reason}; TRC_WEEKEND_SET → {versionId, days}',()=>{
    assert.equal(prop.ok&&pub.ok&&rej.ok&&dis.ok&&wk.ok,true);
    assert.deepEqual(body('ProposeTariffCard'),[{versionId:4}]);assert.deepEqual(body('PublishTariffCard'),[{versionId:4}]);assert.deepEqual(body('DiscardTariffCard'),[{versionId:4}]);
    assert.deepEqual(body('RejectTariffCard'),[{versionId:4,reason:'Prices too low'}]);assert.deepEqual(body('SetTariffCardWeekend'),[{versionId:4,days:[5,6]}]);
  });
  test('[AF-TRC] TRC_IMPORT stays unmapped',()=>{
    assert.equal(imp.ok,false);assert.equal(imp.reason,'unmapped');
  });
}

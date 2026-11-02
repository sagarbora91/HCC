// F4-T1 · Tariff card page — pure UI state (features/tariffcard/logic.ts) + nav wiring. Names prefixed [TRCUI].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/trcui-entry.ts',"export * from '../../src/features/tariffcard/logic';\nexport {PAGE_CATEGORY,PAGE_BLURB,navRoleOk} from '../../src/app/navmap';\nexport {SUB_SCREENS as SUBSCREENS} from '../../src/app/subscreens';");
  await build({entryPoints:['tests/.generated/trcui-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/trcui.mjs',logLevel:'error',jsx:'automatic',loader:{'.tsx':'tsx'}});
  const X=await import('./.generated/trcui.mjs');
  const {seed,execute}=M;
  const mk=(uid='owner')=>{let s=seed();s=execute(s,uid,{type:'TRC_DRAFT_SAVE',label:'UI',effectiveFrom:'2026-11-01',lines:[{kind:'room',roomType:'Deluxe',enteredIncl:350000},{kind:'occupancy',roomType:'Deluxe',occKind:'child',rateExcl:null},{kind:'meal',mealPlan:'CP',rateExcl:20000}]});return s;};

  test('[TRCUI] nav: tariff-card in REVENUE & SALES with blurb; housekeeping / maintenance cannot open it',()=>{
    assert.equal(X.PAGE_CATEGORY['tariff-card'],'revenue');assert.ok(X.PAGE_BLURB['tariff-card']);
    const s=seed();assert.equal(X.navRoleOk(s,'housekeeper','tariff-card'),false);assert.equal(X.navRoleOk(s,'maint','tariff-card'),false);assert.equal(X.navRoleOk(s,'reception','tariff-card'),true);
  });
  test('[TRCUI] buttons by role: Owner publishes, Manager proposes, Reception views',()=>{
    const s=mk(),v=s.tariffCardX.versions[0];
    const o=X.trcUiState(s,'owner',v),m=X.trcUiState(s,'manager',v),r=X.trcUiState(s,'reception',v);
    assert.equal(o.canPublish,true);assert.equal(o.canPropose,false);assert.equal(o.canImport,true);assert.equal(o.canEdit,true);
    assert.equal(m.canPublish,false);assert.equal(m.canPropose,true);assert.equal(m.canEdit,true);assert.equal(m.canImport,false);
    assert.equal(r.canEdit,false);assert.equal(r.canNewDraft,false);assert.equal(r.view,true);assert.match(r.readOnlyNote,/View only/);
    assert.equal(X.trcUiState(s,'housekeeper',v).view,false);
  });
  test('[TRCUI] phone: read-only except the Owner',()=>{
    const s=mk(),v=s.tariffCardX.versions[0];const m=X.trcUiState(s,'manager',v,true),o=X.trcUiState(s,'owner',v,true);
    assert.equal(m.canEdit,false);assert.equal(m.canPropose,false);assert.equal(m.canNewDraft,false);assert.match(m.readOnlyNote,/Read only on the phone/);
    assert.equal(o.canPublish,true);assert.equal(o.canImport,false,'CSV import on desktop');
  });
  test('[TRCUI] editor rows show the rounding difference; tabs split rooms and meals',()=>{
    const s=mk(),v=s.tariffCardX.versions[0];
    const rooms=X.trcTabLines(v,'rooms'),meals=X.trcTabLines(v,'meals');assert.equal(rooms.length,2);assert.equal(meals.length,1);
    const r=X.trcEditorRows(s,rooms)[0];assert.equal(r.entered,350000);assert.equal(r.incl-350000,r.diff);assert.ok(Math.abs(r.diff)<=1);
    assert.equal(X.trcEditorRows(s,rooms)[1].incl,null);assert.match(X.trcEditorRows(s,rooms)[1].note,/Age/);
  });
  test('[TRCUI] edit one price inclusive → save → excl recomputed; other lines untouched',()=>{
    let s=mk();const v=s.tariffCardX.versions[0];const id=v.lines[0].id;
    assert.equal(X.trcParseRupees('2,000.00'),200000);assert.equal(X.trcParseRupees(''),null);assert.equal(X.trcParseRupees('abc'),undefined);
    s=execute(s,'owner',{type:'TRC_DRAFT_SAVE',id:v.id,lines:X.trcEditLine(v.lines,id,200000,true)});
    const w=s.tariffCardX.versions[0];assert.equal(w.lines[0].rateExcl,190476);assert.equal(w.lines[0].displayIncl,200000,'5 % default');assert.equal(w.lines[2].rateExcl,20000);
    s=execute(s,'owner',{type:'TRC_DRAFT_SAVE',id:v.id,lines:X.trcEditLine(w.lines,id,180000,false)});assert.equal(s.tariffCardX.versions[0].lines[0].enteredIncl,undefined);
    assert.equal(X.trcPreviewLine(s,w,{kind:'room',roomType:'Suite',enteredIncl:800000}).error.length>0,true);
  });
  test('[TRCUI] default version: open draft for Owner / Manager, latest published for Reception',()=>{
    let s=mk();const d=s.tariffCardX.versions[0].id;assert.equal(X.trcDefaultVersion(s,'manager').id,d);
    s=execute(s,'owner',{type:'TRC_DRAFT_SAVE',label:'P',effectiveFrom:'2026-11-02',lines:[{kind:'room',roomType:'Suite',rateExcl:470000}]});
    s=execute(s,'owner',{type:'TRC_PUBLISH',id:'TRC-0002'});assert.equal(X.trcDefaultVersion(s,'reception').id,'TRC-0002');assert.equal(X.trcDefaultVersion(s,'owner').id,d);
  });
  test('[TRCUI] sub-screens list the six tabs',()=>{assert.deepEqual(X.SUBSCREENS.filter(x=>x.page==='tariff-card').map(x=>x.id),X.TRC_TAB_IDS);});
}

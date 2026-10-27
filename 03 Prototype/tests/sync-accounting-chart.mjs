// ACC-1 lane acc1-chart · production mapping of the chart commands (sync/accounting-chart.ts → Hcc.Api AccountingChartEndpoints).
// Names prefixed [ACC1-CHART sync].
import {loadSync} from './_sync-harness.mjs';

export async function register({test,assert}){
  const S=await loadSync();
  const area=S.AREAS.find(a=>a.id==='accountingChart');
  const owner={permIds:['PERM-ACC-001','PERM-ACC-007']},acct={permIds:['PERM-ACC-001']};
  const mapper=t=>area.commands.find(c=>c.type===t);
  const sent=async(type,cmd)=>{const calls=[];await mapper(type).send({cmd:{type,...cmd},api:{command:async(name,body)=>{calls.push([name,body]);return {};}}});return calls;};

  // ACC-1 integration: the chart loader is registered (READS + tests/_role-reads.json); it patches accountingX.chart from the master + history.
  const ldr=area.loaders[0];
  const patched=ldr?await ldr.load({api:{get:async p=>p==='/accounting/chart/master'?{groups:[{id:'g-banquet',name:'Banquet income',parent:'sales',nature:'Cr',side:'PL',custom:true,legacyGroup:'Income',path:[]}],
    ledgers:[{code:'5130',name:'Guest amenities',groupId:'indirect-expenses',group:'Expense',nature:'Dr',system:false,control:false,gst:'none',tallyName:null,active:true,note:null,overridden:false,mappedBy:[]},
      {code:'1000',name:'Cash in hand',groupId:'cash',group:'Asset',nature:'Dr',system:true,control:true,gst:'none',tallyName:null,active:true,note:null,overridden:false,mappedBy:[]}],problems:[],canEdit:true}:[]},
    current:()=>({mapping:{}})}):undefined;
  test('[ACC1-CHART sync] the area maps ACC_LEDGER_UPSERT / ACC_LEDGER_RETIRE / ACC_GROUP_UPSERT, reloads accountingX and loads the chart (integration: loader registered)',()=>{
    assert.ok(area,'registered in AREAS');assert.deepEqual(area.commands.map(c=>c.type).sort(),['ACC_GROUP_UPSERT','ACC_LEDGER_RETIRE','ACC_LEDGER_UPSERT']);
    assert.ok(area.commands.every(c=>c.reload.length===1&&c.reload[0]==='accountingX'));
    assert.equal(area.loaders.length,1);assert.equal(ldr.kind,'patch');assert.deepEqual([...ldr.reads],['GET /accounting/chart/master','GET /accounting/chart/history']);
    assert.deepEqual(patched.mapping,{},'the patch keeps the rest of accountingX');assert.deepEqual(patched.chart.ledgers.map(l=>l.code),['5130'],'built-ins without an override are not stored');
    assert.deepEqual(patched.chart.groups.map(g=>g.id),['g-banquet']);
    assert.deepEqual(S.validateAreas(S.AREAS),[]);
  });
  test('[ACC1-CHART sync] prechecks: Owner only (PERM-ACC-007), a 4-digit code, a group name',()=>{
    assert.match(mapper('ACC_LEDGER_UPSERT').precheck({code:'5130'},{},acct),/Only the Owner/);
    assert.match(mapper('ACC_LEDGER_UPSERT').precheck({code:'51A'},{},owner),/4 digits/);
    assert.equal(mapper('ACC_LEDGER_UPSERT').precheck({code:'5130'},{},owner),undefined);
    assert.match(mapper('ACC_LEDGER_RETIRE').precheck({code:''},{},owner),/ledger to retire/);
    assert.match(mapper('ACC_GROUP_UPSERT').precheck({name:''},{},owner),/group name/);
    assert.match(mapper('ACC_GROUP_UPSERT').precheck({name:'Banquet income'},{},acct),/Only the Owner/);
  });
  // The runner's tests are synchronous: send first (await here), assert in the test.
  const bodies={add:await sent('ACC_LEDGER_UPSERT',{create:true,code:' 5130 ',name:'Guest amenities',group:'indirect-expenses',gst:'none'}),
    reactivate:await sent('ACC_LEDGER_UPSERT',{code:'4400',active:true}),retire:await sent('ACC_LEDGER_RETIRE',{code:'1540',reason:' No vehicles '}),
    group:await sent('ACC_GROUP_UPSERT',{name:'Banquet income',parent:'sales'})};
  test('[ACC1-CHART sync] bodies: UpsertLedger keeps unsent fields null, RetireLedger carries the reason, UpsertLedgerGroup the id / name / parent',()=>{
    assert.deepEqual(bodies.add,[['UpsertLedger',{create:true,code:'5130',name:'Guest amenities',groupId:'indirect-expenses',gst:'none',tallyName:null,note:null,active:null}]]);
    assert.deepEqual(bodies.reactivate,[['UpsertLedger',{create:false,code:'4400',name:null,groupId:null,gst:null,tallyName:null,note:null,active:true}]]);
    assert.deepEqual(bodies.retire,[['RetireLedger',{code:'1540',reason:'No vehicles'}]]);
    assert.deepEqual(bodies.group,[['UpsertLedgerGroup',{id:null,name:'Banquet income',parent:'sales'}]]);
  });
}

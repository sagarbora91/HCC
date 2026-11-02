// LF-docdb (LF-CONTRACT §1.10, §1.11, §1.9): the docObjectsX mirror of the documents database — put / get / release, the per-kind size caps and the server's
// refusal words, the v12 → v13 move of bill photos and evidence files, the expense and evidence handlers writing refs, and the demo System health
// "Databases" rows. Names prefixed [LF-DOCDB].
import {build} from 'esbuild';
import {mkdir} from 'node:fs/promises';
await mkdir('tests/.generated',{recursive:true});
await build({entryPoints:['src/clients/sync/syshealth.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/syshealth-sync.mjs',logLevel:'error'});
const SYS=await import('./.generated/syshealth-sync.mjs');

export function register({test,M,assert}){
  const {seed,execute,dobPut,dobGet,dobDelete,dobSize,dobCap,dobRefusal,dobRefCheck,dobMigrateV12,restoreState,sysHealthView,sysDbTone,sysDbPct,cmpSlice,
    DOC_MAX_BYTES,DOC_CAP_MESSAGE,SCHEMA}=M;
  const img=(n,type='image/jpeg')=>`data:${type};base64,`+Buffer.alloc(n,0xff).toString('base64');
  const put=(s,over={})=>dobPut(s,{ref:'DOB-T1',kind:'expense-bill',owner:'EXP-1',dataUrl:img(100),at:'2026-10-24T10:00',by:'owner',...over});

  test('[LF-DOCDB] caps: 150 KB for photos, 100 KB for the slip, 5 MB for an evidence PDF, 8 MB for a legacy image; one refusal text',()=>{
    assert.equal(DOC_MAX_BYTES,153600);assert.equal(DOC_CAP_MESSAGE,'Take the photo again, closer and in good light.');
    assert.deepEqual(['id-image','guest-photo','expense-bill','banking-slip','legacy-image'].map(k=>dobCap(k)),[153600,153600,153600,102399,8388608]);
    assert.equal(dobCap('compliance-evidence','application/pdf'),5242880);assert.equal(dobCap('compliance-evidence','image/png'),153600);
    assert.equal(dobRefusal('expense-bill',img(153600)),'');assert.equal(dobRefusal('expense-bill',img(153601)),DOC_CAP_MESSAGE);
    assert.equal(dobRefusal('banking-slip',img(102399)),'');assert.equal(dobRefusal('banking-slip',img(102400)),DOC_CAP_MESSAGE);
    assert.equal(dobRefusal('compliance-evidence',img(200000,'application/pdf')),'');assert.equal(dobRefusal('compliance-evidence',img(200000)),DOC_CAP_MESSAGE);
    assert.equal(dobRefusal('id-image','not a data url'),'Choose a file to attach.');assert.equal(dobRefusal('id-image','data:image/png;base64,'),'Choose a file to attach.');});

  test('[LF-DOCDB] put / get / delete are copy-on-write; a refused put changes nothing',()=>{
    const s=seed(),before=s.docObjectsX;
    const a=put(s);assert.equal(s.docObjectsX,before);assert.deepEqual([a['DOB-T1'].kind,a['DOB-T1'].bytes,a['DOB-T1'].contentType],['expense-bill',100,'image/jpeg']);
    const t={...s,docObjectsX:a};assert.equal(dobGet(t,'DOB-T1').owner,'EXP-1');assert.equal(dobGet(t,'nope'),undefined);assert.equal(dobGet(t,undefined),undefined);
    const gone=dobDelete(t,'DOB-T1');assert.equal(dobGet({docObjectsX:gone},'DOB-T1'),undefined);assert.ok(a['DOB-T1'],'the old slice is untouched');
    assert.throws(()=>put(s,{dataUrl:img(153601)}),e=>e.message===DOC_CAP_MESSAGE);assert.equal(s.docObjectsX,before);});

  test('[LF-DOCDB] size per kind (demo System health) and the reference check (dangling refs, orphans)',()=>{
    let s=seed();const base=dobSize(s);
    s={...s,docObjectsX:{...s.docObjectsX,...put(s),...dobPut(s,{ref:'DOB-T2',kind:'banking-slip',owner:'T',dataUrl:img(50),at:'x',by:'owner'})}};
    const z=dobSize(s);assert.equal(z.count,base.count+2);assert.equal(z.total,base.total+150);assert.deepEqual(z.byKind['banking-slip'],{count:1,bytes:50});
    // DOB-T1 / T2 belong to no record: orphans. A record pointing at nothing is dangling.
    assert.deepEqual(dobRefCheck(s),{dangling:0,orphans:dobRefCheck(seed()).orphans+2});
    const e=s.expenses[0];e.billPhotoRef='DOB-MISSING';assert.equal(dobRefCheck(s).dangling,1);});

  test('[LF-DOCDB] the v12 → v13 move: bill photos and evidence files become objects, the old fields are deleted, a second pass changes nothing',()=>{
    const s=JSON.parse(JSON.stringify(seed()));s.schema=12;
    const e=s.expenses[0];e.billPhoto=img(2000);
    const files=cmpSlice(s).files;assert.ok(files.length>=1,'the fixture has an evidence file');
    s.complianceFilesX={};for(const f of files)s.complianceFilesX[f.id]=img(300,'application/pdf');
    s.complianceX={...s.complianceX,files:files.map(f=>({...f}))};
    const r=restoreState(s);assert.equal(r.reset,false);const m=r.state;
    assert.equal(m.schema,SCHEMA);assert.equal(m.complianceFilesX,undefined);
    const me=m.expenses[0];assert.equal(me.billPhoto,undefined);assert.equal(dobGet(m,me.billPhotoRef).bytes,2000);
    for(const f of cmpSlice(m).files){assert.equal(dobGet(m,f.objectRef).kind,'compliance-evidence');assert.equal(f.objectRef,'DOB-'+f.id);}
    const again=JSON.stringify(m);assert.equal(dobMigrateV12(m),false);assert.equal(JSON.stringify(m),again);});

  test('[LF-DOCDB] an EXPENSE with a bill photo stores a ref; replacing the photo (EXP_EDIT) releases the old object; over 150 KB is refused with the server text',()=>{
    let s=execute(seed(),'reception',{type:'EXPENSE',description:'Photo run',category:'Supplies',amount:100,method:'UPI',billPhoto:img(3000)});
    const e=s.expenses.at(-1);assert.equal(e.billPhoto,undefined);assert.equal(dobGet(s,e.billPhotoRef).bytes,3000);assert.equal(dobGet(s,e.billPhotoRef).owner,e.id);
    const first=e.billPhotoRef;
    s=execute(s,'reception',{type:'EXP_EDIT',id:e.id,amount:100,category:'Supplies',method:'UPI',description:'Photo run',reason:'Better photo',billPhoto:img(4000)});
    const e2=s.expenses.find(x=>x.id===e.id);assert.notEqual(e2.billPhotoRef,first);assert.equal(dobGet(s,first),undefined);assert.equal(dobGet(s,e2.billPhotoRef).bytes,4000);
    assert.throws(()=>execute(seed(),'reception',{type:'EXPENSE',description:'Big',category:'Supplies',amount:100,method:'UPI',billPhoto:img(160000)}),e=>e.message===DOC_CAP_MESSAGE);
    assert.throws(()=>execute(seed(),'reception',{type:'EXPENSE',description:'Huge',category:'Supplies',amount:100,method:'UPI',billPhoto:img(4194305)}),/Photo too large \(max 4MB\)/);});

  test('[LF-DOCDB] evidence files: an image above 150 KB is refused, a PDF up to 5 MB is kept; deleting the file releases the object',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'accountant',{type:'CMP_EVIDENCE_ADD',fy:2026,itemId:'jn4',name:'scan.jpg',dataUrl:img(160000)}),e=>e.message===DOC_CAP_MESSAGE);
    s=execute(s,'accountant',{type:'CMP_EVIDENCE_ADD',fy:2026,itemId:'jn4',name:'challan.pdf',dataUrl:img(200000,'application/pdf')});
    const f=cmpSlice(s).files.at(-1);assert.equal(f.objectRef,'DOB-'+f.id);assert.equal(dobGet(s,f.objectRef).bytes,200000);
    s=execute(s,'accountant',{type:'CMP_EVIDENCE_DELETE',id:f.id});assert.equal(dobGet(s,'DOB-'+f.id),undefined);});

  test('[LF-DOCDB] demo System health shows two database rows: main and documents sized from the slice; thresholds amber 70 %, red 85 %',()=>{
    let s=seed();const v=sysHealthView(s);assert.equal(v.source,'demo');
    assert.deepEqual(v.databases.map(d=>[d.name,d.kind,d.schemaVersion,d.reachable]),[['Hcc','main','4674',true],['HccDocuments','documents','0002',true]]);
    const z=dobSize(s);const d=v.databases[1];assert.deepEqual([d.objectCount,d.contentBytes,d.danglingRefs],[z.count,z.total,0]);
    assert.ok(d.dataBytes>=z.total&&d.pct===sysDbPct(d.dataBytes)&&d.limitBytes===50*1024**3);
    assert.deepEqual([sysDbTone(0),sysDbTone(69),sysDbTone(70),sysDbTone(84),sysDbTone(85),sysDbTone(100)],['ok','ok','warn','warn','bad','bad']);
    assert.equal(sysHealthView({...s,sysHealthX:{source:'server',parked:[],databases:[{name:'HccDocuments',kind:'documents',schemaVersion:'0002',reachable:false,dataBytes:0,logBytes:0,limitBytes:1,pct:0}]}}).databases[0].reachable,false);});

  test('[LF-DOCDB] the server answer maps to the Databases rows (sizes, ref check, times as the IST clock)',()=>{
    const {databaseFromServer,healthFromServer}=SYS;
    const row=databaseFromServer({name:'HccDocuments',kind:'documents',schemaVersion:'0002',reachable:true,dataBytes:1000,logBytes:50,limitBytes:53687091200,pct:0,objectCount:3,contentBytes:900,danglingRefs:1,orphanObjects:0,lastBackupUtc:'2026-10-24T02:30:00Z',lastDrillUtc:null});
    assert.deepEqual([row.kind,row.objectCount,row.danglingRefs,row.orphanObjects,row.lastBackup,row.lastDrill],['documents',3,1,0,'2026-10-24T08:00',undefined]);
    const h=healthFromServer({checkedUtc:'2026-10-24T04:30:00Z',parked:[],lastBackup:null,lastSuccessfulBackupUtc:null,lastRestoreDrill:null,eventLog:'on',databases:[{name:'Hcc',kind:'main',schemaVersion:'4674',reachable:true,dataBytes:1,logBytes:1,limitBytes:2,pct:0,objectCount:null,contentBytes:null,danglingRefs:null,orphanObjects:null,lastBackupUtc:null,lastDrillUtc:null}]});
    assert.deepEqual(h.databases.map(d=>[d.name,d.kind]),[['Hcc','main']]);});
}

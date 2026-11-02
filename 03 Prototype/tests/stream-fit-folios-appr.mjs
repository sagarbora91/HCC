// FIT-2 folios-appr: Approvals = SplitView of the pending list (what / kind / who / amount / expiry / status) + chosen request pane; Folios list-only until a row is picked.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit-admin-a-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit2-folios-appr.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const F=await import('./.generated/fit2-folios-appr.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
const store=new Map();
const withSession=fn=>{const had='sessionStorage' in globalThis,prev=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:k=>store.has(k)?store.get(k):null,setItem:(k,v)=>store.set(k,String(v)),removeItem:k=>store.delete(k)};
  try{return fn();}finally{if(had)globalThis.sessionStorage=prev;else delete globalThis.sessionStorage;}};
const read=f=>readFileSync(f,'utf8');
function render(uid='owner',prep){
  const s=F.seed();if(prep)prep(s);const user=s.users.find(u=>u.id===uid);
  const ctx={s,uid,user,m:F.metrics(s),page:'approvals',go(){},modal:null,open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:()=>null,search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},arrivals:[],departures:[],pending:0,taskList:s.tasks,
    roomStatus:()=>'Vacant',switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},display:{isMobile:false,framed:false,mode:'auto',setMode(){}},
    setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
  return withSession(()=>{store.clear();return renderToStaticMarkup(h(F.HccContext.Provider,{value:ctx},
    h(F.FitNavContext.Provider,{value:{desktop:true,page:'approvals',tabKey:'',seq:1,setTabCrumb(){}}},h(F.ApprovalsAudit))));});
}
export function register({test,assert}){
  test('[FIT2-FOLIOS-APPR] approvals inbox is a SplitView: list-only until a request is picked, list columns answer what / kind / who / amount / expiry',()=>{
    const html=render();
    assert.ok(html.includes('aria-label="Approvals"')&&html.includes('fit-split'),'split view');
    assert.ok(!html.includes('fit-split has-detail'),'no record open at start');
    for(const c of ['What','Kind','Requested by','Amount','Age / expiry','Status'])assert.ok(read('src/features/admin/ApprovalsAudit.tsx').includes('<th>'+c+'</th>'),c);
    assert.ok(html.includes('Approvals inbox'));
  });
  test('[FIT2-FOLIOS-APPR] approvals: Approve/Reject stay guarded by the same commands and blocks (logic unchanged)',()=>{
    const src=read('src/features/admin/ApprovalsAudit.tsx');
    assert.ok(src.includes("run({type:r.command,id:a.id,approve,key:'approval:'+a.id}")&&src.includes('adminDecideBlock(s,uid,r)')&&src.includes('<GuardedButton block={approveBlock}'));
    assert.ok(src.includes('apr-decide'));
  });
  test('[FIT2-FOLIOS-APPR] folios keeps the SplitView (list-only default) and the Folio requests tab with its count',()=>{
    const src=read('src/features/folio/Folios.tsx');
    assert.ok(src.includes('<SplitView label="Folios"')&&!src.includes("emptyPane=\"show\""));
    assert.ok(src.includes("label:'Folio requests',count:requests.length"));
  });
  test('[FIT2-FOLIOS-APPR] approvals list rows with a seeded pending request (the demo seed has none): what, id/reason, kind, requester, amount, expiry, status',()=>{
    let b,req;
    const html=render('owner',s=>{b=s.bookings.find(x=>['Confirmed','In house'].includes(x.status));req=s.users.find(u=>u.id!=='owner')||s.users[0];
      const exp=new Date(Date.parse(s.clock+'Z')+3*3600000).toISOString().slice(0,16); // same zone-free form as s.clock
      s.approvals=[...s.approvals,{id:'APR-FIT2-1',booking:b.id,amount:125000,reason:'Corporate rate match',status:'Pending',actor:req.id,version:b.version,expiresAt:exp}];});
    const row=html.slice(html.indexOf('<tbody>'),html.indexOf('</tbody>'));
    assert.ok(row.includes('<tr'),'one row rendered');
    assert.ok(row.includes(b.guest),'what = guest');
    assert.ok(row.includes('APR-FIT2-1')&&row.includes('Corporate rate match'),'id and reason');
    assert.ok(row.includes('discount'),'kind');
    assert.ok(row.includes(req.name.split(' · ')[0]),'requested by');
    assert.ok(row.includes(F.money(125000)),'amount');
    assert.ok(/in [23] h/.test(row),'expiry on the simulated clock');
    assert.ok(row.includes('Pending'),'status');
    assert.ok(html.includes('1 pending'),'panel count');
    assert.ok(!html.includes('fit-split has-detail')&&html.includes('list-only'),'list-only until picked');
  });
  test('[FIT2-FOLIOS-APPR] lane CSS: no font-size',()=>{assert.ok(!/font-size/.test(read('src/styles/fit2-folios-appr.css')));});
}

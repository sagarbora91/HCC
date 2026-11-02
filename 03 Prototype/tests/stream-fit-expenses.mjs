// FIT-2 lane expenses (contract §12): the register has ONE filter row (status is a select, not a second tab row), compact figures, a table that
// scrolls inside itself, the approval requests as a SplitView pane that is closed until asked; the add-expense dialog is sections with a result rail.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit2-expenses-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit2-expenses.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const F=await import('./.generated/fit2-expenses.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
const read=f=>readFileSync(f,'utf8');
const store=new Map();
const withSession=fn=>{const had='sessionStorage' in globalThis,prev=globalThis.sessionStorage;
  globalThis.sessionStorage={getItem:k=>store.has(k)?store.get(k):null,setItem:(k,v)=>store.set(k,String(v)),removeItem:k=>store.delete(k)};
  try{return fn();}finally{if(had)globalThis.sessionStorage=prev;else delete globalThis.sessionStorage;}};
const nav=(page,tabKey='')=>({desktop:true,page,tabKey,seq:1,setTabCrumb(){}});
function ctxFor(uid='owner'){
  const s=F.seed();const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,m:F.metrics(s),page:'home',go(){},modal:{type:'expense',key:'k'},open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:()=>null,search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},
    arrivals:[],departures:[],pending:0,taskList:s.tasks,roomStatus:()=>'Vacant',switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},
    display:{isMobile:false,framed:false,mode:'auto',setMode(){}},setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
const page=(Comp,id,uid='owner',tabKey='')=>withSession(()=>renderToStaticMarkup(h(F.HccContext.Provider,{value:ctxFor(uid)},h(F.FitNavContext.Provider,{value:nav(id,tabKey)},h(Comp)))));

export function register({test,assert}){
  test('[FIT2-EXPENSES] register: no status tab row; status is a select in the one filter row with the same options and counts',()=>{
    const html=page(F.Expenses,'expenses');
    assert.ok(!/role="tablist" aria-label="Expense status"/.test(html),'no second tab row');
    const opts=[...html.matchAll(/<option value="(All|Draft|Pending|Posted|Rejected|Reversed|Void)">\1 \((\d+)\)<\/option>/g)];
    assert.deepEqual(opts.map(m=>m[1]),['All','Draft','Pending','Posted','Rejected','Reversed','Void']);
    const s=F.seed();assert.equal(Number(opts[0][2]),s.expenses.length,'All count is the real count');
    assert.equal(Number(opts[2][2]),s.expenses.filter(e=>e.status==='Pending').length);
    const filters=html.slice(html.indexOf('exp-filters'),html.indexOf('Expense register rows'));
    for(const k of ['Expense register','Status','Mode','Month','Search','Lists &amp; photos'])assert.ok(filters.includes(k),k+' in the filter row');
  });
  test('[FIT2-EXPENSES] register: figures are compact and collapse on a tablet; the table scrolls inside its panel',()=>{
    const html=page(F.Expenses,'expenses');
    assert.ok(html.includes('fit-stats-compact'),'compact figures');assert.ok(html.includes('fit-stats-toggle'),'collapse toggle');
    assert.ok(/data-fit-scroll=""[^>]*aria-label="Expense register rows"/.test(html));
    assert.ok(read('src/features/expenses/Expenses.tsx').includes('<StatStrip items={stats} variant="small" compact collapseOnTablet/>'));
  });
  test('[FIT2-EXPENSES] approval requests: the register takes the whole width until the pane is asked for (SplitView list-only), pending count on the button',()=>{
    const html=page(F.Expenses,'expenses');
    assert.match(html,/class="fit-split list-only"/);
    const s=F.seed();const n=s.approvals.filter(a=>a.status==='Pending').length;
    assert.ok(html.includes(`Approval requests (${n} pending)`));assert.ok(html.includes('aria-pressed="false"'));
    const src=read('src/features/expenses/Expenses.tsx');
    assert.ok(src.includes('detail={reqOpen?requestsPane:null}')&&src.includes('SplitView'));
  });
  test('[FIT2-EXPENSES] add expense: three sections with a result rail showing the real form values; Submit and Save draft stay in the bar',()=>{
    const html=page(F.ExpenseModal,'expenses');
    assert.ok(html.includes('fit-form has-summary'));
    for(const k of ['What was bought','Date and tax','Photo and notes'])assert.ok(html.includes(k),k);
    assert.ok(html.includes('No vendor'),'rail shows the vendor value');assert.ok(html.includes('amount not entered'),'rail shows the amount state');
    assert.ok(html.includes('no GST on the bill')&&html.includes('No bill photo'));
    assert.ok(html.includes('Submit for approval')&&html.includes('Save draft')&&html.includes('fit-form-actions'));
    assert.ok(!/<input required/.test(html),'no hidden required field can block the submit silently');
  });
  test('[FIT2-EXPENSES] shared fields: the phone sheet still gets all three parts through ExpenseSccFields; the save command is unchanged',()=>{
    const src=read('src/features/expenses/ExpenseModal.tsx');
    assert.ok(src.includes('export function ExpenseSccFields({x}:{x:ExpForm}){return <><ExpDescField x={x}/><ExpTaxFields x={x}/><ExpPhotoFields x={x}/></>;}'));
    assert.ok(src.includes("run(x.command(draft,modal!.key),draft?'Draft saved"));
  });
  test('[FIT2-EXPENSES] stylesheet: scoped, no font-size, no hex colour, sorts after fit-cash-b.css and before z-touch.css',()=>{
    const css=read('src/styles/fit2-expenses.css').replace(/\/\*[^]*?\*\//g,'');
    assert.ok(!/font-size/.test(css),'DEC-195');assert.ok(!/#[0-9a-fA-F]{3,8}\b/.test(css));
    assert.ok('fit2-expenses.css'>'fit-cash-b.css'&&'fit2-expenses.css'<'z-touch.css');
    for(const sel of css.replace(/@media[^{]*\{/g,'').split('}').map(x=>x.split('{')[0].trim()).filter(Boolean).flatMap(g=>g.split(',')))
      assert.ok(sel.trim().startsWith('.app-shell')||sel.trim().startsWith('.modal.fit-modal')||sel.trim().startsWith('.exp-add-task'),'unguarded: '+sel);
  });
}

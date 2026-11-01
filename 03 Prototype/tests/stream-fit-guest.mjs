// FIT-GUEST (wave W2a FIT, DEC-194…196, contract §6): guest-services, gsv-my-requests, messages, documents and customers are FitPages.
// Each renders a fit-page root, its FitTabs ids equal its SUB_SCREENS lines, the defaults are as set, Messages pages its outbox rows,
// the Customers 360 record pane has the inner tabs Overview / Stays / Legacy / Money / Notes and the phone layout keeps its old markup.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
export async function register({test,M,assert}){
  await build({entryPoints:['tests/fit-guest-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-guest.mjs',external:['react','react-dom','lucide-react'],logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
  const X=await import('./.generated/fit-guest.mjs');
  const React=(await import('react')).default;const {renderToStaticMarkup}=await import('react-dom/server');
  const h=React.createElement;
  const s=M.loadScenario('FY26');
  const noop=()=>{};
  const render=(Page,uid='owner',{mobile=false,page='',tabKey='',search='',props={}}={})=>{
    const user=s.users.find(u=>u.id===uid);
    const ctx={s,uid,user,m:X.metrics(s),page,go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,search,setSearch:noop,
      permission:p=>X.can(s,uid,p),display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop};
    const nav={desktop:!mobile,page,tabKey,seq:1,setTabCrumb:noop};
    return renderToStaticMarkup(h(X.HccContext.Provider,{value:ctx},h(X.FitNavContext.Provider,{value:nav},h(Page,props))));
  };
  const text=t=>t.replace(/<[^>]+>/g,' ').replace(/&#x27;|&#39;/g,"'").replace(/&amp;/g,'&').replace(/\s+/g,' ');
  const tabIds=html=>[...html.matchAll(/data-tab="([^"]+)"/g)].map(m=>m[1]);
  const subs=page=>X.SUB_SCREENS.filter(x=>x.page===page).map(x=>x.id);
  const pages=[['guest-services',X.GuestServicesPage,'reception'],['gsv-my-requests',X.GsvMyRequestsPage,'housekeeper'],['messages',X.MsgMessagesPage,'owner'],['documents',X.MsgDocumentsPage,'reception'],['customers',X.CustomersPage,'owner']];

  for(const [id,Page,uid] of pages)
    test(`[FIT-GUEST] ${id} renders one fit-page root with data-fit-page and the FitPage header row`,()=>{
      const html=render(Page,uid,{page:id});
      assert.match(html,new RegExp(`^<section class="fit-page fit-(fill|split)" data-fit-page="${id}"`));
      assert.ok(html.includes('class="page-title fit-page-head"')&&!/class="page-title"/.test(html),'FitPage header, no PageTitle inside');
      assert.equal((html.match(/class="fit-page /g)||[]).length,1,'one page root');
    });

  test('[FIT-GUEST] guest-services: sub-screens requests / complaints / lost-found / reports = SUB_SCREENS; default Requests; go(page,{tab}) opens another',()=>{
    const html=render(X.GuestServicesPage,'reception',{page:'guest-services'});
    assert.deepEqual(tabIds(html),['requests','complaints','lost-found','reports']);assert.deepEqual(subs('guest-services'),tabIds(html));
    assert.match(html,/data-tab="requests"[^>]*aria-selected="true"/);assert.ok(html.includes('New request')&&!html.includes('Log complaint'));
    const lf=render(X.GuestServicesPage,'reception',{page:'guest-services',tabKey:'lost-found'});
    assert.match(lf,/data-tab="lost-found"[^>]*aria-selected="true"/);assert.ok(lf.includes('Log found item')&&lf.includes('Lost and found items'));
    const rp=render(X.GuestServicesPage,'reception',{page:'guest-services',tabKey:'reports'});assert.ok(rp.includes('Print report')&&rp.includes('Response times by request type'));
    assert.ok(html.includes('class="fit-stats"')&&html.includes('Open requests')&&html.includes('Within SLA')&&html.includes('Open complaints')&&html.includes('Lost &amp; found held'),'the four stat tiles are a StatStrip');
  });
  test('[FIT-GUEST] guest-services Complaints keeps the recovery-discount footnote for roles without billing (FitNote in the panel footer)',()=>{
    for(const uid of ['reception','housekeeper','owner','cashier','supervisor']){
      const t=render(X.GuestServicesPage,uid,{page:'guest-services',tabKey:'complaints'});
      assert.equal(/Recovery discounts are applied/.test(t),!X.can(s,uid,'billing'),uid);
    }
  });
  test('[FIT-GUEST] gsv-my-requests: filters in the toolbar, request cards in one ScrollPanel, info note as FitNote, no tabs (no sub-screens), no money',()=>{
    const html=render(X.GsvMyRequestsPage,'housekeeper',{page:'gsv-my-requests'});
    assert.ok(html.includes('aria-label="Request filter"')&&html.includes('fit-panel-body')&&html.includes('fit-note')&&html.includes('Guest messages are simulated'));
    assert.deepEqual(tabIds(html),[]);assert.deepEqual(subs('gsv-my-requests'),[]);
    assert.ok(!/₹|Rs\./.test(text(html)),'DEC-061: no money on the housekeeper screen');
  });
  test('[FIT-GUEST] messages: sub-screens outbox / triggers / templates / consent = SUB_SCREENS; default Outbox; Compose first in the header actions',()=>{
    const html=render(X.MsgMessagesPage,'owner',{page:'messages'});
    assert.deepEqual(tabIds(html),['outbox','triggers','templates','consent']);assert.deepEqual(subs('messages'),tabIds(html));
    assert.match(html,/data-tab="outbox"[^>]*aria-selected="true"/);
    assert.ok(html.indexOf('Compose')<html.indexOf('Simulate delivery'),'primary action first');
    for(const [tab,needle] of [['triggers','Pending triggers'],['templates','Message templates'],['consent','Guest consent']])
      assert.ok(render(X.MsgMessagesPage,'owner',{page:'messages',tabKey:tab}).includes(needle),tab);
    assert.ok(html.includes('class="fit-stats"')&&html.includes('Not sent (consent)')&&html.includes('Pending triggers'));
  });
  test('[FIT-GUEST] messages: the outbox renders one page (usePaged) with ShowMore in the panel footer, not every row',()=>{
    const total=X.msgOutbox(s).length;
    const html=render(X.MsgMessagesPage,'owner',{page:'messages'});
    const rows=(html.match(/<tr>/g)||[]).length-1;
    assert.ok(rows<=100,'at most one page of rows, got '+rows);
    assert.ok(total>100,'FY26 outbox is long enough to page ('+total+')');
    assert.ok(/Showing 100 of [\d,]+ rows/.test(html),'ShowMore status');
    assert.ok(html.indexOf('show-more')>html.indexOf('fit-panel-foot'),'ShowMore sits in the footer');
    assert.ok(html.includes('Simulated.'),'the demo simulated note stays (FitNote)');
  });
  test('[FIT-GUEST] documents: split view, samples and history on the left, review on the right, the three steps keep their text',()=>{
    const html=render(X.MsgDocumentsPage,'reception',{page:'documents'});
    assert.match(html,/^<section class="fit-page fit-split"/);
    assert.ok(html.includes('1 · Pick a sample document')&&html.includes('Capture history')&&html.includes('2 · Review and confirm'));
    assert.ok(html.includes('class="fit-split')&&html.includes('Fictional samples'));assert.deepEqual(subs('documents'),[]);
  });
  test('[FIT-GUEST] customers: split view; record pane with inner tabs Overview / Stays / Legacy / Money / Notes (not in SUB_SCREENS, no crumb)',()=>{
    const html=render(X.CustomersPage,'owner',{page:'customers',search:'Ananya Rao'});
    assert.match(html,/^<section class="fit-page fit-split" data-fit-page="customers"/);
    assert.ok(html.includes('aria-label="Customer list"')&&html.includes('class="card cust-panel cust-fit"'));
    assert.deepEqual(tabIds(html),['overview','stays','legacy','money','notes']);assert.deepEqual(subs('customers'),[]);
    assert.ok(/\(3 legacy\)/.test(text(html)),'list keeps the legacy count');
    assert.ok(!html.includes('cust-back'),'the split view owns the Back button');
  });
  test('[FIT-GUEST] customers: the Money tab follows the billing permission; no image element on the page (ID images stay behind their reveal)',()=>{
    for(const uid of ['owner','reception','cashier','housekeeper']){
      const html=render(X.CustomersPage,uid,{page:'customers',search:'Ananya Rao'});
      if(!html.includes('cust-fit'))continue;
      assert.equal(tabIds(html).includes('money'),X.can(s,uid,'billing'),uid);
      assert.ok(!/<img/.test(html),uid);
    }
  });
  test('[FIT-GUEST] phone layout keeps the old Customers markup (no fit- class), legacy stays still listed under the stays',()=>{
    const html=render(X.CustomersPage,'owner',{mobile:true,page:'customers',search:'Ananya Rao'});
    assert.ok(!/fit-/.test(html)&&html.includes('class="cust'),'phone: old cust markup, no fit- class');
    assert.ok(/Earlier stays — old Lodge app/.test(text(html)),'phone History still lists legacy stays');
  });
  test('[FIT-GUEST] desktop CSS is in guest-fit.css, every selector guarded by .app-shell, no font-size and no colour literal',()=>{
    const css=readFileSync('src/styles/guest-fit.css','utf8').replace(/\/\*[^]*?\*\//g,'');
    for(const sel of css.split('}').map(r=>r.split('{')[0].trim()).filter(Boolean))assert.ok(sel.split(',').every(x=>x.trim().startsWith('.app-shell')),'unguarded selector: '+sel);
    assert.ok(!/font-size|#[0-9a-f]{3,6}\b|rgb\(/i.test(css));
  });
}

// DEC-076 screens, rendered server-side over the mock state (desktop and phone): the checkout panel (refusal shown, request disabled; an
// unlinked stay offers "Bill to this account"), the booking dialogs' account picker, the company-payment dialog, the Owner-only limit dialog
// and the Groups pages. Names prefixed [CREDIT-UI].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/credit-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {CompanyCreditPanel,CompanyAccountPicker,CompanyPaymentModal} from '../../src/features/groups/credit';
import {LimitModal} from '../../src/features/groups/modals';
import {GroupsPage} from '../../src/features/groups/Groups';
import {GroupsMobile} from '../../src/features/groups/Mobile';
const C:any={CompanyCreditPanel,CompanyAccountPicker,CompanyPaymentModal,LimitModal,GroupsPage,GroupsMobile};
export function render(name:string,props:any,s:any,uid:string,mobile=false){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page:'groups',go:noop,modal:{type:'x',key:'k1'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop};
  return renderToStaticMarkup(<HccContext.Provider value={ctx}>{React.createElement(C[name],props)}</HccContext.Provider>);
}`);
  await build({entryPoints:['tests/.generated/credit-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/credit-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __cuReq} from 'node:module';const require=__cuReq(import.meta.url);"}});
  const {render}=await import('./.generated/credit-ui.mjs');
  const text=html=>html.replace(/<[^>]+>/g,' ').replace(/&#x27;|&#39;/g,"'").replace(/&amp;/g,'&').replace(/\s+/g,' ');
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);

  test('[CREDIT-UI] checkout panel (desktop + phone): credit line, the refusal word for word and the request button disabled',()=>{
    const s=M.execute(M.seed(),'owner',{type:'GRP_LIMIT_SET',company:'CO-1',limit:6635000});
    for(const mobile of [false,true]){
      const h=render('CompanyCreditPanel',{b:bk(s,'HCC-1044'),mobile},s,'reception',mobile),t=text(h);
      assert.match(t,/Company credit · Deccan Pharma Distributors \(fictional\)/);
      assert.match(t,/Credit ₹70,600 used of ₹66,350 · ₹0 left\./);
      assert.match(t,/Credit limit exceeded for Deccan Pharma Distributors \(fictional\): limit ₹66,350, already used by other business ₹66,300, headroom ₹50; this stay on company credit needs ₹100\./);
      assert.match(h,/<button[^>]*disabled=""[^>]*>Request company credit for ₹100<\/button>/);
    }
    const ok=text(render('CompanyCreditPanel',{b:bk(M.seed(),'HCC-1044')},M.seed(),'reception'));
    assert.match(ok,/Move the ₹100 balance to the company’s receivable/);assert.doesNotMatch(ok,/Credit limit exceeded/);
  });
  test('[CREDIT-UI] an in-house stay not billed to an account offers the accounts with what is left (phone 48 px controls)',()=>{
    const s=M.seed();
    const h=render('CompanyCreditPanel',{b:bk(s,'HCC-1041'),mobile:true},s,'reception',true),t=text(h);
    assert.match(t,/Company credit needs the booking billed to a company account/);
    assert.match(t,/Godavari Agro Inputs \(fictional\) · ₹12,000 left/);assert.match(h,/grp-link grp-link-m/);assert.match(t,/Bill to this account/);
    assert.equal(render('CompanyCreditPanel',{b:bk(s,'HCC-1041')},s,'housekeeper'),'','no account controls and no company: nothing shown');
  });
  test('[CREDIT-UI] booking dialog account picker: remaining credit, or the refusal for the estimated stay',()=>{
    const s=M.seed();
    assert.match(text(render('CompanyAccountPicker',{value:'CO-2',onChange:()=>{},stay:630000},s,'reception')),/Credit ₹88,000 used of ₹1,00,000 · ₹12,000 left · this stay about ₹6,300 incl\. GST/);
    assert.match(text(render('CompanyAccountPicker',{value:'CO-2',onChange:()=>{},stay:1575000,mobile:true},s,'reception',true)),/headroom ₹12,000; this booking needs ₹15,750\./);
  });
  test('[CREDIT-UI] company payment dialog and the Owner-only limit dialog',()=>{
    const s=M.seed();
    const p=text(render('CompanyPaymentModal',{id:'CO-1'},s,'cashier'));
    assert.match(p,/Owed on company credit ₹62,000/);assert.match(p,/No cash on company credit/);
    assert.match(text(render('CompanyPaymentModal',{id:'CO-3'},s,'cashier')),/owes nothing on company credit/);
    assert.match(text(render('LimitModal',{id:'CO-2'},s,'manager')),/the Owner decides it \(DEC-076\)/);
    const own=text(render('LimitModal',{id:'CO-1'},s,'owner'));
    assert.match(own,/Used ₹70,600/);assert.match(own,/Remaining ₹79,400/);assert.match(own,/Record a payment from the company/);
  });
  test('[CREDIT-UI] Groups pages render for the Manager (desktop) and Reception (phone)',()=>{
    const s=M.seed();
    assert.match(text(render('GroupsPage',{},s,'manager')),/Groups & corporate/);
    assert.match(text(render('GroupsMobile',{},s,'reception',true)),/Open blocks/);
  });
}

// Wave B9 small owner decisions, prototype side.
//   DEC-080 (A02b-04)  the production build says "Group blocks are not available yet…" on the New group block sheet and cannot submit; the demo build is unchanged.
//   DEC-081 (A09-06)   a printable in-house list and arrivals list (desktop + phone): pure rows (stayListRows), the printed document, the Print buttons.
//   DEC-071 (A11-02)   the Master configuration shows ONE ID-image retention rule (documents.retentionDays); the duplicate doc.retentionDays is gone.
// The production-side refusal of GRP_BLOCK_CREATE (no request sent, same sentence as the server) is tested in sync-money.mjs.
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';

const ENTRY=`
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics,roomStatus,addDays,assignedScopeRoles} from '../../src/services/mock';
import {navItems} from '../../src/app/nav'; // same import order as App.tsx (nav before registry)
import {extraPages,extraModals,extraWide,mobilePages} from '../../src/app/registry';
import {FrontDeskListModal} from '../../src/features/guests/FrontDeskList';
import {BlockModal} from '../../src/features/groups/modals';
void navItems;
function ctx(s:any,uid:string,mobile:boolean,over:any={}):any{
  const user=s.users.find((u:any)=>u.id===uid)??s.users[0];
  const noop=()=>{};
  return {s,uid,user,m:metrics(s),page:'x',go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),act:()=>null,search:'',setSearch:noop,filter:'All',setFilter:noop,tab:'Arrivals',setTab:noop,
    arrivals:s.bookings.filter((b:any)=>b.status==='Confirmed'&&b.arrival<=s.date),departures:s.bookings.filter((b:any)=>b.status==='In house'&&b.departure<=s.date),
    pending:0,taskList:[],roomStatus:(r:any)=>roomStatus(s,r),switchUser:noop,fault:'none',setFault:noop,loadScenario:noop,resetScenario:noop,
    display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop,...over};
}
const html=(s:any,uid:string,mobile:boolean,el:any,over:any={})=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile,over)}>{el}</HccContext.Provider>);
export const list=(s:any,uid:string,kind:string,mobile=false,over:any={})=>html(s,uid,mobile,<FrontDeskListModal id={kind}/>,over);
export const block=(s:any,uid:string)=>html(s,uid,false,<BlockModal/>,{modal:{type:'grp-block',key:'k'}});
export const page=(s:any,uid:string,id:string,mobile=false)=>{const P=(mobile?mobilePages:extraPages)[id];return html(s,uid,mobile,<P/>);};
export const registered={modal:!!extraModals['front-list'],wide:extraWide.includes('front-list')};
`;

export async function register({test,M,assert}){
  await mkdir('tests/.generated',{recursive:true});
  await writeFile('tests/.generated/wb9-entry.tsx',ENTRY);
  const load=async mode=>{
    await build({entryPoints:['tests/.generated/wb9-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:`tests/.generated/wb9-${mode}.mjs`,logLevel:'error',jsx:'automatic',
      define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':JSON.stringify(mode)},
      banner:{js:"import {createRequire as __wb9Req} from 'node:module';const require=__wb9Req(import.meta.url);"}});
    return import(new URL(`./.generated/wb9-${mode}.mjs`,import.meta.url).href);
  };
  const prod=await load('production'),demo=await load('prototype');
  const s=M.seed();
  const esc=t=>String(t).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;').replace(/'/g,'&#x27;');
  const inhouse=M.stayInHouse(s),arrivals=M.stayArrivals(s);

  // ── DEC-080 ──
  const blockProd=prod.block(s,'owner'),blockDemo=demo.block(s,'owner');
  test('[DEC-080] production: the New group block sheet states the reason up front and the Create button is off; the demo build shows no such note',()=>{
    assert.ok(blockProd.includes('Group blocks are not available yet: pickup and release are still being built. Book the rooms as separate reservations.'));
    assert.match(blockProd,/<button class="btn primary" disabled="">Create block/);
    assert.ok(!blockDemo.includes('not available yet'),'demo build unchanged');
    assert.match(blockDemo,/New group block/);
  });

  // ── DEC-071 ──
  test('[DEC-071] Master configuration: one ID-image retention setting (documents.retentionDays, 90 days, Data and system, dated, manual purge only); doc.retentionDays is gone',()=>{
    const defs=M.cfgDefs(s),ids=defs.filter(d=>/retention/i.test(d.key+d.label)&&/doc|ID image/i.test(d.key+d.label));
    assert.deepEqual(ids.map(d=>d.key),['documents.retentionDays']);
    assert.equal(M.cfgDef(s,'doc.retentionDays'),undefined);
    const d=M.cfgDef(s,'documents.retentionDays');
    assert.deepEqual([d.group,d.default,d.min,d.max,d.dated,d.editable],['data',90,1,3650,true,true]);
    assert.match(d.help,/manual purge/);assert.match(d.help,/Nothing is deleted automatically/);assert.match(d.help,/DEC-071/);
    assert.ok(!M.CFG_GROUPS.find(g=>g.id==='messages').blurb.includes('retention'));
  });

  // ── DEC-081: rows ──
  test('[DEC-081] stayListRows: the in-house list holds every in-house stay (room, guest, dates, balance), the arrivals list every waiting arrival (advance paid)',()=>{
    const rows=M.stayListRows(s,'owner','inhouse');
    assert.ok(inhouse.length>=8);
    assert.deepEqual(rows.map(r=>[r.room,r.guest]),inhouse.map(b=>[b.room,b.guest]),'same selection and room order as the In-house stays page');
    const t=M.totals(inhouse[0]);
    assert.deepEqual([rows[0].arrival,rows[0].departure,rows[0].nights,rows[0].people,rows[0].paid,rows[0].balance,rows[0].ref],[inhouse[0].arrival,inhouse[0].departure,3,1,t.paid,t.balance,inhouse[0].id]);
    const arr=M.stayListRows(s,'owner','arrivals');
    assert.deepEqual(arr.map(r=>[r.room,r.guest,r.registered]),arrivals.map(b=>[b.room,b.guest,!!b.registered]));
    assert.ok(arr.length>=2);
    assert.deepEqual(M.STAY_LIST_TITLE,{inhouse:'In-house list',arrivals:'Arrivals list'});
  });
  test('[DEC-081] stayListRows: phone numbers follow the viewer’s masking (a housekeeper sees only the last four digits)',()=>{
    assert.equal(M.stayListRows(s,'owner','inhouse')[0].phone,'+91 98200 01041');
    assert.equal(M.stayListRows(s,'housekeeper','inhouse')[0].phone,'••••••1041');
  });

  // ── DEC-081: the printed document (both builds, desktop sheet and phone sheet) ──
  const docs=[['production desktop',prod,false],['production phone',prod,true],['demo desktop',demo,false],['demo phone',demo,true]];
  for(const [label,X,mobile] of docs){
    const h=X.list(s,'owner','inhouse',mobile),a=X.list(s,'owner','arrivals',mobile);
    test(`[DEC-081] ${label}: the in-house list prints the hotel, the business date and every in-house room and guest`,()=>{
      assert.match(h,/<div class="print-doc invoice fd-list">/,'printDocument() picks .print-doc/.invoice');
      assert.match(h,/<h2>In-house list<\/h2>/);
      assert.ok(h.includes('HCC Demo Residency')&&h.includes('24 Oct 2026')&&h.includes(`${inhouse.length} stays`));
      for(const b of inhouse){
        assert.ok(h.includes(`<td><strong>${b.room}</strong></td><td>${esc(b.guest)}</td>`),`table row for room ${b.room} ${b.guest}`);
        assert.ok(h.includes(esc(b.guest)+'</strong>'),`card for ${b.guest}`);
      }
      assert.match(h,/Balance due/);assert.match(h,/<th>Notes<\/th>/,'room for hand-written notes on paper');
      assert.match(h,/<div class="modal-actions no-print">.*Print<\/button>/,'Print button is outside the printed area');
    });
    test(`[DEC-081] ${label}: the arrivals list prints every waiting arrival with room, stay dates and registration`,()=>{
      assert.match(a,/<h2>Arrivals list<\/h2>/);
      assert.ok(a.includes(`${arrivals.length} arrivals`));
      for(const b of arrivals){
        assert.ok(a.includes(`<td><strong>${b.room}</strong></td><td>${esc(b.guest)}</td>`),`table row for room ${b.room} ${b.guest}`);
        assert.ok(a.includes(esc(b.guest)+'</strong>'));
      }
      assert.match(a,/Registered/);assert.match(a,/Advance paid/);
      assert.ok(!a.includes('Balance due'));
    });
  }
  test('[DEC-081] phone sheet shows cards, paper always gets the table; money columns only with the billing permission; unknown list id is refused',()=>{
    const ph=prod.list(s,'owner','inhouse',true);
    assert.ok(ph.includes('class="fd-cards"')&&ph.includes('class="table-wrap fd-table"'));
    const noMoney=prod.list(s,'housekeeper','inhouse',false,{permission:()=>false});
    assert.ok(!noMoney.includes('Balance due')&&noMoney.includes(inhouse[0].guest),'rooms and guests still print without money');
    assert.ok(noMoney.includes('••••••1041'),'masked phone');
    assert.match(prod.list(s,'owner','nonsense'),/This record is unavailable/);
    const empty={...s,bookings:s.bookings.filter(b=>b.status!=='In house')};
    assert.match(prod.list(empty,'owner','inhouse'),/No guests are in house\./);
  });

  // ── DEC-081: where the buttons are ──
  test('[DEC-081] Print buttons: In-house stays and Arrivals pages, desktop and phone; the modal is registered and wide',()=>{
    assert.ok(prod.page(s,'owner','inhouse').includes('Print in-house list'));
    assert.ok(prod.page(s,'owner','arrivals').includes('Print arrivals list'));
    const pi=prod.page(s,'reception','inhouse',true),pa=prod.page(s,'reception','arrivals',true);
    assert.match(pi,/<button type="button" class="btn full mf-more">.*Print in-house list<\/button>/);
    assert.match(pa,/<button type="button" class="btn full mf-more">.*Print arrivals list<\/button>/);
    assert.deepEqual([prod.registered,demo.registered],[{modal:true,wide:true},{modal:true,wide:true}]);
  });
}

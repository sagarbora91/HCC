// MA-4 · POS masters on the production sync layer (src/clients/sync/pos-masters.ts + the posX loader in sync/pos.ts) against a fake
// server shaped like the .NET DTOs (08 Production/src/Hcc.Contracts/Pos/PosContracts.cs + PosMastersContracts.cs), plus render checks of
// the POS pages and the master lists / sheets on prototype states with changed masters. Names prefixed [POS-MST-SYNC].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import {loadSync,loadPages,fakeServer,startRuntime,capabilities} from './_sync-harness.mjs';
export async function register({test,M,assert}){
  const S=await loadSync();
  // A stub owner of "approvals" (the admin area's slice, which the POS area patches) keeps the runtime to the three areas under test.
  const approvals={id:'approvals-stub',loaders:[{slice:'approvals',load:async()=>[]}],commands:[]};
  const mine={areas:[...S.AREAS.filter(a=>['core','pos','masters'].includes(a.id)),approvals]};
  const tbl=(id,no,o={})=>({tableId:id,code:'REST-'+no,name:'Restaurant '+no,tableNo:no,seats:4,section:null,counter:false,sortOrder:id*10,active:true,retiredOn:null,version:'TV'+id,...o});
  const outlets=[
    {outletId:1,code:'REST',name:'Restaurant',kind:'table',tables:['T1','T2'],type:'restaurant',opens:'07:00',closes:'23:00',gstProfile:'hotel',kotStation:'Kitchen',sortOrder:10,active:true,retiredOn:null,version:'OV1',
      tableRows:[tbl(11,'T1',{section:'Garden'}),tbl(12,'T2'),tbl(13,'T3',{active:false,retiredOn:'2026-10-20'})]},
    {outletId:2,code:'RS',name:'Room service',kind:'room',tables:[],type:'room-service',opens:null,closes:null,gstProfile:'specified',kotStation:null,sortOrder:20,active:true,retiredOn:null,version:'OV2',tableRows:[]}];
  const item=(id,code,name,cat,catCode,paise,o={})=>({itemId:id,code,name,category:cat,pricePaise:paise,veg:true,available:true,outletIds:[1,2],version:'MV'+id,
    categoryId:catCode==='BEV'?31:32,categoryCode:catCode,shortCode:null,gstClass:'food',station:null,sortOrder:id*10,active:true,retiredOn:null,soldOutOn:null,
    prices:[{effectiveFrom:'2017-07-01',amountPaise:paise}],...o});
  const menu=[item(21,'TEA','Masala chai','Beverages','BEV',6000,{prices:[{effectiveFrom:'2017-07-01',amountPaise:6000},{effectiveFrom:'2026-11-01',amountPaise:7000}]}),
    item(22,'NAAN','Butter naan','Breads','BRD',7000,{station:'Tandoor',shortCode:'BN'}),item(23,'KULFI','Malai kulfi','Breads','BRD',15000,{available:false,soldOutOn:'2026-10-24'})];
  const cats=[{categoryId:31,code:'BEV',name:'Beverages',sortOrder:10,active:true,retiredOn:null,items:1,version:'CV31'},{categoryId:32,code:'BRD',name:'Breads',sortOrder:20,active:true,retiredOn:null,items:2,version:'CV32'}];
  const page=items=>({page:1,pageSize:200,total:items.length,items});
  const routes={'GET /pos/outlets':()=>outlets,'GET /pos/menu':()=>menu,'GET /pos/menu-categories':()=>page(cats),'GET /pos/checks':()=>[],'GET /pos/checks/closed':()=>[],
    'GET /pos/void-requests':()=>[],'GET /masters/floors':()=>page([]),'GET /masters/floors/history':()=>page([]),'GET /closing/business-date':()=>({businessDate:'2026-10-24',closed:false})};
  const names=['SaveOutlet','RetireOutlet','ReactivateOutlet','ReorderOutlets','DeleteOutlet','SaveOutletTable','RetireOutletTable','ReactivateOutletTable','ReorderOutletTables','DeleteOutletTable',
    'SaveMenuCategory','RetireMenuCategory','ReactivateMenuCategory','ReorderMenuCategories','DeleteMenuCategory','SaveMenuItem','RetireMenuItem','ReactivateMenuItem','ReorderMenuItems','DeleteMenuItem'];
  for(const n of names)routes['POST /commands/'+n]=()=>({});
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv,mine);
  const st=rt.get().state,bootErrors=rt.get().errors,bootGets=srv.gets().map(c=>c.path);

  test('[POS-MST-SYNC] posX loader maps the master fields: outlet type / timings / GST profile / KOT station / tableRows; versions learned',()=>{
    assert.deepEqual(srv.unrouted,[]);assert.deepEqual(bootErrors,{});
    const o=st.posX.outlets[0];
    assert.deepEqual([o.id,o.code,o.type,o.opens,o.closes,o.gstProfile,o.kotStation,o.sortOrder,o.active],['1','REST','restaurant','07:00','23:00','hotel','Kitchen',10,true]);
    assert.deepEqual(o.tableRows.map(t=>[t.id,t.code,t.tableNo,t.seats,t.section??null,t.active]),[['11','REST-T1','T1',4,'Garden',true],['12','REST-T2','T2',4,null,true],['13','REST-T3','T3',4,null,false]]);
    assert.equal(st.posX.outlets[1].tableRows,undefined);assert.equal(st.posX.outlets[1].gstProfile,'specified');
    assert.equal(rt.ids.version('outlet','1'),'OV1');assert.equal(rt.ids.version('outletTable','12'),'TV12');assert.equal(rt.ids.version('menuItem','22'),'MV22');
    assert.ok(bootGets.includes('/pos/menu-categories'));});
  test('[POS-MST-SYNC] menu items: dated prices, category code, station, short code, sold-out date; categories load into mastersX',()=>{
    const tea=st.posX.menu[0];assert.deepEqual(tea.prices,[{amount:6000,effectiveFrom:'2017-07-01'},{amount:7000,effectiveFrom:'2026-11-01'}]);
    assert.equal(tea.categoryCode,'BEV');assert.equal(st.posX.menu[1].station,'Tandoor');assert.equal(st.posX.menu[1].shortCode,'BN');assert.equal(st.posX.menu[2].soldOutOn,'2026-10-24');
    assert.deepEqual(st.mastersX.rows.menuCategories.map(r=>[r.id,r.code,r.name]),[['31','BEV','Beverages'],['32','BRD','Breads']]);
    assert.equal(rt.ids.version('menuCategory','32'),'CV32');assert.equal(st.mastersX.rows.outlets,undefined);});
  test('[POS-MST-SYNC] the prototype selectors read the server rows: lists, POS tables, prices on a date, sold out, rates',()=>{
    assert.deepEqual(M.mstRows(st,'outlets').map(r=>r.code),['REST','RS']);assert.deepEqual(M.mstRows(st,'posTables').map(r=>[r.code,r.outlet,r.active]),[['REST-T1','REST',true],['REST-T2','REST',true],['REST-T3','REST',false]]);
    assert.deepEqual(M.posTablesOf(st,'1').map(t=>t.tableNo),['T1','T2']);
    const tea=st.posX.menu[0];assert.equal(M.posItemPrice(st,tea),6000);assert.equal(M.posItemPrice(st,tea,'2026-11-02'),7000);
    assert.equal(M.posSoldOut(st,st.posX.menu[2]),true);assert.deepEqual(M.mstByCode(st,'menuItems','NAAN').outlets,['REST','RS']);
    assert.equal(M.posRateFor(st,'2','food',st.date).bps,1800);assert.deepEqual(M.posCategories(st,'1'),['Beverages','Breads']);});

  const seq=[];
  const go=async cmd=>{srv.reset();const r=await rt.run(cmd);seq.push({r,cmds:srv.calls.filter(c=>c.method==='POST'),gets:srv.gets().map(c=>c.path)});return seq.at(-1);};
  const rest=M.mstByCode(st,'outlets','REST'),t2=M.mstByCode(st,'posTables','REST-T2'),naan=M.mstByCode(st,'menuItems','NAAN');
  const addOutlet=await go({type:'MST_MASTER_SAVE',master:'outlets',row:{code:'bar',name:'Bar',type:'bar',opens:'17:00',closes:'',kotStation:'Bar',sortOrder:30},key:'pos-mst:bar'});
  const editOutlet=await go({type:'MST_MASTER_SAVE',master:'outlets',row:{...rest,gstProfile:'standard'}});
  const addTable=await go({type:'MST_MASTER_SAVE',master:'posTables',row:{code:'REST-C1',name:'Counter',outlet:'REST',tableNo:'C1',counter:true}});
  const editTable=await go({type:'MST_MASTER_SAVE',master:'posTables',row:{...t2,seats:6,section:'Hall'}});
  const addCat=await go({type:'MST_MASTER_SAVE',master:'menuCategories',row:{code:'SOUP',name:'Soups'}});
  const editItem=await go({type:'MST_MASTER_SAVE',master:'menuItems',row:{...naan,category:'BEV',price:[...naan.price,{amount:8000,effectiveFrom:'2026-10-24'}],soldOutToday:true}});
  const retire=await go({type:'MST_MASTER_RETIRE',master:'outlets',id:'2'});
  const back=await go({type:'MST_MASTER_REACTIVATE',master:'posTables',id:'13'});
  const del=await go({type:'MST_MASTER_DELETE',master:'menuCategories',id:'31'});
  const order=await go({type:'MST_MASTER_REORDER',master:'menuItems',ids:['23','21','22']});
  test('[POS-MST-SYNC] outlets: new → SaveOutlet {code,name,sortOrder,type,opens,closes,gstProfile,kotStation} with the dialog key; edit carries outletId + version; posX reloads',()=>{
    assert.ok(addOutlet.r.ok);const c=addOutlet.cmds[0];assert.equal(c.path,'/commands/SaveOutlet');assert.equal(c.key,'pos-mst:bar');
    assert.deepEqual(c.body,{code:'BAR',name:'Bar',sortOrder:30,type:'bar',opens:'17:00',closes:null,gstProfile:'hotel',kotStation:'Bar'});
    assert.ok(addOutlet.gets.includes('/pos/outlets')&&addOutlet.gets.includes('/pos/menu-categories'));
    assert.deepEqual(editOutlet.cmds[0].body,{code:'REST',name:'Restaurant',sortOrder:10,type:'restaurant',opens:'07:00',closes:'23:00',gstProfile:'standard',kotStation:'Kitchen',outletId:1,expectedVersion:'OV1'});});
  test('[POS-MST-SYNC] tables: SaveOutletTable with the outlet code turned into outletId; edit with tableId + version',()=>{
    assert.deepEqual([addTable.cmds[0].path,addTable.cmds[0].body],['/commands/SaveOutletTable',{code:'REST-C1',name:'Counter',outletId:1,tableNo:'C1',seats:null,section:null,counter:true}]);
    assert.deepEqual(editTable.cmds[0].body,{code:'REST-T2',name:'Restaurant T2',sortOrder:120,outletId:1,tableNo:'T2',seats:6,section:'Hall',counter:false,tableId:12,expectedVersion:'TV12'});});
  test('[POS-MST-SYNC] menu: SaveMenuCategory; SaveMenuItem with categoryId, outletIds, dated prices (paise), price in force, GST class, station, sold out',()=>{
    assert.deepEqual([addCat.cmds[0].path,addCat.cmds[0].body],['/commands/SaveMenuCategory',{code:'SOUP',name:'Soups'}]);
    const b=editItem.cmds[0].body;assert.equal(editItem.cmds[0].path,'/commands/SaveMenuItem');
    assert.deepEqual(b,{code:'NAAN',name:'Butter naan',sortOrder:220,category:'Beverages',categoryId:31,pricePaise:8000,
      prices:[{effectiveFrom:'2017-07-01',amountPaise:7000},{effectiveFrom:'2026-10-24',amountPaise:8000}],veg:true,outletIds:[1,2],shortCode:'BN',gstClass:'food',station:'Tandoor',soldOutToday:true,
      itemId:22,expectedVersion:'MV22'});
    assert.ok(editItem.gets.includes('/pos/menu'));});
  test('[POS-MST-SYNC] retire / reactivate / delete / reorder → the list’s server command with the id and version (reorder: ids in the new order)',()=>{
    assert.deepEqual([retire.cmds[0].path,retire.cmds[0].body],['/commands/RetireOutlet',{outletId:2,expectedVersion:'OV2'}]);
    assert.deepEqual([back.cmds[0].path,back.cmds[0].body],['/commands/ReactivateOutletTable',{tableId:13,expectedVersion:'TV13'}]);
    assert.deepEqual([del.cmds[0].path,del.cmds[0].body],['/commands/DeleteMenuCategory',{categoryId:31,expectedVersion:'CV31'}]);
    assert.deepEqual([order.cmds[0].path,order.cmds[0].body],['/commands/ReorderMenuItems',{itemIds:[23,21,22]}]);
    for(const x of [retire,back,del,order])assert.ok(x.r.ok&&x.gets.includes('/pos/outlets'),x.cmds[0].path);});

  // Tech admin (masters only, no POS operate): the outlets, menu and categories load for Master configuration; no check is read.
  const tad=fakeServer({...routes,'GET /me/capabilities':()=>capabilities({roles:['TAD'],grants:[['PERM-ADM-001','A'],['PERM-MST-001','A']]})});
  const trt=await startRuntime(S,tad,mine);
  test('[POS-MST-SYNC] Tech admin (PERM-MST-001 only) loads outlets, menu and categories but sends no check / void read',()=>{
    assert.deepEqual(trt.get().errors,{});const gets=tad.gets().map(c=>c.path);
    assert.ok(gets.includes('/pos/outlets')&&gets.includes('/pos/menu')&&gets.includes('/pos/menu-categories'));
    assert.ok(!gets.some(p=>p.startsWith('/pos/checks')||p==='/pos/void-requests'),gets.join(' '));
    assert.equal(trt.get().state.posX.outlets.length,2);assert.deepEqual(trt.get().state.posX.orders,[]);});

  // Render: POS pages and Master configuration (desktop + phone) on the server state and on prototype states with changed POS masters.
  let proto=M.execute(M.seed(),'owner',{type:'MST_MASTER_SAVE',master:'outlets',row:{code:'BAR',name:'Rooftop bar',type:'bar',opens:'17:00',closes:'23:00',kotStation:'Bar'}});
  proto=M.execute(proto,'owner',{type:'MST_MASTER_SAVE',master:'posTables',row:{code:'BAR-B1',name:'Bar B1',outlet:'BAR',tableNo:'B1',seats:2,counter:true}});
  proto=M.execute(proto,'owner',{type:'MST_MASTER_SAVE',master:'menuItems',row:{...M.mstByCode(proto,'menuItems','NAAN'),station:'Tandoor'}});
  proto=M.execute(proto,'owner',{type:'MST_MASTER_RETIRE',master:'posTables',id:M.mstByCode(proto,'posTables','RESTAURANT-T8').id});
  proto=M.execute(proto,'cashier',{type:'POS_MENU_TOGGLE',item:'m-tea'});
  const P=await loadPages();
  const shown=[P.renderPages(st,'1',false),P.renderPages(st,'1',true),P.renderPages(proto,'owner',false),P.renderPages(proto,'owner',true),P.renderPages(proto,'cashier',true),P.renderPages(proto,'techadmin',false)];
  await writeFile('tests/.generated/posmst-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can} from '../../src/services/mock';
import {MasterList} from '../../src/features/masters/MasterList';
import {MasterListPhone} from '../../src/mobile/masters/MasterListPhone';
import {MasterSheet} from '../../src/mobile/masters/MasterSheet';
import {PosKitchenBoard} from '../../src/features/pos/PosPage';
function ctx(s:any,uid:string,mobile:boolean){const user=s.users.find((u:any)=>u.id===uid);
  return {s,uid,user,busy:false,run:async()=>true,open(){},closeModal(){},toast(){},setDirty(){},go(){},modal:null,permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile}} as any;}
const wrap=(s:any,uid:string,el:any,mobile=false)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile)}>{el}</HccContext.Provider>);
export const list=(s:any,uid:string,spec:string,select?:string)=>wrap(s,uid,<MasterList spec={spec} select={select}/>);
export const phone=(s:any,uid:string,spec:string)=>wrap(s,uid,<MasterListPhone spec={spec}/>,true);
export const sheet=(s:any,uid:string,id:string)=>wrap(s,uid,<MasterSheet id={id}/>,true);
export const kitchen=(s:any,uid:string)=>wrap(s,uid,<PosKitchenBoard/>);
`);
  await build({entryPoints:['tests/.generated/posmst-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/posmst-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __pmReq} from 'node:module';const require=__pmReq(import.meta.url);"}});
  const UI=await import('./.generated/posmst-ui.mjs');
  test('[POS-MST-SYNC] every desktop and phone page renders on the server data and on changed POS masters (outlet, counter, station, retired table, sold out)',()=>{
    assert.deepEqual(shown.map(x=>x.errors),[{},{},{},{},{},{}]);assert.ok(shown.every(x=>x.rendered>5));});
  test('[POS-MST-SYNC] master lists and sheets render for the four POS lists; money fields hidden from the housekeeper, locked for Tech admin',()=>{
    for(const id of ['outlets','posTables','menuCategories','menuItems']){
      assert.match(UI.list(proto,'owner',id),/mst-list/,id);assert.match(UI.phone(proto,'techadmin',id),/mst-m-list/,id);}
    const owner=UI.list(proto,'owner','menuItems','m-naan');assert.match(owner,/Kitchen station/);assert.match(owner,/GST class/);assert.match(owner,/In force on/);
    const ta=UI.sheet(proto,'techadmin','outlets:restaurant');assert.match(ta,/GST profile/);assert.match(ta,/Owner changes this/);
    const hk=UI.sheet(proto,'housekeeper','menuItems:m-tea');assert.doesNotMatch(hk,/GST class|In force on/);assert.match(hk,/View only/);
    assert.match(UI.kitchen(proto,'cashier'),/All stations/);assert.match(UI.kitchen(proto,'cashier'),/Sold out today \(86 list\)/);});
}

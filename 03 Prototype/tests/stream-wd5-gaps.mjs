// Wave D5 — role matrix v2 screen gaps left by Wave B8 (DEC-079), prototype screens, production and demo builds:
//   D1  Access control › Role templates shows the Restaurant (RST) template when the server's roles read returned it, with the module
//       permission ids the v1 matrix has no row for (its PERM-POS-* and PERM-HR-004); every server template is read-only. Demo unchanged.
//   D4  the Tech admin's PHONE Messages page has Templates (+ Triggers), as on desktop; other phone users get a view-only Templates list.
//   D7  the OTA channel screens hide the commission rate, "Commission due" and the commission report when the server withheld the rate
//       (no PERM-OTA-004: Reception, Tech admin). Owner / Manager / Accountant and the demo build see them as before.
// Sync-layer side (mapping, the DEC-080 group-block switch): tests/sync-wd5-gaps.mjs.
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';

const ENTRY=`
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics,roomStatus} from '../../src/services/mock';
import {navItems} from '../../src/app/nav'; // same import order as App.tsx (nav before registry)
import '../../src/app/registry';
import {TemplatesTab} from '../../src/features/admin/AccessControl';
import {MsgMessagesMobile,MsgMessagesPage} from '../../src/features/messaging/Messages';
import {ChannelsPage} from '../../src/features/channels/Channels';
import {ChannelsMobile} from '../../src/features/channels/ChannelsMobile';
void navItems;
function ctx(s:any,uid:string,mobile:boolean,over:any={}):any{
  const user=s.users.find((u:any)=>u.id===uid)??s.users[0];
  const noop=()=>{};
  return {s,uid,user,m:metrics(s),page:'x',go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),act:()=>null,search:'',setSearch:noop,filter:'All',setFilter:noop,tab:'Arrivals',setTab:noop,
    arrivals:[],departures:[],pending:0,taskList:[],roomStatus:(r:any)=>roomStatus(s,r),switchUser:noop,fault:'none',setFault:noop,loadScenario:noop,resetScenario:noop,
    display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop,...over};
}
const html=(s:any,uid:string,mobile:boolean,el:any,over:any={})=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile,over)}>{el}</HccContext.Provider>);
export const templates=(s:any,uid:string)=>html(s,uid,false,<TemplatesTab/>);
export const messages=(s:any,uid:string,mobile:boolean)=>html(s,uid,mobile,mobile?<MsgMessagesMobile/>:<MsgMessagesPage/>);
export const channels=(s:any,uid:string,mobile:boolean,tab:string)=>html(s,uid,mobile,mobile?<ChannelsMobile/>:<ChannelsPage/>,{tab});
`;

export async function register({test,M,assert}){
  await mkdir('tests/.generated',{recursive:true});
  await writeFile('tests/.generated/wd5-entry.tsx',ENTRY);
  const load=async mode=>{
    await build({entryPoints:['tests/.generated/wd5-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:`tests/.generated/wd5-${mode}.mjs`,logLevel:'error',jsx:'automatic',
      define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':JSON.stringify(mode)},
      banner:{js:"import {createRequire as __wd5Req} from 'node:module';const require=__wd5Req(import.meta.url);"}});
    return import(new URL(`./.generated/wd5-${mode}.mjs`,import.meta.url).href);
  };
  const prod=await load('production'),demo=await load('prototype');
  const s=M.seed();
  const esc=t=>String(t).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;').replace(/'/g,'&#x27;');

  // ── D1: Restaurant template (the policy shape sync/admin.ts policyFromServer builds from GET /admin/roles; tested in sync-wd5-gaps.mjs) ──
  const pol=M.adminPolicy(s);
  const rst={'PERM-POS-001':{effect:'allow',scope:'P'},'PERM-POS-002':{effect:'allow',scope:'P'},'PERM-POS-003':{effect:'request',scope:'P'},'PERM-HR-004':{effect:'allow',scope:'O'}};
  const withRst={...s,policy:{...pol,templates:{...pol.templates,Restaurant:rst}}};
  const row=(h,id)=>{const i=h.indexOf(`<strong>${id}</strong>`);return i<0?'':h.slice(i,h.indexOf('</tr>',i));};
  const header=h=>h.slice(h.indexOf('<thead>'),h.indexOf('</thead>'));
  const tplProd=prod.templates(withRst,'owner'),tplProdNoRst=prod.templates(s,'owner'),tplDemo=demo.templates(s,'owner');
  test('[DEC-079 D1 D5] Access control (production): the Restaurant template is a column of the matrix with its server grants (POS-001 A, POS-002 A, POS-003 R, HR-004 A·O)',()=>{
    assert.match(header(tplProd),/>Restaurant<\/button><\/th><\/tr>$/,'last column');
    assert.match(tplProd,/<option>Restaurant<\/option>/,'and in the role picker');
    assert.deepEqual(M.adminTemplateRoles(withRst.policy).slice(-2),['Tech admin','Restaurant']);
    assert.deepEqual(M.adminServerPermIds(withRst.policy),['PERM-HR-004','PERM-POS-001','PERM-POS-002','PERM-POS-003']);
    for(const [id,code] of [['PERM-POS-001','A'],['PERM-POS-002','A'],['PERM-POS-003','R'],['PERM-HR-004','A·O']]){
      const r=row(tplProd,id);assert.ok(r,id+' row');assert.ok(r.endsWith(`title="Restaurant: ${M.adminDescribeCell(rst[id])}">${code}</td>`),id+': '+r.slice(-120));
    }
    assert.match(row(tplProd,'PERM-POS-002'),/Restaurant POS · module permission \(server\)/);
    assert.match(row(tplProd,'PERM-HR-004'),/Payroll &amp; HR · module permission \(server\)/);
    assert.ok(row(tplProd,'PERM-FOL-001').endsWith('title="Restaurant: not granted">—</td>'),'no money for the Restaurant role');
  });
  test('[DEC-079 D1 D5] Access control (production): every server template is read-only (one note, selects and module boxes off); without an RST template the nine roles show as before',()=>{
    assert.ok(tplProd.includes('Role templates come from the server (role matrix v2, DEC-079) and are read-only here'));
    assert.ok(!/<select aria-label="PERM-[A-Z]+-\d+ effect for Reception"(?![^>]*disabled)/.test(tplProd),'effect selects disabled');
    assert.ok(!/<input type="checkbox"(?![^>]*disabled)/.test(tplProd),'module boxes disabled');
    assert.ok(!header(tplProdNoRst).includes('Restaurant'));
    assert.ok(!tplProdNoRst.includes('module permission (server)'));
  });
  test('[DEC-079 D1 D5] Access control (demo build): unchanged — nine roles, the v1 matrix only, templates editable by the Owner',()=>{
    assert.ok(!header(tplDemo).includes('Restaurant'));
    assert.equal((header(tplDemo).match(/<th>/g)||[]).length,10,'Permission + nine roles');
    assert.ok(!tplDemo.includes('module permission (server)'));
    assert.ok(!tplDemo.includes('read-only here'));
    assert.match(tplDemo,/<select aria-label="PERM-RES-001 effect for Reception">/,'Reception template editable');
  });

  // ── D4: phone Messages Templates tab ──
  const chips=h=>[...h.matchAll(/<div class="m-chips" role="group" aria-label="Messages section">(.*?)<\/div>/g)].flatMap(m=>[...m[1].matchAll(/<button[^>]*?(aria-pressed="(true|false)")[^>]*>([A-Za-z]+)/g)].map(b=>(b[2]==='true'?'*':'')+b[3]));
  const tadPhone=prod.messages(s,'techadmin',true),ownPhone=prod.messages(s,'owner',true),tadDemo=demo.messages(s,'techadmin',true),recDemo=demo.messages(s,'reception',true);
  const names=M.msgTemplates(s).map(t=>t.name);
  test('[DEC-079 D4 D5] Phone Messages (production, Tech admin): Templates and Triggers only, opening on Templates with every template and an Edit action',()=>{
    assert.deepEqual(chips(tadPhone),['Triggers','*Templates']);
    for(const n of names)assert.ok(tadPhone.includes(esc(n)),n);
    assert.equal((tadPhone.match(/> Edit<\/span>/g)||[]).length,names.length);
    assert.ok(tadPhone.includes('Tap a template to edit it.'));
    assert.ok(!tadPhone.includes('Compose'),'no sending');
  });
  test('[DEC-079 D4 D5] Phone Messages: other users keep Outbox first and gain a Templates list (view only without template rights); the demo Tech admin is unchanged in reach',()=>{
    assert.deepEqual(chips(ownPhone),['*Outbox','Triggers','Templates','Consent']);
    assert.deepEqual(chips(tadDemo),['*Outbox','Triggers','Templates','Consent'],'demo: the Tech admin is not templates-only (matrix v1 baseline, D6)');
    assert.deepEqual(chips(recDemo),['*Outbox','Triggers','Templates','Consent']);
    assert.ok(!recDemo.includes('Tap a template'),'templates tab not open by default');
    const desk=prod.messages(s,'techadmin',false);
    assert.match(desk,/data-tab="templates" aria-selected="true"[^>]*class="active">Templates/,'desktop parity: Templates first for the Tech admin (FIT: FitTabs markup)');
  });

  // ── D7: commission rate withheld ──
  const hidden={...s,channelsX:{...s.channelsX,channels:s.channelsX.channels.map(c=>({...c,commissionBps:0,commissionHidden:true}))}};
  const deskHidden=prod.channels(hidden,'reception',false,'channels'),deskHiddenCom=prod.channels(hidden,'reception',false,'commission');
  const deskShown=prod.channels(s,'owner',false,'channels'),deskDemo=demo.channels(s,'reception',false,'channels');
  const phoneHidden=prod.channels(hidden,'reception',true,'channels'),phoneShown=prod.channels(s,'owner',true,'channels');
  test('[DEC-079 D7 D5] OTA channels (production, rate withheld): no rate on the cards, no "Commission due", no commission report — desktop and phone',()=>{
    assert.equal(M.chnCommissionHidden(hidden),true);assert.equal(M.chnCommissionHidden(s),false);
    assert.ok(deskHidden.includes('<span>Commission</span><strong>Not shown to your role</strong>'));assert.ok(!/<span>Commission<\/span><strong>[\d.]+%/.test(deskHidden));
    assert.ok(phoneHidden.includes('Commission Not shown to your role · markup'));assert.ok(!/Commission [\d.]+%/.test(phoneHidden));
    assert.ok(!deskHidden.includes('Commission due'));assert.ok(!deskHidden.includes('Commission report'));
    assert.ok(!deskHiddenCom.includes('Commission owed to each channel'),'a commission tab link falls back to the inbox');
    assert.ok(!/>Commission</.test(phoneHidden),'no Commission chip');
  });
  test('[DEC-079 D7 D5] OTA channels: with the rate (Owner, Manager, Accountant) and in the demo build the rate, "Commission due" and the report stay',()=>{
    for(const h of [deskShown,deskDemo]){assert.ok(h.includes('18%'));assert.ok(h.includes('Commission due'));assert.ok(h.includes('Commission report'));assert.ok(!h.includes('Not shown to your role'));}
    assert.ok(phoneShown.includes('Commission 18%'));assert.match(phoneShown,/>Commission</);
  });
}

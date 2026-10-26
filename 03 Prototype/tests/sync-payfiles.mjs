// W4 gap G2: payroll files and the WhatsApp payslip flag in the production build.
// - The shared HTTP transport gets a text GET (getText): CSV bodies come back as text, errors are still the server's JSON error.
// - Production downloads the register, PF / ESIC / PT statement and bank file the SERVER builds (PayrollEndpoints.cs) and saves them
//   through the native saveCsv hook; the browser never builds a bank file from the masked staff master.
// - GET /payroll/staff carries phoneUsable (WhatsApp send without unmasking) and the IFSC (so it can be cleared: SaveStaff ifsc "").
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {loadSync,fakeServer,startRuntime,reply,settle} from './_sync-harness.mjs';

const BANK_CSV="Sr,Name,Staff ID,Bank,Account number,IFSC,Net pay (Rs)\r\n1,Ravi Shinde,105,Demo Bank,'123456789012,DEMO0000105,15423.00\r\n";
const REGISTER_CSV='Month,Staff ID,Name\r\n2026-08,105,Ravi Shinde\r\n';
const STAT_CSV='Month,Staff ID,PF\r\n2026-08,105,1800.00\r\n';
const board={businessDate:'2026-10-24',from:'2026-10-01',to:'2026-10-31',allStaff:true,payroll:true,departments:[{code:'HK',name:'Housekeeping',leaveCap:null}],
  leavePolicy:{cl:12,sl:7,el:15,defaultCap:2},roster:[],attendance:[],leaves:[],rateVersions:[],runs:[],advances:[],fnf:[]};
const staffRow=(o={})=>({staffId:105,name:'Ravi Shinde',dept:'HK',designation:'Room attendant',defaultShift:'M',weeklyOff:5,joined:'2024-04-01',exitedOn:null,active:true,
  linked:true,phoneMasked:'••••0010',version:'v-105',payType:'monthly',monthlyPaise:1800000,dailyPaise:null,gender:'M',structure:'S',pfApplicable:true,esicApplicable:true,
  bankName:'Demo Bank',accountMasked:'••••9012',linkedUserId:6,phoneUsable:true,ifsc:'DEMO0000105',...o});

/** HRPayslipModal rendered server-side in one build flavour (production / prototype). */
async function payslipRenderer(flavour){
  const dir='tests/.generated';await mkdir(dir,{recursive:true});
  await writeFile(dir+'/payslip-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {HRPayslipModal} from '../../src/features/staff/modals';
export function render(s:any,uid:string,id:string){const noop=()=>{};
  return renderToStaticMarkup(<HccContext.Provider value={{s,uid,closeModal:noop,run:async()=>false,busy:false,toast:noop,open:noop,setDirty:noop} as any}><HRPayslipModal id={id}/></HccContext.Provider>);}
`);
  const out=`${dir}/payslip-${flavour}.mjs`;
  await build({entryPoints:[dir+'/payslip-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:out,logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':`"${flavour}"`},
    banner:{js:"import {createRequire as __spReq} from 'node:module';const require=__spReq(import.meta.url);"}});
  return import(new URL('../'+out,import.meta.url).href+'?'+flavour);
}

export async function register({test,assert,M}){
  const S=await loadSync();

  // ── Files from the server ──
  const srv=fakeServer({
    'GET /payroll/runs/:month/register.csv':()=>REGISTER_CSV,
    'GET /payroll/runs/:month/statutory.csv':()=>STAT_CSV,
    'GET /payroll/run/:runId/bank-files/latest':({params})=>params.runId==='501'?BANK_CSV:reply(409,{code:'INVALID_TRANSITION',message:'The bank file is made after the pay-run is locked.'}),
  });
  const t=S.createHttpTransport({fetch:srv.fetch,timeoutMs:2000});
  const saved=[];const save=async(name,csv)=>{saved.push({name,csv});return 'downloaded';};
  const run={id:'501',month:'2026-08'};
  const names=[await S.downloadServerPayrollFile(t,'register',run,save),await S.downloadServerPayrollFile(t,'statutory',run,save),
    await S.downloadServerPayrollFile(t,'bank',run,save,2)];
  const calls=srv.gets();
  const locked=await settle(()=>S.downloadServerPayrollFile(t,'bank',{id:'502',month:'2026-09'},save));
  const badId=await settle(()=>S.downloadServerPayrollFile(t,'bank',{id:'HR-RUN-1',month:'2026-09'},save));
  test('[W4 G2] Production payroll files come from the server routes (register, PF / ESIC / PT, latest bank file) and are saved as sent',()=>{
    assert.deepEqual(calls.map(c=>c.path),['/payroll/runs/2026-08/register.csv','/payroll/runs/2026-08/statutory.csv','/payroll/run/501/bank-files/latest']);
    assert.deepEqual(names,['payroll-register-2026-08.csv','pf-esic-pt-2026-08.csv','bank-transfer-2026-08-2.csv']);
    assert.deepEqual(saved.map(f=>f.csv),[REGISTER_CSV,STAT_CSV,BANK_CSV]);
    assert.ok(saved[2].csv.includes("'123456789012")&&!saved[2].csv.includes('••••'),'the bank file carries the server’s account numbers, never masked ones');
    for(const c of calls){assert.match(c.headers.Accept,/^text\/csv/);assert.equal(c.key,undefined,'a file GET has no Idempotency-Key');assert.equal(c.body,undefined);}
    assert.equal(saved.length,3,'nothing saved when the server refuses');
    assert.equal(locked.ok,false);assert.equal(locked.error.wireCode,'INVALID_TRANSITION');assert.match(locked.error.message,/after the pay-run is locked/);
    assert.equal(badId.ok,false);assert.equal(badId.error.wireCode,'VALIDATION');
  });

  // getText: the server's file name from Content-Disposition when the response has headers (browser fetch); network down → read-only.
  const withHeaders=S.createHttpTransport({fetch:async()=>({status:200,ok:true,text:async()=>BANK_CSV,
    headers:{get:n=>({'content-disposition':'attachment; filename=bank-transfer-2026-08-3.csv; filename*=UTF-8\'\'bank-transfer-2026-08-3.csv','content-type':'text/csv; charset=utf-8'})[n.toLowerCase()]??null}})});
  const fileH=await withHeaders.getText('/payroll/run/501/bank-files/latest');
  const nameH=await S.downloadServerPayrollFile(withHeaders,'bank',run,async()=>undefined,9);
  const downSrv=fakeServer({});downSrv.down=true;const downT=S.createHttpTransport({fetch:downSrv.fetch});
  const down=await settle(()=>downT.getText('/payroll/runs/2026-08/register.csv'));
  const notJson=await settle(()=>S.createHttpTransport({fetch:async()=>({status:403,ok:false,text:async()=>'Forbidden'})}).getText('/x/y'));
  test('[W4 G2] Transport text GET: file name and type from the headers when present; network down → UNAVAILABLE + read-only; a non-JSON error body still maps by status',()=>{
    assert.deepEqual(fileH,{text:BANK_CSV,fileName:'bank-transfer-2026-08-3.csv',contentType:'text/csv; charset=utf-8'});
    assert.equal(nameH,'bank-transfer-2026-08-3.csv','the server’s name wins over the fallback');
    assert.equal(S.dispositionFileName('attachment; filename="pf-esic-pt-2026-08.csv"'),'pf-esic-pt-2026-08.csv');
    assert.equal(S.dispositionFileName(null),undefined);
    assert.equal(down.ok,false);assert.equal(down.error.wireCode,'UNAVAILABLE');assert.equal(downT.connection.readOnly,true);
    assert.equal(notJson.ok,false);assert.equal(notJson.error.wireCode,'FORBIDDEN');
  });

  // ── Staff master: phoneUsable + IFSC ──
  const mine={areas:S.AREAS.filter(a=>['core','reservations','frontdesk','staff'].includes(a.id))};
  const ssrv=fakeServer({'GET /payroll/staff':()=>[staffRow(),staffRow({staffId:106,name:'Imran Khan',phoneMasked:'(none)',phoneUsable:false,ifsc:null,linkedUserId:null,version:'v-106'})],
    'GET /payroll/board':()=>board,'POST /commands/SaveStaff':({body})=>({staffId:body.staffId,name:body.name,version:'v2',notes:'saved'})});
  const rt=await startRuntime(S,ssrv,mine);const x=rt.get().state.staffX;
  const base={type:'HR_STAFF_SAVE',id:'105',name:'Ravi Shinde',dept:'HK',designation:'Room attendant',defaultShift:'M',weeklyOff:5,userId:'6',phone:'••••0010',gender:'M',
    payType:'monthly',monthly:1800000,daily:0,esicApplicable:true,bankName:'Demo Bank',accountNo:'••••9012'};
  const keep=await rt.run({...base,ifsc:'demo0000105',key:'k-keep'});
  const clear=await rt.run({...base,ifsc:'  ',key:'k-clear'});
  const change=await rt.run({...base,ifsc:'demo0000999',key:'k-change'});
  const fresh=await rt.run({...base,id:'106',name:'Imran Khan',userId:undefined,phone:'',accountNo:'',bankName:'',ifsc:'',key:'k-none'});
  const bodies=ssrv.commands('SaveStaff').map(c=>c.body);
  test('[W4 G2] Staff loader keeps the mobile masked but carries phoneUsable and the IFSC; IFSC unchanged → left out, cleared → "" (server clears), changed → sent',()=>{
    const ravi=x.staff.find(s=>s.id==='105'),imran=x.staff.find(s=>s.id==='106');
    assert.equal(ravi.phone,'••••0010');assert.equal(ravi.phoneUsable,true);assert.equal(ravi.ifsc,'DEMO0000105');assert.equal(ravi.accountNo,'••••9012');
    assert.equal(imran.phoneUsable,undefined);assert.equal(imran.ifsc,undefined);assert.equal(imran.phone,'');
    assert.ok(keep.ok&&clear.ok&&change.ok&&fresh.ok,[keep,clear,change,fresh].map(r=>r.text).join(' | '));
    assert.ok(!('ifsc' in bodies[0])&&!('phone' in bodies[0])&&!('accountNo' in bodies[0]),JSON.stringify(bodies[0]));
    assert.equal(bodies[1].ifsc,'');assert.equal(bodies[2].ifsc,'DEMO0000999');assert.ok(!('ifsc' in bodies[3]),'blank and none before: nothing to clear');
    assert.equal(S.staffFromServer(staffRow({phoneUsable:undefined,ifsc:undefined})).phoneUsable,undefined,'an older server without the flag: button stays off');
  });

  // ── Payslip WhatsApp button: production uses the server flag; the prototype keeps its own number check ──
  const [prod,proto]=await Promise.all([payslipRenderer('production'),payslipRenderer('prototype')]);
  const s0=M.seed(),aug=s0.staffX.runs.find(r=>r.status==='Locked');
  const sid=aug.lines[0].staffId;
  const withStaff=(patch)=>({...s0,staffX:{...s0.staffX,staff:s0.staffX.staff.map(st=>st.id===sid?{...st,...patch}:st)}});
  const masked=withStaff({phone:'••••0010',phoneUsable:true}),maskedOff=withStaff({phone:'••••0010'});
  const btn=html=>{const m=/<button([^>]*)>((?:(?!<\/button>).)*Send on WhatsApp(?:(?!<\/button>).)*)<\/button>/s.exec(html);
    return m&&[m[0],/\bdisabled=""/.test(m[1])?'disabled':undefined];};
  const id=aug.id+'|'+sid;
  const h={prodOn:prod.render(masked,'accountant',id),prodOff:prod.render(maskedOff,'accountant',id),protoMasked:proto.render(masked,'accountant',id),protoReal:proto.render(s0,'accountant',id)};
  test('[W4 G2] WhatsApp payslip: production enables the send from phoneUsable without unmasking; the prototype still checks its own number',()=>{
    const on=btn(h.prodOn);assert.ok(on&&!on[1],'enabled in production with the flag');assert.match(on[0],/to ••••0010/);assert.ok(!h.prodOn.includes('+••••'));
    const off=btn(h.prodOff);assert.ok(off&&off[1],'disabled without the flag');assert.match(h.prodOff,/No usable mobile number/);
    const pm=btn(h.protoMasked);assert.ok(pm&&pm[1],'prototype ignores the production flag');
    const pr=btn(h.protoReal);assert.ok(pr&&!pr[1]);assert.match(pr[0],/to \+91\d{10}/);
  });
}

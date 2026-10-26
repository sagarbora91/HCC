// Test-only setup for W4 E2E run 2: property, owner, sample masters incl. POS, valid hotel GSTIN. Usage: node setup.mjs <creds.json outside repo>
import fs from 'node:fs';import {call,rnd} from './lib.mjs';
const SD='E:/Projects/HCC/worktrees/w4-e2e2/08 Production/deployment/import-templates/samples/';
const out={};
const ownerPw='Own-'+rnd(12);out.owner={login:'owner',password:ownerPw};
let r=await call('POST','/setup/SetupProperty',{name:'Hotel Sample Residency (fictional)',address:'1 Test Road, Latur',code:'MAIN',state:'Maharashtra',ownerLoginName:'owner',ownerDisplayName:'Test Owner',ownerPassword:ownerPw});
console.log('setup',r.status,JSON.stringify(r.body));
r=await call('POST','/auth/sign-in',{loginName:'owner',password:ownerPw});console.log('signin',r.status);const ck=r.cookie;
const up=async(master,file,asOf)=>{const u=await call('POST','/commands/UploadImportBatch',{master,fileName:file,csvText:fs.readFileSync(SD+file,'utf8'),asOfDate:asOf},{cookie:ck});
 console.log(master,'upload',u.status,JSON.stringify(u.body).slice(0,300));const id=u.body.batchId??u.body.result?.batchId??u.body.batch?.batchId;
 if(!id){return}const c=await call('POST','/commands/CommitImportBatch',{batchId:id},{cookie:ck});console.log(master,'commit',c.status,JSON.stringify(c.body).slice(0,200));};
for(const [m,f,a] of [['room-types','room-types.csv'],['rooms','rooms.csv'],['tariffs','tariffs.csv'],['seasons','seasons.csv'],['staff','staff.csv'],['ledgers','ledgers.csv'],['opening-balances','opening-balances.csv','2026-10-25'],['till-cash','till-cash.csv'],['guests','guests.csv'],['pos-outlets','pos-outlets.csv'],['pos-menu','pos-menu.csv']])await up(m,f,a);
const g=await call('POST','/commands/SetSetting',{key:'brand.gstin',value:'27AAAAA0000A1Z2',reason:'e2e setup'},{cookie:ck});console.log('gstin',g.status,JSON.stringify(g.body).slice(0,300));
fs.writeFileSync(process.argv[2],JSON.stringify(out,null,1));

import fs from 'node:fs';import {call} from './lib.mjs';
const c=JSON.parse(fs.readFileSync('creds.json'));
let r=await call('POST','/auth/sign-in',{loginName:'owner',password:c.owner.password});const ck=r.cookie;
const f=await call('GET','/reservations/1/folio',null,{cookie:ck});console.log(f.status,JSON.stringify(f.body).slice(0,200));
const i=await call('POST','/commands/IssueInvoice',{sourceKind:'folio',sourceId:1},{cookie:ck});console.log(i.status,JSON.stringify(i.body).slice(0,1500));

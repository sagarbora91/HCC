import fs from 'node:fs';import {call,rnd} from './lib.mjs';import {randomInt} from 'node:crypto';
const c=JSON.parse(fs.readFileSync(process.argv[2]));
let r=await call('POST','/auth/sign-in',{loginName:'owner',password:c.owner.password});const ck=r.cookie;
const u=await call('GET','/admin/users?pageSize=100',null,{cookie:ck});console.log(JSON.stringify(u.body).slice(0,700));
c.staff={};
for(const x of (u.body.items||u.body)){ if(x.loginName==='owner')continue;
 const pw=['meena.accounts'].includes(x.loginName);const secret=pw?'Acc-'+rnd(12):String(randomInt(100000,999999));
 const res=await call('POST','/commands/SetCredential',{userId:x.userId,password:pw?secret:null,pin:pw?null:secret,expectedVersion:x.version},{cookie:ck});
 console.log(x.loginName,res.status,JSON.stringify(res.body).slice(0,150));c.staff[x.loginName]={[pw?'password':'pin']:secret,userId:x.userId};}
const d=await call('POST','/commands/RegisterDevice',{label:'E2E browser'},{cookie:ck});console.log('dev',d.status,JSON.stringify(d.body).slice(0,80));c.deviceToken=d.body.deviceToken;
fs.writeFileSync(process.argv[2],JSON.stringify(c,null,1));

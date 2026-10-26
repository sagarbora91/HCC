import fs from 'node:fs';import {call} from './lib.mjs';
const c=JSON.parse(fs.readFileSync(process.argv[2]));
let r=await call('POST','/auth/sign-in',{loginName:'owner',password:c.owner.password});const ck=r.cookie;
const a=await call('POST','/commands/CreateOutlet',{code:'REST',name:'Restaurant',kind:'table',tables:['T1','T2']},{cookie:ck});console.log(a.status,JSON.stringify(a.body));
const b=await call('POST','/commands/CreateOutlet',{code:'RS',name:'Room service',kind:'room',tables:null},{cookie:ck});console.log(b.status,JSON.stringify(b.body));
const ids=[a.body.outletId,b.body.outletId];
for(const [code,name,cat,p,veg] of [['TEA','Masala tea','Beverages',6000,true],['PNR','Paneer butter masala','Main course',28000,true],['THL','Veg thali','Main course',22000,true]]){
 const m=await call('POST','/commands/SaveMenuItem',{itemId:null,code,name,category:cat,pricePaise:p,veg,outletIds:ids,expectedVersion:null},{cookie:ck});console.log(code,m.status,JSON.stringify(m.body).slice(0,100));}

// Test-only: owner sign-in then GET each path given as args (creds file first arg). Prints truncated JSON.
import fs from 'node:fs';import {call} from './lib.mjs';
const c=JSON.parse(fs.readFileSync(process.argv[2]));
const r=await call('POST','/auth/sign-in',{loginName:'owner',password:c.owner.password});
for(const p of process.argv.slice(3)){const x=await call('GET',p,null,{cookie:r.cookie});console.log(p,x.status,JSON.stringify(x.body).slice(0,+process.env.N||1500));}

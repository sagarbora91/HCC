// Test-only: owner signs in and POSTs one command. Usage: node cmd.mjs <creds.json> <CommandName> "<json with single quotes>"
import fs from 'node:fs';import {call} from './lib.mjs';
const c=JSON.parse(fs.readFileSync(process.argv[2]));
const r=await call('POST','/auth/sign-in',{loginName:'owner',password:c.owner.password});
const x=await call('POST','/commands/'+process.argv[3],JSON.parse((process.argv[4]||'{}').replace(/'/g,'"')),{cookie:r.cookie});
console.log(process.argv[3],x.status,JSON.stringify(x.body).slice(0,+process.env.N||800));

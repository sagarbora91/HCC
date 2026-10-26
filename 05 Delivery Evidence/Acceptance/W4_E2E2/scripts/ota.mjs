// Test-only: OTA channel save + connect + simulated inbound message (UI has no "add channel"). Usage: node ota.mjs <creds.json>
import fs from 'node:fs';import {call} from './lib.mjs';
const c=JSON.parse(fs.readFileSync(process.argv[2]));
const r=await call('POST','/auth/sign-in',{loginName:'owner',password:c.owner.password});const ck={cookie:r.cookie};
const s=await call('POST','/commands/SaveChannel',{channelId:null,expectedVersion:null,code:'DTH',name:'Demo Travel Hub (fictional)',commissionBps:1500,markupBps:500,mapping:[{roomTypeId:1,channelCode:'STD-BAR'},{roomTypeId:2,channelCode:'DLX-BAR'}]},ck);
console.log('save',s.status,JSON.stringify(s.body).slice(0,400));
const id=s.body.channelId??s.body.id;const ver=s.body.version;
const k=await call('POST','/commands/SetChannelStatus',{channelId:id,expectedVersion:ver,action:'connect'},ck);console.log('connect',k.status,JSON.stringify(k.body).slice(0,300));
const m=await call('POST','/commands/ImportChannelMessage',{message:{channelId:id,externalId:'EXT-9001',kind:'New',channelRef:'DTH-77001',guest:'Pooja Kale',phone:'9822000055',email:null,channelRoomCode:'DLX-BAR',arrival:'2026-11-02',departure:'2026-11-04',adults:2,children:0,amountPaise:1000000}},ck);console.log('import',m.status,JSON.stringify(m.body).slice(0,400));

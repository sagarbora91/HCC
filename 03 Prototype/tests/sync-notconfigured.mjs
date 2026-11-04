// A09-08: a Production server has no messaging gateway and no OCR engine. It refuses a send / scan with 503 UNAVAILABLE + details.reason
// NOT_CONFIGURED (feature 'messaging' | 'ocr') and a clear sentence, and records event-driven messages as "Not sent: Messaging not configured".
// The production client shows the server's own sentence where a send / scan is requested, does not treat it as a lost connection (no read-only
// switch, no retry), and the Messages screens carry the server's notice. The demo build keeps its simulated banner.
import {loadSync,fakeServer,startRuntime,reply} from './_sync-harness.mjs';
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';

const DIR='tests/.generated';
const MESSAGING='Messaging is not configured on this server: no message was sent.';
const OCR='ID scanning (OCR) is not configured on this server: enter the details by hand.';

/** Bundles a tiny entry for the given build flavour ('production' or 'demo') and imports it. */
async function bundle(name,entry,flavour){
  await mkdir(DIR,{recursive:true});
  const src=`${DIR}/${name}-${flavour}-entry.tsx`,out=`${DIR}/${name}-${flavour}.mjs`;
  await writeFile(src,entry);
  await build({entryPoints:[src],bundle:true,platform:'node',format:'esm',outfile:out,logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':JSON.stringify(flavour)},
    banner:{js:"import {createRequire as __ncReq} from 'node:module';const require=__ncReq(import.meta.url);"}});
  return import(new URL('../'+out,import.meta.url).href);
}

const BANNER=`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {MsgSimulated} from '../../src/features/messaging/shared';
export const banner=(messagingX:any)=>renderToStaticMarkup(<HccContext.Provider value={{s:{messagingX}} as any}><MsgSimulated/></HccContext.Provider>);
`;

export async function register({test,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','reservations','frontdesk','services'].includes(a.id))};
  const refuse=(feature,message)=>reply(503,{code:'UNAVAILABLE',message,details:{reason:'NOT_CONFIGURED',feature}});
  const routes=notConfigured=>({
    'GET /messaging/messages':()=>({items:[],total:0,page:1,pageSize:100,...(notConfigured===undefined?{}:{notConfigured})}),
    'GET /messaging/templates':()=>({templates:[],placeholders:[]}),
    'GET /messaging/rules':()=>[],
    'GET /messaging/consents':()=>({consents:[],guests:[]}),
    'POST /commands/SendMessage':()=>refuse('messaging',MESSAGING),
  });
  const off=fakeServer(routes(MESSAGING)),on=fakeServer(routes(null));
  const rtOff=await startRuntime(S,off,mine),rtOn=await startRuntime(S,on,mine);
  const send=await rtOff.run({type:'MSG_SEND',booking:'1201',channel:'WhatsApp',body:'Hello',key:'nc-send'});
  const scan=await rtOff.run({type:'MSG_DOC_EXTRACT',sample:'aadhaar-sample'});
  const posts=off.calls.filter(c=>c.method==='POST');
  const E=await bundle('nc-errors',`export {errorFromResponse,errorText,OCR_NOT_CONFIGURED,MESSAGING_NOT_CONFIGURED} from '../../src/clients/errors';`,'production');
  const prod=await bundle('nc-banner',BANNER,'production'),demo=await bundle('nc-banner',BANNER,'demo');

  test('[A09-08] The messages read carries the server\'s notice into messagingX; a configured server carries none',()=>{
    assert.equal(rtOff.get().state.messagingX.notConfigured,MESSAGING);
    assert.equal(rtOn.get().state.messagingX.notConfigured,undefined);
    assert.equal('notConfigured' in rtOn.get().state.messagingX,false);
  });

  test('[A09-08] A refused send shows the server\'s sentence, is not a lost connection and sends exactly one request',()=>{
    assert.equal(send.ok,false);assert.equal(send.reason,'server');
    assert.ok(send.text.includes(MESSAGING),send.text);
    assert.equal(send.error.wireCode,'NOT_AVAILABLE','a setup refusal is not "result unknown"');
    assert.equal(send.error.details.reason,'NOT_CONFIGURED');assert.equal(send.error.details.feature,'messaging');
    assert.equal(rtOff.connection.readOnly,false,'the server answered: the app must not switch to read-only');
    assert.equal(posts.filter(c=>c.path.endsWith('/SendMessage')).length,1,'not retried');
  });

  test('[A09-08] A scan request in the production app answers with the OCR sentence and sends nothing',()=>{
    assert.deepEqual([scan.ok,scan.reason,scan.text],[false,'unmapped',OCR]);
    assert.equal(posts.some(c=>/Capture|IdDoc/i.test(c.path)),false);
    assert.equal(S.UNMAPPED_TEXT.MSG_DOC_EXTRACT,OCR);
  });

  test('[A09-08] The wire error of the OCR refusal keeps the server\'s sentence too (feature ocr)',()=>{
    const e=E.errorFromResponse(503,{code:'UNAVAILABLE',message:OCR,details:{reason:'NOT_CONFIGURED',feature:'ocr'}});
    assert.equal(e.wireCode,'NOT_AVAILABLE');assert.equal(e.message,OCR);assert.equal(e.resultUnknown,false);
    assert.equal(E.OCR_NOT_CONFIGURED,OCR);assert.equal(E.MESSAGING_NOT_CONFIGURED,MESSAGING);
    assert.equal(E.errorText(e),'Not available: '+OCR);
  });

  test('[A09-08] The production Messages screens show the server\'s notice; the demo build keeps its simulated banner',()=>{
    const withNotice=prod.banner({notConfigured:MESSAGING}),without=prod.banner({});
    assert.ok(withNotice.includes(MESSAGING)&&withNotice.includes('Messaging is off'),withNotice);
    assert.equal(without,'','a configured server shows no banner in the production build');
    const sim=demo.banner({notConfigured:MESSAGING});
    assert.ok(sim.includes('Simulated.')&&!sim.includes(MESSAGING),'the demo build is unchanged');
  });

  // AF-SMALL (audit items 24 / 25): every command that no area maps has words, and sends nothing.
  const AF_KEYS=['RPT_RUN_DUE','TRC_IMPORT','AI_ASK','AI_CLEAR','AI_SETTINGS','KEY_AGENT_STEP','KEY_CLOCK','KEY_SIM','KEY_ENCODER','SHIFT_CLOSE','STAY_CHECKOUT_BASIS','QUO_EXPIRE','NET_TICK','DIGEST_REDRAFT','GRP_SWEEP','HALL_NIGHT_RUN'];
  const afBefore=off.calls.length;
  const afAsk=await rtOff.run({type:'AI_ASK',text:'hello'});
  const afSent=off.calls.length-afBefore;
  test('[AF-SMALL] every unmapped command in audit items 24/25 has its own text; AI_ASK is refused as unmapped with that text and sends 0 requests',()=>{
    for(const k of AF_KEYS){assert.equal(typeof S.UNMAPPED_TEXT[k],'string',k);assert.ok(S.UNMAPPED_TEXT[k].trim().length>10,k);}
    assert.ok(S.UNMAPPED_TEXT.MSG_DOC_EXTRACT===OCR,'MSG_DOC_EXTRACT kept');
    assert.deepEqual([afAsk.ok,afAsk.reason,afAsk.text],[false,'unmapped',S.UNMAPPED_TEXT.AI_ASK]);
    assert.equal(afSent,0);
  });
}

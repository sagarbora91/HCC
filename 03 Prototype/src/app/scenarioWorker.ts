// Web Worker entry (audit P-01): builds a scenario (FY26 history takes 0.7 s at 1x, 6-20 s on a throttled tablet) off the main
// thread. build.mjs bundles this file separately and inlines it as a string (__HCC_SCENARIO_WORKER__), so the app stays one
// self-contained HTML; app/scenarioGen.ts starts it from a Blob URL. Output is the same deterministic loadScenario() result,
// sent as one JSON string per top-level slice (cheaper to send and to parse than a structured clone of the 8 MB object).
import {loadScenario} from '../fixtures/scenarios';
import {setHistoryProgress} from '../fixtures/history/generate';
export type WorkerRequest={id:string};
export type WorkerReply=
  |{type:'progress';step:string;done:number}
  |{type:'done';order:string[];slices:Record<string,string>;scalars:Record<string,unknown>}
  |{type:'error';message:string};
const post=(m:WorkerReply)=>(self as unknown as {postMessage(m:WorkerReply):void}).postMessage(m);
self.onmessage=(e:MessageEvent<WorkerRequest>)=>{
  try{
    setHistoryProgress((step,done)=>post({type:'progress',step,done}));
    const s=loadScenario(e.data.id) as unknown as Record<string,unknown>;
    setHistoryProgress(null);
    post({type:'progress',step:'Preparing records',done:0.93});
    const order=Object.keys(s),slices:Record<string,string>={},scalars:Record<string,unknown>={};
    for(const k of order){const v=s[k];if(v!==null&&typeof v==='object')slices[k]=JSON.stringify(v);else scalars[k]=v;}
    post({type:'done',order,slices,scalars});
  }catch(err){post({type:'error',message:String((err as Error)?.message??err)});}
};

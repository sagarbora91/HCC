// Builds a scenario without blocking the main thread (audit P-01). Uses the inlined scenario worker when the build provides
// it and the browser can start a Blob worker; otherwise (tests, Node, blocked workers) it builds on the main thread after
// letting the loading screen paint. Either way the result is the deterministic loadScenario(id) state.
import type {State} from '../services/mock';
import {loadScenario} from '../services/mock';
import {rememberJson} from './jsonCache';
import type {WorkerReply} from './scenarioWorker';
export type GenProgress={step:string;done:number};
declare const __HCC_SCENARIO_WORKER__:string;
/** The worker bundle text injected by build.mjs (esbuild define); '' when not built that way. Referenced once (inlined). */
function workerSource():string{try{return __HCC_SCENARIO_WORKER__;}catch{return '';}}
/** Scenarios built in the worker; the small ones are faster to build in place. */
const WORKER_SCENARIOS=['FY26'];

function inWorker(id:string,onProgress?:(p:GenProgress)=>void):Promise<State>|null{
  const src=workerSource();
  if(!src||typeof Worker==='undefined'||typeof Blob==='undefined'||typeof URL==='undefined'||!URL.createObjectURL)return null;
  let url='',w:Worker;
  try{url=URL.createObjectURL(new Blob([src],{type:'text/javascript'}));w=new Worker(url);}
  catch{if(url)URL.revokeObjectURL(url);return null;}
  return new Promise<State>((resolve,reject)=>{
    const end=()=>{w.terminate();URL.revokeObjectURL(url);};
    w.onmessage=(e:MessageEvent<WorkerReply>)=>{
      const m=e.data;
      if(m.type==='progress'){onProgress?.({step:m.step,done:m.done});return;}
      end();
      if(m.type==='error'){reject(new Error(m.message));return;}
      onProgress?.({step:'Opening the records',done:0.96});
      const s:Record<string,unknown>={};
      for(const k of m.order){
        if(k in m.slices){const json=m.slices[k],v=JSON.parse(json);rememberJson(v,json);s[k]=v;}
        else s[k]=m.scalars[k];
      }
      resolve(s as unknown as State);
    };
    w.onerror=e=>{e.preventDefault?.();end();reject(new Error(e.message||'Scenario worker failed'));};
    w.postMessage({id});
  });
}
const paint=()=>new Promise<void>(r=>setTimeout(r,30));
/** Builds scenario `id`; falls back to the main thread if the worker cannot start or fails. */
export async function generateScenario(id:string,onProgress?:(p:GenProgress)=>void):Promise<State>{
  if(WORKER_SCENARIOS.includes(id)){
    const viaWorker=inWorker(id,onProgress);
    if(viaWorker){try{return await viaWorker;}catch{/* fall back to the main thread below */}}
  }
  onProgress?.({step:'Generating demo records',done:0.1});
  await paint();
  return loadScenario(id);
}

// Training driver (wave TRN), prototype build only. The app shell hands its live context to the driver through this tiny module;
// nothing here is reached from the production flavour (callers guard with process.env.HCC_BUILD, build.mjs defines it).
import type {Ctx} from '../../context';
export type TrainingHooks={ctx:Ctx;signedIn:boolean;signIn:(id:string)=>void};
let hooks:TrainingHooks|null=null;
/** Called on every App render (prototype flavour): the driver always reads the latest context, never a stale closure. */
export function setTrainingHooks(h:TrainingHooks){hooks=h;}
export const getTrainingHooks=()=>hooks;
/** True when the page was opened with ?training=1 (the prototype flavour only; saving is switched off then). */
export const trainingMode=():boolean=>{try{return new URLSearchParams(location.search).has('training');}catch{return false;}};

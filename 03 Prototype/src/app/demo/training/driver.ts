// Training driver (wave TRN): window.TRAINING.{replay,resolve,state}. Prototype flavour only, ?training=1 only.
// Contract: C:\Users\Sagar\.claude\skills\training-media\reference\driver-interface.md; plan 02 Planning\HCC_Training_Media_Plan_TRN_2026-11-07.md §7.
// Every act goes through the real UI (clicks, typing, key events on the rendered page); the only direct calls are the app shell's own
// switchUser / loadScenario / go (the same functions the demo toolbar and tiles call). Saving is switched off in training mode (storage.ts).
import {getTrainingHooks} from './hooks';
import {resolveTarget,topDialog,norm} from './targets';
import {parseCatPage} from '../../navmap';
import {posOpenOrders,posUnsent,drawer,totals} from '../../../services/mock';
import {parseAct,parseKey,parseMenuPath,type Act} from './acts';
type RawStep={id:string;auto?:string[];check?:string;target?:string;allowError?:boolean};
type Flat={id:string;chapter:number;first:boolean;uid:string;display:string;auto:string[];check:string;target:string;allowError:boolean};
const sleep=(ms:number)=>new Promise<void>(r=>setTimeout(r,ms));
const frames=(n=2)=>new Promise<void>(r=>{const f=(k:number)=>k<=0?r():requestAnimationFrame(()=>f(k-1));f(n);});
const params=()=>new URLSearchParams(location.search);
let flat:Flat[]|null=null,cur=-1,loading:Promise<void>|null=null;
const log:string[]=[];
async function loadSteps(){
  if(flat)return;
  if(!loading)loading=(async()=>{
    const url=params().get('steps')||'/training/steps.json';
    const r=await fetch(url,{cache:'no-store'});if(!r.ok)throw new Error(`steps not found at ${url} (${r.status})`);
    const j=await r.json() as {chapters:{id:number;uid?:string;display?:string;steps:RawStep[]}[]};
    const out:Flat[]=[];
    for(const c of j.chapters)c.steps.forEach((s,i)=>out.push({id:s.id,chapter:c.id,first:i===0,uid:c.uid||'reception',display:c.display||'',auto:s.auto||[],check:s.check||'',target:s.target||'',allowError:!!s.allowError}));
    flat=out;})();
  await loading;
}
async function hooks(){
  for(let i=0;i<480;i++){const h=getTrainingHooks();if(h)return h;await sleep(250);}
  throw new Error('app never became ready');
}
const ctx=()=>getTrainingHooks()!.ctx;
async function settle(ms=70){await frames(2);await sleep(ms);for(let i=0;i<80&&ctx().busy;i++)await sleep(100);await frames(1);}
async function until(fn:()=>boolean,ms=5000,what='condition'){
  const t0=Date.now();for(;;){let ok=false;try{ok=!!fn();}catch{ok=false;}if(ok)return;if(Date.now()-t0>ms)throw new Error('timeout waiting for '+what);await sleep(60);}
}
function need(target:string,why:string):HTMLElement{
  const el=resolveTarget(target);if(!el)throw new Error(`target not found: ${target} (${why})`);return el;
}
function setValue(el:HTMLElement,v:string){
  const proto=el instanceof HTMLTextAreaElement?HTMLTextAreaElement.prototype:el instanceof HTMLSelectElement?HTMLSelectElement.prototype:HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(proto,'value')!.set!.call(el,v);
  el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));
}
function realClick(el:HTMLElement){if((el as HTMLButtonElement).disabled||el.getAttribute('aria-disabled')==='true')throw new Error('control is disabled: '+(el.textContent||el.getAttribute('aria-label')||'').trim().slice(0,60));el.scrollIntoView({block:'nearest',inline:'nearest'});el.focus?.();el.click();}
function keyEvent(spec:string){
  const k=parseKey(spec);const init={...k,bubbles:true,cancelable:true};
  const t:EventTarget=document.activeElement??document.body; // bubbles to document and window, where the app listens
  t.dispatchEvent(new KeyboardEvent('keydown',init));t.dispatchEvent(new KeyboardEvent('keyup',init));
}
const ready=async(target:string)=>until(()=>!!resolveTarget(target),4000,'target '+target);
/** "Time passes": the hotel closes the business day exactly as the Day closing page does, through the app's own run() path with the
 *  commands the page sends (REGISTER+CHECKIN the pending arrivals, approve pending expenses, settle open restaurant checks, NIGHT_POST,
 *  SHIFT_CLOSE, DAY_CLOSE, NEXT_DAY, SHIFT_OPEN). Used between chapters so a guest can leave on the booked date; logged in TRAINING_NOTES. */
async function closeDay(){
  const back=ctx().uid;
  const go=async(uid:string,cmd:Record<string,unknown>)=>{
    if(ctx().uid!==uid){ctx().switchUser(uid);await settle(120);}
    const ok=await ctx().run(cmd as never);await settle(80);
    if(!ok){const t=document.querySelector('.toast.error');throw new Error('closeday: '+cmd.type+' refused: '+(t?.textContent||'').trim().slice(0,200));}
  };
  const st=()=>ctx().s;
  for(const b of st().bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=st().date)){await go('owner',{type:'REGISTER',id:b.id});await go('owner',{type:'CHECKIN',id:b.id});}
  for(const e of st().expenses.filter(e=>e.status==='Pending'))await go('manager',{type:'EXPENSE_APPROVE',id:e.id,approve:true});
  for(const o of posOpenOrders(st())){
    if(posUnsent(o).length)await go('cashier',{type:'POS_KOT_SEND',id:o.id});
    await go('cashier',{type:'POS_SETTLE',id:o.id,mode:'outlet',method:'UPI',reference:'DEMO-UPI-CLOSE-'+o.id});
  }
  await go('owner',{type:'NIGHT_POST'});
  if(!st().shiftOpen)await go('cashier',{type:'SHIFT_OPEN',handover:st().openingCash});
  await go('owner',{type:'SHIFT_CLOSE',counted:drawer(st())});
  await go('owner',{type:'DAY_CLOSE'});
  await go('owner',{type:'NEXT_DAY'});
  await go('cashier',{type:'SHIFT_OPEN',handover:st().openingCash});
  if(ctx().uid!==back){ctx().switchUser(back);await settle(120);}
  document.querySelectorAll<HTMLElement>('[aria-label="Dismiss message"]').forEach(b=>b.click());
}
/** Visible inline alerts (form errors) now on the page; a new one after an act means the form refused the input. */
const alerts=()=>Array.from(document.querySelectorAll<HTMLElement>('[role="alert"]')).filter(e=>e.getClientRects().length>0).map(e=>(e.textContent||'').trim()).filter(t=>t&&!/^(Choose |Card expires )/.test(t)); // hints that are on screen by design, not refusals
let fresh:string[]=[];
async function doAct(a:Act){
  const c=ctx();const seen=new Set(alerts());
  switch(a.kind){
    case 'nav':c.go(a.arg);break;
    case 'closeday':log.push('closeday '+c.s.date);await closeDay();break;
    case 'user':c.switchUser(a.arg);break;
    case 'signout':c.signOut();break;
    case 'click':await ready(a.arg);realClick(need(a.arg,'click'));break;
    case 'hover':{const el=need(a.arg,'hover');el.dispatchEvent(new MouseEvent('mouseover',{bubbles:true}));el.dispatchEvent(new MouseEvent('mouseenter'));break;}
    case 'focus':need(a.arg,'focus').focus();break;
    case 'scroll':need(a.arg,'scroll').scrollIntoView({block:'center'});break;
    case 'type':{await ready(a.arg);const el=need(a.arg,'type');el.focus();setValue(el,a.value!);break;}
    case 'typebal':{ // "typebal:<target>|<booking id>": type the folio balance of that booking (rupees) so the demo never guesses an amount
      await ready(a.arg);const b=c.s.bookings.find(x=>x.id===a.value);if(!b)throw new Error('no booking '+a.value);
      const el=need(a.arg,'typebal');el.focus();setValue(el,String(totals(b).balance/100));break;}
    case 'select':{await ready(a.arg);const el=need(a.arg,'select') as HTMLSelectElement;
      if(!(el instanceof HTMLSelectElement))throw new Error('not a <select>: '+a.arg);
      const w=norm(a.value);const o=Array.from(el.options).find(x=>norm(x.textContent)===w||norm(x.value)===w)??Array.from(el.options).find(x=>norm(x.textContent).includes(w));
      if(!o)throw new Error(`option "${a.value}" not in ${a.arg}`);setValue(el,o.value);break;}
    case 'check':case 'uncheck':{await ready(a.arg);const el=need(a.arg,'check') as HTMLInputElement;
      const on=el.type==='checkbox'||el.type==='radio'?el.checked:el.getAttribute('aria-checked')==='true';
      if(on!==(a.kind==='check'))realClick(el);break;}
    case 'key':keyEvent(a.arg);break;
    case 'menu':{ // through Modules Home tiles, exactly as a user does
      const [cat,page]=parseMenuPath(a.arg);
      if(c.page!=='home')c.go('home');
      await settle();
      await ready('menutop:'+cat);realClick(need('menutop:'+cat,'menu'));
      // a module with a single page opens that page straight away (e.g. Admin & Setup for Reception): nothing more to click
      if(page){await settle();await until(()=>!!resolveTarget('menuleaf:'+page)||!parseCatPage(ctx().page),4000,'tile '+page);if(parseCatPage(ctx().page))realClick(need('menuleaf:'+page,'menu'));}
      break;}
    case 'modal':c.open({type:a.arg});break;
    case 'wait':{if(/^\d+$/.test(a.arg))await sleep(Number(a.arg));else await until(()=>!!document.querySelector(a.arg.replace(/^sel:/,'')),8000,'wait '+a.arg);break;}
    case 'display':c.display.setMode(a.arg as 'auto'|'desktop'|'mobile');break;
    case 'js':{log.push('js: '+a.arg);await (new Function('ctx','q','qa','return (async()=>{'+a.arg+'})()') as (...x:unknown[])=>Promise<unknown>)(c,(s:string)=>document.querySelector(s),(s:string)=>Array.from(document.querySelectorAll(s)));break;}
    default:throw new Error('act not implemented: '+a.raw);
  }
  await settle();
  fresh=alerts().filter(t=>!seen.has(t));
}
async function checkOk(expr:string){
  if(!expr)return;
  const fn=new Function('ctx','S','q','qa','text','has','return ('+expr+')') as (...x:unknown[])=>boolean;
  const q=(s:string)=>document.querySelector(s),qa=(s:string)=>Array.from(document.querySelectorAll(s));
  await until(()=>{const c=ctx();return fn(c,c.s,q,qa,document.body.innerText,(t:string)=>norm(document.body.innerText).includes(norm(t)));},5000,'check '+expr);
}
async function resetAll(first:Flat){
  await hooks();const mode=first.display||params().get('display')||'desktop';
  try{ctx().prefs?.reset();}catch{/* optional */}
  ctx().display.setMode(mode as 'auto'|'desktop'|'mobile');
  await settle(150);
  await until(()=>!ctx().busy,60000,'idle');
  const toastGone=()=>{document.querySelectorAll<HTMLElement>('[aria-label="Dismiss message"]').forEach(b=>b.click());};
  const before=ctx().s; // loadScenario swaps the whole state object when the worker has finished
  await Promise.resolve((ctx().loadScenario as (id:string)=>unknown)('FY26'));
  await sleep(250);await until(()=>!ctx().busy&&ctx().s!==before&&ctx().s.scenario==='FY26',90000,'scenario FY26');
  await sleep(150);
  // the "scenario loaded" toast is only drawn once somebody is signed in, so sign in first, then remove the toast (no shot shows it)
  if(!getTrainingHooks()!.signedIn)getTrainingHooks()!.signIn('owner');
  await settle(200);toastGone();await sleep(200);toastGone();
  await until(()=>!document.querySelector('.toast'),3000,'toast dismissed').catch(()=>undefined);
  await settle(200);
}
/** The app answered an act with an error toast (a refused command): the step is wrong, not the app. */
function refusal(){const t=document.querySelector('.toast.error');if(t)throw new Error('the app refused the action: '+(t.textContent||'').trim().slice(0,200));
  if(fresh.length)throw new Error('the form shows an error: '+fresh[0].slice(0,200));}
async function runStep(f:Flat){
  const h=getTrainingHooks()!;
  if(f.first){ // chapter start: close anything left open, make sure the chapter's operator is signed in
    if(h.ctx.modal)h.ctx.closeModal();await settle();
    if(!getTrainingHooks()!.signedIn)getTrainingHooks()!.signIn(f.uid);
    else if(ctx().uid!==f.uid)ctx().switchUser(f.uid);
    if(ctx().page!=='home'){ctx().go('home');}
    await settle(120);
  }
  document.querySelectorAll<HTMLElement>('[aria-label="Dismiss message"]').forEach(b=>b.click()); // a toast from the previous step must not be mistaken for this step's answer
  for(const raw of f.auto){await doAct(parseAct(raw));if(!f.allowError)refusal();}
  await checkOk(f.check);
  if(!f.allowError)refusal();
}
export async function replay(id:string){
  await hooks();await loadSteps();
  const idx=flat!.findIndex(s=>s.id===id);if(idx<0)return {done:false,msg:null,err:'unknown step '+id};
  try{
    let from=cur+1;
    if(idx<=cur||cur<0){await resetAll(flat![0]);cur=-1;from=0;}
    for(let i=from;i<=idx;i++){
      try{await runStep(flat![i]);cur=i;}
      catch(e){cur=i;throw new Error(`${flat![i].id}: ${(e as Error).message}`);}
    }
    await settle(120);
    return {done:true,msg:null};
  }catch(e){return {done:false,msg:null,err:String((e as Error).message||e)};}
}
export function resolve(target:string):Element|null{return resolveTarget(target);}
export function state(){const h=getTrainingHooks();return {step:flat&&cur>=0?flat[cur].id:null,uid:h?.ctx.uid,page:h?.ctx.page,date:h?.ctx.s.date,clock:h?.ctx.s.clock,dialog:!!topDialog(),busy:h?.ctx.busy,toasts:Array.from(document.querySelectorAll('.toast')).map(t=>t.textContent),log};}
export function install(){
  /** act: run one act string through the same interpreter (authoring / exploration helper). */
  const act=async(raw:string)=>{try{await doAct(parseAct(raw));return {ok:true};}catch(e){return {ok:false,err:String((e as Error).message)};}};
  (window as unknown as {TRAINING:unknown}).TRAINING={replay,resolve,state,act};
}

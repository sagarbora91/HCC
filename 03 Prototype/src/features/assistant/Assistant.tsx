// Module 26 · Management assistant (SIMULATED, rule-based) — desktop and phone pages plus shared parts.
// No AI service and no network: questions are matched to intents and answered by pure selectors (domain/streams/assistant.ts).
import React,{useEffect,useRef,useState} from 'react';
import {Sparkles,Send,Info,Trash2,RotateCcw,ArrowRight,Bot,Newspaper,Flag} from 'lucide-react';
import {useHcc} from '../../app/context';
import {PageTitle,clockTime,dateLabel} from '../../components/ui';
import {ChipFilter} from '../../app/mobile/components';
import {can,aiHistory,aiSuggestions,aiBriefing,aiFlags,aiDirtyHours,AI_LABEL,AI_QUESTION_MAX} from '../../services/mock';
import type {AIMessage,AIFlag,AIBriefing,AILink,State} from '../../services/mock';
import type {Permission} from '../../services/mock';

/** Page → permission, mirroring the nav gates (kept local to avoid an import cycle with app/nav). Unknown pages are not linked. */
const aiPagePerm:Record<string,Permission>={overview:'reports',rooms:'reservations',reservations:'reservations',calendar:'reservations',arrivals:'checkin',
  folios:'billing',cashier:'payments',approvals:'reports',expenses:'expenses',housekeeping:'housekeeping',closing:'close',reports:'reports',yield:'reports'};
const aiPageLabel:Record<string,string>={overview:'Overview',rooms:'Room board',reservations:'Reservations',calendar:'Calendar',arrivals:'Arrivals & check-in',folios:'Guests & folios',
  cashier:'Cashier shift',approvals:'Approvals & audit',expenses:'Expenses',housekeeping:'Housekeeping',closing:'Day closing',reports:'Reports',yield:'Tariff & yield'};
function useAllowedLinks(){const {s,uid}=useHcc();return (links:AILink[])=>links.filter(l=>aiPagePerm[l.page]&&can(s,uid,aiPagePerm[l.page]));}

/** Ask = AI_ASK with a key derived from the question and history length, so a double tap records once. */
function useAsk(){
  const {s,uid,run}=useHcc();const [pending,setPending]=useState(false);
  const ask=async(question:string)=>{
    const q=question.replace(/\s+/g,' ').trim();if(!q||pending)return false;
    setPending(true);
    const ok=await run({type:'AI_ASK',question:q,key:`AI_ASK:${uid}:${aiHistory(s,uid).length}:${q.toLowerCase()}`},'Answered (simulated)');
    setPending(false);return ok;
  };
  return {ask,pending};
}

function Sev({sev}:{sev:AIFlag['severity']}){return <span className={'ai-sev ai-sev-'+sev.toLowerCase()}>{sev}</span>;}

function AnswerBody({m,onAsk,busy}:{m:AIMessage;onAsk:(q:string)=>void;busy:boolean}){
  const {go}=useHcc();const allowed=useAllowedLinks();const a=m.answer;const links=allowed(a.links);
  return <div className={'ai-answer'+(a.restricted?' restricted':'')}>
    <strong className="ai-answer-title">{a.title}</strong>
    <p className="ai-answer-text">{a.text}</p>
    {a.figures.length>0&&<dl className="ai-figs">{a.figures.map(f=><div key={f.label}><dt>{f.label}</dt><dd className="number">{f.value}</dd></div>)}</dl>}
    {a.items.length>0&&<ul className="ai-items">{a.items.map((x,i)=><li key={i}>{x}</li>)}</ul>}
    <details className="ai-how"><summary>How this was computed</summary><p>{a.how}</p></details>
    <div className="ai-answer-foot">
      <small>Snapshot at {clockTime(m.at)} · {dateLabel(m.date)} · intent “{a.intent}”</small>
      <div className="ai-links">
        {links.map(l=><button key={l.page} type="button" className="btn small-btn ai-go" onClick={()=>go(l.page)}>Go there · {aiPageLabel[l.page]||l.label}<ArrowRight size={13}/></button>)}
        <button type="button" className="btn small-btn subtle ai-again" disabled={busy} onClick={()=>onAsk(m.question)}><RotateCcw size={13}/>Ask again</button>
      </div>
    </div>
  </div>;
}

function Thread({history,onAsk,busy,phone}:{history:AIMessage[];onAsk:(q:string)=>void;busy:boolean;phone?:boolean}){
  const {s,uid}=useHcc();const end=useRef<HTMLDivElement>(null);
  useEffect(()=>{end.current?.scrollIntoView?.({block:'end'});},[history.length]);
  const sugg=aiSuggestions(s,uid);
  return <div className={'ai-thread'+(phone?' ai-thread-phone':'')} aria-live="polite" aria-label="Conversation">
    {!history.length&&<div className="ai-msg ai-bot"><span className="ai-avatar" aria-hidden><Bot size={16}/></span><div className="ai-bubble">
      <strong className="ai-answer-title">Hello — I answer questions about today’s demo data.</strong>
      <p className="ai-answer-text">I am a simulated, rule-based assistant: I match your words to {sugg.length} known topics and compute each answer from the current records. Pick a suggestion or type a question.</p>
    </div></div>}
    {history.map(m=><React.Fragment key={m.id}>
      <div className="ai-msg ai-me"><div className="ai-bubble">{m.question}</div></div>
      <div className="ai-msg ai-bot"><span className="ai-avatar" aria-hidden><Bot size={16}/></span><div className="ai-bubble"><AnswerBody m={m} onAsk={onAsk} busy={busy}/></div></div>
    </React.Fragment>)}
    <div ref={end} className="ai-end"/>
  </div>;
}

function Suggestions({onAsk,busy,phone}:{onAsk:(q:string)=>void;busy:boolean;phone?:boolean}){
  const {s,uid}=useHcc();
  return <div className={phone?'m-chips ai-chips-phone':'ai-chips'} role="group" aria-label="Suggested questions">
    {aiSuggestions(s,uid).map(t=><button key={t.intent} type="button" className={phone?'':'ai-chip'} disabled={busy} onClick={()=>onAsk(t.question)}>{t.label}</button>)}
  </div>;
}

function AskForm({onAsk,busy,phone}:{onAsk:(q:string)=>Promise<boolean>;busy:boolean;phone?:boolean}){
  const [q,setQ]=useState('');
  return <form className={phone?'ai-m-input':'ai-input'} onSubmit={async e=>{e.preventDefault();if(await onAsk(q))setQ('');}}>
    <label className="ai-sr" htmlFor={phone?'ai-q-m':'ai-q'}>Ask the assistant</label>
    <input id={phone?'ai-q-m':'ai-q'} value={q} maxLength={AI_QUESTION_MAX} onChange={e=>setQ(e.target.value)} placeholder="Ask e.g. “How full are we tomorrow?”" enterKeyHint="send" autoComplete="off" inputMode="text"/>
    <button type="submit" className="btn primary" disabled={busy||!q.trim()} aria-label="Ask"><Send size={16}/>{phone?null:'Ask'}</button>
  </form>;
}

export function AIBriefingCard({b,phone}:{b:AIBriefing;phone?:boolean}){
  const {go,s,uid}=useHcc();
  return <section className={'card ai-brief'+(phone?' ai-phone-card':'')} aria-label="Daily briefing">
    <div className="section-title"><div><h2><Newspaper size={16}/> Daily briefing</h2><p>{b.headline} · generated {clockTime(b.at)} (simulated clock)</p></div></div>
    {b.lines.length?<ul className="ai-brief-list">{b.lines.map(l=>{const ok=aiPagePerm[l.page]&&can(s,uid,aiPagePerm[l.page]);return <li key={l.id}>
      <div className="ai-brief-main"><span className={'ai-tone ai-tone-'+l.tone}>{l.tone==='ok'?'OK':l.tone==='warn'?'Attention':l.tone==='alert'?'Alert':'Info'}</span><span className="ai-brief-label">{l.label}</span><strong className="number">{l.value}</strong></div>
      {l.note&&<small>{l.note}</small>}
      {ok&&<button type="button" className="text-btn ai-brief-go" onClick={()=>go(l.page)}>Go there <ArrowRight size={12}/></button>}
    </li>;})}</ul>:<p className="padded">No briefing topics are available for your role.</p>}
  </section>;
}

export function AIFlagsCard({flags,s,phone}:{flags:AIFlag[];s:State;phone?:boolean}){
  const {go,uid}=useHcc();
  return <section className={'card ai-flags'+(phone?' ai-phone-card':'')} aria-label="Anomaly flags">
    <div className="section-title"><div><h2><Flag size={16}/> Anomaly flags</h2><p>Rule-based checks · dirty-room threshold {aiDirtyHours(s)} h on the simulated clock</p></div>{!phone&&<span className="pill">{flags.length}</span>}</div>
    {flags.length?<ul className="ai-flag-list">{flags.map(f=>{const ok=aiPagePerm[f.page]&&can(s,uid,aiPagePerm[f.page]);return <li key={f.id}>
      <div className="ai-flag-head"><Sev sev={f.severity}/><strong>{f.title}</strong></div>
      <p>{f.detail}</p>
      {ok&&<button type="button" className="text-btn" onClick={()=>go(f.page)}>Go there · {aiPageLabel[f.page]} <ArrowRight size={12}/></button>}
    </li>;})}</ul>:<p className="padded">No anomalies found in the data you can see.</p>}
  </section>;
}

function SimLabel(){return <div className="info-note ai-sim"><Info size={15}/><span><b>{AI_LABEL}.</b> No AI service is used and nothing leaves this device. Answers include only data your role may see.</span></div>;}

export function AssistantPage(){
  const {s,uid,run,busy}=useHcc();const {ask,pending}=useAsk();
  const history=aiHistory(s,uid);const b=aiBriefing(s,uid);const flags=aiFlags(s,uid);const off=busy||pending;
  return <div className="ai-page">
    <PageTitle title="Management assistant" sub="Ask plain questions about today’s operations and get the figures, how they were computed and where to act." action={<span className="ai-badge"><Sparkles size={14}/>Simulated · rule-based</span>}/>
    <SimLabel/>
    <div className="ai-layout">
      <section className="card ai-chat" aria-label="Assistant conversation">
        <div className="section-title"><div><h2>Conversation</h2><p>{history.length} of your questions · saved for your demo user only</p></div>
          {history.length>0&&<button type="button" className="btn small-btn" disabled={off} onClick={()=>run({type:'AI_CLEAR'},'Your assistant history was cleared')}><Trash2 size={13}/>Clear my history</button>}</div>
        <Thread history={history} onAsk={q=>{void ask(q);}} busy={off}/>
        <div className="ai-compose"><Suggestions onAsk={q=>{void ask(q);}} busy={off}/><AskForm onAsk={ask} busy={off}/></div>
      </section>
      <aside className="ai-side"><AIBriefingCard b={b}/><AIFlagsCard flags={flags} s={s}/></aside>
    </div>
  </div>;
}

type AIView='ask'|'brief'|'flags';
export function AssistantMobile(){
  const {s,uid,busy}=useHcc();const {ask,pending}=useAsk();const [view,setView]=useState<AIView>('ask');
  const history=aiHistory(s,uid);const flags=aiFlags(s,uid);const off=busy||pending;
  return <div className="ai-m">
    <p className="ai-m-label"><Sparkles size={14}/>{AI_LABEL}</p>
    <ChipFilter<AIView> label="Assistant view" value={view} onChange={setView} options={[{id:'ask',label:'Ask'},{id:'brief',label:'Briefing'},{id:'flags',label:'Flags',count:flags.length}]}/>
    {view==='ask'&&<>
      <Thread history={history} onAsk={q=>{void ask(q);}} busy={off} phone/>
      <div className="ai-m-dock">
        <Suggestions onAsk={q=>{void ask(q);}} busy={off} phone/>
        <AskForm onAsk={ask} busy={off} phone/>
      </div>
    </>}
    {view==='brief'&&<AIBriefingCard b={aiBriefing(s,uid)} phone/>}
    {view==='flags'&&<AIFlagsCard flags={flags} s={s} phone/>}
  </div>;
}

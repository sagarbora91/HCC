// Module 26 · AI management assistant — command handlers. Command types MUST start with 'AI_'. Return value = audit/summary text.
// Simulated assistant: AI_ASK computes a rule-based answer from the current state (no network) and records it in the caller's history.
import {clone} from '../../../domain/clone';
import type {Handler} from '../commands';
import {aiSlice,aiAnswer,aiMatchIntent,AI_HISTORY_MAX,AI_QUESTION_MAX,AI_DEFAULT_DIRTY_HOURS} from '../../../domain/streams/assistant';
export const handlers:Record<string,Handler>={
  /** Ask a question. Any enabled user; the answer is filtered by the caller's permissions. Idempotent via the command key. */
  AI_ASK:({s,uid,cmd,assert,id})=>{
    const q=typeof cmd.question==='string'?cmd.question.replace(/\s+/g,' ').trim():'';
    assert(q.length>=1,'Type a question, or pick one of the suggestions.');
    assert(q.length<=AI_QUESTION_MAX,`Keep the question under ${AI_QUESTION_MAX} characters.`);
    const intent=aiMatchIntent(q),answer=aiAnswer(s,uid,q,intent);
    const slice=clone(aiSlice(s));const history={...(slice.history||{})};
    const mine=[...(history[uid]||[]),{id:id('AIQ'),at:s.clock,date:s.date,question:q,answer}];
    history[uid]=mine.slice(-AI_HISTORY_MAX);
    s.assistantX={...slice,history};
    return `Assistant question (simulated) · ${intent}${answer.restricted?' · restricted for role':''}`;
  },
  /** Clear the caller's own conversation history. */
  AI_CLEAR:({s,uid})=>{
    const slice=clone(aiSlice(s));const n=slice.history?.[uid]?.length||0;
    s.assistantX={...slice,history:{...(slice.history||{}),[uid]:[]}};
    return `Assistant history cleared · ${n} message${n===1?'':'s'}`;
  },
  /** Anomaly thresholds (approve permission). */
  AI_SETTINGS:({s,cmd,req,assert})=>{
    req('approve');
    const h=Number(cmd.dirtyHours);
    assert(Number.isInteger(h)&&h>=1&&h<=48,'Choose a dirty-room threshold between 1 and 48 hours.');
    const slice=clone(aiSlice(s));
    const before=slice.settings?.dirtyHours??AI_DEFAULT_DIRTY_HOURS;
    s.assistantX={...slice,settings:{...(slice.settings||{}),dirtyHours:h}};
    return `Assistant dirty-room threshold ${before} h → ${h} h`;
  },
};

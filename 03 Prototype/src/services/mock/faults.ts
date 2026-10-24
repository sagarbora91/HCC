// Fault simulation for the next command (Prototype Spec §2, AC-022). Demo-only; removable with app/demo.
import {State,Command} from '../../domain/types';
import {HccError} from '../../domain/errors';
import {execute} from './commands';
export type FaultMode='none'|'latency'|'reject'|'lost'|'stale';
export const faultModes:{id:FaultMode;label:string;help:string}[]=[
  {id:'none',label:'No fault',help:'Commands behave normally.'},
  {id:'latency',label:'Slow response (2 s)',help:'The next action waits 2 seconds. Repeated clicks are ignored while it is pending.'},
  {id:'reject',label:'Server rejects',help:'The next action is rejected. Nothing changes.'},
  {id:'lost',label:'Lost response',help:'The next action is saved, but the confirmation is lost. Retrying the same action must not duplicate it.'},
  {id:'stale',label:'Stale data',help:'The next action is refused because the record changed elsewhere. Nothing changes.'},
];
export const LATENCY_MS=2000;
/** Executes a command under a fault mode. Returns the resulting state and, for faults, the error the user sees. */
export function executeWithFault(s:State,uid:string,cmd:Command,mode:FaultMode):{state:State;error?:HccError}{
  if(mode==='reject')return {state:s,error:new HccError('SIMULATED_REJECTION','Simulated rejection: the service refused this action. Nothing was changed.')};
  if(mode==='stale')return {state:s,error:new HccError('STALE_VERSION','Simulated stale data: this record changed on another device. Nothing was changed — review and try again.')};
  const next=execute(s,uid,cmd);
  if(mode==='lost')return {state:next,error:new HccError('RESULT_UNKNOWN','Simulated lost response: the result is unknown. Retry the same action — it will not be duplicated.')};
  return {state:next};
}

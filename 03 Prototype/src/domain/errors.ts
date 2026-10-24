// Typed errors returned by the mock service (Prototype Spec §6).
export type ErrorCode='FORBIDDEN'|'NOT_FOUND'|'NOT_AVAILABLE'|'INVALID_TRANSITION'|'VALIDATION'|'STALE_VERSION'|'DAY_CLOSED'|'SIMULATED_REJECTION'|'RESULT_UNKNOWN'|'RESTRICTED';
export class HccError extends Error{
  constructor(public readonly code:ErrorCode,message:string){super(message);this.name='HccError';}
}
export const errorTitle:Record<ErrorCode,string>={
  FORBIDDEN:'Not permitted',NOT_FOUND:'Record not found',NOT_AVAILABLE:'Not available',INVALID_TRANSITION:'Action not allowed now',
  VALIDATION:'Check the details',STALE_VERSION:'Record changed',DAY_CLOSED:'Business day closed',
  SIMULATED_REJECTION:'Simulated rejection',RESULT_UNKNOWN:'Result unknown',RESTRICTED:'Stay restriction',
};
export function codeOf(e:unknown):ErrorCode{return e instanceof HccError?e.code:'VALIDATION';}

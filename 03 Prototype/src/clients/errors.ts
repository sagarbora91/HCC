// Client error model (contracts §2): the server's {code,message,details?} body → the prototype's HccError codes.
// `code` stays a prototype ErrorCode so existing UI (errorTitle, codeOf) keeps working; `wireCode` keeps the server code.
import {HccError,errorTitle,type ErrorCode} from '../domain/errors';

/** Error codes on the wire — must match `08 Production/src/Hcc.Contracts/HccErrorCode.cs`. */
export const WIRE_CODES=['VALIDATION','FORBIDDEN','NOT_FOUND','NOT_AVAILABLE','RESTRICTED','INVALID_TRANSITION','STALE_VERSION','DAY_CLOSED','APPROVAL_REQUIRED','IDEMPOTENCY_MISMATCH','UNAVAILABLE'] as const;
export type WireCode=typeof WIRE_CODES[number];
/** Client-side only: a response that breaks the contract (bad JSON, unsafe integer, malformed date). Never sent by the server. */
export type ClientWireCode=WireCode|'PROTOCOL';

/** HTTP status per wire code — HccErrorCatalog.HttpStatus. */
export const WIRE_STATUS:Record<WireCode,number>={VALIDATION:422,FORBIDDEN:403,NOT_FOUND:404,NOT_AVAILABLE:409,RESTRICTED:409,INVALID_TRANSITION:409,STALE_VERSION:409,DAY_CLOSED:409,APPROVAL_REQUIRED:403,IDEMPOTENCY_MISMATCH:409,UNAVAILABLE:503};
/** User-facing titles — HccErrorCatalog.Title (same words as the prototype's errorTitle). */
export const WIRE_TITLE:Record<ClientWireCode,string>={VALIDATION:'Check the details',FORBIDDEN:'Not permitted',NOT_FOUND:'Record not found',NOT_AVAILABLE:'Not available',RESTRICTED:'Stay restriction',
  INVALID_TRANSITION:'Action not allowed now',STALE_VERSION:'Record changed',DAY_CLOSED:'Business day closed',APPROVAL_REQUIRED:'Approval required',IDEMPOTENCY_MISMATCH:'Request key already used',UNAVAILABLE:'Server unavailable',PROTOCOL:'Unexpected server response'};

/** Prototype code for each wire code. The three production-only codes map to the nearest prototype meaning:
 *  APPROVAL_REQUIRED → FORBIDDEN (not allowed without approval), IDEMPOTENCY_MISMATCH → VALIDATION (the request itself is wrong),
 *  UNAVAILABLE → RESULT_UNKNOWN (retry with the same Idempotency-Key; nothing may be shown as committed). */
export const PROTOTYPE_CODE:Record<ClientWireCode,ErrorCode>={VALIDATION:'VALIDATION',FORBIDDEN:'FORBIDDEN',NOT_FOUND:'NOT_FOUND',NOT_AVAILABLE:'NOT_AVAILABLE',RESTRICTED:'RESTRICTED',
  INVALID_TRANSITION:'INVALID_TRANSITION',STALE_VERSION:'STALE_VERSION',DAY_CLOSED:'DAY_CLOSED',APPROVAL_REQUIRED:'FORBIDDEN',IDEMPOTENCY_MISMATCH:'VALIDATION',UNAVAILABLE:'RESULT_UNKNOWN',PROTOCOL:'VALIDATION'};

export const isWireCode=(x:unknown):x is WireCode=>typeof x==='string'&&(WIRE_CODES as readonly string[]).includes(x);

export class HccClientError extends HccError{
  constructor(public readonly wireCode:ClientWireCode,message:string,public readonly details?:Record<string,unknown>,public readonly httpStatus?:number){
    super(PROTOTYPE_CODE[wireCode],message);this.name='HccClientError';
  }
  /** True when the outcome of a command is not known (LAN/DB loss): retry with the SAME Idempotency-Key. */
  get resultUnknown(){return this.wireCode==='UNAVAILABLE';}
}

/** Maps a non-2xx response. Unknown or missing codes fall back to the HTTP status (5xx → UNAVAILABLE, else VALIDATION). */
export function errorFromResponse(status:number,body:unknown):HccClientError{
  const b=(body&&typeof body==='object'?body:{}) as {code?:unknown;message?:unknown;details?:unknown};
  const code:ClientWireCode=isWireCode(b.code)?b.code:status>=500||status===0?'UNAVAILABLE':status===404?'NOT_FOUND':status===403?'FORBIDDEN':'VALIDATION';
  const message=typeof b.message==='string'&&b.message?b.message:WIRE_TITLE[code];
  const details=b.details&&typeof b.details==='object'?b.details as Record<string,unknown>:undefined;
  return new HccClientError(code,message,details,status);
}

/** "Title: message" — same shape as the prototype's run() toast. */
export function errorText(e:unknown){
  if(e instanceof HccClientError)return `${WIRE_TITLE[e.wireCode]}: ${e.message}`;
  if(e instanceof HccError)return `${errorTitle[e.code]}: ${e.message}`;
  return `${WIRE_TITLE.VALIDATION}: ${(e as Error)?.message??String(e)}`;
}

// Client error model (contracts §2): the server's {code,message,details?} body → the prototype's HccError codes.
// `code` stays a prototype ErrorCode so existing UI (errorTitle, codeOf) keeps working; `wireCode` keeps the server code.
import {HccError,errorTitle,type ErrorCode} from '../domain/errors';

/** Error codes on the wire — must match `08 Production/src/Hcc.Contracts/HccErrorCode.cs`. */
export const WIRE_CODES=['VALIDATION','FORBIDDEN','NOT_FOUND','NOT_AVAILABLE','RESTRICTED','INVALID_TRANSITION','STALE_VERSION','DAY_CLOSED','APPROVAL_REQUIRED','IDEMPOTENCY_MISMATCH','UNAVAILABLE','UNAUTHENTICATED'] as const;
export type WireCode=typeof WIRE_CODES[number];
/** Client-side only: a response that breaks the contract (bad JSON, unsafe integer, malformed date). Never sent by the server. */
export type ClientWireCode=WireCode|'PROTOCOL';

/** HTTP status per wire code — HccErrorCatalog.HttpStatus. */
export const WIRE_STATUS:Record<WireCode,number>={VALIDATION:422,FORBIDDEN:403,NOT_FOUND:404,NOT_AVAILABLE:409,RESTRICTED:409,INVALID_TRANSITION:409,STALE_VERSION:409,DAY_CLOSED:409,APPROVAL_REQUIRED:403,IDEMPOTENCY_MISMATCH:409,UNAVAILABLE:503,UNAUTHENTICATED:401};
/** User-facing titles — HccErrorCatalog.Title (same words as the prototype's errorTitle). */
export const WIRE_TITLE:Record<ClientWireCode,string>={VALIDATION:'Check the details',FORBIDDEN:'Not permitted',NOT_FOUND:'Record not found',NOT_AVAILABLE:'Not available',RESTRICTED:'Stay restriction',
  INVALID_TRANSITION:'Action not allowed now',STALE_VERSION:'Record changed',DAY_CLOSED:'Business day closed',APPROVAL_REQUIRED:'Approval required',IDEMPOTENCY_MISMATCH:'Request key already used',UNAVAILABLE:'Server unavailable',UNAUTHENTICATED:'Sign in again',PROTOCOL:'Unexpected server response'};

/** Prototype code for each wire code. The production-only codes map to the nearest prototype meaning:
 *  APPROVAL_REQUIRED → FORBIDDEN (not allowed without approval), IDEMPOTENCY_MISMATCH → VALIDATION (the request itself is wrong),
 *  UNAVAILABLE → RESULT_UNKNOWN (retry with the same Idempotency-Key; nothing may be shown as committed),
 *  UNAUTHENTICATED (401: no session / session ended) → FORBIDDEN; the app shows the sign-in screen. */
export const PROTOTYPE_CODE:Record<ClientWireCode,ErrorCode>={VALIDATION:'VALIDATION',FORBIDDEN:'FORBIDDEN',NOT_FOUND:'NOT_FOUND',NOT_AVAILABLE:'NOT_AVAILABLE',RESTRICTED:'RESTRICTED',
  INVALID_TRANSITION:'INVALID_TRANSITION',STALE_VERSION:'STALE_VERSION',DAY_CLOSED:'DAY_CLOSED',APPROVAL_REQUIRED:'FORBIDDEN',IDEMPOTENCY_MISMATCH:'VALIDATION',UNAVAILABLE:'RESULT_UNKNOWN',UNAUTHENTICATED:'FORBIDDEN',PROTOCOL:'VALIDATION'};

export const isWireCode=(x:unknown):x is WireCode=>typeof x==='string'&&(WIRE_CODES as readonly string[]).includes(x);

export class HccClientError extends HccError{
  constructor(public readonly wireCode:ClientWireCode,message:string,public readonly details?:Record<string,unknown>,public readonly httpStatus?:number){
    super(PROTOTYPE_CODE[wireCode],message);this.name='HccClientError';
  }
  /** True when the outcome of a command is not known (LAN/DB loss): retry with the SAME Idempotency-Key. */
  get resultUnknown(){return this.wireCode==='UNAVAILABLE';}
}

/** Shown when the server answers that guest-ID encryption has no key yet (install guide, "Guest ID key"). */
export const GUEST_ID_NOT_SET_UP='ID storage is not set up on the server. Ask the owner to complete the guest ID key step of the install guide (New-HccGuestIdKey.ps1), then try again.';
/** Maps a non-2xx response. Unknown or missing codes fall back to the HTTP status (5xx → UNAVAILABLE, else VALIDATION). */
export function errorFromResponse(status:number,body:unknown,retryAfter?:string|number|null):HccClientError{
  const b=(body&&typeof body==='object'?body:{}) as {code?:unknown;message?:unknown;details?:unknown};
  let code:ClientWireCode=isWireCode(b.code)?b.code:status>=500||status===0?'UNAVAILABLE':status===404?'NOT_FOUND':status===401?'UNAUTHENTICATED':status===403?'FORBIDDEN':'VALIDATION';
  let message=typeof b.message==='string'&&b.message?b.message:WIRE_TITLE[code];
  let details=b.details&&typeof b.details==='object'?b.details as Record<string,unknown>:undefined;
  // APK12-2: the sign-in throttle answers 429 + Retry-After (seconds) with details.reason TOO_MANY_ATTEMPTS: say how long to wait.
  const wait=Number(retryAfter);
  if(status===429||details?.reason==='TOO_MANY_ATTEMPTS'){
    if(Number.isFinite(wait)&&wait>0){details={...details,retryAfterSeconds:wait};const m=Math.max(1,Math.ceil(wait/60));
      message=details.reason==='TOO_MANY_ATTEMPTS'?`Too many sign-in attempts from this device. Wait ${m} minute${m===1?'':'s'}, then try again.`:`${message} Try again in ${m} minute${m===1?'':'s'}.`;}
    else if(details?.reason==='TOO_MANY_ATTEMPTS'&&!/wait/i.test(message))message='Too many sign-in attempts from this device. Wait a few minutes, then try again.';
  }
  // A 503 that the server itself answered with details.reason NOT_CONFIGURED (a setup step is missing, e.g. the guest-ID key) is not a
  // lost connection: the server is reachable, so it must not switch the app to read-only nor be retried. Surfaced as NOT_AVAILABLE.
  if(code==='UNAVAILABLE'&&details?.reason==='NOT_CONFIGURED'){
    code='NOT_AVAILABLE';
    if(details.feature==='guest-id')message=GUEST_ID_NOT_SET_UP;
  }
  return new HccClientError(code,message,details,status);
}

/** "Title: message" — same shape as the prototype's run() toast. */
/** The title is not repeated when the message already starts with it (APK12-3: "Not permitted: Not permitted: �"). */
const titled=(title:string,message:string)=>message.toLowerCase().startsWith(title.toLowerCase()+':')?message:`${title}: ${message}`;
export function errorText(e:unknown){
  if(e instanceof HccClientError)return e.details?.reason==='TOO_MANY_ATTEMPTS'?e.message:titled(WIRE_TITLE[e.wireCode],e.message);
  if(e instanceof HccError)return titled(errorTitle[e.code],e.message);
  return titled(WIRE_TITLE.VALIDATION,(e as Error)?.message??String(e));
}
/** Sign-in screen text: the server's own sentence (wrong PIN, throttle with wait time), without a title prefix. */
export const signInText=(e:unknown)=>e instanceof HccClientError&&e.wireCode!=='UNAVAILABLE'?e.message:errorText(e);

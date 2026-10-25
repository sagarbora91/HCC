// Wire guards shared by the adapters (contracts §2): paise as safe integers, dates YYYY-MM-DD, Idempotency-Key rules.
import {HccClientError} from './errors';

export const API_BASE='/api/v1';
export const IDEMPOTENCY_HEADER='Idempotency-Key';
export const IDEMPOTENCY_KEY_MAX=80;
/** ₹10 crore per money field (ApiConventions.MaxPaisePerField). */
export const MAX_PAISE=10_000_000_000;

const DATE=/^(\d{4})-(\d{2})-(\d{2})$/;
/** A real calendar date in YYYY-MM-DD (no time part, no time zone). */
export function isIsoDate(v:unknown):v is string{
  if(typeof v!=='string')return false;const m=DATE.exec(v);if(!m)return false;
  const d=new Date(Date.UTC(+m[1],+m[2]-1,+m[3]));return d.getUTCFullYear()===+m[1]&&d.getUTCMonth()===+m[2]-1&&d.getUTCDate()===+m[3];
}
/** Money field check: a JSON number that is a safe integer within ±₹10 crore. Strings / BigInt-as-string are refused. */
export function isPaise(v:unknown):v is number{return typeof v==='number'&&Number.isSafeInteger(v)&&Math.abs(v)<=MAX_PAISE;}

/** Field names treated as money (…Paise) and as calendar dates. Event times (…Utc, …At) are ISO-8601 strings and not checked here. */
const PAISE_KEY=/Paise$/,DATE_KEYS=new Set(['date','arrival','departure','businessDate','serviceDate']);
/** Walks a decoded body and throws PROTOCOL on an unsafe money value or a malformed date. `where` = request|response. */
export function checkWire(value:unknown,where:'request'|'response',path='$'):void{
  if(Array.isArray(value)){value.forEach((v,i)=>checkWire(v,where,`${path}[${i}]`));return;}
  if(!value||typeof value!=='object')return;
  for(const [k,v] of Object.entries(value as Record<string,unknown>)){
    const p=`${path}.${k}`;
    if(PAISE_KEY.test(k)&&v!==null&&v!==undefined&&!isPaise(v))throw new HccClientError(where==='request'?'VALIDATION':'PROTOCOL',`${p} is not a safe paise amount (integer within ±${MAX_PAISE}).`,{field:p});
    if(DATE_KEYS.has(k)&&v!==null&&v!==undefined&&!isIsoDate(v))throw new HccClientError(where==='request'?'VALIDATION':'PROTOCOL',`${p} must be a date in YYYY-MM-DD.`,{field:p});
    checkWire(v,where,p);
  }
}
/** Keys may be reused only for a retry of the same request (server answers IDEMPOTENCY_MISMATCH otherwise). */
export function checkIdempotencyKey(key:unknown):string{
  if(typeof key!=='string'||!key||key.length>IDEMPOTENCY_KEY_MAX||!/^[\x21-\x7e]+$/.test(key))
    throw new HccClientError('VALIDATION',`Idempotency-Key must be 1–${IDEMPOTENCY_KEY_MAX} visible ASCII characters.`);
  return key;
}
/** A fresh key for a new user action (one per dialog/attempt, reused on retry). */
export function newIdempotencyKey(prefix='cmd'){
  const r=globalThis.crypto?.randomUUID?.()??(Date.now().toString(36)+Math.random().toString(36).slice(2));
  return checkIdempotencyKey((prefix+':'+r).slice(0,IDEMPOTENCY_KEY_MAX));
}

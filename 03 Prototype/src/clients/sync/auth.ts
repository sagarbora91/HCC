// Sign-in seam (W4-0). The production web build signs in with the server's HttpOnly cookie session (POST /auth/sign-in,
// `credentials:'include'`), exactly as Wave 1. W4-12 adds the APK's token sign-in (`returnToken:true` → secure storage →
// `Authorization: Bearer …`, plus the device token header for PIN sign-in) by providing another AuthAdapter here — nothing else
// in the sync layer needs to change: the runtime asks the adapter for request headers and for sign-in / sign-out.
import type {HttpTransport} from '../http';
import {HccClientError} from '../errors';

/** POST /auth/sign-in body (Hcc.Contracts SignInRequest). Exactly one of password (Owner, Accountant, Tech admin) or pin
 *  (other staff, registered device only — DEC-062). */
export type SignInInput={loginName:string;password?:string;pin?:string};
/** SignInResult (sessionToken only when returnToken was asked for). */
export type SrvSignInResult={userId:number;displayName:string;credentialKind:string;session:{idleExpiresUtc:string;absoluteExpiresUtc:string;idleTimeoutSeconds:number};sessionToken?:string|null};

export interface AuthAdapter{
  readonly kind:'cookie'|'token';
  /** Headers added to every request (cookie: none, except the device token for PIN sign-in on a registered device). */
  headers():Record<string,string>;
  signIn(t:HttpTransport,input:SignInInput):Promise<SrvSignInResult>;
  signOut(t:HttpTransport):Promise<void>;
}

/** Device token of a registered shared device (X-HCC-Device). Assumption until W4-5/W4-12 add device registration screens:
 *  stored under localStorage `hcc-device-token`. */
export const DEVICE_HEADER='X-HCC-Device';
export const DEVICE_TOKEN_KEY='hcc-device-token';
function deviceToken(){try{return globalThis.localStorage?.getItem(DEVICE_TOKEN_KEY)||'';}catch{return '';}}

function check(input:SignInInput){
  if(!input.loginName?.trim())throw new HccClientError('VALIDATION','Enter your login name.');
  if(!!input.password===!!input.pin)throw new HccClientError('VALIDATION','Enter your password or your PIN.');
}

/** Browser: HttpOnly cookie set by the server; nothing stored by the app. */
export function cookieAuth():AuthAdapter{
  return {
    kind:'cookie',
    headers(){const d=deviceToken();const h:Record<string,string>={};if(d)h[DEVICE_HEADER]=d;return h;},
    async signIn(t,input){check(input);
      return t.post<SrvSignInResult>('/auth/sign-in',{loginName:input.loginName.trim(),...(input.password?{password:input.password}:{pin:input.pin}),returnToken:false});},
    async signOut(t){await t.post('/auth/sign-out');},
  };
}

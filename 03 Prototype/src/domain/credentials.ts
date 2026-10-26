// Password / PIN rules shared by the demo mock and the production client (Wave C · A02b-05 self-service change, A06-01 must-change).
// Mirror of 08 Production Hcc.Domain.Access: CredentialPolicy (DEC-062 kind per role), PinRules, PasswordRules, OwnCredentialRules
// (the new secret differs from the current one; lockout message) and CredentialChangeGate (the "change it first" refusal). The
// server decides; these checks give both builds the same refusals in the same words before anything is sent.
// Secrets never go into the state, a log, an audit detail or an Idempotency-Key: the screens pass an explicit key without them.
import type {Role} from './types';

export type CredKind='PASSWORD'|'PIN';
/** Server roles that sign in with a password (RoleCodes.PasswordRoles). */
export const PASSWORD_ROLES=['OWN','ACC','TAD'] as const;
/** The prototype roles of those server roles (ROLE_MAP): Owner, Accountant, Tech admin. */
export const PASSWORD_PROTO_ROLES:readonly Role[]=['Owner','Accountant','Tech admin'];
export const PIN_LENGTH=6,PASSWORD_MIN=12,PASSWORD_MAX=128;
/** AccessOptions.PinHistory: no reuse of the last N PINs (the current one included). */
export const PIN_HISTORY=3;
/** The self-service command (demo: services/mock/handlers/credentials.ts; production: ChangeOwnCredential, sync/admin.ts). */
export const OWN_CREDENTIAL_CHANGE='OWN_CREDENTIAL_CHANGE';
/** details.reason of the server's 403 while an administrator-set credential must be changed (CredentialChangeGate.Reason). */
export const MUST_CHANGE_REASON='MUST_CHANGE_CREDENTIAL';

/** DEC-062: any password role (active, future or expired assignment alike) → password; other roles → PIN; no role → null. */
export function requiredKind(roleCodes:readonly string[]):CredKind|null{
  if(!roleCodes.length)return null;
  return roleCodes.some(r=>(PASSWORD_ROLES as readonly string[]).includes(r))?'PASSWORD':'PIN';
}
/** Demo / display: the kind a prototype role signs in with. */
export const kindOfRole=(role:Role|string|undefined):CredKind=>(PASSWORD_PROTO_ROLES as readonly string[]).includes(String(role))?'PASSWORD':'PIN';

const COMMON_PINS=new Set(['112233','102030','111222','121314','123321','147258','147852','159753','159357','258369','258456',
  '369258','741852','789456','456789','963852','852456','696969','100200','998877','778899','010203']);
/** Same rule as PinRules.IsTrivial on the server. */
export function pinIsTrivial(pin:string):boolean{
  if(COMMON_PINS.has(pin))return true;
  const d=[...pin].map(c=>c.charCodeAt(0)-48);let asc=true,desc=true;
  for(let i=1;i<d.length;i++){asc&&=d[i]===d[i-1]+1;desc&&=d[i]===d[i-1]-1;}
  if(asc||desc)return true;
  for(const period of [1,2,3]){if(pin.length%period)continue;if(pin.slice(0,period).repeat(pin.length/period)===pin)return true;}
  return pin===[...pin].reverse().join('')||new Set(pin).size<=2;
}
/** PinRules.Validate / PasswordRules.Validate wording (loginName '' = not known here: the server checks it). */
export function secretRuleProblem(kind:CredKind,secret:string,loginName:string):string|undefined{
  if(kind==='PIN'){
    if(secret.length!==PIN_LENGTH||!/^\d+$/.test(secret))return `The PIN must be exactly ${PIN_LENGTH} digits.`;
    if(pinIsTrivial(secret))return 'This PIN is too easy to guess. Choose one without runs or repeats.';
    return undefined;
  }
  if(secret.length<PASSWORD_MIN||secret.length>PASSWORD_MAX)return `The password must be ${PASSWORD_MIN}–${PASSWORD_MAX} characters.`;
  if(loginName&&secret.toLowerCase().includes(loginName.toLowerCase()))return 'The password must not contain the sign-in name.';
  if(new Set(secret).size<4)return 'The password is too simple.';
  return undefined;
}
/** Client-side check before an administrator sets a credential (the server repeats it and also refuses a recently used PIN). */
export function credentialProblem(kind:CredKind,secret:string,confirm:string,loginName:string):string|undefined{
  if(!secret)return kind==='PIN'?'Type the new PIN.':'Type the new password.';
  const bad=secretRuleProblem(kind,secret,loginName);if(bad)return bad;
  if(secret!==confirm)return kind==='PIN'?'The two PINs do not match.':'The two passwords do not match.';
  return undefined;
}
/** The rules as shown next to the form. */
export const credentialRules=(kind:CredKind)=>kind==='PIN'
  ?`${PIN_LENGTH} digits, no runs or repeats (not 123456, 111111, 121212 …), not one of the last PINs this person used.`
  :`${PASSWORD_MIN}–${PASSWORD_MAX} characters, at least 4 different characters, not containing the sign-in name.`;
/** OwnCredentialRules.EnsureNew wording. */
export const sameSecretText=(kind:CredKind)=>kind==='PIN'?'Choose a new PIN: it must be different from the current one.':'Choose a new password: it must be different from the current one.';
/** Self-service change (ChangeOwnCredential): current secret typed, new one follows the rules, typed twice, differs from the current. */
export function ownCredentialProblem(kind:CredKind,current:string,next:string,confirm:string,loginName=''):string|undefined{
  if(!current)return kind==='PIN'?'Type your current PIN.':'Type your current password.';
  const bad=credentialProblem(kind,next,confirm,loginName);if(bad)return bad;
  if(current===next)return sameSecretText(kind);
  return undefined;
}
/** The command body the screens send (the key never contains the secret: own-credential:<user>:<nonce>:<attempt>). */
export function ownCredentialCommand(kind:CredKind,current:string,next:string,key:string){
  return kind==='PIN'?{type:OWN_CREDENTIAL_CHANGE,current,newPin:next,key}:{type:OWN_CREDENTIAL_CHANGE,current,newPassword:next,key};
}
export const ownCredentialKey=(userId:string,nonce:string,attempt:number)=>`own-credential:${userId}:${nonce}:${attempt}`;
/** CredentialChangeGate.Message: what every other action answers while an administrator-set credential must be changed. */
export const mustChangeText=(kind:CredKind)=>kind==='PIN'
  ?'Change your PIN first. An administrator set it for you, so choose your own PIN before you continue.'
  :'Change your password first. An administrator set it for you, so choose your own password before you continue.';
/** OwnCredentialRules.LockedMessage. */
export function ownCredentialLockedText(minutesLeft:number){
  const m=Math.max(1,Math.ceil(minutesLeft));
  return `Too many wrong attempts. Try again in ${m} minute${m===1?'':'s'}.`;
}

// ── Demo only (services/mock/handlers/credentials.ts) ──
// The demo signs in by picking a fictional staff member, so it has no real secrets. Every demo user starts with demoPin() or
// demoPassword() (shown on the change sheet of the demo build only); a changed one is kept as a digest in s.credentialsX.
// Not mirrored in the demo: the wrong-attempt lockout (a refused demo action changes nothing, so it cannot count attempts) and
// the A06-01 must-change flag (the demo has no administrator reset; Access control › Sign-in & devices is production only).
// Compiled out of the production flavour (build.mjs defines process.env.HCC_BUILD, the minifier folds the condition): the server
// build never carries them (tests/stream-clients.mjs [P5] production-bundle check). Functions, so `process` is read only when called.
export const demoPin=()=>process.env.HCC_BUILD==='production'?'':'246810';
export const demoPassword=()=>process.env.HCC_BUILD==='production'?'':'Demo-Hotel-2026';
/** Fictional-data digest for the demo store (FNV-1a over user + secret). Not a security measure: the demo has none. */
export function demoSecretDigest(uid:string,secret:string){
  let h=0x811c9dc5;const t=uid+'|'+secret;
  for(let i=0;i<t.length;i++){h^=t.charCodeAt(i);h=Math.imul(h,0x01000193)>>>0;}
  return h.toString(16).padStart(8,'0');
}
export type DemoCredential={kind:CredKind;digest:string;
  /** Digests of the last PIN_HISTORY PINs (newest first), current included; empty for passwords (no password history on the server). */history:string[];
  changedAt:string};
export type DemoCredentials=Record<string,DemoCredential>;
/** The user's demo credential (the starting one when never changed, or when the role now needs the other kind). */
export function demoCredentialOf(all:DemoCredentials|undefined,uid:string,role:Role,clock:string):DemoCredential{
  const kind=kindOfRole(role);const have=all?.[uid];
  if(have&&have.kind===kind)return have;
  const digest=demoSecretDigest(uid,kind==='PIN'?demoPin():demoPassword());
  return {kind,digest,history:kind==='PIN'?[digest]:[],changedAt:clock};
}

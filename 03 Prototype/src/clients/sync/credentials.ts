// Sign-in & devices (Wave 4 gap G1, DEC-062): an administrator (PERM-ADM-001: Owner, Tech admin) sets or resets a user's first
// password or PIN, and registers / revokes the hotel devices on which staff sign in with a PIN. Framework-free (tests drive it
// with the fake-fetch harness); the screen is CredentialsPanel.tsx, shown in Access control in the production build only.
//
// • Who uses what (mirror of Hcc.Domain.Access.CredentialPolicy): any password role (OWN, ACC, TAD) → password, 12–128
//   characters, not containing the sign-in name, at least 4 different characters; every other user → a 6-digit PIN that is not
//   trivial (runs, repeats, mirror halves, two digits only, common keypad patterns). The client checks are only a courtesy:
//   the server decides and its message is shown as it is (PIN history, Owner-only for an Owner account, …).
// • Secrets: the PIN / password goes only into the USER_CREDENTIAL command body (SetCredential). It is never put in the
//   app state, the read cache, a log or the Idempotency-Key: the screen always passes an explicit key without the secret
//   (the runtime would otherwise derive one from a digest of the command, and a 6-digit PIN is recoverable from a digest).
// • Reads: GET /admin/users (credential kind set, lockout, roles) and GET /admin/devices — read on demand by the panel, not
//   kept as state slices (nothing here belongs in the offline read cache).
import type {Command} from '../../services/mock';
import type {RunResult} from './runtime';
import {protoId,utcToClock} from './map';
import {adoptDeviceRegistration,deviceId,deviceServerLabel,forgetDevice,refreshDeviceStaff} from './apk';
import type {HttpTransport} from '../http';

export type CredKind='PASSWORD'|'PIN';
/** Server roles that sign in with a password (RoleCodes.PasswordRoles). */
export const PASSWORD_ROLES=['OWN','ACC','TAD'] as const;
export const PIN_LENGTH=6,PASSWORD_MIN=12,PASSWORD_MAX=128;

// ── Server DTOs (Hcc.Contracts.Access) ──
type SrvUserRole={roleCode:string;validFromUtc:string;validUntilUtc?:string|null};
export type SrvCredUser={userId:number;loginName:string;displayName:string;enabled:boolean;credentialKind:string;lockedOut:boolean;roles:SrvUserRole[];version:string};
export type SrvDevice={deviceId:number;label:string;registeredUtc:string;lastSeenUtc?:string|null;revokedUtc?:string|null};
type SrvPage<T>={items:T[];page:number;pageSize:number;total:number};

export type CredentialUser={id:string;loginName:string;displayName:string;enabled:boolean;lockedOut:boolean;
  /** What the server holds now: PASSWORD, PIN or NONE (cannot sign in yet). */ has:CredKind|'NONE';
  /** What DEC-062 requires for this user's roles; null = no role (cannot sign in at all). */ needs:CredKind|null};
export type DeviceRow={id:string;label:string;registeredAt:string;lastSeenAt:string;revokedAt:string;active:boolean;thisDevice:boolean};
export type CredentialDirectory={users:CredentialUser[];devices:DeviceRow[]};

/** DEC-062: any password role (active, future or expired assignment alike) → password; other roles → PIN; no role → null. */
export function requiredKind(roleCodes:readonly string[]):CredKind|null{
  if(!roleCodes.length)return null;
  return roleCodes.some(r=>(PASSWORD_ROLES as readonly string[]).includes(r))?'PASSWORD':'PIN';
}

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
/** Client-side check before sending (the server repeats it and also refuses a recently used PIN). */
export function credentialProblem(kind:CredKind,secret:string,confirm:string,loginName:string):string|undefined{
  if(!secret)return kind==='PIN'?'Type the new PIN.':'Type the new password.';
  if(kind==='PIN'){
    if(secret.length!==PIN_LENGTH||!/^\d+$/.test(secret))return `The PIN must be exactly ${PIN_LENGTH} digits.`;
    if(pinIsTrivial(secret))return 'This PIN is too easy to guess. Choose one without runs or repeats.';
  }else{
    if(secret.length<PASSWORD_MIN||secret.length>PASSWORD_MAX)return `The password must be ${PASSWORD_MIN}–${PASSWORD_MAX} characters.`;
    if(loginName&&secret.toLowerCase().includes(loginName.toLowerCase()))return 'The password must not contain the sign-in name.';
    if(new Set(secret).size<4)return 'The password is too simple.';
  }
  if(secret!==confirm)return kind==='PIN'?'The two PINs do not match.':'The two passwords do not match.';
  return undefined;
}
/** The rules as shown next to the form. */
export const credentialRules=(kind:CredKind)=>kind==='PIN'
  ?`${PIN_LENGTH} digits, no runs or repeats (not 123456, 111111, 121212 …), not one of the last PINs this person used.`
  :`${PASSWORD_MIN}–${PASSWORD_MAX} characters, at least 4 different characters, not containing the sign-in name.`;

type Getter=<T>(path:string,query?:Record<string,string|number|boolean|undefined>)=>Promise<T>;
const PAGE=100;
async function allPages<T>(get:Getter,path:string,max=20):Promise<T[]>{
  const out:T[]=[];
  for(let page=1;page<=max;page++){const r=await get<SrvPage<T>>(path,{page,pageSize:PAGE});out.push(...(r?.items??[]));if(!r?.items?.length||out.length>=r.total)break;}
  return out;
}
const KINDS:Record<string,CredKind|'NONE'>={PASSWORD:'PASSWORD',PIN:'PIN'};
export const credentialUserFromServer=(u:SrvCredUser):CredentialUser=>({id:protoId(u.userId),loginName:u.loginName,displayName:u.displayName||u.loginName,
  enabled:u.enabled,lockedOut:!!u.lockedOut,has:KINDS[String(u.credentialKind).toUpperCase()]??'NONE',needs:requiredKind(u.roles.map(r=>r.roleCode))});
/** `thisDeviceTag` = " · <first 8 of this installation's device id>" when this device holds a registration (apk.deviceServerLabel). */
export const deviceFromServer=(d:SrvDevice,thisDeviceTag?:string):DeviceRow=>({id:protoId(d.deviceId),label:d.label,registeredAt:utcToClock(d.registeredUtc),
  lastSeenAt:d.lastSeenUtc?utcToClock(d.lastSeenUtc):'',revokedAt:d.revokedUtc?utcToClock(d.revokedUtc):'',active:!d.revokedUtc,
  thisDevice:!d.revokedUtc&&!!thisDeviceTag&&d.label.endsWith(thisDeviceTag)});
/** Users (with their credential state) and the registered devices, read with the administrator's session. */
export async function loadCredentialDirectory(get:Getter,thisDeviceTag?:string):Promise<CredentialDirectory>{
  const [users,devices]=await Promise.all([allPages<SrvCredUser>(get,'/admin/users'),allPages<SrvDevice>(get,'/admin/devices')]);
  return {users:users.map(credentialUserFromServer).sort((a,b)=>Number(a.id)-Number(b.id)),
    devices:devices.map(d=>deviceFromServer(d,thisDeviceTag)).sort((a,b)=>Number(b.active)-Number(a.active)||Number(a.id)-Number(b.id))};
}
export const thisDeviceTag=(registered:boolean)=>registered?' · '+deviceId().slice(0,8):undefined;

type Run=(cmd:Command)=>Promise<RunResult>;
/** Idempotency-Key of one credential attempt: no secret in it. Same key on a retry after a lost answer; a new attempt after a
 *  definitive refusal (e.g. a PIN used before) or a success. */
export const credentialKey=(userId:string,nonce:string,attempt:number)=>`credential:${userId}:${nonce}:${attempt}`;
export type ActionOutcome={ok:true;text:string}|{ok:false;text:string;/** the server (or the precheck) answered: the next try is a new action */ definitive:boolean};
const definitiveOf=(r:Extract<RunResult,{ok:false}>)=>r.reason==='precheck'||(r.reason==='server'&&(r.error as {wireCode?:string}|undefined)?.wireCode!=='UNAVAILABLE');

/** USER_CREDENTIAL → SetCredential {userId, password|pin, expectedVersion}. Checks the rules first; never echoes the secret. */
export async function setCredential(run:Run,user:CredentialUser,kind:CredKind,secret:string,confirm:string,key:string):Promise<ActionOutcome>{
  const bad=credentialProblem(kind,secret,confirm,user.loginName);
  if(bad)return {ok:false,text:bad,definitive:false};
  const cmd:Command=kind==='PIN'?{type:'USER_CREDENTIAL',id:user.id,pin:secret,key}:{type:'USER_CREDENTIAL',id:user.id,password:secret,key};
  const r=await run(cmd);
  if(!r.ok)return {ok:false,text:r.text,definitive:definitiveOf(r)};
  return {ok:true,text:`${kind==='PIN'?'PIN':'Password'} set for ${user.displayName}. It is not shown again — tell them in person.${user.lockedOut?' Their lockout is cleared.':''}${kind==='PIN'?' They sign in with it on a registered hotel device.':''}`};
}
/** DEVICE_REGISTER → RegisterDevice for THIS device: the one-time device token is kept only in this device's storage. */
export async function registerDeviceHere(run:Run,transport:HttpTransport|undefined,name:string,nonce:string):Promise<ActionOutcome>{
  if(!name.trim())return {ok:false,text:'Name the device (e.g. Front desk tablet).',definitive:false};
  const id=deviceId();const label=deviceServerLabel(name,id);
  const r=await run({type:'DEVICE_REGISTER',label,key:`device-register:${id.slice(0,8)}:${nonce}`});
  if(!r.ok)return {ok:false,text:r.text,definitive:definitiveOf(r)};
  const res=(r.result??{}) as {deviceId?:string;deviceToken?:string|null};
  if(!res.deviceToken)return {ok:false,text:`The server registered device #${res.deviceId??'?'} but did not return its key (repeated request). Revoke it below and register again.`,definitive:true};
  adoptDeviceRegistration(res.deviceToken,label);
  let staff=0;if(transport){try{staff=await refreshDeviceStaff(transport);}catch{/* updated later from Master configuration */}}
  return {ok:true,text:`This device is registered as "${label}" (device #${res.deviceId}). ${staff} staff member${staff===1?'':'s'} can pick their name and sign in with a PIN here.`};
}
/** DEVICE_REVOKE → RevokeDevice: PIN sign-in on that device stops and its sessions end. Revoking this device also drops its local registration. */
export async function revokeDevice(run:Run,d:DeviceRow):Promise<ActionOutcome>{
  const r=await run({type:'DEVICE_REVOKE',id:d.id,key:`device-revoke:${d.id}`});
  if(!r.ok)return {ok:false,text:r.text,definitive:definitiveOf(r)};
  if(d.thisDevice)forgetDevice();
  return {ok:true,text:`Device "${d.label}" revoked: staff can no longer sign in with a PIN on it.${d.thisDevice?' This device’s registration was removed too.':''}`};
}

// LF lane lf-money-rate · contract §2 (A1 rate / guest-count change during a stay) and §3 (B1 a regular guest's agreed rate).
// Pure helpers: who may apply a change directly, which posted nights re-post, refusal texts, agreed-rate lookup and the money-role filter.
// Export names are prefixed `str` / `STR_` / `guestAgreed` to avoid clashes.
import type {Booking,GuestAgreedRate,Line,Role,State,STRRateChange,STRState} from '../types';
import {addDays,money} from '../money';
import {rateFor} from '../selectors';
import {adminLimitFor} from './admin';
import {closingReopened} from './closing';
import {rmsCapacity,rmsOccFor} from './roommasters';
import {guestDuplicates} from './guests';
import {mstMoneyOn} from './masters';
import {resCapacity} from './reservations';
import {cfgValueOn} from './config'; // AF2-CTL01 threshold (called at run time only)

export type {STRRateChange,STRState,GuestAgreedRate};
export const STR_SCOPES=['next','today-next','all'] as const;
export type STRScope=typeof STR_SCOPES[number];
export const STR_SCOPE_LABEL:Record<STRScope,string>={'next':'Next day onward','today-next':'Tonight and later','all':'Every open night of the stay'};
/** DEC-MA-11e: the roles that may receive a money value from LF. */
export const LF_MONEY_ROLES:readonly Role[]=['Owner','Manager','Reception','Cashier','Accountant'];
export const lfIsMoneyRole=(s:State,uid:string)=>{const u=s.users.find(x=>x.id===uid);return !!u&&LF_MONEY_ROLES.includes(u.role);};

export const strState=(s:State):STRState=>s.stayRateX??{changes:[]};
export const strChangesFor=(s:State,bookingId:string)=>strState(s).changes.filter(c=>c.booking===bookingId);
/** The latest change of a stay (for the folio header line "Rate changed · ₹old → ₹new from <date>"). */
export function strHeaderLine(s:State,bookingId:string):string|undefined{
  const c=strChangesFor(s,bookingId).at(-1);if(!c||c.oldRate===c.newRate)return undefined;
  return `Rate changed · ${money(c.oldRate)} → ${money(c.newRate)} from ${c.effectiveFrom}`;
}
/** A night is on a closed business date unless the Owner reopened exactly that day (DEC-077). */
export const strNightClosed=(s:State,night:string)=>s.closedDates.includes(night)&&closingReopened(s)?.date!==night;
/** The first night the new rate applies to. */
export function strEffectiveFrom(s:State,b:Booking,scope:STRScope):string{
  if(scope==='next')return addDays(s.date,1);
  if(scope==='today-next')return s.date;
  for(let d=b.arrival;d<b.departure;d=addDays(d,1))if(!strNightClosed(s,d))return d;
  return s.date;
}
/** The posted room lines that re-post (reversal + new line) for the scope: live (not reversed, not a reversal) room lines on open nights from `effectiveFrom`. */
export function strPlan(s:State,b:Booking,scope:STRScope):Line[]{
  const from=strEffectiveFrom(s,b,scope);
  return b.lines.filter(l=>l.kind==='room'&&!l.reversalOf&&l.amount>0&&l.date>=from&&l.date<b.departure&&!strNightClosed(s,l.date)&&!b.lines.some(r=>r.reversalOf===l.id));
}
/** The tariff of the stay's room type on a date (rate card, DEC-198). */
export function strTariff(s:State,b:Booking,date=s.date):number{const r=s.rooms.find(x=>x.id===b.room);return r?rateFor(s,r,date):b.rate;}
/** The lowest rate a Manager may set directly: tariff − PERM-RATE-001 % (Manager 15 %). Owner: 0. Other roles: Infinity (approval). */
export function strDirectFloor(s:State,uid:string,b:Booking,date=s.date):number{
  const u=s.users.find(x=>x.id===uid);if(!u)return Infinity;
  if(u.role==='Owner')return 0;
  if(u.role!=='Manager')return Infinity;
  const pct=adminLimitFor(s,'Manager','PERM-RATE-001')??0;
  return Math.ceil(strTariff(s,b,date)*(100-pct)/100);
}
// ── CTL-01 (AF2-CTL01; AUDITFIX-PLAN §2 Q4 default (a)): a reduction is judged against the stay's AGREED rate as well as the tariff; the stricter wins. ──
/** Master configuration › Rooms and rate plans (Owner-only, config.ts 'rate-control'); server RateControlSettings.ThresholdKey. */
export const STR_THRESHOLD_KEY='rate.agreedReductionBps';
export const STR_THRESHOLD_DEFAULT=1500;
/** The threshold in force (basis points, 0–10000): the Owner's setting, else 15 % (the Manager's PERM-RATE-001 limit). */
export function strThresholdBps(s:State):number{const v=Number(cfgValueOn(s,STR_THRESHOLD_KEY));return Number.isInteger(v)&&v>=0&&v<=10000?v:STR_THRESHOLD_DEFAULT;}
/** The agreed rate of a stay: the agreed rate after its latest change (an Owner-set rate becomes the new agreed rate; a Manager's does not, so a stay
 *  cannot be walked down in steps), else the rate before its first change, else the booking's rate. */
export function strAgreedRate(s:State,b:Booking):number{
  const ch=strChangesFor(s,b.id);
  for(let i=ch.length-1;i>=0;i--)if(ch[i].agreedAfter!==undefined)return ch[i].agreedAfter!;
  return ch.length?ch[0].oldRate:b.rate;
}
/** The lowest rate a Manager sets or approves against the agreed rate (rounded up): a rate AT the floor passes, one paisa below needs the Owner. */
export const strAgreedFloor=(agreed:number,bps:number)=>Math.ceil(agreed*(10000-Math.min(10000,Math.max(0,bps)))/10000);
/** Basis points below the agreed rate (0 for a raise), rounded down. */
export const strReductionBps=(agreed:number,rate:number)=>agreed<=0||rate>=agreed?0:Math.floor((agreed-rate)*10000/agreed);
/** True when `rate` is more than the Owner's threshold below the stay's agreed rate. */
export const strOverThreshold=(s:State,b:Booking,rate:number)=>rate<strAgreedFloor(strAgreedRate(s,b),strThresholdBps(s));
/** "₹2,500 agreed · 24 % below · over the 15 % threshold (Owner)" — shown on the rate-change form and in the audit text. */
export function strJudgedText(s:State,b:Booking,rate:number):string{
  const agreed=strAgreedRate(s,b),bps=strThresholdBps(s),pct=(v:number)=>`${(v/100).toLocaleString('en-IN',{maximumFractionDigits:2})} %`;
  return `agreed ${money(agreed)} · ${pct(strReductionBps(agreed,rate))} below`+(strOverThreshold(s,b,rate)?` · over the ${pct(bps)} threshold (Owner)`:'');
}
/** True when the stay's room type has an extra-person charge (MA-1 occupancy master), so a raise of guests changes money. */
export function strExtraChargeApplies(s:State,b:Booking):boolean{
  const r=s.rooms.find(x=>x.id===b.room);const o=r?rmsOccFor(s,r.type):undefined;if(!o)return false;
  return (mstMoneyOn(o.extraAdult,s.date)??0)>0||(mstMoneyOn(o.extraChild,s.date)??0)>0;
}
/** Whether `uid` may apply the change without an approval (contract §2 "Authority"). */
export function strCanDirect(s:State,uid:string,b:Booking,change:{rate:number;people:number;children:number}):boolean{
  const u=s.users.find(x=>x.id===uid);if(!u)return false;
  if(u.role==='Owner')return true;
  if(u.role!=='Manager')return false;
  const raise=change.people+change.children>(b.people+(b.children??0));
  if(raise&&strExtraChargeApplies(s,b))return false;
  if(change.rate!==b.rate&&strOverThreshold(s,b,change.rate))return false; // CTL-01: more than the threshold below the agreed rate → the Owner
  if(change.rate>=strTariff(s,b))return true;          // at or above the tariff never loses money
  return change.rate>=strDirectFloor(s,uid,b);
}
/** Whether an approver may decide a rate-change approval: the Owner anything; a Manager within the same limits (tariff and, CTL-01, the agreed rate). */
export function strCanApprove(s:State,uid:string,b:Booking,rate:number):boolean{
  const u=s.users.find(x=>x.id===uid);if(!u)return false;
  if(u.role==='Owner')return true;
  if(u.role!=='Manager')return false;
  if(rate!==b.rate&&strOverThreshold(s,b,rate))return false;
  return rate>=strTariff(s,b)||rate>=strDirectFloor(s,uid,b);
}
/** Refusal text for a STAY_RATE_CHANGE input, or ''. */
export function strRefusal(s:State,b:Booking|undefined,input:{rate?:unknown;people?:unknown;children?:unknown;scope?:unknown;reason?:unknown}):string{
  if(!b)return 'Booking not found.';
  if(b.status!=='In house')return 'A rate or guest-count change applies to an in-house stay only.';
  if(!STR_SCOPES.includes(input.scope as STRScope))return 'Choose when the change applies: next day, tonight and later, or every open night.';
  if(!String(input.reason??'').trim())return 'A reason is required.';
  const rate=input.rate===undefined||input.rate===null?b.rate:Number(input.rate);
  if(!Number.isInteger(rate)||rate<0)return 'Enter the new rate in whole paise (0 or more).';
  const people=input.people===undefined||input.people===null?b.people:Number(input.people);
  const children=input.children===undefined||input.children===null?(b.children??0):Number(input.children);
  if(!Number.isInteger(people)||people<1||people>4)return 'Choose 1–4 guests.';
  if(!Number.isInteger(children)||children<0||children>3)return 'Children must be between 0 and 3.';
  if(rate===b.rate&&people===b.people&&children===(b.children??0))return 'Nothing changes: give a new rate or a new number of guests.';
  const room=s.rooms.find(r=>r.id===b.room);
  const max=room?rmsCapacity(s,room.type)??resCapacity(room as never):undefined;
  if(max!==undefined&&people+children>max)return `Room ${b.room} (${room!.type}) accommodates at most ${max} guests including children.`;
  if(input.scope==='next'&&addDays(s.date,1)>=b.departure)return 'The stay ends tomorrow: there is no later night to change. Choose “Tonight and later”.';
  return '';
}

// ── B1 agreed rates ──
/** The guest's agreed rate for a room type valid on a date (validTo inclusive), or undefined. */
export function guestAgreedRateFor(s:State,guestId:string|undefined,type:string,date:string):GuestAgreedRate|undefined{
  const g=guestId?(s.guests||[]).find(x=>x.id===guestId):undefined;
  return g?.agreedRates?.find(r=>r.type===type&&(!r.validTo||date<=r.validTo));
}
/** The guest a booking command resolves to: `guestId`, else the one guest whose phone matches (`guestDuplicates` by phone). */
export function guestAgreedGuestOf(s:State,input:{guestId?:unknown;phone?:unknown}):string|undefined{
  if(typeof input.guestId==='string'&&(s.guests||[]).some(g=>g.id===input.guestId))return input.guestId;
  const phone=String(input.phone??'').trim();if(!phone)return undefined;
  const m=guestDuplicates(s,{phone}).filter(x=>x.reason==='phone');
  return m.length===1?m[0].guest.id:undefined;
}
/** The agreed rates of a guest as a role may see them: money roles only (DEC-MA-11e); everyone else gets undefined. */
export function guestAgreedRatesView(s:State,uid:string,g:{agreedRates?:GuestAgreedRate[]}):GuestAgreedRate[]|undefined{
  return lfIsMoneyRole(s,uid)?g.agreedRates:undefined;
}

// FX merge-ui · Customers 360 "Possible duplicates": find pairs, compare, propose a merge, a second person decides, or dismiss a pair (training finding F18).
// Mirrors the server (Hcc.Application/Customers/CustomerCommands.cs, Hcc.Domain/Customers/CustomerRules.cs):
//   • duplicates   = customers with the same normalised e-mail, or a customer without a mobile number whose name equals that of exactly one customer who has one
//   • merge        = maker-checker (ProposeCustomerMerge, then DecideCustomerMerge by a DIFFERENT person); right = PERM-CRM-003 (Owner, Manager) for both steps
//   • on approval  = history is kept, the merged-away customer becomes an alias of the survivor: its stays (and old Lodge stays) show under the survivor,
//                    preferences follow the server rule (survivor's kept, a different text of the other becomes a note), notes are carried over.
//                    The credit account is NOT touched (it belongs to the guest profile / Udhaar party id, not to the customer view).
// Demo: the slice below is the state (aliases, merge requests, dismissed pairs). Production: the server is the truth — clients/sync/customer-merge.ts fills the
// same slice from GET /customers/duplicates and /customers/merges (`server` set); a dismissal has no server command, so it is demo-only.
import type {State} from '../types';
import {can} from '../selectors';
import {customerIndex,type Customer,type LegacyLookup} from './customers';
import {guestAccess,guestMaskPhone} from './guests';

export type CMXStatus='Pending'|'Approved'|'Rejected';
export type CMXMerge={id:string;source:string;target:string;sourceName:string;targetName:string;reason:string;status:CMXStatus;
  /** User id (demo: the staff id; production: the mapped server principal id). */proposedBy:string;proposedAt:string;
  decidedBy?:string;decidedAt?:string;note?:string;
  /** Production: row version for DecideCustomerMerge (kept in the id directory too). Demo: a counter. */version?:string;
  /** Demo, after approval: what the approval moved. */moved?:{stays:number;legacy:number;preferences:'kept'|'copied'|'noted'|'none';notes:boolean}};
export type CMXReason='email'|'name-without-phone';
/** One side of a pair, already masked for the reader. Production fills it from the server summary (the server masks); demo from Customers 360. */
export type CMXMember={key:string;name:string;phone:string;email:string;stays:number;nights:number;firstStay?:string;lastStay?:string;
  /** Paise; absent for roles that may not see money. */lifetime?:number;tags:string[];
  /** Every stay that moves (all statuses, plus old Lodge stays); falls back to `stays`. */allStays?:number;
  /** Absent = not loaded (production profile read failed or beyond the first pairs). */preferences?:string;notes?:string;
  /** Owner-only (DEC-058): the credit account line, absent otherwise. */credit?:string;inHouse?:boolean};
export type CMXPair={id:string;reason:CMXReason;a:CMXMember;b:CMXMember;/** Key of the member the system suggests to keep (phone first, more stays, earlier key). */suggested:string};
export type CMXSlice={
  /** Demo: merged-away customer key → survivor key (history kept). Empty in production (the server holds the aliases). */aliases:Record<string,string>;
  merges:CMXMerge[];
  /** Demo: pair ids dismissed with "Not the same person". */dismissed:string[];seq:number;
  /** Production only: what GET /customers/duplicates and /customers/merges returned for this reader. */
  server?:{canMerge:boolean;pairs:CMXPair[]}};

export const CMX_REASON_MIN=5,CMX_REASON_MAX=300;
export const cmxEmpty=():CMXSlice=>({aliases:{},merges:[],dismissed:[],seq:0});
export const cmxSlice=(s:State):CMXSlice=>s.customerMergeX??cmxEmpty();
export const cmxReasonText:Record<CMXReason,string>={'email':'Same e-mail address','name-without-phone':'Same name, one record has no mobile number'};
export const cmxPairId=(x:string,y:string)=>[x,y].sort().join('|');
/** Wording shared by the screens and the commands. */
export const CMX_SAME_TEXT='Choose two different customers.';
export const CMX_BUSY_TEXT='This customer already has a pending merge, or one of the two was merged already.';
export const CMX_MAKER_CHECKER_TEXT='A different person must decide this merge (maker-checker).';
export const CMX_LATE_TEXT='One of the two customers was merged in the meantime. Reject this proposal and propose again.';

/** Who may propose / decide / dismiss: PERM-CRM-003 on the server (Owner, Manager) — the prototype's 'approve' right is held by the same two roles. */
export const cmxMayMerge=(s:State,uid:string)=>s.customerMergeX?.server?s.customerMergeX.server.canMerge:can(s,uid,'approve');

// ── matching (server CustomerRules.NormEmail / NormName / FindDuplicates) ──
export function cmxNormEmail(email?:string):string{
  const e=String(email??'').trim().toLowerCase(),at=e.lastIndexOf('@');
  if(at<=0||at===e.length-1)return '';
  let local=e.slice(0,at),domain=e.slice(at+1);
  if(domain==='gmail.com'||domain==='googlemail.com'){const p=local.indexOf('+');if(p>=0)local=local.slice(0,p);local=local.replace(/\./g,'');domain='gmail.com';}
  return local+'@'+domain;
}
export const cmxNormName=(n:string)=>n.replace(/\(fictional\)/gi,' ').trim().normalize('NFKC').toLowerCase().replace(/[^\p{L}\p{N}\p{M} ]+/gu,' ').replace(/\s+/g,' ').trim();
const isNameKey=(k:string)=>k.startsWith('n:');
/** Server SuggestSurvivor: the one with a mobile number, then more stays, then the earlier key. */
export function cmxSuggest(a:Customer,b:Customer):Customer{
  const pa=!isNameKey(a.key),pb=!isNameKey(b.key);
  if(pa!==pb)return pa?a:b;
  if(a.stays.length!==b.stays.length)return a.stays.length>b.stays.length?a:b;
  return a.key<=b.key?a:b;
}
/** Names go into audit text; a name holding a phone number (8+ digits) has every digit run masked (server CustomerCommands.SafeName). */
export const cmxSafeName=(n:string)=>(n.match(/\d/g)??[]).length>=8?n.replace(/\d[\d\s\-().+]*\d|\d/g,'***'):n;

function member(c:Customer,r:{contact:boolean;money:boolean;owner:boolean}):CMXMember{
  const g=c.guest;
  const email=c.email?(r.contact?c.email:c.email[0]+'•••@'+(c.email.split('@')[1]||'')):'';
  return {key:c.key,name:c.name,phone:r.contact?c.phone||'—':guestMaskPhone(c.phone),email,stays:c.completed.length+c.legacyCount,allStays:c.stays.length+c.legacyStays.length,nights:c.nights,firstStay:c.firstStay,lastStay:c.lastStay,
    ...(r.money?{lifetime:c.lifetime}:{}),tags:[],preferences:g?.preferences?.trim()||'',notes:g?.notes?.trim()||'',
    ...(r.owner&&g?.creditAccount?{credit:(g.creditAccount.active?'Active':'Inactive')+' · limit ₹'+(g.creditAccount.limit/100).toLocaleString('en-IN')}:{}),inHouse:!!c.current};
}

/** Demo pairs from Customers 360 (after aliases): email groups, then name-without-phone; dismissed pairs and customers with a pending merge are left out. */
export function cmxPairs(s:State,uid:string,legacy?:LegacyLookup):CMXPair[]{
  const x=cmxSlice(s);
  const list=customerIndex(s,legacy),a=guestAccess(s,uid),r={contact:a.contact,money:can(s,uid,'billing'),owner:can(s,uid,'admin')};
  const busy=new Set(x.merges.filter(m=>m.status==='Pending').flatMap(m=>[m.source,m.target]));
  const out=new Map<string,CMXPair>();
  const add=(reason:CMXReason,group:Customer[])=>{
    const best=group.slice(1).reduce((p,c)=>cmxSuggest(p,c),group[0]);
    for(const c of group){
      if(c===best)continue;
      const id=cmxPairId(best.key,c.key);
      if(out.has(id)||x.dismissed.includes(id)||busy.has(best.key)||busy.has(c.key))continue;
      const [first,second]=[best,c].sort((p,q)=>p.key<q.key?-1:1);
      out.set(id,{id,reason,a:member(first,r),b:member(second,r),suggested:best.key});
    }
  };
  const byEmail=new Map<string,Customer[]>();
  for(const c of list){const e=cmxNormEmail(c.email);if(e)(byEmail.get(e)||byEmail.set(e,[]).get(e)!).push(c);}
  for(const g of byEmail.values())if(g.length>1)add('email',g.sort((p,q)=>p.key<q.key?-1:1));
  const phoned=new Map<string,Customer[]>();
  for(const c of list)if(!isNameKey(c.key)){const n=cmxNormName(c.name);(phoned.get(n)||phoned.set(n,[]).get(n)!).push(c);}
  for(const c of list)if(isNameKey(c.key)){const m=phoned.get(cmxNormName(c.name));if(m&&m.length===1)add('name-without-phone',[c,m[0]].sort((p,q)=>p.key<q.key?-1:1));}
  return [...out.values()].sort((p,q)=>p.id<q.id?-1:1);
}

export type CMXView={server:boolean;canMerge:boolean;pairs:CMXPair[];pending:CMXMerge[];recent:CMXMerge[]};
/** Everything the screens show. A role without the right gets nothing (no list, no action). */
export function cmxView(s:State,uid:string,legacy?:LegacyLookup):CMXView{
  const x=cmxSlice(s),canMerge=cmxMayMerge(s,uid);
  if(!canMerge)return {server:!!x.server,canMerge:false,pairs:[],pending:[],recent:[]};
  const newest=(p:CMXMerge,q:CMXMerge)=>q.proposedAt.localeCompare(p.proposedAt)||q.id.localeCompare(p.id);
  return {server:!!x.server,canMerge,pairs:x.server?x.server.pairs:cmxPairs(s,uid,legacy),
    pending:x.merges.filter(m=>m.status==='Pending').sort(newest),recent:x.merges.filter(m=>m.status!=='Pending').sort(newest).slice(0,10)};
}

/** Compare table rows: label, value of a, value of b; `differs` marks rows to highlight. */
export function cmxRows(p:CMXPair,showMoney:boolean):{label:string;a:string;b:string;differs:boolean}[]{
  const d=(l:string,a:string,b:string)=>({label:l,a:a||'—',b:b||'—',differs:a.trim().toLowerCase()!==b.trim().toLowerCase()});
  const rows=[d('Name',p.a.name,p.b.name),d('Mobile',p.a.phone,p.b.phone),d('E-mail',p.a.email,p.b.email),d('Stays',String(p.a.stays),String(p.b.stays)),d('Nights',String(p.a.nights),String(p.b.nights)),
    d('First stay',p.a.firstStay??'',p.b.firstStay??''),d('Last stay',p.a.lastStay??'',p.b.lastStay??'')];
  if(showMoney&&(p.a.lifetime!==undefined||p.b.lifetime!==undefined))rows.push(d('Lifetime spend','₹'+((p.a.lifetime??0)/100).toLocaleString('en-IN'),'₹'+((p.b.lifetime??0)/100).toLocaleString('en-IN')));
  rows.push(d('Preferences',p.a.preferences??'Not loaded',p.b.preferences??'Not loaded'),d('Notes',p.a.notes??'Not loaded',p.b.notes??'Not loaded'));
  if(p.a.tags.length||p.b.tags.length)rows.push(d('Tags',p.a.tags.join(', '),p.b.tags.join(', ')));
  if(p.a.credit||p.b.credit)rows.push(d('Credit account',p.a.credit??'None',p.b.credit??'None'));
  return rows;
}
/** The confirm text: what moves, what does not, and that the screen cannot undo it. `keep` is the survivor, `drop` the customer merged into it. */
const n=(m:CMXMember)=>m.allStays??m.stays;
export function cmxMoves(keep:CMXMember,drop:CMXMember):string[]{
  return [
    `Stays: all ${n(drop)} stay${n(drop)===1?'':'s'} (upcoming and cancelled too) of ${drop.name} (including earlier stays from the old Lodge app) now show under ${keep.name}.`,
    `Tags and notes of ${drop.name} move to ${keep.name}.`,
    `Preferences: ${keep.name}'s are kept. If ${drop.name} had different ones, they are saved as a note on ${keep.name}.`,
    `Credit account: not changed. It belongs to the guest profile and stays where it is.`,
    `${drop.name} is not deleted: history is kept and the old record points to ${keep.name}.`];
}
export const CMX_UNDO_TEXT='This cannot be undone from this screen. A second Owner or Manager must approve it first; nothing moves until then.';
/** Production only: the main Customers list is built on the device from bookings, so a merge done on the server shows there after a refresh from the server. */
export const CMX_LIST_NOTE='The Customers list on this device still shows both records until it is refreshed from the server.';
export const cmxApproveToast=(server:boolean,m:{sourceName:string;targetName:string})=>server?`${m.sourceName} merged into ${m.targetName} on the server. ${CMX_LIST_NOTE}`:`${m.sourceName} merged into ${m.targetName}`;
export const CMX_APPROVE_UNDO_TEXT='This cannot be undone from this screen.';
